namespace MagiCore;

public interface IMemoryTelemetry
{
    Task CaptureAsync(MemoryTelemetryEvent telemetryEvent, CancellationToken cancellationToken = default);
}