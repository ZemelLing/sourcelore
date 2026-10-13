using SourceLore.Infrastructure.Lore;
using LoreVcs.Types.Args;

namespace SourceLore.Lore.IntegrationTests;

public sealed class NativeRuntimeFixture : IDisposable
{
    public LoreRuntime Runtime { get; } = new();
    public void Dispose() => Runtime.Dispose();
}

public class NativeRuntimeTests(NativeRuntimeFixture fixture) : IClassFixture<NativeRuntimeFixture>
{
    [Fact]
    public async Task SynchronizationRejectsIgnoredPhysicalFilesAndPreservesUnpushedRevision()
    {
        await using var server = new ControlledServer();
        await server.StartAsync();
        var source = System.IO.Path.Combine(server.Root, "risk A");
        var target = System.IO.Path.Combine(server.Root, "risk B");
        Directory.CreateDirectory(source);
        using var globals = new LoreGlobalArgs { RepositoryPath = source, WorkingDirectory = source };
        using var create = new LoreRepositoryCreateArgs { RepositoryUrl = server.Url + "/risk" };
        await global::LoreVcs.Lore.RepositoryCreate(globals, create).WaitAsync();
        var backend = new LoreWorkspaceBackend(fixture.Runtime);
        var a = await backend.OpenAsync(source);
        await File.WriteAllTextAsync(System.IO.Path.Combine(source, "code.txt"), "初始\n", TestContext.Current.CancellationToken);
        await backend.StageAsync(a, ["code.txt"], true); await backend.CommitAsync(a, "初始");
        await backend.PushAsync(a, await backend.ReadTargetAsync(a));
        var b = await backend.CloneAsync(server.Url + "/risk", target);
        try
        {
            await File.WriteAllTextAsync(System.IO.Path.Combine(target, ".loreignore"), "blocked.txt\n", TestContext.Current.CancellationToken);
            await backend.StageAsync(b, [".loreignore"], true); await backend.CommitAsync(b, "本地未推送忽略配置");
            await File.WriteAllTextAsync(System.IO.Path.Combine(target, "blocked.txt"), "不可丢弃的本地缓存\n", TestContext.Current.CancellationToken);
            var before = await backend.StatusAsync(b, true);
            Assert.Empty(before.Files);
            await File.WriteAllTextAsync(System.IO.Path.Combine(source, "blocked.txt"), "远端新文件\n", TestContext.Current.CancellationToken);
            await backend.StageAsync(a, ["blocked.txt"], true); await backend.CommitAsync(a, "远端新增");
            await backend.PushAsync(a, await backend.ReadTargetAsync(a));
            var anchor = await backend.VerifyRemoteAsync(b);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => backend.SyncAsync(b, anchor));
            Assert.Contains("未跟踪或忽略", error.Message);
            Assert.Equal(before.Revision, (await backend.StatusAsync(b, true)).Revision);
            Assert.Equal("不可丢弃的本地缓存\n", await File.ReadAllTextAsync(System.IO.Path.Combine(target, "blocked.txt"), TestContext.Current.CancellationToken));
            Assert.Equal("本地未推送忽略配置", (await backend.RevisionAsync(b, before.Revision)).Message);
        }
        finally
        {
            using var release = new LoreRepositoryReleaseArgs();
            await global::LoreVcs.Lore.RepositoryRelease(globals, release).WaitAsync();
            using var targetGlobals = new LoreGlobalArgs { RepositoryPath = target, Offline = true };
            await global::LoreVcs.Lore.RepositoryRelease(targetGlobals, release).WaitAsync();
        }
    }

    [Fact]
    public async Task ComparisonsReadBothVersionsStrictlyAndNeverRewriteWorkingBytes()
    {
        await using var repository = new LoreFixture(fixture.Runtime);
        await repository.InitializeAsync();
        var samples = new Dictionary<string, byte[]>
        {
            ["utf8.txt"] = System.Text.Encoding.UTF8.GetBytes("原文中文\r\n末行"),
            ["utf16le.txt"] = new byte[] { 0xff, 0xfe }.Concat(System.Text.Encoding.Unicode.GetBytes("中文\r\n")).ToArray(),
            ["utf16be.txt"] = new byte[] { 0xfe, 0xff }.Concat(System.Text.Encoding.BigEndianUnicode.GetBytes("中文\n")).ToArray(),
            ["empty.txt"] = [], ["deleted.txt"] = System.Text.Encoding.UTF8.GetBytes("删除\n"),
            ["binary.txt"] = [1, 0, 2], ["unknown.txt"] = [0xff],
            ["large.txt"] = new byte[SourceLore.Core.Workspaces.TextContent.MaxBytes + 1]
        };
        foreach (var sample in samples) await File.WriteAllBytesAsync(System.IO.Path.Combine(repository.Path, sample.Key), sample.Value, TestContext.Current.CancellationToken);
        await repository.Backend.StageAsync(repository.Workspace, samples.Keys.ToArray(), true);
        await repository.Backend.CommitAsync(repository.Workspace, "编码和大小基线");
        var revision = (await repository.Backend.StatusAsync(repository.Workspace, true)).Revision;
        var initialFiles = await repository.Backend.ChangedFilesAsync(repository.Workspace, revision, "");
        Assert.Contains(initialFiles, file => file.Path == "utf8.txt" && file.IsFile);
        var initialDiff = await repository.Backend.CompareAsync(repository.Workspace, "utf8.txt", "", revision);
        Assert.False(initialDiff.Left.Exists);
        Assert.Equal("原文中文\r\n末行", initialDiff.Right.Content.Text);
        File.Delete(System.IO.Path.Combine(repository.Path, "deleted.txt"));
        await repository.WriteAsync("new.txt", "新增\n");
        await repository.WriteAsync("utf8.txt", "原文中文\n末行\n");
        await File.WriteAllBytesAsync(System.IO.Path.Combine(repository.Path, "utf16le.txt"), new byte[] { 0xff, 0xfe }.Concat(System.Text.Encoding.Unicode.GetBytes("修改中文\n末行")).ToArray(), TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(System.IO.Path.Combine(repository.Path, "utf16be.txt"), new byte[] { 0xfe, 0xff }.Concat(System.Text.Encoding.BigEndianUnicode.GetBytes("修改中文\r\n")).ToArray(), TestContext.Current.CancellationToken);
        foreach (var path in new[] { "utf8.txt", "utf16le.txt", "utf16be.txt", "empty.txt", "deleted.txt", "new.txt" })
        {
            var full = System.IO.Path.Combine(repository.Path, path);
            var before = File.Exists(full) ? await File.ReadAllBytesAsync(full, TestContext.Current.CancellationToken) : null;
            var diff = await repository.Backend.CompareAsync(repository.Workspace, path, revision, null);
            Assert.Equal(before is not null, diff.Right.Exists);
            Assert.Equal(samples.ContainsKey(path), diff.Left.Exists);
            Assert.Equal(diff.Left.Content.Text, string.Concat(diff.Rows.Where(row => row.Left is not null).Select(row => row.Left!.Exact)));
            Assert.Equal(diff.Right.Content.Text, string.Concat(diff.Rows.Where(row => row.Right is not null).Select(row => row.Right!.Exact)));
            if (before is not null) Assert.Equal(before, await File.ReadAllBytesAsync(full, TestContext.Current.CancellationToken));
        }
        foreach (var path in new[] { "binary.txt", "unknown.txt", "large.txt" })
        {
            await repository.WriteAsync(path, "合法工作内容\n");
            await Assert.ThrowsAsync<InvalidDataException>(() => repository.Backend.CompareAsync(repository.Workspace, path, revision, null));
            Assert.Equal("合法工作内容\n", await File.ReadAllTextAsync(System.IO.Path.Combine(repository.Path, path), TestContext.Current.CancellationToken));
        }
        using (var exported = await repository.Backend.ExportRevisionAsync(repository.Workspace, "large.txt", revision))
        {
            Assert.Equal(SourceLore.Core.Workspaces.TextContent.MaxBytes + 1, new FileInfo(exported.Path).Length);
            Assert.True(File.GetAttributes(exported.Path).HasFlag(FileAttributes.ReadOnly));
            var exportedPath = exported.Path;
            exported.Dispose();
            Assert.False(File.Exists(exportedPath));
            Assert.Equal("合法工作内容\n", await File.ReadAllTextAsync(System.IO.Path.Combine(repository.Path, "large.txt"), TestContext.Current.CancellationToken));
        }
        await repository.Backend.StageAsync(repository.Workspace, ["utf8.txt"], true);
        await repository.Backend.CommitAsync(repository.Workspace, "换行变化版本");
        var newer = (await repository.Backend.StatusAsync(repository.Workspace, true)).Revision;
        var historyFiles = await repository.Backend.ChangedFilesAsync(repository.Workspace, newer, revision);
        Assert.Contains(historyFiles, file => file.Path == "utf8.txt");
        var historical = await repository.Backend.CompareAsync(repository.Workspace, "utf8.txt", revision, newer);
        Assert.Equal("原文中文\r\n末行", historical.Left.Content.Text);
        Assert.Equal("原文中文\n末行\n", historical.Right.Content.Text);
        Assert.All(historical.Rows, row => Assert.NotEqual(SourceLore.Core.Workspaces.DiffKind.Context, row.Kind));
        await File.WriteAllBytesAsync(System.IO.Path.Combine(repository.Path, "utf8.txt"), new byte[SourceLore.Core.Workspaces.TextContent.MaxBytes + 1], TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.Backend.CompareAsync(repository.Workspace, "utf8.txt", revision, null));
    }

    [Fact]
    public void PinnedLibraryLoadsAndReportsVersion()
    {
        Assert.StartsWith(LoreRuntime.PackageVersion, fixture.Runtime.Version);
    }

    [Fact]
    public async Task WorkingCopyUsesNativeStagingIntentAndPreservesUnselectedEdits()
    {
        await using var repository = new LoreFixture(fixture.Runtime);
        await repository.InitializeAsync();
        await repository.WriteAsync("程序.txt", "第一版\n");
        await repository.WriteAsync("保留.txt", "保留原文\n");
        await repository.WriteAsync("删除.txt", "将删除的内容\n");
        await repository.Backend.StageAsync(repository.Workspace, ["程序.txt", "保留.txt", "删除.txt"], true);
        await repository.Backend.CommitAsync(repository.Workspace, "初始版本");
        Assert.Empty((await repository.Backend.StatusAsync(repository.Workspace, true)).Files);

        await repository.WriteAsync("程序.txt", "暂存内容\n");
        await repository.WriteAsync("保留.txt", "未选的修改\n");
        await repository.WriteAsync("新 文件.txt", "新增\n");
        File.Delete(System.IO.Path.Combine(repository.Path, "删除.txt"));
        var status = await repository.Backend.StatusAsync(repository.Workspace, true);
        Assert.Contains(status.Files, file => file.Path == "程序.txt" && file.Dirty);
        Assert.Contains(status.Files, file => file.Path == "新 文件.txt");
        Assert.Contains(status.Files, file => file.Path == "删除.txt" && file.Action == "DELETE");
        var patch = await repository.Backend.DiffAsync(repository.Workspace, "程序.txt");
        Assert.Contains("第一版", patch);
        Assert.Contains("暂存内容", patch);
        await repository.Backend.StageAsync(repository.Workspace, ["程序.txt"], true);
        await repository.WriteAsync("程序.txt", "暂存后继续编辑\n");
        await repository.Backend.CommitAsync(repository.Workspace, "只提交选择的文件");
        status = await repository.Backend.StatusAsync(repository.Workspace, true);
        Assert.DoesNotContain(status.Files, file => file.Path == "程序.txt");
        Assert.Contains(status.Files, file => file.Path == "保留.txt" && !file.Staged);
        Assert.Equal("暂存后继续编辑\n", await File.ReadAllTextAsync(System.IO.Path.Combine(repository.Path, "程序.txt"), TestContext.Current.CancellationToken));

        await repository.Backend.StageAsync(repository.Workspace, ["保留.txt"], true);
        Assert.Contains((await repository.Backend.StatusAsync(repository.Workspace, false)).Files, file => file.Path == "保留.txt" && file.Staged);
        await repository.Backend.StageAsync(repository.Workspace, ["保留.txt"], false);
        Assert.Contains((await repository.Backend.StatusAsync(repository.Workspace, false)).Files, file => file.Path == "保留.txt" && !file.Staged);
        Assert.Equal("未选的修改\n", await File.ReadAllTextAsync(System.IO.Path.Combine(repository.Path, "保留.txt"), TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<Exception>(() => repository.Backend.CommitAsync(repository.Workspace, "空暂存应失败"));

        Directory.CreateDirectory(System.IO.Path.Combine(repository.Path, "子 目录"));
        var nested = await repository.Backend.OpenAsync(System.IO.Path.Combine(repository.Path, "子 目录"));
        Assert.Equal(repository.Workspace.InstanceId, nested.InstanceId);
        Assert.Equal(repository.Workspace.RootPath, nested.RootPath);
    }

    [Fact]
    public async Task ControlledServerCanPushCloneAndSyncWithoutCli()
    {
        await using var server = new ControlledServer();
        await server.StartAsync();
        var anonymousLogin = await Assert.ThrowsAsync<LoreVcs.LoreError>(() => new LoreWorkspaceBackend(fixture.Runtime).LoginAsync(server.Url, new Progress<SourceLore.Core.Workspaces.AuthenticationLink>()));
        Assert.Contains("No authentication configured", anonymousLogin.Message, StringComparison.OrdinalIgnoreCase);
        var source = System.IO.Path.Combine(server.Root, "A");
        var target = System.IO.Path.Combine(server.Root, "B");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        using var globals = new LoreGlobalArgs { RepositoryPath = source, WorkingDirectory = source };
        using var create = new LoreRepositoryCreateArgs { RepositoryUrl = server.Url + "/code" };
        await global::LoreVcs.Lore.RepositoryCreate(globals, create).WaitAsync();
        var backend = new LoreWorkspaceBackend(fixture.Runtime);
        var first = await backend.OpenAsync(source);
        await File.WriteAllTextAsync(System.IO.Path.Combine(source, "程序.txt"), "初始版本\n", TestContext.Current.CancellationToken);
        await backend.StageAsync(first, ["程序.txt"], true);
        await backend.CommitAsync(first, "服务器初始版本");
        using var push = new LoreBranchPushArgs { FastForwardMerge = false };
        Assert.Equal(server.Url, (await backend.ReadTargetAsync(first)).Address);
        Assert.Contains((await backend.ReadTargetAsync(first)).RepositoryId, character => character != '0');
        await backend.PushAsync(first, await backend.ReadTargetAsync(first));
        using var cloneGlobals = new LoreGlobalArgs { RepositoryPath = target, WorkingDirectory = target };
        var second = await backend.CloneAsync(server.Url + "/code", target);
        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.CloneAsync(server.Url + "/code", source));
        var failedTarget = System.IO.Path.Combine(server.Root, "失败 克隆");
        var cloneFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => backend.CloneAsync(server.Url + "/missing-repository", failedTarget));
        Assert.Contains(failedTarget, cloneFailure.Message);
        Assert.True(Directory.Exists(failedTarget));
        Assert.Equal("初始版本\n", await File.ReadAllTextAsync(System.IO.Path.Combine(source, "程序.txt"), TestContext.Current.CancellationToken));
        Assert.NotEqual(first.InstanceId, second.InstanceId);
        Assert.Equal("初始版本\n", await File.ReadAllTextAsync(System.IO.Path.Combine(target, "程序.txt"), TestContext.Current.CancellationToken));
        await File.WriteAllTextAsync(System.IO.Path.Combine(source, "程序.txt"), "远端修改\n", TestContext.Current.CancellationToken);
        await backend.StageAsync(first, ["程序.txt"], true);
        await backend.CommitAsync(first, "远端新版本");
        await backend.PushAsync(first, await backend.ReadTargetAsync(first));
        using var sync = new LoreRevisionSyncArgs { Reset = false, ForwardChanges = false };
        await backend.SyncAsync(second, await backend.VerifyRemoteAsync(second));
        Assert.Equal("远端修改\n", await File.ReadAllTextAsync(System.IO.Path.Combine(target, "程序.txt"), TestContext.Current.CancellationToken));
        Assert.Equal((await backend.StatusAsync(first, true)).Revision, (await backend.StatusAsync(second, true)).Revision);
        await File.WriteAllTextAsync(System.IO.Path.Combine(source, "程序.txt"), "A 分歧\n", TestContext.Current.CancellationToken);
        await backend.StageAsync(first, ["程序.txt"], true);
        await backend.CommitAsync(first, "A 分歧版本");
        await backend.PushAsync(first, await backend.ReadTargetAsync(first));
        await File.WriteAllTextAsync(System.IO.Path.Combine(target, "程序.txt"), "B 分歧\n", TestContext.Current.CancellationToken);
        await backend.StageAsync(second, ["程序.txt"], true);
        await backend.CommitAsync(second, "B 分歧版本");
        var beforeConflict = (await backend.StatusAsync(second, true)).Revision;
        var rejectedTarget = await backend.ReadTargetAsync(second);
        await Assert.ThrowsAnyAsync<Exception>(() => backend.PushAsync(second, rejectedTarget));
        Assert.Equal(beforeConflict, (await backend.StatusAsync(second, true)).Revision);
        var events = new System.Collections.Concurrent.ConcurrentQueue<string>();
        try { await backend.SyncAsync(second, await backend.VerifyRemoteAsync(second)); }
        catch (LoreVcs.LoreError error) { TestContext.Current.TestOutputHelper!.WriteLine("Divergent sync return=" + error.ReturnCode); }
        var conflicted = await backend.StatusAsync(second, true);
        TestContext.Current.TestOutputHelper!.WriteLine("Divergent sync events=" + string.Join(",", events.Distinct()) + "; unresolved=" + conflicted.HasUnresolvedConflicts);
        Assert.True(conflicted.HasUnresolvedConflicts);
        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.ContinueMergeAsync(second, "仍有冲突不允许继续"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.AbortMergeAsync(second, "wrong", conflicted.MergedRevision));
        var conflictBytes = await File.ReadAllBytesAsync(System.IO.Path.Combine(target, "程序.txt"), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.SwitchBranchAsync(second, "main"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.CreateBranchAsync(second, "unsafe-merge"));
        Assert.Equal(conflictBytes, await File.ReadAllBytesAsync(System.IO.Path.Combine(target, "程序.txt"), TestContext.Current.CancellationToken));
        using var abort = new LoreBranchMergeAbortArgs();
        var abortedResult = await backend.AbortMergeAsync(second, conflicted.Revision, conflicted.MergedRevision);
        var backupEntry = (await File.ReadAllLinesAsync(System.IO.Path.Combine(abortedResult.BackupPath, "manifest.txt"), TestContext.Current.CancellationToken)).Single(line => line.EndsWith("\t程序.txt", StringComparison.Ordinal)).Split('\t')[0];
        Assert.Equal(conflictBytes, await File.ReadAllBytesAsync(System.IO.Path.Combine(abortedResult.BackupPath, backupEntry), TestContext.Current.CancellationToken));
        var allowedBackups = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SourceLore.AbortBackups")) + System.IO.Path.DirectorySeparatorChar;
        if (!System.IO.Path.GetFullPath(abortedResult.BackupPath).StartsWith(allowedBackups, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid backup cleanup path");
        Directory.Delete(abortedResult.BackupPath, true);
        var aborted = await backend.StatusAsync(second, true);
        Assert.False(aborted.HasConflicts);
        Assert.Equal(beforeConflict, aborted.Revision);
        Assert.Equal("B 分歧\n", await File.ReadAllTextAsync(System.IO.Path.Combine(target, "程序.txt"), TestContext.Current.CancellationToken));
        try { await backend.SyncAsync(second, await backend.VerifyRemoteAsync(second)); }
        catch (LoreVcs.LoreError error) { TestContext.Current.TestOutputHelper!.WriteLine("Repeated sync return=" + error.ReturnCode); }
        Assert.True((await backend.StatusAsync(second, true)).HasUnresolvedConflicts);
        await File.WriteAllTextAsync(System.IO.Path.Combine(target, "程序.txt"), "人工解决 A 与 B\n", TestContext.Current.CancellationToken);
        using var resolve = new LoreBranchMergeResolveArgs { Paths = ["程序.txt"] };
        await backend.ResolveAsync(second, "程序.txt");
        Assert.False((await backend.StatusAsync(second, true)).HasUnresolvedConflicts);
        await backend.ContinueMergeAsync(second, "完成冲突合并");
        var mergeRevision = (await backend.StatusAsync(second, true)).Revision;
        var mergeEntry = await backend.RevisionAsync(second, mergeRevision);
        Assert.Equal(2, mergeEntry.Parents.Count);
        var parentTexts = new List<string>();
        foreach (var parent in mergeEntry.Parents)
        {
            Assert.Contains(await backend.ChangedFilesAsync(second, mergeRevision, parent), file => file.Path == "程序.txt" && file.IsFile);
            var comparison = await backend.CompareOnlineAsync(second, "程序.txt", parent, mergeRevision);
            parentTexts.Add(comparison.Left.Content.Text);
            Assert.Equal("人工解决 A 与 B\n", comparison.Right.Content.Text);
            Assert.Equal(parent, comparison.Left.Label);
            Assert.Equal(mergeRevision, comparison.Right.Label);
        }
        Assert.Contains("A 分歧\n", parentTexts);
        Assert.Contains("B 分歧\n", parentTexts);
        await backend.PushAsync(second, await backend.ReadTargetAsync(second));
        Assert.False((await backend.StatusAsync(second, true)).HasMerge);
        await backend.CreateBranchAsync(second, "after-merge");
        await backend.SwitchBranchAsync(second, "main");
        Assert.Equal("人工解决 A 与 B\n", await File.ReadAllTextAsync(System.IO.Path.Combine(target, "程序.txt"), TestContext.Current.CancellationToken));
        using var release = new LoreRepositoryReleaseArgs();
        await global::LoreVcs.Lore.RepositoryRelease(globals, release).WaitAsync();
        await global::LoreVcs.Lore.RepositoryRelease(cloneGlobals, release).WaitAsync();
    }

    [Fact]
    public async Task HistoryPagesKeepOriginalTipWhenNewRevisionIsCommitted()
    {
        await using var repository = new LoreFixture(fixture.Runtime);
        await repository.InitializeAsync();
        var expected = new List<string>();
        for (var index = 0; index < 5; index++)
        {
            await repository.WriteAsync("程序.txt", $"版本 {index}\n");
            await repository.Backend.StageAsync(repository.Workspace, ["程序.txt"], true);
            await repository.Backend.CommitAsync(repository.Workspace, $"历史 {index}");
            expected.Add((await repository.Backend.StatusAsync(repository.Workspace, false)).Revision);
        }
        var pager = new SourceLore.Core.Workspaces.RevisionHistoryPager(repository.Backend, repository.Workspace, expected[^1]);
        var entries = (await pager.NextAsync(2)).ToList();
        Assert.Equal("历史 4", entries[0].Message);
        Assert.NotNull(entries[0].Timestamp);
        Assert.InRange(entries[0].Timestamp!.Value, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1));
        await repository.WriteAsync("程序.txt", "分页期间新增\n");
        await repository.Backend.StageAsync(repository.Workspace, ["程序.txt"], true);
        await repository.Backend.CommitAsync(repository.Workspace, "新版本不插入旧分页");
        while (pager.HasMore) entries.AddRange(await pager.NextAsync(2));
        Assert.Equal(expected.AsEnumerable().Reverse(), entries.Take(5).Select(entry => entry.Revision));
        Assert.Equal(entries.Count, entries.Select(entry => entry.Revision).Distinct().Count());
        Assert.DoesNotContain(entries, entry => entry.Message == "新版本不插入旧分页");
        await Assert.ThrowsAnyAsync<Exception>(() => repository.Backend.RevisionAsync(repository.Workspace, new string('F', 64)));
    }

    [Fact]
    public async Task BatchStagingWithInvalidPathReconcilesNativeFlagsWithoutChangingContent()
    {
        await using var repository = new LoreFixture(fixture.Runtime);
        await repository.InitializeAsync();
        await repository.WriteAsync("one.txt", "一号内容\n");
        await repository.WriteAsync("two.txt", "二号内容\n");
        await repository.Backend.StatusAsync(repository.Workspace, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.Backend.StageAsync(repository.Workspace, ["one.txt", "../outside.txt", "two.txt"], true));
        var state = await repository.Backend.StatusAsync(repository.Workspace, true);
        TestContext.Current.TestOutputHelper!.WriteLine("Mixed stage native flags: " + string.Join(",", state.Files.Select(file => file.Path + "=" + file.Staged)));
        Assert.All(state.Files.Where(file => !file.IsDirectory), file => Assert.True(file.Staged));
        Assert.Equal("一号内容\n", await File.ReadAllTextAsync(System.IO.Path.Combine(repository.Path, "one.txt"), TestContext.Current.CancellationToken));
        Assert.Equal("二号内容\n", await File.ReadAllTextAsync(System.IO.Path.Combine(repository.Path, "two.txt"), TestContext.Current.CancellationToken));
        await repository.Backend.StageAsync(repository.Workspace, ["one.txt", "two.txt"], true);
        Assert.All((await repository.Backend.StatusAsync(repository.Workspace, false)).Files.Where(file => !file.IsDirectory), file => Assert.True(file.Staged));
        await repository.Backend.StageAsync(repository.Workspace, ["one.txt", "two.txt"], false);
        Assert.All((await repository.Backend.StatusAsync(repository.Workspace, false)).Files.Where(file => !file.IsDirectory), file => Assert.False(file.Staged));
    }

    [Fact]
    public async Task WatcherTracksExternalChangesRevertsDeletesAndOverflowRecovery()
    {
        await using var repository = new LoreFixture(fixture.Runtime);
        await repository.InitializeAsync();
        await repository.WriteAsync("程序.txt", "原文\n");
        await repository.WriteAsync(".loreignore", "ignored.txt\n");
        await repository.Backend.StageAsync(repository.Workspace, ["程序.txt", ".loreignore"], true);
        await repository.Backend.CommitAsync(repository.Workspace, "监控基线");
        var snapshots = System.Threading.Channels.Channel.CreateUnbounded<SourceLore.Core.Workspaces.WorkspaceSnapshot>();
        var scans = 0;
        var incremental = 0;
        await using var monitor = new WorkspaceFileMonitor(repository.Path, async batch =>
        {
            if (!batch.RequiresScan) { await repository.Backend.UpdateDirtyAsync(repository.Workspace, batch.Paths); Interlocked.Increment(ref incremental); }
            else Interlocked.Increment(ref scans);
            var status = await repository.Backend.StatusAsync(repository.Workspace, batch.RequiresScan);
            await snapshots.Writer.WriteAsync(status);
        });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        async Task<SourceLore.Core.Workspaces.WorkspaceSnapshot> Until(Func<SourceLore.Core.Workspaces.WorkspaceSnapshot, bool> matches)
        {
            while (true) { var state = await snapshots.Reader.ReadAsync(timeout.Token); if (matches(state)) return state; }
        }
        await repository.WriteAsync("程序.txt", "外部修改\n");
        await Until(state => state.Files.Any(file => file.Path == "程序.txt" && file.Dirty));
        await repository.WriteAsync("程序.txt", "原文\n");
        await Until(state => state.Files.All(file => file.Path != "程序.txt"));
        await repository.WriteAsync("新增.txt", "新增\n");
        await Until(state => state.Files.Any(file => file.Path == "新增.txt"));
        File.Delete(System.IO.Path.Combine(repository.Path, "新增.txt"));
        File.Delete(System.IO.Path.Combine(repository.Path, "程序.txt"));
        await Until(state => state.Files.Any(file => file.Path == "程序.txt" && file.Action == "DELETE") && state.Files.All(file => file.Path != "新增.txt"));
        await repository.WriteAsync("程序.txt", "原文\n");
        await repository.WriteAsync("ignored.txt", "忽略样本\n");
        monitor.RequestScan();
        var recovered = await Until(state => state.Files.All(file => file.Path != "程序.txt"));
        Assert.DoesNotContain(recovered.Files, file => file.Path == "ignored.txt");
        Assert.True(incremental > 0);
        Assert.True(scans > 0);
        Assert.Equal("原文\n", await File.ReadAllTextAsync(System.IO.Path.Combine(repository.Path, "程序.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BranchOperationsRefuseLocalWorkAndReconcileActualCurrentBranch()
    {
        await using var repository = new LoreFixture(fixture.Runtime);
        await repository.InitializeAsync();
        await repository.WriteAsync("程序.txt", "原文\n");
        await repository.Backend.StageAsync(repository.Workspace, ["程序.txt"], true);
        await repository.Backend.CommitAsync(repository.Workspace, "初始版本");
        var start = await repository.Backend.StatusAsync(repository.Workspace, true);
        await repository.Backend.CreateBranchAsync(repository.Workspace, "feature-code");
        var created = await repository.Backend.StatusAsync(repository.Workspace, true);
        Assert.Equal("feature-code", created.BranchName);
        Assert.Equal(start.Revision, created.Revision);
        var branches = await repository.Backend.BranchesAsync(repository.Workspace);
        Assert.Contains(branches, branch => branch.Name == "feature-code" && branch.IsCurrent);
        await Assert.ThrowsAnyAsync<Exception>(() => repository.Backend.CreateBranchAsync(repository.Workspace, "feature-code"));
        await Assert.ThrowsAnyAsync<Exception>(() => repository.Backend.CreateBranchAsync(repository.Workspace, ""));
        await Assert.ThrowsAnyAsync<Exception>(() => repository.Backend.CreateBranchAsync(repository.Workspace, new string('x', 1001)));
        await repository.Backend.SwitchBranchAsync(repository.Workspace, "main");
        Assert.Equal("main", (await repository.Backend.StatusAsync(repository.Workspace, true)).BranchName);
        await repository.WriteAsync("程序.txt", "不能丢失的修改\n");
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.Backend.SwitchBranchAsync(repository.Workspace, "feature-code"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.Backend.CreateBranchAsync(repository.Workspace, "unsafe"));
        await repository.Backend.StageAsync(repository.Workspace, ["程序.txt"], true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.Backend.SwitchBranchAsync(repository.Workspace, "feature-code"));
        Assert.Equal("不能丢失的修改\n", await File.ReadAllTextAsync(System.IO.Path.Combine(repository.Path, "程序.txt"), TestContext.Current.CancellationToken));
        Assert.Equal("main", (await repository.Backend.StatusAsync(repository.Workspace, true)).BranchName);
        await repository.Backend.CommitAsync(repository.Workspace, "保存本地工作");
        await repository.Backend.SwitchBranchAsync(repository.Workspace, "feature-code");
        Assert.Equal("原文\n", await File.ReadAllTextAsync(System.IO.Path.Combine(repository.Path, "程序.txt"), TestContext.Current.CancellationToken));
        var main = (await repository.Backend.BranchesAsync(repository.Workspace)).First(branch => branch.Name == "main");
        var history = new SourceLore.Core.Workspaces.RevisionHistoryPager(repository.Backend, repository.Workspace, main.Latest);
        Assert.Equal("保存本地工作", (await history.NextAsync(1))[0].Message);
        Assert.Equal("feature-code", (await repository.Backend.StatusAsync(repository.Workspace, false)).BranchName);
        await repository.Backend.SwitchBranchAsync(repository.Workspace, "main");
        Assert.Equal("不能丢失的修改\n", await File.ReadAllTextAsync(System.IO.Path.Combine(repository.Path, "程序.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RecordPinnedSdkBranchCreationBehavior()
    {
        await using var repository = new LoreFixture(fixture.Runtime);
        await repository.InitializeAsync();
        await repository.WriteAsync("程序.txt", "初始内容\n");
        await repository.Backend.StageAsync(repository.Workspace, ["程序.txt"], true);
        await repository.Backend.CommitAsync(repository.Workspace, "初始版本");
        var before = await repository.Backend.StatusAsync(repository.Workspace, false);
        using var globals = new LoreGlobalArgs
        {
            RepositoryPath = repository.Path, WorkingDirectory = repository.Path, Offline = true, Local = true
        };
        using var args = new LoreBranchCreateArgs { Branch = "feature-probe" };
        await global::LoreVcs.Lore.BranchCreate(globals, args).WaitAsync();
        var after = await repository.Backend.StatusAsync(repository.Workspace, false);
        TestContext.Current.TestOutputHelper!.WriteLine($"Lore {fixture.Runtime.Version}: BranchCreate changed current branch from {before.BranchName} to {after.BranchName}; revision unchanged={before.Revision == after.Revision}");
        Assert.Equal("feature-probe", after.BranchName);
        Assert.NotEqual(before.BranchName, after.BranchName);
        Assert.Equal(before.Revision, after.Revision);
    }
}
