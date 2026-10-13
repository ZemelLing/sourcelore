using SourceLore.Core.Operations;

namespace SourceLore.Core.Workspaces;

public interface IRemoteWorkspaceBackend
{
    Task<WorkspaceIdentity> CloneAsync(string remoteUrl, string destination, IProgress<OperationProgress>? progress = null);
}
