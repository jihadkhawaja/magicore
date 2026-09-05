using System.Collections.Concurrent;

namespace MagiCore;

public sealed class InMemoryGraphStore : IGraphMemoryStore
{
    private readonly ConcurrentDictionary<string, MemoryRelation> relations = new(StringComparer.Ordinal);

    public Task UpsertAsync(IReadOnlyList<ExtractedRelation> extractedRelations, string memoryId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var relation in extractedRelations.Where(IsValid).Distinct())
        {
            var key = $"{Normalize(relation.Source)}|{Normalize(relation.Relationship)}|{Normalize(relation.Target)}|{memoryId}";
            relations.TryAdd(key, new MemoryRelation(Guid.NewGuid().ToString("N"), relation.Source.Trim(), relation.Relationship.Trim(), relation.Target.Trim(), memoryId));
        }
        return Task.CompletedTask;
    }

    public Task RemoveMemoryAsync(string memoryId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var pair in relations.Where(pair => pair.Value.MemoryId == memoryId)) relations.TryRemove(pair.Key, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, double>> GetMemoryBoostsAsync(string query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var terms = Normalize(query).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var boosts = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var relation in relations.Values)
        {
            var relationTerms = Normalize($"{relation.Source} {relation.Relationship} {relation.Target}").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (!relationTerms.Any(terms.Contains)) continue;
            boosts[relation.MemoryId] = Math.Min(0.5, boosts.GetValueOrDefault(relation.MemoryId) + 0.25);
        }
        return Task.FromResult<IReadOnlyDictionary<string, double>>(boosts);
    }

    public Task<IReadOnlyList<MemoryRelation>> GetRelationsAsync(string? query = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = query is null ? null : Normalize(query);
        IReadOnlyList<MemoryRelation> result = relations.Values
            .Where(relation => normalized is null || Normalize($"{relation.Source} {relation.Relationship} {relation.Target}").Contains(normalized, StringComparison.Ordinal))
            .OrderBy(relation => relation.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(relation => relation.Relationship, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult(result);
    }

    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        relations.Clear();
        return Task.CompletedTask;
    }

    private static bool IsValid(ExtractedRelation relation) => !string.IsNullOrWhiteSpace(relation.Source) && !string.IsNullOrWhiteSpace(relation.Relationship) && !string.IsNullOrWhiteSpace(relation.Target);

    private static string Normalize(string text) => string.Join(" ", text.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
}