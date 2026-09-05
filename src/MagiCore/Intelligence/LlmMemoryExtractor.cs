using System.Text.Json;
using Microsoft.Extensions.AI;

namespace MagiCore;

public sealed class LlmMemoryExtractor : IMemoryExtractor
{
    private readonly IChatClient client;

    public LlmMemoryExtractor(IChatClient client)
    {
        Guard.NotNull(client);
        this.client = client;
    }

    public async Task<IReadOnlyList<MemoryInput>> ExtractAsync(IReadOnlyList<Message> messages, MemoryAddOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0) return [];
        var instructions = options is null ? MemoryBehaviorPrompts.NormalExtraction : MemoryBehaviorPrompts.ForExtraction(options);
        var hasMultimodal = messages.Any(m => m.Contents is { Count: > 0 } contents && contents.Any(c => c is not TextContent));
        List<ChatMessage> chatMessages;

        if (!hasMultimodal)
        {
            var conversationText = string.Join("\n", messages.Select(m => $"{m.Role}: {m.Content}"));
            chatMessages = new List<ChatMessage>
            {
                new(ChatRole.System, instructions),
                new(ChatRole.User, $"Conversation:\n{conversationText}\n\nReturn only a JSON array of strings.")
            };
        }
        else
        {
            chatMessages = new List<ChatMessage>
            {
                new(ChatRole.System, instructions)
            };
            foreach (var message in messages)
            {
                chatMessages.Add(message.ToChatMessage());
            }
            chatMessages.Add(new(ChatRole.User, "Extract all salient long-term memory facts from the conversation and any attached images/media above. Return only a JSON array of strings."));
        }

        var response = await client.GetResponseAsync(chatMessages, cancellationToken: cancellationToken);
        var text = response.Text ?? string.Empty;
        return ParseFacts(text);
    }

    internal static IReadOnlyList<MemoryInput> ParseFacts(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return [];
        var json = ExtractJsonArrayPayload(response);
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var facts = JsonSerializer.Deserialize<string[]>(json, new JsonSerializerOptions { AllowTrailingCommas = true }) ?? [];
            return facts.Where(fact => !string.IsNullOrWhiteSpace(fact)).Select(fact => new MemoryInput(fact.Trim())).ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static string ExtractJsonArrayPayload(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
            {
                var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (lastFence > firstNewline)
                {
                    trimmed = trimmed.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
                }
            }
        }
        var start = trimmed.IndexOf('[');
        var end = trimmed.LastIndexOf(']');
        return start >= 0 && end > start ? trimmed.Substring(start, end - start + 1) : trimmed;
    }
}