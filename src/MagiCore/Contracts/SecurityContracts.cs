namespace MagiCore;

public sealed record MemoryAdmissionContext(
    string Text,
    string UserId,
    string? AgentId = null,
    string? RunId = null,
    string? ActorId = null,
    string? Role = null,
    MemoryScope Scope = MemoryScope.User,
    IReadOnlyDictionary<string, string>? Metadata = null,
    MemoryBehavior Behavior = MemoryBehavior.Normal,
    string? MemoryType = null);

public sealed record MemoryAdmissionDecision(
    bool IsAdmitted,
    double ConfidenceScore = 1.0,
    string? Reason = null);

public interface IAdmissionGate
{
    Task<MemoryAdmissionDecision> EvaluateAsync(
        MemoryAdmissionContext context,
        IReadOnlyList<Memory> existingMemories,
        CancellationToken cancellationToken = default);
}
