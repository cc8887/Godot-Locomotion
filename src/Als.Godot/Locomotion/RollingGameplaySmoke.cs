using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Diagnostics;
using GodotAls.Animation;
using GodotAls.Dispatch;

namespace GodotAls.Locomotion;

// Runs real R edges through the ordinary demo. Changes of input after acceptance
// must not change the stored roll target; the motor checks the committed state.
public partial class RollingGameplaySmoke : Node
{
    private P4LocomotionDemo _demo = null!;
    private int _hz = 60, _stalls, _accepted, _busy, _completed, _rollFrames, _turnFrames;
    private long _injected, _committed;
    private bool _done;
    private AlsRollingState _previousRoll;
    private float _previousYaw;
    private Vector3 _start;
    private Input.MouseModeEnum _oldMouse;
    private bool _oldAccumulation;
    private ulong _digest = AlsResultDigest.OffsetBasis;
    private Camera3D? _camera;
    private int _nextCapture;
    private string? _captureDirectory;

    public RollingGameplaySmoke()
    { ProcessThreadGroup = ProcessThreadGroupEnum.MainThread; ProcessThreadGroupOrder = -2; }

    public override void _Ready()
    {
        try
        {
            var arg = OS.GetCmdlineUserArgs().SingleOrDefault(a => a.StartsWith("--hz="));
            if (arg is not null) _hz = int.Parse(arg[5..]);
            Require(_hz is 30 or 60 or 120, "Expected 30/60/120 Hz."); Engine.PhysicsTicksPerSecond = _hz;
            _oldMouse = Input.MouseMode; _oldAccumulation = Input.UseAccumulatedInput; Input.UseAccumulatedInput = false;
            if (OS.GetCmdlineUserArgs().Contains("--single"))
            {
                AlsAnimationRuntimeOptions.ConfigureDemo();
                _demo = ResourceLoader.Load<PackedScene>("res://scenes/demo/p4_locomotion_demo.tscn").Instantiate<P4LocomotionDemo>();
                _demo.ConfigureRuntimePolicyForSmoke(AlsHarnessMode.Single, true); AddChild(_demo);
            }
            else
            {
                var scene = ResourceLoader.Load<PackedScene>(ProjectSettings.GetSetting("application/run/main_scene").AsString());
                var entry = scene.Instantiate<AlsDemoEntry>(); AddChild(entry); _demo = entry.Demo;
            }
            Require(_demo.IsRuntimeReady, "Normal demo did not initialize.");
            _demo.ActiveCharacter.MovementAnchor.GlobalPosition = _start = new(10, 1, 10);
            var capture = OS.GetCmdlineUserArgs().SingleOrDefault(a => a.StartsWith("--capture-dir="));
            if (capture is not null)
            {
                Require(DisplayServer.GetName() != "headless", "Capture requires a renderer.");
                _captureDirectory = ProjectSettings.GlobalizePath(capture[14..]); Directory.CreateDirectory(_captureDirectory);
                _camera = new Camera3D { Fov = 50 }; AddChild(_camera); _camera.MakeCurrent();
                var light = new OmniLight3D { Position = _start + new Vector3(0,4,2), OmniRange = 15, LightEnergy = 4 }; AddChild(light);
                _nextCapture = _hz / 2; RenderingServer.FramePostDraw += Capture;
            }
            _demo.RuntimeContext.ActionOutcomeCommitted += (_, outcome) =>
            {
                switch (outcome.ResultCode)
                {
                    case AlsActionResultCode.Accepted: _accepted++; break;
                    case AlsActionResultCode.RejectedBusy: _busy++; break;
                    case AlsActionResultCode.Completed: _completed++; break;
                    default: throw new InvalidOperationException($"Unexpected roll outcome {outcome.ResultCode}.");
                }
            };
        }
        catch (Exception error) { Fail(error); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done || _demo is null) return;
        try
        {
            var character = _demo.ActiveCharacter; var frame = character.Diagnostics;
            Require(_demo.ErrorCount == 0 && !character.IsPoseFrozen, "Roll broke the production runtime.");
            if (frame.CommittedFrameId > _committed)
            {
                Require(frame.CommittedFrameId == _injected && _injected == _committed + 1, "Frame identity skipped.");
                var input = character.LatestMotorInput; var roll = frame.Result.Rolling;
                AlsResultDigest.Append(ref _digest, frame.Result);
                var root = AlsP3Presentation.Compose(input.CharacterTransform, _demo.RuntimeContext.PresentationTransform);
                root.Origin += Vector3.Up * input.MeshHeightOffset;
                var expectedRoot = new System.Numerics.Vector3(root.Origin.X, root.Origin.Y, root.Origin.Z);
                Require(System.Numerics.Vector3.Distance(frame.VisualRootTransform.Origin, expectedRoot) < .0001f &&
                    System.Numerics.Vector3.Distance(input.FootIk.ComponentToWorld.Position, expectedRoot) < .0001f,
                    "Mesh presentation and foot gathering disagree about capsule-height compensation.");
                var yaw = -input.CharacterYaw * (180f / MathF.PI);
                Require(character.ConsumedRootMotion == frame.Result.RootMotionSource, "Motion did not use this frame's source.");
                if (roll.Active)
                {
                    _rollFrames++;
                    if (roll.InstanceId == _previousRoll.InstanceId)
                        Require(roll.TargetYawDegrees == _previousRoll.TargetYawDegrees, "Input retargeted an active roll.");
                }
                if (_previousRoll.Active)
                {
                    Require(input.Stance == AlsStance.Crouching && input.JumpAccepted == 0, "Roll failed to crouch or allowed jumping.");
                    var expected = AlsRollingGameplay.Rotate(_previousYaw, _previousRoll.TargetYawDegrees, input.DeltaTime);
                    var error = Mathf.Abs(Mathf.Wrap(yaw - expected, -180, 180));
                    Require(error < .003f, $"Native roll rotation differs: frame={input.Identity.FrameId}, expected={expected}, actual={yaw}.");
                    if (Mathf.Abs(Mathf.Wrap(yaw - _previousYaw, -180, 180)) > .01f) _turnFrames++;
                }
                if (frame.CommittedFrameId == _hz * 2)
                {
                    Require(!roll.Active && input.Stance == AlsStance.Standing, "Roll did not restore desired standing stance.");
                    Require(character.MovementAnchor.GlobalPosition.DistanceTo(_start) > 1, "Roll did not translate through the motor.");
                }
                if (frame.CommittedFrameId == _hz * 3)
                    Require(input.Floor.IsGrounded == 0 && !roll.Active, "Airborne input started a roll.");
                _previousRoll = roll; _previousYaw = yaw; _committed = frame.CommittedFrameId; _stalls = 0;
                if (_committed == _hz * 7)
                {
                    Require(_accepted == 2 && _busy == 2 && _completed == 2 && _rollFrames > _hz && _turnFrames > 8,
                        $"Unexpected coverage accepted={_accepted}, busy={_busy}, complete={_completed}, roll={_rollFrames}, turn={_turnFrames}.");
                    Require(!roll.Active && input.Stance == AlsStance.Crouching, "Roll overwrote the user's desired crouch stance.");
                    GD.Print($"ROLLING_GAMEPLAY_OK hz={_hz} frames={_committed} accepted={_accepted} busy={_busy} completed={_completed} rollingFrames={_rollFrames} turnFrames={_turnFrames} digest={_digest:X16}");
                    _done = true; Cleanup(); GetTree().Quit(); return;
                }
            }
            else if (++_stalls > _hz * 4) throw new InvalidOperationException("Rolling commit stalled.");
            if (_injected > _committed) return;
            var next = _committed + 1;
            if (next == _hz / 2) { Input.ActionPress("move_right"); KeyEvent(Key.R, true); }
            if (next == _hz / 2 + 1 || next == _hz * 3 / 5 + 1 || next == _hz * 3 + 1 || next == _hz * 5 + 1) KeyEvent(Key.R, false);
            if (next == _hz * 3 / 5) KeyEvent(Key.R, true);
            if (next == _hz * 7 / 10) { Input.ActionRelease("move_right"); Input.ActionPress("move_left"); }
            if (next == _hz * 4 / 5) KeyEvent(Key.Space, true);
            if (next == _hz * 4 / 5 + 1) KeyEvent(Key.Space, false);
            if (next == _hz) Input.ActionRelease("move_left");
            if (next == _hz * 3) { character.MovementAnchor.GlobalPosition += Vector3.Up * 5; KeyEvent(Key.R, true); }
            if (next == _hz * 9 / 2) Input.ActionPress("crouch_toggle");
            if (next == _hz * 9 / 2 + 1) Input.ActionRelease("crouch_toggle");
            if (next == _hz * 5) KeyEvent(Key.R, true);
            _injected = next;
        }
        catch (Exception error) { Fail(error); }
    }

    private static void KeyEvent(Key key, bool pressed) =>
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
    internal static void PlaceOnOpenFloor(P4LocomotionDemo demo)
    {
        var floor = demo.GetNode<StaticBody3D>("World/StartFloor");
        var collider = floor.GetNode<CollisionShape3D>("CollisionShape3D");
        var shape = (BoxShape3D)collider.Shape;
        demo.ActiveCharacter.MovementAnchor.GlobalPosition = new(10,
            collider.GlobalPosition.Y + shape.Size.Y * .5f + demo.RuntimeContext.MotorSettings.StandingHeight * .5f, 10);
    }
    public override void _Process(double delta)
    {
        if (_camera is null || _done) return;
        var position = _demo.ActiveCharacter.MovementAnchor.GlobalPosition;
        _camera.GlobalPosition = position + new Vector3(3, 1.8f, 3);
        _camera.LookAt(position + Vector3.Up * .1f);
    }
    private void Capture()
    {
        if (_done || _committed < _nextCapture || _nextCapture > _hz * 3 / 2) return;
        using var picture = GetViewport().GetTexture().GetImage();
        Require(picture.SavePng(Path.Combine(_captureDirectory!, $"roll-{_committed:D4}.png")) == Error.Ok, "Screenshot failed.");
        _nextCapture += _hz / 10;
    }
    private void Cleanup()
    {
        if (_captureDirectory is not null) RenderingServer.FramePostDraw -= Capture;
        KeyEvent(Key.R, false); KeyEvent(Key.Space, false);
        Input.ActionRelease("move_right"); Input.ActionRelease("move_left"); Input.ActionRelease("crouch_toggle");
        Input.UseAccumulatedInput = _oldAccumulation; Input.MouseMode = _oldMouse;
        _demo?.DisposeRuntime();
    }
    private void Fail(Exception error) { _done = true; Cleanup(); GD.PushError(error.ToString()); GetTree().Quit(1); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
