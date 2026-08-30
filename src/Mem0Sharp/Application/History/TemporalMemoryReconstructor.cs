namespace Mem0Sharp;

internal static class TemporalMemoryReconstructor
{
    public static IReadOnlyList<Memory> Reconstruct(
        IEnumerable<MemoryHistoryEntry> history,
        DateTimeOffset pointInTime,
        MemoryFilter? filter = null)
    {
        Guard.NotNull(history);

        return history
            .Where(entry => entry.UpdatedAt <= pointInTime)
            .GroupBy(entry => entry.MemoryId, StringComparer.Ordinal)
            .Select(group => group.OrderBy(entry => entry.UpdatedAt).Last())
            .Where(entry => entry.Event != MemoryHistoryEvent.Delete && !entry.IsDeleted && entry.Snapshot is not null)
            .Select(entry => entry.Snapshot!)
            .Where(memory => MemoryFilterEvaluator.Matches(memory, filter, pointInTime))
            .OrderByDescending(memory => memory.UpdatedAt)
            .ToArray();
    }
}