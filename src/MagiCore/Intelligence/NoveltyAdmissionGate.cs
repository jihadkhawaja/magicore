namespace MagiCore;

public sealed class NoveltyAdmissionGate : IAdmissionGate
{
    private readonly double maxOverlapThreshold;

    public NoveltyAdmissionGate(double maxOverlapThreshold = 0.90)
    {
        this.maxOverlapThreshold = maxOverlapThreshold;
    }

    public Task<MemoryAdmissionDecision> EvaluateAsync(
        MemoryAdmissionContext context,
        IReadOnlyList<Memory> existingMemories,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(context);
        if (existingMemories.Count == 0)
        {
            return Task.FromResult(new MemoryAdmissionDecision(true, 1.0, "Initial memory admission."));
        }

        var candidateTokens = Tokenize(context.Text);
        if (candidateTokens.Count == 0)
        {
            return Task.FromResult(new MemoryAdmissionDecision(false, 0.0, "Empty input tokens."));
        }

        foreach (var existing in existingMemories)
        {
            var existingTokens = Tokenize(existing.Text);
            if (existingTokens.Count == 0) continue;

            var intersection = candidateTokens.Intersect(existingTokens, StringComparer.OrdinalIgnoreCase).Count();
            var union = candidateTokens.Union(existingTokens, StringComparer.OrdinalIgnoreCase).Count();
            var jaccard = union == 0 ? 0.0 : (double)intersection / union;

            if (jaccard >= maxOverlapThreshold)
            {
                return Task.FromResult(new MemoryAdmissionDecision(
                    IsAdmitted: false,
                    ConfidenceScore: 1.0 - jaccard,
                    Reason: $"Memory overlaps {jaccard:P0} with existing memory '{existing.Id}' (exceeds novelty threshold {maxOverlapThreshold:P0})."));
            }
        }

        return Task.FromResult(new MemoryAdmissionDecision(true, 1.0, "Novelty check passed."));
    }

    private static HashSet<string> Tokenize(string text) =>
        text.Split([' ', '\t', '\r', '\n', '.', ',', '!', '?', ';', ':', '-', '(', ')', '"', '\''],
                   StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant())
            .Where(t => t.Length > 2)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
