using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MagiCore;

public sealed class QdrantMemoryStore : IMemoryStore
{
    private const int ScrollPageSize = 256;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly QdrantMemoryStoreOptions options;
    private readonly string collectionPath;

    public QdrantMemoryStore(HttpClient httpClient, QdrantMemoryStoreOptions options)
    {
        Guard.NotNull(httpClient);
        Guard.NotNull(options);
        if (!options.Endpoint.IsAbsoluteUri) throw new ArgumentException("Qdrant endpoint must be absolute.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.CollectionName)) throw new ArgumentException("CollectionName is required.", nameof(options));
        if (options.EmbeddingDimensions < 1) throw new ArgumentOutOfRangeException(nameof(options));
        this.httpClient = httpClient;
        this.options = options;
        collectionPath = $"collections/{Uri.EscapeDataString(options.CollectionName)}";
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var probe = await SendAsync(HttpMethod.Get, collectionPath, null, cancellationToken, ensureSuccess: false);
        if (probe.IsSuccessStatusCode) return;
        if (probe.StatusCode != HttpStatusCode.NotFound) await ThrowRequestErrorAsync(probe, cancellationToken);
        using var response = await SendAsync(HttpMethod.Put, collectionPath, new { vectors = new { size = options.EmbeddingDimensions, distance = "Cosine" } }, cancellationToken);
    }

    public Task SaveAsync(Memory memory, IReadOnlyList<float>? embedding = null, CancellationToken cancellationToken = default)
    {
        var vector = embedding ?? new float[options.EmbeddingDimensions];
        return SaveBatchAsync([new MemoryWriteRecord(memory, vector, null!)], cancellationToken);
    }

    public async Task SaveBatchAsync(IReadOnlyList<MemoryWriteRecord> records, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(records);
        if (records.Count == 0) return;
        foreach (var record in records)
        {
            if (record.Embedding is not null) ValidateEmbedding(record.Embedding);
        }

        var points = records.Select(record => new
        {
            id = PointId(record.Memory.Id),
            vector = record.Embedding ?? new float[options.EmbeddingDimensions],
            payload = new { memory = record.Memory }
        }).ToArray();
        using var response = await SendAsync(HttpMethod.Put, $"{collectionPath}/points?wait=true", new { points }, cancellationToken);
    }

    public async Task<Memory?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(id);
        using var response = await SendAsync(HttpMethod.Get, $"{collectionPath}/points/{Uri.EscapeDataString(PointId(id))}?with_payload=true", null, cancellationToken, ensureSuccess: false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) await ThrowRequestErrorAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        return ReadMemory(payload?["result"]?["payload"]?["memory"]);
    }

    public async IAsyncEnumerable<Memory> GetAllAsync(MemoryFilter? filter = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var point in await ScrollAsync(false, cancellationToken))
        {
            if (MemoryFilterEvaluator.Matches(point.Memory, filter)) yield return point.Memory;
        }
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(IReadOnlyList<float> embedding, MemoryFilter? filter = null, int topK = 5, CancellationToken cancellationToken = default)
    {
        ValidateEmbedding(embedding);
        if (topK < 0) throw new ArgumentOutOfRangeException(nameof(topK));
        if (topK == 0) return [];

        var searchBody = new JsonObject
        {
            ["vector"] = JsonSerializer.SerializeToNode(embedding),
            ["limit"] = topK,
            ["with_payload"] = true,
            ["with_vector"] = false
        };

        var qdrantFilter = BuildQdrantFilter(filter);
        if (qdrantFilter is not null)
        {
            searchBody["filter"] = qdrantFilter;
        }

        using var response = await SendAsync(HttpMethod.Post, $"{collectionPath}/points/search", searchBody, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        var resultsArray = payload?["result"]?.AsArray();
        if (resultsArray is null) return [];

        var results = new List<SearchResult>();
        foreach (var item in resultsArray)
        {
            var memory = ReadMemory(item?["payload"]?["memory"]);
            if (memory is null) continue;
            if (!MemoryFilterEvaluator.Matches(memory, filter)) continue;
            var score = item?["score"]?.GetValue<double>() ?? 0;
            results.Add(new SearchResult(memory, score));
        }
        return results;
    }

    public async Task<IReadOnlyList<IReadOnlyList<SearchResult>>> SearchBatchAsync(IReadOnlyList<IReadOnlyList<float>> embeddings, MemoryFilter? filter = null, int topK = 5, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(embeddings);
        foreach (var embedding in embeddings) ValidateEmbedding(embedding);
        if (topK < 0) throw new ArgumentOutOfRangeException(nameof(topK));
        if (embeddings.Count == 0 || topK == 0) return embeddings.Select(_ => (IReadOnlyList<SearchResult>)[]).ToArray();

        var qdrantFilter = BuildQdrantFilter(filter);
        var searches = embeddings.Select(embedding =>
        {
            var searchObj = new JsonObject
            {
                ["vector"] = JsonSerializer.SerializeToNode(embedding),
                ["limit"] = topK,
                ["with_payload"] = true,
                ["with_vector"] = false
            };
            if (qdrantFilter is not null)
            {
                searchObj["filter"] = qdrantFilter.DeepClone();
            }
            return (JsonNode)searchObj;
        }).ToArray();

        using var response = await SendAsync(HttpMethod.Post, $"{collectionPath}/points/search/batch", new JsonObject { ["searches"] = new JsonArray(searches) }, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        var batchArray = payload?["result"]?.AsArray();
        if (batchArray is null) return embeddings.Select(_ => (IReadOnlyList<SearchResult>)[]).ToArray();

        var batchedResults = new List<IReadOnlyList<SearchResult>>(batchArray.Count);
        foreach (var resultSet in batchArray)
        {
            var results = new List<SearchResult>();
            if (resultSet is JsonArray items)
            {
                foreach (var item in items)
                {
                    var memory = ReadMemory(item?["payload"]?["memory"]);
                    if (memory is null) continue;
                    if (!MemoryFilterEvaluator.Matches(memory, filter)) continue;
                    var score = item?["score"]?.GetValue<double>() ?? 0;
                    results.Add(new SearchResult(memory, score));
                }
            }
            batchedResults.Add(results);
        }
        return batchedResults;
    }

    public async Task DeleteAsync(string id, MemoryHistoryEntry? history = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(id);
        await DeleteIdsAsync([id], cancellationToken);
    }

    public async Task<int> DeleteAllAsync(MemoryFilter? filter = null, IReadOnlyList<MemoryDeleteRecord>? records = null, CancellationToken cancellationToken = default)
    {
        var ids = new List<string>();
        if (records is not null)
        {
            ids.AddRange(records.Select(r => r.Memory.Id));
        }
        else
        {
            await foreach (var memory in GetAllAsync(filter, cancellationToken)) ids.Add(memory.Id);
        }
        await DeleteIdsAsync(ids, cancellationToken);
        return ids.Count;
    }

    public Task SaveHistoryAsync(MemoryHistoryEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<MemoryHistoryEntry>> GetHistoryAsync(string memoryId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MemoryHistoryEntry>>([]);

    public Task<IReadOnlyList<MemoryHistoryEntry>> GetAllHistoryAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MemoryHistoryEntry>>([]);

    public Task<RollbackResult> RollbackAsync(DateTimeOffset pointInTime, MemoryFilter? filter = null, CancellationToken cancellationToken = default) => Task.FromResult(new RollbackResult(0, 0, []));

    public Task<RollbackResult> RollbackToHistoryAsync(string historyEntryId, CancellationToken cancellationToken = default) => Task.FromResult(new RollbackResult(0, 0, []));

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        using var deletion = await SendAsync(HttpMethod.Delete, collectionPath, null, cancellationToken, ensureSuccess: false);
        if (!deletion.IsSuccessStatusCode && deletion.StatusCode != HttpStatusCode.NotFound) await ThrowRequestErrorAsync(deletion, cancellationToken);
        using var creation = await SendAsync(HttpMethod.Put, collectionPath, new { vectors = new { size = options.EmbeddingDimensions, distance = "Cosine" } }, cancellationToken);
    }

    private async Task DeleteIdsAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0) return;
        using var response = await SendAsync(HttpMethod.Post, $"{collectionPath}/points/delete?wait=true", new { points = ids.Select(PointId).ToArray() }, cancellationToken);
    }

    private async Task<IReadOnlyList<QdrantPoint>> ScrollAsync(bool withVectors, CancellationToken cancellationToken)
    {
        var points = new List<QdrantPoint>();
        JsonNode? offset = null;
        do
        {
            var body = new JsonObject
            {
                ["limit"] = ScrollPageSize,
                ["with_payload"] = true,
                ["with_vector"] = withVectors
            };
            if (offset is not null) body["offset"] = offset.DeepClone();
            using var response = await SendAsync(HttpMethod.Post, $"{collectionPath}/points/scroll", body, cancellationToken);
            var payload = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
            var result = payload?["result"];
            foreach (var point in result?["points"]?.AsArray() ?? [])
            {
                var memory = ReadMemory(point?["payload"]?["memory"]);
                if (memory is null) continue;
                var vector = point?["vector"]?.AsArray().Select(value => value?.GetValue<float>() ?? 0).ToArray();
                points.Add(new QdrantPoint(memory, vector));
            }
            offset = result?["next_page_offset"]?.DeepClone();
        }
        while (offset is not null);
        return points;
    }

    private static JsonNode? BuildQdrantFilter(MemoryFilter? filter)
    {
        if (filter is null) return null;
        var must = new JsonArray();
        if (filter.UserId is not null)
            must.Add(new JsonObject { ["key"] = "memory.userId", ["match"] = new JsonObject { ["value"] = filter.UserId } });
        if (filter.AgentId is not null)
            must.Add(new JsonObject { ["key"] = "memory.agentId", ["match"] = new JsonObject { ["value"] = filter.AgentId } });
        if (filter.RunId is not null)
            must.Add(new JsonObject { ["key"] = "memory.runId", ["match"] = new JsonObject { ["value"] = filter.RunId } });
        if (filter.Scope is not null)
            must.Add(new JsonObject { ["key"] = "memory.scope", ["match"] = new JsonObject { ["value"] = (int)filter.Scope.Value } });
        if (filter.Behavior is not null)
            must.Add(new JsonObject { ["key"] = "memory.behavior", ["match"] = new JsonObject { ["value"] = (int)filter.Behavior.Value } });
        if (filter.MemoryType is not null)
            must.Add(new JsonObject { ["key"] = "memory.memoryType", ["match"] = new JsonObject { ["value"] = filter.MemoryType } });

        return must.Count == 0 ? null : new JsonObject { ["must"] = must };
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken, bool ensureSuccess = true)
    {
        var endpoint = new Uri(options.Endpoint.AbsoluteUri.TrimEnd('/') + "/" + path);
        var request = new HttpRequestMessage(method, endpoint);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (!string.IsNullOrWhiteSpace(options.ApiKey)) request.Headers.Add("api-key", options.ApiKey);
        var response = await httpClient.SendAsync(request, cancellationToken);
        request.Dispose();
        if (ensureSuccess && !response.IsSuccessStatusCode)
        {
            await ThrowRequestErrorAsync(response, cancellationToken);
        }
        return response;
    }

    private static async Task ThrowRequestErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await Compatibility.ReadAsStringAsync(response.Content, cancellationToken);
        throw Compatibility.CreateHttpRequestException($"Qdrant request failed with {(int)response.StatusCode}: {body}", response.StatusCode);
    }

    private void ValidateEmbedding(IReadOnlyList<float> embedding)
    {
        Guard.NotNull(embedding);
        if (embedding.Count != options.EmbeddingDimensions) throw new ArgumentException("Embedding dimensions do not match the Qdrant collection.", nameof(embedding));
    }

    private static string PointId(string memoryId) => Guid.TryParse(memoryId, out var id) ? id.ToString("D") : memoryId;
    private static Memory? ReadMemory(JsonNode? node) => node?.Deserialize<Memory>(JsonOptions);

    private sealed record QdrantPoint(Memory Memory, IReadOnlyList<float>? Vector);
}