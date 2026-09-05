using System.Runtime.Versioning;
using MagiCore;
using Xunit;

namespace MagiCore.NetStandard.Tests;

public sealed class NetStandardSmokeTests
{
    private const string NetStandardFrameworkName = ".NETStandard,Version=v2.0";

    [Fact]
    public void LoadsNetStandardAssetsForAllPackages()
    {
        Assert.Equal(NetStandardFrameworkName, FrameworkName(typeof(MemoryService)));
        Assert.Equal(NetStandardFrameworkName, FrameworkName(typeof(VectorDataMemoryStore)));
    }

    [Fact]
    public async Task CoreMemoryFlowRunsFromNetStandardAsset()
    {
        var service = new MemoryService();

        var added = await service.AddAsync("Alice prefers dark mode", "alice");
        var result = Assert.Single(await service.SearchAsync("dark mode", new MemoryFilter(UserId: "alice")));

        Assert.Equal(Assert.Single(added.Memories).Id, result.Memory.Id);
    }

    [Fact]
    public async Task VectorDataFlowRunsFromNetStandardAsset()
    {
        var store = VectorDataMemoryStore.CreateInMemory();
        await store.InitializeAsync();
        var service = new MemoryService(store);

        await service.AddAsync("portable VectorData memory", "alice");

        Assert.Equal("portable VectorData memory", Assert.Single(await service.GetAllAsync()).Text);
    }

    private static string? FrameworkName(Type type) => type.Assembly.GetCustomAttributes(typeof(TargetFrameworkAttribute), false)
        .Cast<TargetFrameworkAttribute>()
        .Single()
        .FrameworkName;
}