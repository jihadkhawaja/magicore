namespace Mem0Sharp.Evaluation;

internal sealed record CapabilityCheckResult
{
    public required string Feature { get; init; }
    public required string Category { get; init; }
    public required string Status { get; init; }
    public required string Evidence { get; init; }
    public double DurationMs { get; init; }
}

internal sealed record CapabilityReport
{
    public int Passed => Checks.Count(check => check.Status == "PASS");
    public int Failed => Checks.Count(check => check.Status == "FAIL");
    public int Skipped => Checks.Count(check => check.Status == "SKIP");
    public required IReadOnlyList<CapabilityCheckResult> Checks { get; init; }
}

internal sealed record QuestionResult
{
    public required string QuestionId { get; init; }
    public required string Category { get; init; }
    public required string Question { get; init; }
    public required string ExpectedAnswer { get; init; }
    public string? GeneratedAnswer { get; init; }
    public string? JudgeVerdict { get; init; }
    public string? JudgeReasoning { get; init; }
    public bool? Correct { get; init; }
    public double? F1 { get; init; }
    public double? Bleu1 { get; init; }
    public bool RetrievalHit { get; init; }
    public int RetrievedCount { get; init; }
    public double SearchLatencyMs { get; init; }
    public IReadOnlyList<string> RetrievedMemories { get; init; } = [];
}

internal sealed record CategoryMetrics
{
    public required string Category { get; init; }
    public int Questions { get; init; }
    public int Correct { get; init; }
    public double Accuracy => Questions == 0 ? 0 : (double)Correct / Questions;
    public double? AccuracyLower95 { get; init; }
    public double? AccuracyUpper95 { get; init; }
    public int RetrievalQuestions { get; init; }
    public int RetrievalHits { get; init; }
    public double RetrievalHitRate { get; init; }
    public double? RetrievalHitRateLower95 { get; init; }
    public double? RetrievalHitRateUpper95 { get; init; }
}

internal sealed record ScenarioReport
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public int MemoriesStored { get; init; }
    public double IngestSeconds { get; init; }
    public int Questions { get; init; }
    public int Judged { get; init; }
    public int Correct { get; init; }
    public double? Accuracy => Judged == 0 ? null : (double)Correct / Judged;
    public double? AccuracyLower95 { get; init; }
    public double? AccuracyUpper95 { get; init; }
    public double? MeanF1 { get; init; }
    public double? MeanBleu1 { get; init; }
    public int RetrievalQuestions { get; init; }
    public int RetrievalHits { get; init; }
    public double RetrievalHitRate { get; init; }
    public double? RetrievalHitRateLower95 { get; init; }
    public double? RetrievalHitRateUpper95 { get; init; }
    public double MeanRetrievedCount { get; init; }
    public double MeanSearchLatencyMs { get; init; }
    public IReadOnlyList<CategoryMetrics> Categories { get; init; } = [];
    public IReadOnlyList<QuestionResult> Results { get; init; } = [];
    public string? Error { get; init; }
}

internal sealed record EvaluationReport
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Mode { get; init; }
    public string? ChatModel { get; init; }
    public string? EmbeddingModel { get; init; }
    public string? JudgeModel { get; init; }
    public required string Store { get; init; }
    public required string Dataset { get; init; }
    public int ConversationCount { get; init; }
    public int QuestionCount { get; init; }
    public bool SyntheticDataset { get; init; }
    public CapabilityReport? Capabilities { get; init; }
    public required IReadOnlyList<ScenarioReport> ScenarioReports { get; init; }
}
