using System.Collections.Concurrent;

namespace MagiCore;

public sealed class InMemoryEntityStore : IEntityStore
{
    private readonly ConcurrentDictionary<string, EntityState> entities = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HashSet<string>> memoryToEntities = new(StringComparer.Ordinal);
    private readonly object sync = new();

    public Task UpsertLinksAsync(IReadOnlyList<ExtractedEntity> extractedEntities, string memoryId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            var entityKeysForMemory = memoryToEntities.GetOrAdd(memoryId, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            foreach (var extracted in extractedEntities)
            {
                var key = Normalize(extracted.Text);
                entityKeysForMemory.Add(key);
                entities.AddOrUpdate(
                    key,
                    _ => new EntityState(Guid.NewGuid().ToString("N"), extracted.Text.Trim(), extracted.Type, new HashSet<string>(StringComparer.Ordinal) { memoryId }),
                    (_, current) => current with { LinkedMemoryIds = current.LinkedMemoryIds.Append(memoryId).ToHashSet(StringComparer.Ordinal) });
            }
        }
        return Task.CompletedTask;
    }

    public Task RemoveMemoryAsync(string memoryId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (memoryToEntities.TryRemove(memoryId, out var linkedKeys))
            {
                foreach (var key in linkedKeys)
                {
                    if (!entities.TryGetValue(key, out var entity)) continue;
                    var links = entity.LinkedMemoryIds.Where(id => id != memoryId).ToHashSet(StringComparer.Ordinal);
                    if (links.Count == 0) entities.TryRemove(key, out _);
                    else entities[key] = entity with { LinkedMemoryIds = links };
                }
            }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, double>> GetMemoryBoostsAsync(IReadOnlyList<ExtractedEntity> extractedEntities, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var boosts = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var extracted in extractedEntities)
        {
            if (!entities.TryGetValue(Normalize(extracted.Text), out var entity)) continue;
            var contribution = 0.5 / Math.Max(entity.LinkedMemoryIds.Count, 1);
            foreach (var memoryId in entity.LinkedMemoryIds) boosts[memoryId] = Math.Min(0.5, boosts.GetValueOrDefault(memoryId) + contribution);
        }
        return Task.FromResult<IReadOnlyDictionary<string, double>>(boosts);
    }

    public Task<IReadOnlyList<MemoryEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<MemoryEntity> result = entities.Values
            .Select(entity => new MemoryEntity(entity.Id, entity.Text, entity.Type, entity.LinkedMemoryIds))
            .OrderBy(entity => entity.Text, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult(result);
    }

    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            entities.Clear();
            memoryToEntities.Clear();
        }
        return Task.CompletedTask;
    }

    private static string Normalize(string text) => string.Join(" ", text.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

    private sealed record EntityState(string Id, string Text, EntityType Type, IReadOnlyCollection<string> LinkedMemoryIds);
}