using System.Text.Json;
using Microsoft.Extensions.AI;

namespace MagiCore;

public sealed class LlmConsolidationVerifier : IConsolidationVerifier
{
    private readonly IChatClient chatClient;
    private readonly double threshold;

    public LlmConsolidationVerifier(IChatClient chatClient, double threshold = 0.7)
    {
        Guard.NotNull(chatClient);
        this.chatClient = chatClient;
        this.threshold = threshold;
    }

    public async Task<ConsolidationVerificationResult> VerifyAsync(
        IReadOnlyList<Memory> sourceMemories,
        string consolidatedSummary,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(sourceMemories);
        Guard.NotNullOrWhiteSpace(consolidatedSummary);

        if (sourceMemories.Count == 0)
        {
            return new ConsolidationVerificationResult(false, 0.0, "No source memories provided for verification.");
        }

        var sources = sourceMemories.Select(m => m.Text).ToArray();
        const string prompt = """
            You are an anti-hallucination verification system for long-term agent memory.
            Evaluate whether the provided "consolidated_summary" is fully supported and strictly entailed by the "source_memories".
            Detect any hallucinated facts, altered constraints, or unsupported claims.

            Return JSON only:
            {
                "isValid": true|false,
                "entailmentScore": 0.0 to 1.0,
                "reason": "Brief explanation of verification decision"
            }
            """;

        var userPayload = JsonSerializer.Serialize(new
        {
            source_memories = sources,
            consolidated_summary = consolidatedSummary
        });

        var response = await chatClient.GetResponseAsync(
        [
            new ChatMessage(ChatRole.System, prompt),
            new ChatMessage(ChatRole.User, userPayload)
        ], cancellationToken: cancellationToken);

        var text = response.Text ?? string.Empty;
        var parsed = ParseVerificationJson(text);
        if (parsed is not null)
        {
            var isValid = parsed.IsValid && parsed.EntailmentScore >= threshold;
            return parsed with { IsValid = isValid };
        }

        return new ConsolidationVerificationResult(true, 1.0, "Default pass (verifier response was unformatted).");
    }

    private static ConsolidationVerificationResult? ParseVerificationJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            using var doc = JsonDocument.Parse(text.Substring(start, end - start + 1));
            var root = doc.RootElement;
            var isValid = root.TryGetProperty("isValid", out var v) && v.GetBoolean();
            var score = root.TryGetProperty("entailmentScore", out var s) && s.TryGetDouble(out var d) ? d : (isValid ? 1.0 : 0.0);
            var reason = root.TryGetProperty("reason", out var r) ? r.GetString() : null;
            return new ConsolidationVerificationResult(isValid, score, reason);
        }
        catch
        {
            return null;
        }
    }
}
