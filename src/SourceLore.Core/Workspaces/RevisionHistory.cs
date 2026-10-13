namespace SourceLore.Core.Workspaces;

public sealed record RevisionEntry(string Revision, ulong Number, IReadOnlyList<string> Parents,
    string Message, string Author, DateTimeOffset? Timestamp)
{
    public string Summary => Message.Split('\n', 2)[0];
    public string TimeLabel => Timestamp?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "时间未知";
}

public interface IHistoryBackend
{
    Task<RevisionEntry> RevisionAsync(WorkspaceIdentity workspace, string revision);
}

// Walk an immutable DAG from a fixed tip. Each page performs at most pageSize queries.
// Only commit traversal state after a whole page succeeds, so retry never skips failed records.
public sealed class RevisionHistoryPager(IHistoryBackend backend, WorkspaceIdentity workspace, string tip)
{
    private List<string> frontier = IsRevision(tip) ? [tip] : [];
    private HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim gate = new(1, 1);
    public string Tip { get; } = tip;
    public bool HasMore => frontier.Count > 0;
    public static bool IsRevision(string value) => value.Length > 0 && value.Any(character => character != '0');

    public async Task<IReadOnlyList<RevisionEntry>> NextAsync(int pageSize = 100)
    {
        if (pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(pageSize));
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var pending = new List<string>(frontier);
            var seen = new HashSet<string>(visited, StringComparer.OrdinalIgnoreCase);
            var result = new List<RevisionEntry>();
            while (pending.Count > 0 && result.Count < pageSize)
            {
                var revision = pending[^1];
                pending.RemoveAt(pending.Count - 1);
                if (!seen.Add(revision)) continue;
                var entry = await backend.RevisionAsync(workspace, revision).ConfigureAwait(false);
                if (!string.Equals(entry.Revision, revision, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("历史返回的版本与请求身份不符。");
                result.Add(entry);
                foreach (var parent in entry.Parents.Reverse())
                    if (IsRevision(parent) && !seen.Contains(parent)) pending.Add(parent);
            }
            frontier = pending.Where(value => !seen.Contains(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            visited = seen;
            return result;
        }
        finally { gate.Release(); }
    }
}
