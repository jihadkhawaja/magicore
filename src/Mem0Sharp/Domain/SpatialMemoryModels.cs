namespace Mem0Sharp;

public sealed record SpatialPoint(double X, double Y, double Z)
{
    public double DistanceTo(SpatialPoint other)
    {
        var deltaX = X - other.X;
        var deltaY = Y - other.Y;
        var deltaZ = Z - other.Z;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ);
    }

    internal bool IsFinite => IsFiniteNumber(X) && IsFiniteNumber(Y) && IsFiniteNumber(Z);

    internal static bool IsFiniteNumber(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

public sealed record SpatialObservation
{
    public required string MapId { get; init; }
    public required SpatialPoint Position { get; init; }
    public required string Description { get; init; }
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
    public SpatialPoint? ObserverPosition { get; init; }
    public string? EntityId { get; init; }
    public double Confidence { get; init; } = 1;
}

public sealed record SpatialRecallOptions
{
    public required string MapId { get; init; }
    public required string UserId { get; init; }
    public string? AgentId { get; init; }
    public required SpatialPoint Center { get; init; }
    public double Radius { get; init; } = 10;
    public int TopK { get; init; } = 10;
    public DateTimeOffset? ObservedAfter { get; init; }
    public double MinimumConfidence { get; init; }
    public string? EntityId { get; init; }
}

public sealed record SpatialRecallResult(Memory Memory, SpatialObservation Observation, double Distance);