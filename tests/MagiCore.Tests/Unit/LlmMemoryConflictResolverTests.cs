using MagiCore;
using Microsoft.Extensions.AI;
using Xunit;

namespace MagiCore.Tests;

public sealed class LlmMemoryConflictResolverTests
{
    [Fact]
    public async Task ResolveAcceptsCommentsTrailingCommasAndSurroundingText()
    {
        const string response = """
            Here is the result:
            ```json
            {
              // Reconcile the changed city.
              "memory": [
                { "text": "new city", "event": "UPDATE", "id": 0, },
              ]
            }
            ```
            """;
        var existing = new Memory { Id = "memory-id", Text = "old city", UserId = "alice" };
        var resolver = new LlmMemoryConflictResolver(new StubChatClient(response));

        var decision = Assert.Single(await resolver.ResolveAsync(
            [new Message("user", "I moved")], [existing], new MemoryAddOptions()));

        Assert.Equal(MemoryAction.Update, decision.Event);
        Assert.Equal("memory-id", decision.MemoryId);
        Assert.Equal("new city", decision.Text);
    }

    [Fact]
    public async Task ResolveReturnsNoDecisionsForMalformedResponse()
    {
        var resolver = new LlmMemoryConflictResolver(new StubChatClient("not valid { json //"));

        var decisions = await resolver.ResolveAsync([], [], new MemoryAddOptions());

        Assert.Empty(decisions);
    }

    [Fact]
    public async Task ResolveSkipsDecisionFieldsWithUnexpectedTypes()
    {
        const string response = """
            { "memory": [42, { "text": 7, "event": true }, { "text": "valid fact", "event": "ADD" }] }
            """;
        var resolver = new LlmMemoryConflictResolver(new StubChatClient(response));

        var decision = Assert.Single(await resolver.ResolveAsync([], [], new MemoryAddOptions()));

        Assert.Equal(MemoryAction.Add, decision.Event);
        Assert.Equal("valid fact", decision.Text);
    }

    private sealed class StubChatClient(string response) : IChatClient
    {
        public ChatClientMetadata Metadata { get; } = new("StubChatClient");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));

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