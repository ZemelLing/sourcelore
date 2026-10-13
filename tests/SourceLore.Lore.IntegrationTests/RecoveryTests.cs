using SourceLore.Core.Operations;
using SourceLore.Core.Workspaces;
using SourceLore.Infrastructure.Lore.Diagnostics;
using SourceLore.Infrastructure.Lore.Persistence;

namespace SourceLore.Lore.IntegrationTests;

public class RecoveryTests
{
    [Fact]
    public async Task RestartRetainsUncertainWriteWithoutReplayingOrPersistingCredentials()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SourceLore.JournalTests", Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = new WorkspaceIdentity("instance-1", "C:\\workspace", "repo", "lore://user:test-secret@server/repo?token=test-secret");
            var tracker = new OperationTracker(new PendingOperationStore(directory), SafeDiagnostics.Describe);
            var result = await tracker.RunAsync(workspace, OperationKind.Push, "revision-1",
                () => Task.FromException(new IOException("Bearer test-secret token=test-secret lore://user:test-secret@server/path?token=test-secret")));
            Assert.False(result.Succeeded);
            Assert.True(result.NeedsReconciliation);
            Assert.DoesNotContain("test-secret", result.Error);
            var restartedStore = new PendingOperationStore(directory);
            var marker = await restartedStore.ReadAsync(workspace);
            Assert.Equal(OperationKind.Push, marker!.Kind);
            Assert.Equal("revision-1", marker.StartingRevision);
            var calls = 0;
            var restartedTracker = new OperationTracker(restartedStore, SafeDiagnostics.Describe);
            result = await restartedTracker.RunAsync(workspace, OperationKind.Push, "revision-2", () => { calls++; return Task.CompletedTask; });
            Assert.Equal(0, calls);
            Assert.True(result.NeedsReconciliation);
            var json = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(directory, "*.json")), TestContext.Current.CancellationToken);
            Assert.DoesNotContain("test-secret", json);
            await restartedStore.ClearAsync(workspace);
            Assert.Null(await restartedStore.ReadAsync(workspace));
        }
        finally
        {
            var safeRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SourceLore.JournalTests")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(directory).StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid test cleanup path");
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
