using System.Globalization;
using System.Text.Json;
using Godot;
using GodotAls.Dispatch;

namespace GodotAls.Locomotion;

// Observes the ordinary owner with automatic landing actions retained by default.
// Completion means a captured diagnostic, not animation quality acceptance.
public partial class KimodoDropHeightDiagnostic : Node
{
    private AlsDemoEntry _entry = null!;
    private int _hz, _tick, _dropTick, _impactTick;
    private float _height, _groundY;
    private string _output = "";
    private string? _capture;
    private bool _done, _move, _isolate;
    private int _pendingTick;
    private Camera3D? _camera;
    private int? _previousMaxSteps;
    private readonly List<object> _frames = [], _captures = [];

    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _hz = int.Parse(args.FirstOrDefault(a => a.StartsWith("--hz="))?[5..] ?? "60");
            _height = float.Parse(args.Single(a => a.StartsWith("--drop-height="))[14..], CultureInfo.InvariantCulture);
            _output = args.Single(a => a.StartsWith("--output="))[9..];
            _capture = args.FirstOrDefault(a => a.StartsWith("--capture-dir="))?[14..];
            _move = args.Contains("--move"); _isolate = args.Contains("--isolate-air");
            Require(_hz is 30 or 60 or 120 && float.IsFinite(_height) && _height is > 0 and <= 40, "Invalid drop parameters.");
            Engine.PhysicsTicksPerSecond = _hz;
            _entry = new AlsDemoEntry { ConfigureBeforeReady = d => d.ConfigureRuntimePolicyForSmoke(
                args.Contains("--parallel") ? AlsHarnessMode.Parallel : AlsHarnessMode.Single, true) };
            AddChild(_entry);
            Require(_entry.Demo.IsRuntimeReady, "Ordinary Demo initialization failed.");
            _entry.Demo.OrbitCamera.SetMouseCaptured(false);
            if (_isolate) _entry.Demo.RuntimeContext.AutomaticRagdollEnvironment = null;
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
                _previousMaxSteps = Engine.MaxPhysicsStepsPerFrame; Engine.MaxPhysicsStepsPerFrame = 1;
                _camera = new Camera3D { Fov = 45 }; AddChild(_camera); _camera.MakeCurrent();
                RenderingServer.ViewportSetUpdateMode(GetViewport().GetViewportRid(), RenderingServer.ViewportUpdateMode.Always);
                RenderingServer.FramePostDraw += Capture;
            }
            ProcessThreadGroup = ProcessThreadGroupEnum.MainThread; ProcessThreadGroupOrder = AlsP3FrameStages.Observe + 1;
        }
        catch (Exception e) { Fail(e); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done) return;
        try
        {
            ++_tick;
            var character = _entry.Demo.ActiveCharacter;
            Require(_entry.Demo.ErrorCount == 0 && character.FailureDiagnosticCount == 0 && !character.IsPoseFrozen, "Production frame failed.");
            var input = character.LatestMotorInput; var full = character.FullMovementDiagnostics; var result = character.Diagnostics.Result;
            Require(input.Identity == full.Identity && full.Identity.FrameId == character.RuntimeCommittedFrameId, "Motor/pose identity differs.");
            var skeleton = character.PhysicalDisplaySkeleton;
            var bones = new Dictionary<string, object>();
            foreach (var name in new[] { "root", "pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "head", "upperarm_l", "upperarm_r", "hand_l", "hand_r", "thigh_l", "thigh_r", "calf_l", "calf_r", "foot_l", "foot_r" })
            {
                var id = skeleton.FindBone(name); var bone = skeleton.GetBoneGlobalPose(id); var rotation = skeleton.GetBonePoseRotation(id);
                bones[name] = new { component = V(bone.Origin), world = V(skeleton.GlobalTransform * bone.Origin), rotation = new[] { rotation.X, rotation.Y, rotation.Z, rotation.W } };
            }
            if (_dropTick != 0 && _impactTick == 0 && (input.Floor.IsGrounded == 1 || character.PhysicsDriven)) _impactTick = _tick;
            var graph = full.GraphCapture;
            _frames.Add(new { tick = _tick, committed = character.RuntimeCommittedFrameId, presented = character.PublishedFrameId,
                airborne = input.Floor.IsGrounded == 0, physicsDriven = character.PhysicsDriven,
                state = result.ResolvedLocomotionState.ToString(), movementState = full.MovementState,
                action = result.MovementAction, rolling = result.Rolling, ragdoll = full.Ragdoll,
                actorPosition = V(character.MovementAnchor.GlobalPosition), velocity = V(new(input.ActualVelocity.X, input.ActualVelocity.Y, input.ActualVelocity.Z)),
                fallSpeed = full.Global.FallSpeed, landPrediction = full.Global.LandPrediction,
                prediction = full.RefactoredPrediction, rig = full.RefactoredRig, locks = full.RefactoredLocks, bones,
                graph = graph?.Identity == input.Identity ? graph.Row : null, graphNames = graph?.Names });
            if (_camera is not null)
            {
                var pelvis = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(skeleton.FindBone("pelvis")).Origin;
                _camera.GlobalPosition = pelvis + new Vector3(3.1f, .8f, 3.1f); _camera.LookAt(pelvis);
                if (_dropTick != 0 && (_tick - _dropTick) % (_hz / 10) == 0) _pendingTick = _tick;
            }
            if (_dropTick == 0 && _tick >= _hz / 2)
            {
                Require(input.Floor.IsGrounded == 1, "Warmup did not reach ground.");
                var motor = (AlsCharacterMotor)character.MovementAnchor;
                motor.GlobalPosition = new(10, _groundY + _height, 10); motor.Velocity = Vector3.Zero;
                _dropTick = _tick;
                if (_move) Input.ActionPress("move_forward");
            }
            if (_impactTick != 0 && _tick >= _impactTick + _hz * 2)
            { Save(true); _done = true; GD.Print($"KIMODO_DROP_CAPTURE_COMPLETE height={_height} hz={_hz} impact={_impactTick} frames={_tick} automatic={!_isolate}"); GetTree().Quit(); }
            Require(_tick < _hz * 12, "Drop observation timed out.");
        }
        catch (Exception e) { Fail(e); }
    }

    private void Capture()
    {
        if (_done || _pendingTick == 0) return;
        var character = _entry.Demo.ActiveCharacter;
        Require(_tick == _pendingTick && character.RuntimeCommittedFrameId == _pendingTick && character.PublishedFrameId == _pendingTick, "Capture identity differs.");
        var path = Path.Combine(_capture!, "drop-" + _pendingTick + ".png");
        using var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(path) != Error.Ok) { Fail(new Exception("PNG save failed.")); return; }
        _captures.Add(new { tick = _pendingTick, committed = character.RuntimeCommittedFrameId, presented = character.PublishedFrameId, screenshot = path });
        _pendingTick = 0;
    }
    private void Save(bool completed, string? failure = null) => File.WriteAllText(_output, JsonSerializer.Serialize(new {
        completed, failure, hz = _hz, height = _height, moving = _move, automaticRagdoll = !_isolate, groundY = _groundY,
        resourcePack = System.Environment.GetEnvironmentVariable("ALS_RESOURCE_PACK_PATH"), impactTick = _impactTick,
        frames = _frames, captures = _captures, qualityAccepted = false
    }, new JsonSerializerOptions { IncludeFields = true }));
    private void Fail(Exception e) { _done = true; if (_output.Length > 0) Save(false, e.ToString()); GD.PushError(e.ToString()); GetTree().Quit(1); }
    private static float[] V(Vector3 p) => [p.X, p.Y, p.Z];
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private static void Disable(Node node) { if (node is Node3D spatial) spatial.Hide(); if (node is CollisionObject3D body) { body.CollisionLayer = 0; body.CollisionMask = 0; } foreach (var child in node.GetChildren()) Disable(child); }
    public override void _ExitTree() { Input.ActionRelease("move_forward"); Input.MouseMode = Input.MouseModeEnum.Visible; if (_capture is not null) RenderingServer.FramePostDraw -= Capture; if (_previousMaxSteps.HasValue) Engine.MaxPhysicsStepsPerFrame = _previousMaxSteps.Value; }
}
