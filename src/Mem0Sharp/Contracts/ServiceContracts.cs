using Microsoft.Extensions.AI;

namespace Mem0Sharp;

public interface IMemoryService
{
    Task<AddResult> AddAsync(string text, MemoryAddOptions? options = null, CancellationToken cancellationToken = default);
#if NETSTANDARD2_0
    Task<AddResult> AddAsync(string text, string userId, string? agentId = null, string? runId = null, MemoryScope scope = MemoryScope.User, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default);
#else
    Task<AddResult> AddAsync(string text, string userId, string? agentId = null, string? runId = null, MemoryScope scope = MemoryScope.User, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default) =>
        AddAsync(text, new MemoryAddOptions { UserId = userId, AgentId = agentId, RunId = runId, Scope = scope, Metadata = metadata }, cancellationToken);
#endif
    Task<AddResult> AddAsync(IEnumerable<Message> messages, MemoryAddOptions? options = null, CancellationToken cancellationToken = default);
#if NETSTANDARD2_0
    Task<AddResult> AddAsync(IEnumerable<ChatMessage> chatMessages, MemoryAddOptions? options = null, CancellationToken cancellationToken = default);
    Task<AddResult> AddAsync(IEnumerable<Message> messages, string userId, string? agentId = null, string? runId = null, MemoryScope scope = MemoryScope.User, CancellationToken cancellationToken = default);
    Task<AddResult> AddAsync(IEnumerable<ChatMessage> chatMessages, string userId, string? agentId = null, string? runId = null, MemoryScope scope = MemoryScope.User, CancellationToken cancellationToken = default);
#else
    Task<AddResult> AddAsync(IEnumerable<ChatMessage> chatMessages, MemoryAddOptions? options = null, CancellationToken cancellationToken = default) =>
        AddAsync(chatMessages.Select(Message.FromChatMessage), options, cancellationToken);
    Task<AddResult> AddAsync(IEnumerable<Message> messages, string userId, string? agentId = null, string? runId = null, MemoryScope scope = MemoryScope.User, CancellationToken cancellationToken = default) =>
        AddAsync(messages, new MemoryAddOptions { UserId = userId, AgentId = agentId, RunId = runId, Scope = scope }, cancellationToken);
    Task<AddResult> AddAsync(IEnumerable<ChatMessage> chatMessages, string userId, string? agentId = null, string? runId = null, MemoryScope scope = MemoryScope.User, CancellationToken cancellationToken = default) =>
        AddAsync(chatMessages.Select(Message.FromChatMessage), new MemoryAddOptions { UserId = userId, AgentId = agentId, RunId = runId, Scope = scope }, cancellationToken);
#endif
    Task<AddResult> AddManyAsync(IEnumerable<string> texts, MemoryAddOptions? options = null, CancellationToken cancellationToken = default);
    Task<AddResult> AddAsync(DataContent image, MemoryAddOptions? options = null, CancellationToken cancellationToken = default);
#if NETSTANDARD2_0
    Task<AddResult> AddAsync(ReadOnlyMemory<byte> imageData, string mediaType, MemoryAddOptions? options = null, CancellationToken cancellationToken = default);
    Task<AddResult> AddAsync(Uri imageUri, string mediaType = "image/jpeg", MemoryAddOptions? options = null, CancellationToken cancellationToken = default);
#else
    Task<AddResult> AddAsync(ReadOnlyMemory<byte> imageData, string mediaType, MemoryAddOptions? options = null, CancellationToken cancellationToken = default) =>
        AddAsync(new DataContent(imageData, mediaType), options, cancellationToken);
    Task<AddResult> AddAsync(Uri imageUri, string mediaType = "image/jpeg", MemoryAddOptions? options = null, CancellationToken cancellationToken = default) =>
        AddAsync(new DataContent(imageUri, mediaType), options, cancellationToken);
#endif

    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, MemorySearchOptions? options = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SearchResult>> SearchAtAsync(string query, DateTimeOffset pointInTime, MemorySearchOptions? options = null, CancellationToken cancellationToken = default);
#if NETSTANDARD2_0
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, MemoryFilter? filter, int? topK = null, CancellationToken cancellationToken = default);
#else
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, MemoryFilter? filter, int? topK = null, CancellationToken cancellationToken = default) =>
        SearchAsync(query, new MemorySearchOptions { Filter = filter, TopK = topK ?? 5 }, cancellationToken);
#endif

    Task<IReadOnlyList<SearchResult>> SearchAsync(DataContent image, MemorySearchOptions? options = null, CancellationToken cancellationToken = default);
#if NETSTANDARD2_0
    Task<IReadOnlyList<SearchResult>> SearchAsync(ReadOnlyMemory<byte> imageData, string mediaType, MemorySearchOptions? options = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SearchResult>> SearchAsync(Uri imageUri, string mediaType = "image/jpeg", MemorySearchOptions? options = null, CancellationToken cancellationToken = default);
#else
    Task<IReadOnlyList<SearchResult>> SearchAsync(ReadOnlyMemory<byte> imageData, string mediaType, MemorySearchOptions? options = null, CancellationToken cancellationToken = default) =>
        SearchAsync(new DataContent(imageData, mediaType), options, cancellationToken);
    Task<IReadOnlyList<SearchResult>> SearchAsync(Uri imageUri, string mediaType = "image/jpeg", MemorySearchOptions? options = null, CancellationToken cancellationToken = default) =>
        SearchAsync(new DataContent(imageUri, mediaType), options, cancellationToken);
#endif

    Task<IReadOnlyList<IReadOnlyList<SearchResult>>> SearchManyAsync(IEnumerable<string> queries, MemorySearchOptions? options = null, CancellationToken cancellationToken = default);
#if NETSTANDARD2_0
    Task<IReadOnlyList<IReadOnlyList<SearchResult>>> SearchManyAsync(IEnumerable<string> queries, MemoryFilter? filter, int? topK = null, CancellationToken cancellationToken = default);
#else
    Task<IReadOnlyList<IReadOnlyList<SearchResult>>> SearchManyAsync(IEnumerable<string> queries, MemoryFilter? filter, int? topK = null, CancellationToken cancellationToken = default) =>
        SearchManyAsync(queries, new MemorySearchOptions { Filter = filter, TopK = topK ?? 5 }, cancellationToken);
#endif

    Task<Memory?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Memory>> GetAllAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Memory>> GetAllAtAsync(DateTimeOffset pointInTime, MemoryFilter? filter = null, CancellationToken cancellationToken = default);
    Task<MemoryPage> GetPageAsync(MemoryPageOptions options, MemoryFilter? filter = null, CancellationToken cancellationToken = default);
    Task<Memory> UpdateAsync(string id, MemoryUpdate update, CancellationToken cancellationToken = default);
#if NETSTANDARD2_0
    Task<Memory> UpdateAsync(string id, string text, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default);
#else
    Task<Memory> UpdateAsync(string id, string text, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default) =>
        UpdateAsync(id, new MemoryUpdate { Text = text, Metadata = metadata }, cancellationToken);
#endif

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<int> DeleteAllAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default);
    Task<int> ForgetStaleAsync(TimeSpan retentionWindow, MemoryFilter? filter = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Memory>> ConsolidateAsync(MemoryFilter? filter = null, int maxItems = 10, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryHistoryEntry>> GetHistoryAsync(string id, CancellationToken cancellationToken = default);
    Task<RollbackResult> RollbackAsync(DateTimeOffset pointInTime, MemoryFilter? filter = null, CancellationToken cancellationToken = default);
    Task<RollbackResult> RollbackToHistoryAsync(string historyEntryId, CancellationToken cancellationToken = default);

    Task<TrajectoryRecord> AppendTrajectoryAsync(TrajectoryRecord record, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Memory>> ExtractOnDemandAsync(string queryOrTask, MemoryFilter? filter = null, CancellationToken cancellationToken = default);

    Task ResetAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryRelation>> GetRelationsAsync(string? query = null, CancellationToken cancellationToken = default);
}