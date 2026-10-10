using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;
using System.Text.Json;

namespace GodotAls.Locomotion;

// Observes real falling through the ordinary Demo owner; never supplies animation poses.
public partial class KimodoAirDiagnostic : Node
{
    private AlsDemoEntry _entry = null!;
    private int _hz, _tick, _dropTick, _air, _fast, _partialFlail, _prediction, _landed;
    private string _output = null!;
    private string? _capture;
    private string? _pending;
    private int _pendingTick;
    private bool _done;
    private readonly List<object> _rows = [], _captures = [];
    private Camera3D? _camera;
    private Label? _label;
    private int? _previousMaxSteps;
    private float _groundY;

    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _hz = int.Parse(args.FirstOrDefault(a => a.StartsWith("--hz="))?[5..] ?? "60");
            Require(_hz is 30 or 60 or 120, "Unsupported physics frequency.");
            Engine.PhysicsTicksPerSecond = _hz;
            _output = args.Single(a => a.StartsWith("--air-output="))[13..];
            _capture = args.FirstOrDefault(a => a.StartsWith("--capture-dir="))?[14..];
            _entry = new AlsDemoEntry { ConfigureBeforeReady = demo => demo.ConfigureRuntimePolicyForSmoke(
                args.Contains("--parallel") ? AlsHarnessMode.Parallel : AlsHarnessMode.Single, true) };
            AddChild(_entry);
            Require(_entry.Demo.IsRuntimeReady, "Ordinary Demo initialization failed.");
            _entry.Demo.OrbitCamera.SetMouseCaptured(false);
            // Isolate normal Fall/landing prediction; separate existing tests cover impact Ragdoll.
            _entry.Demo.RuntimeContext.AutomaticRagdollEnvironment = null;
            var world = _entry.Demo.GetNode<Node3D>("World");
            foreach (var child in world.GetChildren()) if (child.Name != "StartFloor") Disable(child);
            var floor = world.GetNode<StaticBody3D>("StartFloor");
            floor.GetNode<CollisionShape3D>("CollisionShape3D").Position = Vector3.Zero;
            floor.GetNode<CollisionShape3D>("CollisionShape3D").Shape = new BoxShape3D { Size = new(300, .5f, 300) };
            RollingGameplaySmoke.PlaceOnOpenFloor(_entry.Demo);
            _groundY = _entry.Demo.ActiveCharacter.MovementAnchor.GlobalPosition.Y;
            if (_capture is not null)
            {
                Require(DisplayServer.GetName() != "headless", "Capture requires rendering.");
                Directory.CreateDirectory(_capture);
                _previousMaxSteps = Engine.MaxPhysicsStepsPerFrame;
                Engine.MaxPhysicsStepsPerFrame = 1;
                RenderingServer.ViewportSetUpdateMode(GetViewport().GetViewportRid(), RenderingServer.ViewportUpdateMode.Always);
                _camera = new Camera3D { Fov = 45 }; AddChild(_camera); _camera.MakeCurrent();
                var canvas = new CanvasLayer(); AddChild(canvas);
                _label = new Label { Position = new(18, 220) }; canvas.AddChild(_label);
                RenderingServer.FramePostDraw += Capture;
            }
            ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
            ProcessThreadGroupOrder = AlsP3FrameStages.Observe + 1;
        }
        catch (Exception e) { Fail(e); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done) return;
        try
        {
            ++_tick;
            var demo = _entry.Demo; var character = demo.ActiveCharacter;
            Require(demo.ErrorCount == 0 && character.FailureDiagnosticCount == 0 && !character.IsPoseFrozen, "Production frame failed.");
            var input = character.LatestMotorInput; var full = character.FullMovementDiagnostics;
            Require(input.Identity == full.Identity && full.Identity.FrameId == character.RuntimeCommittedFrameId, "Motor/pose identity differs.");
            var skeleton = character.PhysicalDisplaySkeleton;
            var bones = new Dictionary<string, object>();
            foreach (var name in new[] { "root", "pelvis", "neck_01", "hand_l", "hand_r", "foot_l", "foot_r" })
            {
                var bone = skeleton.GetBoneGlobalPose(skeleton.FindBone(name));
                var p = skeleton.GlobalTransform * bone.Origin;
                bones[name] = new { component = V(bone.Origin), world = V(p) };
            }
            Vector3 Point(string name) => skeleton.GlobalBasis * skeleton.GetBoneGlobalPose(skeleton.FindBone(name)).Origin;
            var pelvis = Point("pelvis") - Point("root"); var torso = Point("neck_01") - Point("pelvis");
            var tilt = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(torso.Normalized().Y, -1, 1)));
            var horizontal = new Vector2(pelvis.X, pelvis.Z).Length();
            var airborne = input.Floor.IsGrounded == 0;
            if (_dropTick != 0 && airborne)
            {
                _air++;
                if (full.Global.FallSpeed < -10) _fast++;
                if (full.Global.FallSpeed is < -7 and > -25 && full.Global.LandPrediction < .1f) _partialFlail++;
                if (full.Global.LandPrediction > .05f) _prediction++;
                // Check the committed steady Fall, including the partial Flail range.
                if (_tick > _dropTick + _hz / 3 && full.Global.LandPrediction < .1f)
                    Require(tilt <= 35 && pelvis.Y is >= .75f and <= 1.2f && horizontal <= .15f,
                        $"Invalid final airborne body: tilt={tilt:F2}, height={pelvis.Y:F3}, planar={horizontal:F3}.");
            }
            if (_dropTick != 0 && !airborne && _air > 0) _landed++;
            _rows.Add(new { tick = _tick, committed = character.RuntimeCommittedFrameId, presented = character.PublishedFrameId,
                identity = input.Identity, airborne, actorPosition = V(character.MovementAnchor.GlobalPosition),
                velocity = new[] { input.ActualVelocity.X, input.ActualVelocity.Y, input.ActualVelocity.Z },
                fallSpeed = full.Global.FallSpeed, landPrediction = full.Global.LandPrediction,
                pelvisHeight = pelvis.Y, planarPelvis = horizontal, torsoTiltDegrees = tilt, bones });
            if (_camera is not null)
            {
                var p = character.MovementAnchor.GlobalPosition;
                _camera.GlobalPosition = p + new Vector3(3.2f, 1.8f, 3.2f); _camera.LookAt(p + Vector3.Up * .2f);
                _label!.Text = $"Air / {_hz} Hz | fall {full.Global.FallSpeed:F1} m/s | prediction {full.Global.LandPrediction:F2}\nTorso {tilt:F1} deg | pelvis {pelvis.Y:F2} m | frame {character.RuntimeCommittedFrameId}";
                if (_dropTick != 0 && (_tick - _dropTick) % (_hz / 4) == 0 && _landed < _hz / 2)
                { _pending = "air-" + _tick; _pendingTick = _tick; }
            }
            if (_dropTick == 0 && _tick >= _hz / 2)
            {
                Require(!airborne, "Warmup did not reach ground.");
                var motor = (AlsCharacterMotor)character.MovementAnchor;
                motor.GlobalPosition = new(10, _groundY + 30, 10); motor.Velocity = Vector3.Zero;
                _dropTick = _tick;
                Input.ActionPress("move_forward");
            }
            if (_landed >= _hz)
            {
                Require(_air >= _hz * 2 && _fast > 0 && _partialFlail > 0 && _prediction > 0, "Air branch input coverage incomplete.");
                Require(MathF.Abs(character.MovementAnchor.GlobalPosition.Y - _groundY) < .05f, "Capsule did not return to the floor.");
                Save(true); _done = true;
                GD.Print($"KIMODO_AIR_DIAGNOSTIC_OK hz={_hz} frames={_tick} air={_air} fast={_fast} partial_flail_inputs={_partialFlail} prediction={_prediction} physics=real entry=ordinary");
                GetTree().Quit();
            }
            Require(_tick < _hz * 9, "Fall/landing timed out.");
        }
        catch (Exception e) { Fail(e); }
    }
    private void Capture()
    {
        if (_pending is null || _done) return;
        var character = _entry.Demo.ActiveCharacter;
        if (_tick != _pendingTick || character.RuntimeCommittedFrameId != _pendingTick || character.PublishedFrameId != _pendingTick)
        { Fail(new Exception("Capture did not present the requested committed frame.")); return; }
        var path = Path.Combine(_capture!, _pending + ".png");
        using var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(path) != Error.Ok) { Fail(new Exception("PNG save failed.")); return; }
        _captures.Add(new { tick = _pendingTick, committed = character.RuntimeCommittedFrameId, presented = character.PublishedFrameId, screenshot = path });
        _pending = null;
    }
    private void Save(bool passed, string? failure = null) => File.WriteAllText(_output, JsonSerializer.Serialize(new {
        passed, failure, hz = _hz, resourcePack = System.Environment.GetEnvironmentVariable("ALS_RESOURCE_PACK_PATH"),
        frames = _rows, captures = _captures, airFrames = _air, fastFrames = _fast,
        partialFlailInputFrames = _partialFlail, predictionFrames = _prediction, landedFrames = _landed
    }, new JsonSerializerOptions { IncludeFields = true }));
    private static float[] V(Vector3 p) => [p.X, p.Y, p.Z];
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private static void Disable(Node node) { if (node is Node3D spatial) spatial.Hide(); if (node is CollisionObject3D body) { body.CollisionLayer = 0; body.CollisionMask = 0; } foreach (var child in node.GetChildren()) Disable(child); }
    private void Fail(Exception e) { _done = true; if (_output is not null) Save(false, e.ToString()); GD.PushError(e.ToString()); GetTree().Quit(1); }
    public override void _ExitTree() { Input.ActionRelease("move_forward"); Input.MouseMode = Input.MouseModeEnum.Visible; if (_capture is not null) RenderingServer.FramePostDraw -= Capture; if (_previousMaxSteps.HasValue) Engine.MaxPhysicsStepsPerFrame = _previousMaxSteps.Value; }
}
