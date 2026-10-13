using SourceLore.App.ViewModels;
using SourceLore.Core.Operations;
using SourceLore.Core.Workspaces;
using SourceLore.Infrastructure.Lore.Persistence;

namespace SourceLore.App.SmokeTests;

public class WorkspaceSessionTests
{
    [Fact]
    public async Task AliasesRestoreDraftSelectionAndPageWhileClonesRemainIndependent()
    {
        var model = new MainWindowViewModel(new Backend());
        await model.OpenPathAsync("A");
        model.Message = "A draft";
        model.SelectedFile = model.Files[1];
        model.SelectedPage = 1;
        await model.OpenPathAsync("B");
        Assert.Equal("", model.Message);
        Assert.Equal("first.txt", model.SelectedFile!.Path);
        Assert.Equal(0, model.SelectedPage);
        model.Message = "B draft";
        await model.OpenPathAsync("a-alias");
        Assert.Equal("A draft", model.Message);
        Assert.Equal("second.txt", model.SelectedFile!.Path);
        Assert.Equal(1, model.SelectedPage);
        Assert.Equal(2, model.Recent.Count);
        await model.OpenPathAsync("B");
        Assert.Equal("B draft", model.Message);
        await model.WaitForIdleAsync();
    }

    [Fact]
    public async Task MissingRecentCanBeRemovedPersistentlyWithoutDeletingLocalFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "SourceLore.RecentTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sentinel = Path.Combine(root, "keep.txt");
            await File.WriteAllTextAsync(sentinel, "用户内容", TestContext.Current.CancellationToken);
            var store = new RecentWorkspaceStore(Path.Combine(root, "recent.json"));
            await store.WriteAsync([new("missing", "removed directory")]);
            var model = new MainWindowViewModel(new Backend(), recentStore: store);
            await model.InitializeRecentAsync();
            model.SelectedRecent = Assert.Single(model.Recent);
            model.OpenRecent.Execute(null);
            await model.WaitForIdleAsync();
            Assert.Contains("工作目录不存在", model.Status);
            Assert.Single(model.Recent);
            model.RemoveRecent.Execute(null);
            await model.WaitForIdleAsync();
            Assert.Empty(await store.ReadAsync());
            Assert.Equal("用户内容", await File.ReadAllTextAsync(sentinel, TestContext.Current.CancellationToken));
            Assert.DoesNotContain("remote", await File.ReadAllTextAsync(Path.Combine(root, "recent.json"), TestContext.Current.CancellationToken));
        }
        finally
        {
            var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SourceLore.RecentTests")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(root).StartsWith(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid cleanup root");
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class Backend : IWorkspaceBackend
    {
        public Task<WorkspaceIdentity> OpenAsync(string path)
        {
            if (path == "missing") throw new DirectoryNotFoundException("工作目录不存在。");
            var id = path == "a-alias" ? "A" : path;
            return Task.FromResult(new WorkspaceIdentity(id, id, id, "same remote"));
        }
        public Task<WorkspaceSnapshot> StatusAsync(WorkspaceIdentity workspace, bool scan, IProgress<OperationProgress>? progress = null) => Task.FromResult(new WorkspaceSnapshot(
            workspace.InstanceId, "main", "", [File("first.txt"), File("second.txt")], DateTimeOffset.UtcNow));
        private static FileChange File(string path) => new(path, 10, "MODIFY", false, true, false, false, false, null);
        public Task<string> DiffAsync(WorkspaceIdentity workspace, string path) => Task.FromResult(workspace.InstanceId + path);
        public Task StageAsync(WorkspaceIdentity workspace, IReadOnlyList<string> paths, bool staged) => throw new NotSupportedException();
        public Task CommitAsync(WorkspaceIdentity workspace, string message) => throw new NotSupportedException();
    }
}
