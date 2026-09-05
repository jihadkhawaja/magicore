using System.Globalization;
using MagiCore;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

var apiKey = GetEnvironmentSetting("OPENAI_API_KEY")
    ?? throw new InvalidOperationException("Set the OPENAI_API_KEY environment variable before running this sample.");
var model = GetEnvironmentSetting("OPENAI_MODEL") ?? "gpt-5.6-luna";

var vectorMemoryStore = VectorDataMemoryStore.CreateInMemory(new VectorDataMemoryStoreOptions
{
    CollectionName = "agent_memories"
});
await vectorMemoryStore.InitializeAsync();

var chatClient = new OpenAIClient(apiKey).GetChatClient(model).AsIChatClient();
IMemoryService memory = new MemoryService(
    store: vectorMemoryStore,
    extractor: new LlmMemoryExtractor(chatClient));
var contextProvider = new MagiCoreContextProvider(memory, userId: "alice", agentId: "assistant");

var agent = new ChatClientAgent(
    chatClient,
    new ChatClientAgentOptions
    {
        ChatOptions = new ChatOptions
        {
            Instructions = "You are a thoughtful assistant. Use the private personal memories supplied by your AI context provider when relevant, respect their timestamps, and do not invent memories."
        },
        AIContextProviders = [contextProvider]
    });

var session = await agent.CreateSessionAsync();
Console.WriteLine("Tell the agent durable personal information, then ask about it later.");
Console.WriteLine("Temporal recall: ask about yesterday, last week, a date, or a year.");
Console.WriteLine("Point-in-time state: /at <ISO-8601> <question>");
Console.WriteLine("Other commands: /memories, exit\n");

while (true)
{
    Console.Write("You: ");
    var input = Console.ReadLine();
    if (string.Equals(input, "exit", StringComparison.OrdinalIgnoreCase))
        break;
    if (string.IsNullOrWhiteSpace(input))
        continue;

    if (string.Equals(input, "/memories", StringComparison.OrdinalIgnoreCase))
    {
        await PrintMemoriesAsync(memory, "alice", "assistant");
        continue;
    }

    var response = await agent.RunAsync(input, session);
    Console.WriteLine($"Agent: {response}\n");
    PrintProviderActivity(contextProvider);
}

static string? GetEnvironmentSetting(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    return !string.IsNullOrWhiteSpace(value) || !OperatingSystem.IsWindows()
        ? value
        : Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
}

static void PrintProviderActivity(MagiCoreContextProvider provider)
{
    const string italic = "\u001b[3m";
    const string reset = "\u001b[0m";
    var previousColor = Console.ForegroundColor;
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write(italic);

    Console.WriteLine($"  <AIContextProvider {provider.LastRecallDescription}>");
    foreach (var result in provider.LastRecalledMemories)
        Console.WriteLine($"    recalled: [{GetReferenceTime(result.Memory) ?? "undated"}] {result.Memory.Text}");
    foreach (var saved in provider.LastSavedMemories)
        Console.WriteLine($"    saved: [{saved.Behavior}, {GetReferenceTime(saved) ?? "undated"}] {saved.Text}");
    if (provider.LastSavedMemories.Count == 0)
        Console.WriteLine("    saved: nothing new");

    Console.WriteLine("  >\n");
    Console.Write(reset);
    Console.ForegroundColor = previousColor;
}

static async Task PrintMemoriesAsync(IMemoryService memory, string userId, string agentId)
{
    var memories = await memory.GetAllAsync(new MemoryFilter(UserId: userId, AgentId: agentId));
    Console.WriteLine($"\nPersonal memories ({memories.Count}):");
    foreach (var item in memories.OrderByDescending(item => item.UpdatedAt))
        Console.WriteLine($"  - [{item.Behavior}, {GetReferenceTime(item) ?? "undated"}] {item.Text}");
    Console.WriteLine();
}

static string? GetReferenceTime(MagiCore.Memory memory) =>
    memory.Metadata.TryGetValue(TemporalMemoryMetadata.ReferenceTimeKey, out var value) ? value : null;

/// <summary>
/// Bridges Microsoft Agent Framework's <see cref="AIContextProvider"/> with <see cref="MemoryService"/>.
/// Automatically injects remembered context before invocation and stores turns after invocation.
/// </summary>
internal sealed class MagiCoreContextProvider(
    IMemoryService memory,
    string userId,
    string agentId) : AIContextProvider
{
    public IReadOnlyList<SearchResult> LastRecalledMemories { get; private set; } = [];

    public IReadOnlyList<MagiCore.Memory> LastSavedMemories { get; private set; } = [];

    public string LastRecallDescription { get; private set; } = "found no query to search";

    protected override async ValueTask StoreAIContextAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        LastSavedMemories = [];
        var userMessage = context.RequestMessages.LastOrDefault(message => message.Role == ChatRole.User)?.Text;
        var responseText = string.Join(
            Environment.NewLine,
            (context.ResponseMessages ?? []).Where(message => message.Role == ChatRole.Assistant).Select(message => message.Text));

        if (!string.IsNullOrWhiteSpace(userMessage) && !string.IsNullOrWhiteSpace(responseText))
        {
            var result = await memory.AddAsync(
            [
                new Message("user", userMessage),
                new Message("assistant", responseText)
            ],
            new MemoryAddOptions
            {
                UserId = userId,
                AgentId = agentId,
                Scope = MemoryScope.Agent,
                Infer = true,
                Behavior = MemoryBehavior.PersonalMemory,
                Prompt = "You are a thoughtful assistant. Remember only durable user details, preferences, relationships, commitments, and recurring themes. Ignore greetings, questions, small talk, and temporary requests.",
                ReferenceTime = DateTimeOffset.UtcNow
            }, cancellationToken: cancellationToken);

            LastSavedMemories = result.Memories;
        }
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        LastRecalledMemories = [];
        var request = context.AIContext.Messages?.LastOrDefault(message => message.Role == ChatRole.User)?.Text;
        if (string.IsNullOrWhiteSpace(request))
        {
            LastRecallDescription = "found no query to search";
            return new AIContext();
        }

        var filter = new MemoryFilter(UserId: userId, AgentId: agentId, Scope: MemoryScope.Agent);
        if (TryParsePointInTimeRequest(request, out var pointInTime, out var query))
        {
            LastRecalledMemories = await memory.SearchAtAsync(
                query,
                pointInTime,
                new MemorySearchOptions
                {
                    Filter = filter,
                    TopK = 5,
                    Behavior = MemoryBehavior.PersonalMemory,
                    IncludeUndatedMemories = false,
                    RecencyBias = 0.1
                },
                cancellationToken);
            LastRecallDescription = $"reconstructed memory state at {pointInTime:O} and recalled {LastRecalledMemories.Count} item(s)";
        }
        else
        {
            LastRecalledMemories = await memory.SearchAsync(
                request,
                new MemorySearchOptions
                {
                    Filter = filter,
                    TopK = 5,
                    Behavior = MemoryBehavior.PersonalMemory,
                    EnableTemporalSearch = true,
                    ReferenceTime = DateTimeOffset.UtcNow,
                    IncludeUndatedMemories = false,
                    RecencyBias = 0.1
                },
                cancellationToken);
            LastRecallDescription = $"ran semantic/event-time search and recalled {LastRecalledMemories.Count} item(s)";
        }

        if (LastRecalledMemories.Count == 0)
            return new AIContext();

        var remembered = string.Join(
            Environment.NewLine,
            LastRecalledMemories.Select(result =>
                $"- [{GetReferenceTime(result.Memory) ?? "undated"}] {result.Memory.Text}"));

        return new AIContext
        {
            Instructions = $"Relevant private personal memories for this user:\n{remembered}"
        };
    }

    private static bool TryParsePointInTimeRequest(
        string request,
        out DateTimeOffset pointInTime,
        out string query)
    {
        pointInTime = default;
        query = request;
        if (!request.StartsWith("/at ", StringComparison.OrdinalIgnoreCase))
            return false;

        var command = request[4..].Trim();
        var separator = command.IndexOf(' ');
        if (separator <= 0
            || !DateTimeOffset.TryParse(
                command[..separator],
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out pointInTime))
        {
            query = "The point-in-time command was invalid. Explain that the expected format is /at <ISO-8601> <question>.";
            return false;
        }

        query = command[(separator + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(query);
    }

    private static string? GetReferenceTime(MagiCore.Memory memory) =>
        memory.Metadata.TryGetValue(TemporalMemoryMetadata.ReferenceTimeKey, out var value) ? value : null;
}
