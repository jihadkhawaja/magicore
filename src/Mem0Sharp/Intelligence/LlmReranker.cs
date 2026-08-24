using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Mem0Sharp;

public sealed class LlmReranker : IMemoryReranker
{
    private const int MaxInputLength = 4000;
    private static readonly Regex ScoreRegex = new(@"-?\d+(?:\.\d+)?", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly IChatClient client;
    private readonly int maxDegreeOfParallelism;

    public LlmReranker(IChatClient client, int maxDegreeOfParallelism = 4)
    {
        Guard.NotNull(client);
        if (maxDegreeOfParallelism < 1) throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism));
        this.client = client;
        this.maxDegreeOfParallelism = maxDegreeOfParallelism;
    }

    public async Task<IReadOnlyList<SearchResult>> RerankAsync(string query, IReadOnlyList<SearchResult> candidates, int topK, CancellationToken cancellationToken = default)
    {
        if (candidates.Count == 0 || topK <= 0) return [];

        var scored = new SearchResult[candidates.Count];
        await Compatibility.ForEachAsync(Enumerable.Range(0, candidates.Count), maxDegreeOfParallelism, async (index, ct) =>
        {
            var candidate = candidates[index];
            try
            {
                var response = await client.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, "Score the relevance of the document to the query from 0.0 to 1.0. Return only the number."),
                    new ChatMessage(ChatRole.User, $"Query: {query.Substring(0, Math.Min(query.Length, MaxInputLength))}\n\nDocument: {candidate.Memory.Text.Substring(0, Math.Min(candidate.Memory.Text.Length, MaxInputLength))}")
                ], cancellationToken: ct);
                var rerankScore = ParseScore(response.Text ?? string.Empty);
                var details = candidate.ScoreDetails is null
                    ? new SearchScoreDetails(candidate.Score, Reranker: rerankScore)
                    : candidate.ScoreDetails with { Reranker = rerankScore };
                scored[index] = candidate with { Score = rerankScore, ScoreDetails = details };
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                scored[index] = candidate;
            }
        }, cancellationToken);

        return scored.OrderByDescending(result => result.Score).Take(topK).ToArray();
    }

    internal static double ParseScore(string response)
    {
        var match = ScoreRegex.Match(response);
        return match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var score)
            ? Compatibility.Clamp(score, 0, 1)
            : 0.5;
    }

}