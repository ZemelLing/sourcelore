using System.Collections.Concurrent;
using SourceLore.Core.Operations;

namespace SourceLore.Core.Workspaces;

public sealed class WorkspaceSession(WorkspaceIdentity identity)
{
    public WorkspaceIdentity Identity { get; } = identity;
    public WorkspaceScheduler Scheduler { get; } = new();
    public QueryGeneration Queries { get; } = new();
    public string CommitDraft { get; set; } = "";
    public string? SelectedPath { get; set; }
    public HashSet<string> CheckedPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int SelectedPage { get; set; }
    public WorkspaceSnapshot? Snapshot { get; set; }
    public RevisionHistoryPager? HistoryPager { get; set; }
    public string? HistoryBranchId { get; set; }
    public List<RevisionEntry> History { get; } = [];
}

public sealed class WorkspaceSessions
{
    private readonly ConcurrentDictionary<string, WorkspaceSession> sessions = new(StringComparer.OrdinalIgnoreCase);
    public WorkspaceSession GetOrAdd(WorkspaceIdentity identity) => sessions.GetOrAdd(identity.InstanceId, _ => new(identity));
    public bool Remove(string instanceId) => sessions.TryRemove(instanceId, out _);
}
