using System.Text;
using SourceLore.App.ViewModels;
using SourceLore.Core.Operations;
using SourceLore.Core.Workspaces;

namespace SourceLore.App.SmokeTests;

public class TextComparisonViewModelTests
{
    [Fact]
    public async Task LateTypedComparisonCannotReplaceNewSourcesAndModeSwitchDoesNotReadAgain()
    {
        var backend = new Backend();
        var model = new MainWindowViewModel(backend);
        await model.OpenPathAsync("A");
        await model.OpenPathAsync("B");
        Assert.Contains("B", model.ComparisonLabel);
        Assert.Contains("B before", model.LeftPatch);
        Assert.Contains("B after", model.RightPatch);
        var calls = backend.Reads;
        model.SideBySide = true; model.SideBySide = false;
        Assert.Equal(calls, backend.Reads);
        backend.Older.SetResult(Build("A"));
        await model.WaitForIdleAsync();
        Assert.Contains("B before", model.Patch);
        Assert.DoesNotContain("A before", model.LeftPatch);
    }
    [Fact]
    public async Task MissingExternalEditorExplainsFailureAndPreservesFileAndDraft()
    {
        var testRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SourceLore.ExternalEditorTests");
        var root = System.IO.Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var full = System.IO.Path.Combine(root, "same.txt");
        await File.WriteAllTextAsync(full, "保留中文\n", TestContext.Current.CancellationToken);
        try
        {
            var model = new MainWindowViewModel(new Backend(), openFile: _ => throw new FileNotFoundException("外部编辑器不存在"));
            await model.OpenPathAsync(root);
            await model.WaitForIdleAsync();
            model.Message = "草稿";
            model.OpenWorkingFile.Execute(null);
            await model.WaitForIdleAsync();
            Assert.Contains("外部编辑器不存在", model.Feedback);
            Assert.Equal("草稿", model.Message);
            Assert.Equal("保留中文\n", await File.ReadAllTextAsync(full, TestContext.Current.CancellationToken));
        }
        finally
        {
            if (!System.IO.Path.GetFullPath(root).StartsWith(System.IO.Path.GetFullPath(testRoot) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid cleanup root");
            Directory.Delete(root, true);
        }
    }
    private static TextComparison Build(string id) => TextComparison.Build("same.txt",
        new(id + " revision", true, TextContent.Decode(Encoding.UTF8.GetBytes(id + " before\r\n"))),
        new(id + " working", true, TextContent.Decode(Encoding.UTF8.GetBytes(id + " after\n"))));
    private sealed class Backend : IWorkspaceBackend, ITextComparisonBackend
    {
        public int Reads;
        public TaskCompletionSource<TextComparison> Older { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<WorkspaceIdentity> OpenAsync(string path) => Task.FromResult(new WorkspaceIdentity(path, path, path, ""));
        public Task<WorkspaceSnapshot> StatusAsync(WorkspaceIdentity workspace, bool scan, IProgress<OperationProgress>? progress = null) => Task.FromResult(new WorkspaceSnapshot("tip", "main", "", [new("same.txt", 10, "MODIFY", false, true, false, false, false, null)], DateTimeOffset.UtcNow));
        public Task<TextComparison> CompareAsync(WorkspaceIdentity workspace, string path, string sourceRevision, string? targetRevision)
        { Reads++; return workspace.InstanceId == "A" ? Older.Task : Task.FromResult(Build(workspace.InstanceId)); }
        public Task<string> DiffAsync(WorkspaceIdentity workspace, string path) => throw new InvalidOperationException("Typed comparison must be used");
        public Task StageAsync(WorkspaceIdentity workspace, IReadOnlyList<string> paths, bool staged) => throw new NotSupportedException();
        public Task CommitAsync(WorkspaceIdentity workspace, string message) => throw new NotSupportedException();
    }
}
