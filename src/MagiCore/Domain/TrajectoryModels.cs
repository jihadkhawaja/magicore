namespace MagiCore;

public sealed record TrajectoryRecord
{
    public required string Id { get; init; }
    public required string SessionId { get; init; }
    public required string UserId { get; init; }
    public string? AgentId { get; init; }
    public string? RunId { get; init; }
    public required IReadOnlyList<Message> Messages { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
