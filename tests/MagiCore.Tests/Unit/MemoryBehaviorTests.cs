using MagiCore;
using Microsoft.Extensions.AI;
using Xunit;

namespace MagiCore.Tests;

public sealed class MemoryBehaviorTests
{
    [Fact]
    public void NormalIsTheDefaultBehavior()
    {
        Assert.Equal(MemoryBehavior.Normal, new MemoryAddOptions().Behavior);
    }

    [Fact]
    public async Task NormalBehaviorPreservesTheExistingExtractionPrompt()
    {
        var client = new RecordingChatClient("[]");
        var extractor = new LlmMemoryExtractor(client);

        await extractor.ExtractAsync([new Message("user", "I like tea")]);

        Assert.Equal(
            "Extract durable user facts from the conversation. Return only a JSON array of strings. Ignore greetings, questions, and temporary requests.",
            client.Messages[0].Text);
    }

    [Theory]
    [InlineData(MemoryBehavior.Dreaming, "dream-like memory consolidation")]
    [InlineData(MemoryBehavior.RandomThoughts, "spontaneous thoughts")]
    [InlineData(MemoryBehavior.PersonalMemory, "first-person perspective")]
    public async Task LlmExtractorAppliesTheSelectedBehavior(MemoryBehavior behavior, string expectedInstruction)
    {
        var client = new RecordingChatClient("[\"A shaped memory\"]");
        var extractor = new LlmMemoryExtractor(client);

        var memories = await extractor.ExtractAsync(
            [new Message("user", "I like tea")],
            new MemoryAddOptions { Behavior = behavior, Prompt = "You are a curious, optimistic research assistant." });

        Assert.Equal("A shaped memory", Assert.Single(memories).Text);
        Assert.Contains(expectedInstruction, client.Messages[0].Text);
        Assert.Contains("curious, optimistic research assistant", client.Messages[0].Text);
    }

    [Fact]
    public async Task NonNormalBehaviorShapesDirectTextThroughTheSameService()
    {
        var client = new RecordingChatClient("[\"I noticed that Alice values quiet mornings.\"]");
        var service = new MemoryService(extractor: new LlmMemoryExtractor(client));

        var result = await service.AddAsync("Alice enjoys coffee before sunrise.", new MemoryAddOptions
        {
            UserId = "alice",
            Behavior = MemoryBehavior.PersonalMemory,
            Prompt = "Speak as a thoughtful travel companion."
        });

        Assert.Equal("I noticed that Alice values quiet mornings.", Assert.Single(result.Memories).Text);
        Assert.Equal(ChatRole.User, client.Messages[1].Role);
    }

    [Fact]
    public async Task InferFalseKeepsDirectTextVerbatim()
    {
        var service = new MemoryService();

        var result = await service.AddAsync("Keep this exact text.", new MemoryAddOptions
        {
            Infer = false,
            Behavior = MemoryBehavior.Dreaming
        });

        Assert.Equal("Keep this exact text.", Assert.Single(result.Memories).Text);
    }

    [Fact]
    public async Task LlmExtractorAppliesBehaviorPrompt()
    {
        var client = new RecordingChatClient("[\"dream fragment\"]");
        var extractor = new LlmMemoryExtractor(client);

        var inputs = await extractor.ExtractAsync(
            [new Message("user", "Think about this")],
            new MemoryAddOptions { Behavior = MemoryBehavior.RandomThoughts });

        Assert.Single(inputs);
        Assert.Contains(client.Messages, m => m.Role == ChatRole.System && (m.Text ?? "").Contains("spontaneous thoughts"));
    }

    [Fact]
    public async Task ConflictResolverReceivesBehaviorInstructions()
    {
        var client = new RecordingChatClient("{\"memory\":[]}");
        var resolver = new LlmMemoryConflictResolver(client);

        await resolver.ResolveAsync(
            [new Message("user", "A half-remembered melody")],
            [],
            new MemoryAddOptions { Behavior = MemoryBehavior.Dreaming });

        Assert.Contains("dream-like memory consolidation", client.Messages[0].Text);
    }

    private sealed class RecordingChatClient(string response) : IChatClient
    {
        public IReadOnlyList<ChatMessage> Messages { get; private set; } = [];

        public ChatClientMetadata Metadata { get; } = new("RecordingChatClient");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Messages = chatMessages.ToArray();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate { Contents = [new TextContent(response)] };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}