namespace MagiCore;

public sealed record MemoryTelemetryEvent(string Name, DateTimeOffset Timestamp, IReadOnlyDictionary<string, object?> Properties);