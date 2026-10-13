namespace SourceLore.Core.Workspaces;

public sealed record BranchEntry(string Id, string Name, string Latest, bool IsCurrent)
{
    public string Label => Name + (IsCurrent ? "  · 当前" : "");
}

public interface IBranchBackend
{
    Task<IReadOnlyList<BranchEntry>> BranchesAsync(WorkspaceIdentity workspace);
    Task CreateBranchAsync(WorkspaceIdentity workspace, string name);
    Task SwitchBranchAsync(WorkspaceIdentity workspace, string name);
}
