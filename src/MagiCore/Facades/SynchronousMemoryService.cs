using Microsoft.Extensions.AI;

namespace MagiCore;

public sealed class SynchronousMemoryService
{
    private readonly IMemoryService service;

    public SynchronousMemoryService(IMemoryService service) => this.service = service;

    public AddResult Add(string text, MemoryAddOptions? options = null) => service.AddAsync(text, options).GetAwaiter().GetResult();
    public AddResult Add(IEnumerable<Message> messages, MemoryAddOptions? options = null) => service.AddAsync(messages, options).GetAwaiter().GetResult();
    public AddResult AddMany(IEnumerable<string> texts, MemoryAddOptions? options = null) => service.AddManyAsync(texts, options).GetAwaiter().GetResult();
    public AddResult Add(DataContent image, MemoryAddOptions? options = null) => service.AddAsync(image, options).GetAwaiter().GetResult();
    public AddResult Add(ReadOnlyMemory<byte> imageData, string mediaType, MemoryAddOptions? options = null) => service.AddAsync(imageData, mediaType, options).GetAwaiter().GetResult();
    public AddResult Add(Uri imageUri, string mediaType = "image/jpeg", MemoryAddOptions? options = null) => service.AddAsync(imageUri, mediaType, options).GetAwaiter().GetResult();
    public IReadOnlyList<SearchResult> Search(string query, MemorySearchOptions? options = null) => service.SearchAsync(query, options).GetAwaiter().GetResult();
    public IReadOnlyList<SearchResult> SearchAt(string query, DateTimeOffset pointInTime, MemorySearchOptions? options = null) => service.SearchAtAsync(query, pointInTime, options).GetAwaiter().GetResult();
    public IReadOnlyList<SearchResult> Search(DataContent image, MemorySearchOptions? options = null) => service.SearchAsync(image, options).GetAwaiter().GetResult();
    public IReadOnlyList<SearchResult> Search(ReadOnlyMemory<byte> imageData, string mediaType, MemorySearchOptions? options = null) => service.SearchAsync(imageData, mediaType, options).GetAwaiter().GetResult();
    public IReadOnlyList<SearchResult> Search(Uri imageUri, string mediaType = "image/jpeg", MemorySearchOptions? options = null) => service.SearchAsync(imageUri, mediaType, options).GetAwaiter().GetResult();
    public IReadOnlyList<IReadOnlyList<SearchResult>> SearchMany(IEnumerable<string> queries, MemorySearchOptions? options = null) => service.SearchManyAsync(queries, options).GetAwaiter().GetResult();
    public Memory? Get(string id) => service.GetAsync(id).GetAwaiter().GetResult();
    public IReadOnlyList<Memory> GetAll(MemoryFilter? filter = null) => service.GetAllAsync(filter).GetAwaiter().GetResult();
    public IReadOnlyList<Memory> GetAllAt(DateTimeOffset pointInTime, MemoryFilter? filter = null) => service.GetAllAtAsync(pointInTime, filter).GetAwaiter().GetResult();
    public MemoryPage GetPage(MemoryPageOptions options, MemoryFilter? filter = null) => service.GetPageAsync(options, filter).GetAwaiter().GetResult();
    public Memory Update(string id, MemoryUpdate update) => service.UpdateAsync(id, update).GetAwaiter().GetResult();
    public void Delete(string id) => service.DeleteAsync(id).GetAwaiter().GetResult();
    public int DeleteAll(MemoryFilter? filter = null) => service.DeleteAllAsync(filter).GetAwaiter().GetResult();
    public int ForgetStale(TimeSpan retentionWindow, MemoryFilter? filter = null) => service.ForgetStaleAsync(retentionWindow, filter).GetAwaiter().GetResult();
    public IReadOnlyList<Memory> Consolidate(MemoryFilter? filter = null, int maxItems = 10) => service.ConsolidateAsync(filter, maxItems).GetAwaiter().GetResult();
    public IReadOnlyList<MemoryHistoryEntry> History(string id) => service.GetHistoryAsync(id).GetAwaiter().GetResult();
    public void Reset() => service.ResetAsync().GetAwaiter().GetResult();
    public IReadOnlyList<MemoryRelation> GetRelations(string? query = null) => service.GetRelationsAsync(query).GetAwaiter().GetResult();
}