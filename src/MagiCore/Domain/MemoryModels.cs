using Microsoft.Extensions.AI;

namespace MagiCore;

public enum MemoryScope
{
    User,
    Session,
    Agent
}

public sealed record Memory
{
    public required string Id { get; init; }
    public required string Text { get; init; }
    public required string UserId { get; init; }
    public string? AgentId { get; init; }
    public string? RunId { get; init; }
    public MemoryScope Scope { get; init; } = MemoryScope.User;
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string Hash { get; init; } = string.Empty;
    public MemoryBehavior Behavior { get; init; } = MemoryBehavior.Normal;
    public string? MemoryType { get; init; }
}

public sealed record MemoryInput(
    string Text, 
    MemoryScope Scope = MemoryScope.User, 
    IReadOnlyDictionary<string, string>? Metadata = null, 
    DateTimeOffset? ExpiresAt = null, 
    MemoryBehavior Behavior = MemoryBehavior.Normal, 
    string? MemoryType = null);

public sealed record Message(string Role, string Content, IReadOnlyList<AIContent>? Contents = null)
{
    public ChatMessage ToChatMessage()
    {
        var roleLower = Role?.ToLowerInvariant() ?? "user";
        var role = roleLower switch
        {
            "system" => ChatRole.System,
            "assistant" => ChatRole.Assistant,
            "tool" => ChatRole.Tool,
            _ => ChatRole.User
        };
        var authorName = roleLower is "system" or "assistant" or "user" or "tool" ? null : Role;
        ChatMessage chatMessage;
        if (Contents is { Count: > 0 })
        {
            chatMessage = new ChatMessage(role, Contents.ToList());
        }
        else
        {
            chatMessage = new ChatMessage(role, Content);
        }

        if (authorName is not null)
        {
            chatMessage.AuthorName = authorName;
        }
        return chatMessage;
    }

    public static Message FromChatMessage(ChatMessage chatMessage)
    {
        var role = chatMessage.AuthorName ?? chatMessage.Role.Value;
        var text = chatMessage.Text ?? string.Empty;
        var contents = chatMessage.Contents.Count > 0 ? chatMessage.Contents.ToArray() : null;
        return new Message(role, text, contents);
    }

    public static DataContent CreateDataContent(Uri uri, string mediaType = "image/jpeg")
    {
        Guard.NotNull(uri);
        return uri.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase)
            ? new DataContent(uri.ToString(), mediaType)
            : new DataContent(System.Text.Encoding.UTF8.GetBytes(uri.ToString()), mediaType);
    }

    public static DataContent CreateDataContent(string uriOrData, string mediaType = "image/jpeg")
    {
        Guard.NotNullOrWhiteSpace(uriOrData);
        return uriOrData.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            ? new DataContent(uriOrData, mediaType)
            : new DataContent(System.Text.Encoding.UTF8.GetBytes(uriOrData), mediaType);
    }

    public static Message FromImage(Uri uri, string mediaType = "image/jpeg", string role = "user") =>
        new(role, string.Empty, [CreateDataContent(uri, mediaType)]);

    public static Message FromImage(string uri, string mediaType = "image/jpeg", string role = "user") =>
        new(role, string.Empty, [CreateDataContent(uri, mediaType)]);

    public static Message FromImage(ReadOnlyMemory<byte> data, string mediaType = "image/jpeg", string role = "user") =>
        new(role, string.Empty, [new DataContent(data, mediaType)]);

    public static Message FromTextAndImage(string text, Uri uri, string mediaType = "image/jpeg", string role = "user") =>
        new(role, text, [new TextContent(text), CreateDataContent(uri, mediaType)]);

    public static Message FromTextAndImage(string text, string uri, string mediaType = "image/jpeg", string role = "user") =>
        new(role, text, [new TextContent(text), CreateDataContent(uri, mediaType)]);

    public static Message FromTextAndImage(string text, ReadOnlyMemory<byte> data, string mediaType = "image/jpeg", string role = "user") =>
        new(role, text, [new TextContent(text), new DataContent(data, mediaType)]);

    public static implicit operator ChatMessage(Message message) => message.ToChatMessage();
    public static implicit operator Message(ChatMessage chatMessage) => FromChatMessage(chatMessage);
}