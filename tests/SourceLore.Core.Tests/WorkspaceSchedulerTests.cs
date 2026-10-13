using SourceLore.Core.Operations;

namespace SourceLore.Core.Tests;

public class WorkspaceSchedulerTests
{
    [Fact]
    public async Task SameWorkspaceWaitsWhileAnotherWorkspaceCanProceed()
    {
        var scheduler = new WorkspaceScheduler();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        var first = scheduler.RunAsync(async () => { entered.SetResult(); await release.Task.WaitAsync(token); }, token);
        await entered.Task;
        var secondEntered = false;
        var second = scheduler.RunAsync(() => { secondEntered = true; return Task.CompletedTask; }, token);
        Assert.False(secondEntered);
        Assert.Equal(42, await new WorkspaceScheduler().RunAsync(() => Task.FromResult(42), token));
        release.SetResult();
        await Task.WhenAll(first, second);
        Assert.True(secondEntered);
    }

    [Fact]
    public async Task FailureReleasesGateAndQueuedCancellationDoesNotCancelRunningWork()
    {
        var scheduler = new WorkspaceScheduler();
        var token = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<IOException>(() => scheduler.RunAsync(() => Task.FromException(new IOException()), token));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = scheduler.RunAsync(() => release.Task.WaitAsync(token), token);
        using var cancel = new CancellationTokenSource();
        var queued = scheduler.RunAsync(() => Task.CompletedTask, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.False(running.IsCompleted);
        release.SetResult();
        await running;
        await scheduler.RunAsync(() => Task.CompletedTask, token);
    }

    [Fact]
    public void NewSelectionInvalidatesOldResults()
    {
        var generation = new QueryGeneration();
        var older = generation.Advance();
        var current = generation.Advance();
        Assert.False(generation.IsCurrent(older));
        Assert.True(generation.IsCurrent(current));
    }
}
