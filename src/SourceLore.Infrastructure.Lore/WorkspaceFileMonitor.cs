using SourceLore.Core.Workspaces;

namespace SourceLore.Infrastructure.Lore;

public sealed class WorkspaceFileMonitor : IWorkspaceMonitor
{
    private readonly FileSystemWatcher watcher;
    private readonly string root;
    private readonly Func<WorkspaceInvalidation, Task> changed;
    private readonly HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim signal = new(0, 1);
    private readonly Task pump;
    private bool scan;
    private bool disposed;

    public WorkspaceFileMonitor(string root, Func<WorkspaceInvalidation, Task> changed)
    {
        this.root = Path.GetFullPath(root);
        this.changed = changed;
        watcher = new(this.root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
        watcher.Changed += (_, evt) => Notify(evt.FullPath, false);
        watcher.Created += (_, evt) => Notify(evt.FullPath, true);
        watcher.Deleted += (_, evt) => Notify(evt.FullPath, true);
        watcher.Renamed += (_, evt) => { Notify(evt.OldFullPath, true); Notify(evt.FullPath, true); };
        watcher.Error += (_, _) => RequestScan();
        pump = PumpAsync();
        watcher.EnableRaisingEvents = true;
    }

    private void Notify(string absolute, bool needsScan)
    {
        var path = Path.GetRelativePath(root, absolute).Replace('\\', '/');
        if (path == ".." || path.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(path)) return;
        if (path.Equals(".lore", StringComparison.OrdinalIgnoreCase) || path.StartsWith(".lore/", StringComparison.OrdinalIgnoreCase) ||
            path.Equals(".urc", StringComparison.OrdinalIgnoreCase) || path.StartsWith(".urc/", StringComparison.OrdinalIgnoreCase)) return;
        needsScan |= string.Equals(Path.GetFileName(path), ".loreignore", StringComparison.OrdinalIgnoreCase);
        lock (paths)
        {
            if (disposed) return;
            if (paths.Count < 1024) paths.Add(path); else needsScan = true;
            scan |= needsScan;
            Wake();
        }
    }

    // Also used after focus recovery and to exercise overflow recovery deterministically.
    public void RequestScan()
    {
        lock (paths) { if (disposed) return; scan = true; Wake(); }
    }
    private void Wake() { if (signal.CurrentCount == 0) signal.Release(); }

    private async Task PumpAsync()
    {
        try
        {
            while (true)
            {
                await signal.WaitAsync(stop.Token).ConfigureAwait(false);
                await Task.Delay(250, stop.Token).ConfigureAwait(false);
                WorkspaceInvalidation batch;
                lock (paths) { batch = new(paths.ToArray(), scan); paths.Clear(); scan = false; }
                try { await changed(batch).ConfigureAwait(false); }
                catch { RequestScan(); } // Recovery is a scan, never a claim that a failed update is clean.
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync()
    {
        lock (paths) { if (disposed) return; disposed = true; }
        watcher.Dispose(); stop.Cancel();
        await pump.ConfigureAwait(false); // An active SDK call is allowed to finish.
        stop.Dispose(); signal.Dispose();
    }
}
