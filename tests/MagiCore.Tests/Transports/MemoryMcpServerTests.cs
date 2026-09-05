using MagiCore;
using MagiCore.McpSample;
using System.Text.Json;
using Xunit;

namespace MagiCore.Tests;

public sealed class MemoryMcpServerTests
{
    [Fact]
    public async Task AddMemoryToolWritesToTheLocalService()
    {
        var memory = new MemoryService();
        var tools = new McpTools(memory);

        var result = await tools.AddMemoryAsync("local only", user_id: "alice", infer: false);

        Assert.False(string.IsNullOrWhiteSpace(result.Memories[0].Id));
        var stored = Assert.Single(await memory.GetAllAsync(new MemoryFilter(UserId: "alice")));
        Assert.Equal("local only", stored.Text);
    }

    [Fact]
    public async Task AddMemoryToolAcceptsSnakeCaseBehavior()
    {
        var extractor = new RecordingBehaviorExtractor();
        var memory = new MemoryService(extractor: extractor);
        var tools = new McpTools(memory);

        await tools.AddMemoryAsync("follow this thought", behavior: "random_thoughts");

        Assert.Equal(MemoryBehavior.RandomThoughts, extractor.Options!.Behavior);
        Assert.Equal("shaped thought", Assert.Single(await memory.GetAllAsync()).Text);
    }

    [Fact]
    public async Task UpdateMemoryToolUpdatesTheRequestedMemory()
    {
        var memory = new MemoryService();
        var added = await memory.AddAsync("old text");
        var tools = new McpTools(memory);

        var updated = await tools.UpdateMemoryAsync(added.Memories[0].Id, "new text");

        Assert.Equal("new text", updated.Text);
        Assert.Equal("new text", (await memory.GetAsync(updated.Id))!.Text);
    }

    [Fact]
    public async Task SearchMemoriesToolAppliesIdentityFilters()
    {
        var memory = new MemoryService();
        await memory.AddAsync("Alice likes tea", new MemoryAddOptions { UserId = "alice", Infer = false });
        await memory.AddAsync("Bob likes tea", new MemoryAddOptions { UserId = "bob", Infer = false });
        var tools = new McpTools(memory);

        var results = await tools.SearchMemoriesAsync("tea", user_id: "alice", threshold: 0);

        var result = Assert.Single(results);
        Assert.Equal("alice", result.Memory.UserId);
        Assert.Equal("Alice likes tea", result.Memory.Text);
    }

    [Fact]
    public async Task GetMemoriesToolAppliesIdentityFilters()
    {
        var memory = new MemoryService();
        await memory.AddAsync("Alice fact", new MemoryAddOptions { UserId = "alice", AgentId = "writer", Infer = false });
        await memory.AddAsync("Bob fact", new MemoryAddOptions { UserId = "bob", AgentId = "writer", Infer = false });
        var tools = new McpTools(memory);

        var results = await tools.GetMemoriesAsync(user_id: "alice", agent_id: "writer");

        var result = Assert.Single(results);
        Assert.Equal("Alice fact", result.Text);
    }

    [Fact]
    public async Task GetMemoryToolThrowsForUnknownMemory()
    {
        var tools = new McpTools(new MemoryService());

        var error = await Assert.ThrowsAsync<KeyNotFoundException>(() => tools.GetMemoryAsync("missing"));

        Assert.Contains("Memory was not found", error.Message);
    }

    [Fact]
    public async Task DeleteMemoryToolDeletesTheRequestedMemory()
    {
        var memory = new MemoryService();
        var added = await memory.AddAsync("remove me");
        var tools = new McpTools(memory);

        var result = await tools.DeleteMemoryAsync(added.Memories[0].Id);

        Assert.Equal("{\"deleted\":true}", JsonSerializer.Serialize(result));
        Assert.Null(await memory.GetAsync(added.Memories[0].Id));
    }

    [Fact]
    public async Task DeleteAllMemoriesToolDeletesOnlyMatchingIdentity()
    {
        var memory = new MemoryService();
        await memory.AddAsync("Alice fact", new MemoryAddOptions { UserId = "alice", Infer = false });
        await memory.AddAsync("Bob fact", new MemoryAddOptions { UserId = "bob", Infer = false });
        var tools = new McpTools(memory);

        var deleted = await tools.DeleteAllMemoriesAsync(user_id: "alice");

        Assert.Equal(1, deleted);
        Assert.Equal("bob", Assert.Single(await memory.GetAllAsync()).UserId);
    }

    [Fact]
    public async Task ListEntitiesToolReturnsDistinctIdentityValues()
    {
        var memory = new MemoryService();
        var tools = new McpTools(memory);
        await tools.AddMemoryAsync("first", user_id: "alice", agent_id: "writer", run_id: "run-1", infer: false);
        await tools.AddMemoryAsync("second", user_id: "alice", agent_id: "writer", run_id: "run-1", infer: false);
        await tools.AddMemoryAsync("third", user_id: "bob", infer: false);

        var entities = await tools.ListEntitiesAsync();
        var serialized = entities.Select(entity => JsonSerializer.Serialize(entity)).ToArray();

        Assert.Equal(4, entities.Count);
        Assert.Contains("{\"type\":\"user\",\"name\":\"alice\"}", serialized);
        Assert.Contains("{\"type\":\"agent\",\"name\":\"writer\"}", serialized);
        Assert.Contains("{\"type\":\"run\",\"name\":\"run-1\"}", serialized);
        Assert.Contains("{\"type\":\"user\",\"name\":\"bob\"}", serialized);
    }

    [Fact]
    public async Task DeleteEntitiesToolDeletesOnlyMatchingMemories()
    {
        var memory = new MemoryService();
        var tools = new McpTools(memory);
        await tools.AddMemoryAsync("Alice fact", user_id: "alice", infer: false);
        await tools.AddMemoryAsync("Bob fact", user_id: "bob", infer: false);

        var result = await tools.DeleteEntitiesAsync("user", "alice");

        Assert.Equal("{\"deleted\":1}", JsonSerializer.Serialize(result));
        Assert.Single(await memory.GetAllAsync());
        Assert.Equal("bob", (await memory.GetAllAsync()).Single().UserId);
    }

    [Fact]
    public async Task DeleteEntitiesToolRejectsUnknownEntityTypes()
    {
        var tools = new McpTools(new MemoryService());

        var error = await Assert.ThrowsAsync<ArgumentException>(() => tools.DeleteEntitiesAsync("team", "platform"));

        Assert.Contains("entity_type must be user, agent, or run", error.Message);
    }

    private sealed class RecordingBehaviorExtractor : IMemoryExtractor
    {
        public MemoryAddOptions? Options { get; private set; }

        public Task<IReadOnlyList<MemoryInput>> ExtractAsync(IReadOnlyList<Message> messages, MemoryAddOptions? options = null, CancellationToken cancellationToken = default)
        {
            Options = options;
            return Task.FromResult<IReadOnlyList<MemoryInput>>([new MemoryInput(options is not null ? "shaped thought" : "normal thought")]);
        }
    }
}
