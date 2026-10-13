using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SourceLore.App.ViewModels;
using SourceLore.App.Views;
using SourceLore.Infrastructure.Lore;
using SourceLore.Infrastructure.Lore.Diagnostics;
using SourceLore.Infrastructure.Lore.Persistence;

namespace SourceLore.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            LoreRuntime? runtime = null;
            MainWindowViewModel model;
            try
            {
                runtime = new LoreRuntime();
                UiSmoke.NativeVersion = runtime.Version;
                var journalDirectory = UiSmoke.WorkspacePath is not null
                    ? Path.Combine(Path.GetTempPath(), "SourceLore.AppSmoke", Guid.NewGuid().ToString("N"))
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SourceLore", "pending");
                if (UiSmoke.WorkspacePath is not null) UiSmoke.JournalDirectory = journalDirectory;
                var recentPath = UiSmoke.WorkspacePath is not null
                    ? Path.Combine(journalDirectory, "recent.json")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SourceLore", "recent.json");
                model = new(new LoreWorkspaceBackend(runtime), SafeDiagnostics.Describe, journal: new PendingOperationStore(journalDirectory), recentStore: new RecentWorkspaceStore(recentPath),
                    monitorFactory: (owner, changed) => new WorkspaceFileMonitor(owner.RootPath, async batch =>
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => changed(batch));
                    }), openBrowser: url => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }),
                    openFile: file =>
                    {
                        var editor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
                        if (!File.Exists(editor)) throw new InvalidOperationException("未找到 Windows 记事本；请安装外部文本编辑器后再打开。");
                        var start = new System.Diagnostics.ProcessStartInfo(editor) { UseShellExecute = true };
                        start.ArgumentList.Add(file);
                        System.Diagnostics.Process.Start(start);
                    });
            }
            catch (Exception error) { model = new(startupError: SafeDiagnostics.Describe(error)); }
            desktop.MainWindow = new MainWindow { DataContext = model };
            var closing = false;
            desktop.MainWindow.Closing += async (_, args) =>
            {
                if (closing || runtime is null) return;
                args.Cancel = true;
                closing = true;
                desktop.MainWindow.IsEnabled = false;
                await model.StopMonitoringAsync();
                await model.WaitForIdleAsync();
                model.ReleaseExternalSnapshots();
                await runtime.WhenIdleAsync();
                runtime.Dispose();
                desktop.MainWindow.Close();
            };
            desktop.Exit += (_, _) =>
            {
                runtime?.Dispose();
                if (UiSmoke.JournalDirectory is not null && Directory.Exists(UiSmoke.JournalDirectory))
                {
                    var prefix = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SourceLore.AppSmoke")) + Path.DirectorySeparatorChar;
                    if (Path.GetFullPath(UiSmoke.JournalDirectory).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        Directory.Delete(UiSmoke.JournalDirectory, recursive: true);
                }
            };
            desktop.MainWindow.Opened += async (_, _) =>
            {
                await model.InitializeRecentAsync();
                if (UiSmoke.ReportPath is not null) UiSmoke.Run(desktop);
            };
            desktop.MainWindow.Activated += async (_, _) => await model.RecoverFocusAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
