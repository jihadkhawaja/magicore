namespace MagiCore;

public sealed class CompositeAdmissionGate : IAdmissionGate
{
    private readonly IReadOnlyList<IAdmissionGate> gates;

    public CompositeAdmissionGate(params IAdmissionGate[] gates)
    {
        Guard.NotNull(gates);
        this.gates = gates;
    }

    public CompositeAdmissionGate(IEnumerable<IAdmissionGate> gates)
    {
        Guard.NotNull(gates);
        this.gates = gates.ToArray();
    }

    public async Task<MemoryAdmissionDecision> EvaluateAsync(
        MemoryAdmissionContext context,
        IReadOnlyList<Memory> existingMemories,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(context);
        var reasons = new List<string>();
        var minScore = 1.0;

        foreach (var gate in gates)
        {
            var decision = await gate.EvaluateAsync(context, existingMemories, cancellationToken);
            if (!decision.IsAdmitted)
            {
                return decision;
            }
            if (decision.Reason is not null)
            {
                reasons.Add(decision.Reason);
            }
            minScore = Math.Min(minScore, decision.ConfidenceScore);
        }

        return new MemoryAdmissionDecision(
            IsAdmitted: true,
            ConfidenceScore: minScore,
            Reason: reasons.Count > 0 ? string.Join("; ", reasons) : "All admission gates passed.");
    }
}
