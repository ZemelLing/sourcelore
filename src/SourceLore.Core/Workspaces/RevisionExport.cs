namespace SourceLore.Core.Workspaces;

public interface IRevisionExportBackend
{
    Task<RevisionExport> ExportRevisionAsync(WorkspaceIdentity workspace, string path, string revision);
}
public sealed class RevisionExport(string path, Action release) : IDisposable
{
    public string Path { get; } = path;
    private Action? cleanup = release;
    public void Dispose() => Interlocked.Exchange(ref cleanup, null)?.Invoke();
}
