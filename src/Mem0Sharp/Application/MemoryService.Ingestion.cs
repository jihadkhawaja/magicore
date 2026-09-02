using System.Globalization;
using Microsoft.Extensions.AI;

namespace Mem0Sharp;

public sealed partial class MemoryService : IMemoryService
{
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
        AddReferenceTime(metadata, addOptions.ReferenceTime);

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

    private async Task<AddResult> SaveInputsAsync(IEnumerable<MemoryInput> inputs, MemoryAddOptions addOptions, CancellationToken cancellationToken)
    {
        var saved = new List<Memory>();
        var actions = new List<MemoryActionResult>();
        var deduplicationKeys = new HashSet<string>(StringComparer.Ordinal);
        var existingMemories = new List<Memory>();

        if (addOptions.Deduplicate || admissionGate is not null)
        {
            await foreach (var existing in store.GetAllAsync(CreateScopeFilter(addOptions), cancellationToken))
            {
                existingMemories.Add(existing);
                if (addOptions.Deduplicate)
                {
                    var contentHash = string.IsNullOrEmpty(existing.Hash) ? ComputeHash(existing.Text) : existing.Hash;
                    deduplicationKeys.Add(ComputeDeduplicationKey(contentHash, GetReferenceTime(existing.Metadata)));
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
            var referenceTime = addOptions.ReferenceTime ?? GetReferenceTime(input.Metadata) ?? GetReferenceTime(addOptions.Metadata);
            if (addOptions.Deduplicate && !deduplicationKeys.Add(ComputeDeduplicationKey(hash, referenceTime)))
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
            AddReferenceTime(metadata, addOptions.ReferenceTime);
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
        var historyEntries = records.Select(record => CreateHistoryEntry(record.Memory, MemoryHistoryEvent.Add, null, record.Memory.Text, embedding: record.Embedding)).ToArray();

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
                    var updated = await UpdateAsync(decision.MemoryId, new MemoryUpdate
                    {
                        Text = decision.Text,
                        Metadata = decision.Metadata,
                        UpdateReferenceTime = addOptions.ReferenceTime.HasValue,
                        ReferenceTime = addOptions.ReferenceTime
                    }, cancellationToken);
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

    private static void AddReferenceTime(IDictionary<string, string> metadata, DateTimeOffset? referenceTime)
    {
        if (referenceTime.HasValue)
        {
            metadata[TemporalMemoryMetadata.ReferenceTimeKey] = referenceTime.Value.ToString("O", CultureInfo.InvariantCulture);
        }
    }

    private static string ComputeHash(string text) => Compatibility.Sha256Hex(text);

    private static string ComputeDeduplicationKey(string contentHash, DateTimeOffset? referenceTime) =>
        referenceTime.HasValue
            ? contentHash + ":" + referenceTime.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            : contentHash;

    private static DateTimeOffset? GetReferenceTime(IReadOnlyDictionary<string, string>? metadata) =>
        metadata is not null
        && metadata.TryGetValue(TemporalMemoryMetadata.ReferenceTimeKey, out var value)
        && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var referenceTime)
            ? referenceTime
            : null;
}