using Microsoft.Extensions.AI;

namespace MagiCore;

public sealed class TelemetryMemoryService : IMemoryService
{
    private readonly IMemoryService inner;
    private readonly IMemoryTelemetry telemetry;

    public TelemetryMemoryService(IMemoryService inner, IMemoryTelemetry telemetry)
    {
        this.inner = inner;
        this.telemetry = telemetry;
    }

    public Task<AddResult> AddAsync(string text, MemoryAddOptions? options = null, CancellationToken cancellationToken = default) =>
        CaptureAsync<AddResult>("magicore.add", () => inner.AddAsync(text, options, cancellationToken), new Dictionary<string, object?> { ["input_type"] = "text", ["infer"] = options?.Infer, ["behavior"] = options?.Behavior.ToString() }, cancellationToken);

    public Task<AddResult> AddAsync(string text, string userId, string? agentId = null, string? runId = null, MemoryScope scope = MemoryScope.User, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default) =>
        AddAsync(text, new MemoryAddOptions { UserId = userId, AgentId = agentId, RunId = runId, Scope = scope, Metadata = metadata }, cancellationToken);

    public Task<AddResult> AddAsync(IEnumerable<Message> messages, MemoryAddOptions? options = null, CancellationToken cancellationToken = default) =>
        CaptureAsync<AddResult>("magicore.add", () => inner.AddAsync(messages, options, cancellationToken), new Dictionary<string, object?> { ["input_type"] = "messages", ["infer"] = options?.Infer, ["behavior"] = options?.Behavior.ToString() }, cancellationToken);

    public Task<AddResult> AddAsync(IEnumerable<ChatMessage> chatMessages, MemoryAddOptions? options = null, CancellationToken cancellationToken = default) =>
        AddAsync(chatMessages.Select(Message.FromChatMessage), options, cancellationToken);

    public Task<AddResult> AddAsync(IEnumerable<Message> messages, string userId, string? agentId = null, string? runId = null, MemoryScope scope = MemoryScope.User, CancellationToken cancellationToken = default) =>
        AddAsync(messages, new MemoryAddOptions { UserId = userId, AgentId = agentId, RunId = runId, Scope = scope }, cancellationToken);

    public Task<AddResult> AddAsync(IEnumerable<ChatMessage> chatMessages, string userId, string? agentId = null, string? runId = null, MemoryScope scope = MemoryScope.User, CancellationToken cancellationToken = default) =>
        AddAsync(chatMessages.Select(Message.FromChatMessage), new MemoryAddOptions { UserId = userId, AgentId = agentId, RunId = runId, Scope = scope }, cancellationToken);

    public Task<AddResult> AddManyAsync(IEnumerable<string> texts, MemoryAddOptions? options = null, CancellationToken cancellationToken = default) =>
        CaptureAsync<AddResult>("magicore.add_many", () => inner.AddManyAsync(texts, options, cancellationToken), cancellationToken: cancellationToken);

    public Task<AddResult> AddAsync(DataContent image, MemoryAddOptions? options = null, CancellationToken cancellationToken = default) =>
        CaptureAsync<AddResult>("magicore.add_image", () => inner.AddAsync(image, options, cancellationToken), new Dictionary<string, object?> { ["input_type"] = "image", ["media_type"] = image.MediaType }, cancellationToken);

    public Task<AddResult> AddAsync(ReadOnlyMemory<byte> imageData, string mediaType, MemoryAddOptions? options = null, CancellationToken cancellationToken = default) =>
        AddAsync(new DataContent(imageData, mediaType), options, cancellationToken);

    public Task<AddResult> AddAsync(Uri imageUri, string mediaType = "image/jpeg", MemoryAddOptions? options = null, CancellationToken cancellationToken = default) =>
        AddAsync(Message.CreateDataContent(imageUri, mediaType), options, cancellationToken);

    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, MemorySearchOptions? options = null, CancellationToken cancellationToken = default) =>
        CaptureAsync<IReadOnlyList<SearchResult>>("magicore.search", () => inner.SearchAsync(query, options, cancellationToken), new Dictionary<string, object?> { ["top_k"] = options?.TopK, ["rerank"] = options?.Rerank, ["explain"] = options?.Explain }, cancellationToken);

    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, MemoryFilter? filter, int? topK = null, CancellationToken cancellationToken = default) =>
        SearchAsync(query, new MemorySearchOptions { Filter = filter, TopK = topK ?? 5 }, cancellationToken);

    public Task<IReadOnlyList<SearchResult>> SearchAtAsync(string query, DateTimeOffset pointInTime, MemorySearchOptions? options = null, CancellationToken cancellationToken = default) =>
        CaptureAsync<IReadOnlyList<SearchResult>>("magicore.search_at", () => inner.SearchAtAsync(query, pointInTime, options, cancellationToken), new Dictionary<string, object?> { ["point_in_time"] = pointInTime, ["top_k"] = options?.TopK }, cancellationToken);

    public Task<IReadOnlyList<SearchResult>> SearchAsync(DataContent image, MemorySearchOptions? options = null, CancellationToken cancellationToken = default) =>
        CaptureAsync<IReadOnlyList<SearchResult>>("magicore.search_image", () => inner.SearchAsync(image, options, cancellationToken), new Dictionary<string, object?> { ["top_k"] = options?.TopK }, cancellationToken);

    public Task<IReadOnlyList<SearchResult>> SearchAsync(ReadOnlyMemory<byte> imageData, string mediaType, MemorySearchOptions? options = null, CancellationToken cancellationToken = default) =>
        SearchAsync(new DataContent(imageData, mediaType), options, cancellationToken);

    public Task<IReadOnlyList<SearchResult>> SearchAsync(Uri imageUri, string mediaType = "image/jpeg", MemorySearchOptions? options = null, CancellationToken cancellationToken = default) =>
        SearchAsync(Message.CreateDataContent(imageUri, mediaType), options, cancellationToken);

    public Task<IReadOnlyList<IReadOnlyList<SearchResult>>> SearchManyAsync(IEnumerable<string> queries, MemorySearchOptions? options = null, CancellationToken cancellationToken = default) =>
        CaptureAsync<IReadOnlyList<IReadOnlyList<SearchResult>>>("magicore.search_many", () => inner.SearchManyAsync(queries, options, cancellationToken), new Dictionary<string, object?> { ["top_k"] = options?.TopK, ["include_non_factual"] = options?.IncludeNonFactual }, cancellationToken);

    public Task<IReadOnlyList<IReadOnlyList<SearchResult>>> SearchManyAsync(IEnumerable<string> queries, MemoryFilter? filter, int? topK = null, CancellationToken cancellationToken = default) =>
        SearchManyAsync(queries, new MemorySearchOptions { Filter = filter, TopK = topK ?? 5 }, cancellationToken);

    public Task<Memory?> GetAsync(string id, CancellationToken cancellationToken = default) => CaptureAsync<Memory?>("magicore.get", () => inner.GetAsync(id, cancellationToken), cancellationToken: cancellationToken);
    public Task<IReadOnlyList<Memory>> GetAllAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default) => CaptureAsync<IReadOnlyList<Memory>>("magicore.get_all", () => inner.GetAllAsync(filter, cancellationToken), cancellationToken: cancellationToken);
    public Task<IReadOnlyList<Memory>> GetAllAtAsync(DateTimeOffset pointInTime, MemoryFilter? filter = null, CancellationToken cancellationToken = default) => CaptureAsync<IReadOnlyList<Memory>>("magicore.get_all_at", () => inner.GetAllAtAsync(pointInTime, filter, cancellationToken), new Dictionary<string, object?> { ["point_in_time"] = pointInTime }, cancellationToken);
    public Task<MemoryPage> GetPageAsync(MemoryPageOptions options, MemoryFilter? filter = null, CancellationToken cancellationToken = default) => CaptureAsync<MemoryPage>("magicore.get_page", () => inner.GetPageAsync(options, filter, cancellationToken), cancellationToken: cancellationToken);
    public Task<Memory> UpdateAsync(string id, MemoryUpdate update, CancellationToken cancellationToken = default) => CaptureAsync<Memory>("magicore.update", () => inner.UpdateAsync(id, update, cancellationToken), cancellationToken: cancellationToken);
    public Task<Memory> UpdateAsync(string id, string text, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default) => UpdateAsync(id, new MemoryUpdate { Text = text, Metadata = metadata }, cancellationToken);
    public Task DeleteAsync(string id, CancellationToken cancellationToken = default) => CaptureAsync("magicore.delete", () => inner.DeleteAsync(id, cancellationToken), cancellationToken: cancellationToken);
    public Task<int> DeleteAllAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default) => CaptureAsync<int>("magicore.delete_all", () => inner.DeleteAllAsync(filter, cancellationToken), cancellationToken: cancellationToken);
    public Task<int> ForgetStaleAsync(TimeSpan retentionWindow, MemoryFilter? filter = null, CancellationToken cancellationToken = default) => CaptureAsync<int>("magicore.forget_stale", () => inner.ForgetStaleAsync(retentionWindow, filter, cancellationToken), new Dictionary<string, object?> { ["retention_window_hours"] = retentionWindow.TotalHours }, cancellationToken);
    public Task<IReadOnlyList<Memory>> ConsolidateAsync(MemoryFilter? filter = null, int maxItems = 10, CancellationToken cancellationToken = default) => CaptureAsync<IReadOnlyList<Memory>>("magicore.consolidate", () => inner.ConsolidateAsync(filter, maxItems, cancellationToken), new Dictionary<string, object?> { ["max_items"] = maxItems }, cancellationToken);
    public Task<IReadOnlyList<MemoryHistoryEntry>> GetHistoryAsync(string id, CancellationToken cancellationToken = default) => CaptureAsync<IReadOnlyList<MemoryHistoryEntry>>("magicore.history", () => inner.GetHistoryAsync(id, cancellationToken), cancellationToken: cancellationToken);
    public Task<RollbackResult> RollbackAsync(DateTimeOffset pointInTime, MemoryFilter? filter = null, CancellationToken cancellationToken = default) => CaptureAsync<RollbackResult>("magicore.rollback", () => inner.RollbackAsync(pointInTime, filter, cancellationToken), cancellationToken: cancellationToken);
    public Task<RollbackResult> RollbackToHistoryAsync(string historyEntryId, CancellationToken cancellationToken = default) => CaptureAsync<RollbackResult>("magicore.rollback_to_history", () => inner.RollbackToHistoryAsync(historyEntryId, cancellationToken), cancellationToken: cancellationToken);
    public Task<TrajectoryRecord> AppendTrajectoryAsync(TrajectoryRecord record, CancellationToken cancellationToken = default) => CaptureAsync<TrajectoryRecord>("magicore.append_trajectory", () => inner.AppendTrajectoryAsync(record, cancellationToken), cancellationToken: cancellationToken);
    public Task<IReadOnlyList<Memory>> ExtractOnDemandAsync(string queryOrTask, MemoryFilter? filter = null, CancellationToken cancellationToken = default) => CaptureAsync<IReadOnlyList<Memory>>("magicore.extract_on_demand", () => inner.ExtractOnDemandAsync(queryOrTask, filter, cancellationToken), cancellationToken: cancellationToken);
    public Task ResetAsync(CancellationToken cancellationToken = default) => CaptureAsync("magicore.reset", () => inner.ResetAsync(cancellationToken), cancellationToken: cancellationToken);
    public Task<IReadOnlyList<MemoryRelation>> GetRelationsAsync(string? query = null, CancellationToken cancellationToken = default) => CaptureAsync<IReadOnlyList<MemoryRelation>>("magicore.graph", () => inner.GetRelationsAsync(query, cancellationToken), cancellationToken: cancellationToken);

    private async Task<T> CaptureAsync<T>(string name, Func<Task<T>> operation, IReadOnlyDictionary<string, object?>? properties = null, CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            var result = await operation();
            await telemetry.CaptureAsync(new MemoryTelemetryEvent(name, started, Merge(properties, true)), cancellationToken);
            return result;
        }
        catch
        {
            await telemetry.CaptureAsync(new MemoryTelemetryEvent(name, started, Merge(properties, false)), cancellationToken);
            throw;
        }
    }

    private async Task CaptureAsync(string name, Func<Task> operation, IReadOnlyDictionary<string, object?>? properties = null, CancellationToken cancellationToken = default)
    {
        await CaptureAsync(name, async () => { await operation(); return true; }, properties, cancellationToken);
    }

    private static IReadOnlyDictionary<string, object?> Merge(IReadOnlyDictionary<string, object?>? properties, bool success)
    {
        var result = (properties ?? new Dictionary<string, object?>()).ToDictionary(pair => pair.Key, pair => pair.Value);
        result["success"] = success;
        result["sync_type"] = "async";
        return result;
    }
}