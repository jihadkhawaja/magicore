using MagiCore;
using Microsoft.Extensions.AI;
using Xunit;

namespace MagiCore.Tests;

public sealed class MemoryServiceTests
{
    [Fact]
    public async Task AddAndSearchRanksRelatedMemory()
    {
        var service = new MemoryService();
        await service.AddAsync("I prefer dark mode and vim keybindings", "alice");
        await service.AddAsync("I enjoy hiking on weekends", "alice");

        var results = await service.SearchAsync("What editor settings does Alice prefer?", new MemoryFilter(UserId: "alice"));

        Assert.NotEmpty(results);
        Assert.Contains("dark mode", results[0].Memory.Text);
    }

    [Fact]
    public async Task FactualSearchExcludesAssociativeMemoriesUnlessRequested()
    {
        var service = new MemoryService();
        await service.AddAsync("Alice prefers dark mode", new MemoryAddOptions { UserId = "alice", Infer = false });
        var associative = await service.AddAsync("Alice may enjoy nocturnal themes", new MemoryAddOptions
        {
            UserId = "alice",
            Infer = false,
            Behavior = MemoryBehavior.Dreaming,
            MemoryType = "association"
        });

        var factual = await service.SearchAsync("nocturnal themes", new MemorySearchOptions
        {
            Filter = new MemoryFilter(UserId: "alice"),
            Threshold = 0
        });
        Assert.DoesNotContain(factual, result => result.Memory.Id == Assert.Single(associative.Memories).Id);

        var all = await service.SearchAsync("nocturnal themes", new MemorySearchOptions
        {
            Filter = new MemoryFilter(UserId: "alice"),
            Threshold = 0,
            IncludeNonFactual = true
        });
        var result = Assert.Single(all, item => item.Memory.Id == Assert.Single(associative.Memories).Id);
        Assert.Equal(MemoryBehavior.Dreaming, result.Memory.Behavior);
        Assert.Equal("association", result.Memory.MemoryType);
    }

    [Fact]
    public async Task EnrichmentExtractionFailureDoesNotPersistMemory()
    {
        var service = new MemoryService(entityExtractor: new ThrowingEntityExtractor());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync("must not be persisted"));

        Assert.Empty(await service.GetAllAsync(new MemoryFilter(IncludeExpired: true)));
    }

    [Fact]
    public async Task FiltersDoNotLeakBetweenUsers()
    {
        var service = new MemoryService();
        await service.AddAsync("Alice likes tea", "alice");
        await service.AddAsync("Bob likes coffee", "bob");

        var searches = await service.SearchManyAsync(["tea", "coffee"], new MemoryFilter(UserId: "alice"));
        Assert.Equal(2, searches.Count);
        Assert.NotEmpty(searches[0]);
        Assert.Empty(searches[1]);

        var memories = await service.GetAllAsync(new MemoryFilter(UserId: "alice"));

        var memory = Assert.Single(memories);
        Assert.Equal("alice", memory.UserId);

        Assert.Equal(1, await service.DeleteAllAsync(new MemoryFilter(UserId: "alice")));
        Assert.Single(await service.GetAllAsync());
    }

    [Fact]
    public async Task UpdateAndDeleteChangeTheStore()
    {
        var service = new MemoryService();
        var added = await service.AddAsync("old preference", "alice");
        var id = added.Memories[0].Id;

        var updated = await service.UpdateAsync(id, "new preference");
        Assert.Equal("new preference", updated.Text);
        Assert.NotEqual(added.Memories[0].Hash, updated.Hash);

        await service.DeleteAsync(id);
        Assert.Null(await service.GetAsync(id));
    }

    [Fact]
    public async Task HistoryPersistsAcrossServiceInstances()
    {
        var store = new InMemoryStore();
        var service = new MemoryService(store);
        var added = await service.AddAsync("old preference", new MemoryAddOptions
        {
            UserId = "alice",
            Metadata = new Dictionary<string, string> { ["actor_id"] = "assistant", ["role"] = "writer" }
        });
        var id = added.Memories[0].Id;

        await service.UpdateAsync(id, "new preference");
        await service.DeleteAsync(id);

        var history = await new MemoryService(store).GetHistoryAsync(id);

        Assert.Collection(
            history,
            entry =>
            {
                Assert.Equal(MemoryHistoryEvent.Add, entry.Event);
                Assert.Null(entry.OldMemory);
                Assert.Equal("old preference", entry.NewMemory);
                Assert.Equal(added.Memories[0].CreatedAt, entry.CreatedAt);
                Assert.Equal(added.Memories[0].UpdatedAt, entry.UpdatedAt);
                Assert.False(entry.IsDeleted);
                Assert.Equal("assistant", entry.ActorId);
                Assert.Equal("writer", entry.Role);
                Assert.Equal(added.Memories[0], entry.Snapshot);
            },
            entry =>
            {
                Assert.Equal(MemoryHistoryEvent.Update, entry.Event);
                Assert.Equal("old preference", entry.OldMemory);
                Assert.Equal("new preference", entry.NewMemory);
                Assert.Equal(added.Memories[0].CreatedAt, entry.CreatedAt);
                Assert.True(entry.UpdatedAt >= entry.CreatedAt);
                Assert.False(entry.IsDeleted);
                Assert.Equal("new preference", entry.Snapshot?.Text);
            },
            entry =>
            {
                Assert.Equal(MemoryHistoryEvent.Delete, entry.Event);
                Assert.Equal("new preference", entry.OldMemory);
                Assert.Null(entry.NewMemory);
                Assert.Equal(added.Memories[0].CreatedAt, entry.CreatedAt);
                Assert.True(entry.UpdatedAt >= entry.CreatedAt);
                Assert.True(entry.IsDeleted);
                Assert.Equal("new preference", entry.Snapshot?.Text);
            });
    }

    [Fact]
    public async Task DeleteAllRecordsHistoryForEachMemory()
    {
        var store = new InMemoryStore();
        var service = new MemoryService(store);
        var first = await service.AddAsync("first", "alice");
        var second = await service.AddAsync("second", "alice");

        Assert.Equal(2, await service.DeleteAllAsync(new MemoryFilter(UserId: "alice")));

        Assert.Equal(MemoryHistoryEvent.Delete, (await service.GetHistoryAsync(first.Memories[0].Id))[^1].Event);
        Assert.Equal(MemoryHistoryEvent.Delete, (await service.GetHistoryAsync(second.Memories[0].Id))[^1].Event);
    }

    [Fact]
    public async Task AdvancedMetadataFiltersSupportNestedLogicAndNumericComparison()
    {
        var service = new MemoryService();
        await service.AddAsync("premium tea", new MemoryAddOptions { UserId = "alice", Metadata = new Dictionary<string, string> { ["tier"] = "premium", ["score"] = "12" } });
        await service.AddAsync("basic coffee", new MemoryAddOptions { UserId = "alice", Metadata = new Dictionary<string, string> { ["tier"] = "basic", ["score"] = "4" } });

        var filter = new MemoryFilter(
            UserId: "alice",
            Metadata: new FilterGroup(
                FilterLogic.And,
                new MetadataFilter("score", FilterOperator.GreaterThan, 10),
                new FilterGroup(FilterLogic.Not, new MetadataFilter("tier", FilterOperator.Equal, "basic"))));

        var memory = Assert.Single(await service.GetAllAsync(filter));
        Assert.Equal("premium tea", memory.Text);
    }

    [Fact]
    public async Task ExpiredMemoriesAreHiddenUnlessRequested()
    {
        var service = new MemoryService();
        await service.AddAsync("expired", new MemoryAddOptions { UserId = "alice", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) });
        await service.AddAsync("active", new MemoryAddOptions { UserId = "alice", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1) });

        Assert.Equal("active", Assert.Single(await service.GetAllAsync(new MemoryFilter(UserId: "alice"))).Text);
        Assert.Equal(2, (await service.GetAllAsync(new MemoryFilter(UserId: "alice", IncludeExpired: true))).Count);
        Assert.Empty(await service.SearchAsync("expired", new MemoryFilter(UserId: "alice")));
    }

    [Fact]
    public async Task RawMessageAddBypassesExtractorAndPagingReturnsTotal()
    {
        var service = new MemoryService(extractor: new ThrowingExtractor());
        await service.AddAsync(new[] { new Message("user", "first") }, new MemoryAddOptions { UserId = "alice", Infer = false });
        await service.AddAsync(new[] { new Message("user", "second") }, new MemoryAddOptions { UserId = "alice", Infer = false });

        var page = await service.GetPageAsync(new MemoryPageOptions { Offset = 1, Limit = 1 }, new MemoryFilter(UserId: "alice"));

        Assert.Equal(2, page.Total);
        Assert.Single(page.Results);
    }

    [Fact]
    public async Task SearchThresholdAndExplanationAreApplied()
    {
        var service = new MemoryService();
        await service.AddAsync("dark mode", "alice");

        Assert.Empty(await service.SearchAsync("dark mode", new MemorySearchOptions { Filter = new MemoryFilter(UserId: "alice"), Threshold = 1.1 }));
        var result = Assert.Single(await service.SearchAsync("dark mode", new MemorySearchOptions { Filter = new MemoryFilter(UserId: "alice"), Threshold = 0, Explain = true }));
        Assert.NotNull(result.ScoreDetails);
        Assert.True(result.ScoreDetails.Semantic > 0);
        Assert.Equal(result.ScoreDetails.Semantic + result.ScoreDetails.Keyword, result.ScoreDetails.Raw, 10);
    }

    [Fact]
    public async Task ResetClearsMemoriesAndHistory()
    {
        var store = new InMemoryStore();
        var service = new MemoryService(store);
        var id = (await service.AddAsync("remember me")).Memories[0].Id;

        await service.ResetAsync();

        Assert.Empty(await service.GetAllAsync(new MemoryFilter(IncludeExpired: true)));
        Assert.Empty(await service.GetHistoryAsync(id));
    }

    [Fact]
    public async Task HybridSearchIncludesKeywordScoreAndExplanation()
    {
        var service = new MemoryService(embeddings: new ConstantEmbeddingGenerator());
        await service.AddAsync("project codename zephyr", "alice");
        await service.AddAsync("unrelated preference", "alice");

        var result = Assert.Single((await service.SearchAsync("zephyr", new MemorySearchOptions
        {
            Filter = new MemoryFilter(UserId: "alice"),
            TopK = 1,
            Threshold = 0,
            Explain = true
        })).Take(1));

        Assert.Contains("zephyr", result.Memory.Text);
        Assert.NotNull(result.ScoreDetails);
        Assert.True(result.ScoreDetails.Keyword > 0);
    }

    [Fact]
    public async Task ConfiguredRerankerRunsAfterHybridScoring()
    {
        var service = new MemoryService(embeddings: new ConstantEmbeddingGenerator(), reranker: new ReverseReranker());
        await service.AddAsync("first", "alice");
        await service.AddAsync("second", "alice");

        var results = await service.SearchAsync("anything", new MemorySearchOptions { Filter = new MemoryFilter(UserId: "alice"), TopK = 2, Threshold = 0, Rerank = true });

        Assert.Equal("first", results[0].Memory.Text);
        Assert.NotNull(results[0].ScoreDetails?.Reranker);
    }

    [Fact]
    public async Task DuplicateFactsAreSuppressedWithinTheSameScope()
    {
        var service = new MemoryService();

        var first = await service.AddAsync("Alice likes tea", "alice");
        var duplicate = await service.AddAsync("Alice likes tea", "alice");
        var otherUser = await service.AddAsync("Alice likes tea", "bob");

        Assert.Single(first.Memories);
        Assert.Empty(duplicate.Memories);
        Assert.Equal(MemoryAction.None, Assert.Single(duplicate.Actions!).Event);
        Assert.Single(otherUser.Memories);
    }

    [Fact]
    public async Task ConflictDecisionsUpdateAndDeleteExistingMemories()
    {
        var resolver = new QueueConflictResolver();
        var service = new MemoryService(conflictResolver: resolver);
        var id = (await service.AddAsync("old city", "alice")).Memories[0].Id;
        resolver.Decisions = [new MemoryDecision("new city", MemoryAction.Update, id)];

        var update = await service.AddAsync(new[] { new Message("user", "I moved") }, new MemoryAddOptions { UserId = "alice" });
        Assert.Equal(MemoryAction.Update, Assert.Single(update.Actions!).Event);
        Assert.Equal("new city", (await service.GetAsync(id))!.Text);

        resolver.Decisions = [new MemoryDecision(string.Empty, MemoryAction.Delete, id)];
        var delete = await service.AddAsync(new[] { new Message("user", "Forget my city") }, new MemoryAddOptions { UserId = "alice" });
        Assert.Equal(MemoryAction.Delete, Assert.Single(delete.Actions!).Event);
        Assert.Null(await service.GetAsync(id));
    }

    [Fact]
    public async Task ProceduralMemoryRequiresAgentAndUsesGenerator()
    {
        var service = new MemoryService(proceduralMemoryGenerator: new StubProcedureGenerator());

        var result = await service.AddAsync(new[] { new Message("assistant", "Use the deploy tool") }, new MemoryAddOptions
        {
            AgentId = "deploy-agent",
            MemoryType = "procedural_memory"
        });

        var memory = Assert.Single(result.Memories);
        Assert.Equal(MemoryScope.Agent, memory.Scope);
        Assert.Equal("1. Validate. 2. Deploy.", memory.Text);
    }

    [Fact]
    public async Task EntitiesAreLinkedBoostedAndCleanedUp()
    {
        var entities = new InMemoryEntityStore();
        var service = new MemoryService(embeddings: new ConstantEmbeddingGenerator(), entityStore: entities);
        var id = (await service.AddAsync("Alice lives in Berlin", "alice")).Memories[0].Id;
        await service.AddAsync("Bob likes coffee", "alice");

        var result = Assert.Single((await service.SearchAsync("Alice", new MemorySearchOptions
        {
            Filter = new MemoryFilter(UserId: "alice"),
            TopK = 1,
            Threshold = 0,
            Explain = true
        })).Take(1));

        Assert.Equal(id, result.Memory.Id);
        Assert.True(result.ScoreDetails!.Entity > 0);
        Assert.Contains(await entities.GetAllAsync(), entity => entity.Text == "Alice" && entity.LinkedMemoryIds.Contains(id));

        await service.DeleteAsync(id);
        Assert.DoesNotContain(await entities.GetAllAsync(), entity => entity.LinkedMemoryIds.Contains(id));
    }

    [Fact]
    public async Task GraphRelationsAreStoredBoostedAndCleanedUp()
    {
        var graph = new InMemoryGraphStore();
        var service = new MemoryService(embeddings: new ConstantEmbeddingGenerator(), graphExtractor: new StubGraphExtractor(), graphStore: graph);
        var id = (await service.AddAsync("Alice lives in Berlin", "alice")).Memories[0].Id;
        await service.AddAsync("Bob likes coffee", "alice");

        var relation = Assert.Single(await service.GetRelationsAsync());
        Assert.Equal(id, relation.MemoryId);
        var result = Assert.Single((await service.SearchAsync("Where does Alice live?", new MemorySearchOptions
        {
            Filter = new MemoryFilter(UserId: "alice"), TopK = 1, Threshold = 0, Explain = true
        })).Take(1));
        Assert.Equal(id, result.Memory.Id);
        Assert.True(result.ScoreDetails!.Entity > 0);

        await service.DeleteAsync(id);
        Assert.Empty(await service.GetRelationsAsync());
    }

    [Fact]
    public async Task BulkDeleteCleansEntityAndGraphLinks()
    {
        var entities = new InMemoryEntityStore();
        var graph = new InMemoryGraphStore();
        var service = new MemoryService(entityStore: entities, graphExtractor: new StubGraphExtractor(), graphStore: graph);
        await service.AddAsync("Alice lives in Berlin", "alice");

        Assert.Equal(1, await service.DeleteAllAsync(new MemoryFilter(UserId: "alice")));

        Assert.Empty(await entities.GetAllAsync());
        Assert.Empty(await graph.GetRelationsAsync());
    }

    [Fact]
    public async Task AddManyUsesOneBatchEmbeddingCall()
    {
        var embeddings = new CountingBatchEmbeddingGenerator();
        var service = new MemoryService(embeddings: embeddings);

        var result = await service.AddManyAsync(["first", "second", "third"], new MemoryAddOptions { UserId = "alice" });

        Assert.Equal(3, result.Memories.Count);
        Assert.Equal(1, embeddings.BatchCalls);
        Assert.Equal(0, embeddings.SingleCalls);
    }

    [Fact]
    public async Task SearchManyUsesOneBatchEmbeddingAndVectorStoreCall()
    {
        var embeddings = new CountingBatchEmbeddingGenerator();
        var vectorStore = new CountingBatchVectorStore();
        var service = new MemoryService(store: vectorStore, embeddings: embeddings);

        var results = await service.SearchManyAsync(["first", "second"]);

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.Single(result));
        Assert.Equal(1, embeddings.BatchCalls);
        Assert.Equal(0, embeddings.SingleCalls);
        Assert.Equal(1, vectorStore.BatchSearchCalls);
        Assert.Equal(0, vectorStore.SingleSearchCalls);
    }

    [Fact]
    public async Task ConfigurationCanAddTelemetryWithoutCapturingContentOrIds()
    {
        var telemetry = new InMemoryTelemetryCollector();
        var service = new MemoryServiceConfiguration { Telemetry = telemetry }.CreateService();

        await service.AddAsync("private fact", "private-user-id");
        await service.SearchAsync("private query");

        Assert.Equal(["magicore.add", "magicore.search"], telemetry.Events.Select(item => item.Name));
        var serializedProperties = string.Join(' ', telemetry.Events.SelectMany(item => item.Properties).Select(item => $"{item.Key}:{item.Value}"));
        Assert.DoesNotContain("private fact", serializedProperties);
        Assert.DoesNotContain("private-user-id", serializedProperties);
        Assert.DoesNotContain("private query", serializedProperties);
    }

    [Fact]
    public async Task ConsolidateAsyncCreatesABehavioralSummaryFromRecentMemories()
    {
        var service = new MemoryService();
        await service.AddAsync("Alice prefers dark mode and Vim in the editor.", "alice");
        await service.AddAsync("Alice likes a terminal with a dark theme and keyboard shortcuts.", "alice");
        await service.AddAsync("Alice often works late into the evening.", "alice");

        var created = await service.ConsolidateAsync(new MemoryFilter(UserId: "alice"), maxItems: 3);

        var summary = Assert.Single(created);
        Assert.Equal("consolidated_memory", summary.MemoryType);
        Assert.Contains("dark mode", summary.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Vim", summary.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("3", summary.Metadata["summary_source_count"]);
    }

    [Fact]
    public async Task SearchUsesRecencyBiasToPreferFreshMemories()
    {
        var store = new InMemoryStore();
        var service = new MemoryService(store, embeddings: new ConstantEmbeddingGenerator());
        var oldMemory = new Memory
        {
            Id = "old-memory",
            Text = "Alice prefers the old editor layout.",
            UserId = "alice",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            Hash = "old-hash",
            Metadata = new Dictionary<string, string>()
        };
        var newMemory = new Memory
        {
            Id = "new-memory",
            Text = "Alice prefers dark mode and Vim.",
            UserId = "alice",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            Hash = "new-hash",
            Metadata = new Dictionary<string, string>()
        };

        await store.SaveAsync(oldMemory);
        await store.SaveAsync(newMemory);

        var results = await service.SearchAsync("editor preferences", new MemorySearchOptions
        {
            Filter = new MemoryFilter(UserId: "alice"),
            TopK = 2,
            Threshold = 0,
            RecencyBias = 0.9,
            FreshnessWindow = TimeSpan.FromDays(40)
        });

        Assert.Equal("new-memory", results[0].Memory.Id);
    }

    [Fact]
    public async Task SearchFiltersMemoriesByExplicitReferenceTimeRange()
    {
        var service = new MemoryService(embeddings: new ConstantEmbeddingGenerator());
        var oldMemory = Assert.Single((await service.AddAsync("The launch was planned for spring.", new MemoryAddOptions
        {
            UserId = "alice",
            ReferenceTime = new DateTimeOffset(2025, 3, 15, 0, 0, 0, TimeSpan.Zero)
        })).Memories);
        var currentMemory = Assert.Single((await service.AddAsync("The launch moved to autumn.", new MemoryAddOptions
        {
            UserId = "alice",
            ReferenceTime = new DateTimeOffset(2025, 9, 20, 0, 0, 0, TimeSpan.Zero)
        })).Memories);

        var results = await service.SearchAsync("launch plan", new MemorySearchOptions
        {
            Filter = new MemoryFilter(UserId: "alice"),
            TopK = 10,
            Threshold = 0,
            Hybrid = false,
            TimeRange = new MemoryTimeRange(
                new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2025, 12, 31, 23, 59, 59, TimeSpan.Zero)),
            IncludeUndatedMemories = false
        });

        Assert.Equal("2025-03-15T00:00:00.0000000+00:00", oldMemory.Metadata[TemporalMemoryMetadata.ReferenceTimeKey]);
        Assert.DoesNotContain(results, result => result.Memory.Id == oldMemory.Id);
        Assert.Equal(currentMemory.Id, Assert.Single(results).Memory.Id);
    }

    [Fact]
    public async Task TemporalSearchInterpretsExplicitYear()
    {
        var service = new MemoryService(embeddings: new ConstantEmbeddingGenerator());
        await service.AddAsync("Alice worked in Berlin.", new MemoryAddOptions
        {
            UserId = "alice",
            ReferenceTime = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero)
        });
        var expected = Assert.Single((await service.AddAsync("Alice moved to Lisbon.", new MemoryAddOptions
        {
            UserId = "alice",
            ReferenceTime = new DateTimeOffset(2025, 4, 10, 0, 0, 0, TimeSpan.Zero)
        })).Memories);

        var results = await service.SearchAsync("Where did Alice live in 2025?", new MemorySearchOptions
        {
            Filter = new MemoryFilter(UserId: "alice"),
            TopK = 10,
            Threshold = 0,
            Hybrid = false,
            EnableTemporalSearch = true,
            IncludeUndatedMemories = false
        });

        Assert.Equal(expected.Id, Assert.Single(results).Memory.Id);
    }

    [Fact]
    public async Task TemporalSearchFailsOpenWhenInterpretationConfidenceIsLow()
    {
        var interpreter = new StubTemporalQueryInterpreter(new TemporalQueryInterpretation(
            new MemoryTimeRange(
                new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2025, 12, 31, 23, 59, 59, TimeSpan.Zero)),
            0.4));
        var service = new MemoryService(interpreter, embeddings: new ConstantEmbeddingGenerator());
        await service.AddAsync("Alice worked in Berlin.", new MemoryAddOptions
        {
            UserId = "alice",
            ReferenceTime = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero)
        });
        await service.AddAsync("Alice moved to Lisbon.", new MemoryAddOptions
        {
            UserId = "alice",
            ReferenceTime = new DateTimeOffset(2025, 4, 10, 0, 0, 0, TimeSpan.Zero)
        });

        var results = await service.SearchAsync("Where did Alice live?", new MemorySearchOptions
        {
            Filter = new MemoryFilter(UserId: "alice"),
            TopK = 10,
            Threshold = 0,
            Hybrid = false,
            EnableTemporalSearch = true,
            MinimumTemporalConfidence = 0.8,
            IncludeUndatedMemories = false
        });

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task UpdatePreservesReferenceTimeUnlessExplicitlyChanged()
    {
        var service = new MemoryService();
        var originalTime = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var updatedTime = new DateTimeOffset(2025, 4, 10, 0, 0, 0, TimeSpan.Zero);
        var memory = Assert.Single((await service.AddAsync("Alice worked in Berlin.", new MemoryAddOptions
        {
            UserId = "alice",
            ReferenceTime = originalTime
        })).Memories);

        var metadataUpdate = await service.UpdateAsync(memory.Id, new MemoryUpdate
        {
            Metadata = new Dictionary<string, string> { ["source"] = "profile" }
        });
        var timeUpdate = await service.UpdateAsync(memory.Id, new MemoryUpdate
        {
            UpdateReferenceTime = true,
            ReferenceTime = updatedTime
        });

        Assert.Equal(originalTime.ToString("O"), metadataUpdate.Metadata[TemporalMemoryMetadata.ReferenceTimeKey]);
        Assert.Equal(updatedTime.ToString("O"), timeUpdate.Metadata[TemporalMemoryMetadata.ReferenceTimeKey]);
    }

    [Fact]
    public async Task DeduplicationKeepsIdenticalEventsAtDifferentReferenceTimes()
    {
        var service = new MemoryService();
        var firstOptions = new MemoryAddOptions
        {
            UserId = "alice",
            ReferenceTime = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };
        var secondOptions = firstOptions with
        {
            ReferenceTime = new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero)
        };

        var first = await service.AddAsync("Alice completed the daily check-in.", firstOptions);
        var duplicate = await service.AddAsync("Alice completed the daily check-in.", firstOptions);
        var second = await service.AddAsync("Alice completed the daily check-in.", secondOptions);

        Assert.Single(first.Memories);
        Assert.Empty(duplicate.Memories);
        Assert.Single(second.Memories);
        Assert.Equal(2, (await service.GetAllAsync(new MemoryFilter(UserId: "alice"))).Count);
    }

    [Fact]
    public async Task ForgetStaleAsyncRemovesMemoriesPastRetentionWindow()
    {
        var store = new InMemoryStore();
        var service = new MemoryService(store);
        var oldId = (await service.AddAsync("stale preference", new MemoryAddOptions { UserId = "alice" })).Memories[0].Id;
        var freshId = (await service.AddAsync("fresh preference", new MemoryAddOptions { UserId = "alice" })).Memories[0].Id;

        var oldMemory = await service.GetAsync(oldId);
        await store.SaveAsync(oldMemory! with { UpdatedAt = DateTimeOffset.UtcNow.AddDays(-30), CreatedAt = DateTimeOffset.UtcNow.AddDays(-30) });

        var removed = await service.ForgetStaleAsync(TimeSpan.FromDays(7), new MemoryFilter(UserId: "alice"));

        Assert.Equal(1, removed);
        Assert.Null(await service.GetAsync(oldId));
        Assert.NotNull(await service.GetAsync(freshId));
    }

    [Fact]
    public void SynchronousFacadeWrapsTheAsyncServiceSurface()
    {
        var memory = new SynchronousMemoryService(new MemoryService(graphExtractor: new StubGraphExtractor(), graphStore: new InMemoryGraphStore()));

        var id = memory.Add("Alice lives in Berlin").Memories[0].Id;
        memory.Add("sync fact");

        Assert.Equal(2, memory.SearchMany(["Alice", "sync"]).Count);
        var page = memory.GetPage(new MemoryPageOptions { Offset = 1, Limit = 1 });
        Assert.Equal(2, page.Total);
        Assert.Single(page.Results);
        Assert.Equal(id, Assert.Single(memory.GetRelations()).MemoryId);

        memory.Delete(id);
        Assert.Null(memory.Get(id));
    }

    private sealed class ThrowingExtractor : IMemoryExtractor
    {
        public Task<IReadOnlyList<MemoryInput>> ExtractAsync(IReadOnlyList<Message> messages, MemoryAddOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Raw adds must not invoke the extractor.");
    }

    private sealed class ThrowingEntityExtractor : IEntityExtractor
    {
        public Task<IReadOnlyList<ExtractedEntity>> ExtractAsync(string text, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Entity extraction failed.");
    }

    private sealed class ConstantEmbeddingGenerator : IEmbeddingGenerator
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            var result = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var _ in values) result.Add(new Embedding<float>(new float[] { 1, 0 }));
            return Task.FromResult(result);
        }
        public Task<IReadOnlyList<float>> GenerateVectorAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<float>>([1, 0]);
        public Task<IReadOnlyList<IReadOnlyList<float>>> GenerateVectorBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IReadOnlyList<float>>>(texts.Select(_ => (IReadOnlyList<float>)[1, 0]).ToArray());
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class ReverseReranker : IMemoryReranker
    {
        public Task<IReadOnlyList<SearchResult>> RerankAsync(string query, IReadOnlyList<SearchResult> candidates, int topK, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SearchResult> results = candidates.Reverse().Select((result, index) => result with
            {
                Score = 1 - index * 0.1,
                ScoreDetails = (result.ScoreDetails ?? new SearchScoreDetails(result.Score)) with { Reranker = 1 - index * 0.1 }
            }).Take(topK).ToArray();
            return Task.FromResult(results);
        }
    }

    private sealed class QueueConflictResolver : IMemoryConflictResolver
    {
        public IReadOnlyList<MemoryDecision> Decisions { get; set; } = [];

        public Task<IReadOnlyList<MemoryDecision>> ResolveAsync(IReadOnlyList<Message> messages, IReadOnlyList<Memory> existingMemories, MemoryAddOptions options, CancellationToken cancellationToken = default) => Task.FromResult(Decisions);
    }

    private sealed class StubProcedureGenerator : IProceduralMemoryGenerator
    {
        public Task<string> GenerateAsync(IReadOnlyList<Message> messages, string? prompt = null, CancellationToken cancellationToken = default) => Task.FromResult("1. Validate. 2. Deploy.");
    }

    private sealed class StubTemporalQueryInterpreter(TemporalQueryInterpretation? interpretation) : ITemporalQueryInterpreter
    {
        public Task<TemporalQueryInterpretation?> InterpretAsync(string query, DateTimeOffset referenceTime, CancellationToken cancellationToken = default) =>
            Task.FromResult(interpretation);
    }

    private sealed class StubGraphExtractor : IGraphMemoryExtractor
    {
        public Task<IReadOnlyList<ExtractedRelation>> ExtractAsync(string text, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ExtractedRelation> relations = text.Contains("Alice", StringComparison.Ordinal)
                ? [new ExtractedRelation("Alice", "lives in", "Berlin")]
                : [];
            return Task.FromResult(relations);
        }
    }

    private sealed class CountingBatchEmbeddingGenerator : IEmbeddingGenerator
    {
        public int BatchCalls { get; private set; }
        public int SingleCalls { get; private set; }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            var list = values.ToArray();
            if (list.Length > 1) BatchCalls++;
            else SingleCalls++;
            var result = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var _ in list) result.Add(new Embedding<float>(new float[] { 1, 0 }));
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<float>> GenerateVectorAsync(string text, CancellationToken cancellationToken = default)
        {
            SingleCalls++;
            return Task.FromResult<IReadOnlyList<float>>([1, 0]);
        }

        public Task<IReadOnlyList<IReadOnlyList<float>>> GenerateVectorBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
        {
            BatchCalls++;
            return Task.FromResult<IReadOnlyList<IReadOnlyList<float>>>(texts.Select(_ => (IReadOnlyList<float>)[1, 0]).ToArray());
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class CountingBatchVectorStore : IMemoryStore
    {
        private readonly Memory result = new() { Id = "result", Text = "batch result", UserId = "default_user" };

        public int BatchSearchCalls { get; private set; }
        public int SingleSearchCalls { get; private set; }

        public Task SaveAsync(Memory memory, IReadOnlyList<float>? embedding = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveBatchAsync(IReadOnlyList<MemoryWriteRecord> records, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Memory?> GetAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<Memory?>(null);
        public async IAsyncEnumerable<Memory> GetAllAsync(MemoryFilter? filter = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }
        public Task DeleteAsync(string id, MemoryHistoryEntry? history = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> DeleteAllAsync(MemoryFilter? filter = null, IReadOnlyList<MemoryDeleteRecord>? records = null, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task SaveHistoryAsync(MemoryHistoryEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<MemoryHistoryEntry>> GetHistoryAsync(string memoryId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MemoryHistoryEntry>>([]);
        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<SearchResult>> SearchAsync(IReadOnlyList<float> embedding, MemoryFilter? filter = null, int topK = 5, CancellationToken cancellationToken = default)
        {
            SingleSearchCalls++;
            return Task.FromResult<IReadOnlyList<SearchResult>>([new SearchResult(result, 1)]);
        }

        public Task<IReadOnlyList<IReadOnlyList<SearchResult>>> SearchBatchAsync(IReadOnlyList<IReadOnlyList<float>> embeddings, MemoryFilter? filter = null, int topK = 5, CancellationToken cancellationToken = default)
        {
            BatchSearchCalls++;
            IReadOnlyList<IReadOnlyList<SearchResult>> results = embeddings.Select(_ => (IReadOnlyList<SearchResult>)[new SearchResult(result, 1)]).ToArray();
            return Task.FromResult(results);
        }
    }
}
