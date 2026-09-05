namespace MagiCore;

public sealed record QdrantMemoryStoreOptions
{
    public required Uri Endpoint { get; init; }
    public string CollectionName { get; init; } = "magicore_memories";
    public required int EmbeddingDimensions { get; init; }
    public string? ApiKey { get; init; }
}