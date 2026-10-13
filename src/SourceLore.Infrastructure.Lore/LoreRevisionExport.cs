using LoreVcs.Types.Args;
using LoreVcs.Types.Enums;
using LoreVcs.Types.Events;
using SourceLore.Core.Workspaces;
using Sdk = LoreVcs.Lore;

namespace SourceLore.Infrastructure.Lore;

public sealed partial class LoreWorkspaceBackend : IRevisionExportBackend
{
    public Task<RevisionExport> ExportRevisionAsync(WorkspaceIdentity workspace, string path, string revision) => Run(workspace, async () =>
    {
        _ = TextPreview.WorkingPath(workspace.RootPath, path);
        if (!RevisionHistoryPager.IsRevision(revision)) throw new InvalidOperationException("此侧没有版本文件可导出。");
        using var globals = Globals(workspace.RootPath);
        using var info = new LoreFileInfoArgs { Paths = [path], Revision = revision };
        var found = false; var failed = false; ulong size = 0;
        await Sdk.FileInfo(globals, info).Callback((evt, _) =>
        {
            if (evt.Tag == LoreEventTag.ERROR) failed = true;
            if (evt.Tag == LoreEventTag.FILE_INFO) { var data = evt.GetData<LoreFileInfoEventDataFFI>(); found = data.IsFile && !data.FlagAdded; size = data.Size; }
        }).WaitAsync().ConfigureAwait(false);
        if (failed || !found) throw new InvalidOperationException("此版本的文件不存在或内容不可用。");
        if (size > 512UL * 1024 * 1024) throw new InvalidOperationException("版本文件超过 512 MiB 外部导出上限；首版不提供大资产导出。");
        var folder = Path.Combine(System.IO.Path.GetTempPath(), "SourceLore.ExternalSnapshots", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var output = Path.Combine(folder, "snapshot.txt");
        void Release()
        {
            if (File.Exists(output)) { File.SetAttributes(output, FileAttributes.Normal); File.Delete(output); }
            if (Directory.Exists(folder)) Directory.Delete(folder);
        }
        try
        {
            using var args = new LoreFileWriteArgs { Path = path, Revision = revision, Output = output };
            failed = false;
            await Sdk.FileWrite(globals, args).Callback((evt, _) => { if (evt.Tag == LoreEventTag.ERROR) failed = true; }).WaitAsync().ConfigureAwait(false);
            if (failed || !File.Exists(output)) throw new InvalidOperationException("版本导出失败，未打开外部工具。");
            File.SetAttributes(output, FileAttributes.ReadOnly);
            return new RevisionExport(output, Release);
        }
        catch { Release(); throw; }
    });
}
