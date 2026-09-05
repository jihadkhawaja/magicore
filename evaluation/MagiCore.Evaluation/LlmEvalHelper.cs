using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace MagiCore.Evaluation;

/// <summary>
/// Generates answers from retrieved memories and judges them against ground truth,
/// following the LOCOMO benchmark's J-score methodology (binary LLM judge with
/// partial-credit, paraphrase, and date-tolerance rules) plus token-F1 and BLEU-1
/// answer-quality metrics used by popular memory evaluations.
/// </summary>
internal sealed partial class LlmEvalHelper(IChatClient answerClient, IChatClient judgeClient)
{
    private const string AnswerSystemPrompt =
        "You are a precise assistant answering questions about a past conversation, using only the retrieved memories below. " +
        "Answer in one or two short sentences with the most specific detail available (names, numbers, places beat generic descriptions). " +
        "If the memories do not contain the information, say exactly: \"I don't have that information.\" " +
        "Do not invent details.";

    private const string JudgeSystemPrompt =
        "You are evaluating conversational AI memory recall. Return JSON only with the format requested.";

    // Unified judge rules adapted from the LOCOMO benchmark's judge prompt.
    private const string JudgeRules = """
        Label the generated answer as CORRECT or WRONG.

        ## Rules

        1. **PARTIAL CREDIT**: If the generated answer includes AT LEAST ONE correct item from the gold answer's list, mark CORRECT. Only mark WRONG if NONE of the gold answer items appear.
        2. **PARAPHRASES COUNT**: Same concept in different words is CORRECT. Judge semantic meaning, not exact wording.
        3. **EXTRA DETAIL IS FINE**: A longer answer that includes the gold answer's key facts plus additional information is CORRECT.
        4. **DATE TOLERANCE**: Dates within 14 days of each other are CORRECT. Durations within 50% are CORRECT.
        5. **SEMANTIC OVERLAP**: Judge whether the generated answer addresses the same topic and captures the core idea of the gold answer.
        6. **SAME REFERENT**: If the generated answer references the same named entity or concept as the gold answer, mark CORRECT.
        7. **FOCUS ON KNOWLEDGE, NOT WORDING**: Only mark WRONG when the generated answer demonstrates a genuinely different or incorrect understanding.
        8. **ADVERSARIAL QUESTIONS**: When the gold answer states the information was never discussed, mark CORRECT only if the generated answer clearly states the information is not available instead of guessing.

        ## ONLY mark WRONG if:
        - The generated answer contains ZERO correct items from the gold answer
        - The answer addresses a completely different topic
        - For adversarial questions: the answer invents details instead of declining

        Return JSON with "reasoning" (one sentence) and "label" (CORRECT or WRONG). Do NOT include both labels.
        """;

    public async Task<string> GenerateAnswerAsync(string question, IReadOnlyList<SearchResult> memories, CancellationToken cancellationToken)
    {
        var context = memories.Count == 0
            ? "(no memories retrieved)"
            : string.Join("\n", memories.Select((result, index) => $"{index + 1}. {result.Memory.Text}"));
        var response = await answerClient.GetResponseAsync(
        [
            new ChatMessage(ChatRole.System, AnswerSystemPrompt),
            new ChatMessage(ChatRole.User, $"Retrieved memories:\n{context}\n\nQuestion: {question}")
        ], cancellationToken: cancellationToken);
        return response.Text ?? string.Empty;
    }

    public async Task<JudgeOutcome> JudgeAsync(string question, string expectedAnswer, string generatedAnswer, CancellationToken cancellationToken)
    {
        var response = await judgeClient.GetResponseAsync(
        [
            new ChatMessage(ChatRole.System, JudgeSystemPrompt),
            new ChatMessage(ChatRole.User, $"{JudgeRules}\n\n## Question\nQuestion: {question}\nGold answer: {expectedAnswer}\nGenerated answer: {generatedAnswer}")
        ], cancellationToken: cancellationToken);

        var (label, reasoning) = ParseJudgment(response.Text ?? string.Empty);
        return new JudgeOutcome(
            Correct: string.Equals(label, "CORRECT", StringComparison.OrdinalIgnoreCase),
            Reasoning: reasoning,
            F1: TokenF1(expectedAnswer, generatedAnswer, questionIsAdversarial: expectedAnswer.Contains("never", StringComparison.OrdinalIgnoreCase)),
            Bleu1: Bleu1(expectedAnswer, generatedAnswer));
    }

    private static (string Label, string Reasoning) ParseJudgment(string response)
    {
        try
        {
            var trimmed = response.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewline = trimmed.IndexOf('\n');
                var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (firstNewline >= 0 && lastFence > firstNewline)
                {
                    trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
                }
            }
            using var document = JsonDocument.Parse(trimmed);
            var label = document.RootElement.TryGetProperty("label", out var labelElement) ? labelElement.GetString() ?? string.Empty : string.Empty;
            var reasoning = document.RootElement.TryGetProperty("reasoning", out var reasoningElement) ? reasoningElement.GetString() ?? string.Empty : string.Empty;
            if (label.Length > 0) return (label, reasoning);
        }
        catch (JsonException)
        {
            // Fall through to the regex fallback below.
        }
        return (VerdictPattern().IsMatch(response) ? "CORRECT" : "WRONG", string.Empty);
    }

    /// <summary>Token-level F1 between the gold and generated answer, after normalization.</summary>
    internal static double TokenF1(string gold, string generated, bool questionIsAdversarial = false)
    {
        var goldTokens = Tokenize(gold);
        var generatedTokens = Tokenize(generated);
        if (goldTokens.Count == 0 || generatedTokens.Count == 0) return 0;

        // For adversarial ("never discussed") questions the gold is a refusal explanation;
        // token overlap is meaningless, so score refusals by whether the system declined.
        if (questionIsAdversarial)
        {
            var declined = generated.Contains("don't have", StringComparison.OrdinalIgnoreCase)
                || generated.Contains("not mentioned", StringComparison.OrdinalIgnoreCase)
                || generated.Contains("never", StringComparison.OrdinalIgnoreCase)
                || generated.Contains("no information", StringComparison.OrdinalIgnoreCase)
                || generated.Contains("unknown", StringComparison.OrdinalIgnoreCase);
            return declined ? 1.0 : 0.0;
        }

        var remaining = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var token in goldTokens) remaining[token] = remaining.TryGetValue(token, out var count) ? count + 1 : 1;
        var overlap = 0;
        foreach (var token in generatedTokens)
        {
            if (remaining.TryGetValue(token, out var count) && count > 0)
            {
                overlap++;
                remaining[token] = count - 1;
            }
        }
        if (overlap == 0) return 0;
        var precision = (double)overlap / generatedTokens.Count;
        var recall = (double)overlap / goldTokens.Count;
        return 2 * precision * recall / (precision + recall);
    }

    /// <summary>BLEU-1 (unigram precision with brevity penalty) between gold and generated answer.</summary>
    internal static double Bleu1(string gold, string generated)
    {
        var goldTokens = Tokenize(gold);
        var generatedTokens = Tokenize(generated);
        if (goldTokens.Count == 0 || generatedTokens.Count == 0) return 0;

        var goldCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var token in goldTokens) goldCounts[token] = goldCounts.TryGetValue(token, out var count) ? count + 1 : 1;
        var clipped = 0;
        foreach (var token in generatedTokens)
        {
            if (goldCounts.TryGetValue(token, out var count) && count > 0)
            {
                clipped++;
                goldCounts[token] = count - 1;
            }
        }
        var precision = (double)clipped / generatedTokens.Count;
        var brevityPenalty = generatedTokens.Count >= goldTokens.Count
            ? 1.0
            : Math.Exp(1.0 - (double)goldTokens.Count / generatedTokens.Count);
        return brevityPenalty * precision;
    }

    private static List<string> Tokenize(string text) =>
        [.. TokenPattern().Matches(text.ToLowerInvariant()).Select(match => match.Value)];

    [GeneratedRegex(@"\bCORRECT\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VerdictPattern();

    [GeneratedRegex(@"[a-z0-9']+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}

internal sealed record JudgeOutcome(bool Correct, string Reasoning, double F1, double Bleu1);
