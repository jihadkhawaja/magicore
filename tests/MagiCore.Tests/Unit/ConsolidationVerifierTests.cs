using MagiCore;
using Microsoft.Extensions.AI;
using Xunit;

namespace MagiCore.Tests;

public sealed class ConsolidationVerifierTests
{
    [Fact]
    public async Task HeuristicVerifierPassesWhenTokenCoverageMeetsThreshold()
    {
        var verifier = new HeuristicConsolidationVerifier(minCoverage: 0.3);
        var source = new[]
        {
            new Memory { Id = "1", Text = "Alice prefers dark mode in her code editor", UserId = "alice" },
            new Memory { Id = "2", Text = "Alice uses vim keybindings and Linux", UserId = "alice" }
        };

        var validSummary = "Alice prefers dark mode and uses vim keybindings in editor";
        var result = await verifier.VerifyAsync(source, validSummary);

        Assert.True(result.IsValid);
        Assert.True(result.EntailmentScore >= 0.3);
    }

    [Fact]
    public async Task HeuristicVerifierRejectsWhenTokenCoverageIsLow()
    {
        var verifier = new HeuristicConsolidationVerifier(minCoverage: 0.5);
        var source = new[]
        {
            new Memory { Id = "1", Text = "Alice prefers dark mode", UserId = "alice" }
        };

        var driftedSummary = "Bob enjoys skydiving and extreme sports in the Himalayas";
        var result = await verifier.VerifyAsync(source, driftedSummary);

        Assert.False(result.IsValid);
        Assert.True(result.EntailmentScore < 0.5);
    }

    [Fact]
    public async Task LlmConsolidationVerifierValidatesEntailmentJsonResponse()
    {
        const string mockResponse = """
            {
                "isValid": true,
                "entailmentScore": 0.95,
                "reason": "Summary is strictly derived from source facts."
            }
            """;
        var chatClient = new MockChatClient(mockResponse);
        var verifier = new LlmConsolidationVerifier(chatClient, threshold: 0.7);

        var source = new[]
        {
            new Memory { Id = "1", Text = "Alice lives in Berlin", UserId = "alice" }
        };

        var result = await verifier.VerifyAsync(source, "Alice is based in Berlin");

        Assert.True(result.IsValid);
        Assert.Equal(0.95, result.EntailmentScore);
        Assert.Contains("strictly derived", result.Reason);
    }

    [Fact]
    public async Task LlmConsolidationVerifierRejectsHallucinatedConsolidation()
    {
        const string mockResponse = """
            {
                "isValid": false,
                "entailmentScore": 0.2,
                "reason": "Altered city from Berlin to Tokyo without basis."
            }
            """;
        var chatClient = new MockChatClient(mockResponse);
        var verifier = new LlmConsolidationVerifier(chatClient, threshold: 0.7);

        var source = new[]
        {
            new Memory { Id = "1", Text = "Alice lives in Berlin", UserId = "alice" }
        };

        var result = await verifier.VerifyAsync(source, "Alice moved to Tokyo");

        Assert.False(result.IsValid);
        Assert.Equal(0.2, result.EntailmentScore);
        Assert.Contains("Tokyo", result.Reason);
    }

    [Fact]
    public async Task ConsolidateAsyncRejectsWhenVerificationFails()
    {
        var rejectingVerifier = new HeuristicConsolidationVerifier(minCoverage: 0.8);
        var service = new MemoryService(consolidationVerifier: rejectingVerifier);

        await service.AddAsync("Alice lives in Berlin", "alice");
        await service.AddAsync("Alice works on C#", "alice");

        // The heuristic verifier with 0.8 threshold will evaluate the concatenated summary
        // If we set minCoverage to 0.99 with unmatched tokens, it will reject
        var strictVerifier = new StrictRejectVerifier();
        var strictService = new MemoryService(consolidationVerifier: strictVerifier);
        await strictService.AddAsync("Alice lives in Berlin", "alice");

        var consolidated = await strictService.ConsolidateAsync(new MemoryFilter(UserId: "alice"));
        Assert.Empty(consolidated);
    }

    private sealed class StrictRejectVerifier : IConsolidationVerifier
    {
        public Task<ConsolidationVerificationResult> VerifyAsync(IReadOnlyList<Memory> sourceMemories, string consolidatedSummary, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConsolidationVerificationResult(false, 0.0, "Strict verification rejection."));
    }

    private sealed class MockChatClient(string response) : IChatClient
    {
        public ChatClientMetadata Metadata { get; } = new("MockChatClient");

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
