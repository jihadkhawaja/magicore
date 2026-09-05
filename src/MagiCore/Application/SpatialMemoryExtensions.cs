using System.Text.Json;

namespace MagiCore;

public static class SpatialMemoryExtensions
{
    private const string ObservationKey = "spatial_observation_v1";
    private const string MapKey = "spatial_map_id";

    public static Task<AddResult> RememberSpatialAsync(this IMemoryService memory,
        SpatialObservation observation, MemoryAddOptions options, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(memory);
        Guard.NotNull(observation);
        Guard.NotNull(options);
        Guard.NotNullOrWhiteSpace(options.UserId);
        Validate(observation);
        var metadata = options.Metadata?.ToDictionary(pair => pair.Key, pair => pair.Value)
            ?? new Dictionary<string, string>();
        metadata[ObservationKey] = JsonSerializer.Serialize(observation);
        metadata[MapKey] = observation.MapId;
        return memory.AddAsync(observation.Description, options with
        {
            Metadata = metadata,
            MemoryType = "spatial_memory",
            Infer = false,
            Deduplicate = false,
            ReferenceTime = observation.ObservedAt
        }, cancellationToken);
    }

    public static async Task<IReadOnlyList<SpatialRecallResult>> RecallSpatialAsync(this IMemoryService memory,
        SpatialRecallOptions options, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(memory);
        Guard.NotNull(options);
        Guard.NotNullOrWhiteSpace(options.MapId);
        Guard.NotNullOrWhiteSpace(options.UserId);
        if (options.Center is null || !options.Center.IsFinite) throw new ArgumentException("A finite center is required.", nameof(options));
        if (!SpatialPoint.IsFiniteNumber(options.Radius) || options.Radius < 0) throw new ArgumentOutOfRangeException(nameof(options.Radius));
        if (options.TopK < 1) throw new ArgumentOutOfRangeException(nameof(options.TopK));
        if (!SpatialPoint.IsFiniteNumber(options.MinimumConfidence) || options.MinimumConfidence < 0 || options.MinimumConfidence > 1)
            throw new ArgumentOutOfRangeException(nameof(options.MinimumConfidence));

        var records = await memory.GetAllAsync(new MemoryFilter(UserId: options.UserId, AgentId: options.AgentId), cancellationToken);
        var results = new List<SpatialRecallResult>();
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (record.ExpiresAt <= DateTimeOffset.UtcNow || record.MemoryType != "spatial_memory"
                || !record.Metadata.TryGetValue(MapKey, out var mapId) || mapId != options.MapId
                || !record.Metadata.TryGetValue(ObservationKey, out var json)) continue;
            SpatialObservation? observation;
            try
            {
                observation = JsonSerializer.Deserialize<SpatialObservation>(json);
                if (observation is null) continue;
                Validate(observation);
            }
            catch (JsonException) { continue; }
            catch (ArgumentException) { continue; }
            if (observation.MapId != options.MapId || observation.Confidence < options.MinimumConfidence
                || observation.ObservedAt < options.ObservedAfter
                || (options.EntityId is not null && observation.EntityId != options.EntityId)) continue;
            var distance = options.Center.DistanceTo(observation.Position);
            if (distance <= options.Radius) results.Add(new SpatialRecallResult(record, observation, distance));
        }
        return results.OrderBy(result => result.Distance)
            .ThenByDescending(result => result.Observation.ObservedAt)
            .ThenBy(result => result.Memory.Id, StringComparer.Ordinal)
            .Take(options.TopK).ToArray();
    }

    private static void Validate(SpatialObservation observation)
    {
        Guard.NotNullOrWhiteSpace(observation.MapId);
        Guard.NotNullOrWhiteSpace(observation.Description);
        if (observation.Position is null || !observation.Position.IsFinite
            || (observation.ObserverPosition is not null && !observation.ObserverPosition.IsFinite))
            throw new ArgumentException("Spatial positions must be finite.", nameof(observation));
        if (!SpatialPoint.IsFiniteNumber(observation.Confidence) || observation.Confidence < 0 || observation.Confidence > 1)
            throw new ArgumentOutOfRangeException(nameof(observation.Confidence));
    }
}