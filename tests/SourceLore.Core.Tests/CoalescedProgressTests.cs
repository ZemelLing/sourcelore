using SourceLore.Core.Operations;

namespace SourceLore.Core.Tests;

public class CoalescedProgressTests
{
    [Fact]
    public async Task BurstIsCoalescedAndCompletionCannotBeLostOrOverwritten()
    {
        var observed = new List<OperationProgress>();
        var progress = new CoalescedProgress(new InlineProgress(update => observed.Add(update)), TimeSpan.FromMilliseconds(20));
        for (var index = 0; index < 10_000; index++) progress.Report(new("progress " + index));
        await progress.CompleteAsync(new("finished"));
        progress.Report(new("late update"));
        Assert.True(observed.Count < 100);
        Assert.True(observed[^1].IsFinal);
        Assert.Equal("finished", observed[^1].Message);
        Assert.DoesNotContain(observed, update => update.Message == "late update");
        await progress.CompleteAsync(new("duplicate completion"));
        Assert.Single(observed, update => update.IsFinal);
    }

    private sealed class InlineProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }
}
