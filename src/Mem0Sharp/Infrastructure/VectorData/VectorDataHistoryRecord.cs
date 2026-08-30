using System.Text.Json;
using Microsoft.Extensions.VectorData;

namespace Mem0Sharp;

public sealed class VectorDataHistoryRecord
{
    [VectorStoreKey]
    public string Id { get; set; } = string.Empty;

    [VectorStoreData(IsIndexed = true)]
    public string MemoryId { get; set; } = string.Empty;

    [VectorStoreData(IsIndexed = true)]
    public string Event { get; set; } = nameof(MemoryHistoryEvent.Add);

    [VectorStoreData]
    public string? OldMemory { get; set; }

    [VectorStoreData]
    public string? NewMemory { get; set; }

    [VectorStoreData]
    public string? SnapshotJson { get; set; }

    [VectorStoreData]
    public string? EmbeddingJson { get; set; }

    [VectorStoreData(IsIndexed = true)]
    public string? ActorId { get; set; }

    [VectorStoreData(IsIndexed = true)]
    public string? Role { get; set; }

    [VectorStoreData(IsIndexed = true)]
    public string CreatedAt { get; set; } = string.Empty;

    [VectorStoreData(IsIndexed = true)]
    public string UpdatedAt { get; set; } = string.Empty;

    [VectorStoreData(IsIndexed = true)]
    public bool IsDeleted { get; set; }

    public static VectorDataHistoryRecord FromEntry(MemoryHistoryEntry entry)
    {
        Guard.NotNull(entry);

        return new VectorDataHistoryRecord
        {
            Id = entry.Id,
            MemoryId = entry.MemoryId,
            Event = entry.Event.ToString(),
            OldMemory = entry.OldMemory,
            NewMemory = entry.NewMemory,
            SnapshotJson = entry.Snapshot is null ? null : JsonSerializer.Serialize(entry.Snapshot),
            EmbeddingJson = entry.Embedding is null ? null : JsonSerializer.Serialize(entry.Embedding),
            ActorId = entry.ActorId,
            Role = entry.Role,
            CreatedAt = entry.CreatedAt.ToString("o"),
            UpdatedAt = entry.UpdatedAt.ToString("o"),
            IsDeleted = entry.IsDeleted
        };
    }

    public MemoryHistoryEntry ToEntry()
    {
        var evt = Enum.TryParse<MemoryHistoryEvent>(Event, ignoreCase: true, out var parsedEvent)
            ? parsedEvent
            : MemoryHistoryEvent.Add;

        var createdAt = DateTimeOffset.TryParse(CreatedAt, out var parsedCreatedAt)
            ? parsedCreatedAt
            : DateTimeOffset.UtcNow;

        var updatedAt = DateTimeOffset.TryParse(UpdatedAt, out var parsedUpdatedAt)
            ? parsedUpdatedAt
            : createdAt;

        return new MemoryHistoryEntry
        {
            Id = Id,
            MemoryId = MemoryId,
            Event = evt,
            OldMemory = OldMemory,
            NewMemory = NewMemory,
            Snapshot = Deserialize<Memory>(SnapshotJson),
            Embedding = Deserialize<float[]>(EmbeddingJson),
            ActorId = ActorId,
            Role = Role,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            IsDeleted = IsDeleted
        };
    }

    private static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrEmpty(json)) return default;
        try { return JsonSerializer.Deserialize<T>(json!); }
        catch (JsonException) { return default; }
    }
}
