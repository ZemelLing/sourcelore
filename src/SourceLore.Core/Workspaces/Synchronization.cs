using SourceLore.Core.Operations;

namespace SourceLore.Core.Workspaces;

public sealed record RemoteTarget(string Address, string DisplayAddress, string BranchId, string BranchName, string LocalRevision, string RepositoryId = "", string LocalHeadRevision = "")
{
    public string PushRevision => LocalHeadRevision.Length == 0 ? LocalRevision : LocalHeadRevision;
}
public sealed record RemoteAnchor(RemoteTarget Target, string Revision, bool BranchExists, bool LocalAhead, bool RemoteAhead, DateTimeOffset VerifiedAt);
public sealed record AbortResult(WorkspaceSnapshot Snapshot, string BackupPath);
public interface ISynchronizationBackend
{
    Task<RemoteTarget> ReadTargetAsync(WorkspaceIdentity workspace);
    Task<RemoteAnchor> VerifyRemoteAsync(WorkspaceIdentity workspace);
    Task<RemoteAnchor> PushAsync(WorkspaceIdentity workspace, RemoteTarget target, IProgress<OperationProgress>? progress = null);
    Task<WorkspaceSnapshot> SyncAsync(WorkspaceIdentity workspace, RemoteAnchor target, IProgress<OperationProgress>? progress = null);
    Task<WorkspaceSnapshot> ResolveAsync(WorkspaceIdentity workspace, string path);
    Task<WorkspaceSnapshot> ContinueMergeAsync(WorkspaceIdentity workspace, string message);
    Task<AbortResult> AbortMergeAsync(WorkspaceIdentity workspace, string expectedRevision, string expectedMergedRevision);
}
