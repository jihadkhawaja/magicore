namespace MagiCore;

public sealed partial class MemoryService : IMemoryService
{
    public Task<IReadOnlyList<MemoryRelation>> GetRelationsAsync(string? query = null, CancellationToken cancellationToken = default) =>
        graphStore?.GetRelationsAsync(query, cancellationToken) ?? Task.FromResult<IReadOnlyList<MemoryRelation>>([]);

    private async Task<MemoryEnrichment> PrepareEnrichmentAsync(string text, CancellationToken cancellationToken)
    {
        var entities = await entityExtractor.ExtractAsync(text, cancellationToken);
        var relations = graphStore is not null && graphExtractor is not null
            ? await graphExtractor.ExtractAsync(text, cancellationToken)
            : [];
        return new MemoryEnrichment(entities, relations);
    }

    private async Task ApplyEnrichmentAsync(MemoryEnrichment enrichment, string memoryId, CancellationToken cancellationToken)
    {
        try
        {
            await entityStore.RemoveMemoryAsync(memoryId, cancellationToken);
            await entityStore.UpsertLinksAsync(enrichment.Entities, memoryId, cancellationToken);
            if (graphStore is not null)
            {
                await graphStore.RemoveMemoryAsync(memoryId, cancellationToken);
                if (graphExtractor is not null) await graphStore.UpsertAsync(enrichment.Relations, memoryId, cancellationToken);
            }
        }
        catch
        {
            await RemoveEnrichmentAfterFailureAsync(memoryId, cancellationToken);
            throw;
        }
    }

    private async Task RemoveEnrichmentAsync(string memoryId, CancellationToken cancellationToken)
    {
        await entityStore.RemoveMemoryAsync(memoryId, cancellationToken);
        if (graphStore is not null) await graphStore.RemoveMemoryAsync(memoryId, cancellationToken);
    }

    private async Task RemoveEnrichmentAfterFailureAsync(string memoryId, CancellationToken cancellationToken)
    {
        try { await entityStore.RemoveMemoryAsync(memoryId, cancellationToken); }
        catch { }
        if (graphStore is not null)
        {
            try { await graphStore.RemoveMemoryAsync(memoryId, cancellationToken); }
            catch { }
        }
    }

    private sealed record MemoryEnrichment(IReadOnlyList<ExtractedEntity> Entities, IReadOnlyList<ExtractedRelation> Relations);
}