namespace MagiCore;

public sealed class CrossEncoderReranker : IMemoryReranker
{
    private readonly ICrossEncoderScorer scorer;
    private readonly bool normalizeScores;

    public CrossEncoderReranker(ICrossEncoderScorer scorer, bool normalizeScores = true)
    {
        Guard.NotNull(scorer);
        this.scorer = scorer;
        this.normalizeScores = normalizeScores;
    }

    public async Task<IReadOnlyList<SearchResult>> RerankAsync(string query, IReadOnlyList<SearchResult> candidates, int topK, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(query);
        Guard.NotNull(candidates);
        if (topK < 0) throw new ArgumentOutOfRangeException(nameof(topK));
        if (candidates.Count == 0 || topK == 0) return [];

        var scores = await scorer.ScoreAsync(query, candidates.Select(candidate => candidate.Memory.Text).ToArray(), cancellationToken);
        if (scores.Count != candidates.Count) throw new InvalidOperationException("The cross-encoder returned a different number of scores than input documents.");

        return candidates.Select((candidate, index) => HostedRerankerClient.WithRerankScore(candidate, Normalize(scores[index])))
            .OrderByDescending(candidate => candidate.Score)
            .Take(topK)
            .ToArray();
    }

    private double Normalize(double score)
    {
        if (!Compatibility.IsFinite(score)) return 0;
        return normalizeScores ? 1 / (1 + Math.Exp(-score)) : score;
    }
}