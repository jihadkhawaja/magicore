namespace Mem0Sharp;

/// <summary>Controller-reported primitive outcomes. Completed does not mean the mission succeeded.</summary>
public enum RobotActionOutcome { Completed, Blocked, Failed, Cancelled }

/// <summary>A persistent action attempt with measured execution feedback.</summary>
public sealed record RobotActionEpisode
{
    /// <summary>A stable ID for this attempt, reused on write retries.</summary>
    public required string EpisodeId { get; init; }
    /// <summary>The environment map.</summary>
    public required string MapId { get; init; }
    /// <summary>The metric coordinate frame revision.</summary>
    public required string FrameId { get; init; }
    /// <summary>The requested mission, stored as untrusted historical context.</summary>
    public required string Mission { get; init; }
    /// <summary>The executed primitive or skill identifier.</summary>
    public required string Action { get; init; }
    /// <summary>An optional target object identity.</summary>
    public string? EntityId { get; init; }
    /// <summary>The measured starting point.</summary>
    public required SpatialPoint Start { get; init; }
    /// <summary>The measured ending point.</summary>
    public required SpatialPoint End { get; init; }
    /// <summary>The start heading in radians, using the adapter's documented frame convention.</summary>
    public double HeadingRadians { get; init; }
    /// <summary>Execution start time.</summary>
    public required DateTimeOffset StartedAt { get; init; }
    /// <summary>Time of the controller's final feedback.</summary>
    public required DateTimeOffset CompletedAt { get; init; }
    /// <summary>Measured feedback, never inferred solely from an LLM plan.</summary>
    public RobotActionOutcome Outcome { get; init; }
    /// <summary>A controller report; not an instruction for future missions.</summary>
    public required string Feedback { get; init; }
}

/// <summary>Recall action attempts near their start positions in one map/frame.</summary>
public sealed record RobotEpisodeRecallOptions
{
    /// <summary>Scope, start-position radius, target entity, completion-time lower bound and limit. Confidence is unused.</summary>
    public required SpatialRecallOptions Spatial { get; init; }
    /// <summary>Exact coordinate frame revision.</summary>
    public required string FrameId { get; init; }
    /// <summary>Completion-time cutoff.</summary>
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>Optional exact skill identifier.</summary>
    public string? Action { get; init; }
    /// <summary>Optional heading to match; angular differences wrap around at two pi.</summary>
    public double? HeadingRadians { get; init; }
    /// <summary>Heading tolerance in radians, between zero and pi.</summary>
    public double HeadingToleranceRadians { get; init; } = 0.35;
}