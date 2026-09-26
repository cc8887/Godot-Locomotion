using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;

namespace GodotAls.Locomotion;

// Sends engine input events through the default Demo input owner. Alt retains
// its production meaning (walking); this does not add a free-look mode.
public partial class P4KeyboardMouseSmoke : Node
{
    private const int LastFrame = 360;
    private readonly record struct ExpectedInput(NumericsVector2 Axes, bool Walk, float Yaw, float Pitch);
    private readonly ExpectedInput[] _expected = new ExpectedInput[LastFrame + 1];
    private P4LocomotionDemo? _demo;
    private Input.MouseModeEnum _previousMouseMode;
    private bool _previousAccumulatedInput;
    private bool _ownsInput, _finished, _alt, _left, _right;
    private long _injected, _committed;
    private int _stalledTicks, _mouseEvents, _walkFrames, _releasedWalkFrames, _leftFrames, _rightFrames;
    private int _movingLookingFrames, _rotatedFrames;
    private int _toePinnedFrames, _lastCaptureFrame;
    private string? _captureDirectory;
    private bool _captureSubscribed;
    private AlsFrameIdentity _previousIdentity;

    public P4KeyboardMouseSmoke()
    {
        // Production Demo captures actions in group -1, Motor gathers in 0,
        // animation uses the configured worker/query/commit groups. Inspect the previous
        // complete frame and send the next frame's events before that chain.
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = -2;
    }

    public override void _Ready()
    {
        try
        {
            foreach (var action in new[] { "walk", "move_left", "move_right", "move_forward", "move_back", "aim", "sprint" })
                Require(!Input.IsActionPressed(action), $"Input action {action} was already held before the smoke.");
            _previousMouseMode = Input.MouseMode;
            _previousAccumulatedInput = Input.UseAccumulatedInput;
            _ownsInput = true;
            Input.UseAccumulatedInput = false;
            var capture = OS.GetCmdlineUserArgs().SingleOrDefault(value => value.StartsWith("--capture-dir=", StringComparison.Ordinal));
            if (capture is not null)
            {
                Require(DisplayServer.GetName() != "headless", "Screenshots require a rendered smoke.");
                _captureDirectory = ProjectSettings.GlobalizePath(capture["--capture-dir=".Length..]);
                Require(!Directory.Exists(_captureDirectory), "Capture directory already exists.");
                Directory.CreateDirectory(_captureDirectory);
                RenderingServer.FramePostDraw += CaptureFrame;
                _captureSubscribed = true;
            }
            var scene = ResourceLoader.Load<PackedScene>(ProjectSettings.GetSetting("application/run/main_scene").AsString())
                ?? throw new InvalidOperationException("Production P4 Demo scene is missing.");
            var entry = scene.Instantiate<AlsDemoEntry>();
            // Intentionally do not ConfigureForSmoke: the production Demo must
            // call CaptureGodotFrame and read the actual Input singleton.
            AddChild(entry);
            _demo = entry.Demo;
            Require(_demo.IsRuntimeReady, "Production Demo did not initialize.");
            var floor = _demo.GetNode<StaticBody3D>("World/StartFloor");
            var size = new Vector3(40f, .5f, 40f);
            var floorShape = floor.GetNode<CollisionShape3D>("CollisionShape3D");
            floorShape.Position = Vector3.Zero;
            floorShape.Shape = new BoxShape3D { Size = size };
            floor.GetNode<MeshInstance3D>("MeshInstance3D").Position = new Vector3(0, .25f, 0);
            foreach (var terrain in _demo.GetNode<Node3D>("World").GetChildren())
                if (terrain != floor) DisableTerrain(terrain);
            _demo.OrbitCamera.SetMouseCaptured(true);
            _demo.OrbitCamera.Notification((int)Node.NotificationWMWindowFocusOut);
            Require(!_demo.OrbitCamera.IsMouseCaptured, "Defocus must release the tracked capture state.");
            _demo.OrbitCamera.Notification((int)Node.NotificationWMWindowFocusIn);
            Require(_demo.OrbitCamera.IsMouseCaptured &&
                (DisplayServer.GetName() == "headless" || Input.MouseMode == Input.MouseModeEnum.Captured),
                "Refocusing must restore the requested mouse capture.");
        }
        catch (Exception exception) { Fail(exception); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_finished || _demo is null) return;
        try
        {
            Require(_demo.IsRuntimeReady && _demo.ErrorCount == 0, "Production Demo reported an error.");
            var character = _demo.ActiveCharacter;
            var frame = character.Diagnostics;
            if (frame.CommittedFrameId > _committed)
            {
                Require(frame.CommittedFrameId == _committed + 1 && frame.CommittedFrameId == _injected,
                    "Input injection and committed frame order diverged.");
                var input = character.LatestMotorInput;
                var expected = _expected[checked((int)frame.CommittedFrameId)];
                Require(input.Identity == frame.Identity && frame.Result.Identity == frame.Identity,
                    "Motor and result did not retain the committed frame identity.");
                Require(character.UsesCompleteMovement && character.FullMovementDiagnostics.Identity == frame.Identity &&
                    character.FullMovementDiagnostics.Feedback.Identity == frame.Identity &&
                    frame.CharacterRotationFeedback.Identity == frame.Identity,
                    "Keyboard/mouse frame did not commit the complete movement graph.");
                if (!OS.GetCmdlineUserArgs().Contains("--legacy-animation"))
                {
                    var movement = character.FullMovementDiagnostics;
                    Require(character.UsesLayeredPose && movement.UsesRefactoredFeet &&
                        movement.RootIdentity == frame.Identity && movement.FootPoseIdentity == frame.Identity &&
                        movement.LockCurveProducersMatch && movement.Overlay == _demo.Overlay,
                        $"Default Demo boundary differs: layered={character.UsesLayeredPose} feet={movement.UsesRefactoredFeet} root={movement.RootIdentity==frame.Identity} foot={movement.FootPoseIdentity==frame.Identity} locks={movement.LockCurveProducersMatch} overlay={movement.Overlay}/{_demo.Overlay}; raw={movement.RawLockProducers}; final={movement.LockProducers}.");
                    Require(character.SplitFootDiagnostics.Resumed > 0,
                        "Default Demo did not execute split foot dispatch.");
                    if (movement.RefactoredRig.LeftToePinned || movement.RefactoredRig.RightToePinned)
                        _toePinnedFrames++;
                }
                Require(input.Command.MovementAxes == expected.Axes &&
                    input.Command.RequestedGait == (expected.Walk ? AlsGait.Walking : AlsGait.Running),
                    "Physical Alt/A/D events were not reflected in the production command.");
                Require(input.Command.RequestedRotationMode == AlsRotationMode.LookingDirection &&
                    NearAngle(input.Command.ViewYaw, expected.Yaw) && NearAngle(input.Command.AimYaw, expected.Yaw) &&
                    MathF.Abs(input.Command.ViewPitch - expected.Pitch) < 1e-5f,
                    "Mouse events did not reach the production camera and command.");
                var cameraRight = new NumericsVector3(MathF.Cos(expected.Yaw), 0, -MathF.Sin(expected.Yaw));
                Require(NumericsVector3.Distance(input.InputDirection, cameraRight * expected.Axes.X) < 1e-4f,
                    "A/D world movement did not follow the event-driven camera basis.");
                var rotation = input.CharacterRotation;
                Require(rotation.Applied == 1 && rotation.FeedbackIdentity == _previousIdentity &&
                    float.IsFinite(rotation.SmoothedTargetYaw) && float.IsFinite(rotation.ActorYawDelta) &&
                    NearAngle(frame.Result.TargetYaw, input.CharacterYaw) && frame.Result.ActualGait == rotation.ActualGait,
                    "Character rotation did not consume the previous committed animation feedback.");
                Require(float.IsFinite(frame.Result.AnimationPhase) && float.IsFinite(frame.Result.PlayRate) &&
                    float.IsFinite(frame.Result.Stride), "Complete animation published nonfinite timing.");
                if (frame.CommittedFrameId > 2) Require(input.Floor.IsGrounded != 0, "Flat input fixture left the ground.");
                if (expected.Axes.X < 0) _leftFrames++;
                if (expected.Axes.X > 0) _rightFrames++;
                if (expected.Walk && expected.Axes != NumericsVector2.Zero) _walkFrames++;
                if (!expected.Walk && expected.Axes != NumericsVector2.Zero) _releasedWalkFrames++;
                if (rotation.Branch == AlsCharacterRotationBranch.MovingLooking) _movingLookingFrames++;
                if (MathF.Abs(rotation.ActorYawDelta) > 1e-5f) _rotatedFrames++;
                _previousIdentity = frame.Identity;
                _committed = frame.CommittedFrameId;
                _stalledTicks = 0;
                if (_committed == LastFrame) { Complete(); return; }
            }
            else if (++_stalledTicks > 240)
                throw new InvalidOperationException("Keyboard/mouse committed frame progress stalled.");

            if (_injected > _committed) return;
            var next = _committed + 1;
            Inject(next);
            _expected[checked((int)next)] = new(
                new NumericsVector2((_right ? 1 : 0) - (_left ? 1 : 0), 0), _alt,
                _demo.OrbitCamera.Yaw, _demo.OrbitCamera.Pitch);
            _injected = next;
        }
        catch (Exception exception) { Fail(exception); }
    }

    private void Inject(long frame)
    {
        switch (frame)
        {
            case 31: KeyEvent(Key.Alt, true); KeyEvent(Key.D, true); break;
            case 61: MouseEvent(new Vector2(80, -16)); break;
            case 121: KeyEvent(Key.D, false); KeyEvent(Key.A, true); break;
            case 141: MouseEvent(new Vector2(-160, 32)); break;
            case 211: KeyEvent(Key.Alt, false); break;
            case 241: MouseEvent(new Vector2(120, -8)); break;
            case 271: KeyEvent(Key.A, false); KeyEvent(Key.D, true); break;
            case 301: KeyEvent(Key.D, false); KeyEvent(Key.Alt, true); MouseEvent(new Vector2(-400, 0)); break;
            case 331: KeyEvent(Key.Alt, false); break;
        }
        Require(Input.IsActionPressed("walk") == _alt && Input.IsActionPressed("move_left") == _left &&
            Input.IsActionPressed("move_right") == _right, "Physical key events did not update the Godot action map.");
    }

    private void KeyEvent(Key key, bool pressed)
    {
        if (key == Key.Alt) _alt = pressed;
        else if (key == Key.A) _left = pressed;
        else if (key == Key.D) _right = pressed;
        else throw new ArgumentOutOfRangeException(nameof(key));
        using var inputEvent = new InputEventKey
        {
            PhysicalKeycode = key, Keycode = key, Pressed = pressed,
            AltPressed = _alt, Echo = false,
        };
        Input.ParseInputEvent(inputEvent);
        Input.FlushBufferedEvents();
    }

    private void MouseEvent(Vector2 relative)
    {
        var camera = _demo!.OrbitCamera;
        var expectedYaw = Mathf.Wrap(camera.Yaw - relative.X * camera.MouseSensitivity, -Mathf.Pi, Mathf.Pi);
        var expectedPitch = Mathf.Clamp(camera.Pitch - relative.Y * camera.MouseSensitivity, camera.MinimumPitch, camera.MaximumPitch);
        using var inputEvent = new InputEventMouseMotion { Relative = relative, AltPressed = _alt };
        Input.ParseInputEvent(inputEvent);
        Input.FlushBufferedEvents();
        Require(camera.IsMouseCaptured && NearAngle(camera.Yaw, expectedYaw) && MathF.Abs(camera.Pitch - expectedPitch) < 1e-5f,
            "Mouse event did not traverse the captured camera's input callback.");
        _mouseEvents++;
    }

    private void Complete()
    {
        Require(_walkFrames == 180 && _releasedWalkFrames == 90 && _leftFrames == 150 && _rightFrames == 120 &&
            _mouseEvents == 4 && _movingLookingFrames >= 180 && _rotatedFrames > 0,
            "Keyboard/mouse replay did not cover walking, Alt release, both strafing directions and actual character rotation.");
        Require(OS.GetCmdlineUserArgs().Contains("--legacy-animation") || _toePinnedFrames > 0,
            "Default Demo never applied final toe contact.");
        CleanupInput();
        Require(!Input.IsActionPressed("walk") && !Input.IsActionPressed("move_left") && !Input.IsActionPressed("move_right"),
            "Keyboard/mouse replay left a controlled action held.");
        _demo!.DisposeRuntime();
        _finished = true;
        GD.Print($"P4_KEYBOARD_MOUSE_OK frames={_committed} physical_keys=Alt,A,D mouse_events={_mouseEvents} " +
            $"walking={_walkFrames} alt_released_moving={_releasedWalkFrames} left={_leftFrames} right={_rightFrames} " +
            $"moving_looking={_movingLookingFrames} rotated={_rotatedFrames} toes={_toePinnedFrames} feedback=previous_committed owner=production");
        GetTree().Quit();
    }

    private void CleanupInput()
    {
        if (!_ownsInput) return;
        if (_left) KeyEvent(Key.A, false);
        if (_right) KeyEvent(Key.D, false);
        if (_alt) KeyEvent(Key.Alt, false);
        Input.UseAccumulatedInput = _previousAccumulatedInput;
        Input.MouseMode = _previousMouseMode;
        _ownsInput = false;
    }

    private void Fail(Exception exception)
    {
        if (_finished) return;
        _finished = true;
        CleanupInput();
        GD.PushError($"P4_KEYBOARD_MOUSE_FAIL frame={_committed} {exception}");
        GetTree().Quit(1);
    }

    private void CaptureFrame()
    {
        if (_finished || _demo is null || !_demo.IsRuntimeReady || _captureDirectory is null) return;
        try
        {
            var frame = checked((int)_demo.ActiveCharacter.Diagnostics.CommittedFrameId);
            if (frame < _lastCaptureFrame + 60) return;
            using var image = GetViewport().GetTexture().GetImage();
            var error = image.SavePng(Path.Combine(_captureDirectory, $"frame-{frame:D4}.png"));
            Require(error == Error.Ok, $"Screenshot failed: {error}");
            _lastCaptureFrame = frame;
        }
        catch (Exception exception) { Fail(exception); }
    }

    public override void _ExitTree()
    {
        if (_captureSubscribed) RenderingServer.FramePostDraw -= CaptureFrame;
        CleanupInput();
    }

    private static void DisableTerrain(Node node)
    {
        if (node is Node3D spatial) spatial.Hide();
        if (node is CollisionObject3D collider) { collider.CollisionLayer = 0; collider.CollisionMask = 0; }
        foreach (var child in node.GetChildren()) DisableTerrain(child);
    }

    private static bool NearAngle(float a, float b) => MathF.Abs(Mathf.Wrap(a - b, -Mathf.Pi, Mathf.Pi)) < 1e-4f;
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
