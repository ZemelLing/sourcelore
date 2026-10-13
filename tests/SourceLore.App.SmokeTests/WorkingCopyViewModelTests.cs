using SourceLore.App.ViewModels;
using SourceLore.Core.Workspaces;
using SourceLore.Core.Operations;

namespace SourceLore.App.SmokeTests;

public class WorkingCopyViewModelTests
{
    [Fact]
    public async Task BatchFailureShowsActualPartialStagingAndPreservesDraft()
    {
        var backend = new Backend();
        var model = new MainWindowViewModel(backend, journal: new Store());
        await model.OpenPathAsync("root");
        model.Message = "草稿保留";
        foreach (var row in model.Choices) row.IsChecked = true;
        Assert.All(model.Files, file => Assert.False(file.Staged));
        model.StageChecked.Execute(null);
        await model.WaitForIdleAsync();
        Assert.True(model.Files[0].Staged);
        Assert.False(model.Files[1].Staged);
        Assert.Contains("部分完成", model.Status);
        Assert.Equal("草稿保留", model.Message);
        Assert.False(model.StageChecked.CanExecute(null));
    }

    [Fact]
    public async Task BackgroundScanDoesNotEraseRejectedUserActionFeedback()
    {
        var monitor = new Monitor();
        var model = new MainWindowViewModel(new Backend(), monitorFactory: (_, changed) => { monitor.Changed = changed; return monitor; });
        await model.OpenPathAsync("root");
        model.Commit.Execute(null);
        await model.WaitForIdleAsync();
        await monitor.Changed!(new(["one.txt"], true));
        Assert.Contains("请填写", model.Feedback);
        Assert.Contains("已核对", model.Status);
        await model.StopMonitoringAsync();
    }

    [Fact]
    public async Task UncertainCommitPreservesDraftAndCannotReplayAutomatically()
    {
        var backend = new Backend { FirstStaged = true, CommitFails = true };
        var model = new MainWindowViewModel(backend, journal: new Store());
        await model.OpenPathAsync("root");
        model.Message = "不能丢失的说明";
        model.Commit.Execute(null);
        await model.WaitForIdleAsync();
        Assert.Equal("不能丢失的说明", model.Message);
        Assert.Contains("待核实", model.Status);
        Assert.False(model.Commit.CanExecute(null));
        model.Commit.Execute(null);
        Assert.Equal(1, backend.CommitCalls);
    }

    [Theory]
    [InlineData("", true, false, "请填写")]
    [InlineData("说明", false, false, "没有可提交")]
    [InlineData("说明", true, true, "冲突")]
    public async Task InvalidCommitDoesNotCallBackend(string message, bool staged, bool conflict, string reason)
    {
        var backend = new Backend { FirstStaged = staged, Conflict = conflict };
        var model = new MainWindowViewModel(backend);
        await model.OpenPathAsync("root");
        model.Message = message;
        model.Commit.Execute(null);
        await model.WaitForIdleAsync();
        Assert.Contains(reason, model.Status);
        Assert.Equal(message, model.Message);
        Assert.Equal(0, backend.CommitCalls);
    }

    [Fact]
    public async Task MonitorWaitsForActiveCommitAndStopsBeforeClosing()
    {
        var backend = new Backend { FirstStaged = true, CommitPause = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var monitor = new Monitor();
        var model = new MainWindowViewModel(backend, monitorFactory: (_, changed) => { monitor.Changed = changed; return monitor; });
        await model.OpenPathAsync("root");
        model.Message = "明确提交";
        model.Commit.Execute(null);
        Assert.True(model.Busy);
        var update = monitor.Changed!(new(["one.txt"], false));
        Assert.False(update.IsCompleted);
        Assert.Equal(0, backend.DirtyCalls);
        backend.CommitPause.SetResult();
        await update;
        await model.WaitForIdleAsync();
        Assert.Equal(1, backend.DirtyCalls);
        await model.StopMonitoringAsync();
        await monitor.Changed!(new(["two.txt"], true));
        Assert.True(monitor.Disposed);
        Assert.Equal(1, backend.DirtyCalls);
    }

    [Fact]
    public async Task FailedScanRetainsKnownFilesAndMarksStateStale()
    {
        var backend = new Backend();
        var model = new MainWindowViewModel(backend);
        await model.OpenPathAsync("root");
        backend.StatusFails = true;
        model.Refresh.Execute(null);
        await model.WaitForIdleAsync();
        Assert.Equal(2, model.Files.Count);
        Assert.Contains("可能过期", model.WorkspaceStateLabel);
        Assert.Contains("操作失败", model.Status);
    }

    private sealed class Monitor : IWorkspaceMonitor
    {
        public Func<WorkspaceInvalidation, Task>? Changed { get; set; }
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class Store : IPendingOperationStore
    {
        private PendingOperation? operation;
        public Task<PendingOperation?> ReadAsync(WorkspaceIdentity workspace) => Task.FromResult(operation);
        public Task WriteAsync(PendingOperation value) { operation = value; return Task.CompletedTask; }
        public Task ClearAsync(WorkspaceIdentity workspace) { operation = null; return Task.CompletedTask; }
    }
    private sealed class Backend : IWorkspaceBackend, IDirtyWorkspaceBackend
    {
        public bool FirstStaged { get; set; }
        public bool StatusFails { get; set; }
        public int DirtyCalls { get; private set; }
        public int CommitCalls { get; private set; }
        public bool CommitFails { get; set; }
        public bool Conflict { get; set; }
        public TaskCompletionSource? CommitPause { get; set; }
        public Task<WorkspaceIdentity> OpenAsync(string path) => Task.FromResult(new WorkspaceIdentity(path, path, path, ""));
        public Task<WorkspaceSnapshot> StatusAsync(WorkspaceIdentity workspace, bool scan, IProgress<OperationProgress>? progress = null)
        {
            if (StatusFails) throw new IOException("incomplete scan");
            return Task.FromResult(new WorkspaceSnapshot("revision", "main", "", [File("one.txt", FirstStaged), File("two.txt", false)], DateTimeOffset.UtcNow));
        }
        private FileChange File(string path, bool staged) => new(path, 1, "MODIFY", false, true, staged, Conflict, Conflict, null);
        public Task StageAsync(WorkspaceIdentity workspace, IReadOnlyList<string> paths, bool staged) { FirstStaged = staged; throw new IOException("second path failed after first succeeded"); }
        public Task<string> DiffAsync(WorkspaceIdentity workspace, string path) => Task.FromResult("diff");
        public Task CommitAsync(WorkspaceIdentity workspace, string message) { CommitCalls++; if (CommitFails) throw new IOException("completion lost"); return CommitPause?.Task ?? Task.CompletedTask; }
        public Task UpdateDirtyAsync(WorkspaceIdentity workspace, IReadOnlyList<string> paths) { DirtyCalls++; return Task.CompletedTask; }
    }
}
