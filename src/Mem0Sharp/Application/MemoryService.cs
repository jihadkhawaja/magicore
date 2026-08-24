using System.Globalization;
using System.Numerics.Tensors;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;

namespace Mem0Sharp;

public sealed class MemoryService : IMemoryService
{
    private readonly IMemoryStore store;
    private readonly IEmbeddingGenerator<string, Embedding<float>> embeddings;
    private readonly IEmbeddingGenerator<DataContent, Embedding<float>>? imageEmbeddings;
    private readonly IMemoryExtractor extractor;
    private readonly IMemoryReranker? reranker;
    private readonly IMemoryConflictResolver? conflictResolver;
    private readonly IProceduralMemoryGenerator? proceduralMemoryGenerator;
    private readonly IEntityExtractor entityExtractor;
    private readonly IEntityStore entityStore;
    private readonly IGraphMemoryExtractor? graphExtractor;
    private readonly IGraphMemoryStore? graphStore;
    private readonly IAdmissionGate? admissionGate;
    private readonly IConsolidationVerifier? consolidationVerifier;
    private readonly ITrajectoryStore trajectoryStore;
    private readonly MemoryOptions options;
    private readonly Dictionary<string, float[]> vectors = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim indexLock = new(1, 1);

    public MemoryService(
        IMemoryStore? store = null,
        IEmbeddingGenerator<string, Embedding<float>>? embeddings = null,
        IMemoryExtractor? extractor = null,
        MemoryOptions? options = null,
        IMemoryReranker? reranker = null,
        IMemoryConflictResolver? conflictResolver = null,
        IProceduralMemoryGenerator? proceduralMemoryGenerator = null,
        IEntityExtractor? entityExtractor = null,
        IEntityStore? entityStore = null,
        IGraphMemoryExtractor? graphExtractor = null,
        IGraphMemoryStore? graphStore = null,
        IAdmissionGate? admissionGate = null,
        IConsolidationVerifier? consolidationVerifier = null,
        ITrajectoryStore? trajectoryStore = null,
        IEmbeddingGenerator<DataContent, Embedding<float>>? imageEmbeddings = null)
    {
        this.store = store ?? new InMemoryStore();
        this.embeddings = embeddings ?? new LocalEmbeddingGenerator();
        this.extractor = extractor ?? new BasicMemoryExtractor();
        this.options = options ?? new MemoryOptions();
        this.reranker = reranker;
        this.conflictResolver = conflictResolver;
        this.proceduralMemoryGenerator = proceduralMemoryGenerator;
        this.entityExtractor = entityExtractor ?? new RuleBasedEntityExtractor();
        this.entityStore = entityStore ?? new InMemoryEntityStore();
        this.graphExtractor = graphExtractor;
        this.graphStore = graphStore;
        this.admissionGate = admissionGate;
        this.consolidationVerifier = consolidationVerifier;
        this.trajectoryStore = trajectoryStore ?? new InMemoryTrajectoryStore();
        this.imageEmbeddings = imageEmbeddings;
    }

    public Task<AddResult> AddAsync(string text, MemoryAddOptions? options = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(text);
        var addOptions = options ?? new MemoryAddOptions();
        if (addOptions.Infer && addOptions.Behavior != MemoryBehavior.Normal)
        {
            return AddAsync([new Message("user", text)], addOptions, cancellationToken);
        }
        return SaveInputsAsync([new MemoryInput(text, addOptions.Scope, addOptions.Metadata, addOptions.ExpiresAt, addOptions.Behavior, addOptions.MemoryType)], addOptions, cancellationToken);
    }

    public Task<AddResult> AddAsync(string text, string userId, string? agentId = null, string? runId = null, MemoryScope scope = MemoryScope.User, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default) =>
        AddAsync(text, new MemoryAddOptions { UserId = userId, AgentId = agentId, RunId = runId, Scope = scope, Metadata = metadata }, cancellationToken);

    public Task<AddResult> AddAsync(IEnumerable<Message> messages, string userId, string? agentId = null, string? runId = null, MemoryScope scope = MemoryScope.User, CancellationToken cancellationToken = default) =>
        AddAsync(messages, new MemoryAddOptions { UserId = userId, AgentId = agentId, RunId = runId, Scope = scope }, cancellationToken);

    public Task<AddResult> AddAsync(IEnumerable<ChatMessage> chatMessages, MemoryAddOptions? options = null, CancellationToken cancellationToken = default) =>
        AddAsync(chatMessages.Select(Message.FromChatMessage), options, cancellationToken);

    public Task<AddResult> AddAsync(IEnumerable<ChatMessage> chatMessages, string userId, string? agentId = null, string? runId = null, MemoryScope scope = MemoryScope.User, CancellationToken cancellationToken = default) =>
        AddAsync(chatMessages.Select(Message.FromChatMessage), new MemoryAddOptions { UserId = userId, AgentId = agentId, RunId = runId, Scope = scope }, cancellationToken);

    public async Task<AddResult> AddAsync(IEnumerable<Message> messages, MemoryAddOptions? options = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(messages);
        var addOptions = options ?? new MemoryAddOptions();
        var materialized = messages.ToArray();
        if (string.Equals(addOptions.MemoryType, "procedural_memory", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(addOptions.AgentId)) throw new ArgumentException("Procedural memory requires an AgentId.", nameof(options));
            if (proceduralMemoryGenerator is null) throw new InvalidOperationException("Procedural memory requires an IProceduralMemoryGenerator.");
            var procedure = await proceduralMemoryGenerator.GenerateAsync(materialized, addOptions.Prompt, cancellationToken);
            return await SaveInputsAsync([new MemoryInput(procedure, MemoryScope.Agent, Behavior: addOptions.Behavior, MemoryType: "procedural_memory")], addOptions with { Scope = MemoryScope.Agent }, cancellationToken);
        }
        if (addOptions.Infer && conflictResolver is not null)
        {
            var existing = await GetAllAsync(CreateScopeFilter(addOptions), cancellationToken);
            var decisions = await conflictResolver.ResolveAsync(materialized, existing, addOptions, cancellationToken);
            return await ApplyDecisionsAsync(decisions, addOptions, cancellationToken);
        }
        IReadOnlyList<MemoryInput> inputs = addOptions.Infer
            ? await extractor.ExtractAsync(materialized, addOptions, cancellationToken)
            : materialized.Where(message => !string.IsNullOrWhiteSpace(message.Content))
                .Select(message => new MemoryInput(message.Content.Trim(), Metadata: new Dictionary<string, string> { ["role"] = message.Role }))
                .ToArray();
        return await SaveInputsAsync(inputs.Select(input => input with { Scope = addOptions.Scope, Behavior = addOptions.Behavior, MemoryType = addOptions.MemoryType ?? input.MemoryType }), addOptions, cancellationToken);
    }

    public Task<AddResult> AddManyAsync(IEnumerable<string> texts, MemoryAddOptions? options = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(texts);
        var addOptions = options ?? new MemoryAddOptions();
        return SaveInputsAsync(texts.Select(text => new MemoryInput(text, addOptions.Scope, addOptions.Metadata, addOptions.ExpiresAt, addOptions.Behavior, addOptions.MemoryType)), addOptions, cancellationToken);
    }

    public async Task<AddResult> AddAsync(DataContent image, MemoryAddOptions? options = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(image);
        var addOptions = options ?? new MemoryAddOptions();
        var metadata = (addOptions.Metadata ?? new Dictionary<string, string>()).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(image.MediaType)) metadata["media_type"] = image.MediaType!;
        if (image.Uri is not null) metadata["media_uri"] = image.Uri.ToString();

        if (addOptions.Infer && extractor is not null)
        {
            var msg = new Message("user", string.Empty, [image]);
            return await AddAsync([msg], addOptions with { Metadata = metadata }, cancellationToken);
        }

        if (imageEmbeddings is not null)
        {
            var vector = await imageEmbeddings.GenerateVectorCoreAsync(image, cancellationToken);
            var text = !string.IsNullOrWhiteSpace(addOptions.Prompt)
                ? addOptions.Prompt!
                : (image.Uri is not null ? $"Image: {image.Uri}" : $"Image ({image.MediaType ?? "binary"})");

            var memory = new Memory
            {
                Id = Guid.NewGuid().ToString("N"),
                Text = text,
                UserId = addOptions.UserId,
                AgentId = addOptions.AgentId,
                RunId = addOptions.RunId,
                Scope = addOptions.Scope,
                Metadata = metadata,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = addOptions.ExpiresAt,
                Hash = ComputeHash(text),
                Behavior = addOptions.Behavior,
                MemoryType = addOptions.MemoryType ?? "image_memory"
            };

            var history = CreateHistoryEntry(memory, MemoryHistoryEvent.Add, null, memory.Text);
            await store.SaveBatchAsync([new MemoryWriteRecord(memory, vector, history!)], cancellationToken);

            await indexLock.WaitAsync(cancellationToken);
            try { vectors[memory.Id] = vector.ToArray(); }
            finally { indexLock.Release(); }

            return new AddResult([memory], [new MemoryActionResult(memory.Id, memory.Text, MemoryAction.Add)]);
        }

        var fallbackMsg = new Message("user", string.Empty, [image]);
        return await AddAsync([fallbackMsg], addOptions with { Metadata = metadata }, cancellationToken);
    }

    public Task<AddResult> AddAsync(ReadOnlyMemory<byte> imageData, string mediaType, MemoryAddOptions? options = null, CancellationToken cancellationToken = default) =>
        AddAsync(new DataContent(imageData, mediaType), options, cancellationToken);

    public Task<AddResult> AddAsync(Uri imageUri, string mediaType = "image/jpeg", MemoryAddOptions? options = null, CancellationToken cancellationToken = default) =>
        AddAsync(Message.CreateDataContent(imageUri, mediaType), options, cancellationToken);

    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, MemorySearchOptions? searchOptions = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(query);
        var effective = searchOptions ?? new MemorySearchOptions { TopK = options.DefaultTopK, Threshold = options.MinimumScore, Hybrid = options.EnableHybridSearch };
        return SearchCoreAsync(query, effective, cancellationToken);
    }

    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, MemoryFilter? filter, int? topK = null, CancellationToken cancellationToken = default) =>
        SearchAsync(query, new MemorySearchOptions { Filter = filter, TopK = topK ?? options.DefaultTopK }, cancellationToken);

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(DataContent image, MemorySearchOptions? searchOptions = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(image);
        if (imageEmbeddings is null)
        {
            throw new InvalidOperationException("An image embedding generator (IEmbeddingGenerator<DataContent, Embedding<float>>) is required to perform image searches.");
        }
        var effective = searchOptions ?? new MemorySearchOptions { TopK = options.DefaultTopK, Threshold = options.MinimumScore, Hybrid = false };
        if (effective.TopK < 0) throw new ArgumentOutOfRangeException(nameof(searchOptions));

        var effectiveOptions = effective with { Filter = ApplySearchFilter(effective) };
        var queryVector = await imageEmbeddings.GenerateVectorCoreAsync(image, cancellationToken);
        var candidateLimit = Math.Max(effective.TopK * 4, 60);
        var semanticResults = await store.SearchAsync(queryVector, effectiveOptions.Filter, candidateLimit, cancellationToken);

        return semanticResults
            .Where(result => result.Score >= effective.Threshold)
            .OrderByDescending(result => result.Score)
            .Take(effective.TopK)
            .Select(result => effective.Explain ? result with { ScoreDetails = new SearchScoreDetails(result.Score, Threshold: effective.Threshold) } : result with { ScoreDetails = null })
            .ToArray();
    }

    public Task<IReadOnlyList<SearchResult>> SearchAsync(ReadOnlyMemory<byte> imageData, string mediaType, MemorySearchOptions? options = null, CancellationToken cancellationToken = default) =>
        SearchAsync(new DataContent(imageData, mediaType), options, cancellationToken);

    public Task<IReadOnlyList<SearchResult>> SearchAsync(Uri imageUri, string mediaType = "image/jpeg", MemorySearchOptions? options = null, CancellationToken cancellationToken = default) =>
        SearchAsync(Message.CreateDataContent(imageUri, mediaType), options, cancellationToken);

    private async Task<IReadOnlyList<SearchResult>> SearchCoreAsync(string query, MemorySearchOptions searchOptions, CancellationToken cancellationToken)
    {
        if (searchOptions.TopK < 0) throw new ArgumentOutOfRangeException(nameof(searchOptions));
        var effectiveOptions = searchOptions with { Filter = ApplySearchFilter(searchOptions) };
        var queryVector = await embeddings.GenerateVectorCoreAsync(query, cancellationToken);
        var candidateLimit = Math.Max(searchOptions.TopK * 4, 60);
        var semanticResults = await store.SearchAsync(queryVector, effectiveOptions.Filter, candidateLimit, cancellationToken);

        return await RankSearchResultsAsync(query, effectiveOptions, semanticResults, cancellationToken);
    }

    private async Task<IReadOnlyList<SearchResult>> RankSearchResultsAsync(string query, MemorySearchOptions searchOptions, IReadOnlyList<SearchResult> semanticResults, CancellationToken cancellationToken)
    {
        var queryEntities = await entityExtractor.ExtractAsync(query, cancellationToken);
        var entityBoosts = (await entityStore.GetMemoryBoostsAsync(queryEntities, cancellationToken)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (graphStore is not null)
        {
            foreach (var boost in await graphStore.GetMemoryBoostsAsync(query, cancellationToken))
            {
                entityBoosts[boost.Key] = Math.Min(0.5, entityBoosts.GetValueOrDefault(boost.Key) + boost.Value);
            }
        }
        IReadOnlyList<SearchResult> ranked = searchOptions.Hybrid
            ? HybridSearchScorer.ScoreAndRank(query, semanticResults, entityBoosts, searchOptions.Threshold, searchOptions.TopK, searchOptions.Explain)
            : semanticResults.Where(result => result.Score >= searchOptions.Threshold)
                .OrderByDescending(result => result.Score)
                .Take(searchOptions.TopK)
                .Select(result => searchOptions.Explain ? result with { ScoreDetails = new SearchScoreDetails(result.Score, Threshold: searchOptions.Threshold) } : result with { ScoreDetails = null })
                .ToArray();
        if (searchOptions.Rerank && reranker is not null)
        {
            ranked = await reranker.RerankAsync(query, ranked, searchOptions.TopK, cancellationToken);
        }
        if (searchOptions.RecencyBias > 0)
        {
            ranked = ApplyRecencyBias(ranked, searchOptions);
        }
        return ranked;
    }

    private static IReadOnlyList<SearchResult> ApplyRecencyBias(IReadOnlyList<SearchResult> ranked, MemorySearchOptions searchOptions)
    {
        var recencyBias = Compatibility.Clamp(searchOptions.RecencyBias, 0d, 1d);
        if (recencyBias <= 0) return ranked;
        var window = searchOptions.FreshnessWindow ?? TimeSpan.FromDays(30);
        if (window <= TimeSpan.Zero) window = TimeSpan.FromDays(30);
        var now = DateTimeOffset.UtcNow;

        return ranked
            .Select(result =>
            {
                var age = now - result.Memory.UpdatedAt;
                var freshness = age <= TimeSpan.Zero ? 1d : Compatibility.Clamp(1d - age.TotalSeconds / window.TotalSeconds, 0d, 1d);
                var boostedScore = result.Score * (1d - recencyBias) + freshness * recencyBias;
                return result with { Score = boostedScore };
            })
            .OrderByDescending(result => result.Score)
            .ToArray();
    }

    public async Task<IReadOnlyList<IReadOnlyList<SearchResult>>> SearchManyAsync(IEnumerable<string> queries, MemorySearchOptions? searchOptions = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(queries);
        var materialized = queries.ToArray();
        foreach (var query in materialized) Guard.NotNullOrWhiteSpace(query);
        if (materialized.Length == 0) return [];

        var effective = searchOptions ?? new MemorySearchOptions { TopK = options.DefaultTopK, Threshold = options.MinimumScore, Hybrid = options.EnableHybridSearch };
        effective = effective with { Filter = ApplySearchFilter(effective) };
        if (effective.TopK < 0) throw new ArgumentOutOfRangeException(nameof(searchOptions));

        var queryVectors = await embeddings.GenerateVectorBatchCoreAsync(materialized, cancellationToken);
        if (queryVectors.Count != materialized.Length) throw new InvalidOperationException("The embedding provider returned a different number of vectors than input queries.");
        var semanticBatches = await store.SearchBatchAsync(queryVectors, effective.Filter, Math.Max(effective.TopK * 4, 60), cancellationToken);
        if (semanticBatches.Count != materialized.Length) throw new InvalidOperationException("The vector store returned a different number of result sets than input queries.");

        var batchedResults = new IReadOnlyList<SearchResult>[materialized.Length];
        for (var index = 0; index < materialized.Length; index++)
        {
            batchedResults[index] = await RankSearchResultsAsync(materialized[index], effective, semanticBatches[index], cancellationToken);
        }
        return batchedResults;
    }

    public Task<IReadOnlyList<IReadOnlyList<SearchResult>>> SearchManyAsync(IEnumerable<string> queries, MemoryFilter? filter, int? topK = null, CancellationToken cancellationToken = default) =>
        SearchManyAsync(queries, new MemorySearchOptions { Filter = filter, TopK = topK ?? options.DefaultTopK }, cancellationToken);

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
        var updated = existing with
        {
            Text = update.Text ?? existing.Text,
            Metadata = update.Metadata ?? existing.Metadata,
            ExpiresAt = update.UpdateExpiration ? update.ExpiresAt : existing.ExpiresAt,
            Hash = update.Text is null ? existing.Hash : ComputeHash(update.Text),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var updatedVector = await embeddings.GenerateVectorCoreAsync(updated.Text, cancellationToken);
        var enrichment = update.Text is null ? null : await PrepareEnrichmentAsync(updated.Text, cancellationToken);
        var history = CreateHistoryEntry(updated, MemoryHistoryEvent.Update, existing.Text, updated.Text);

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

    private async Task<AddResult> SaveInputsAsync(IEnumerable<MemoryInput> inputs, MemoryAddOptions addOptions, CancellationToken cancellationToken)
    {
        var saved = new List<Memory>();
        var actions = new List<MemoryActionResult>();
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        var existingMemories = new List<Memory>();

        if (addOptions.Deduplicate || admissionGate is not null)
        {
            await foreach (var existing in store.GetAllAsync(CreateScopeFilter(addOptions), cancellationToken))
            {
                existingMemories.Add(existing);
                if (addOptions.Deduplicate)
                {
                    hashes.Add(string.IsNullOrEmpty(existing.Hash) ? ComputeHash(existing.Text) : existing.Hash);
                }
            }
        }

        var pending = new List<Memory>();
        foreach (var input in inputs.Where(item => !string.IsNullOrWhiteSpace(item.Text)))
        {
            var text = input.Text.Trim();

            // Admission Gate Evaluation (SSGM / VMG)
            if (admissionGate is not null)
            {
                var actorId = addOptions.Metadata?.TryGetValue("actor_id", out var a) == true ? a : null;
                var role = addOptions.Metadata?.TryGetValue("role", out var r) == true ? r : null;
                var context = new MemoryAdmissionContext(
                    Text: text,
                    UserId: addOptions.UserId,
                    AgentId: addOptions.AgentId,
                    RunId: addOptions.RunId,
                    ActorId: actorId,
                    Role: role,
                    Scope: input.Scope,
                    Metadata: input.Metadata ?? addOptions.Metadata,
                    Behavior: input.Behavior,
                    MemoryType: input.MemoryType ?? addOptions.MemoryType);

                var decision = await admissionGate.EvaluateAsync(context, existingMemories, cancellationToken);
                if (!decision.IsAdmitted)
                {
                    actions.Add(new MemoryActionResult(null, text, MemoryAction.None));
                    continue;
                }
            }

            var hash = ComputeHash(text);
            if (addOptions.Deduplicate && !hashes.Add(hash))
            {
                actions.Add(new MemoryActionResult(null, text, MemoryAction.None));
                continue;
            }
            var now = DateTimeOffset.UtcNow;
            var metadata = (addOptions.Metadata ?? new Dictionary<string, string>()).ToDictionary(pair => pair.Key, pair => pair.Value);
            if (input.Metadata is not null)
            {
                foreach (var item in input.Metadata) metadata[item.Key] = item.Value;
            }
            pending.Add(new Memory
            {
                Id = Guid.NewGuid().ToString("N"),
                Text = text,
                UserId = addOptions.UserId,
                AgentId = addOptions.AgentId,
                RunId = addOptions.RunId,
                Scope = input.Scope,
                Metadata = metadata,
                CreatedAt = now,
                UpdatedAt = now,
                ExpiresAt = input.ExpiresAt ?? addOptions.ExpiresAt,
                Hash = hash,
                Behavior = input.Behavior,
                MemoryType = input.MemoryType
            });
        }

        if (pending.Count == 0)
        {
            return new AddResult(saved, actions);
        }

        IReadOnlyList<IReadOnlyList<float>> generatedVectors = await embeddings.GenerateVectorBatchCoreAsync(pending.Select(memory => memory.Text).ToArray(), cancellationToken);
        if (generatedVectors.Count != pending.Count) throw new InvalidOperationException("The embedding provider returned a different number of vectors than input texts.");

        var records = pending.Select((memory, index) => new MemoryVectorRecord(memory, generatedVectors[index])).ToArray();
        var enrichments = new Dictionary<string, MemoryEnrichment>(StringComparer.Ordinal);
        foreach (var record in records) enrichments[record.Memory.Id] = await PrepareEnrichmentAsync(record.Memory.Text, cancellationToken);
        var historyEntries = records.Select(record => CreateHistoryEntry(record.Memory, MemoryHistoryEvent.Add, null, record.Memory.Text)).ToArray();

        var writeRecords = records.Select((record, index) => new MemoryWriteRecord(record.Memory, record.Embedding, historyEntries[index]!)).ToArray();
        await store.SaveBatchAsync(writeRecords, cancellationToken);

        foreach (var record in records)
        {
            var memory = record.Memory;
            await indexLock.WaitAsync(cancellationToken);
            try { vectors[memory.Id] = record.Embedding.ToArray(); }
            finally { indexLock.Release(); }
            await ApplyEnrichmentAsync(enrichments[memory.Id], memory.Id, cancellationToken);
            saved.Add(memory);
            actions.Add(new MemoryActionResult(memory.Id, memory.Text, MemoryAction.Add));
        }
        return new AddResult(saved, actions);
    }

    private async Task<AddResult> ApplyDecisionsAsync(IReadOnlyList<MemoryDecision> decisions, MemoryAddOptions addOptions, CancellationToken cancellationToken)
    {
        var memories = new List<Memory>();
        var actions = new List<MemoryActionResult>();
        foreach (var decision in decisions)
        {
            switch (decision.Event)
            {
                case MemoryAction.Add:
                    var added = await SaveInputsAsync([new MemoryInput(decision.Text, addOptions.Scope, decision.Metadata, Behavior: addOptions.Behavior, MemoryType: addOptions.MemoryType)], addOptions, cancellationToken);
                    memories.AddRange(added.Memories);
                    actions.AddRange(added.Actions ?? []);
                    break;
                case MemoryAction.Update when decision.MemoryId is not null:
                    var updated = await UpdateAsync(decision.MemoryId, new MemoryUpdate { Text = decision.Text, Metadata = decision.Metadata }, cancellationToken);
                    memories.Add(updated);
                    actions.Add(new MemoryActionResult(updated.Id, updated.Text, MemoryAction.Update));
                    break;
                case MemoryAction.Delete when decision.MemoryId is not null:
                    var deleted = await GetAsync(decision.MemoryId, cancellationToken);
                    await DeleteAsync(decision.MemoryId, cancellationToken);
                    actions.Add(new MemoryActionResult(decision.MemoryId, deleted?.Text, MemoryAction.Delete));
                    break;
                default:
                    actions.Add(new MemoryActionResult(decision.MemoryId, decision.Text, MemoryAction.None));
                    break;
            }
        }
        return new AddResult(memories, actions);
    }

    private static MemoryFilter CreateScopeFilter(MemoryAddOptions addOptions) => new(addOptions.UserId, addOptions.AgentId, addOptions.RunId, addOptions.Scope);

    private static MemoryFilter? ApplySearchFilter(MemorySearchOptions searchOptions)
    {
        if (searchOptions.Behavior is not null)
        {
            return (searchOptions.Filter ?? new MemoryFilter()) with { Behavior = searchOptions.Behavior };
        }
        if (searchOptions.IncludeNonFactual || searchOptions.Filter?.Behavior is not null) return searchOptions.Filter;
        return (searchOptions.Filter ?? new MemoryFilter()) with { Behavior = MemoryBehavior.Normal };
    }

    private static string ComputeHash(string text) => Compatibility.Sha256Hex(text);

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

    private static MemoryHistoryEntry? CreateHistoryEntry(Memory memory, MemoryHistoryEvent eventType, string? oldMemory, string? newMemory, DateTimeOffset? updatedAt = null, bool isDeleted = false)
    {
        return new MemoryHistoryEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            MemoryId = memory.Id,
            Event = eventType,
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

    public static double CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        if (left.Count != right.Count || left.Count == 0) return 0;
        if (left is float[] leftArr && right is float[] rightArr)
        {
            return TensorPrimitives.CosineSimilarity(leftArr, rightArr);
        }
        var leftSpan = left.ToArray();
        var rightSpan = right.ToArray();
        return TensorPrimitives.CosineSimilarity(leftSpan, rightSpan);
    }
}