namespace SourceLore.Core.Workspaces;

public sealed record WorkspaceInvalidation(IReadOnlyList<string> Paths, bool RequiresScan);
public interface IDirtyWorkspaceBackend
{
    Task UpdateDirtyAsync(WorkspaceIdentity workspace, IReadOnlyList<string> paths);
}
public interface IWorkspaceMonitor : IAsyncDisposable { }
public delegate IWorkspaceMonitor WorkspaceMonitorFactory(WorkspaceIdentity workspace, Func<WorkspaceInvalidation, Task> changed);
