using Avalonia.Threading;
using SourceLore.App.ViewModels;
using SourceLore.App.Views;

namespace SourceLore.App;

internal static partial class UiSmoke
{
    private static async Task CheckOnlineWorkflowAsync(MainWindow window)
    {
        if (!Uri.TryCreate(OnlineRemote, UriKind.Absolute, out var uri) || uri.Host is not ("127.0.0.1" or "localhost" or "::1"))
            throw new InvalidOperationException("Online diagnostic only permits an isolated loopback test server");
        var allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SourceLore.OnlineSmokeTests")) + Path.DirectorySeparatorChar;
        if (!WorkspacePath!.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !SecondWorkspacePath!.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Online diagnostic requires isolated owned test directories");
        var model = (MainWindowViewModel)window.DataContext!;
        model.RemoteUrl = OnlineRemote!;
        model.CloneDestination = WorkspacePath!;
        model.Clone.Execute(null); await model.WaitForIdleAsync();
        Check(File.Exists(Path.Combine(WorkspacePath!, "程序.txt")), "First GUI clone failed: " + model.Feedback);
        model.CloneDestination = SecondWorkspacePath!;
        model.Clone.Execute(null); await model.WaitForIdleAsync();
        Check(File.Exists(Path.Combine(SecondWorkspacePath!, "程序.txt")), "Second GUI clone failed: " + model.Feedback);
        await model.OpenPathAsync(WorkspacePath!);
        await File.WriteAllTextAsync(Path.Combine(WorkspacePath!, "程序.txt"), "GUI 本地修改\n");
        await File.WriteAllTextAsync(Path.Combine(WorkspacePath!, "保留.txt"), "未选择的工作修改\n");
        await model.WaitForIdleAsync(); model.Refresh.Execute(null); await model.WaitForIdleAsync();
        model.SelectedFile = model.Files.First(file => file.Path == "程序.txt"); await model.WaitForIdleAsync();
        Check(model.LeftPatch.Contains("服务器初始", StringComparison.Ordinal) && model.RightPatch.Contains("GUI 本地修改", StringComparison.Ordinal), "Working diff sources incorrect: " + model.Patch);
        model.SideBySide = true; await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Check(window.LeftDiffEditor.Text == model.LeftPatch && window.RightDiffEditor.Text == model.RightPatch, "Side-by-side AOT bindings failed");
        model.SideBySide = false;
        await model.WaitForIdleAsync(); await model.StageSelectedAsync(true); model.Message = "GUI 选择提交";
        await model.WaitForIdleAsync(); await model.CommitStagedAsync();
        Check(model.Files.Any(file => file.Path == "保留.txt" && !file.Staged) && !model.Files.Any(file => file.Path == "程序.txt"), "Selective commit lost unselected edit");
        await model.WaitForIdleAsync(); model.VerifyRemote.Execute(null); await model.WaitForIdleAsync();
        Check(model.RemoteStateLabel.Contains("有未推送", StringComparison.Ordinal), "Local commit falsely reported pushed: " + model.RemoteStateLabel);
        model.SelectedPage = 3;
        model.PreparePush.Execute(null); await model.WaitForIdleAsync();
        Check(model.HasRemotePlan && model.RemotePlanLabel.Contains("main", StringComparison.Ordinal), "Push target not reviewed");
        model.ConfirmRemoteOperation.Execute(null); await model.WaitForIdleAsync();
        Check(model.RemoteStateLabel.Contains("数量为 0", StringComparison.Ordinal), "Successful push not verified: " + model.Feedback);
        Check(await File.ReadAllTextAsync(Path.Combine(WorkspacePath!, "保留.txt")) == "未选择的工作修改\n", "Push committed or changed unselected content");
        await model.OpenPathAsync(SecondWorkspacePath!);
        model.SelectedPage = 3;
        model.PrepareSync.Execute(null); await model.WaitForIdleAsync();
        Check(model.HasRemotePlan, "Sync preparation failed: " + model.Feedback);
        model.ConfirmRemoteOperation.Execute(null); await model.WaitForIdleAsync();
        Check(await File.ReadAllTextAsync(Path.Combine(SecondWorkspacePath!, "程序.txt")) == "GUI 本地修改\n", "Second workspace did not receive pushed content: " + model.Feedback);
        Check(await File.ReadAllTextAsync(Path.Combine(SecondWorkspacePath!, "保留.txt")) == "服务器保留原文\n", "Sync included uncommitted content");
        model.SelectedPage = 1;
        model.RefreshHistory.Execute(null); await model.WaitForIdleAsync();
        model.SelectedHistory = model.History.First(entry => entry.Message == "GUI 选择提交"); await model.WaitForIdleAsync();
        model.SelectedHistoryFile = model.HistoryFiles.First(file => file.Path == "程序.txt"); await model.WaitForIdleAsync();
        Check(model.HistoryLeft.Contains("服务器初始", StringComparison.Ordinal) && model.HistoryRight.Contains("GUI 本地修改", StringComparison.Ordinal) && window.HistoryDiffEditor.Text == model.HistoryPatch, "Historical AOT comparison did not show committed versions");
        model.SelectedPage = 2;
        model.NewBranchName = "aot-online-feature";
        model.CreateBranch.Execute(null); await model.WaitForIdleAsync();
        Check(model.Branches.Any(branch => branch.Name == "aot-online-feature" && branch.IsCurrent), "Create and switch failed: " + model.Feedback);
        model.SelectedBranch = model.Branches.First(branch => branch.Name == "main");
        model.SwitchBranch.Execute(null); await model.WaitForIdleAsync();
        Check(model.Branches.Any(branch => branch.Name == "main" && branch.IsCurrent), "Switch back failed: " + model.Feedback);
    }
    private static void Check(bool condition, string failure)
    {
        if (!condition) throw new InvalidOperationException(failure);
    }
}
