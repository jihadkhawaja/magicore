namespace MagiCore;

public sealed class HeuristicConsolidationVerifier : IConsolidationVerifier
{
    private readonly double minCoverage;

    public HeuristicConsolidationVerifier(double minCoverage = 0.3)
    {
        this.minCoverage = minCoverage;
    }

    public Task<ConsolidationVerificationResult> VerifyAsync(
        IReadOnlyList<Memory> sourceMemories,
        string consolidatedSummary,
        CancellationToken cancellationToken = default)
    {
        if (sourceMemories.Count == 0 || string.IsNullOrWhiteSpace(consolidatedSummary))
        {
            return Task.FromResult(new ConsolidationVerificationResult(false, 0.0, "Empty source memories or summary."));
        }

        var sourceTokens = sourceMemories
            .SelectMany(m => Tokenize(m.Text))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var summaryTokens = Tokenize(consolidatedSummary);
        if (summaryTokens.Count == 0)
        {
            return Task.FromResult(new ConsolidationVerificationResult(false, 0.0, "Summary contains no tokens."));
        }

        var matched = summaryTokens.Count(t => sourceTokens.Contains(t));
        var ratio = (double)matched / summaryTokens.Count;

        var isValid = ratio >= minCoverage;
        var reason = isValid
            ? $"Heuristic token coverage {ratio:P1} meets threshold {minCoverage:P1}."
            : $"Heuristic token coverage {ratio:P1} is below threshold {minCoverage:P1}. Potential drift or unsupported content.";

        return Task.FromResult(new ConsolidationVerificationResult(isValid, ratio, reason));
    }

    private static List<string> Tokenize(string text) =>
        text.Split([' ', '\t', '\r', '\n', '.', ',', '!', '?', ';', ':', '-', '(', ')', '"', '\''],
                   StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2)
            .ToList();
}
