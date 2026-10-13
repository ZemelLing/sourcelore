using System.Collections.Concurrent;
using LoreVcs.Types.Args;
using LoreVcs.Types.Enums;
using LoreVcs.Types.Events;
using SourceLore.Core.Workspaces;
using Sdk = LoreVcs.Lore;

namespace SourceLore.Infrastructure.Lore;

public sealed partial class LoreWorkspaceBackend : IRevisionDetailBackend
{
    public Task<IReadOnlyList<RevisionFileChange>> ChangedFilesAsync(WorkspaceIdentity workspace, string revision, string parent) => Run<IReadOnlyList<RevisionFileChange>>(workspace, async () =>
    {
        using var globals = Globals(workspace.RootPath);
        var files = new ConcurrentQueue<RevisionFileChange>(); var failed = false; var overflow = false;
        void Add(RevisionFileChange change)
        {
            if (files.Count >= 10_000) { overflow = true; return; }
            files.Enqueue(change);
        }
        if (!RevisionHistoryPager.IsRevision(parent))
        {
            using var args = new LoreRevisionInfoArgs { Revision = revision, Delta = true };
            await Sdk.RevisionInfo(globals, args).Callback((evt, _) =>
            {
                if (evt.Tag == LoreEventTag.ERROR) failed = true;
                if (evt.Tag == LoreEventTag.REVISION_INFO_DELTA)
                {
                    var data = evt.GetData<LoreRevisionInfoDeltaEventDataFFI>();
                    Add(new(data.Path, data.Action.ToString(), data.FlagFile, data.FromPath));
                }
            }).WaitAsync().ConfigureAwait(false);
        }
        else
        {
            using var args = new LoreRevisionDiffArgs { RevisionSource = parent, RevisionTarget = revision };
            await Sdk.RevisionDiff(globals, args).Callback((evt, _) =>
            {
                if (evt.Tag == LoreEventTag.ERROR) failed = true;
                if (evt.Tag == LoreEventTag.REVISION_DIFF_FILE)
                {
                    var data = evt.GetData<LoreRevisionDiffFileEventDataFFI>();
                    Add(new(data.Path, data.Action.ToString(), data.OldIsFile || data.NewIsFile, data.FromPath));
                }
            }).WaitAsync().ConfigureAwait(false);
        }
        if (failed) throw new InvalidOperationException("历史文件列表读取失败，不能视为没有变更。");
        if (overflow) throw new InvalidOperationException("历史比较超过 10,000 个文件的列表上限，请缩小比较范围或输入单一路径。");
        return files.DistinctBy(file => file.Path, StringComparer.OrdinalIgnoreCase).OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    });
}
