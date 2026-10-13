namespace SourceLore.Core.Workspaces;

public sealed record RecentWorkspace(string RootPath, string Name);

public interface IRecentWorkspaceStore
{
    Task<IReadOnlyList<RecentWorkspace>> ReadAsync();
    Task WriteAsync(IReadOnlyList<RecentWorkspace> entries);
}
