using MagiCore;
using Microsoft.Extensions.AI;
using Xunit;

namespace MagiCore.Tests;

public sealed class TrajectoryStoreTests
{
    [Fact]
    public async Task AppendAndRetrieveTrajectoriesPreservesEpisodes()
    {
        var store = new InMemoryTrajectoryStore();
        var trajectory = new TrajectoryRecord
        {
            Id = "traj-1",
            SessionId = "sess-1",
            UserId = "alice",
            Messages =
            [
                new Message("user", "What is the capital of France?"),
                new Message("assistant", "The capital of France is Paris.")
            ]
        };

        await store.AppendTrajectoryAsync(trajectory);

        var retrieved = await store.GetTrajectoryAsync("traj-1");
        Assert.NotNull(retrieved);
        Assert.Equal("sess-1", retrieved.SessionId);
        Assert.Equal(2, retrieved.Messages.Count);

        var all = new List<TrajectoryRecord>();
        await foreach (var item in store.GetTrajectoriesAsync(new MemoryFilter(UserId: "alice")))
        {
            all.Add(item);
        }
        Assert.Single(all);
    }

    [Fact]
    public async Task ExtractOnDemandProcessesEpisodicTrajectories()
    {
        var trajectoryStore = new InMemoryTrajectoryStore();
        var service = new MemoryService(trajectoryStore: trajectoryStore);

        await service.AppendTrajectoryAsync(new TrajectoryRecord
        {
            Id = "t1",
            SessionId = "s1",
            UserId = "alice",
            Messages =
            [
                new Message("user", "My favorite framework is .NET 10."),
                new Message("assistant", "Great choice! .NET 10 is fast.")
            ]
        });

        var extracted = await service.ExtractOnDemandAsync(
            "Extract framework preferences",
            new MemoryFilter(UserId: "alice"));

        Assert.NotEmpty(extracted);
        Assert.Contains(extracted, m => m.Text.Contains(".NET", StringComparison.OrdinalIgnoreCase) || m.Text.Contains("framework", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ChatMessageOverloadsWorkSeamlesslyWithService()
    {
        var service = new MemoryService();
        var chatMessages = new List<ChatMessage>
        {
            new(ChatRole.User, "I live in Seattle."),
            new(ChatRole.Assistant, "Understood.")
        };

        var result = await service.AddAsync(chatMessages, "alice");

        Assert.NotEmpty(result.Memories);
        Assert.Contains("Seattle", result.Memories[0].Text);
    }
}
