namespace MagiCore;

public interface ITrajectoryStore
{
    Task AppendTrajectoryAsync(TrajectoryRecord record, CancellationToken cancellationToken = default);
    IAsyncEnumerable<TrajectoryRecord> GetTrajectoriesAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default);
    Task<TrajectoryRecord?> GetTrajectoryAsync(string id, CancellationToken cancellationToken = default);
    Task ResetAsync(CancellationToken cancellationToken = default);
}
