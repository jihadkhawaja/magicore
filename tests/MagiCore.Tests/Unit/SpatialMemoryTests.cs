using Xunit;

namespace MagiCore.Tests;

public sealed class SpatialMemoryTests
{
    [Fact]
    public async Task Recall_RoundTripsAndIncludesRadiusBoundaryAcrossServiceInstances()
    {
        var store = new InMemoryStore();
        var memory = new MemoryService(store: store);
        var observation = Observe(new SpatialPoint(0, 3, 4));
        await memory.RememberSpatialAsync(observation, new MemoryAddOptions { UserId = "robot" });
        await memory.RememberSpatialAsync(observation with { Position = new SpatialPoint(0, 0, 6) }, new MemoryAddOptions { UserId = "robot" });
        var restarted = new MemoryService(store: store);
        var results = await restarted.RecallSpatialAsync(Query());
        var result = Assert.Single(results);
        Assert.Equal(5, result.Distance);
        Assert.Equal(observation, result.Observation);
    }

    [Fact]
    public async Task Recall_IsolatesMapsUsersAgentsAndIgnoresExpiredRecords()
    {
        var memory = new MemoryService();
        var observation = Observe(new SpatialPoint(0, 0, 0));
        await memory.RememberSpatialAsync(observation, new MemoryAddOptions { UserId = "other" });
        await memory.RememberSpatialAsync(observation with { MapId = "other" }, new MemoryAddOptions { UserId = "robot" });
        await memory.RememberSpatialAsync(observation, new MemoryAddOptions { UserId = "robot", AgentId = "other" });
        await memory.RememberSpatialAsync(observation, new MemoryAddOptions { UserId = "robot", AgentId = "unit", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) });
        Assert.Empty(await memory.RecallSpatialAsync(Query() with { AgentId = "unit" }));
    }

    [Fact]
    public async Task Recall_FiltersAndOrdersObservationsWithoutTextDeduplication()
    {
        var memory = new MemoryService();
        var older = Observe(new SpatialPoint(1, 0, 0)) with { ObservedAt = DateTimeOffset.UtcNow.AddDays(-1) };
        await memory.RememberSpatialAsync(older, new MemoryAddOptions { UserId = "robot" });
        await memory.RememberSpatialAsync(older with { Position = new SpatialPoint(2, 0, 0), ObservedAt = DateTimeOffset.UtcNow }, new MemoryAddOptions { UserId = "robot" });
        Assert.Equal(2, (await memory.RecallSpatialAsync(Query())).Count);
        Assert.Equal(1, Assert.Single(await memory.RecallSpatialAsync(Query() with { TopK = 1 })).Distance);
        Assert.Equal(2, Assert.Single(await memory.RecallSpatialAsync(Query() with { ObservedAfter = DateTimeOffset.UtcNow.AddHours(-1) })).Distance);
        Assert.Empty(await memory.RecallSpatialAsync(Query() with { MinimumConfidence = 0.99 }));
        Assert.Empty(await memory.RecallSpatialAsync(Query() with { EntityId = "missing" }));
    }

    [Fact]
    public async Task SpatialMemory_RejectsInvalidGeometryAndQueries()
    {
        var memory = new MemoryService();
        await Assert.ThrowsAsync<ArgumentException>(() => memory.RememberSpatialAsync(Observe(new SpatialPoint(double.NaN, 0, 0)), new MemoryAddOptions { UserId = "robot" }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => memory.RecallSpatialAsync(Query() with { Radius = -1 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => memory.RecallSpatialAsync(Query() with { Radius = double.PositiveInfinity }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => memory.RecallSpatialAsync(Query() with { TopK = 0 }));
    }

    private static SpatialObservation Observe(SpatialPoint position) => new()
    {
        MapId = "lab-v1", Position = position, Description = "Red crate", EntityId = "crate",
        ObserverPosition = new SpatialPoint(0, 1, 0), Confidence = 0.9
    };

    private static SpatialRecallOptions Query() => new()
    {
        MapId = "lab-v1", UserId = "robot", Center = new SpatialPoint(0, 0, 0), Radius = 5
    };
}