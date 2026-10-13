using SourceLore.App.ViewModels;
using SourceLore.Core.Workspaces;
using SourceLore.Core.Operations;

namespace SourceLore.App.SmokeTests;

public class QueryRaceTests
{
    [Fact]
    public async Task SwitchingWorkspaceWithSameFileMetadataDiscardsLateDiffAndLoadsNewSource()
    {
        var backend = new DelayedBackend();
        var model = new MainWindowViewModel(backend);
        await model.OpenPathAsync("A");
        model.SelectedFile = model.Files[0];
        Assert.Equal("正在加载差异…", model.Patch);
        await model.OpenPathAsync("B");
        Assert.Equal("B source", model.Patch);
        backend.OlderDiff.SetResult("A source");
        await model.WaitForIdleAsync();
        Assert.Equal("B source", model.Patch);
        Assert.Contains("B", model.Identity);
    }

    private sealed class DelayedBackend : IWorkspaceBackend
    {
        internal TaskCompletionSource<string> OlderDiff { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<WorkspaceIdentity> OpenAsync(string path) => Task.FromResult(new WorkspaceIdentity(path, path, path, ""));
        public Task<WorkspaceSnapshot> StatusAsync(WorkspaceIdentity workspace, bool scan, IProgress<OperationProgress>? progress = null) => Task.FromResult(new WorkspaceSnapshot(
            workspace.InstanceId, "main", "", [new("same.txt", 10, "MODIFY", false, true, false, false, false, null)], DateTimeOffset.UtcNow));
        public Task<string> DiffAsync(WorkspaceIdentity workspace, string path) => workspace.InstanceId == "A" ? OlderDiff.Task : Task.FromResult("B source");
        public Task StageAsync(WorkspaceIdentity workspace, IReadOnlyList<string> paths, bool staged) => throw new NotSupportedException();
        public Task CommitAsync(WorkspaceIdentity workspace, string message) => throw new NotSupportedException();
    }
}
