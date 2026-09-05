using Godot;
using Mem0Sharp;

public partial class RobotLab : Node3D
{
    private CharacterBody3D robot = null!;
    private Camera3D camera = null!;
    private Camera3D sensorCamera = null!;
    private SubViewport sensor = null!;
    private Label status = null!;
    private Label pose = null!;
    private RichTextLabel memories = null!;
    private LineEdit mission = null!;
    private float pitch;
    private RobotBrain? brain;
    private bool brainReady;
    private bool busy;
    private bool autonomous;
    private int stepCount;
    private ulong nextStepAt;
    private string activeAction = "wait";
    private double actionRemaining;
    private CancellationTokenSource? operation;
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<Node3D> markers = [];
    private string lastFailure = "";
    private Label reasoning = null!;

    public override void _Ready()
    {
        BuildWorld();
        BuildRobot();
        BuildHud();
        if (OS.GetCmdlineUserArgs().Contains("--smoke-test") || OS.GetCmdlineUserArgs().Contains("--live-smoke"))
            _ = SmokeTestAsync(OS.GetCmdlineUserArgs().Contains("--live-smoke"));
    }

    public override void _PhysicsProcess(double delta)
    {
        var movement = Vector3.Zero;
        if (Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            if (Input.IsPhysicalKeyPressed(Key.W)) movement.Z -= 1;
            if (Input.IsPhysicalKeyPressed(Key.S)) movement.Z += 1;
            if (Input.IsPhysicalKeyPressed(Key.A)) movement.X -= 1;
            if (Input.IsPhysicalKeyPressed(Key.D)) movement.X += 1;
        }
        var speed = 2.5f;
        if (actionRemaining > 0)
        {
            var duration = Math.Min(delta, actionRemaining);
            actionRemaining -= duration;
            if (activeAction == "forward") movement.Z = -1;
            if (activeAction == "backward") movement.Z = 1;
            if (activeAction == "turn_left") robot.RotateY((float)duration * 0.8f);
            if (activeAction == "turn_right") robot.RotateY(-(float)duration * 0.8f);
            if (activeAction == "look_up") pitch = Mathf.Clamp(pitch + (float)duration * 0.5f, -0.9f, 0.9f);
            if (activeAction == "look_down") pitch = Mathf.Clamp(pitch - (float)duration * 0.5f, -0.9f, 0.9f);
            camera.Rotation = new Vector3(pitch, 0, 0);
            speed = 1.5f * (float)(duration / delta);
        }
        robot.Velocity = robot.Basis * movement.Normalized() * speed + Vector3.Down * 2;
        robot.MoveAndSlide();
        sensorCamera.GlobalTransform = camera.GlobalTransform;
        pose.Text = $"ROBOT 01    X {robot.Position.X:F1}    Y {camera.GlobalPosition.Y:F1}    Z {robot.Position.Z:F1}    |    MAP warehouse-v1";
        if (autonomous && !busy && Time.GetTicksMsec() >= nextStepAt) _ = StepAsync();
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Keycode: Key.Escape }) Input.MouseMode = Input.MouseModeEnum.Visible;
        if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        {
            Stop();
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        if (input is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            robot.RotateY(-motion.Relative.X * 0.003f);
            pitch = Mathf.Clamp(pitch - motion.Relative.Y * 0.003f, -0.9f, 0.9f);
            camera.Rotation = new Vector3(pitch, 0, 0);
        }
    }

    private void BuildWorld()
    {
        var environment = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("b7cbd0"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White,
                AmbientLightEnergy = 0.65f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic
            }
        };
        AddChild(environment);
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, -30, 0), LightEnergy = 1.4f, ShadowEnabled = true });
        Box("Floor", new Vector3(0, -0.2f, 0), new Vector3(24, 0.4f, 24), "79898c");
        Box("BackWall", new Vector3(0, 2, -12), new Vector3(24, 4, 0.3f), "d1dcda");
        Box("LeftWall", new Vector3(-12, 2, 0), new Vector3(0.3f, 4, 24), "c0cecc");
        Box("RightWall", new Vector3(12, 2, 0), new Vector3(0.3f, 4, 24), "c0cecc");
        Box("FrontWall", new Vector3(0, 2, 12), new Vector3(24, 4, 0.3f), "c0cecc");
        for (var stripe = -10; stripe <= 10; stripe += 2)
        {
            Box("FloorLine", new Vector3(stripe, 0.012f, 0), new Vector3(0.025f, 0.01f, 24), "a1b2b1", false);
            Box("FloorLine", new Vector3(0, 0.012f, stripe), new Vector3(24, 0.01f, 0.025f), "a1b2b1", false);
        }
        Box("RedCrate", new Vector3(-3, 0.65f, -3), new Vector3(1.3f, 1.3f, 1.3f), "cf534c");
        Box("BlueCrate", new Vector3(3, 0.65f, -5), new Vector3(1.3f, 1.3f, 1.3f), "428ea7");
        Box("GreenCrate", new Vector3(5, 0.65f, 2), new Vector3(1.3f, 1.3f, 1.3f), "4c9e73");
        Box("YellowCrate", new Vector3(-5, 0.65f, 3), new Vector3(1.3f, 1.3f, 1.3f), "e3bb45");
        Box("ChargingDock", new Vector3(0, 0.12f, 9), new Vector3(2.8f, 0.24f, 2), "40b8a2");
        for (var shelf = 0; shelf < 3; shelf++)
        {
            Box("Shelf", new Vector3(-8, 0.5f + shelf, -6), new Vector3(3, 0.12f, 1.4f), "45535a");
            Box("Parcel", new Vector3(-8.5f + shelf * 0.5f, 0.9f + shelf, -6), new Vector3(0.7f, 0.7f, 0.8f), "b7a37d");
        }
        Box("RackPost", new Vector3(-9.5f, 1.5f, -6), new Vector3(0.12f, 3, 1.4f), "45535a");
        Box("RackPost", new Vector3(-6.5f, 1.5f, -6), new Vector3(0.12f, 3, 1.4f), "45535a");
        Sign("MEM0 / SPATIAL LAB", new Vector3(0, 3, -11.8f), 90);
        Sign("01   STORAGE", new Vector3(-7.7f, 3.4f, -11.8f), 55);
        Sign("02   INSPECTION", new Vector3(7, 3.4f, -11.8f), 55);
    }

    private void Box(string name, Vector3 position, Vector3 size, string color, bool collision = true)
    {
        var body = new StaticBody3D { Name = name, Position = position };
        body.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(color), Roughness = 0.8f }
        });
        if (collision) body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(body);
    }

    private void Sign(string text, Vector3 position, int size)
    {
        AddChild(new Label3D { Text = text, Position = position, FontSize = size, PixelSize = 0.007f, Modulate = new Color("244c50"), OutlineSize = 0 });
    }

    private void BuildRobot()
    {
        robot = new CharacterBody3D { Position = new Vector3(0, 0.9f, 7) };
        robot.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.3f, Height = 1.8f } });
        camera = new Camera3D { Position = new Vector3(0, 0.6f, 0), Current = true, Fov = 75 };
        robot.AddChild(camera);
        AddChild(robot);
        sensor = new SubViewport { Size = new Vector2I(640, 360), World3D = GetViewport().World3D, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        sensorCamera = new Camera3D { Current = true, Fov = 75 };
        sensor.AddChild(sensorCamera);
        AddChild(sensor);
    }

    private void BuildHud()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(root);
        var top = new PanelContainer();
        top.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        top.OffsetBottom = 66;
        top.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("edf3ef"), ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 16, ContentMarginBottom = 16 });
        root.AddChild(top);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 24);
        top.AddChild(row);
        var title = new Label { Text = "MEM0  /  SPATIAL LAB", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        title.AddThemeColorOverride("font_color", new Color("1c4645"));
        title.AddThemeFontSizeOverride("font_size", 23);
        row.AddChild(title);
        status = new Label { Text = "MANUAL", Modulate = new Color("245c58") };
        row.AddChild(status);
        var panel = new PanelContainer { Position = new Vector2(24, 90), CustomMinimumSize = new Vector2(310, 440), Size = new Vector2(310, 590) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.94f, 0.97f, 0.95f, 0.96f), ContentMarginLeft = 20, ContentMarginRight = 20, ContentMarginTop = 20, ContentMarginBottom = 20 });
        root.AddChild(panel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        panel.AddChild(column);
        var heading = new Label { Text = "ROBOT 01 / MISSION" };
        heading.AddThemeColorOverride("font_color", new Color("244c50"));
        column.AddChild(heading);
        mission = new LineEdit { Text = "Find the red crate and remember its location.", CustomMinimumSize = new Vector2(270, 42) };
        column.AddChild(mission);
        var controls = new HBoxContainer();
        column.AddChild(controls);
        AddButton(controls, "Observe", "Capture one view and execute one decision", () => { if (!busy) _ = StepAsync(); });
        AddButton(controls, "Run", "Run up to 30 autonomous decisions", () =>
        {
            if (busy) return;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            stepCount = 0;
            autonomous = true;
            nextStepAt = 0;
        });
        AddButton(controls, "Stop", "Cancel the pending decision and stop movement", Stop);
        AddButton(column, "Recall Memories", "Load persisted observations without a model request", () => { if (!busy) _ = RecallAsync(); });
        reasoning = new Label { Text = "PAUSED", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(270, 65) };
        reasoning.AddThemeColorOverride("font_color", new Color("244c50"));
        column.AddChild(reasoning);
        memories = new RichTextLabel { Text = "No observations recalled.", SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(270, 260) };
        memories.AddThemeColorOverride("default_color", new Color("244c50"));
        column.AddChild(memories);
        var footer = new PanelContainer();
        footer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
        footer.OffsetTop = -52;
        footer.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("edf3ef"), ContentMarginLeft = 24, ContentMarginTop = 12 });
        root.AddChild(footer);
        pose = new Label();
        pose.AddThemeColorOverride("font_color", new Color("244c50"));
        footer.AddChild(pose);
    }

    private static void AddButton(Container parent, string text, string tooltip, Action action)
    {
        var button = new Button { Text = text, TooltipText = tooltip, CustomMinimumSize = new Vector2(0, 38), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        button.Pressed += action;
        parent.AddChild(button);
    }

    private async Task EnsureBrainAsync(CancellationToken token)
    {
        brain ??= new RobotBrain(ProjectSettings.GlobalizePath("res://"));
        if (!brainReady)
        {
            status.Text = "CONNECTING";
            await brain.InitializeAsync(token);
            brainReady = true;
        }
    }

    private CancellationToken BeginOperation()
    {
        busy = true;
        operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operation.CancelAfter(TimeSpan.FromSeconds(90));
        return operation.Token;
    }

    private void EndOperation()
    {
        operation?.Dispose();
        operation = null;
        busy = false;
    }

    private async Task RecallAsync()
    {
        var token = BeginOperation();
        try
        {
            await EnsureBrainAsync(token);
            var recalled = await brain!.RecallAsync(Point(camera.GlobalPosition), token);
            token.ThrowIfCancellationRequested();
            ShowMemories(recalled);
            status.Text = $"RECALLED {recalled.Count}";
        }
        catch (OperationCanceledException) { if (IsInsideTree()) status.Text = "STOPPED / TIMED OUT"; }
        catch (Exception error) { ReportFailure(error); }
        finally { EndOperation(); }
    }

    private async Task StepAsync()
    {
        if (busy) return;
        var token = BeginOperation();
        lastFailure = "";
        Input.MouseMode = Input.MouseModeEnum.Visible;
        try
        {
            await EnsureBrainAsync(token);
            status.Text = "CAPTURING";
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            token.ThrowIfCancellationRequested();
            var observer = Point(sensorCamera.GlobalPosition);
            var observedAt = DateTimeOffset.UtcNow;
            var capturedTransform = sensorCamera.GlobalTransform;
            using var image = sensor.GetTexture().GetImage();
            var imageBytes = image.SavePngToBuffer();
            var recalled = await brain!.RecallAsync(observer, token);
            token.ThrowIfCancellationRequested();
            ShowMemories(recalled);
            status.Text = "THINKING";
            var decision = await brain.DecideAsync(imageBytes, observer, robot.Rotation.Y, pitch, mission.Text, recalled, token);
            token.ThrowIfCancellationRequested();
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            token.ThrowIfCancellationRequested();
            sensorCamera.GlobalTransform = capturedTransform;
            var observations = new List<SpatialObservation>
            {
                new() { MapId = RobotBrain.MapId, Position = observer, ObserverPosition = observer,
                    Description = decision.Description, ObservedAt = observedAt, Confidence = 0.5 }
            };
            foreach (var detection in decision.Objects.Where(item => item.Confidence >= 0.6))
            {
                var pixel = new Vector2((float)detection.X * sensor.Size.X, (float)detection.Y * sensor.Size.Y);
                var origin = sensorCamera.ProjectRayOrigin(pixel);
                var ray = PhysicsRayQueryParameters3D.Create(origin, origin + sensorCamera.ProjectRayNormal(pixel) * 30);
                ray.Exclude = new Godot.Collections.Array<Rid> { robot.GetRid() };
                var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
                if (hit.Count == 0) continue;
                observations.Add(new SpatialObservation
                {
                    MapId = RobotBrain.MapId, Position = Point((Vector3)hit["position"]), ObserverPosition = observer,
                    Description = detection.Name, Confidence = detection.Confidence, ObservedAt = observedAt
                });
            }
            status.Text = "REMEMBERING";
            foreach (var observation in observations) await brain.RememberAsync(observation, token);
            recalled = await brain.RecallAsync(observer, token);
            token.ThrowIfCancellationRequested();
            ShowMemories(recalled);
            reasoning.Text = $"{decision.Action.ToUpperInvariant()} / {decision.Seconds:F1}s\n{decision.Reason}";
            activeAction = decision.Action;
            actionRemaining = decision.Seconds;
            status.Text = "MOVING";
            while (actionRemaining > 0)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                token.ThrowIfCancellationRequested();
            }
            stepCount++;
            if (stepCount >= 30 || decision.Action == "wait") autonomous = false;
            status.Text = autonomous ? $"RUNNING / {stepCount}" : "PAUSED";
            nextStepAt = Time.GetTicksMsec() + 3000;
        }
        catch (OperationCanceledException)
        {
            autonomous = false;
            actionRemaining = 0;
            if (IsInsideTree()) status.Text = "STOPPED / TIMED OUT";
        }
        catch (Exception error) { ReportFailure(error); }
        finally { EndOperation(); }
    }

    private void ShowMemories(IReadOnlyList<SpatialRecallResult> recalled)
    {
        foreach (var marker in markers) marker.QueueFree();
        markers.Clear();
        memories.Text = $"NEARBY MEMORY / {recalled.Count}\n\n" + string.Join("\n\n", recalled.Select(result =>
            $"{result.Observation.Description}\n{result.Distance:F1} m | {result.Observation.ObservedAt:MM-dd HH:mm} UTC | {result.Observation.Confidence:P0}"));
        foreach (var result in recalled.Where(item => item.Observation.Confidence >= 0.6))
        {
            var point = result.Observation.Position;
            var marker = new MeshInstance3D
            {
                Position = new Vector3((float)point.X, (float)point.Y + 0.15f, (float)point.Z),
                Mesh = new SphereMesh { Radius = 0.09f, Height = 0.18f },
                Layers = 2,
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("f3d96c"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded }
            };
            AddChild(marker);
            markers.Add(marker);
        }
        sensorCamera.CullMask = 1;
    }

    private void Stop()
    {
        autonomous = false;
        actionRemaining = 0;
        operation?.Cancel();
        status.Text = "STOPPED";
    }

    private void ReportFailure(Exception error)
    {
        autonomous = false;
        actionRemaining = 0;
        lastFailure = error is System.ClientModel.ClientResultException apiError
            ? $"OpenAI HTTP {apiError.Status}. Check model access, quota and local configuration."
            : $"{error.GetType().Name}. Check local configuration and PostgreSQL/pgvector availability.";
        if (!IsInsideTree()) return;
        status.Text = "PAUSED / ERROR";
        reasoning.Text = lastFailure;
        GD.Print(lastFailure);
    }

    private async Task SmokeTestAsync(bool live)
    {
        try
        {
            for (var frame = 0; frame < 15; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = sensor.GetTexture().GetImage();
            var colors = new HashSet<Color>();
            for (var pixelY = 0; pixelY < image.GetHeight(); pixelY += 12)
                for (var pixelX = 0; pixelX < image.GetWidth(); pixelX += 12) colors.Add(image.GetPixel(pixelX, pixelY));
            if (colors.Count < 12) throw new InvalidOperationException("Camera render is blank.");
            var directory = ProjectSettings.GlobalizePath("res://artifacts");
            Directory.CreateDirectory(directory);
            image.SavePng(Path.Combine(directory, "robot-camera.png"));
            using var screen = GetViewport().GetTexture().GetImage();
            screen.SavePng(Path.Combine(directory, "robot-lab.png"));
            var initialPosition = robot.Position;
            activeAction = "forward";
            actionRemaining = 0.3;
            while (actionRemaining > 0) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (initialPosition.DistanceTo(robot.Position) < 0.2f) throw new InvalidOperationException("Robot failed to move.");
            robot.Position = new Vector3(0, 0.9f, -11);
            activeAction = "forward";
            actionRemaining = 1;
            while (actionRemaining > 0) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (robot.Position.Z < -11.6f) throw new InvalidOperationException("Robot crossed the wall.");
            robot.Position = initialPosition;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (live)
            {
                await StepAsync();
                if (!string.IsNullOrEmpty(lastFailure)) throw new InvalidOperationException(lastFailure);
                using var restarted = new RobotBrain(ProjectSettings.GlobalizePath("res://"));
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await restarted.InitializeAsync(timeout.Token);
                var recalled = await restarted.RecallAsync(Point(camera.GlobalPosition), timeout.Token);
                if (recalled.Count == 0) throw new InvalidOperationException("Persisted spatial recall was empty.");
                GD.Print($"LIVE PASS: recalled {recalled.Count} observations from a new database client.");
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var liveScreen = GetViewport().GetTexture().GetImage();
                liveScreen.SavePng(Path.Combine(directory, "robot-lab-live.png"));
            }
            GD.Print($"SMOKE PASS: {colors.Count} camera colors; movement and wall collision verified.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.Print($"SMOKE FAIL: {error.GetType().Name}; {(error is InvalidOperationException ? error.Message : lastFailure)}");
            GetTree().Quit(1);
        }
    }

    private static SpatialPoint Point(Vector3 value) => new(value.X, value.Y, value.Z);

    public override void _ExitTree()
    {
        lifetime.Cancel();
        brain?.Dispose();
        lifetime.Dispose();
    }
}