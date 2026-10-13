using SourceLore.App.ViewModels;
using SourceLore.Core.Workspaces;
using SourceLore.Core.Operations;

namespace SourceLore.App.SmokeTests;

public class HistoryViewModelTests
{
    [Fact]
    public async Task FailedOlderPagePreservesListAndNewTipRequiresExplicitRefresh()
    {
        var backend = new Backend();
        var model = new MainWindowViewModel(backend);
        await model.OpenPathAsync("workspace");
        model.RefreshHistory.Execute(null);
        await model.WaitForIdleAsync();
        Assert.Equal(100, model.History.Count);
        backend.Fail = true;
        model.MoreHistory.Execute(null);
        await model.WaitForIdleAsync();
        Assert.Equal(100, model.History.Count);
        Assert.Contains("未缓存", model.HistoryStatus);
        backend.Fail = false;
        backend.Tip = 105;
        model.Refresh.Execute(null);
        await model.WaitForIdleAsync();
        Assert.Contains("请刷新历史", model.HistoryStatus);
        model.MoreHistory.Execute(null);
        await model.WaitForIdleAsync();
        Assert.Equal(104, model.History.Count);
        Assert.Equal("104", model.History[0].Revision);
        model.RefreshHistory.Execute(null);
        await model.WaitForIdleAsync();
        Assert.Equal("105", model.History[0].Revision);
    }

    private sealed class Backend : IWorkspaceBackend, IHistoryBackend
    {
        public int Tip { get; set; } = 104;
        public bool Fail { get; set; }
        public Task<WorkspaceIdentity> OpenAsync(string path) => Task.FromResult(new WorkspaceIdentity(path, path, path, ""));
        public Task<WorkspaceSnapshot> StatusAsync(WorkspaceIdentity workspace, bool scan, IProgress<OperationProgress>? progress = null) => Task.FromResult(new WorkspaceSnapshot(Tip.ToString(), "main", "", [], DateTimeOffset.UtcNow));
        public Task<RevisionEntry> RevisionAsync(WorkspaceIdentity workspace, string revision)
        {
            if (Fail) throw new IOException("版本 " + revision + " 未缓存，离线不可用。");
            var number = int.Parse(revision);
            return Task.FromResult(new RevisionEntry(revision, (ulong)number, number > 1 ? [(number - 1).ToString()] : [], "版本 " + revision, "", null));
        }
        public Task<string> DiffAsync(WorkspaceIdentity workspace, string path) => throw new NotSupportedException();
        public Task StageAsync(WorkspaceIdentity workspace, IReadOnlyList<string> paths, bool staged) => throw new NotSupportedException();
        public Task CommitAsync(WorkspaceIdentity workspace, string message) => throw new NotSupportedException();
    }
}
