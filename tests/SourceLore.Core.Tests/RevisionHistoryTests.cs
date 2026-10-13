using SourceLore.Core.Workspaces;

namespace SourceLore.Core.Tests;

public class RevisionHistoryTests
{
    [Fact]
    public async Task FixedDagTipKeepsBothParentsAndRetriesWithoutOmittingRecords()
    {
        var backend = new Backend();
        var pager = new RevisionHistoryPager(backend, new("workspace", "root", "name", ""), "merge");
        var first = await pager.NextAsync(2);
        Assert.Equal(new[] { "merge", "main" }, first.Select(entry => entry.Revision));
        backend.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => pager.NextAsync(2));
        backend.Fail = false;
        var second = await pager.NextAsync(2);
        Assert.Equal(new[] { "base", "other" }, second.Select(entry => entry.Revision));
        Assert.Equal("merge", pager.Tip);
        Assert.False(pager.HasMore);
        Assert.Empty(await pager.NextAsync());
    }

    private sealed class Backend : IHistoryBackend
    {
        public bool Fail { get; set; }
        public Task<RevisionEntry> RevisionAsync(WorkspaceIdentity workspace, string revision)
        {
            if (Fail && revision == "other") throw new IOException("offline missing revision");
            string[] parents = revision switch { "merge" => ["main", "other"], "main" or "other" => ["base"], _ => [] };
            return Task.FromResult(new RevisionEntry(revision, 0, parents, revision, "", null));
        }
    }
}
