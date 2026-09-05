using DotNet.Testcontainers.Builders;
using MagiCore;
using Xunit;

namespace MagiCore.Tests;

public sealed class QdrantMemoryStoreIntegrationTests
{
    [Fact]
    public async Task QdrantStoreSupportsPersistenceFiltersBatchSearchAndReset()
    {
        await using var container = new ContainerBuilder("qdrant/qdrant:v1.15.4")
            .WithPortBinding(6333, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(6333).ForPath("/readyz")))
            .Build();
        await container.StartAsync();
        var endpoint = new Uri($"http://{container.Hostname}:{container.GetMappedPublicPort(6333)}");
        using var httpClient = new HttpClient();
        var store = new QdrantMemoryStore(httpClient, new QdrantMemoryStoreOptions
        {
            Endpoint = endpoint,
            CollectionName = "magicore_test",
            EmbeddingDimensions = 8
        });
        await store.InitializeAsync();
        var service = new MemoryService(store, new LocalEmbeddingGenerator(8));
        await service.AddAsync("Alice likes premium tea", new MemoryAddOptions
        {
            UserId = "alice",
            Metadata = new Dictionary<string, string> { ["tier"] = "premium", ["score"] = "12" }
        });
        await service.AddAsync("Bob likes coffee", new MemoryAddOptions { UserId = "bob" });

        var filtered = await service.GetAllAsync(new MemoryFilter(
            UserId: "alice",
            Metadata: new FilterGroup(FilterLogic.And,
                new MetadataFilter("tier", FilterOperator.Equal, "premium"),
                new MetadataFilter("score", FilterOperator.GreaterThan, 10))));
        var memory = Assert.Single(filtered);
        var searches = await service.SearchManyAsync(["premium tea", "coffee"], new MemoryFilter(UserId: "alice"));

        Assert.Equal(2, searches.Count);
        Assert.Equal(memory.Id, Assert.Single(searches[0]).Memory.Id);
        Assert.Equal(memory.Id, Assert.Single(searches[1]).Memory.Id);
        Assert.Equal(memory.Id, (await new MemoryService(store, new LocalEmbeddingGenerator(8)).GetAsync(memory.Id))!.Id);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SearchAsync([1, 2]));

        await service.UpdateAsync(memory.Id, "Alice likes green tea");
        Assert.Equal("Alice likes green tea", (await service.GetAsync(memory.Id))!.Text);
        Assert.Equal(1, await service.DeleteAllAsync(new MemoryFilter(UserId: "alice")));
        Assert.Null(await service.GetAsync(memory.Id));

        await service.ResetAsync();
        Assert.Empty(await service.GetAllAsync());
    }
}