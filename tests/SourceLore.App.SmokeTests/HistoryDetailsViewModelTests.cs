using System.Text;
using SourceLore.App.ViewModels;
using SourceLore.Core.Operations;
using SourceLore.Core.Workspaces;

namespace SourceLore.App.SmokeTests;

public class HistoryDetailsViewModelTests
{
    [Fact]
    public async Task ParentSwitchDiscardsLateListsAndComparisonsAndKeepsWorkspaceSourceSeparate()
    {
        var backend = new Backend { DelayMainParent = true };
        var model = new MainWindowViewModel(backend);
        await model.OpenPathAsync("workspace");
        await model.WaitForIdleAsync();
        var workspacePatch = model.Patch;
        var merge = Entry("merge", ["main-parent", "other-parent"]);
        model.History.Add(merge); model.History.Add(Entry("arbitrary", []));
        model.SelectedHistory = merge;
        Assert.Equal("main-parent", model.SelectedHistoryBase!.Revision);
        model.SelectedHistoryBase = model.HistoryBases.First(choice => choice.Revision == "other-parent");
        Assert.Contains("other-parent", model.HistoryComparisonLabel);
        Assert.Contains("merge", model.HistoryComparisonLabel);
        backend.Older.SetResult([new("old.txt", "MODIFY", true, null)]);
        await model.WaitForIdleAsync();
        Assert.Single(model.HistoryFiles);
        Assert.Equal("same.txt", model.HistoryFiles[0].Path);
        Assert.Contains("other-parent", model.HistoryLeft);
        Assert.Equal(workspacePatch, model.Patch);
        model.SelectedHistoryBase = model.HistoryBases.First(choice => choice.Revision == "arbitrary");
        await model.WaitForIdleAsync();
        Assert.Contains("arbitrary", model.HistoryComparisonLabel);
    }
    [Fact]
    public async Task InitialRevisionUsesAbsentSideAndSameParentAcrossNewTargetsReloads()
    {
        var model = new MainWindowViewModel(new Backend());
        await model.OpenPathAsync("workspace");
        model.SelectedHistory = Entry("initial", []);
        await model.WaitForIdleAsync();
        Assert.Equal("", model.SelectedHistoryBase!.Revision);
        Assert.Contains("initial", model.HistoryComparisonLabel);
        model.SelectedHistory = Entry("one", ["shared"]);
        await model.WaitForIdleAsync();
        Assert.Contains("one", model.HistoryRight);
        model.SelectedHistory = Entry("two", ["shared"]);
        await model.WaitForIdleAsync();
        Assert.Contains("two", model.HistoryRight);
        Assert.DoesNotContain("one", model.HistoryRight);
    }
    private static RevisionEntry Entry(string revision, string[] parents) => new(revision, 1, parents, "完整说明\n第二行", "作者", DateTimeOffset.UtcNow);
    private sealed class Backend : IWorkspaceBackend, ITextComparisonBackend, IRevisionDetailBackend
    {
        public bool DelayMainParent;
        public TaskCompletionSource<IReadOnlyList<RevisionFileChange>> Older = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<WorkspaceIdentity> OpenAsync(string path) => Task.FromResult(new WorkspaceIdentity(path, path, path, ""));
        public Task<WorkspaceSnapshot> StatusAsync(WorkspaceIdentity workspace, bool scan, IProgress<OperationProgress>? progress = null) => Task.FromResult(new WorkspaceSnapshot("tip", "main", "", [new("same.txt", 1, "MODIFY", false, true, false, false, false, null)], DateTimeOffset.UtcNow));
        public Task<IReadOnlyList<RevisionFileChange>> ChangedFilesAsync(WorkspaceIdentity workspace, string revision, string parent) => DelayMainParent && parent == "main-parent" ? Older.Task : Task.FromResult<IReadOnlyList<RevisionFileChange>>([new("same.txt", "MODIFY", true, null)]);
        public Task<TextComparison> CompareAsync(WorkspaceIdentity workspace, string path, string sourceRevision, string? targetRevision) => Task.FromResult(TextComparison.Build(path,
            new(sourceRevision, sourceRevision != "", TextContent.Decode(Encoding.UTF8.GetBytes(sourceRevision))),
            new(targetRevision ?? "工作文件", true, TextContent.Decode(Encoding.UTF8.GetBytes(targetRevision ?? "工作文件")))));
        public Task<string> DiffAsync(WorkspaceIdentity workspace, string path) => throw new InvalidOperationException("Use typed comparisons");
        public Task StageAsync(WorkspaceIdentity workspace, IReadOnlyList<string> paths, bool staged) => throw new NotSupportedException();
        public Task CommitAsync(WorkspaceIdentity workspace, string message) => throw new NotSupportedException();
    }
}
