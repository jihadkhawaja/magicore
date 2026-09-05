using System.ComponentModel;
using ModelContextProtocol.Server;

namespace MagiCore.McpSample;

[McpServerToolType]
public sealed class McpTools(IMemoryService memory)
{
    [McpServerTool(Name = "add_memory", ReadOnly = false)]
    [Description("Store a memory in the local C# memory service.")]
    public async Task<AddResult> AddMemoryAsync(
        [Description("The memory text to store.")] string text,
        string? user_id = null,
        string? agent_id = null,
        string? run_id = null,
        bool infer = true,
        string? behavior = null,
        string? prompt = null,
        CancellationToken cancellationToken = default)
    {
        var result = await memory.AddAsync(text, new MemoryAddOptions
        {
            UserId = user_id ?? "default_user",
            AgentId = agent_id,
            RunId = run_id,
            Infer = infer,
            Behavior = ParseBehavior(behavior),
            Prompt = prompt
        }, cancellationToken);
        Log("added", result.Memories);
        return result;
    }

    [McpServerTool(Name = "search_memories", ReadOnly = true)]
    [Description("Search local memories.")]
    public async Task<IReadOnlyList<SearchResult>> SearchMemoriesAsync(
        string query,
        string? user_id = null,
        string? agent_id = null,
        string? run_id = null,
        int top_k = 10,
        double threshold = 0.1,
        bool rerank = false,
        bool explain = false,
        bool include_expired = false,
        CancellationToken cancellationToken = default)
    {
        var results = await memory.SearchAsync(query, new MemorySearchOptions
        {
            Filter = Filter(user_id, agent_id, run_id, include_expired),
            TopK = top_k,
            Threshold = threshold,
            Rerank = rerank,
            Explain = explain
        }, cancellationToken);
        Log("recalled", results.Select(result => result.Memory), query);
        return results;
    }

    [McpServerTool(Name = "get_memories", ReadOnly = true)]
    [Description("List local memories.")]
    public async Task<IReadOnlyList<Memory>> GetMemoriesAsync(
        string? user_id = null,
        string? agent_id = null,
        string? run_id = null,
        bool include_expired = false,
        CancellationToken cancellationToken = default)
    {
        var results = await memory.GetAllAsync(Filter(user_id, agent_id, run_id, include_expired), cancellationToken);
        Log("recalled", results);
        return results;
    }

    [McpServerTool(Name = "get_memory", ReadOnly = true)]
    [Description("Get one local memory.")]
    public async Task<Memory> GetMemoryAsync(string memory_id, CancellationToken cancellationToken = default)
    {
        var result = await memory.GetAsync(memory_id, cancellationToken) ?? throw new KeyNotFoundException("Memory was not found.");
        Log("recalled", [result]);
        return result;
    }

    [McpServerTool(Name = "update_memory", ReadOnly = false)]
    [Description("Update one local memory.")]
    public async Task<Memory> UpdateMemoryAsync(string memory_id, string text, CancellationToken cancellationToken = default)
    {
        var result = await memory.UpdateAsync(memory_id, new MemoryUpdate { Text = text }, cancellationToken);
        Log("updated", [result]);
        return result;
    }

    [McpServerTool(Name = "delete_memory", ReadOnly = false, Destructive = true)]
    [Description("Delete one local memory.")]
    public async Task<object> DeleteMemoryAsync(string memory_id, CancellationToken cancellationToken = default)
    {
        await memory.DeleteAsync(memory_id, cancellationToken);
        return new { deleted = true };
    }

    [McpServerTool(Name = "delete_all_memories", ReadOnly = false, Destructive = true)]
    [Description("Delete matching local memories.")]
    public Task<int> DeleteAllMemoriesAsync(
        string? user_id = null,
        string? agent_id = null,
        string? run_id = null,
        CancellationToken cancellationToken = default) =>
        memory.DeleteAllAsync(Filter(user_id, agent_id, run_id, includeExpired: true), cancellationToken);

    [McpServerTool(Name = "list_entities", ReadOnly = true)]
    [Description("List user, agent, and run identifiers in local memory.")]
    public async Task<IReadOnlyList<object>> ListEntitiesAsync(CancellationToken cancellationToken = default)
    {
        var memories = await memory.GetAllAsync(new MemoryFilter(IncludeExpired: true), cancellationToken);
        return memories.Select(memory => (Type: "user", Name: (string?)memory.UserId))
            .Concat(memories.Select(memory => (Type: "agent", Name: memory.AgentId)))
            .Concat(memories.Select(memory => (Type: "run", Name: memory.RunId)))
            .Where(entity => !string.IsNullOrWhiteSpace(entity.Name))
            .Distinct()
            .Select(entity => (object)new { type = entity.Type, name = entity.Name })
            .ToArray();
    }

    [McpServerTool(Name = "delete_entities", ReadOnly = false, Destructive = true)]
    [Description("Delete memories associated with an entity.")]
    public Task<object> DeleteEntitiesAsync(string entity_type, string entity_name, CancellationToken cancellationToken = default)
    {
        var filter = entity_type.ToLowerInvariant() switch
        {
            "user" => new MemoryFilter(UserId: entity_name, IncludeExpired: true),
            "agent" => new MemoryFilter(AgentId: entity_name, IncludeExpired: true),
            "run" => new MemoryFilter(RunId: entity_name, IncludeExpired: true),
            _ => throw new ArgumentException("entity_type must be user, agent, or run.")
        };
        return DeleteEntitiesCoreAsync(filter, cancellationToken);
    }

    private async Task<object> DeleteEntitiesCoreAsync(MemoryFilter filter, CancellationToken cancellationToken) =>
        new { deleted = await memory.DeleteAllAsync(filter, cancellationToken) };

    private static MemoryFilter Filter(string? userId, string? agentId, string? runId, bool includeExpired) =>
        new(userId, agentId, runId, IncludeExpired: includeExpired);

    private static MemoryBehavior ParseBehavior(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return MemoryBehavior.Normal;
        var normalized = value.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        return Enum.TryParse<MemoryBehavior>(normalized, true, out var behavior) && Enum.IsDefined(behavior)
            ? behavior
            : throw new ArgumentException("'behavior' must be normal, dreaming, random_thoughts, or personal_memory.");
    }

    private static void Log(string action, IEnumerable<Memory> memories, string? query = null)
    {
        var prefix = query is null ? $"[magicore] {action}" : $"[magicore] {action} for '{query}'";
        var entries = memories.Select(memory => $"{memory.Id} ({memory.UserId}): {memory.Text}").ToArray();
        Console.Error.WriteLine(entries.Length == 0 ? $"{prefix}: none" : $"{prefix}: {string.Join(" | ", entries)}");
    }
}