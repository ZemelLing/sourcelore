using LoreVcs.Types.Args;
using LoreVcs.Types.Enums;
using LoreVcs.Types.Events;
using SourceLore.Core.Workspaces;
using Sdk = LoreVcs.Lore;

namespace SourceLore.Infrastructure.Lore;

public sealed partial class LoreWorkspaceBackend : IHistoryContentBackend
{
    public Task<TextComparison> CompareAsync(WorkspaceIdentity workspace, string path, string sourceRevision, string? targetRevision) => CompareCoreAsync(workspace, path, sourceRevision, targetRevision, false);
    public Task<TextComparison> CompareOnlineAsync(WorkspaceIdentity workspace, string path, string sourceRevision, string? targetRevision) => CompareCoreAsync(workspace, path, sourceRevision, targetRevision, true);
    private Task<TextComparison> CompareCoreAsync(WorkspaceIdentity workspace, string path, string sourceRevision, string? targetRevision, bool online) => Run(workspace, async () =>
    {
        var working = TextPreview.WorkingPath(workspace.RootPath, path);
        var rightExists = targetRevision is null && File.Exists(working);
        var workingBytes = rightExists ? await TextPreview.ReadBytesAsync(working).ConfigureAwait(false) : [];
        var right = targetRevision is null ? new ComparisonSource("工作文件（本次读取）", rightExists, TextContent.Decode(workingBytes))
            : await ReadRevisionTextAsync(workspace, path, targetRevision, online).ConfigureAwait(false);
        var left = await ReadRevisionTextAsync(workspace, path, sourceRevision, online).ConfigureAwait(false);
        var comparison = TextComparison.Build(path, left, right);
        if (targetRevision is null)
        {
            var current = rightExists && File.Exists(working) ? await TextPreview.ReadBytesAsync(working).ConfigureAwait(false) : [];
            if (File.Exists(working) != rightExists || rightExists && !workingBytes.AsSpan().SequenceEqual(current))
                throw new InvalidOperationException("读取期间工作文件已变化，差异已过期，请重新选择或扫描。");
        }
        return comparison;
    });

    private static async Task<ComparisonSource> ReadRevisionTextAsync(WorkspaceIdentity workspace, string path, string revision, bool online)
    {
        if (!RevisionHistoryPager.IsRevision(revision)) return new("无父版本", false, TextContent.Decode([]));
        using var globals = new LoreGlobalArgs { RepositoryPath = workspace.RootPath, WorkingDirectory = workspace.RootPath, Offline = !online, Local = !online, Cache = online, EventIntervalMs = 100 };
        using var info = new LoreFileInfoArgs { Paths = [path], Revision = revision };
        var found = false; var failed = false; ulong size = 0;
        await Sdk.FileInfo(globals, info).Callback((evt, _) =>
        {
            if (evt.Tag == LoreEventTag.ERROR) failed = true;
            if (evt.Tag == LoreEventTag.FILE_INFO)
            {
                var data = evt.GetData<LoreFileInfoEventDataFFI>();
                // 0.10 also reports working-only additions even for a revision query.
                found = data.IsFile && !data.FlagAdded;
                size = data.Size;
            }
        }).WaitAsync().ConfigureAwait(false);
        if (failed) throw new InvalidOperationException("版本文件信息不可用；请确认版本内容已缓存，不能将读取失败视为空文件。");
        if (!found) return new(revision, false, TextContent.Decode([]));
        if (size > TextContent.MaxBytes) throw new InvalidDataException("版本内容超过 8 MiB 预览上限，请使用外部工具查看。");
        var folder = Path.Combine(Path.GetTempPath(), "SourceLore.TextExports", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var output = Path.Combine(folder, "content.txt");
        try
        {
            using var args = new LoreFileWriteArgs { Path = path, Revision = revision, Output = output };
            failed = false;
            await Sdk.FileWrite(globals, args).Callback((evt, _) => { if (evt.Tag == LoreEventTag.ERROR) failed = true; }).WaitAsync().ConfigureAwait(false);
            if (failed || !File.Exists(output)) throw new InvalidOperationException("版本内容不可用；不能将导出失败视为空文件。");
            return new(revision, true, TextContent.Decode(await TextPreview.ReadBytesAsync(output).ConfigureAwait(false)));
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
            Directory.Delete(folder);
        }
    }
}
