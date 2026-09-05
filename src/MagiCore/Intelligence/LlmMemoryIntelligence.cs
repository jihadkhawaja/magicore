using System.Text.Json;
using Microsoft.Extensions.AI;

namespace MagiCore;

public sealed class LlmMemoryConflictResolver : IMemoryConflictResolver
{
    private readonly IChatClient client;

    public LlmMemoryConflictResolver(IChatClient client)
    {
        Guard.NotNull(client);
        this.client = client;
    }

    public async Task<IReadOnlyList<MemoryDecision>> ResolveAsync(IReadOnlyList<Message> messages, IReadOnlyList<Memory> existingMemories, MemoryAddOptions options, CancellationToken cancellationToken = default)
    {
        const string normalPrompt = "Extract durable facts and reconcile them with existing memories. Return JSON only: {\"memory\":[{\"text\":\"fact\",\"event\":\"ADD|UPDATE|DELETE|NONE\",\"id\":\"existing numeric id when required\"}]}";
        var existing = existingMemories.Select((memory, index) => new { id = index.ToString(), text = memory.Text }).ToArray();

        var systemPrompt = MemoryBehaviorPrompts.ForConflictResolution(normalPrompt, options);
        var userContent = JsonSerializer.Serialize(new { existing, messages, instructions = options.Prompt });

        var chatMessages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userContent)
        };

        var response = await client.GetResponseAsync(chatMessages, cancellationToken: cancellationToken);
        var responseText = response.Text ?? string.Empty;

        using var document = ParseResponse(responseText);
        if (document is null) return [];
        if (!document.RootElement.TryGetProperty("memory", out var items) || items.ValueKind != JsonValueKind.Array) return [];
        var decisions = new List<MemoryDecision>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var text = item.TryGetProperty("text", out var textValue) && textValue.ValueKind == JsonValueKind.String
                ? textValue.GetString() ?? string.Empty
                : string.Empty;
            var eventName = item.TryGetProperty("event", out var eventValue) && eventValue.ValueKind == JsonValueKind.String
                ? eventValue.GetString()
                : null;
            if (!Enum.TryParse<MemoryAction>(eventName, true, out var eventType)) continue;
            string? memoryId = null;
            if (item.TryGetProperty("id", out var idValue) && TryGetIndex(idValue, out var index) && index >= 0 && index < existingMemories.Count)
            {
                memoryId = existingMemories[index].Id;
            }
            if (eventType is MemoryAction.Update or MemoryAction.Delete && memoryId is null) continue;
            decisions.Add(new MemoryDecision(text.Trim(), eventType, memoryId));
        }
        return decisions;
    }

    private static bool TryGetIndex(JsonElement value, out int index)
    {
        if (value.ValueKind == JsonValueKind.Number) return value.TryGetInt32(out index);
        if (value.ValueKind == JsonValueKind.String) return int.TryParse(value.GetString(), out index);
        index = 0;
        return false;
    }

    private static JsonDocument? ParseResponse(string response)
    {
        var start = response.IndexOf('{');
        var end = response.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            return JsonDocument.Parse(
                response.Substring(start, end - start + 1),
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed class LlmProceduralMemoryGenerator : IProceduralMemoryGenerator
{
    private readonly IChatClient client;

    public LlmProceduralMemoryGenerator(IChatClient client)
    {
        Guard.NotNull(client);
        this.client = client;
    }

    public async Task<string> GenerateAsync(IReadOnlyList<Message> messages, string? prompt = null, CancellationToken cancellationToken = default)
    {
        var conversationText = string.Join("\n", messages.Select(m => $"{m.Role}: {m.Content}"));
        var chatMessages = new List<ChatMessage>
        {
            new(ChatRole.System, prompt ?? "Summarize the agent procedure as concise, reusable steps. Preserve tool names, ordering, decisions, and failure recovery."),
            new(ChatRole.User, $"Procedure Trace:\n{conversationText}")
        };

        var response = await client.GetResponseAsync(chatMessages, cancellationToken: cancellationToken);
        return response.Text ?? string.Empty;
    }
}