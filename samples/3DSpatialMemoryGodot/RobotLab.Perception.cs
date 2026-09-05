using Godot;
using Mem0Sharp;

public partial class RobotLab
{
    private const int DepthWidth = 80;
    private const int DepthHeight = 45;
    private TaskCompletionSource<RobotDepthFrame>? depthCapture;
    private bool motionBlocked;

    private Task<RobotDepthFrame> CaptureDepthAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (depthCapture is not null) throw new InvalidOperationException("Depth capture is already pending.");
        depthCapture = new TaskCompletionSource<RobotDepthFrame>();
        return depthCapture.Task.WaitAsync(token);
    }

    private RobotDepthFrame CaptureDepth()
    {
        var points = new SpatialPoint?[DepthWidth * DepthHeight];
        var excluded = new Godot.Collections.Array<Rid> { robot.GetRid() };
        var space = GetWorld3D().DirectSpaceState;
        for (var row = 0; row < DepthHeight; row++)
        for (var column = 0; column < DepthWidth; column++)
        {
            var pixel = new Vector2((column + 0.5f) * sensor.Size.X / DepthWidth, (row + 0.5f) * sensor.Size.Y / DepthHeight);
            var origin = sensorCamera.ProjectRayOrigin(pixel);
            using var ray = PhysicsRayQueryParameters3D.Create(origin, origin + sensorCamera.ProjectRayNormal(pixel) * 30);
            ray.Exclude = excluded;
            var hit = space.IntersectRay(ray);
            if (hit.Count > 0) points[row * DepthWidth + column] = Point((Vector3)hit["position"]);
        }
        return new RobotDepthFrame(Point(sensorCamera.GlobalPosition), DateTimeOffset.UtcNow, points);
    }

    private async Task<RobotActionEpisode> ExecuteActionAsync(string action, double seconds, string missionText, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var start = Point(camera.GlobalPosition);
        var heading = robot.Rotation.Y;
        var startedAt = DateTimeOffset.UtcNow;
        motionBlocked = false;
        activeAction = action;
        actionRemaining = seconds;
        try
        {
            while (actionRemaining > 0)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                token.ThrowIfCancellationRequested();
            }
            var end = Point(camera.GlobalPosition);
            var horizontalDistance = new SpatialPoint(start.X, 0, start.Z).DistanceTo(new SpatialPoint(end.X, 0, end.Z));
            var translating = action is "forward" or "backward";
            var blocked = translating && (motionBlocked || horizontalDistance < 1.5 * seconds * 0.5);
            return new RobotActionEpisode
            {
                EpisodeId = Guid.NewGuid().ToString("N"), MapId = RobotBrain.MapId, FrameId = RobotBrain.FrameId,
                Mission = missionText, Action = action, Start = start, End = end, HeadingRadians = heading,
                StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow,
                Outcome = blocked ? RobotActionOutcome.Blocked : RobotActionOutcome.Completed,
                Feedback = translating ? $"Measured travel {horizontalDistance:F2} m; wall contact {motionBlocked}." : "Primitive completed; mission completion not verified."
            };
        }
        finally { actionRemaining = 0; }
    }

    private sealed record RobotDepthFrame(SpatialPoint Observer, DateTimeOffset ObservedAt, SpatialPoint?[] Points)
    {
        public SpatialPoint? PointAt(double normalizedX, double normalizedY)
        {
            var column = Math.Clamp((int)(normalizedX * DepthWidth), 0, DepthWidth - 1);
            var row = Math.Clamp((int)(normalizedY * DepthHeight), 0, DepthHeight - 1);
            return Points[row * DepthWidth + column];
        }
    }
}