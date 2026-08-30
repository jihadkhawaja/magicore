using System.Collections.Concurrent;
using System.Linq.Expressions;
using Microsoft.Extensions.VectorData;

namespace Mem0Sharp.VectorData;

public sealed class VectorDataMemoryStore : IMemoryStore, ITemporalMemoryStore
{
    private readonly VectorStoreCollection<string, VectorDataMemoryRecord> _collection;
    private readonly VectorStoreCollection<string, VectorDataHistoryRecord>? _historyCollection;
    private readonly VectorDataMemoryStoreOptions _options;
    private readonly ConcurrentDictionary<string, byte> _trackedKeys = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<MemoryHistoryEntry>> _fallbackHistory = new(StringComparer.Ordinal);

    public VectorDataMemoryStore(
        VectorStoreCollection<string, VectorDataMemoryRecord> collection,
        VectorStoreCollection<string, VectorDataHistoryRecord>? historyCollection = null,
        VectorDataMemoryStoreOptions? options = null)
    {
        _collection = collection ?? throw new ArgumentNullException(nameof(collection));
        _historyCollection = historyCollection;
        _options = options ?? new VectorDataMemoryStoreOptions();
        ValidateOptions(_options);
    }

    public VectorDataMemoryStore(
        VectorStore vectorStore,
        VectorDataMemoryStoreOptions? options = null)
    {
        Guard.NotNull(vectorStore);
        _options = options ?? new VectorDataMemoryStoreOptions();
        ValidateOptions(_options);
        _collection = vectorStore.GetCollection<string, VectorDataMemoryRecord>(
            _options.CollectionName,
            CreateMemoryCollectionDefinition(_options.VectorDimensions));
        if (!string.IsNullOrEmpty(_options.HistoryCollectionName))
        {
            _historyCollection = vectorStore.GetCollection<string, VectorDataHistoryRecord>(
                _options.HistoryCollectionName,
                CreateHistoryCollectionDefinition());
        }
    }

    public static VectorDataMemoryStore CreateInMemory(VectorDataMemoryStoreOptions? options = null)
    {
        var opts = options ?? new VectorDataMemoryStoreOptions();
        ValidateOptions(opts);
        var collection = new InMemoryVectorRecordCollection<VectorDataMemoryRecord>(
            opts.CollectionName,
            r => r.Id,
            r => r.Vector);

        InMemoryVectorRecordCollection<VectorDataHistoryRecord>? historyCollection = null;
        if (!string.IsNullOrEmpty(opts.HistoryCollectionName))
        {
            historyCollection = new InMemoryVectorRecordCollection<VectorDataHistoryRecord>(
                opts.HistoryCollectionName,
                r => r.Id);
        }

        return new VectorDataMemoryStore(collection, historyCollection, opts);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_options.AutoCreateCollection)
        {
            await _collection.EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);
            if (_historyCollection is not null)
            {
                await _historyCollection.EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task SaveAsync(Memory memory, IReadOnlyList<float>? embedding = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(memory);
        cancellationToken.ThrowIfCancellationRequested();

        var record = VectorDataMemoryRecord.FromMemory(memory, embedding);
        await _collection.UpsertAsync(record, cancellationToken: cancellationToken).ConfigureAwait(false);
        _trackedKeys[memory.Id] = 0;
    }

    public async Task SaveBatchAsync(IReadOnlyList<MemoryWriteRecord> records, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(records);
        cancellationToken.ThrowIfCancellationRequested();

        if (records.Count == 0)
            return;

        var vectorRecords = records.Select(r => VectorDataMemoryRecord.FromMemory(r.Memory, r.Embedding)).ToArray();
        await _collection.UpsertAsync(vectorRecords, cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var record in records)
        {
            _trackedKeys[record.Memory.Id] = 0;
            await SaveHistoryAsync(record.History, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<Memory?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(id);
        cancellationToken.ThrowIfCancellationRequested();

        var record = await _collection.GetAsync(id, cancellationToken: cancellationToken).ConfigureAwait(false);
        return record?.ToMemory();
    }

    public async IAsyncEnumerable<Memory> GetAllAsync(
        MemoryFilter? filter = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var keys = _trackedKeys.Keys.ToArray();
        if (keys.Length == 0)
            yield break;

        var records = _collection.GetAsync(keys, cancellationToken: cancellationToken);
        await foreach (var record in records.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (record is null)
                continue;

            var memory = record.ToMemory();
            if (MemoryFilterEvaluator.Matches(memory, filter))
            {
                yield return memory;
            }
        }
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        IReadOnlyList<float> embedding,
        MemoryFilter? filter = null,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(embedding);
        cancellationToken.ThrowIfCancellationRequested();

        if (topK <= 0)
            return [];

        var vector = new ReadOnlyMemory<float>(embedding.ToArray());
        var pageSize = Math.Max(topK * 4, 10);
        var now = DateTimeOffset.UtcNow;
        var backendFilter = CreateSearchFilter(filter, now);
        var matches = new List<SearchResult>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var skip = 0;

        while (matches.Count < topK)
        {
            var searchOptions = new VectorSearchOptions<VectorDataMemoryRecord>
            {
                Filter = backendFilter,
                IncludeVectors = false,
                Skip = skip
            };
            var searchResults = _collection.SearchAsync(vector, pageSize, searchOptions, cancellationToken);
            var fetched = 0;
            var newRecords = 0;

            await foreach (var result in searchResults.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                fetched++;
                if (result.Record is null || !seenIds.Add(result.Record.Id))
                    continue;

                newRecords++;
                var memory = result.Record.ToMemory();
                if (MemoryFilterEvaluator.Matches(memory, filter, now))
                {
                    matches.Add(new SearchResult(memory, result.Score ?? 0.0));
                }
            }

            if (fetched < pageSize || newRecords == 0)
                break;

            skip += fetched;
        }

        return matches
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.Memory.UpdatedAt)
            .Take(topK)
            .ToArray();
    }

    internal static VectorStoreCollectionDefinition CreateMemoryCollectionDefinition(int vectorDimensions)
    {
        if (vectorDimensions <= 0)
            throw new ArgumentOutOfRangeException(nameof(vectorDimensions), "Vector dimensions must be greater than zero.");

        return new VectorStoreCollectionDefinition
        {
            Properties =
            [
                new VectorStoreKeyProperty(nameof(VectorDataMemoryRecord.Id), typeof(string)),
                new VectorStoreDataProperty(nameof(VectorDataMemoryRecord.Text), typeof(string)) { IsIndexed = true, IsFullTextIndexed = true },
                new VectorStoreDataProperty(nameof(VectorDataMemoryRecord.UserId), typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty(nameof(VectorDataMemoryRecord.AgentId), typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty(nameof(VectorDataMemoryRecord.RunId), typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty(nameof(VectorDataMemoryRecord.Scope), typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty(nameof(VectorDataMemoryRecord.MetadataJson), typeof(string)),
                new VectorStoreDataProperty(nameof(VectorDataMemoryRecord.CreatedAt), typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty(nameof(VectorDataMemoryRecord.UpdatedAt), typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty(nameof(VectorDataMemoryRecord.ExpiresAtUtcTicks), typeof(long)) { IsIndexed = true },
                new VectorStoreDataProperty(nameof(VectorDataMemoryRecord.Hash), typeof(string)),
                new VectorStoreDataProperty(nameof(VectorDataMemoryRecord.Behavior), typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty(nameof(VectorDataMemoryRecord.MemoryType), typeof(string)) { IsIndexed = true },
                new VectorStoreVectorProperty(nameof(VectorDataMemoryRecord.Vector), typeof(ReadOnlyMemory<float>), vectorDimensions)
                {
                    DistanceFunction = DistanceFunction.CosineDistance
                }
            ]
        };
    }

    internal static VectorStoreCollectionDefinition CreateHistoryCollectionDefinition() => new()
    {
        Properties =
        [
            new VectorStoreKeyProperty(nameof(VectorDataHistoryRecord.Id), typeof(string)),
            new VectorStoreDataProperty(nameof(VectorDataHistoryRecord.MemoryId), typeof(string)) { IsIndexed = true },
            new VectorStoreDataProperty(nameof(VectorDataHistoryRecord.Event), typeof(string)) { IsIndexed = true },
            new VectorStoreDataProperty(nameof(VectorDataHistoryRecord.OldMemory), typeof(string)),
            new VectorStoreDataProperty(nameof(VectorDataHistoryRecord.NewMemory), typeof(string)),
            new VectorStoreDataProperty(nameof(VectorDataHistoryRecord.SnapshotJson), typeof(string)),
            new VectorStoreDataProperty(nameof(VectorDataHistoryRecord.EmbeddingJson), typeof(string)),
            new VectorStoreDataProperty(nameof(VectorDataHistoryRecord.ActorId), typeof(string)) { IsIndexed = true },
            new VectorStoreDataProperty(nameof(VectorDataHistoryRecord.Role), typeof(string)) { IsIndexed = true },
            new VectorStoreDataProperty(nameof(VectorDataHistoryRecord.CreatedAt), typeof(string)) { IsIndexed = true },
            new VectorStoreDataProperty(nameof(VectorDataHistoryRecord.UpdatedAt), typeof(string)) { IsIndexed = true },
            new VectorStoreDataProperty(nameof(VectorDataHistoryRecord.IsDeleted), typeof(bool)) { IsIndexed = true }
        ]
    };

    private static Expression<Func<VectorDataMemoryRecord, bool>> CreateSearchFilter(MemoryFilter? filter, DateTimeOffset now)
    {
        var record = Expression.Parameter(typeof(VectorDataMemoryRecord), "record");
        Expression body = Expression.Constant(true);

        if (filter?.IncludeExpired != true)
        {
            body = Expression.AndAlso(
                body,
                Expression.GreaterThan(
                    Expression.Property(record, nameof(VectorDataMemoryRecord.ExpiresAtUtcTicks)),
                    Expression.Constant(now.UtcDateTime.Ticks)));
        }

        body = AddEquality(body, record, nameof(VectorDataMemoryRecord.UserId), filter?.UserId);
        body = AddEquality(body, record, nameof(VectorDataMemoryRecord.AgentId), filter?.AgentId);
        body = AddEquality(body, record, nameof(VectorDataMemoryRecord.RunId), filter?.RunId);
        body = AddEquality(body, record, nameof(VectorDataMemoryRecord.Scope), filter?.Scope?.ToString());
        body = AddEquality(body, record, nameof(VectorDataMemoryRecord.Behavior), filter?.Behavior?.ToString());
        body = AddEquality(body, record, nameof(VectorDataMemoryRecord.MemoryType), filter?.MemoryType);

        return Expression.Lambda<Func<VectorDataMemoryRecord, bool>>(body, record);
    }

    private static Expression AddEquality(Expression body, ParameterExpression record, string propertyName, string? value)
    {
        if (value is null)
            return body;

        return Expression.AndAlso(
            body,
            Expression.Equal(Expression.Property(record, propertyName), Expression.Constant(value)));
    }

    private static void ValidateOptions(VectorDataMemoryStoreOptions options)
    {
        Guard.NotNullOrWhiteSpace(options.CollectionName);
        if (options.VectorDimensions <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.VectorDimensions), "Vector dimensions must be greater than zero.");
    }

    public async Task<IReadOnlyList<IReadOnlyList<SearchResult>>> SearchBatchAsync(
        IReadOnlyList<IReadOnlyList<float>> embeddings,
        MemoryFilter? filter = null,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(embeddings);
        cancellationToken.ThrowIfCancellationRequested();

        var results = new List<IReadOnlyList<SearchResult>>(embeddings.Count);
        foreach (var embedding in embeddings)
        {
            results.Add(await SearchAsync(embedding, filter, topK, cancellationToken).ConfigureAwait(false));
        }
        return results;
    }

    public async Task DeleteAsync(string id, MemoryHistoryEntry? history = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(id);
        cancellationToken.ThrowIfCancellationRequested();

        await _collection.DeleteAsync(id, cancellationToken: cancellationToken).ConfigureAwait(false);
        _trackedKeys.TryRemove(id, out _);

        if (history is not null)
        {
            await SaveHistoryAsync(history, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<int> DeleteAllAsync(
        MemoryFilter? filter = null,
        IReadOnlyList<MemoryDeleteRecord>? records = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (records is not null && records.Count > 0)
        {
            var keysToDelete = records.Select(r => r.Memory.Id).ToArray();
            await _collection.DeleteAsync(keysToDelete, cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var record in records)
            {
                _trackedKeys.TryRemove(record.Memory.Id, out _);
                await SaveHistoryAsync(record.History, cancellationToken).ConfigureAwait(false);
            }

            return records.Count;
        }

        var matchingMemories = new List<Memory>();
        await foreach (var memory in GetAllAsync(filter, cancellationToken).ConfigureAwait(false))
        {
            matchingMemories.Add(memory);
        }

        if (matchingMemories.Count == 0)
            return 0;

        var keys = matchingMemories.Select(m => m.Id).ToArray();
        await _collection.DeleteAsync(keys, cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var key in keys)
        {
            _trackedKeys.TryRemove(key, out _);
        }

        return matchingMemories.Count;
    }

    public async Task SaveHistoryAsync(MemoryHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        if (_historyCollection is not null)
        {
            var record = VectorDataHistoryRecord.FromEntry(entry);
            await _historyCollection.UpsertAsync(record, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        _fallbackHistory.GetOrAdd(entry.MemoryId, static _ => new ConcurrentQueue<MemoryHistoryEntry>()).Enqueue(entry);
    }

    public Task<IReadOnlyList<MemoryHistoryEntry>> GetHistoryAsync(string memoryId, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(memoryId);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<MemoryHistoryEntry> entries = _fallbackHistory.TryGetValue(memoryId, out var queue)
            ? queue.ToArray()
            : [];

        return Task.FromResult(entries);
    }

    public Task<IReadOnlyList<MemoryHistoryEntry>> GetAllHistoryAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var all = _fallbackHistory.Values.SelectMany(q => q).OrderBy(h => h.UpdatedAt).ToArray();
        return Task.FromResult<IReadOnlyList<MemoryHistoryEntry>>(all);
    }

    public Task<IReadOnlyList<Memory>> GetAllAtAsync(DateTimeOffset pointInTime, MemoryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var all = _fallbackHistory.Values.SelectMany(queue => queue).ToArray();
        return Task.FromResult(TemporalMemoryReconstructor.Reconstruct(all, pointInTime, filter));
    }

    public async Task<RollbackResult> RollbackAsync(DateTimeOffset pointInTime, MemoryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var restored = 0;
        var deleted = 0;
        var affected = new List<string>();

        foreach (var pair in _fallbackHistory)
        {
            var memoryId = pair.Key;
            var entriesBefore = pair.Value
                .Where(e => e.UpdatedAt <= pointInTime)
                .OrderBy(e => e.UpdatedAt)
                .ToArray();
            var targetEntry = entriesBefore.Length == 0 ? null : entriesBefore[entriesBefore.Length - 1];
            var target = targetEntry is null || targetEntry.Event == MemoryHistoryEvent.Delete || targetEntry.IsDeleted
                ? null
                : targetEntry.Snapshot;
            var current = await GetAsync(memoryId, cancellationToken).ConfigureAwait(false);

            var matches = filter is null || (target is not null
                ? MemoryFilterEvaluator.Matches(target, filter, pointInTime)
                : entriesBefore.Length == 0 && current is not null && MemoryFilterEvaluator.Matches(current, filter));
            if (!matches) continue;

            if (target is null)
            {
                if (current is not null)
                {
                    await DeleteAsync(memoryId, cancellationToken: cancellationToken).ConfigureAwait(false);
                    deleted++;
                    affected.Add(memoryId);
                }
            }
            else if (current != target)
            {
                await SaveAsync(target, targetEntry?.Embedding, cancellationToken).ConfigureAwait(false);
                restored++;
                affected.Add(memoryId);
            }
        }

        return new RollbackResult(restored, deleted, affected.ToArray());
    }

    public Task<RollbackResult> RollbackToHistoryAsync(string historyEntryId, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(historyEntryId);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var pair in _fallbackHistory)
        {
            var target = pair.Value.FirstOrDefault(e => e.Id == historyEntryId);
            if (target is not null)
            {
                return RollbackAsync(target.UpdatedAt, cancellationToken: cancellationToken);
            }
        }

        return Task.FromResult(new RollbackResult(0, 0, []));
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await _collection.EnsureCollectionDeletedAsync(cancellationToken).ConfigureAwait(false);
            await _collection.EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            var keys = _trackedKeys.Keys.ToArray();
            if (keys.Length > 0)
            {
                await _collection.DeleteAsync(keys, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }

        _trackedKeys.Clear();
        _fallbackHistory.Clear();
    }
}
