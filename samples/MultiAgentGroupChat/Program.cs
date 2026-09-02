using System.ClientModel;
using System.Globalization;
using Mem0Sharp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

var configuration = SampleConfiguration.Load(
    Path.Combine(AppContext.BaseDirectory, "sampleconfig.local.yaml"));

var vectorMemoryStore = VectorDataMemoryStore.CreateInMemory(new VectorDataMemoryStoreOptions
{
    CollectionName = "multi_agent_group_chat_memories"
});
await vectorMemoryStore.InitializeAsync();

var openAiClient = new OpenAIClient(
    new ApiKeyCredential(configuration.OpenAi.ApiKey),
    new OpenAIClientOptions { Endpoint = new Uri(configuration.OpenAi.Endpoint) });
var chatClient = openAiClient.GetChatClient(configuration.OpenAi.ChatModel).AsIChatClient();
IMemoryService memory = new MemoryService(
    store: vectorMemoryStore,
    extractor: new LlmMemoryExtractor(chatClient));
const string userId = "group-chat-user";

var personas = new[]
{
    new Persona("Maya", "You are warm, empathetic, and optimistic. Notice emotions and help the group feel heard."),
    new Persona("Atlas", "You are analytical and constructively skeptical. Test assumptions and favor evidence."),
    new Persona("Pixel", "You are playful and imaginative. Offer surprising ideas while staying relevant."),
    new Persona("Sage", "You are calm, concise, and practical. Synthesize the discussion into useful next steps.")
};

var agents = new List<GroupAgent>();
foreach (var persona in personas)
{
    var memoryContext = new AgentMemoryContextProvider(memory, userId, persona.Name, persona.Instructions);
    var agent = new ChatClientAgent(
        chatClient,
        new ChatClientAgentOptions
        {
            Name = persona.Name,
            ChatOptions = new ChatOptions
            {
                Instructions = $"""
                    You are {persona.Name}, one participant in a group chat with a user and three other AI agents.
                    {persona.Instructions}
                    Respond as {persona.Name} in one short conversational message. You may address other participants by name.
                    Your AI context provider automatically supplies relevant private personal memories when available.
                    Respect memory timestamps and do not invent memories.
                    Never claim to remember another agent's private memory.
                    Do not prefix your response with your name.
                    """
            },
            AIContextProviders = [memoryContext]
        });

    agents.Add(new GroupAgent(persona.Name, agent, await agent.CreateSessionAsync(), memoryContext));
}

var transcript = new List<GroupMessage>();

Console.WriteLine("=== Four-Agent Group Chat ===");
Console.WriteLine("Participants: User, Maya, Atlas, Pixel, and Sage");
Console.WriteLine("Agents use private AIContextProviders for personal, temporal, and point-in-time memory.");
Console.WriteLine("Type a message, /strict <question> to isolate long-term recall, /memories to inspect private memories, or exit to stop.\n");

while (true)
{
    Console.Write("User: ");
    var input = Console.ReadLine();
    if (string.Equals(input, "exit", StringComparison.OrdinalIgnoreCase))
        break;
    if (string.IsNullOrWhiteSpace(input))
        continue;

    if (string.Equals(input, "/memories", StringComparison.OrdinalIgnoreCase))
    {
        await PrintMemoriesAsync(memory, userId, personas);
        continue;
    }

    if (input.StartsWith("/strict ", StringComparison.OrdinalIgnoreCase))
    {
        var question = input[8..].Trim();
        if (string.IsNullOrWhiteSpace(question))
        {
            Console.WriteLine("Usage: /strict <question>\n");
            continue;
        }

        await RunStrictMemoryTestAsync(agents, question);
        continue;
    }

    transcript.Add(new GroupMessage("User", input));

    foreach (var participant in agents)
    {
        var recentConversation = string.Join(
            Environment.NewLine,
            transcript.TakeLast(16).Select(message => $"{message.Speaker}: {message.Text}"));
        var prompt = $"""
            Here is the latest shared group-chat transcript:
            {recentConversation}

            Add your next message to the conversation. React naturally and avoid repeating points already made.
            """;

        participant.MemoryContext.BeginTurn(input);
        var response = await participant.Agent.RunAsync(prompt, participant.Session);
        var responseText = response.ToString();
        transcript.Add(new GroupMessage(participant.Name, responseText));
        Console.WriteLine($"{participant.Name}: {responseText}");
        PrintProviderActivity(participant.Name, participant.MemoryContext);
    }

    Console.WriteLine();
}

static async Task RunStrictMemoryTestAsync(IEnumerable<GroupAgent> agents, string question)
{
    Console.WriteLine("\n--- Strict long-term memory test (fresh sessions, no shared transcript) ---");
    foreach (var participant in agents)
    {
        participant.MemoryContext.BeginTurn(question, storeAfterRun: false);
        var isolatedSession = await participant.Agent.CreateSessionAsync();
        var prompt = $"""
            This is a strict long-term memory test. You have no conversation history or other agents' replies.
            Your AI context provider has searched only your private long-term memory for the exact question below.
            Answer only from the supplied memory context. If none was supplied, say that you do not know.

            Question: {question}
            """;

        var response = await participant.Agent.RunAsync(prompt, isolatedSession);
        Console.WriteLine($"{participant.Name}: {response}");
        PrintStrictRecallResult(participant.Name, participant.MemoryContext);
    }
    Console.WriteLine("--- End strict test ---\n");
}

static void PrintStrictRecallResult(string agentName, AgentMemoryContextProvider memoryContext)
{
    const string italic = "\u001b[3m";
    const string reset = "\u001b[0m";
    var previousColor = Console.ForegroundColor;
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write(italic);

    if (memoryContext.LastRecalledMemories.Count == 0)
    {
        Console.WriteLine($"  <{agentName}'s AIContextProvider found no matching long-term memory>");
    }
    else
    {
        Console.WriteLine($"  <{agentName}'s AIContextProvider supplied private long-term memory:");
        foreach (var result in memoryContext.LastRecalledMemories)
            Console.WriteLine($"    - [{GetReferenceTime(result.Memory) ?? "undated"}] {result.Memory.Text}");
        Console.WriteLine("  >");
    }

    Console.Write(reset);
    Console.ForegroundColor = previousColor;
}

static void PrintProviderActivity(string agentName, AgentMemoryContextProvider memoryContext)
{
    const string italic = "\u001b[3m";
    const string reset = "\u001b[0m";
    var previousColor = Console.ForegroundColor;

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write(italic);

    Console.WriteLine($"  <{agentName}'s AIContextProvider {memoryContext.LastRecallDescription}>");
    foreach (var result in memoryContext.LastRecalledMemories)
        Console.WriteLine($"    recalled: [{GetReferenceTime(result.Memory) ?? "undated"}] {result.Memory.Text}");

    if (memoryContext.LastSavedMemories.Count == 0)
    {
        Console.WriteLine("    saved: nothing new");
    }
    else
    {
        foreach (var memory in memoryContext.LastSavedMemories)
        {
            var referenceTime = GetReferenceTime(memory) ?? "undated";
            Console.WriteLine($"    saved: [{memory.Behavior}, {referenceTime}] {memory.Text}");
        }
    }

    Console.WriteLine("  >");
    Console.Write(reset);
    Console.ForegroundColor = previousColor;
}

static string? GetReferenceTime(Mem0Sharp.Memory memory) =>
    memory.Metadata.TryGetValue(TemporalMemoryMetadata.ReferenceTimeKey, out var value) ? value : null;

static async Task PrintMemoriesAsync(IMemoryService memory, string userId, IEnumerable<Persona> personas)
{
    Console.WriteLine();
    foreach (var persona in personas)
    {
        var memories = await memory.GetAllAsync(new MemoryFilter(UserId: userId, AgentId: persona.Name));
        Console.WriteLine($"{persona.Name}'s memory ({memories.Count}):");
        foreach (var item in memories.OrderByDescending(item => item.UpdatedAt).Take(5))
            Console.WriteLine($"  - [{item.Behavior}, {GetReferenceTime(item) ?? "undated"}] {item.Text.Replace(Environment.NewLine, " ")}");
    }
    Console.WriteLine();
}

internal sealed record Persona(string Name, string Instructions);

internal sealed record GroupMessage(string Speaker, string Text);

internal sealed record GroupAgent(
    string Name,
    ChatClientAgent Agent,
    AgentSession Session,
    AgentMemoryContextProvider MemoryContext);

internal sealed class AgentMemoryContextProvider(
    IMemoryService memory,
    string userId,
    string agentId,
    string personalityInstructions) : AIContextProvider
{
    private string? currentUserMessage;
    private bool storeCurrentTurn;

    public IReadOnlyList<Mem0Sharp.Memory> LastSavedMemories { get; private set; } = [];

    public IReadOnlyList<SearchResult> LastRecalledMemories { get; private set; } = [];

    public string LastRecallDescription { get; private set; } = "found no query to search";

    public void BeginTurn(string userMessage, bool storeAfterRun = true)
    {
        currentUserMessage = userMessage;
        storeCurrentTurn = storeAfterRun;
        LastSavedMemories = [];
        LastRecalledMemories = [];
    }

    protected override async ValueTask StoreAIContextAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        var userMessage = currentUserMessage;
        currentUserMessage = null;
        if (!storeCurrentTurn || string.IsNullOrWhiteSpace(userMessage))
            return;

        var responseText = string.Join(
            Environment.NewLine,
            (context.ResponseMessages ?? [])
                .Where(message => message.Role == ChatRole.Assistant)
                .Select(message => message.Text));
        if (string.IsNullOrWhiteSpace(responseText))
            return;

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
            Prompt = $"{personalityInstructions} Remember only durable user details, preferences, relationships, commitments, and recurring themes. Ignore greetings, small talk, questions, and temporary requests.",
            ReferenceTime = DateTimeOffset.UtcNow
        }, cancellationToken: cancellationToken);

        LastSavedMemories = result.Memories;
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var request = currentUserMessage;
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
                CreateSearchOptions(filter, enableTemporalSearch: false),
                cancellationToken);
            LastRecallDescription = $"reconstructed memory state at {pointInTime:O} and recalled {LastRecalledMemories.Count} item(s)";
        }
        else
        {
            LastRecalledMemories = await memory.SearchAsync(
                request,
                CreateSearchOptions(filter, enableTemporalSearch: true),
                cancellationToken);
            LastRecallDescription = $"ran semantic/event-time search and recalled {LastRecalledMemories.Count} item(s)";
        }

        if (LastRecalledMemories.Count == 0)
            return new AIContext();

        var remembered = string.Join(
            Environment.NewLine,
            LastRecalledMemories.Select(result =>
                $"- [{GetMemoryReferenceTime(result.Memory) ?? "undated"}] {result.Memory.Text}"));

        return new AIContext
        {
            Instructions = $"Relevant private personal memories for this user:\n{remembered}"
        };
    }

    private static MemorySearchOptions CreateSearchOptions(
        MemoryFilter filter,
        bool enableTemporalSearch) => new()
    {
        Filter = filter,
        TopK = 5,
        Behavior = MemoryBehavior.PersonalMemory,
        EnableTemporalSearch = enableTemporalSearch,
        ReferenceTime = DateTimeOffset.UtcNow,
        IncludeUndatedMemories = false,
        RecencyBias = 0.1
    };

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
            return false;
        }

        query = command[(separator + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(query);
    }

    private static string? GetMemoryReferenceTime(Mem0Sharp.Memory memory) =>
        memory.Metadata.TryGetValue(TemporalMemoryMetadata.ReferenceTimeKey, out var value) ? value : null;
}