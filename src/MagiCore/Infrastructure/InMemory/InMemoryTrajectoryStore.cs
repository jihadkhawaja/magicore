using System.Collections.Concurrent;

namespace MagiCore;

public sealed class InMemoryTrajectoryStore : ITrajectoryStore
{
    private readonly ConcurrentDictionary<string, TrajectoryRecord> trajectories = new(StringComparer.Ordinal);
    private readonly object sync = new();

    public Task AppendTrajectoryAsync(TrajectoryRecord record, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            trajectories[record.Id] = record;
        }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<TrajectoryRecord> GetTrajectoriesAsync(
        MemoryFilter? filter = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var list = trajectories.Values.OrderBy(t => t.CreatedAt).ToArray();
        foreach (var item in list)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (filter is not null)
            {
                if (filter.UserId is not null && !string.Equals(item.UserId, filter.UserId, StringComparison.Ordinal)) continue;
                if (filter.AgentId is not null && !string.Equals(item.AgentId, filter.AgentId, StringComparison.Ordinal)) continue;
                if (filter.RunId is not null && !string.Equals(item.RunId, filter.RunId, StringComparison.Ordinal)) continue;
            }
            yield return item;
        }
    }

    public Task<TrajectoryRecord?> GetTrajectoryAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        trajectories.TryGetValue(id, out var record);
        return Task.FromResult<TrajectoryRecord?>(record);
    }

    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            trajectories.Clear();
        }
        return Task.CompletedTask;
    }
}
