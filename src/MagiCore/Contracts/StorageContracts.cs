namespace MagiCore;

public sealed record MemoryVectorRecord(Memory Memory, IReadOnlyList<float> Embedding);

public sealed record MemoryWriteRecord(Memory Memory, IReadOnlyList<float>? Embedding, MemoryHistoryEntry History);

public sealed record MemoryDeleteRecord(Memory Memory, MemoryHistoryEntry History);

public interface ITemporalMemoryStore
{
    Task<IReadOnlyList<Memory>> GetAllAtAsync(DateTimeOffset pointInTime, MemoryFilter? filter = null, CancellationToken cancellationToken = default);
}

public interface IMemoryStore
{
    Task SaveAsync(Memory memory, IReadOnlyList<float>? embedding = null, CancellationToken cancellationToken = default);
    Task SaveBatchAsync(IReadOnlyList<MemoryWriteRecord> records, CancellationToken cancellationToken = default);
    Task<Memory?> GetAsync(string id, CancellationToken cancellationToken = default);
    IAsyncEnumerable<Memory> GetAllAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, MemoryHistoryEntry? history = null, CancellationToken cancellationToken = default);
    Task<int> DeleteAllAsync(MemoryFilter? filter = null, IReadOnlyList<MemoryDeleteRecord>? records = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SearchResult>> SearchAsync(IReadOnlyList<float> embedding, MemoryFilter? filter = null, int topK = 5, CancellationToken cancellationToken = default);
#if NETSTANDARD2_0
    Task<IReadOnlyList<IReadOnlyList<SearchResult>>> SearchBatchAsync(IReadOnlyList<IReadOnlyList<float>> embeddings, MemoryFilter? filter = null, int topK = 5, CancellationToken cancellationToken = default);
#else
    async Task<IReadOnlyList<IReadOnlyList<SearchResult>>> SearchBatchAsync(IReadOnlyList<IReadOnlyList<float>> embeddings, MemoryFilter? filter = null, int topK = 5, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(embeddings);
        var results = new List<IReadOnlyList<SearchResult>>(embeddings.Count);
        foreach (var embedding in embeddings) results.Add(await SearchAsync(embedding, filter, topK, cancellationToken));
        return results;
    }
#endif
    Task SaveHistoryAsync(MemoryHistoryEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryHistoryEntry>> GetHistoryAsync(string memoryId, CancellationToken cancellationToken = default);
#if NETSTANDARD2_0
    Task<IReadOnlyList<MemoryHistoryEntry>> GetAllHistoryAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default);
    Task<RollbackResult> RollbackAsync(DateTimeOffset pointInTime, MemoryFilter? filter = null, CancellationToken cancellationToken = default);
    Task<RollbackResult> RollbackToHistoryAsync(string historyEntryId, CancellationToken cancellationToken = default);
#else
    Task<IReadOnlyList<MemoryHistoryEntry>> GetAllHistoryAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MemoryHistoryEntry>>([]);
    Task<RollbackResult> RollbackAsync(DateTimeOffset pointInTime, MemoryFilter? filter = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RollbackResult(0, 0, []));
    Task<RollbackResult> RollbackToHistoryAsync(string historyEntryId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RollbackResult(0, 0, []));
#endif
    Task ResetAsync(CancellationToken cancellationToken = default);
}