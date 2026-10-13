using SourceLore.App.ViewModels;
using SourceLore.Core.Operations;
using SourceLore.Core.Workspaces;

namespace SourceLore.App.SmokeTests;

public class BranchViewModelTests
{
    [Fact]
    public async Task FailureRetainsKnownBranchesAndUncertainSwitchShowsActualBranchAndBlocksReplay()
    {
        var backend = new Backend();
        var store = new Store();
        var model = new MainWindowViewModel(backend, journal: store);
        await model.OpenPathAsync("root");
        Assert.Equal(2, model.Branches.Count);
        backend.ListFails = true;
        model.RefreshBranches.Execute(null);
        await model.WaitForIdleAsync();
        Assert.Equal(2, model.Branches.Count);
        Assert.Contains("过期", model.BranchStatus);
        backend.ListFails = false;
        model.SelectedBranch = model.Branches.First(entry => entry.Name == "feature");
        model.SwitchBranch.Execute(null);
        await model.WaitForIdleAsync();
        Assert.Contains("feature", model.Identity);
        Assert.Contains("待核实", model.Status);
        Assert.Contains("SwitchBranch", model.Recovery);
        Assert.False(model.SwitchBranch.CanExecute(null));
        Assert.NotNull(store.Pending);
    }

    [Fact]
    public async Task LateDiffFromPreviousBranchCannotReplaceRefreshedCleanWorkspace()
    {
        var backend = new Backend { Dirty = true, SwitchFails = false, DelayedDiff = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var model = new MainWindowViewModel(backend, journal: new Store());
        await model.OpenPathAsync("root");
        Assert.Equal("正在加载差异…", model.Patch);
        backend.Dirty = false;
        model.SelectedBranch = model.Branches.First(entry => entry.Name == "feature");
        model.SwitchBranch.Execute(null);
        Assert.Contains("feature", model.Identity);
        backend.DelayedDiff.SetResult("old main diff");
        await model.WaitForIdleAsync();
        Assert.Equal("", model.Patch);
    }

    [Fact]
    public async Task DirtyWorkAndUnfinishedMergePreventBranchMutation()
    {
        var backend = new Backend { Dirty = true };
        var model = new MainWindowViewModel(backend);
        await model.OpenPathAsync("root");
        model.NewBranchName = "feature";
        model.CreateBranch.Execute(null);
        await model.WaitForIdleAsync();
        Assert.Contains("本地修改", model.Status);
        Assert.Equal(0, backend.Writes);
        backend.Dirty = false; backend.Merging = true;
        model.CreateBranch.Execute(null);
        await model.WaitForIdleAsync();
        Assert.Contains("合并流程", model.Status);
        Assert.Equal(0, backend.Writes);
    }

    private sealed class Store : IPendingOperationStore
    {
        public PendingOperation? Pending { get; private set; }
        public Task<PendingOperation?> ReadAsync(WorkspaceIdentity workspace) => Task.FromResult(Pending);
        public Task WriteAsync(PendingOperation operation) { Pending = operation; return Task.CompletedTask; }
        public Task ClearAsync(WorkspaceIdentity workspace) { Pending = null; return Task.CompletedTask; }
    }
    private sealed class Backend : IWorkspaceBackend, IBranchBackend
    {
        public bool ListFails { get; set; }
        public bool Dirty { get; set; }
        public bool Merging { get; set; }
        public int Writes { get; private set; }
        public bool SwitchFails { get; set; } = true;
        public TaskCompletionSource<string>? DelayedDiff { get; set; }
        private string current = "main";
        public Task<WorkspaceIdentity> OpenAsync(string path) => Task.FromResult(new WorkspaceIdentity("instance", path, path, ""));
        public Task<WorkspaceSnapshot> StatusAsync(WorkspaceIdentity workspace, bool scan, IProgress<OperationProgress>? progress = null) => Task.FromResult(new WorkspaceSnapshot("revision", current, Merging ? "other" : "", Dirty ? [new("code.txt", 1, "MODIFY", false, true, true, false, false, null)] : [], DateTimeOffset.UtcNow));
        public Task<IReadOnlyList<BranchEntry>> BranchesAsync(WorkspaceIdentity workspace)
        {
            if (ListFails) throw new IOException("offline");
            return Task.FromResult<IReadOnlyList<BranchEntry>>([new("main", "main", "revision", current == "main"), new("feature", "feature", "revision", current == "feature")]);
        }
        public Task SwitchBranchAsync(WorkspaceIdentity workspace, string name) { Writes++; current = name; if (SwitchFails) throw new IOException("completion lost"); return Task.CompletedTask; }
        public Task CreateBranchAsync(WorkspaceIdentity workspace, string name) { Writes++; throw new NotSupportedException(); }
        public Task<string> DiffAsync(WorkspaceIdentity workspace, string path) => DelayedDiff?.Task ?? Task.FromResult("diff");
        public Task StageAsync(WorkspaceIdentity workspace, IReadOnlyList<string> paths, bool staged) => throw new NotSupportedException();
        public Task CommitAsync(WorkspaceIdentity workspace, string message) => throw new NotSupportedException();
    }
}
