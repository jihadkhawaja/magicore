using System.Globalization;

namespace MagiCore;

public sealed partial class MemoryService : IMemoryService
{
    public Task<Memory?> GetAsync(string id, CancellationToken cancellationToken = default) => store.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<MemoryHistoryEntry>> GetHistoryAsync(string id, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(id);
        return store.GetHistoryAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<Memory>> GetAllAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        var result = new List<Memory>();
        await foreach (var memory in store.GetAllAsync(filter, cancellationToken)) result.Add(memory);
        return result;
    }

    public Task<IReadOnlyList<Memory>> GetAllAtAsync(DateTimeOffset pointInTime, MemoryFilter? filter = null, CancellationToken cancellationToken = default) =>
        store is ITemporalMemoryStore temporalStore
            ? temporalStore.GetAllAtAsync(pointInTime, filter, cancellationToken)
            : throw new NotSupportedException($"The configured memory store '{store.GetType().Name}' does not support point-in-time reads.");

    public async Task<MemoryPage> GetPageAsync(MemoryPageOptions pageOptions, MemoryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(pageOptions);
        if (pageOptions.Offset < 0) throw new ArgumentOutOfRangeException(nameof(pageOptions));
        if (pageOptions.Limit < 0) throw new ArgumentOutOfRangeException(nameof(pageOptions));
        var memories = await GetAllAsync(filter, cancellationToken);
        return new MemoryPage(memories.Skip(pageOptions.Offset).Take(pageOptions.Limit).ToArray(), memories.Count, pageOptions.Offset, pageOptions.Limit);
    }

    public async Task<int> ForgetStaleAsync(TimeSpan retentionWindow, MemoryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        if (retentionWindow < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retentionWindow));
        var cutoff = DateTimeOffset.UtcNow - retentionWindow;
        var stale = (await GetAllAsync(filter is null ? new MemoryFilter(IncludeExpired: true) : filter with { IncludeExpired = true }, cancellationToken))
            .Where(memory => memory.UpdatedAt < cutoff || memory.CreatedAt < cutoff || (memory.ExpiresAt.HasValue && memory.ExpiresAt.Value < DateTimeOffset.UtcNow))
            .ToArray();
        foreach (var memory in stale) await DeleteAsync(memory.Id, cancellationToken);
        return stale.Length;
    }

    public async Task<IReadOnlyList<Memory>> ConsolidateAsync(MemoryFilter? filter = null, int maxItems = 10, CancellationToken cancellationToken = default)
    {
        if (maxItems < 0) throw new ArgumentOutOfRangeException(nameof(maxItems));
        var memories = (await GetAllAsync(filter, cancellationToken))
            .OrderByDescending(memory => memory.UpdatedAt)
            .Take(maxItems)
            .ToArray();
        if (memories.Length == 0) return [];

        var summaryText = string.Join(" ", memories.Select(memory => memory.Text).Where(text => !string.IsNullOrWhiteSpace(text)));
        if (string.IsNullOrWhiteSpace(summaryText)) return [];

        // Decoupled anti-drift verification (SSGM)
        if (consolidationVerifier is not null)
        {
            var verification = await consolidationVerifier.VerifyAsync(memories, summaryText, cancellationToken);
            if (!verification.IsValid)
            {
                return [];
            }
        }

        var scope = filter?.Scope ?? MemoryScope.User;
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["summary_source_count"] = memories.Length.ToString(CultureInfo.InvariantCulture),
            ["summary_window_start"] = memories.Min(memory => memory.CreatedAt).ToString("O"),
            ["summary_window_end"] = memories.Max(memory => memory.UpdatedAt).ToString("O"),
            ["summary_generated_at"] = DateTimeOffset.UtcNow.ToString("O")
        };

        var result = await SaveInputsAsync(
            [new MemoryInput(summaryText, scope, metadata, Behavior: MemoryBehavior.Normal, MemoryType: "consolidated_memory")],
            new MemoryAddOptions
            {
                UserId = filter?.UserId ?? "default_user",
                AgentId = filter?.AgentId,
                RunId = filter?.RunId,
                Scope = scope,
                Metadata = metadata,
                Infer = false,
                MemoryType = "consolidated_memory"
            },
            cancellationToken);
        return result.Memories;
    }

    public async Task<RollbackResult> RollbackAsync(DateTimeOffset pointInTime, MemoryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        var result = await store.RollbackAsync(pointInTime, filter, cancellationToken);
        await indexLock.WaitAsync(cancellationToken);
        try
        {
            vectors.Clear();
        }
        finally
        {
            indexLock.Release();
        }
        return result;
    }

    public async Task<RollbackResult> RollbackToHistoryAsync(string historyEntryId, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(historyEntryId);
        var result = await store.RollbackToHistoryAsync(historyEntryId, cancellationToken);
        await indexLock.WaitAsync(cancellationToken);
        try
        {
            vectors.Clear();
        }
        finally
        {
            indexLock.Release();
        }
        return result;
    }

    public async Task<TrajectoryRecord> AppendTrajectoryAsync(TrajectoryRecord record, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(record);
        await trajectoryStore.AppendTrajectoryAsync(record, cancellationToken);
        return record;
    }

    public async Task<IReadOnlyList<Memory>> ExtractOnDemandAsync(string queryOrTask, MemoryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(queryOrTask);
        var matchingTrajectories = new List<TrajectoryRecord>();
        await foreach (var trajectory in trajectoryStore.GetTrajectoriesAsync(filter, cancellationToken))
        {
            matchingTrajectories.Add(trajectory);
        }

        if (matchingTrajectories.Count == 0) return [];

        var allMessages = matchingTrajectories.SelectMany(t => t.Messages).ToArray();
        var addOptions = new MemoryAddOptions
        {
            UserId = filter?.UserId ?? "default_user",
            AgentId = filter?.AgentId,
            RunId = filter?.RunId,
            Scope = filter?.Scope ?? MemoryScope.User,
            Prompt = queryOrTask,
            Infer = true
        };

        var extracted = await extractor.ExtractAsync(allMessages, addOptions, cancellationToken);
        var addResult = await SaveInputsAsync(extracted, addOptions, cancellationToken);
        return addResult.Memories;
    }

    public async Task<Memory> UpdateAsync(string id, MemoryUpdate update, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(update);
        if (update.Text is not null) Guard.NotNullOrWhiteSpace(update.Text);
        var existing = await store.GetAsync(id, cancellationToken) ?? throw new KeyNotFoundException($"Memory '{id}' was not found.");
        var updatedMetadata = CreateUpdatedMetadata(existing, update);
        var updated = existing with
        {
            Text = update.Text ?? existing.Text,
            Metadata = updatedMetadata,
            ExpiresAt = update.UpdateExpiration ? update.ExpiresAt : existing.ExpiresAt,
            Hash = update.Text is null ? existing.Hash : ComputeHash(update.Text),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var updatedVector = await embeddings.GenerateVectorCoreAsync(updated.Text, cancellationToken);
        var enrichment = update.Text is null ? null : await PrepareEnrichmentAsync(updated.Text, cancellationToken);
        var history = CreateHistoryEntry(updated, MemoryHistoryEvent.Update, existing.Text, updated.Text, embedding: updatedVector);

        await store.SaveBatchAsync([new MemoryWriteRecord(updated, updatedVector, history!)], cancellationToken);

        await indexLock.WaitAsync(cancellationToken);
        try { vectors[id] = updatedVector.ToArray(); }
        finally { indexLock.Release(); }

        if (enrichment is not null)
        {
            await ApplyEnrichmentAsync(enrichment, id, cancellationToken);
        }
        return updated;
    }

    public Task<Memory> UpdateAsync(string id, string text, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default) =>
        UpdateAsync(id, new MemoryUpdate { Text = text, Metadata = metadata }, cancellationToken);

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var existing = await store.GetAsync(id, cancellationToken);
        if (existing is not null) await RemoveEnrichmentAsync(id, cancellationToken);
        var history = existing is null ? null : CreateHistoryEntry(existing, MemoryHistoryEvent.Delete, existing.Text, null, DateTimeOffset.UtcNow, true);

        await store.DeleteAsync(id, history, cancellationToken);

        await indexLock.WaitAsync(cancellationToken);
        try { vectors.Remove(id); }
        finally { indexLock.Release(); }
    }

    public async Task<int> DeleteAllAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        var memories = await GetAllAsync(filter, cancellationToken);
        foreach (var memory in memories) await RemoveEnrichmentAsync(memory.Id, cancellationToken);

        var records = memories.Select(memory => new MemoryDeleteRecord(memory, CreateHistoryEntry(memory, MemoryHistoryEvent.Delete, memory.Text, null, DateTimeOffset.UtcNow, true)!)).ToArray();
        var deleted = await store.DeleteAllAsync(filter, records, cancellationToken);
        foreach (var memory in memories) await RemoveVectorAsync(memory.Id, cancellationToken);
        return deleted;
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await store.ResetAsync(cancellationToken);
        await trajectoryStore.ResetAsync(cancellationToken);
        await indexLock.WaitAsync(cancellationToken);
        try { vectors.Clear(); }
        finally { indexLock.Release(); }
        await entityStore.ResetAsync(cancellationToken);
        if (graphStore is not null) await graphStore.ResetAsync(cancellationToken);
    }

    private static IReadOnlyDictionary<string, string> CreateUpdatedMetadata(Memory existing, MemoryUpdate update)
    {
        var metadata = (update.Metadata ?? existing.Metadata).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (update.UpdateReferenceTime)
        {
            if (update.ReferenceTime.HasValue) AddReferenceTime(metadata, update.ReferenceTime);
            else metadata.Remove(TemporalMemoryMetadata.ReferenceTimeKey);
        }
        else if (!metadata.ContainsKey(TemporalMemoryMetadata.ReferenceTimeKey)
            && existing.Metadata.TryGetValue(TemporalMemoryMetadata.ReferenceTimeKey, out var referenceTime))
        {
            metadata[TemporalMemoryMetadata.ReferenceTimeKey] = referenceTime;
        }
        return metadata;
    }

    private static MemoryHistoryEntry? CreateHistoryEntry(Memory memory, MemoryHistoryEvent eventType, string? oldMemory, string? newMemory, DateTimeOffset? updatedAt = null, bool isDeleted = false, IReadOnlyList<float>? embedding = null)
    {
        return new MemoryHistoryEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            MemoryId = memory.Id,
            Event = eventType,
            Snapshot = memory,
            Embedding = embedding,
            OldMemory = oldMemory,
            NewMemory = newMemory,
            CreatedAt = memory.CreatedAt,
            UpdatedAt = updatedAt ?? memory.UpdatedAt,
            IsDeleted = isDeleted,
            ActorId = memory.Metadata.TryGetValue("actor_id", out var actorId) ? actorId : null,
            Role = memory.Metadata.TryGetValue("role", out var role) ? role : null
        };
    }

    private async Task RemoveVectorAsync(string memoryId, CancellationToken cancellationToken)
    {
        await indexLock.WaitAsync(cancellationToken);
        try { vectors.Remove(memoryId); }
        finally { indexLock.Release(); }
    }
}