using SourceLore.Core.Workspaces;

namespace SourceLore.Core.Tests;

public class WorkspaceSessionsTests
{
    [Fact]
    public void NativeInstanceIdentityDeduplicatesAliasesButIsolatesDifferentClones()
    {
        var sessions = new WorkspaceSessions();
        var first = sessions.GetOrAdd(new("instance-1", "C:\\Repo", "repo", "lore://server/repo"));
        var alias = sessions.GetOrAdd(new("INSTANCE-1", "C:\\repo\\.", "repo", "lore://server/repo"));
        var clone = sessions.GetOrAdd(new("instance-2", "C:\\Clone", "repo", "lore://server/repo"));
        first.CommitDraft = "尚未提交的说明";
        Assert.Same(first, alias);
        Assert.NotSame(first, clone);
        Assert.Empty(clone.CommitDraft);
        Assert.Equal("尚未提交的说明", alias.CommitDraft);
        Assert.NotSame(first.Scheduler, clone.Scheduler);
        Assert.True(sessions.Remove(first.Identity.InstanceId));
        Assert.Same(clone, sessions.GetOrAdd(clone.Identity));
    }
}
