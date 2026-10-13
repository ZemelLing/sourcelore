namespace SourceLore.Core.Operations;

public sealed record OperationProgress(string Message, bool IsFinal = false);

/// <summary>Keep only the latest intermediate update; the terminal update is delivered separately.</summary>
public sealed class CoalescedProgress(IProgress<OperationProgress>? target, TimeSpan interval)
{
    private readonly object gate = new();
    private OperationProgress? latest;
    private Task pump = Task.CompletedTask;
    private bool pumping, completed;

    public void Report(OperationProgress update)
    {
        if (target is null) return;
        lock (gate)
        {
            if (completed) return;
            latest = update;
            if (pumping) return;
            pumping = true;
            pump = PumpAsync();
        }
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            await Task.Delay(interval).ConfigureAwait(false);
            lock (gate)
            {
                if (completed || latest is null) { pumping = false; return; }
                var update = latest;
                latest = null;
                target!.Report(update);
            }
        }
    }

    public async Task CompleteAsync(OperationProgress terminal)
    {
        Task pending;
        lock (gate)
        {
            if (completed) return;
            completed = true;
            latest = null;
            pending = pump;
        }
        await pending.ConfigureAwait(false);
        target?.Report(terminal with { IsFinal = true });
    }
}
