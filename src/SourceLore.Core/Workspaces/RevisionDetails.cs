namespace SourceLore.Core.Workspaces;

public sealed record RevisionFileChange(string Path, string Action, bool IsFile, string? FromPath)
{
    public string Label => $"{Action} {Path}";
}
public sealed record RevisionChoice(string Revision, string Label);
public interface IRevisionDetailBackend
{
    Task<IReadOnlyList<RevisionFileChange>> ChangedFilesAsync(WorkspaceIdentity workspace, string revision, string parent);
}
