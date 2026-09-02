namespace Mem0Sharp;

public sealed record MemoryPageOptions
{
    public int Offset { get; init; }
    public int Limit { get; init; } = 20;
}

public sealed record MemoryPage(IReadOnlyList<Memory> Results, int Total, int Offset, int Limit);

public sealed record MemoryTimeRange(DateTimeOffset? Start = null, DateTimeOffset? End = null);

public sealed record TemporalQueryInterpretation(
    MemoryTimeRange Range,
    double Confidence,
    string? MatchedText = null);

public static class TemporalMemoryMetadata
{
    public const string ReferenceTimeKey = "mem0.reference_time";
}

public sealed record MemorySearchOptions
{
    public MemoryFilter? Filter { get; init; }
    public int TopK { get; init; } = 20;
    public double Threshold { get; init; } = 0.1;
    public bool Rerank { get; init; }
    public bool Explain { get; init; }
    public bool Hybrid { get; init; } = true;
    public MemoryBehavior? Behavior { get; init; }
    public bool IncludeNonFactual { get; init; }
    public double RecencyBias { get; init; }
    public TimeSpan? FreshnessWindow { get; init; }
    public MemoryTimeRange? TimeRange { get; init; }
    public bool EnableTemporalSearch { get; init; }
    public DateTimeOffset? ReferenceTime { get; init; }
    public double MinimumTemporalConfidence { get; init; } = 0.8;
    public bool IncludeUndatedMemories { get; init; } = true;
}

public sealed record SearchScoreDetails(
    double Semantic,
    double Keyword = 0,
    double Entity = 0,
    double? Reranker = null,
    double Raw = 0,
    double MaxPossible = 1,
    double Threshold = 0);

public sealed record SearchResult(Memory Memory, double Score, SearchScoreDetails? ScoreDetails = null);