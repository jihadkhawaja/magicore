using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MagiCore;

internal static class HostedRerankerClient
{
    public static async Task<IReadOnlyList<SearchResult>> RerankAsync(HttpClient httpClient, Uri endpoint, string apiKey, object payload, IReadOnlyList<SearchResult> candidates, int topK, string providerName, CancellationToken cancellationToken)
    {
        if (candidates.Count == 0 || topK == 0) return [];

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await Compatibility.ReadAsStringAsync(response.Content, cancellationToken);
            throw new HttpRequestException($"{providerName} rerank request failed with {(int)response.StatusCode}: {body}");
        }

        var result = await response.Content.ReadFromJsonAsync<HostedRerankResponse>(cancellationToken);
        if (result?.Results is null) throw new InvalidDataException($"{providerName} returned no rerank results.");

        return result.Results
            .Where(item => item.Index >= 0 && item.Index < candidates.Count)
            .GroupBy(item => item.Index)
            .Select(group => group.First())
            .Select(item => WithRerankScore(candidates[item.Index], item.RelevanceScore))
            .OrderByDescending(item => item.Score)
            .Take(topK)
            .ToArray();
    }

    public static SearchResult WithRerankScore(SearchResult candidate, double score)
    {
        var normalized = Compatibility.IsFinite(score) ? Compatibility.Clamp(score, 0, 1) : 0;
        var details = candidate.ScoreDetails is null
            ? new SearchScoreDetails(candidate.Score, Reranker: normalized)
            : candidate.ScoreDetails with { Reranker = normalized };
        return candidate with { Score = normalized, ScoreDetails = details };
    }

    private sealed record HostedRerankResponse([property: JsonPropertyName("results")] HostedRerankResult[] Results);
    private sealed record HostedRerankResult(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("relevance_score")] double RelevanceScore);
}