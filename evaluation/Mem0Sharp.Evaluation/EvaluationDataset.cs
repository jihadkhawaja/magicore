using System.Text.Json;

namespace Mem0Sharp.Evaluation;

/// <summary>
/// A self-contained evaluation dataset of fictional multi-session conversations.
/// All people, places, and events are invented for benchmarking only.
/// Question categories follow the LOCOMO benchmark style: single-hop recall,
/// multi-hop reasoning, temporal reasoning, and adversarial (unanswerable) questions.
/// </summary>
internal static class EvaluationDataset
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    internal const string CategorySingleHop = "single-hop";
    internal const string CategoryMultiHop = "multi-hop";
    internal const string CategoryTemporal = "temporal";
    internal const string CategoryAdversarial = "adversarial";

    internal static readonly string[] Categories =
    [
        CategorySingleHop,
        CategoryMultiHop,
        CategoryTemporal,
        CategoryAdversarial
    ];



    internal static EvaluationDatasetSnapshot LoadSnapshot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, "evaldataset.realworld.json");
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException($"Evaluation dataset was not found: {fullPath}", fullPath);
        var dataset = JsonSerializer.Deserialize<DatasetFile>(File.ReadAllText(fullPath), JsonOptions)
            ?? throw new InvalidDataException("Evaluation dataset JSON is empty or invalid.");
        var conversations = (IReadOnlyList<EvalConversation>)(dataset.Conversations ?? []);
        var questions = (IReadOnlyList<EvalQuestion>)(dataset.Questions ?? []);
        Validate(conversations, questions);
        var categories = questions.Select(question => question.Category).Distinct(StringComparer.Ordinal).ToArray();
        return new EvaluationDatasetSnapshot(dataset.Name ?? Path.GetFileNameWithoutExtension(fullPath), conversations, questions, categories);
    }

    private static void Validate(IReadOnlyList<EvalConversation> conversations, IReadOnlyList<EvalQuestion> questions)
    {
        if (conversations.Count == 0) throw new InvalidDataException("Evaluation dataset must contain at least one conversation.");
        if (questions.Count == 0) throw new InvalidDataException("Evaluation dataset must contain at least one question.");
        var conversationIds = conversations.Select(conversation => conversation.Id).ToArray();
        if (conversationIds.Distinct(StringComparer.Ordinal).Count() != conversationIds.Length) throw new InvalidDataException("Evaluation conversation IDs must be unique.");
        if (questions.Select(question => question.Id).Distinct(StringComparer.Ordinal).Count() != questions.Count) throw new InvalidDataException("Evaluation question IDs must be unique.");
        if (questions.Any(question => !conversationIds.Contains(question.ConversationId, StringComparer.Ordinal))) throw new InvalidDataException("Every evaluation question must reference an existing conversation.");
        if (questions.Any(question => string.IsNullOrWhiteSpace(question.Category))) throw new InvalidDataException("Every evaluation question must specify a category.");
    }

    private sealed class DatasetFile
    {
        public string? Name { get; set; }
        public List<EvalConversation>? Conversations { get; set; }
        public List<EvalQuestion>? Questions { get; set; }
    }
}

internal sealed record EvaluationDatasetSnapshot(
    string Name,
    IReadOnlyList<EvalConversation> Conversations,
    IReadOnlyList<EvalQuestion> Questions,
    IReadOnlyList<string> Categories);

internal sealed record EvalConversation(
    string Id,
    string SpeakerA,
    string SpeakerB,
    IReadOnlyList<EvalSession> Sessions);

internal sealed record EvalSession(string Date, IReadOnlyList<EvalTurn> Turns);

internal sealed record EvalTurn(string Speaker, string Text);

internal sealed record EvalQuestion(
    string Id,
    string ConversationId,
    string Category,
    string Question,
    string ExpectedAnswer,
    IReadOnlyList<string> Evidence)
{
    internal bool IsAdversarial => Category == EvaluationDataset.CategoryAdversarial;
}
