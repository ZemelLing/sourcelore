using Avalonia;

namespace SourceLore.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--smoke-ui")
            UiSmoke.ReportPath = Path.GetFullPath(args[1]);
        if (args.Length == 3 && args[0] == "--smoke-workspace")
        {
            UiSmoke.WorkspacePath = Path.GetFullPath(args[1]);
            UiSmoke.ReportPath = Path.GetFullPath(args[2]);
        }
        if (args.Length == 2 && args[0] == "--smoke-dependency-error")
        {
            UiSmoke.ExpectDependencyError = true;
            UiSmoke.ReportPath = Path.GetFullPath(args[1]);
        }
        if (args.Length == 5 && args[0] == "--smoke-online")
        {
            UiSmoke.OnlineRemote = args[1];
            UiSmoke.WorkspacePath = Path.GetFullPath(args[2]);
            UiSmoke.SecondWorkspacePath = Path.GetFullPath(args[3]);
            UiSmoke.ReportPath = Path.GetFullPath(args[4]);
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
