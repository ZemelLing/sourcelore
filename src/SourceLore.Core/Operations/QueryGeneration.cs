namespace SourceLore.Core.Operations;

public sealed class QueryGeneration
{
    private long generation;
    public long Advance() => Interlocked.Increment(ref generation);
    public bool IsCurrent(long value) => Interlocked.Read(ref generation) == value;
}
