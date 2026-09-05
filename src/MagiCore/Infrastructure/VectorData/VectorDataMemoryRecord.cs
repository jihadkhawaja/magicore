using System.Text.Json;
using Microsoft.Extensions.VectorData;

namespace MagiCore;

public sealed class VectorDataMemoryRecord
{
    private static readonly IReadOnlyDictionary<string, string> EmptyDictionary = new Dictionary<string, string>();

    [VectorStoreKey]
    public string Id { get; set; } = string.Empty;

    [VectorStoreData(IsIndexed = true, IsFullTextIndexed = true)]
    public string Text { get; set; } = string.Empty;

    [VectorStoreData(IsIndexed = true)]
    public string UserId { get; set; } = string.Empty;

    [VectorStoreData(IsIndexed = true)]
    public string? AgentId { get; set; }

    [VectorStoreData(IsIndexed = true)]
    public string? RunId { get; set; }

    [VectorStoreData(IsIndexed = true)]
    public string Scope { get; set; } = nameof(MemoryScope.User);

    [VectorStoreData]
    public string? MetadataJson { get; set; }

    [VectorStoreData(IsIndexed = true)]
    public string CreatedAt { get; set; } = string.Empty;

    [VectorStoreData(IsIndexed = true)]
    public string UpdatedAt { get; set; } = string.Empty;

    [VectorStoreData(IsIndexed = true)]
    public long ExpiresAtUtcTicks { get; set; } = long.MaxValue;

    [VectorStoreData]
    public string Hash { get; set; } = string.Empty;

    [VectorStoreData(IsIndexed = true)]
    public string Behavior { get; set; } = nameof(MemoryBehavior.Normal);

    [VectorStoreData(IsIndexed = true)]
    public string? MemoryType { get; set; }

    public ReadOnlyMemory<float>? Vector { get; set; }

    public static VectorDataMemoryRecord FromMemory(Memory memory, IReadOnlyList<float>? embedding = null)
    {
        Guard.NotNull(memory);

        return new VectorDataMemoryRecord
        {
            Id = memory.Id,
            Text = memory.Text,
            UserId = memory.UserId,
            AgentId = memory.AgentId,
            RunId = memory.RunId,
            Scope = memory.Scope.ToString(),
            MetadataJson = memory.Metadata.Count > 0 ? JsonSerializer.Serialize(memory.Metadata) : null,
            CreatedAt = memory.CreatedAt.ToString("o"),
            UpdatedAt = memory.UpdatedAt.ToString("o"),
            ExpiresAtUtcTicks = memory.ExpiresAt?.UtcDateTime.Ticks ?? long.MaxValue,
            Hash = memory.Hash,
            Behavior = memory.Behavior.ToString(),
            MemoryType = memory.MemoryType,
            Vector = embedding is not null ? new ReadOnlyMemory<float>(embedding.ToArray()) : null
        };
    }

    public Memory ToMemory()
    {
        IReadOnlyDictionary<string, string> metadata = EmptyDictionary;
        if (!string.IsNullOrEmpty(MetadataJson))
        {
            try
            {
                metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(MetadataJson!) ?? EmptyDictionary;
            }
            catch
            {
                metadata = EmptyDictionary;
            }
        }

        var scope = Enum.TryParse<MemoryScope>(Scope, ignoreCase: true, out var parsedScope)
            ? parsedScope
            : MemoryScope.User;

        var behavior = Enum.TryParse<MemoryBehavior>(Behavior, ignoreCase: true, out var parsedBehavior)
            ? parsedBehavior
            : MemoryBehavior.Normal;

        var createdAt = DateTimeOffset.TryParse(CreatedAt, out var parsedCreatedAt)
            ? parsedCreatedAt
            : DateTimeOffset.UtcNow;

        var updatedAt = DateTimeOffset.TryParse(UpdatedAt, out var parsedUpdatedAt)
            ? parsedUpdatedAt
            : createdAt;

        var expiresAt = ExpiresAtUtcTicks == long.MaxValue
            ? (DateTimeOffset?)null
            : new DateTimeOffset(ExpiresAtUtcTicks, TimeSpan.Zero);

        return new Memory
        {
            Id = Id,
            Text = Text,
            UserId = UserId,
            AgentId = AgentId,
            RunId = RunId,
            Scope = scope,
            Metadata = metadata,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            ExpiresAt = expiresAt,
            Hash = Hash,
            Behavior = behavior,
            MemoryType = MemoryType
        };
    }
}
