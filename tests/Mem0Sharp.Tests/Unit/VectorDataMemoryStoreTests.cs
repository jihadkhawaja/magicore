using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Numerics.Tensors;
using Mem0Sharp;
using Microsoft.Extensions.VectorData;
using Xunit;

namespace Mem0Sharp.Tests.Unit;

public sealed class VectorDataMemoryStoreTests
{
    internal sealed class InMemoryTestRecordCollection<TRecord> : VectorStoreCollection<string, TRecord>
        where TRecord : class
    {
        private readonly ConcurrentDictionary<string, TRecord> _records = new(StringComparer.Ordinal);
        private readonly Func<TRecord, string> _keySelector;
        private readonly Func<TRecord, ReadOnlyMemory<float>?>? _vectorSelector;

        public InMemoryTestRecordCollection(
            string name,
            Func<TRecord, string> keySelector,
            Func<TRecord, ReadOnlyMemory<float>?>? vectorSelector = null)
        {
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
            return Task.FromResult(record);
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

    [Fact]
    public async Task GetAllAsync_NewStoreInstance_ReturnsPersistedRecordsWithFiltering()
    {
        var collection = new InMemoryTestRecordCollection<VectorDataMemoryRecord>("persisted", record => record.Id);
        var writer = new VectorDataMemoryStore(collection);
        for (var index = 0; index < 12; index++)
        {
            await writer.SaveAsync(new Memory
            {
                Id = $"memory-{index}", Text = "Warehouse observation", UserId = "robot", AgentId = "camera"
            });
        }
        await writer.SaveAsync(new Memory { Id = "other-user", Text = "Private", UserId = "other", AgentId = "camera" });
        await writer.SaveAsync(new Memory { Id = "other-agent", Text = "Private", UserId = "robot", AgentId = "other" });
        await writer.SaveAsync(new Memory
        {
            Id = "expired", Text = "Old", UserId = "robot", AgentId = "camera", ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1)
        });

        var reader = new VectorDataMemoryStore(collection);
        var records = new List<Memory>();
        await foreach (var memory in reader.GetAllAsync(new MemoryFilter(UserId: "robot", AgentId: "camera")))
            records.Add(memory);

        Assert.Equal(12, records.Count);
        Assert.All(records, memory => Assert.StartsWith("memory-", memory.Id));
    }

    [Fact]
    public void VectorDataMemoryRecord_Roundtrip_PreservesAllFields()
    {
        var original = new Memory
        {
            Id = "mem-123",
            Text = "Prefers dark mode and C#",
            UserId = "user-1",
            AgentId = "agent-1",
            RunId = "run-1",
            Scope = MemoryScope.User,
            Metadata = new Dictionary<string, string> { ["theme"] = "dark", ["lang"] = "csharp" },
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
            ExpiresAt = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
            Hash = "hash-123",
            Behavior = MemoryBehavior.Normal,
            MemoryType = "fact"
        };

        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var record = VectorDataMemoryRecord.FromMemory(original, embedding);

        Assert.Equal("mem-123", record.Id);
        Assert.Equal("Prefers dark mode and C#", record.Text);
        Assert.Equal("user-1", record.UserId);
        Assert.Equal("agent-1", record.AgentId);
        Assert.Equal("run-1", record.RunId);
        Assert.Equal("User", record.Scope);
        Assert.NotNull(record.Vector);
        Assert.Equal(3, record.Vector.Value.Length);

        var restored = record.ToMemory();

        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Text, restored.Text);
        Assert.Equal(original.UserId, restored.UserId);
        Assert.Equal(original.AgentId, restored.AgentId);
        Assert.Equal(original.RunId, restored.RunId);
        Assert.Equal(original.Scope, restored.Scope);
        Assert.Equal(original.Metadata["theme"], restored.Metadata["theme"]);
        Assert.Equal(original.Metadata["lang"], restored.Metadata["lang"]);
        Assert.Equal(original.CreatedAt, restored.CreatedAt);
        Assert.Equal(original.UpdatedAt, restored.UpdatedAt);
        Assert.Equal(original.ExpiresAt, restored.ExpiresAt);
        Assert.Equal(original.Hash, restored.Hash);
        Assert.Equal(original.Behavior, restored.Behavior);
        Assert.Equal(original.MemoryType, restored.MemoryType);
    }

    [Fact]
    public void VectorDataHistoryRecord_Roundtrip_PreservesAllFields()
    {
        var original = new MemoryHistoryEntry
        {
            Id = "hist-123",
            MemoryId = "mem-123",
            Event = MemoryHistoryEvent.Update,
            OldMemory = "Prefers light mode",
            NewMemory = "Prefers dark mode",
            ActorId = "actor-1",
            Role = "user",
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
            IsDeleted = false
        };

        var record = VectorDataHistoryRecord.FromEntry(original);
        Assert.Equal("hist-123", record.Id);
        Assert.Equal("mem-123", record.MemoryId);
        Assert.Equal("Update", record.Event);
        Assert.Equal("actor-1", record.ActorId);

        var restored = record.ToEntry();
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.MemoryId, restored.MemoryId);
        Assert.Equal(original.Event, restored.Event);
        Assert.Equal(original.OldMemory, restored.OldMemory);
        Assert.Equal(original.NewMemory, restored.NewMemory);
        Assert.Equal(original.ActorId, restored.ActorId);
        Assert.Equal(original.Role, restored.Role);
        Assert.Equal(original.CreatedAt, restored.CreatedAt);
        Assert.Equal(original.UpdatedAt, restored.UpdatedAt);
        Assert.Equal(original.IsDeleted, restored.IsDeleted);
    }

    [Fact]
    public async Task VectorDataMemoryStore_CrudAndSearch_WorksAsExpected()
    {
        var collection = new InMemoryTestRecordCollection<VectorDataMemoryRecord>(
            "mem0_memories",
            r => r.Id,
            r => r.Vector);

        var historyCollection = new InMemoryTestRecordCollection<VectorDataHistoryRecord>(
            "mem0_history",
            r => r.Id);

        var store = new VectorDataMemoryStore(collection, historyCollection);
        await store.InitializeAsync();

        var memory1 = new Memory
        {
            Id = "m1",
            Text = "Alice loves coffee",
            UserId = "alice",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var embedding1 = new float[] { 1.0f, 0.0f, 0.0f };

        var memory2 = new Memory
        {
            Id = "m2",
            Text = "Bob loves tea",
            UserId = "bob",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var embedding2 = new float[] { 0.0f, 1.0f, 0.0f };

        // Save
        await store.SaveAsync(memory1, embedding1);
        await store.SaveAsync(memory2, embedding2);

        // Get
        var fetched = await store.GetAsync("m1");
        Assert.NotNull(fetched);
        Assert.Equal("Alice loves coffee", fetched.Text);

        // GetAll
        var allMemories = new List<Memory>();
        await foreach (var m in store.GetAllAsync())
        {
            allMemories.Add(m);
        }
        Assert.Equal(2, allMemories.Count);

        // Search with embedding & filter
        var searchResults = await store.SearchAsync(new float[] { 0.9f, 0.1f, 0.0f }, new MemoryFilter(UserId: "alice"), topK: 5);
        Assert.Single(searchResults);
        Assert.Equal("m1", searchResults[0].Memory.Id);
        Assert.True(searchResults[0].Score > 0.8);

        // Delete
        await store.DeleteAsync("m1");
        var afterDelete = await store.GetAsync("m1");
        Assert.Null(afterDelete);
    }

    [Fact]
    public async Task VectorDataMemoryStore_IntegratedWithMemoryService_EndToEnd()
    {
        var collection = new InMemoryTestRecordCollection<VectorDataMemoryRecord>(
            "mem0_memories",
            r => r.Id,
            r => r.Vector);

        var store = collection.ToMemoryStore();
        await store.InitializeAsync();

        var memoryService = new MemoryService(store: store);

        // Add memories via MemoryService
        await memoryService.AddAsync("I prefer dark mode and vim keybindings", userId: "alice");
        await memoryService.AddAsync("I enjoy hiking on weekends", userId: "alice");
        await memoryService.AddAsync("Bob enjoys mountain biking", userId: "bob");

        // Search for Alice's editor preference
        var results = await memoryService.SearchAsync(
            "What editor settings does Alice prefer?",
            new MemoryFilter(UserId: "alice"),
            topK: 3);

        Assert.NotEmpty(results);
        Assert.Contains("dark mode", results[0].Memory.Text);
        Assert.All(results, r => Assert.Equal("alice", r.Memory.UserId));
    }

    [Fact]
    public async Task SearchAsync_PagesUntilMetadataFilterHasEnoughMatches()
    {
        var collection = new InMemoryTestRecordCollection<VectorDataMemoryRecord>(
            "mem0_memories",
            record => record.Id,
            record => record.Vector);
        var store = new VectorDataMemoryStore(collection);

        for (var index = 0; index < 12; index++)
        {
            await store.SaveAsync(CreateMemory($"bob-{index}", "bob"), [1.0f, 0.0f]);
            await store.SaveAsync(CreateMemory($"alice-{index}", "alice"), [0.99f, 0.01f]);
        }

        var target = CreateMemory("target", "alice") with
        {
            Metadata = new Dictionary<string, string> { ["kind"] = "target" }
        };
        await store.SaveAsync(target, [0.9f, 0.1f]);

        var results = await store.SearchAsync(
            [1.0f, 0.0f],
            new MemoryFilter(
                UserId: "alice",
                Metadata: new MetadataFilter("kind", FilterOperator.Equal, "target")),
            topK: 1);

        Assert.Equal("target", Assert.Single(results).Memory.Id);
    }

    [Fact]
    public void CollectionDefinition_UsesConfiguredVectorDimensions()
    {
        var definition = VectorDataMemoryStore.CreateMemoryCollectionDefinition(1536);
        var vectorProperty = Assert.IsType<VectorStoreVectorProperty>(
            Assert.Single(definition.Properties, property => property.Name == nameof(VectorDataMemoryRecord.Vector)));

        Assert.Equal(1536, vectorProperty.Dimensions);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VectorDataMemoryStore.CreateInMemory(new VectorDataMemoryStoreOptions { VectorDimensions = 0 }));
    }

    private static Memory CreateMemory(string id, string userId) => new()
    {
        Id = id,
        Text = id,
        UserId = userId,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
}
