namespace MagiCore;

public sealed record ConsolidationVerificationResult(
    bool IsValid, 
    double EntailmentScore = 1.0, 
    string? Reason = null);

public interface IConsolidationVerifier
{
    Task<ConsolidationVerificationResult> VerifyAsync(
        IReadOnlyList<Memory> sourceMemories, 
        string consolidatedSummary, 
        CancellationToken cancellationToken = default);
}
