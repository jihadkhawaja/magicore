using System.Text.Json;
using Godot;
using MagiCore;

public partial class RobotLab
{
    private static readonly MemoryAddOptions ScenarioScope = new() { UserId = "robotics-regression", AgentId = "robot-01" };

    private async Task RunMemoryScenarioAsync()
    {
        autonomous = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        var token = BeginOperation();
        try
        {
            var directory = ProjectSettings.GlobalizePath("res://artifacts");
            Directory.CreateDirectory(directory);
            await RoboticsScenarioAsync(directory, token);
            status.Text = "MEMORY TEST PASS";
        }
        catch (OperationCanceledException) { if (IsInsideTree()) status.Text = "TEST CANCELLED"; }
        catch (Exception error) { ReportFailure(error); }
        finally { EndOperation(); }
    }

    private async Task RoboticsScenarioAsync(string directory, CancellationToken token)
    {
        var red = GetNode<StaticBody3D>("RedCrate");
        var originalCrate = red.Position;
        var originalRobot = robot.Transform;
        var originalPitch = pitch;
        var time = DateTimeOffset.UtcNow.AddMinutes(-1);
        var memory = new MemoryService();
        var checks = new List<string>();
        Node3D? blocker = null;
        try
        {
            status.Text = "MEMORY TEST";
            robot.Position = new Vector3(0, 0.9f, 7);
            robot.Rotation = Vector3.Zero;
            pitch = 0;
            camera.Rotation = Vector3.Zero;
            await DemoPauseAsync("OBSERVE", "Robot 01 captures RGB-D evidence of the red crate.");
            var first = await ScenarioObservationAsync(red, "red-crate", "red crate", "first", time, token);
            await memory.RememberObjectAsync(first, ScenarioScope, token);
            await memory.RememberObjectAsync(first, ScenarioScope, token);
            await memory.RememberSpatialAsync(first.Observation, ScenarioScope, token);
            Require((await memory.RecallObjectsAsync(ScenarioQuery(time.AddSeconds(1)), token)).Single().ObservationCount == 1, "retry deduplication");
            checks.Add("Repeated sensor event counts once.");
            await DemoPauseAsync("REMEMBER", "Duplicate sensor events resolve to one observation.");

            red.Position = new Vector3(5, 0.65f, -5);
            var moved = await ScenarioObservationAsync(red, "red-crate", "red crate", "moved", time.AddSeconds(10), token);
            Require(first.Observation.Position.DistanceTo(moved.Observation.Position) > 5, "physics-measured displacement");
            await memory.RememberObjectAsync(moved, ScenarioScope, token);
            var delayed = first with { ObservationId = "delayed", Observation = first.Observation with { ObservedAt = time.AddSeconds(5) } };
            await memory.RememberObjectAsync(delayed, ScenarioScope, token);
            var oldQuery = ScenarioQuery(time.AddSeconds(11), first.Observation.Position, 1);
            var oldRaw = await memory.RecallSpatialAsync(oldQuery.Spatial, token);
            var oldBeliefs = await memory.RecallObjectsAsync(oldQuery, token);
            Require(oldRaw.Count > 0 && oldBeliefs.Count == 0, "stale-location baseline comparison");
            var relocated = (await memory.RecallObjectsAsync(ScenarioQuery(time.AddSeconds(11)), token)).Single();
            Require(relocated.RelocationCount == 1 && relocated.Position == moved.Observation.Position, "out-of-order reconstruction");
            checks.Add("Moved crate leaves zero ghost objects at its old location; raw spatial recall still returns the old point.");
            ShowMemories([relocated]);
            await DemoPauseAsync("RELOCATED", "The crate moved. Object memory removes its stale location.");

            var midpoint = (camera.GlobalPosition + red.Position) / 2;
            Box("ScenarioOccluder", midpoint, new Vector3(2.5f, 3, 0.5f), "566d73");
            blocker = GetNode<Node3D>("ScenarioOccluder");
            var occludedDepth = await CaptureDepthAsync(token);
            var pixel = sensorCamera.UnprojectPosition(red.Position);
            var foreground = occludedDepth.PointAt(pixel.X / sensor.Size.X, pixel.Y / sensor.Size.Y);
            Require(foreground is not null && foreground.DistanceTo(moved.Observation.Position) > 1, "physical occlusion");
            await memory.RememberObjectAsync(moved with
            {
                ObservationId = "occluded", Visibility = ObjectVisibility.Occluded,
                Observation = moved.Observation with { ObservedAt = time.AddSeconds(20) }
            }, ScenarioScope, token);
            var occluded = (await memory.RecallObjectsAsync(ScenarioQuery(time.AddSeconds(21)), token)).Single();
            Require(occluded.State == SpatialBeliefState.Occluded && occluded.LastSeenAt == time.AddSeconds(10), "occlusion is not absence");
            ShowMemories([occluded]);
            await DemoPauseAsync("OCCLUDED", "Occlusion preserves the last-seen position for re-observation.");
            blocker.QueueFree();
            blocker = null;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            checks.Add("Foreground occlusion preserves last-seen location and requests re-observation.");

            red.Position = new Vector3(-6, 0.65f, 8);
            var emptyDepth = await CaptureDepthAsync(token);
            var cleared = emptyDepth.PointAt(pixel.X / sensor.Size.X, pixel.Y / sensor.Size.Y);
            Require(cleared is not null && cleared.DistanceTo(moved.Observation.Position) > 1, "checked empty location");
            await memory.RememberObjectAsync(moved with
            {
                ObservationId = "absent", Visibility = ObjectVisibility.Absent,
                Observation = moved.Observation with { ObservedAt = time.AddSeconds(30) }
            }, ScenarioScope, token);
            Require((await memory.RecallObjectsAsync(ScenarioQuery(time.AddSeconds(31)), token)).Single().State == SpatialBeliefState.Missing, "confirmed absence");
            red.Position = new Vector3(5, 0.65f, -5);
            var recovered = await ScenarioObservationAsync(red, "red-crate", "red crate", "recovered", time.AddSeconds(40), token);
            await memory.RememberObjectAsync(recovered, ScenarioScope, token);
            var blue = await ScenarioObservationAsync(GetNode<StaticBody3D>("BlueCrate"), "blue-crate", "blue crate", "blue", time.AddSeconds(40), token);
            await memory.RememberObjectAsync(blue, ScenarioScope, token);
            var objects = await memory.RecallObjectsAsync(ScenarioQuery(time.AddSeconds(41)), token);
            Require(objects.Count == 2 && objects.All(item => !item.NeedsObservation), "re-observation recovery");
            Require(RoboticsMemoryExtensions.GetObjectRelations(objects, 4).Any(item => item.Kind == SpatialRelationKind.Near), "measured scene relations");
            var aged = await memory.RecallObjectsAsync(ScenarioQuery(time.AddDays(1)), token);
            Require(aged.All(item => item.NeedsObservation && item.Confidence < 0.5), "time-based re-observation");
            checks.Add("Confirmed absence, reacquisition, geometric relations and confidence aging verified.");
            ShowMemories(objects);
            await DemoPauseAsync("REACQUIRED", "The crate is found again; confidence and relations are refreshed.");

            robot.Position = new Vector3(0, 0.9f, -11);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var blocked = await ExecuteActionAsync("forward", 1, "Inspect the warehouse", token);
            Require(blocked.Outcome == RobotActionOutcome.Blocked && robot.Position.Z >= -11.6f, "controller wall feedback");
            await memory.RememberRobotEpisodeAsync(blocked, ScenarioScope, token);
            checks.Add("Actual wall collision recorded as Blocked, not mission success.");
            await DemoPauseAsync("BLOCKED", "Controller feedback records wall contact as a blocked action.");

            var checkpointPath = Path.Combine(directory, "robotics-checkpoint.json");
            await using (var stream = File.Create(checkpointPath))
                await JsonSerializer.SerializeAsync(stream, await memory.GetAllAsync(cancellationToken: token), cancellationToken: token);
            await ReplayRoboticsAsync(directory, token);
            checks.Add("Object beliefs and blocked-action history recovered from disk into a fresh store.");

            robot.Position = new Vector3(0, 0.9f, 7);
            ShowMemories(objects);
            reasoning.Text = "MEMORY TEST PASS\nMoved crate recovered; blocked motion recorded.";
            await DemoPauseAsync("MEMORY TEST PASS", "Two objects and the blocked action survived checkpoint replay.");
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var screen = GetViewport().GetTexture().GetImage();
            screen.SavePng(Path.Combine(directory, "robotics-memory.png"));
            await using (var stream = File.Create(Path.Combine(directory, "robotics-report.json")))
                await JsonSerializer.SerializeAsync(stream, new
                {
                    passed = true, checks, rawOldLocationCount = oldRaw.Count, beliefOldLocationCount = oldBeliefs.Count,
                    objectCount = objects.Count, relocations = relocated.RelocationCount,
                    actionOutcome = blocked.Outcome.ToString(),
                    scope = "Deterministic Godot RGB-D adapter and memory regression; scripted labels/identities, not neural perception accuracy."
                }, new JsonSerializerOptions { WriteIndented = true }, token);
            GD.Print($"ROBOTICS PASS: {checks.Count} scenario checks; raw ghosts={oldRaw.Count}, object ghosts={oldBeliefs.Count}; checkpoint replay passed.");
        }
        finally
        {
            blocker?.QueueFree();
            red.Position = originalCrate;
            robot.Transform = originalRobot;
            pitch = originalPitch;
            camera.Rotation = new Vector3(pitch, 0, 0);
            actionRemaining = 0;
            ShowMemories([]);
        }
    }

    private async Task DemoPauseAsync(string phase, string detail)
    {
        if (!OS.GetCmdlineUserArgs().Contains("--record-demo")) return;
        status.Text = phase;
        reasoning.Text = detail;
        await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
    }

    private async Task ReplayRoboticsAsync(string directory, CancellationToken token)
    {
        await using var stream = File.OpenRead(Path.Combine(directory, "robotics-checkpoint.json"));
        var records = await JsonSerializer.DeserializeAsync<MagiCore.Memory[]>(stream, cancellationToken: token)
            ?? throw new InvalidDataException("Missing robotics checkpoint.");
        var store = new InMemoryStore();
        foreach (var record in records) await store.SaveAsync(record, cancellationToken: token);
        var restarted = new MemoryService(store: store);
        var query = ScenarioQuery(DateTimeOffset.UtcNow);
        var objects = await restarted.RecallObjectsAsync(query, token);
        Require(objects.Count == 2 && objects.Single(item => item.EntityId == "red-crate").Position.X > 4, "disk object recovery");
        var episodes = await restarted.RecallRobotEpisodesAsync(new RobotEpisodeRecallOptions
        {
            FrameId = RobotBrain.FrameId, Spatial = query.Spatial
        }, token);
        Require(episodes.Count == 1 && episodes[0].Outcome == RobotActionOutcome.Blocked, "disk action recovery");
        GD.Print("REPLAY PASS: fresh service/store recovered two objects and a blocked action from disk.");
    }

    private async Task<RoboticsObservation> ScenarioObservationAsync(Node3D target, string entityId, string label,
        string eventId, DateTimeOffset time, CancellationToken token)
    {
        var depth = await CaptureDepthAsync(token);
        var pixel = sensorCamera.UnprojectPosition(target.GlobalPosition);
        Require(!sensorCamera.IsPositionBehind(target.GlobalPosition)
            && pixel.X >= 0 && pixel.X < sensor.Size.X && pixel.Y >= 0 && pixel.Y < sensor.Size.Y, "target inside camera view");
        var measured = depth.PointAt(pixel.X / sensor.Size.X, pixel.Y / sensor.Size.Y);
        Require(measured is not null && measured.DistanceTo(Point(target.GlobalPosition)) < 1.2, "depth hits target surface");
        return new RoboticsObservation
        {
            ObservationId = eventId, SourceId = "scripted-rgbd-test", FrameId = RobotBrain.FrameId, PositionUncertainty = 0.35,
            Observation = new SpatialObservation
            {
                MapId = RobotBrain.MapId, EntityId = entityId, Description = label, ObservedAt = time,
                Position = measured!, ObserverPosition = depth.Observer, Confidence = 0.9
            }
        };
    }

    private static RoboticsRecallOptions ScenarioQuery(DateTimeOffset time, SpatialPoint? center = null, double radius = 35) => new()
    {
        FrameId = RobotBrain.FrameId, At = time,
        Spatial = new SpatialRecallOptions
        {
            MapId = RobotBrain.MapId, UserId = ScenarioScope.UserId!, AgentId = ScenarioScope.AgentId,
            Center = center ?? new SpatialPoint(0, 1, 0), Radius = radius, TopK = 64, MinimumConfidence = 0.4
        }
    };

    private static void Require(bool condition, string check)
    {
        if (!condition) throw new InvalidOperationException($"Robotics check failed: {check}.");
    }
}