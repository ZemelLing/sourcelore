using LoreVcs.Types.Args;
using LoreVcs.Types.Enums;
using LoreVcs.Types.Events;
using SourceLore.Core.Workspaces;
using SourceLore.Infrastructure.Lore.Diagnostics;
using Sdk = LoreVcs.Lore;

namespace SourceLore.Infrastructure.Lore;

public sealed partial class LoreWorkspaceBackend
{
    private static async Task RequireTrackedWorkingFilesAsync(WorkspaceIdentity workspace, string revision)
    {
        // The remote can advance after the incoming-path check. Refuse all untracked
        // physical files, including ignored ones, so a later target cannot overwrite them.
        var folders = new Stack<string>(); folders.Push(workspace.RootPath);
        var paths = new List<string>(); var walked = 0;
        while (folders.TryPop(out var folder))
        {
            foreach (var item in new DirectoryInfo(folder).EnumerateFileSystemInfos())
            {
                if (folder == workspace.RootPath && (item.Name.Equals(".lore", StringComparison.OrdinalIgnoreCase) || item.Name.Equals(".urc", StringComparison.OrdinalIgnoreCase))) continue;
                if (++walked > 10_000) throw new InvalidOperationException("同步前物理文件核对超过 10,000 项上限；尚未执行同步。");
                if (item.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("工作目录包含链接或重解析点，无法安全核对同步：" + item.Name);
                if (item is DirectoryInfo) folders.Push(item.FullName);
                else paths.Add(Path.GetRelativePath(workspace.RootPath, item.FullName));
            }
        }
        using var globals = Globals(workspace.RootPath);
        foreach (var batch in paths.Chunk(100))
        {
            var tracked = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var failed = false;
            using var info = new LoreFileInfoArgs { Paths = batch, Revision = revision };
            await Sdk.FileInfo(globals, info).Callback((evt, _) =>
            {
                if (evt.Tag == LoreEventTag.ERROR) failed = true;
                if (evt.Tag == LoreEventTag.FILE_INFO) { var data = evt.GetData<LoreFileInfoEventDataFFI>(); if (data.IsFile && !data.FlagAdded) tracked.Add(data.Path); }
            }).WaitAsync().ConfigureAwait(false);
            var missing = batch.FirstOrDefault(path => !tracked.Contains(path));
            if (failed || missing is not null) throw new InvalidOperationException("同步前发现未跟踪或忽略的物理文件；请先移出工作目录或明确纳入版本，客户端不会删除它：" + missing);
        }
    }
    private async Task CheckIncomingPathsAsync(WorkspaceIdentity workspace, string source, string target)
    {
        using var online = OnlineGlobals(workspace.RootPath);
        using var diff = new LoreRevisionDiffArgs { RevisionSource = source, RevisionTarget = target };
        var changes = new List<(string Path, bool NewFile, string Action)>(); var failed = false; var overflow = false;
        await Sdk.RevisionDiff(online, diff).Callback((evt, _) =>
        {
            if (evt.Tag == LoreEventTag.ERROR) failed = true;
            if (evt.Tag == LoreEventTag.REVISION_DIFF_FILE)
            {
                var data = evt.GetData<LoreRevisionDiffFileEventDataFFI>();
                if (changes.Count >= 10_000) overflow = true;
                else changes.Add((data.Path, data.NewIsFile, data.Action.ToString()));
            }
        }).WaitAsync().ConfigureAwait(false);
        if (failed || overflow) throw new InvalidOperationException("不能完整核对传入文件覆盖风险；同步未开始。最多核对 10,000 项变更。");
        var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace.RootPath)) + Path.DirectorySeparatorChar;
        var walked = 0;
        foreach (var change in changes)
        {
            if (change.Path is "" or ".") continue;
            var full = TextPreview.WorkingPath(workspace.RootPath, change.Path);
            for (var ancestor = full; ancestor is not null && ancestor.StartsWith(prefix, StringComparison.OrdinalIgnoreCase); ancestor = Path.GetDirectoryName(ancestor))
            {
                if ((File.Exists(ancestor) || Directory.Exists(ancestor)) && File.GetAttributes(ancestor).HasFlag(FileAttributes.ReparsePoint))
                    throw new InvalidOperationException("传入路径涉及链接或重解析点，不能安全同步；当前内容保留：" + change.Path);
                if (File.Exists(ancestor)) existingFiles.Add(Path.GetRelativePath(workspace.RootPath, ancestor));
            }
            if (Directory.Exists(full) && (change.NewFile || change.Action == "DELETE"))
            {
                var pendingFolders = new Stack<string>(); pendingFolders.Push(full);
                while (pendingFolders.TryPop(out var folder))
                {
                    foreach (var item in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                    {
                        if (++walked > 10_000) throw new InvalidOperationException("目录覆盖核对超过 10,000 项上限，拒绝同步。");
                        if (item.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("目录覆盖涉及链接，拒绝同步：" + change.Path);
                        if (item is DirectoryInfo) pendingFolders.Push(item.FullName);
                        else existingFiles.Add(Path.GetRelativePath(workspace.RootPath, item.FullName));
                        if (pendingFolders.Count + existingFiles.Count > 10_000) throw new InvalidOperationException("目录覆盖风险超过 10,000 项核对上限，拒绝同步。");
                    }
                }
            }
        }
        using var globals = Globals(workspace.RootPath);
        foreach (var path in existingFiles)
        {
            var tracked = false; failed = false;
            using var info = new LoreFileInfoArgs { Paths = [path], Revision = source };
            await Sdk.FileInfo(globals, info).Callback((evt, _) =>
            {
                if (evt.Tag == LoreEventTag.ERROR) failed = true;
                if (evt.Tag == LoreEventTag.FILE_INFO) { var data = evt.GetData<LoreFileInfoEventDataFFI>(); tracked = data.IsFile && !data.FlagAdded; }
            }).WaitAsync().ConfigureAwait(false);
            if (failed || !tracked) throw new InvalidOperationException("传入版本可能覆盖未跟踪或忽略的文件，同步已拒绝，文件保留：" + path);
        }
        var final = await ReadStatusAsync(workspace, true).ConfigureAwait(false);
        if (final.Revision != source || final.Files.Count > 0) throw new InvalidOperationException("核对期间本地工作已变化，同步未开始，请处理修改后重新确认。");
    }

    public Task<WorkspaceSnapshot> ResolveAsync(WorkspaceIdentity workspace, string path) => Run(workspace, async () =>
    {
        _ = TextPreview.WorkingPath(workspace.RootPath, path);
        var before = await ReadStatusAsync(workspace, true).ConfigureAwait(false);
        if (!before.HasMerge || !before.Files.Any(file => file.Path == path && file.Conflict && !file.IsDirectory))
            throw new InvalidOperationException("所选文件不是当前合并中的冲突文件，请重新核对。");
        using var globals = Globals(workspace.RootPath);
        using var args = new LoreBranchMergeResolveArgs { Paths = [path] };
        var failed = false;
        await Sdk.BranchMergeResolve(globals, args).Callback((evt, _) => { if (evt.Tag == LoreEventTag.ERROR) failed = true; }).WaitAsync().ConfigureAwait(false);
        var actual = await ReadStatusAsync(workspace, true).ConfigureAwait(false);
        if (failed || actual.Files.Any(file => file.Path == path && file.Unresolved)) throw new InvalidOperationException("后端尚未确认冲突已解决，请保留内容并继续核对。");
        return actual;
    });
    public Task<WorkspaceSnapshot> ContinueMergeAsync(WorkspaceIdentity workspace, string message) => Run(workspace, async () =>
    {
        var before = await ReadStatusAsync(workspace, true).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(message)) throw new InvalidOperationException("请填写合并提交说明。");
        if (!before.HasMerge || before.HasUnresolvedConflicts) throw new InvalidOperationException("合并尚未开始或仍有未解决冲突，不能继续。");
        using var globals = Globals(workspace.RootPath);
        using var args = new LoreRevisionCommitArgs { Message = message };
        var failed = false;
        await Sdk.RevisionCommit(globals, args).Callback((evt, _) => { if (evt.Tag == LoreEventTag.ERROR) failed = true; }).WaitAsync().ConfigureAwait(false);
        var actual = await ReadStatusAsync(workspace, true).ConfigureAwait(false);
        if (failed || actual.HasConflicts || actual.HasMerge) throw new InvalidOperationException("合并提交未确认完成；请检查当前版本，不自动重放。");
        return actual;
    });
    public Task<AbortResult> AbortMergeAsync(WorkspaceIdentity workspace, string expectedRevision, string expectedMergedRevision) => Run(workspace, async () =>
    {
        var before = await ReadStatusAsync(workspace, true).ConfigureAwait(false);
        if (before.Revision != expectedRevision || before.MergedRevision != expectedMergedRevision || !before.HasMerge)
            throw new InvalidOperationException("待中止的合并状态已变化，请重新阅读影响并确认。");
        var backup = Path.Combine(Path.GetTempPath(), "SourceLore.AbortBackups", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        try
        {
            var manifest = new List<string>(); long total = 0; var index = 0;
            foreach (var file in before.Files.Where(file => !file.IsDirectory))
            {
                if (++index > 10_000) throw new InvalidOperationException("中止备份超过 10,000 个文件上限。");
                var full = TextPreview.WorkingPath(workspace.RootPath, file.Path);
                if (!File.Exists(full)) { manifest.Add("不存在\t" + file.Path); continue; }
                var bytes = await TextPreview.ReadBytesAsync(full).ConfigureAwait(false);
                total += bytes.Length;
                if (total > 128 * 1024 * 1024) throw new InvalidOperationException("中止备份超过 128 MiB 总上限。");
                var copy = index + ".bin";
                await File.WriteAllBytesAsync(Path.Combine(backup, copy), bytes).ConfigureAwait(false);
                manifest.Add(copy + "\t" + file.Path);
            }
            await File.WriteAllLinesAsync(Path.Combine(backup, "manifest.txt"), manifest).ConfigureAwait(false);
            using var globals = Globals(workspace.RootPath);
            using var args = new LoreBranchMergeAbortArgs();
            var failed = false;
            await Sdk.BranchMergeAbort(globals, args).Callback((evt, _) => { if (evt.Tag == LoreEventTag.ERROR) failed = true; }).WaitAsync().ConfigureAwait(false);
            var actual = await ReadStatusAsync(workspace, true).ConfigureAwait(false);
            if (failed || actual.Revision != expectedRevision || actual.HasConflicts || actual.HasMerge)
                throw new InvalidOperationException("中止未确认完成，原有本地版本与冲突状态需核对。");
            return new AbortResult(actual, backup);
        }
        catch (Exception error) { throw new InvalidOperationException("中止未确认完成；已生成的文件备份保留在 " + backup + "。" + SafeDiagnostics.Describe(error), error); }
    });
}
