namespace MagiCore;

public sealed record MemoryOptions
{
    public int DefaultTopK { get; init; } = 5;
    public double MinimumScore { get; init; } = 0.05;
    public int MaxCandidateCount { get; init; } = 1000;
    public bool EnableHybridSearch { get; init; } = true;
}