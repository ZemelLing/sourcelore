using System.Runtime.CompilerServices;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using SourceLore.App.ViewModels;
using SourceLore.App.Views;
using SourceLore.Infrastructure.Lore.Diagnostics;

namespace SourceLore.App;

// Explicit diagnostic entry point used against the published executable, without a test runtime.
internal static partial class UiSmoke
{
    internal static string? ReportPath { get; set; }
    internal static string? WorkspacePath { get; set; }
    internal static bool ExpectDependencyError { get; set; }
    internal static string? JournalDirectory { get; set; }
    internal static string? NativeVersion { get; set; }
    internal static string? OnlineRemote { get; set; }
    internal static string? SecondWorkspacePath { get; set; }

    internal static void Run(IClassicDesktopStyleApplicationLifetime desktop)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var window = desktop.MainWindow ?? throw new InvalidOperationException("No main window");
                if (!window.IsVisible || window.Bounds.Width < 900 || window.Title != "SourceLore")
                    throw new InvalidOperationException("Window or compiled title binding was not initialized");
                if (RuntimeFeature.IsDynamicCodeSupported)
                    throw new InvalidOperationException("Smoke entry must run in a NativeAOT publish");
                if (ExpectDependencyError)
                {
                    var model = (MainWindowViewModel)window.DataContext!;
                    if (model.Open.CanExecute(null) || !model.Status.Contains("lorelib.dll", StringComparison.Ordinal))
                        throw new InvalidOperationException("Missing/corrupt native dependency did not block repository operations");
                }
                if (OnlineRemote is not null)
                    await CheckOnlineWorkflowAsync((MainWindow)window);
                else if (WorkspacePath is not null)
                    await CheckWorkspaceAsync((MainWindow)window);
                await ((MainWindowViewModel)window.DataContext!).StopMonitoringAsync();
                await ((MainWindowViewModel)window.DataContext!).WaitForIdleAsync();
                ((MainWindowViewModel)window.DataContext!).ReleaseExternalSnapshots();
                var coverage = ExpectDependencyError ? "dependency diagnostics; repository operations blocked" :
                    OnlineRemote is not null ? "seven capabilities: clone/edit/two diff modes/selective local commit/push/second workspace sync/history compare/create-switch branch; confirmed remote anchor" :
                    WorkspacePath is null ? "visible window; compiled title binding" : "native status/diff/stage/unstage/commit/history/branches; Unicode selection/copy; readonly editor; invalid commit/root";
                File.WriteAllText(ReportPath!, "PASS: NativeAOT; " + coverage + "; UI dispatch; graceful exit; native=" + (NativeVersion ?? "not loaded") + "\n");
                desktop.Shutdown(0);
            }
            catch (Exception ex)
            {
                if (desktop.MainWindow?.DataContext is MainWindowViewModel model)
                {
                    await model.StopMonitoringAsync();
                    await model.WaitForIdleAsync();
                    model.ReleaseExternalSnapshots();
                }
                File.WriteAllText(ReportPath!, "FAIL: " + SafeDiagnostics.Describe(ex));
                desktop.Shutdown(1);
            }
        }, DispatcherPriority.Background);
    }

    private static async Task CheckWorkspaceAsync(MainWindow window)
    {
        var model = (MainWindowViewModel)window.DataContext!;
        await model.OpenPathAsync(WorkspacePath!);
        await model.WaitForIdleAsync();
        await CheckInputClipboardAsync(window);
        if (!model.Files.Any(file => file.Path == "程序.txt" && file.Dirty))
            throw new InvalidOperationException("AOT status did not report the changed Unicode file: " + model.Status);
        model.SelectedFile = model.Files.First(file => file.Path == "程序.txt");
        await model.WaitForIdleAsync();
        if (!model.Patch.Contains("第一版", StringComparison.Ordinal) || !model.Patch.Contains("暂存内容", StringComparison.Ordinal))
            throw new InvalidOperationException("AOT native diff did not return both sides: " + model.Patch);
        var editor = window.DiffEditor;
        if (!editor.IsReadOnly || !editor.ShowLineNumbers || editor.Document.LineCount < 2)
            throw new InvalidOperationException("Readonly diff control was not initialized");
        editor.SelectAll();
        if (editor.SelectedText != model.Patch)
            throw new InvalidOperationException("Unicode selection changed the text");
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        editor.ScrollToLine(editor.Document.LineCount);
        if (editor.Document.LineCount < 200 || editor.TextArea.TextView.ScrollOffset.Y <= 0)
            throw new InvalidOperationException($"Long diff did not scroll: lines={editor.Document.LineCount}, offset={editor.TextArea.TextView.ScrollOffset.Y}, width={editor.Bounds.Width}, height={editor.Bounds.Height}");
        await CheckCopyAsync(editor, window.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("Clipboard window unavailable"), model.Patch);
        var originalBytes = await File.ReadAllBytesAsync(Path.Combine(WorkspacePath!, "程序.txt"));
        model.SideBySide = true;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        if (!window.LeftDiffEditor.IsVisible || !window.RightDiffEditor.IsVisible || editor.IsVisible ||
            !window.LeftDiffEditor.Text.Contains("第一版", StringComparison.Ordinal) || !window.RightDiffEditor.Text.Contains("暂存内容", StringComparison.Ordinal))
            throw new InvalidOperationException("AOT side-by-side bindings did not preserve comparison sources");
        await CheckCopyAsync(window.RightDiffEditor, window.TryGetPlatformHandle()!.Handle, model.RightPatch);
        model.SideBySide = false;
        var afterSwitch = await File.ReadAllBytesAsync(Path.Combine(WorkspacePath!, "程序.txt"));
        if (!originalBytes.AsSpan().SequenceEqual(afterSwitch))
            throw new InvalidOperationException("Diff view switch changed working bytes");

        await model.WaitForIdleAsync();
        model.Choices.First(row => row.File.Path == "程序.txt").IsChecked = true;
        model.StageChecked.Execute(null);
        await model.WaitForIdleAsync();
        if (!model.Files.Any(file => file.Path == "程序.txt" && file.Staged))
            throw new InvalidOperationException("AOT staging did not persist inclusion intent");
        await model.WaitForIdleAsync();
        model.UnstageChecked.Execute(null);
        await model.WaitForIdleAsync();
        if (model.Files.Any(file => file.Path == "程序.txt" && file.Staged))
            throw new InvalidOperationException("AOT unstage failed");
        await model.WaitForIdleAsync();
        await model.StageSelectedAsync(true);
        await File.WriteAllTextAsync(Path.Combine(WorkspacePath!, "程序.txt"), "暂存后继续编辑\n");
        model.Message = "NativeAOT 只提交选择的文件";
        await model.WaitForIdleAsync();
        await model.CommitStagedAsync();
        if (model.Message.Length != 0 || model.Files.Any(file => file.Path == "程序.txt") ||
            !model.Files.Any(file => file.Path == "保留.txt" && !file.Staged))
            throw new InvalidOperationException("AOT commit did not preserve selected/unselected semantics: " + model.Status);
        if (await File.ReadAllTextAsync(Path.Combine(WorkspacePath!, "保留.txt")) != "未选的修改\n")
            throw new InvalidOperationException("AOT commit changed an unselected file");
        await model.WaitForIdleAsync();
        model.RefreshHistory.Execute(null);
        await model.WaitForIdleAsync();
        if (model.History.Count < 2 || model.History[0].Message != "NativeAOT 只提交选择的文件")
            throw new InvalidOperationException("AOT history did not load actual revisions: " + model.HistoryStatus);
        model.SelectedPage = 1;
        model.SelectedHistory = model.History[0];
        await model.WaitForIdleAsync();
        model.SelectedHistoryFile = model.HistoryFiles.First(file => file.Path == "程序.txt");
        await model.WaitForIdleAsync();
        if (!model.HistoryRight.Contains("暂存后继续编辑", StringComparison.Ordinal) ||
            !model.HistoryLeft.Contains("第一版", StringComparison.Ordinal) || window.HistoryDiffEditor.Text != model.HistoryPatch)
            throw new InvalidOperationException("AOT parent comparison or history bindings did not show committed sources: " + model.HistoryPatch);
        model.SelectedHistoryBase = model.HistoryBases.First(choice => choice.Revision == model.History[1].Revision);
        await model.WaitForIdleAsync();
        if (!model.HistoryComparisonLabel.Contains(model.History[1].Revision, StringComparison.Ordinal))
            throw new InvalidOperationException("AOT historical comparison labels did not identify selected base");
        // Exercise validation without committing an empty staging selection.
        model.Message = "空暂存应失败";
        await model.WaitForIdleAsync();
        await model.CommitStagedAsync();
        if (model.Message.Length == 0 || !model.Feedback.Contains("没有可提交", StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid commit did not preserve its draft");
        model.NewBranchName = "aot-feature";
        await model.WaitForIdleAsync();
        model.CreateBranch.Execute(null);
        await model.WaitForIdleAsync();
        if (!model.Feedback.Contains("本地修改", StringComparison.Ordinal))
            throw new InvalidOperationException("Dirty branch creation was not blocked: " + model.Feedback);
        model.SelectedFile = model.Files.First(file => file.Path == "保留.txt");
        await model.WaitForIdleAsync();
        await model.StageSelectedAsync(true);
        model.Message = "保存其他本地修改";
        await model.WaitForIdleAsync();
        await model.CommitStagedAsync();
        await model.WaitForIdleAsync();
        model.RefreshHistory.Execute(null);
        await model.WaitForIdleAsync();
        var origin = model.History[0].Revision;
        model.SelectedPage = 2;
        await model.WaitForIdleAsync();
        model.CreateBranch.Execute(null);
        await model.WaitForIdleAsync();
        if (!model.Branches.Any(branch => branch.Name == "aot-feature" && branch.IsCurrent) || model.History[0].Revision != origin)
            throw new InvalidOperationException("AOT create-and-switch did not preserve its current revision: " + model.Status);
        model.SelectedBranch = model.Branches.First(branch => branch.Name == "main");
        await model.WaitForIdleAsync();
        model.SwitchBranch.Execute(null);
        await model.WaitForIdleAsync();
        if (!model.Branches.Any(branch => branch.Name == "main" && branch.IsCurrent))
            throw new InvalidOperationException("AOT switch did not reconcile current branch: " + model.Status);
        var invalid = Path.Combine(WorkspacePath!, "无效 目录");
        Directory.CreateDirectory(Path.Combine(invalid, ".lore"));
        await model.OpenPathAsync(invalid);
        if (!model.Feedback.Contains("操作失败", StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid native repository was reported as opened");
        await model.WaitForIdleAsync();
    }

    private static async Task CheckInputClipboardAsync(MainWindow window)
    {
        var input = window.RootPathInput;
        var owner = window.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("Clipboard window unavailable");
        using var backup = await ClipboardSmokeState.CaptureAsync(owner);
        var original = input.Text;
        var start = input.SelectionStart;
        var end = input.SelectionEnd;
        const string sample = @"G:\中文 工作目录\程序.txt";
        try
        {
            input.Text = sample;
            input.SelectAll();
            await input.CopySelectionAsync(false);
            if (await backup.ReadTextAsync() != sample || input.Text != sample)
                throw new InvalidOperationException("AOT text input copy changed Unicode text");
            await input.CopySelectionAsync(true);
            if (input.Text != "") throw new InvalidOperationException("AOT text input cut failed");
            await input.PasteSelectionAsync();
            if (input.Text != sample) throw new InvalidOperationException("AOT text input paste changed Unicode path");
        }
        finally
        {
            input.Text = original;
            input.SelectionStart = start;
            input.SelectionEnd = end;
            await backup.RestoreAsync();
        }
    }

    private static async Task CheckCopyAsync(DiffTextEditor editor, nint owner, string expected)
    {
        using var backup = await ClipboardSmokeState.CaptureAsync(owner);
        Exception? copyFailure = null;
        try
        {
            editor.SelectAll();
            if (editor.SelectionLength != expected.Length)
                throw new InvalidOperationException($"Copy selection changed: expected length={expected.Length}, selection={editor.SelectionLength}");
            await editor.CopySelectionAsync();
            var text = await backup.ReadTextAsync();
            if (text != expected.Replace("\r\n", "\n").Replace("\n", Environment.NewLine))
                throw new InvalidOperationException($"Readonly editor copy mismatch: selection={editor.SelectionLength}, expected={expected.Replace("\r\n", "\n").Replace("\n", Environment.NewLine).Length}, copied={text?.Length.ToString() ?? "null"}");
        }
        catch (Exception error) { copyFailure = error; throw; }
        finally
        {
            try
            {
                await backup.RestoreAsync();
            }
            catch (Exception error) { throw new InvalidOperationException($"Clipboard restore failed, HRESULT={error.HResult:X8}; original check: {copyFailure?.Message ?? "passed"}", error); }
        }
    }

}
