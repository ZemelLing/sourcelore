using SourceLore.Core.Operations;

namespace SourceLore.Core.Workspaces;

public sealed record WorkspaceIdentity(string InstanceId, string RootPath, string Name, string RemoteUrl);

public sealed record FileChange(string Path, ulong Size, string Action, bool IsDirectory,
    bool Dirty, bool Staged, bool Conflict, bool Unresolved, string? FromPath)
{
    public string ConflictLabel => Unresolved ? "未解决" : "已标记解决，等待合并提交";
}

public sealed record WorkspaceSnapshot(string Revision, string BranchName, string MergedRevision,
    IReadOnlyList<FileChange> Files, DateTimeOffset VerifiedAt, string StagedRevision = "")
{
    public bool HasConflicts => Files.Any(file => file.Conflict);
    public bool HasUnresolvedConflicts => Files.Any(file => file.Unresolved);
    public bool HasMerge => HasConflicts || HasUnresolvedConflicts || RevisionHistoryPager.IsRevision(MergedRevision) &&
        (StagedRevision.Length == 0 || RevisionHistoryPager.IsRevision(StagedRevision) && StagedRevision != Revision);
}

public interface IWorkspaceBackend
{
    Task<WorkspaceIdentity> OpenAsync(string path);
    Task<WorkspaceSnapshot> StatusAsync(WorkspaceIdentity workspace, bool scan, IProgress<OperationProgress>? progress = null);
    Task StageAsync(WorkspaceIdentity workspace, IReadOnlyList<string> paths, bool staged);
    Task CommitAsync(WorkspaceIdentity workspace, string message);
    Task<string> DiffAsync(WorkspaceIdentity workspace, string path);
}
