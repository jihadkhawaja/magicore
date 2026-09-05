namespace MagiCore;

public sealed record MemoryAddOptions
{
    public string UserId { get; init; } = "default_user";
    public string? AgentId { get; init; }
    public string? RunId { get; init; }
    public MemoryScope Scope { get; init; } = MemoryScope.User;
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
    public DateTimeOffset? ReferenceTime { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public bool Infer { get; init; } = true;
    public string? Prompt { get; init; }
    public string? MemoryType { get; init; }
    public MemoryBehavior Behavior { get; init; } = MemoryBehavior.Normal;
    public bool Deduplicate { get; init; } = true;
}

public enum MemoryBehavior
{
    Normal,
    Dreaming,
    RandomThoughts,
    PersonalMemory
}

public sealed record MemoryUpdate
{
    public string? Text { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
    public bool UpdateReferenceTime { get; init; }
    public DateTimeOffset? ReferenceTime { get; init; }
    public bool UpdateExpiration { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public enum MemoryAction
{
    Add,
    Update,
    Delete,
    None
}

public sealed record MemoryDecision(string Text, MemoryAction Event, string? MemoryId = null, IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record MemoryActionResult(string? Id, string? Memory, MemoryAction Event);

public sealed record AddResult(IReadOnlyList<Memory> Memories, IReadOnlyList<MemoryActionResult>? Actions = null);