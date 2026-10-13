namespace SourceLore.Core.Operations;

/// <summary>Serialize all native calls for one working directory, including metadata reads and scans.</summary>
public sealed class WorkspaceScheduler
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken queuedCancellation = default)
    {
        // Cancellation can remove queued work only. Once started, resources stay alive until native END.
        await gate.WaitAsync(queuedCancellation).ConfigureAwait(false);
        try { return await operation().ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    public Task RunAsync(Func<Task> operation, CancellationToken queuedCancellation = default) =>
        RunAsync(async () => { await operation().ConfigureAwait(false); return true; }, queuedCancellation);
}
