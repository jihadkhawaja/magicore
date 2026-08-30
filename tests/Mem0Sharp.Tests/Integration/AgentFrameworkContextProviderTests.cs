#if NET10_0
using Mem0Sharp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Mem0Sharp.Tests.Integration;

public sealed class AgentFrameworkContextProviderTests
{
    private sealed class Mem0ContextProvider : AIContextProvider
    {
        private readonly MemoryService _memory;
        private readonly string _userId;

        public Mem0ContextProvider(MemoryService memory, string userId)
        {
            _memory = memory;
            _userId = userId;
        }

        protected override async ValueTask StoreAIContextAsync(
            InvokedContext context,
            CancellationToken cancellationToken = default)
        {
            var text = string.Join("\n", context.RequestMessages.Concat(context.ResponseMessages ?? []));
            if (!string.IsNullOrWhiteSpace(text))
            {
                await _memory.AddAsync(text, new MemoryAddOptions
                {
                    UserId = _userId,
                    Infer = false
                }, cancellationToken: cancellationToken);
            }
        }

        protected override async ValueTask<AIContext> ProvideAIContextAsync(
            InvokingContext context,
            CancellationToken cancellationToken = default)
        {
            var memories = await _memory.GetAllAsync(
                new MemoryFilter(UserId: _userId),
                cancellationToken: cancellationToken);

            if (memories.Count == 0)
                return new AIContext();

            var remembered = string.Join(
                Environment.NewLine,
                memories.Select(memory => $"- {memory.Text}"));

            return new AIContext
            {
                Instructions = $"Relevant memories for this user:\n{remembered}"
            };
        }
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public List<IEnumerable<ChatMessage>> CapturedMessageBatches { get; } = [];
        public List<ChatOptions?> CapturedOptions { get; } = [];
        public string ResponseToReturn { get; set; } = "Understood.";

        public ChatClientMetadata Metadata => new("RecordingChatClient");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var list = messages.ToList();
            CapturedMessageBatches.Add(list);
            CapturedOptions.Add(options);

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, ResponseToReturn)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }

    [Fact]
    public async Task StoreAIContextAsync_AutomaticallyPersistsTurn_AndProvideAIContextAsync_InjectsMemoriesOnNextTurn()
    {
        // 1. Arrange: setup memory service, mock chat client, and Agent with Mem0ContextProvider
        var memory = new MemoryService();
        var mockChat = new RecordingChatClient();
        var provider = new Mem0ContextProvider(memory, "alice");

        var agent = new ChatClientAgent(
            mockChat,
            new ChatClientAgentOptions
            {
                ChatOptions = new ChatOptions
                {
                    Instructions = "Base assistant instruction."
                },
                AIContextProviders = [provider]
            });

        var session = await agent.CreateSessionAsync();

        // 2. Turn 1: User provides a preference
        mockChat.ResponseToReturn = "I will remember that you prefer dark mode.";
        var response1 = await agent.RunAsync("I prefer dark mode and vim keybindings.", session);

        Assert.Equal("I will remember that you prefer dark mode.", response1.ToString());

        // Verify that StoreAIContextAsync automatically stored the turn in Mem0Sharp without manual calls
        var memoriesAfterTurn1 = await memory.GetAllAsync(new MemoryFilter(UserId: "alice"));
        Assert.NotEmpty(memoriesAfterTurn1);
        Assert.Contains(memoriesAfterTurn1, m => m.Text.Contains("dark mode"));

        // 3. Turn 2: User asks about preferences
        mockChat.ResponseToReturn = "Based on your preference, I have selected dark mode.";
        var response2 = await agent.RunAsync("What theme should I use?", session);

        Assert.Equal("Based on your preference, I have selected dark mode.", response2.ToString());

        // Verify that during Turn 2, ProvideAIContextAsync injected memories into the agent context
        Assert.True(mockChat.CapturedOptions.Count >= 2);
        var turn2Options = mockChat.CapturedOptions[1];
        Assert.NotNull(turn2Options);
        Assert.Contains("Relevant memories for this user", turn2Options.Instructions ?? string.Empty);
        Assert.Contains("dark mode", turn2Options.Instructions ?? string.Empty);
    }

    [Fact]
    public async Task Mem0ContextProvider_UserScoping_IsolatesMemoriesBetweenUsers()
    {
        var memory = new MemoryService();
        var mockChat = new RecordingChatClient();

        var aliceProvider = new Mem0ContextProvider(memory, "alice");
        var bobProvider = new Mem0ContextProvider(memory, "bob");

        var aliceAgent = new ChatClientAgent(mockChat, new ChatClientAgentOptions { AIContextProviders = [aliceProvider] });
        var bobAgent = new ChatClientAgent(mockChat, new ChatClientAgentOptions { AIContextProviders = [bobProvider] });

        var aliceSession = await aliceAgent.CreateSessionAsync();
        var bobSession = await bobAgent.CreateSessionAsync();

        mockChat.ResponseToReturn = "Noted for Alice.";
        await aliceAgent.RunAsync("Alice likes TypeScript.", aliceSession);

        mockChat.ResponseToReturn = "Noted for Bob.";
        await bobAgent.RunAsync("Bob likes C#.", bobSession);

        var aliceMemories = await memory.GetAllAsync(new MemoryFilter(UserId: "alice"));
        var bobMemories = await memory.GetAllAsync(new MemoryFilter(UserId: "bob"));

        Assert.Contains(aliceMemories, m => m.Text.Contains("TypeScript"));
        Assert.DoesNotContain(aliceMemories, m => m.Text.Contains("Bob likes C#"));

        Assert.Contains(bobMemories, m => m.Text.Contains("C#"));
        Assert.DoesNotContain(bobMemories, m => m.Text.Contains("Alice likes TypeScript"));
    }

    [Fact]
    public async Task Mem0ContextProvider_BackedByVectorDataMemoryStore_WorksAsExpected()
    {
        var collection = new Mem0Sharp.Tests.Unit.VectorDataMemoryStoreTests.InMemoryTestRecordCollection<VectorData.VectorDataMemoryRecord>(
            "agent_memories",
            r => r.Id,
            r => r.Vector);
        var store = new VectorData.VectorDataMemoryStore(collection);
        await store.InitializeAsync();

        var memory = new MemoryService(store: store);
        var mockChat = new RecordingChatClient();
        var provider = new Mem0ContextProvider(memory, "alice");

        var agent = new ChatClientAgent(
            mockChat,
            new ChatClientAgentOptions
            {
                AIContextProviders = [provider]
            });

        var session = await agent.CreateSessionAsync();

        mockChat.ResponseToReturn = "Got it, your favorite language is C#.";
        await agent.RunAsync("My favorite language is C#.", session);

        var aliceMemories = await memory.GetAllAsync(new MemoryFilter(UserId: "alice"));
        Assert.Single(aliceMemories);
        Assert.Contains("favorite language is C#", aliceMemories[0].Text);
    }
}
#endif
