using SourceLore.App.ViewModels;
using SourceLore.Core.Operations;
using SourceLore.Core.Workspaces;
using SourceLore.Infrastructure.Lore.Diagnostics;

namespace SourceLore.App.SmokeTests;

public class SynchronizationViewModelTests
{
    [Fact]
    public async Task TargetPreparationDoesNotWriteAndRemoteAnchorBecomesUnknownAfterOfflineCommitOrFailure()
    {
        var backend = new Backend { Files = [new("code.txt", 10, "MODIFY", false, true, false, false, false, null)] };
        var model = new MainWindowViewModel(backend, journal: new Store());
        await model.OpenPathAsync("A"); model.Message = "保留草稿";
        model.PreparePush.Execute(null); await model.WaitForIdleAsync();
        Assert.True(model.HasRemotePlan); Assert.Equal(0, backend.PushCalls);
        Assert.Contains("未提交", model.RemotePlanLabel);
        model.ConfirmRemoteOperation.Execute(null); await model.WaitForIdleAsync();
        Assert.Equal(1, backend.PushCalls); Assert.Equal(0, backend.CommitCalls);
        Assert.Equal("保留草稿", model.Message); Assert.Single(model.Files);
        Assert.Contains("数量为 0", model.RemoteStateLabel);
        backend.Revision = "local-two";
        model.Refresh.Execute(null); await model.WaitForIdleAsync();
        Assert.Contains("本地版本已变化", model.RemoteStateLabel);
        Assert.DoesNotContain("数量为 0", model.RemoteStateLabel);
        backend.VerifyFails = true;
        model.VerifyRemote.Execute(null); await model.WaitForIdleAsync();
        Assert.Contains("过期", model.RemoteStateLabel); Assert.Contains("数量未知", model.RemoteStateLabel);
    }
    [Fact]
    public async Task InterruptedPushIsNeverReplayedAndRestartRequiresExplicitRemoteReconciliation()
    {
        var backend = new Backend { PushFailsAfterUpload = true }; var store = new Store();
        var model = new MainWindowViewModel(backend, SafeDiagnostics.Describe, journal: store);
        await model.OpenPathAsync("A");
        model.PreparePush.Execute(null); await model.WaitForIdleAsync();
        model.ConfirmRemoteOperation.Execute(null); await model.WaitForIdleAsync();
        Assert.Equal(1, backend.PushCalls); Assert.NotNull(store.Pending);
        Assert.DoesNotContain("test-secret", model.Feedback);
        Assert.False(model.PreparePush.CanExecute(null));
        var restarted = new MainWindowViewModel(backend, SafeDiagnostics.Describe, journal: store);
        await restarted.OpenPathAsync("A");
        Assert.Equal(1, backend.PushCalls); Assert.Contains("Push", restarted.Recovery);
        backend.VerifyFails = true;
        restarted.ConfirmReconciliation.Execute(null); await restarted.WaitForIdleAsync();
        Assert.NotNull(store.Pending); Assert.Equal(1, backend.PushCalls);
        backend.VerifyFails = false;
        restarted.ConfirmReconciliation.Execute(null); await restarted.WaitForIdleAsync();
        Assert.Null(store.Pending); Assert.Equal(1, backend.PushCalls);
    }
    [Fact]
    public async Task ReopenedConflictRequiresBackendResolutionBeforeLocalMergeCommit()
    {
        var backend = new Backend { Merged = "remote-other", Files = [new("code.txt", 10, "MODIFY", false, true, true, true, true, null)] };
        var model = new MainWindowViewModel(backend, journal: new Store());
        await model.OpenPathAsync("A");
        Assert.Single(model.Conflicts); Assert.False(model.ContinueMerge.CanExecute(null));
        model.PrepareSync.Execute(null); await model.WaitForIdleAsync();
        Assert.Equal(0, backend.SyncCalls); Assert.Contains("未完成合并", model.Feedback);
        model.MarkConflictResolved.Execute(null); await model.WaitForIdleAsync();
        Assert.Equal(1, backend.ResolveCalls); Assert.False(model.Conflicts[0].Unresolved);
        Assert.True(model.ContinueMerge.CanExecute(null));
        model.Message = "解决后合并";
        model.ContinueMerge.Execute(null); await model.WaitForIdleAsync();
        Assert.False(model.HasMerge); Assert.Empty(model.Conflicts); Assert.Equal("", model.Message);
        Assert.Equal(1, backend.MergeCalls); Assert.Equal(0, backend.PushCalls);
    }
    [Fact]
    public async Task AbortNeedsConfirmationAndFailureRetainsRecoveryAndDraft()
    {
        var backend = new Backend { Merged = "remote-other", AbortFails = true, Files = [new("code.txt", 10, "MODIFY", false, true, true, true, true, null)] };
        var store = new Store(); var model = new MainWindowViewModel(backend, journal: store);
        await model.OpenPathAsync("A"); model.Message = "保留说明";
        model.PrepareAbort.Execute(null); await model.WaitForIdleAsync();
        Assert.Equal(0, backend.AbortCalls); Assert.Contains("人工合并编辑", model.RemotePlanLabel);
        model.CancelRemotePlan.Execute(null); await model.WaitForIdleAsync();
        Assert.Equal(0, backend.AbortCalls); Assert.False(model.HasRemotePlan);
        model.PrepareAbort.Execute(null); await model.WaitForIdleAsync();
        model.ConfirmRemoteOperation.Execute(null); await model.WaitForIdleAsync();
        Assert.Equal(1, backend.AbortCalls); Assert.True(model.HasMerge);
        Assert.Contains("待核实", model.Feedback); Assert.NotNull(store.Pending);
        Assert.Equal("保留说明", model.Message); Assert.Equal("", model.BackupPath);
    }
    [Fact]
    public async Task WorkspaceSwitchInvalidatesConfirmationAndLocalChangesPreventSync()
    {
        var backend = new Backend(); var model = new MainWindowViewModel(backend, journal: new Store());
        await model.OpenPathAsync("A");
        model.PreparePush.Execute(null); await model.WaitForIdleAsync();
        await model.OpenPathAsync("B");
        Assert.False(model.HasRemotePlan); Assert.False(model.ConfirmRemoteOperation.CanExecute(null));
        Assert.Equal(0, backend.PushCalls);
        backend.Files = [new("untracked.txt", 10, "ADD", false, true, false, false, false, null)];
        model.PrepareSync.Execute(null); await model.WaitForIdleAsync();
        Assert.Equal(0, backend.SyncCalls); Assert.False(model.HasRemotePlan);
        Assert.Contains("本地修改或未跟踪", model.Feedback);
    }
    private sealed class Store : IPendingOperationStore
    {
        public PendingOperation? Pending;
        public Task<PendingOperation?> ReadAsync(WorkspaceIdentity workspace) => Task.FromResult(Pending);
        public Task WriteAsync(PendingOperation operation) { Pending = operation; return Task.CompletedTask; }
        public Task ClearAsync(WorkspaceIdentity workspace) { Pending = null; return Task.CompletedTask; }
    }
    private sealed class Backend : IWorkspaceBackend, ISynchronizationBackend
    {
        public string Revision = "local-one", Remote = "remote-old", Merged = "";
        public FileChange[] Files = [];
        public bool VerifyFails, PushFailsAfterUpload, AbortFails;
        public int PushCalls, SyncCalls, ResolveCalls, MergeCalls, AbortCalls, CommitCalls;
        public Task<WorkspaceIdentity> OpenAsync(string path) => Task.FromResult(new WorkspaceIdentity(path, path, path, ""));
        public Task<WorkspaceSnapshot> StatusAsync(WorkspaceIdentity workspace, bool scan, IProgress<OperationProgress>? progress = null) => Task.FromResult(State());
        private WorkspaceSnapshot State() => new(Revision, "main", Merged, Files, DateTimeOffset.UtcNow);
        private RemoteTarget Target() => new("lore://server", "lore://server", "branch", "main", Revision, "repo");
        public Task<RemoteTarget> ReadTargetAsync(WorkspaceIdentity workspace) => Task.FromResult(Target());
        public Task<RemoteAnchor> VerifyRemoteAsync(WorkspaceIdentity workspace) => VerifyFails ? Task.FromException<RemoteAnchor>(new IOException("网络不可达")) : Task.FromResult(new RemoteAnchor(Target(), Remote, true, Remote != Revision, false, DateTimeOffset.UtcNow));
        public async Task<RemoteAnchor> PushAsync(WorkspaceIdentity workspace, RemoteTarget target, IProgress<OperationProgress>? progress = null)
        { PushCalls++; Remote = Revision; if (PushFailsAfterUpload) throw new IOException("断开 Authorization: Bearer test-secret"); return await VerifyRemoteAsync(workspace); }
        public Task<WorkspaceSnapshot> SyncAsync(WorkspaceIdentity workspace, RemoteAnchor target, IProgress<OperationProgress>? progress = null) { SyncCalls++; Revision = Remote; return Task.FromResult(State()); }
        public Task<WorkspaceSnapshot> ResolveAsync(WorkspaceIdentity workspace, string path) { ResolveCalls++; Files = Files.Select(file => file with { Unresolved = false }).ToArray(); return Task.FromResult(State()); }
        public Task<WorkspaceSnapshot> ContinueMergeAsync(WorkspaceIdentity workspace, string message) { MergeCalls++; Revision = "merged-new"; Merged = ""; Files = []; return Task.FromResult(State()); }
        public Task<AbortResult> AbortMergeAsync(WorkspaceIdentity workspace, string expectedRevision, string expectedMergedRevision)
        { AbortCalls++; if (AbortFails) throw new IOException("备份或中止失败"); Merged = ""; Files = []; return Task.FromResult(new AbortResult(State(), "backup")); }
        public Task<string> DiffAsync(WorkspaceIdentity workspace, string path) => Task.FromResult("readonly diff");
        public Task StageAsync(WorkspaceIdentity workspace, IReadOnlyList<string> paths, bool staged) => throw new NotSupportedException();
        public Task CommitAsync(WorkspaceIdentity workspace, string message) { CommitCalls++; return Task.CompletedTask; }
    }
}
