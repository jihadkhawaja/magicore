namespace MagiCore;

public sealed class ZeroEntropyReranker : IMemoryReranker
{
    private static readonly Uri DefaultEndpoint = new("https://api.zeroentropy.dev/v1/models/rerank");
    private readonly HttpClient httpClient;
    private readonly string apiKey;
    private readonly string model;
    private readonly Uri endpoint;

    public ZeroEntropyReranker(HttpClient httpClient, string apiKey, string model = "zerank-1", Uri? endpoint = null)
    {
        Guard.NotNull(httpClient);
        Guard.NotNullOrWhiteSpace(apiKey);
        Guard.NotNullOrWhiteSpace(model);
        this.httpClient = httpClient;
        this.apiKey = apiKey;
        this.model = model;
        this.endpoint = endpoint ?? DefaultEndpoint;
    }

    public Task<IReadOnlyList<SearchResult>> RerankAsync(string query, IReadOnlyList<SearchResult> candidates, int topK, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(query);
        Guard.NotNull(candidates);
        if (topK < 0) throw new ArgumentOutOfRangeException(nameof(topK));
        var payload = new
        {
            model,
            query,
            documents = candidates.Select(candidate => candidate.Memory.Text).ToArray(),
            top_n = Math.Min(topK, candidates.Count)
        };
        return HostedRerankerClient.RerankAsync(httpClient, endpoint, apiKey, payload, candidates, topK, "ZeroEntropy", cancellationToken);
    }
}