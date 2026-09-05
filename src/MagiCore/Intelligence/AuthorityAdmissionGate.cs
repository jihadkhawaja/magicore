namespace MagiCore;

public sealed class AuthorityAdmissionGate : IAdmissionGate
{
    private static readonly HashSet<string> DefaultUntrustedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "guest",
        "anonymous",
        "untrusted"
    };

    private readonly HashSet<string> untrustedRoles;

    public AuthorityAdmissionGate(IEnumerable<string>? untrustedRoles = null)
    {
        this.untrustedRoles = untrustedRoles is not null
            ? new HashSet<string>(untrustedRoles, StringComparer.OrdinalIgnoreCase)
            : DefaultUntrustedRoles;
    }

    public Task<MemoryAdmissionDecision> EvaluateAsync(
        MemoryAdmissionContext context,
        IReadOnlyList<Memory> existingMemories,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(context);

        if (!string.IsNullOrWhiteSpace(context.Role) && untrustedRoles.Contains(context.Role!))
        {
            if (context.Scope is MemoryScope.Agent or MemoryScope.User)
            {
                return Task.FromResult(new MemoryAdmissionDecision(
                    IsAdmitted: false,
                    ConfidenceScore: 0.0,
                    Reason: $"Role '{context.Role}' lacks authority to write to {context.Scope} scope."));
            }
        }

        return Task.FromResult(new MemoryAdmissionDecision(
            IsAdmitted: true,
            ConfidenceScore: 1.0,
            Reason: "Authority verified."));
    }
}
