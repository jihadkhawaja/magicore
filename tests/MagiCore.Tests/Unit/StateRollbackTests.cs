using MagiCore;
using Xunit;

namespace MagiCore.Tests;

public sealed class StateRollbackTests
{
    [Fact]
    public async Task InMemoryStoreReconstructsFilteredStateWithoutMutatingCurrentMemories()
    {
        var store = new InMemoryStore();
        var service = new MemoryService(store);
        var alice = (await service.AddAsync("Alice likes tea", new MemoryAddOptions
        {
            UserId = "alice",
            Metadata = new Dictionary<string, string> { ["subject"] = "beverages" }
        })).Memories[0];
        var pointInTime = DateTimeOffset.UtcNow;
        await Task.Delay(10);

        await service.UpdateAsync(alice.Id, "Alice likes coffee");
        await service.AddAsync("Bob likes tea", new MemoryAddOptions
        {
            UserId = "bob",
            Metadata = new Dictionary<string, string> { ["subject"] = "beverages" }
        });

        var historical = await store.GetAllAtAsync(
            pointInTime,
            new MemoryFilter(
                UserId: "alice",
                Metadata: new MetadataFilter("subject", FilterOperator.Equal, "beverages")));

        Assert.Equal("Alice likes tea", Assert.Single(historical).Text);
        Assert.Equal("Alice likes coffee", (await service.GetAsync(alice.Id))?.Text);
    }

    [Fact]
    public async Task SearchAtAsyncSearchesGlobalOrUserAndSubjectScopedHistoricalState()
    {
        var service = new MemoryService();
        var alice = (await service.AddAsync("Alice likes tea", new MemoryAddOptions
        {
            UserId = "alice",
            Metadata = new Dictionary<string, string> { ["subject"] = "beverages" }
        })).Memories[0];
        var bob = (await service.AddAsync("Bob likes tea", new MemoryAddOptions
        {
            UserId = "bob",
            Metadata = new Dictionary<string, string> { ["subject"] = "beverages" }
        })).Memories[0];
        await service.AddAsync("Alice visits Rome", new MemoryAddOptions
        {
            UserId = "alice",
            Metadata = new Dictionary<string, string> { ["subject"] = "travel" }
        });
        var pointInTime = DateTimeOffset.UtcNow;
        await Task.Delay(10);

        await service.UpdateAsync(alice.Id, "Alice likes coffee");
        await service.DeleteAsync(bob.Id);

        var global = await service.SearchAtAsync("tea", pointInTime, new MemorySearchOptions { TopK = 10, Threshold = 0 });
        var scoped = await service.SearchAtAsync("tea", pointInTime, new MemorySearchOptions
        {
            Filter = new MemoryFilter(
                UserId: "alice",
                Metadata: new MetadataFilter("subject", FilterOperator.Equal, "beverages")),
            TopK = 10,
            Threshold = 0
        });

        Assert.Equal(3, global.Count);
        Assert.Equal("Alice likes tea", Assert.Single(scoped).Memory.Text);
        Assert.Equal("Alice likes coffee", (await service.GetAsync(alice.Id))?.Text);
        Assert.Null(await service.GetAsync(bob.Id));
    }

    [Fact]
    public async Task InMemoryStoreRollbackRestoresStateAtTimestamp()
    {
        var service = new MemoryService();

        var t0 = DateTimeOffset.UtcNow;
        await Task.Delay(10);

        var add1 = await service.AddAsync("Alice loves pizza", "alice");
        var id1 = add1.Memories[0].Id;

        await Task.Delay(20);
        var t1 = DateTimeOffset.UtcNow;
        await Task.Delay(20);

        await service.UpdateAsync(id1, "Alice loves sushi");

        var add2 = await service.AddAsync("Alice drives a Tesla", "alice");
        var id2 = add2.Memories[0].Id;

        await Task.Delay(20);
        var t2 = DateTimeOffset.UtcNow;

        // Current state
        var currentMemories = await service.GetAllAsync(new MemoryFilter(UserId: "alice"));
        Assert.Equal(2, currentMemories.Count);
        Assert.Equal("Alice loves sushi", currentMemories.First(m => m.Id == id1).Text);

        // Roll back to t1 (before the update and before id2 was added)
        var rollbackResult = await service.RollbackAsync(t1);

        var rolledBackMemories = await service.GetAllAsync(new MemoryFilter(UserId: "alice"));
        Assert.Single(rolledBackMemories);
        Assert.Equal("Alice loves pizza", rolledBackMemories[0].Text);
        Assert.Equal(id1, rolledBackMemories[0].Id);
        Assert.True(rollbackResult.RestoredCount > 0 || rollbackResult.DeletedCount > 0);
    }

    [Fact]
    public async Task InMemoryStoreRollbackOnlyChangesTheRequestedUserAndSubject()
    {
        var service = new MemoryService();
        var alice = (await service.AddAsync("Alice likes tea", new MemoryAddOptions
        {
            UserId = "alice",
            Metadata = new Dictionary<string, string> { ["subject"] = "beverages" }
        })).Memories[0];
        var bob = (await service.AddAsync("Bob likes tea", new MemoryAddOptions
        {
            UserId = "bob",
            Metadata = new Dictionary<string, string> { ["subject"] = "beverages" }
        })).Memories[0];
        var pointInTime = DateTimeOffset.UtcNow;
        await Task.Delay(10);

        await service.UpdateAsync(alice.Id, "Alice likes coffee");
        await service.UpdateAsync(bob.Id, "Bob likes coffee");
        var laterAlice = (await service.AddAsync("Alice likes juice", new MemoryAddOptions
        {
            UserId = "alice",
            Metadata = new Dictionary<string, string> { ["subject"] = "beverages" }
        })).Memories[0];

        var result = await service.RollbackAsync(
            pointInTime,
            new MemoryFilter(
                UserId: "alice",
                Metadata: new MetadataFilter("subject", FilterOperator.Equal, "beverages")));

        Assert.Equal("Alice likes tea", (await service.GetAsync(alice.Id))?.Text);
        Assert.Equal("Bob likes coffee", (await service.GetAsync(bob.Id))?.Text);
        Assert.Null(await service.GetAsync(laterAlice.Id));
        Assert.Contains(alice.Id, result.AffectedMemoryIds);
        Assert.Contains(laterAlice.Id, result.AffectedMemoryIds);
        Assert.DoesNotContain(bob.Id, result.AffectedMemoryIds);
    }

    [Fact]
    public async Task VectorDataStoreRollbackRestoresPreviousMemoryState()
    {
        var store = VectorDataMemoryStore.CreateInMemory();
        await store.InitializeAsync();
        var service = new MemoryService(store);

        var add1 = await service.AddAsync("Original fact", "bob");
        var id1 = add1.Memories[0].Id;

        await Task.Delay(50);
        var t1 = DateTimeOffset.UtcNow;
        await Task.Delay(50);

        await service.UpdateAsync(id1, "Poisoned or modified fact");
        await service.AddAsync("Spurious memory", "bob");

        // Verify corrupted state
        var current = await service.GetAllAsync(new MemoryFilter(UserId: "bob"));
        Assert.Equal(2, current.Count);
        Assert.Equal("Poisoned or modified fact", current.First(m => m.Id == id1).Text);

        // Rollback to t1
        var result = await service.RollbackAsync(t1);

        var restored = await service.GetAllAsync(new MemoryFilter(UserId: "bob"));
        Assert.Single(restored);
        Assert.Equal("Original fact", restored[0].Text);
        Assert.Equal(id1, restored[0].Id);
    }

    [Fact]
    public async Task VectorDataStoreSearchesAndRollsBackHistoricalUserSubjectState()
    {
        var store = VectorDataMemoryStore.CreateInMemory();
        await store.InitializeAsync();
        var service = new MemoryService(store);
        var alice = (await service.AddAsync("Alice likes tea", new MemoryAddOptions
        {
            UserId = "alice",
            Metadata = new Dictionary<string, string> { ["subject"] = "beverages" }
        })).Memories[0];
        var bob = (await service.AddAsync("Bob likes tea", new MemoryAddOptions
        {
            UserId = "bob",
            Metadata = new Dictionary<string, string> { ["subject"] = "beverages" }
        })).Memories[0];
        var pointInTime = DateTimeOffset.UtcNow;
        await Task.Delay(10);

        await service.UpdateAsync(alice.Id, "Alice likes coffee");
        await service.UpdateAsync(bob.Id, "Bob likes coffee");

        var filter = new MemoryFilter(
            UserId: "alice",
            Metadata: new MetadataFilter("subject", FilterOperator.Equal, "beverages"));
        var historical = await service.SearchAtAsync("tea", pointInTime, new MemorySearchOptions
        {
            Filter = filter,
            TopK = 5,
            Threshold = 0
        });
        await service.RollbackAsync(pointInTime, filter);

        Assert.Equal("Alice likes tea", Assert.Single(historical).Memory.Text);
        Assert.Equal("Alice likes tea", (await service.GetAsync(alice.Id))?.Text);
        Assert.Equal("beverages", (await service.GetAsync(alice.Id))?.Metadata["subject"]);
        Assert.Equal("Bob likes coffee", (await service.GetAsync(bob.Id))?.Text);
        Assert.Equal("Alice likes tea", Assert.Single(await service.SearchAsync("tea", new MemorySearchOptions
        {
            Filter = filter,
            TopK = 1,
            Threshold = 0
        })).Memory.Text);
    }
}
