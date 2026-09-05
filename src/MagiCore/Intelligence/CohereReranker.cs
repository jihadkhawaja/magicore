namespace MagiCore;

public sealed class CohereReranker : IMemoryReranker
{
    private static readonly Uri DefaultEndpoint = new("https://api.cohere.com/v1/rerank");
    private readonly HttpClient httpClient;
    private readonly string apiKey;
    private readonly string model;
    private readonly int maxChunksPerDocument;
    private readonly Uri endpoint;

    public CohereReranker(HttpClient httpClient, string apiKey, string model = "rerank-v3.5", int maxChunksPerDocument = 10, Uri? endpoint = null)
    {
        Guard.NotNull(httpClient);
        Guard.NotNullOrWhiteSpace(apiKey);
        Guard.NotNullOrWhiteSpace(model);
        if (maxChunksPerDocument < 1) throw new ArgumentOutOfRangeException(nameof(maxChunksPerDocument));
        this.httpClient = httpClient;
        this.apiKey = apiKey;
        this.model = model;
        this.maxChunksPerDocument = maxChunksPerDocument;
        this.endpoint = endpoint ?? DefaultEndpoint;
    }

    public Task<IReadOnlyList<SearchResult>> RerankAsync(string query, IReadOnlyList<SearchResult> candidates, int topK, CancellationToken cancellationToken = default)
    {
        Validate(query, candidates, topK);
        var payload = new
        {
            model,
            query,
            documents = candidates.Select(candidate => candidate.Memory.Text).ToArray(),
            top_n = Math.Min(topK, candidates.Count),
            return_documents = false,
            max_chunks_per_doc = maxChunksPerDocument
        };
        return HostedRerankerClient.RerankAsync(httpClient, endpoint, apiKey, payload, candidates, topK, "Cohere", cancellationToken);
    }

    private static void Validate(string query, IReadOnlyList<SearchResult> candidates, int topK)
    {
        Guard.NotNullOrWhiteSpace(query);
        Guard.NotNull(candidates);
        if (topK < 0) throw new ArgumentOutOfRangeException(nameof(topK));
    }
}