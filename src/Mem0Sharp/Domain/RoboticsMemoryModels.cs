namespace Mem0Sharp;

/// <summary>The visibility reported by a perception adapter, not inferred from detector silence.</summary>
public enum ObjectVisibility { Observed, Occluded, Absent }

/// <summary>The state of remembered evidence; none of these states authorizes physical motion.</summary>
public enum SpatialBeliefState { Observed, Stale, Occluded, Missing, Uncertain, Conflicted }

/// <summary>Point-level metric relations, not physical affordances.</summary>
public enum SpatialRelationKind { Near, Above }

/// <summary>A directed relation between two reconstructed objects in one map/frame.</summary>
public sealed record SpatialObjectRelation(string SubjectId, string TargetId, SpatialRelationKind Kind, double Distance);

/// <summary>An immutable, externally localized object observation in a versioned metric frame.</summary>
public sealed record RoboticsObservation
{
    /// <summary>A stable event ID, reused when retrying the same sensor observation.</summary>
    public required string ObservationId { get; init; }
    /// <summary>The sensor or perception pipeline that produced the evidence.</summary>
    public required string SourceId { get; init; }
    /// <summary>The coordinate frame and revision, for example building/map-v3. Units are meters.</summary>
    public required string FrameId { get; init; }
    /// <summary>The measured point, capture time, label and externally associated entity ID.</summary>
    public required SpatialObservation Observation { get; init; }
    /// <summary>A conservative positional error radius in meters, not a covariance estimate.</summary>
    public double PositionUncertainty { get; init; } = 0.15;
    /// <summary>Absent requires an explicit visibility/coverage check by the caller.</summary>
    public ObjectVisibility Visibility { get; init; }
}

/// <summary>Policy for reconstructing an object map at an event time.</summary>
public sealed record RoboticsRecallOptions
{
    /// <summary>Tenant, map, entity, radius and result limits. Applied after resolving object history.</summary>
    public required SpatialRecallOptions Spatial { get; init; }
    /// <summary>Only evidence in this exact coordinate frame revision is comparable.</summary>
    public required string FrameId { get; init; }
    /// <summary>The event-time cutoff, also used for deterministic aging.</summary>
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>Maximum age of positive evidence before a fresh observation is requested.</summary>
    public TimeSpan FreshFor { get; init; } = TimeSpan.FromMinutes(2);
    /// <summary>Half-life of the latest positive confidence. Repeated frames do not inflate confidence.</summary>
    public TimeSpan ConfidenceHalfLife { get; init; } = TimeSpan.FromDays(1);
    /// <summary>Maximum acceptable position error radius.</summary>
    public double MaximumUncertainty { get; init; } = 0.5;
    /// <summary>Minimum displacement beyond both error radii counted as a relocation.</summary>
    public double MovementThreshold { get; init; } = 0.5;
}

/// <summary>A reconstructed object belief with auditable, event-time-ordered evidence.</summary>
public sealed record SpatialObjectMemory(
    string EntityId, RoboticsObservation Latest, SpatialPoint Position,
    DateTimeOffset? LastSeenAt, double Confidence, SpatialBeliefState State,
    int ObservationCount, int RelocationCount, double Distance,
    IReadOnlyList<RoboticsObservation> Evidence)
{
    /// <summary>Whether memory explicitly requests fresh sensing before using the remembered location.</summary>
    public bool NeedsObservation => State != SpatialBeliefState.Observed;
}