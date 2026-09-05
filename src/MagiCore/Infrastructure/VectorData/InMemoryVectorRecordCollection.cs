using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Numerics.Tensors;
using Microsoft.Extensions.VectorData;

namespace MagiCore;

/// <summary>
/// A lightweight, in-memory implementation of <see cref="VectorStoreCollection{TKey, TRecord}"/>
/// that performs cosine similarity search over vector records.
/// </summary>
/// <typeparam name="TRecord">The record type.</typeparam>
public sealed class InMemoryVectorRecordCollection<TRecord> : VectorStoreCollection<string, TRecord>
    where TRecord : class
{
    private readonly ConcurrentDictionary<string, TRecord> _records = new(StringComparer.Ordinal);
    private readonly Func<TRecord, string> _keySelector;
    private readonly Func<TRecord, ReadOnlyMemory<float>?>? _vectorSelector;

    public InMemoryVectorRecordCollection(
        string name,
        Func<TRecord, string> keySelector,
        Func<TRecord, ReadOnlyMemory<float>?>? vectorSelector = null)
    {
        Guard.NotNullOrWhiteSpace(name);
        Guard.NotNull(keySelector);

        Name = name;
        _keySelector = keySelector;
        _vectorSelector = vectorSelector;
    }

    public override string Name { get; }

    public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public override Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public override Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
    {
        _records.Clear();
        return Task.CompletedTask;
    }

    public override Task<TRecord?> GetAsync(string key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
    {
        _records.TryGetValue(key, out var record);
        return Task.FromResult<TRecord?>(record);
    }

    public override async IAsyncEnumerable<TRecord> GetAsync(
        IEnumerable<string> keys,
        RecordRetrievalOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var key in keys)
        {
            if (_records.TryGetValue(key, out var record))
            {
                yield return record;
            }
        }
        await Task.CompletedTask;
    }

    public override async IAsyncEnumerable<TRecord> GetAsync(
        Expression<Func<TRecord, bool>> filter,
        int top = 5,
        FilteredRecordRetrievalOptions<TRecord>? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var compiled = filter.Compile();
        var matched = _records.Values.Where(compiled).Take(top);
        foreach (var record in matched)
        {
            yield return record;
        }
        await Task.CompletedTask;
    }

    public override Task UpsertAsync(TRecord record, CancellationToken cancellationToken = default)
    {
        var key = _keySelector(record);
        _records[key] = record;
        return Task.CompletedTask;
    }

    public override Task UpsertAsync(IEnumerable<TRecord> records, CancellationToken cancellationToken = default)
    {
        foreach (var record in records)
        {
            var key = _keySelector(record);
            _records[key] = record;
        }
        return Task.CompletedTask;
    }

    public override Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        _records.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public override Task DeleteAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        foreach (var key in keys)
        {
            _records.TryRemove(key, out _);
        }
        return Task.CompletedTask;
    }

    public override async IAsyncEnumerable<VectorSearchResult<TRecord>> SearchAsync<TVector>(
        TVector vector,
        int top = 5,
        VectorSearchOptions<TRecord>? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (vector is not ReadOnlyMemory<float> queryVector || _vectorSelector is null)
            yield break;

        var compiledFilter = options?.Filter?.Compile();
        var candidates = new List<(TRecord Record, double Score)>();

        foreach (var record in _records.Values)
        {
            if (compiledFilter is not null && !compiledFilter(record))
                continue;

            var recordVector = _vectorSelector(record);
            if (recordVector is null || recordVector.Value.Length != queryVector.Length)
                continue;

            var sim = TensorPrimitives.CosineSimilarity(queryVector.Span, recordVector.Value.Span);
            candidates.Add((record, (double)sim));
        }

        var results = candidates
            .OrderByDescending(c => c.Score)
            .Skip(options?.Skip ?? 0)
            .Take(top);

        foreach (var (record, score) in results)
        {
            yield return new VectorSearchResult<TRecord>(record, score);
        }

        await Task.CompletedTask;
    }

    public override object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;
}
