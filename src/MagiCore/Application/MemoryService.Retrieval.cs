using System.Globalization;
using System.Numerics.Tensors;
using Microsoft.Extensions.AI;

namespace MagiCore;

public sealed partial class MemoryService : IMemoryService
{
    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, MemorySearchOptions? searchOptions = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(query);
        var effective = searchOptions ?? new MemorySearchOptions { TopK = options.DefaultTopK, Threshold = options.MinimumScore, Hybrid = options.EnableHybridSearch };
        return SearchCoreAsync(query, effective, cancellationToken);
    }

    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, MemoryFilter? filter, int? topK = null, CancellationToken cancellationToken = default) =>
        SearchAsync(query, new MemorySearchOptions { Filter = filter, TopK = topK ?? options.DefaultTopK }, cancellationToken);

    public async Task<IReadOnlyList<SearchResult>> SearchAtAsync(string query, DateTimeOffset pointInTime, MemorySearchOptions? searchOptions = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(query);
        var effective = searchOptions ?? new MemorySearchOptions { TopK = options.DefaultTopK, Threshold = options.MinimumScore, Hybrid = options.EnableHybridSearch };
        if (effective.TopK < 0) throw new ArgumentOutOfRangeException(nameof(searchOptions));
        if (effective.TopK == 0) return [];

        var memoriesAtPoint = await GetAllAtAsync(pointInTime, ApplySearchFilter(effective), cancellationToken);
        var timeRange = await ResolveTimeRangeAsync(query, effective, cancellationToken);
        if (timeRange is not null)
        {
            memoriesAtPoint = memoriesAtPoint.Where(memory => IsInTimeRange(memory, timeRange, effective.IncludeUndatedMemories)).ToArray();
        }
        if (memoriesAtPoint.Count == 0) return [];

        var queryVector = await embeddings.GenerateVectorCoreAsync(query, cancellationToken);
        var memoryVectors = await embeddings.GenerateVectorBatchCoreAsync(memoriesAtPoint.Select(memory => memory.Text).ToArray(), cancellationToken);
        if (memoryVectors.Count != memoriesAtPoint.Count) throw new InvalidOperationException("The embedding provider returned a different number of vectors than historical memories.");

        var semanticResults = memoriesAtPoint
            .Select((memory, index) => new SearchResult(memory, CosineSimilarity(queryVector, memoryVectors[index])))
            .ToArray();
        IReadOnlyList<SearchResult> ranked = effective.Hybrid
            ? HybridSearchScorer.ScoreAndRank(query, semanticResults, new Dictionary<string, double>(StringComparer.Ordinal), effective.Threshold, effective.TopK, effective.Explain)
            : semanticResults.Where(result => result.Score >= effective.Threshold)
                .OrderByDescending(result => result.Score)
                .Take(effective.TopK)
                .Select(result => effective.Explain ? result with { ScoreDetails = new SearchScoreDetails(result.Score, Threshold: effective.Threshold) } : result with { ScoreDetails = null })
                .ToArray();

        if (effective.Rerank && reranker is not null)
        {
            ranked = await reranker.RerankAsync(query, ranked, effective.TopK, cancellationToken);
        }
        return effective.RecencyBias > 0 ? ApplyRecencyBias(ranked, effective, pointInTime) : ranked;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(DataContent image, MemorySearchOptions? searchOptions = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(image);
        if (imageEmbeddings is null)
        {
            throw new InvalidOperationException("An image embedding generator (IEmbeddingGenerator<DataContent, Embedding<float>>) is required to perform image searches.");
        }
        var effective = searchOptions ?? new MemorySearchOptions { TopK = options.DefaultTopK, Threshold = options.MinimumScore, Hybrid = false };
        if (effective.TopK < 0) throw new ArgumentOutOfRangeException(nameof(searchOptions));

        var effectiveOptions = effective with { Filter = ApplySearchFilter(effective) };
        var queryVector = await imageEmbeddings.GenerateVectorCoreAsync(image, cancellationToken);
        var candidateLimit = Math.Max(effective.TopK * 4, 60);
        var semanticResults = await store.SearchAsync(queryVector, effectiveOptions.Filter, candidateLimit, cancellationToken);

        return semanticResults
            .Where(result => effective.TimeRange is null || IsInTimeRange(result.Memory, ValidateTimeRange(effective.TimeRange), effective.IncludeUndatedMemories))
            .Where(result => result.Score >= effective.Threshold)
            .OrderByDescending(result => result.Score)
            .Take(effective.TopK)
            .Select(result => effective.Explain ? result with { ScoreDetails = new SearchScoreDetails(result.Score, Threshold: effective.Threshold) } : result with { ScoreDetails = null })
            .ToArray();
    }

    public Task<IReadOnlyList<SearchResult>> SearchAsync(ReadOnlyMemory<byte> imageData, string mediaType, MemorySearchOptions? options = null, CancellationToken cancellationToken = default) =>
        SearchAsync(new DataContent(imageData, mediaType), options, cancellationToken);

    public Task<IReadOnlyList<SearchResult>> SearchAsync(Uri imageUri, string mediaType = "image/jpeg", MemorySearchOptions? options = null, CancellationToken cancellationToken = default) =>
        SearchAsync(Message.CreateDataContent(imageUri, mediaType), options, cancellationToken);

    private async Task<IReadOnlyList<SearchResult>> SearchCoreAsync(string query, MemorySearchOptions searchOptions, CancellationToken cancellationToken)
    {
        if (searchOptions.TopK < 0) throw new ArgumentOutOfRangeException(nameof(searchOptions));
        var effectiveOptions = searchOptions with { Filter = ApplySearchFilter(searchOptions) };
        var queryVector = await embeddings.GenerateVectorCoreAsync(query, cancellationToken);
        var candidateLimit = Math.Max(searchOptions.TopK * 4, 60);
        var semanticResults = await store.SearchAsync(queryVector, effectiveOptions.Filter, candidateLimit, cancellationToken);

        return await RankSearchResultsAsync(query, effectiveOptions, semanticResults, cancellationToken);
    }

    private async Task<IReadOnlyList<SearchResult>> RankSearchResultsAsync(string query, MemorySearchOptions searchOptions, IReadOnlyList<SearchResult> semanticResults, CancellationToken cancellationToken)
    {
        var timeRange = await ResolveTimeRangeAsync(query, searchOptions, cancellationToken);
        if (timeRange is not null)
        {
            semanticResults = semanticResults.Where(result => IsInTimeRange(result.Memory, timeRange, searchOptions.IncludeUndatedMemories)).ToArray();
        }
        var queryEntities = await entityExtractor.ExtractAsync(query, cancellationToken);
        var entityBoosts = (await entityStore.GetMemoryBoostsAsync(queryEntities, cancellationToken)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (graphStore is not null)
        {
            foreach (var boost in await graphStore.GetMemoryBoostsAsync(query, cancellationToken))
            {
                entityBoosts[boost.Key] = Math.Min(0.5, entityBoosts.GetValueOrDefault(boost.Key) + boost.Value);
            }
        }
        IReadOnlyList<SearchResult> ranked = searchOptions.Hybrid
            ? HybridSearchScorer.ScoreAndRank(query, semanticResults, entityBoosts, searchOptions.Threshold, searchOptions.TopK, searchOptions.Explain)
            : semanticResults.Where(result => result.Score >= searchOptions.Threshold)
                .OrderByDescending(result => result.Score)
                .Take(searchOptions.TopK)
                .Select(result => searchOptions.Explain ? result with { ScoreDetails = new SearchScoreDetails(result.Score, Threshold: searchOptions.Threshold) } : result with { ScoreDetails = null })
                .ToArray();
        if (searchOptions.Rerank && reranker is not null)
        {
            ranked = await reranker.RerankAsync(query, ranked, searchOptions.TopK, cancellationToken);
        }
        if (searchOptions.RecencyBias > 0)
        {
            ranked = ApplyRecencyBias(ranked, searchOptions, DateTimeOffset.UtcNow);
        }
        return ranked;
    }

    private static IReadOnlyList<SearchResult> ApplyRecencyBias(IReadOnlyList<SearchResult> ranked, MemorySearchOptions searchOptions, DateTimeOffset now)
    {
        var recencyBias = Compatibility.Clamp(searchOptions.RecencyBias, 0d, 1d);
        if (recencyBias <= 0) return ranked;
        var window = searchOptions.FreshnessWindow ?? TimeSpan.FromDays(30);
        if (window <= TimeSpan.Zero) window = TimeSpan.FromDays(30);

        return ranked
            .Select(result =>
            {
                var age = now - result.Memory.UpdatedAt;
                var freshness = age <= TimeSpan.Zero ? 1d : Compatibility.Clamp(1d - age.TotalSeconds / window.TotalSeconds, 0d, 1d);
                var boostedScore = result.Score * (1d - recencyBias) + freshness * recencyBias;
                return result with { Score = boostedScore };
            })
            .OrderByDescending(result => result.Score)
            .ToArray();
    }

    public async Task<IReadOnlyList<IReadOnlyList<SearchResult>>> SearchManyAsync(IEnumerable<string> queries, MemorySearchOptions? searchOptions = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(queries);
        var materialized = queries.ToArray();
        foreach (var query in materialized) Guard.NotNullOrWhiteSpace(query);
        if (materialized.Length == 0) return [];

        var effective = searchOptions ?? new MemorySearchOptions { TopK = options.DefaultTopK, Threshold = options.MinimumScore, Hybrid = options.EnableHybridSearch };
        effective = effective with { Filter = ApplySearchFilter(effective) };
        if (effective.TopK < 0) throw new ArgumentOutOfRangeException(nameof(searchOptions));

        var queryVectors = await embeddings.GenerateVectorBatchCoreAsync(materialized, cancellationToken);
        if (queryVectors.Count != materialized.Length) throw new InvalidOperationException("The embedding provider returned a different number of vectors than input queries.");
        var semanticBatches = await store.SearchBatchAsync(queryVectors, effective.Filter, Math.Max(effective.TopK * 4, 60), cancellationToken);
        if (semanticBatches.Count != materialized.Length) throw new InvalidOperationException("The vector store returned a different number of result sets than input queries.");

        var batchedResults = new IReadOnlyList<SearchResult>[materialized.Length];
        for (var index = 0; index < materialized.Length; index++)
        {
            batchedResults[index] = await RankSearchResultsAsync(materialized[index], effective, semanticBatches[index], cancellationToken);
        }
        return batchedResults;
    }

    public Task<IReadOnlyList<IReadOnlyList<SearchResult>>> SearchManyAsync(IEnumerable<string> queries, MemoryFilter? filter, int? topK = null, CancellationToken cancellationToken = default) =>
        SearchManyAsync(queries, new MemorySearchOptions { Filter = filter, TopK = topK ?? options.DefaultTopK }, cancellationToken);

    private static MemoryFilter? ApplySearchFilter(MemorySearchOptions searchOptions)
    {
        if (searchOptions.Behavior is not null)
        {
            return (searchOptions.Filter ?? new MemoryFilter()) with { Behavior = searchOptions.Behavior };
        }
        if (searchOptions.IncludeNonFactual || searchOptions.Filter?.Behavior is not null) return searchOptions.Filter;
        return (searchOptions.Filter ?? new MemoryFilter()) with { Behavior = MemoryBehavior.Normal };
    }

    private async Task<MemoryTimeRange?> ResolveTimeRangeAsync(string query, MemorySearchOptions searchOptions, CancellationToken cancellationToken)
    {
        if (searchOptions.TimeRange is not null) return ValidateTimeRange(searchOptions.TimeRange);
        if (!searchOptions.EnableTemporalSearch) return null;

        var interpretation = await temporalQueryInterpreter.InterpretAsync(query, searchOptions.ReferenceTime ?? DateTimeOffset.UtcNow, cancellationToken);
        var minimumConfidence = Compatibility.Clamp(searchOptions.MinimumTemporalConfidence, 0, 1);
        return interpretation is not null && interpretation.Confidence >= minimumConfidence
            ? ValidateTimeRange(interpretation.Range)
            : null;
    }

    private static MemoryTimeRange ValidateTimeRange(MemoryTimeRange range)
    {
        if (range.Start.HasValue && range.End.HasValue && range.Start.Value > range.End.Value)
        {
            throw new ArgumentException("The temporal search range start must not be after its end.", nameof(range));
        }
        return range;
    }

    private static bool IsInTimeRange(Memory memory, MemoryTimeRange range, bool includeUndated)
    {
        if (!memory.Metadata.TryGetValue(TemporalMemoryMetadata.ReferenceTimeKey, out var value)
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var referenceTime))
        {
            return includeUndated;
        }
        return (!range.Start.HasValue || referenceTime >= range.Start.Value)
            && (!range.End.HasValue || referenceTime <= range.End.Value);
    }

    public static double CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        if (left.Count != right.Count || left.Count == 0) return 0;
        if (left is float[] leftArr && right is float[] rightArr)
        {
            return TensorPrimitives.CosineSimilarity(leftArr, rightArr);
        }
        var leftSpan = left.ToArray();
        var rightSpan = right.ToArray();
        return TensorPrimitives.CosineSimilarity(leftSpan, rightSpan);
    }
}