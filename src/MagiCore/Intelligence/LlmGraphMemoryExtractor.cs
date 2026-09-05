using System.Text.Json;
using Microsoft.Extensions.AI;

namespace MagiCore;

public sealed class LlmGraphMemoryExtractor : IGraphMemoryExtractor
{
    private readonly IChatClient client;

    public LlmGraphMemoryExtractor(IChatClient client)
    {
        Guard.NotNull(client);
        this.client = client;
    }

    public async Task<IReadOnlyList<ExtractedRelation>> ExtractAsync(string text, CancellationToken cancellationToken = default)
    {
        var response = await client.GetResponseAsync(
        [
            new ChatMessage(ChatRole.System, "Extract factual relationships. Return only a JSON array of objects with source, relationship, and target string fields."),
            new ChatMessage(ChatRole.User, text)
        ], cancellationToken: cancellationToken);
        return ParseRelations(response.Text ?? string.Empty);
    }

    internal static IReadOnlyList<ExtractedRelation> ParseRelations(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return [];
        var json = LlmMemoryExtractor.ExtractJsonArrayPayload(response);
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<ExtractedRelation[]>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true }) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}