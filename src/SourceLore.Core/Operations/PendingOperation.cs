using SourceLore.Core.Workspaces;
using System.Collections.Concurrent;

namespace SourceLore.Core.Operations;

public enum OperationKind { Stage, Unstage, Commit, Clone, Push, Sync, Resolve, Abort, CreateBranch, SwitchBranch }

public sealed record PendingOperation(string InstanceId, string RootPath, OperationKind Kind,
    string StartingRevision, DateTimeOffset StartedAt);

public interface IPendingOperationStore
{
    Task<PendingOperation?> ReadAsync(WorkspaceIdentity workspace);
    Task WriteAsync(PendingOperation operation);
    Task ClearAsync(WorkspaceIdentity workspace);
}

public sealed record OperationResult(bool Succeeded, bool NeedsReconciliation, string? Error);

/// <summary>Record intent only. An interrupted write is never replayed by this class.</summary>
public sealed class OperationTracker(IPendingOperationStore store, Func<Exception, string> safeError)
{
    private readonly ConcurrentDictionary<string, WorkspaceScheduler> schedulers = new(StringComparer.OrdinalIgnoreCase);

    public Task<OperationResult> RunAsync(WorkspaceIdentity workspace, OperationKind kind,
        string startingRevision, Func<Task> operation) => schedulers.GetOrAdd(workspace.InstanceId, _ => new())
        .RunAsync(() => RunTrackedAsync(workspace, kind, startingRevision, operation));

    private async Task<OperationResult> RunTrackedAsync(WorkspaceIdentity workspace, OperationKind kind,
        string startingRevision, Func<Task> operation)
    {
        if (await store.ReadAsync(workspace).ConfigureAwait(false) is not null)
            return new(false, true, "上次操作的结果尚待核对，请刷新并检查当前版本后再操作。");
        await store.WriteAsync(new(workspace.InstanceId, workspace.RootPath, kind, startingRevision, DateTimeOffset.UtcNow)).ConfigureAwait(false);
        try
        {
            await operation().ConfigureAwait(false);
            await store.ClearAsync(workspace).ConfigureAwait(false);
            return new(true, false, null);
        }
        catch (Exception ex)
        {
            // Keep the marker even when the error looked deterministic: only the backend can establish actual state.
            return new(false, true, safeError(ex));
        }
    }
}
