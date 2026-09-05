using System.Collections.Concurrent;

namespace MagiCore;

public sealed class InMemoryTelemetryCollector : IMemoryTelemetry
{
    private readonly ConcurrentQueue<MemoryTelemetryEvent> events = new();

    public IReadOnlyList<MemoryTelemetryEvent> Events => events.ToArray();

    public Task CaptureAsync(MemoryTelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        events.Enqueue(telemetryEvent);
        return Task.CompletedTask;
    }
}