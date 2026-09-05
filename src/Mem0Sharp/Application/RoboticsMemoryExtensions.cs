using System.Text.Json;

namespace Mem0Sharp;

/// <summary>Persistent robotics evidence on existing memory stores, independent of perception and control.</summary>
public static partial class RoboticsMemoryExtensions
{
    private const string EvidenceKey = "robotics_observation_v1";
    private const string EvidenceType = "robotics_observation";

    /// <summary>Appends measured evidence without LLM inference or text deduplication.</summary>
    /// <param name="memory">The backing memory service.</param>
    /// <param name="evidence">Evidence with an externally assigned stable object identity.</param>
    /// <param name="options">Required user scope and optional agent/retention policy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored memory result.</returns>
    public static Task<AddResult> RememberObjectAsync(this IMemoryService memory,
        RoboticsObservation evidence, MemoryAddOptions options, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(memory);
        Guard.NotNull(options);
        Guard.NotNullOrWhiteSpace(options.UserId);
        Validate(evidence);
        cancellationToken.ThrowIfCancellationRequested();
        var metadata = options.Metadata?.ToDictionary(pair => pair.Key, pair => pair.Value)
            ?? new Dictionary<string, string>();
        metadata[EvidenceKey] = JsonSerializer.Serialize(evidence);
        return memory.AddAsync(evidence.Observation.Description, options with
        {
            Metadata = metadata, MemoryType = EvidenceType, Infer = false, Deduplicate = false,
            ReferenceTime = evidence.Observation.ObservedAt
        }, cancellationToken);
    }

    /// <summary>Replays object evidence before filtering locations; stale locations never replace newer ones.</summary>
    /// <param name="memory">The backing memory service.</param>
    /// <param name="options">Scope, reference time, geometry and aging policy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Nearest reconstructed beliefs, including explicit uncertainty and evidence provenance.</returns>
    public static async Task<IReadOnlyList<SpatialObjectMemory>> RecallObjectsAsync(this IMemoryService memory,
        RoboticsRecallOptions options, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(memory);
        Validate(options);
        cancellationToken.ThrowIfCancellationRequested();
        var query = options.Spatial;
        var records = await memory.GetAllAsync(new MemoryFilter(UserId: query.UserId, AgentId: query.AgentId), cancellationToken).ConfigureAwait(false);
        var observations = new List<RoboticsObservation>();
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (record.MemoryType != EvidenceType || record.ExpiresAt <= DateTimeOffset.UtcNow
                || !record.Metadata.TryGetValue(EvidenceKey, out var json)) continue;
            try
            {
                var evidence = JsonSerializer.Deserialize<RoboticsObservation>(json);
                if (evidence is null) continue;
                Validate(evidence);
                if (evidence.FrameId != options.FrameId || evidence.Observation.MapId != query.MapId
                    || evidence.Observation.ObservedAt > options.At
                    || (query.EntityId is not null && evidence.Observation.EntityId != query.EntityId)) continue;
                observations.Add(evidence);
            }
            catch (JsonException) { }
            catch (ArgumentException) { }
        }

        var results = new List<SpatialObjectMemory>();
        foreach (var group in observations.Distinct().GroupBy(item => item.Observation.EntityId!, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var history = group.OrderBy(item => item.Observation.ObservedAt)
                .ThenBy(item => item.SourceId, StringComparer.Ordinal)
                .ThenBy(item => item.ObservationId, StringComparer.Ordinal).ToArray();
            var latestTime = history[history.Length - 1].Observation.ObservedAt;
            var newest = history.Where(item => item.Observation.ObservedAt == latestTime).ToArray();
            var latest = newest.OrderByDescending(item => item.Observation.Confidence)
                .ThenBy(item => item.SourceId, StringComparer.Ordinal).First();
            var positive = history.Where(item => item.Visibility == ObjectVisibility.Observed).ToArray();
            var lastSeen = positive.OrderByDescending(item => item.Observation.ObservedAt)
                .ThenByDescending(item => item.Observation.Confidence)
                .ThenBy(item => item.SourceId, StringComparer.Ordinal)
                .ThenBy(item => item.ObservationId, StringComparer.Ordinal).FirstOrDefault();
            var position = lastSeen?.Observation.Position ?? latest.Observation.Position;
            var age = lastSeen is null ? TimeSpan.MaxValue : options.At - lastSeen.Observation.ObservedAt;
            var confidence = (lastSeen?.Observation.Confidence ?? 0)
                * Math.Pow(0.5, age.TotalSeconds / options.ConfidenceHalfLife.TotalSeconds);
            var conflicted = history.GroupBy(item => (item.SourceId, item.ObservationId)).Any(events => events.Count() > 1)
                || newest.Any(item => item.Visibility != latest.Visibility
                || item.Observation.Position.DistanceTo(latest.Observation.Position)
                    > item.PositionUncertainty + latest.PositionUncertainty + options.MovementThreshold);
            var state = conflicted ? SpatialBeliefState.Conflicted
                : latest.Visibility == ObjectVisibility.Absent ? SpatialBeliefState.Missing
                : latest.Visibility == ObjectVisibility.Occluded ? SpatialBeliefState.Occluded
                : latest.PositionUncertainty > options.MaximumUncertainty || confidence <= 0 || confidence < query.MinimumConfidence ? SpatialBeliefState.Uncertain
                : age > options.FreshFor ? SpatialBeliefState.Stale : SpatialBeliefState.Observed;
            var relocations = 0;
            for (var index = 1; index < positive.Length; index++)
            {
                var previous = positive[index - 1];
                var current = positive[index];
                if (current.Observation.ObservedAt > previous.Observation.ObservedAt
                    && current.Observation.Position.DistanceTo(previous.Observation.Position)
                        > options.MovementThreshold + previous.PositionUncertainty + current.PositionUncertainty)
                    relocations++;
            }
            var distance = query.Center.DistanceTo(position);
            if (distance <= query.Radius && !(latestTime < query.ObservedAfter))
                results.Add(new SpatialObjectMemory(group.Key, latest, position, lastSeen?.Observation.ObservedAt,
                    confidence, state, history.Length, relocations, distance, history));
        }
        return results.OrderBy(item => item.Distance).ThenBy(item => item.EntityId, StringComparer.Ordinal)
            .Take(query.TopK).ToArray();
    }

    /// <summary>Finds a unique label-and-distance match; ambiguous or displaced detections require a new identity or an external tracker.</summary>
    /// <param name="observation">A measured detection without an entity identity.</param>
    /// <param name="frameId">The exact coordinate frame revision.</param>
    /// <param name="objects">Previously reconstructed object beliefs.</param>
    /// <param name="maximumDistance">Maximum measured-point separation in meters.</param>
    /// <returns>The single candidate ID, or null. This is not a visual re-identification algorithm.</returns>
    public static string? AssociateObject(SpatialObservation observation, string frameId,
        IReadOnlyList<SpatialObjectMemory> objects, double maximumDistance = 0.75)
    {
        Guard.NotNull(observation);
        Guard.NotNull(objects);
        Guard.NotNullOrWhiteSpace(frameId);
        Guard.NotNullOrWhiteSpace(observation.Description);
        if (observation.Position is null || !observation.Position.IsFinite
            || !SpatialPoint.IsFiniteNumber(maximumDistance) || maximumDistance < 0)
            throw new ArgumentException("Association requires finite geometry and a nonnegative radius.");
        var candidates = objects.Where(item => item.Latest.FrameId == frameId
            && item.Latest.Observation.MapId == observation.MapId
            && item.State is SpatialBeliefState.Observed or SpatialBeliefState.Stale
            && string.Equals(item.Latest.Observation.Description.Trim(), observation.Description.Trim(), StringComparison.OrdinalIgnoreCase)
            && item.Position.DistanceTo(observation.Position) <= maximumDistance)
            .Select(item => item.EntityId).Distinct(StringComparer.Ordinal).Take(2).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    /// <summary>Derives conservative point relations in a Y-up metric frame; never implies support, containment or a traversable path.</summary>
    /// <param name="objects">Beliefs from one recall snapshot.</param>
    /// <param name="nearDistance">Maximum separation including both error radii.</param>
    /// <returns>Directed near/above relations between fresh, unambiguous objects in the same map/frame.</returns>
    public static IReadOnlyList<SpatialObjectRelation> GetObjectRelations(IReadOnlyList<SpatialObjectMemory> objects,
        double nearDistance = 3)
    {
        Guard.NotNull(objects);
        if (!SpatialPoint.IsFiniteNumber(nearDistance) || nearDistance < 0) throw new ArgumentOutOfRangeException(nameof(nearDistance));
        var relations = new List<SpatialObjectRelation>();
        foreach (var subject in objects.Where(item => !item.NeedsObservation))
        foreach (var target in objects.Where(item => !item.NeedsObservation))
        {
            if (subject.EntityId == target.EntityId || subject.Latest.FrameId != target.Latest.FrameId
                || subject.Latest.Observation.MapId != target.Latest.Observation.MapId) continue;
            var uncertainty = subject.Latest.PositionUncertainty + target.Latest.PositionUncertainty;
            var distance = subject.Position.DistanceTo(target.Position);
            if (distance + uncertainty > nearDistance) continue;
            relations.Add(new(subject.EntityId, target.EntityId, SpatialRelationKind.Near, distance));
            if (subject.Position.Y - target.Position.Y > uncertainty)
                relations.Add(new(subject.EntityId, target.EntityId, SpatialRelationKind.Above, distance));
        }
        return relations;
    }

    private static void Validate(RoboticsObservation evidence)
    {
        Guard.NotNull(evidence);
        Guard.NotNullOrWhiteSpace(evidence.ObservationId);
        Guard.NotNullOrWhiteSpace(evidence.SourceId);
        Guard.NotNullOrWhiteSpace(evidence.FrameId);
        Guard.NotNull(evidence.Observation);
        Guard.NotNullOrWhiteSpace(evidence.Observation.MapId);
        Guard.NotNullOrWhiteSpace(evidence.Observation.EntityId);
        Guard.NotNullOrWhiteSpace(evidence.Observation.Description);
        if (evidence.Observation.Position is null || !evidence.Observation.Position.IsFinite
            || (evidence.Observation.ObserverPosition is not null && !evidence.Observation.ObserverPosition.IsFinite))
            throw new ArgumentException("Evidence positions must be finite.", nameof(evidence));
        if (!SpatialPoint.IsFiniteNumber(evidence.Observation.Confidence) || evidence.Observation.Confidence < 0 || evidence.Observation.Confidence > 1
            || !SpatialPoint.IsFiniteNumber(evidence.PositionUncertainty) || evidence.PositionUncertainty < 0
            || !Enum.IsDefined(typeof(ObjectVisibility), evidence.Visibility))
            throw new ArgumentOutOfRangeException(nameof(evidence));
    }

    private static void Validate(RoboticsRecallOptions options)
    {
        Guard.NotNull(options);
        Guard.NotNull(options.Spatial);
        Guard.NotNullOrWhiteSpace(options.FrameId);
        var query = options.Spatial;
        Guard.NotNullOrWhiteSpace(query.MapId);
        Guard.NotNullOrWhiteSpace(query.UserId);
        if (query.Center is null || !query.Center.IsFinite) throw new ArgumentException("A finite center is required.", nameof(options));
        if (!SpatialPoint.IsFiniteNumber(query.Radius) || query.Radius < 0 || query.TopK < 1
            || !SpatialPoint.IsFiniteNumber(query.MinimumConfidence) || query.MinimumConfidence < 0 || query.MinimumConfidence > 1
            || options.FreshFor < TimeSpan.Zero || options.ConfidenceHalfLife <= TimeSpan.Zero
            || !SpatialPoint.IsFiniteNumber(options.MaximumUncertainty) || options.MaximumUncertainty < 0
            || !SpatialPoint.IsFiniteNumber(options.MovementThreshold) || options.MovementThreshold < 0)
            throw new ArgumentOutOfRangeException(nameof(options));
    }
}