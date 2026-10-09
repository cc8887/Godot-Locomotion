using Godot;
using GodotAls.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;
using System.Text.Json;

namespace GodotAls.Locomotion;

// Observe the ordinary player pipeline through start, held input, release and idle.
// The production camera and terrain are retained unless --flat-floor is explicit.
public partial class AlsMovementReleaseDiagnostic : Node
{
    private AlsDemoEntry _entry = null!;
    private readonly List<object> _rows = [];
    private readonly List<object> _captures = [];
    private string _output = null!, _case = "forward";
    private string? _capture;
    private readonly HashSet<int> _captureTicks = [];
    private string[] _actions = [];
    private int _tick, _lastTick = 600;
    private int _pressTick = 30, _releaseTick = 210, _actualReleaseTick;
    private string _mode = "run", _releaseSupport = "scheduled";
    private bool _collectViolations;
    private readonly List<object> _violations = [];
    private bool _done;
    private int _pending;
    private bool _flat;
    private bool _verify;
    private int _unsupported;
    private AlsSoleGeometryObserver _sole = null!;
    private StaticBody3D _floor = null!;
    private Label? _label;

    public override void _Ready()
    {
        try
        {
            Engine.PhysicsTicksPerSecond = 60;
            var args = OS.GetCmdlineUserArgs();
            _output = args.Single(a => a.StartsWith("--release-output="))[17..];
            _case = args.FirstOrDefault(a => a.StartsWith("--release-case="))?[15..] ?? "forward";
            string? Option(string name) => args.FirstOrDefault(a => a.StartsWith(name + "="))?[(name.Length + 1)..];
            _mode = Option("--release-mode") ?? "run";
            _releaseSupport = Option("--release-support") ?? "scheduled";
            _pressTick = int.Parse(Option("--press-tick") ?? "30");
            _releaseTick = int.Parse(Option("--release-tick") ?? "210");
            _lastTick = int.Parse(Option("--last-tick") ?? "600");
            if (_mode is not ("walk" or "run" or "crouch") ||
                _releaseSupport is not ("scheduled" or "left-lock" or "right-lock" or "left-plant" or "right-plant") ||
                _pressTick < 5 || _releaseTick <= _pressTick || _lastTick <= _releaseTick + 120)
                throw new ArgumentException("Invalid release mode, support window or tick schedule");
            _actions = _case switch
            {
                "forward" => ["move_forward"], "back" => ["move_back"],
                "left" => ["move_left"], "right" => ["move_right"],
                "left-forward" => ["move_left", "move_forward"],
                "right-forward" => ["move_right", "move_forward"],
                "left-back" => ["move_left", "move_back"],
                "right-back" => ["move_right", "move_back"],
                _ => throw new ArgumentException("Unknown release case")
            };
            _flat = args.Contains("--flat-floor");
            _verify = args.Contains("--verify-release");
            _collectViolations = args.Contains("--collect-release-violations");
            if (_collectViolations && !_verify) throw new ArgumentException("Collecting violations requires verification");
            _capture = args.FirstOrDefault(a => a.StartsWith("--capture-dir="))?[14..];
            _entry = new AlsDemoEntry { ConfigureBeforeReady = demo => demo.ConfigureRuntimePolicyForSmoke(
                args.Contains("--parallel") ? AlsHarnessMode.Parallel : AlsHarnessMode.Single, true) };
            AddChild(_entry);
            if (!_entry.Demo.IsRuntimeReady) throw new Exception("Ordinary entry failed");
            var world = _entry.Demo.GetNode<Node3D>("World");
            _floor = world.GetNode<StaticBody3D>("StartFloor");
            if (_flat)
            {
                foreach (var child in world.GetChildren()) if (child != _floor) Disable(child);
                var shape = _floor.GetNode<CollisionShape3D>("CollisionShape3D");
                shape.Position = Vector3.Zero; shape.Shape = new BoxShape3D { Size = new(300, .5f, 300) };
                var mesh = _floor.GetNode<MeshInstance3D>("MeshInstance3D");
                mesh.Position = new(0, .25f, 0); mesh.Scale = Vector3.One; mesh.Mesh = new PlaneMesh { Size = new(300, 300) };
            }
            _sole = new(_entry.Demo.ActiveCharacter.PhysicalDisplaySkeleton.GetParent());
            var requestedTicks = args.FirstOrDefault(a => a.StartsWith("--capture-ticks="))?[16..];
            foreach (var tick in (requestedTicks ?? "29,31,35,40,45,60,90,150,209,211,220,230,240,250,270,330,450,599").Split(','))
                _captureTicks.Add(int.Parse(tick));
            if (_capture is not null)
            {
                if (DisplayServer.GetName() == "headless") throw new Exception("Rendering required for screenshots");
                Directory.CreateDirectory(_capture);
                var canvas = new CanvasLayer(); AddChild(canvas);
                _label = new Label { Position = new(740, 18) }; canvas.AddChild(_label);
                RenderingServer.FramePostDraw += Capture;
            }
            ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
            ProcessThreadGroupOrder = AlsP3FrameStages.Observe + 1;
            GD.Print($"ALS_RELEASE_PROBE pack={GodotAls.Import.AlsAnimationResourcePack.ConfigRoot} root={AlsAnimationRuntimeOptions.RootMotionEnabled} sampleRoot={AlsAnimationRuntimeOptions.SampleRootMotion} case={_case} mode={_mode} support={_releaseSupport} flat={_flat}");
        }
        catch (Exception e) { Fail(e); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done) return;
        try
        {
            var character = _entry.Demo.ActiveCharacter;
            if (_entry.Demo.ErrorCount != 0 || character.FailureDiagnosticCount != 0 || character.IsPoseFrozen)
                throw new Exception("Production frame failed");
            ++_tick;
            if (_tick == 1 && _mode == "walk") Input.ActionPress("walk");
            if (_tick == 1 && _mode == "crouch") Input.ActionPress("crouch_toggle");
            if (_tick == 3) Input.ActionRelease("crouch_toggle");
            if (_tick == _pressTick) foreach (var action in _actions) Input.ActionPress(action);
            var input = character.LatestMotorInput; var full = character.FullMovementDiagnostics;
            var frame = character.Diagnostics.Result; var motor = (AlsCharacterMotor)character.MovementAnchor;
            // Feedback belongs to this committed pose. The ordinary input owner
            // consumes the release next tick; record both boundaries explicitly.
            var planted = full.Feedback.FeetPosition;
            var stopSelection = !planted.Present ? "absent" :
                AlsRefactoredStopRule.LockRight.Evaluate(planted.Value) ? "right-lock" :
                AlsRefactoredStopRule.LockLeft.Evaluate(planted.Value) ? "left-lock" :
                AlsRefactoredStopRule.PlantRight.Evaluate(planted.Value) ? "right-plant" : "left-plant";
            if (_actualReleaseTick == 0 && _tick >= _releaseTick &&
                (_releaseSupport == "scheduled" || stopSelection == _releaseSupport))
            {
                _actualReleaseTick = _tick; Release();
                if (_releaseSupport != "scheduled")
                    foreach (var offset in new[] { 0, 1, 4, 8, 16, 30, 60, 120 }) _captureTicks.Add(_tick + offset);
            }
            if (_actualReleaseTick == 0 && _tick == _releaseTick + 180)
            {
                Check(false, $"Requested support window {_releaseSupport} absent during 180 held frames");
                _actualReleaseTick = _tick; Release();
            }
            var support = _sole.CaptureQuality(full.Identity, (long)_floor.GetInstanceId(), _floor.GlobalTransform, .25f,
                full.RefactoredRig.Left.Location.ContactApplied, full.RefactoredRig.Right.Location.ContactApplied);
            var boneSamples = Bones(character);
            _rows.Add(new { tick = _tick, identity = full.Identity, dt = input.DeltaTime,
                presentationPending = character.Diagnostics.PresentationPending,
                visualVisible = character.Visible, visualReady = character.LifecycleDiagnostics.IsVisualReady,
                skeletonVisible = character.PhysicalDisplaySkeleton.IsVisibleInTree(),
                committed = character.RuntimeCommittedFrameId, publishedInput = character.PublishedFrameId,
                phase = _tick <= _pressTick ? "initial-idle" : _actualReleaseTick == 0 || _tick <= _actualReleaseTick ? "held" : "released",
                mode = _mode, animationPhase = character.RuntimeAnimationPhase,
                footPlanted = planted, stopSelectionFromCurrentCurve = stopSelection,
                feedbackIdentity = full.Feedback.Identity, movementState = full.MovementState,
                motion = full.RefactoredMotion, lockProducers = full.LockProducers,
                rawLockProducers = full.RawLockProducers, rig = full.RefactoredRig,
                inputAxes = new[] { input.Command.MovementAxes.X, input.Command.MovementAxes.Y },
                inputHeld = input.Command.MovementAxes.LengthSquared() > 0,
                position = V(motor.GlobalPosition), velocity = new[] { input.ActualVelocity.X, input.ActualVelocity.Y, input.ActualVelocity.Z },
                worldVelocity = V(motor.WorldMovementVelocity), desiredSpeed = input.DesiredSpeed,
                grounded = input.Floor.IsGrounded, collisions = motor.GetSlideCollisionCount(),
                drive = input.CurrentDriveMode.ToString(), rootEnabled = AlsAnimationRuntimeOptions.RootMotionEnabled,
                sampleRoot = AlsAnimationRuntimeOptions.SampleRootMotion,
                rootSource = frame.RootMotionSource.HasMotion, consumedRoot = character.ConsumedRootMotion.HasMotion,
                proposedRoot = frame.ProposedRootMotionDelta, consumedDelta = character.RootMotionWorldDelta,
                stance = frame.ActualStance.ToString(), gait = frame.ActualGait.ToString(),
                pelvisOffsetMeters = full.RefactoredRig.PelvisOffset * .01f,
                locks = full.RefactoredLocks, preFoot = full.RefactoredRigFeet,
                bones = boneSamples, soles = support, ground = full.Ground });
            if (_verify)
            {
                // Match the existing production/native cold-presentation contract:
                // frame one commits history while hidden; frame two must reveal a
                // complete rig. Hidden skin is still recorded, never called rendered.
                var cold = _tick == 1 && full.UsesRefactoredFeet;
                var legalPresentation = character.Diagnostics.PresentationPending == cold &&
                    character.Visible == !cold && character.LifecycleDiagnostics.IsVisible == !cold &&
                    character.LifecycleDiagnostics.IsVisualReady == !cold &&
                    character.PhysicalDisplaySkeleton.IsVisibleInTree() == !cold &&
                    full.RefactoredRigFeet.LocationEvaluated == !cold;
                Check(legalPresentation, $"Cold/warm presentation contract violated: tick={_tick}, cold={cold}");
                var legalHiddenColdPose = cold && legalPresentation;
                if (AlsAnimationRuntimeOptions.RootMotionEnabled || AlsAnimationRuntimeOptions.SampleRootMotion ||
                    input.CurrentDriveMode != AlsDriveMode.MotorDriven || frame.RootMotionSource.HasMotion ||
                    character.ConsumedRootMotion.HasMotion || frame.ProposedRootMotionDelta != AlsRootMotionDelta.Identity ||
                    character.RootMotionWorldDelta != AlsRootMotionDelta.Identity)
                    Check(false, "Root motion affected the disabled ordinary movement path");
                var skeleton = character.PhysicalDisplaySkeleton;
                if (skeleton.GetBonePosePosition(skeleton.FindBone("root")).LengthSquared() > 1e-8f)
                    Check(false, "Disabled animated root still translated");
                var offset = skeleton.GlobalBasis * (skeleton.GetBoneGlobalPose(skeleton.FindBone("pelvis")).Origin-
                    skeleton.GetBoneGlobalPose(skeleton.FindBone("root")).Origin);
                if (new Vector2(offset.X, offset.Z).Length() > .5f)
                    Check(false, "Body detached during start, held input or release");
                if (_flat)
                {
                    var height = Math.Min(support.Left.MinimumSupportHeight, support.Right.MinimumSupportHeight);
                    if (!legalHiddenColdPose && height < -2) Check(false, $"Rendered sole penetrated the flat floor: tick={_tick}, height={height:F3} cm");
                    _unsupported = !legalHiddenColdPose && height > 5 ? _unsupported + 1 : 0;
                    if (_unsupported >= 30) Check(false, "Both rendered soles float for 0.5s during start or release");
                }
            }
            if (_label is not null)
            {
                var p = motor.GlobalPosition;
                _label.Text = $"{_mode} / {_case} | {(input.Command.MovementAxes.LengthSquared() > 0 ? "HELD" : "RELEASED / IDLE")} | tick {_tick}\nCapsule ({p.X:F2}, {p.Y:F2}, {p.Z:F2})\nSpeed {motor.Velocity.Length():F2} m/s | pelvis {full.RefactoredRig.PelvisOffset * .01f:F3} m\nSupport {planted.Value:F2} | rule {stopSelection}";
                if (_captureTicks.Contains(_tick)) _pending = _tick;
            }
            if (_tick < _lastTick) return;
            Check(_actualReleaseTick != 0, "Movement input never released");
            Save(); GD.Print($"{(_violations.Count != 0 ? "ALS_MOVEMENT_RELEASE_FAILED" : _verify ? "ALS_MOVEMENT_RELEASE_OK" : "ALS_MOVEMENT_RELEASE_DIAGNOSTIC_OK")} frames={_rows.Count} violations={_violations.Count} output={_output}");
            _done = true; GetTree().Quit(_violations.Count == 0 ? 0 : 1);
        }
        catch (Exception e) { Fail(e); }
    }

    private static Dictionary<string, object> Bones(AlsP3Character character)
    {
        var skeleton = character.PhysicalDisplaySkeleton; var result = new Dictionary<string, object>();
        foreach (var name in new[] { "root", "pelvis", "thigh_l", "calf_l", "foot_l", "ball_l", "thigh_r", "calf_r", "foot_r", "ball_r" })
        {
            var index = skeleton.FindBone(name); var component = skeleton.GetBoneGlobalPose(index);
            result[name] = new { world = V(skeleton.GlobalTransform * component.Origin), component = V(component.Origin),
                local = V(skeleton.GetBonePosePosition(index)) };
        }
        return result;
    }
    private void Capture()
    {
        if (_pending == 0) return;
        var character = _entry.Demo.ActiveCharacter;
        var path = Path.Combine(_capture!, $"{_case}-{_tick}.png");
        using var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(path) != Error.Ok) throw new Exception("PNG failed");
        _captures.Add(new { requestedTick = _pending, observedTick = _tick,
            committed = character.RuntimeCommittedFrameId, presented = character.PublishedFrameId,
            presentationPending = character.Diagnostics.PresentationPending,
            visualVisible = character.Visible, visualReady = character.LifecycleDiagnostics.IsVisualReady,
            skeletonVisible = character.PhysicalDisplaySkeleton.IsVisibleInTree(),
            inputAxes = new[] { character.LatestMotorInput.Command.MovementAxes.X, character.LatestMotorInput.Command.MovementAxes.Y },
            inputHeld = character.LatestMotorInput.Command.MovementAxes.LengthSquared() > 0,
            label = _label!.Text,
            screenshot = path, bones = Bones(character) });
        _pending = 0;
    }
    private void Save() => File.WriteAllText(_output, JsonSerializer.Serialize(new {
        pack = GodotAls.Import.AlsAnimationResourcePack.ConfigRoot, root = AlsAnimationRuntimeOptions.RootMotionEnabled,
        sampleRoot = AlsAnimationRuntimeOptions.SampleRootMotion, scenario = _case, flatFloor = _flat,
        mode = _mode, pressTick = _pressTick, earliestReleaseTick = _releaseTick,
        actualReleaseTick = _actualReleaseTick, releaseSupport = _releaseSupport, lastTick = _lastTick,
        captureTicks = _captureTicks.Order().ToArray(), violations = _violations,
        passed = _violations.Count == 0 && _rows.Count == _lastTick,
        frames = _rows, captures = _captures }, new JsonSerializerOptions { IncludeFields = true }));
    private void Check(bool passed, string reason)
    {
        if (passed) return;
        _violations.Add(new { tick = _tick, reason });
        if (!_collectViolations) throw new Exception(reason);
    }
    private static float[] V(Vector3 p) => [p.X, p.Y, p.Z];
    private static void Release() { foreach (var action in new[] { "move_forward", "move_back", "move_left", "move_right" }) Input.ActionRelease(action); }
    private static void Disable(Node node) { if (node is Node3D spatial) spatial.Hide(); if (node is CollisionObject3D body) { body.CollisionLayer = 0; body.CollisionMask = 0; } foreach (var child in node.GetChildren()) Disable(child); }
    private void Fail(Exception e) { _done = true; if (_output is not null) Save(); GD.PushError(e.ToString()); GetTree().Quit(1); }
    public override void _ExitTree() { Release(); Input.ActionRelease("walk"); Input.ActionRelease("crouch_toggle"); if (_capture is not null) RenderingServer.FramePostDraw -= Capture; }
}
