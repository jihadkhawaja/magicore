namespace MagiCore;

public interface IEntityExtractor
{
    Task<IReadOnlyList<ExtractedEntity>> ExtractAsync(string text, CancellationToken cancellationToken = default);
}

public interface IEntityStore
{
    Task UpsertLinksAsync(IReadOnlyList<ExtractedEntity> entities, string memoryId, CancellationToken cancellationToken = default);
    Task RemoveMemoryAsync(string memoryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, double>> GetMemoryBoostsAsync(IReadOnlyList<ExtractedEntity> entities, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryEntity>> GetAllAsync(CancellationToken cancellationToken = default);
    Task ResetAsync(CancellationToken cancellationToken = default);
}

public interface IGraphMemoryExtractor
{
    Task<IReadOnlyList<ExtractedRelation>> ExtractAsync(string text, CancellationToken cancellationToken = default);
}

public interface IGraphMemoryStore
{
    Task UpsertAsync(IReadOnlyList<ExtractedRelation> relations, string memoryId, CancellationToken cancellationToken = default);
    Task RemoveMemoryAsync(string memoryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, double>> GetMemoryBoostsAsync(string query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryRelation>> GetRelationsAsync(string? query = null, CancellationToken cancellationToken = default);
    Task ResetAsync(CancellationToken cancellationToken = default);
}