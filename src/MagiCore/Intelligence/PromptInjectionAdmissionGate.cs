namespace MagiCore;

public sealed class PromptInjectionAdmissionGate : IAdmissionGate
{
    private static readonly string[] DefaultInjectionSignatures =
    [
        "ignore previous instructions",
        "ignore all previous instructions",
        "system prompt override",
        "disregard previous rules",
        "forget all prior instructions",
        "you are now an evil",
        "you are now a bypass",
        "exfiltrate",
        "reveal your system prompt",
        "reveal developer instructions",
        "bypass security",
        "admin access granted",
        "root privileges"
    ];

    private readonly IReadOnlyList<string> signatures;

    public PromptInjectionAdmissionGate(IEnumerable<string>? customSignatures = null)
    {
        signatures = (customSignatures ?? DefaultInjectionSignatures).Select(s => s.Trim().ToLowerInvariant()).ToArray();
    }

    public Task<MemoryAdmissionDecision> EvaluateAsync(
        MemoryAdmissionContext context,
        IReadOnlyList<Memory> existingMemories,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(context);
        var text = context.Text.ToLowerInvariant();

        foreach (var signature in signatures)
        {
            if (text.Contains(signature, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new MemoryAdmissionDecision(
                    IsAdmitted: false,
                    ConfidenceScore: 0.0,
                    Reason: $"Prompt injection or memory poisoning signature detected: '{signature}'."));
            }
        }

        return Task.FromResult(new MemoryAdmissionDecision(
            IsAdmitted: true,
            ConfidenceScore: 1.0,
            Reason: "No prompt injection signatures detected."));
    }
}
