namespace Mem0Sharp;

public enum MemoryHistoryEvent
{
    Add,
    Update,
    Delete
}

public sealed record MemoryHistoryEntry
{
    public required string Id { get; init; }
    public required string MemoryId { get; init; }
    public required MemoryHistoryEvent Event { get; init; }
    public Memory? Snapshot { get; init; }
    public IReadOnlyList<float>? Embedding { get; init; }
    public string? OldMemory { get; init; }
    public string? NewMemory { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public bool IsDeleted { get; init; }
    public string? ActorId { get; init; }
    public string? Role { get; init; }
    public string? SourceMessageHash { get; init; }
    public string? SessionId { get; init; }
    public string? ProvenanceTraceId { get; init; }
}

public sealed record RollbackResult(
    int RestoredCount,
    int DeletedCount,
    IReadOnlyList<string> AffectedMemoryIds);