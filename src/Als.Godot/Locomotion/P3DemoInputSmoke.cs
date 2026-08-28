using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;

namespace GodotAls.Locomotion;

public partial class P3DemoInputSmoke : Node
{
    private readonly record struct InputSmokeEvidence(
        int DirectionCount,
        int CameraBasisChecks,
        int PitchChecks,
        int AimingChecks,
        int ClearChecks);

    private bool _finished;

    public override async void _Ready()
    {
        try
        {
            await RunSmokeAsync();
            _finished = true;
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            _finished = true;
            GD.PushError($"GODOT_ALS_P3_DEMO_INPUT_FAIL {exception}");
            GetTree().Quit(1);
        }
    }

    private async Task RunSmokeAsync()
    {
        ValidateActionMap();
        ValidateCommandSemantics();
        var evidence = await ValidateCameraRelativeMovementMatrixAsync();
        ValidateHudSemantics();
        Require(typeof(P3LocomotionDemo).IsSubclassOf(typeof(Node3D)),
            "demo root was not a Node3D production scene root");
        GD.Print(
            $"GODOT_ALS_P3_DEMO_INPUT_OK actions={ControlledActions.Length} " +
            $"directions={evidence.DirectionCount} camera_basis={evidence.CameraBasisChecks} " +
            $"pitch={evidence.PitchChecks} aiming={evidence.AimingChecks} " +
            $"cleared={evidence.ClearChecks} hud=1");
    }

    public override void _ExitTree()
    {
        if (!_finished)
        {
            GD.PushError("GODOT_ALS_P3_DEMO_INPUT_FAIL scene exited before validation");
        }
    }

    private static void ValidateActionMap()
    {
        RequireKey("move_left", Key.A);
        RequireKey("move_right", Key.D);
        RequireKey("move_forward", Key.W);
        RequireKey("move_back", Key.S);
        RequireKey("walk", Key.Alt);
        RequireKey("sprint", Key.Shift);
        RequireKey("crouch_toggle", Key.Ctrl);
        RequireKey("jump", Key.Space);
        RequireKey("rotation_mode_toggle", Key.V);
        RequireMouseButton("aim", MouseButton.Right);
        RequireKey("mouse_capture_toggle", Key.Escape);
    }

    private static void ValidateCommandSemantics()
    {
        var adapter = new AlsPlayerInputAdapter();

        adapter.CaptureFrame(1, new AlsPlayerInputSnapshot(
            new NumericsVector2(1f, 1f),
            WalkHeld: true,
            SprintHeld: false,
            CrouchTogglePressed: false,
            JumpPressed: false,
            RotationModeTogglePressed: false,
            AimHeld: false), 0.25f, -0.125f);
        var first = adapter.GetCommand(1);
        Require(first == adapter.GetCommand(1), "same-frame command was not stable");
        Require(first.RequestedGait == AlsGait.Walking, "Alt did not request walking");
        Require(first.RequestedStance == AlsStance.Standing, "default stance was not standing");
        Require(first.RequestedRotationMode == AlsRotationMode.LookingDirection,
            "default rotation mode was not looking direction");
        Require(first.JumpPressed == 0, "idle jump edge was nonzero");
        Require(MathF.Abs(first.MovementAxes.Length() - 1f) < 0.00001f,
            "diagonal WASD axes were not normalized");
        Require(first.ViewYaw == 0.25f && first.AimYaw == 0.25f,
            "camera yaw was not captured into view and aim yaw");
        Require(first.ViewPitch == -0.125f && first.AimPitch == -0.125f,
            "camera pitch was not captured into view and aim pitch");

        adapter.CaptureFrame(2, new AlsPlayerInputSnapshot(
            -NumericsVector2.UnitY,
            WalkHeld: false,
            SprintHeld: true,
            CrouchTogglePressed: true,
            JumpPressed: true,
            RotationModeTogglePressed: true,
            AimHeld: false), -0.5f, 0f);
        var toggled = adapter.GetCommand(2);
        Require(toggled.RequestedGait == AlsGait.Sprinting, "Shift did not request sprinting");
        Require(toggled.RequestedStance == AlsStance.Crouching, "Ctrl did not toggle crouch");
        Require(toggled.RequestedRotationMode == AlsRotationMode.VelocityDirection,
            "V did not toggle velocity direction");
        Require(toggled.JumpPressed == 1, "Space edge was not emitted");

        adapter.CaptureFrame(3, new AlsPlayerInputSnapshot(
            NumericsVector2.Zero,
            WalkHeld: false,
            SprintHeld: false,
            CrouchTogglePressed: false,
            JumpPressed: false,
            RotationModeTogglePressed: false,
            AimHeld: false), -0.5f, 0f);
        var persisted = adapter.GetCommand(3);
        Require(persisted.RequestedGait == AlsGait.Running, "default gait was not running");
        Require(persisted.RequestedStance == AlsStance.Crouching,
            "crouch toggle did not persist across frames");
        Require(persisted.RequestedRotationMode == AlsRotationMode.VelocityDirection,
            "rotation toggle did not persist across frames");
        Require(persisted.JumpPressed == 0, "jump edge repeated on the next frame");

        adapter.CaptureFrame(4, new AlsPlayerInputSnapshot(
            NumericsVector2.Zero,
            WalkHeld: false,
            SprintHeld: false,
            CrouchTogglePressed: false,
            JumpPressed: false,
            RotationModeTogglePressed: false,
            AimHeld: true), 1f, 0f);
        Require(adapter.GetCommand(4).RequestedRotationMode == AlsRotationMode.Aiming,
            "RMB did not enter aiming");

        adapter.CaptureFrame(5, new AlsPlayerInputSnapshot(
            NumericsVector2.Zero,
            WalkHeld: false,
            SprintHeld: false,
            CrouchTogglePressed: false,
            JumpPressed: false,
            RotationModeTogglePressed: true,
            AimHeld: true), 1f, 0f);
        Require(adapter.GetCommand(5).RequestedRotationMode == AlsRotationMode.Aiming,
            "V changed rotation mode while aiming");

        adapter.CaptureFrame(6, new AlsPlayerInputSnapshot(
            NumericsVector2.Zero,
            WalkHeld: false,
            SprintHeld: false,
            CrouchTogglePressed: false,
            JumpPressed: false,
            RotationModeTogglePressed: false,
            AimHeld: false), 1f, 0f);
        Require(adapter.GetCommand(6).RequestedRotationMode == AlsRotationMode.VelocityDirection,
            "RMB release did not restore the prior rotation mode");

        adapter.CaptureFrame(7, new AlsPlayerInputSnapshot(
            NumericsVector2.Zero,
            WalkHeld: true,
            SprintHeld: true,
            CrouchTogglePressed: true,
            JumpPressed: false,
            RotationModeTogglePressed: true,
            AimHeld: false), 1f, 0f);
        var bothModifiers = adapter.GetCommand(7);
        Require(bothModifiers.RequestedGait == AlsGait.Walking,
            "walk did not take precedence over sprint");
        Require(bothModifiers.RequestedStance == AlsStance.Standing,
            "second Ctrl edge did not restore standing");
        Require(bothModifiers.RequestedRotationMode == AlsRotationMode.LookingDirection,
            "second V edge did not restore looking direction");

        RequireThrows<InvalidOperationException>(
            () => adapter.CaptureFrame(7, default, 0f, 0f),
            "duplicate frame capture was accepted");
        RequireThrows<InvalidOperationException>(
            () => adapter.CaptureFrame(9, default, 0f, 0f),
            "skipped frame capture was accepted");
        RequireThrows<InvalidOperationException>(
            () => adapter.GetCommand(8),
            "uncaptured command frame was accepted");

        adapter.CaptureFrame(8, default, 0f, 0f);
        Require(adapter.GetCommand(8).JumpPressed == 0, "default snapshot emitted a jump edge");
    }

    private async Task<InputSmokeEvidence> ValidateCameraRelativeMovementMatrixAsync()
    {
        var previousMouseMode = Input.MouseMode;
        Node3D? target = null;
        AlsOrbitCamera? orbit = null;
        var directionCount = 0;
        var cameraBasisChecks = 0;
        var pitchChecks = 0;
        var aimingChecks = 0;
        var clearChecks = 0;
        ReleaseControlledActions();
        try
        {
            target = new Node3D { Name = "CameraTarget" };
            AddChild(target);
            target.GlobalPosition = new Vector3(3f, 2f, -4f);

            orbit = new AlsOrbitCamera { Name = "OrbitCamera" };
            var springArm = new SpringArm3D { Name = "SpringArm3D" };
            var camera = new Camera3D { Name = "Camera3D" };
            springArm.AddChild(camera);
            orbit.AddChild(springArm);
            AddChild(orbit);
            orbit.Configure(target);
            orbit._Process(0d);

            Require(orbit.Target == target, "orbit camera did not retain its live target");
            Require(orbit.FollowOffset == new Vector3(0f, 0.53f, 0f),
                $"orbit follow offset was not aligned to the movement anchor: {orbit.FollowOffset}");
            Require(orbit.GlobalPosition == target.GlobalPosition + orbit.FollowOffset,
                "orbit camera did not follow its target at shoulder height");
            Require(camera.IsInsideTree(), "camera basis probe was not live in the scene tree");

            orbit.SetMouseCaptured(false);
            orbit.ToggleMouseCapture();
            Require(orbit.IsMouseCaptured, "Esc capture toggle did not capture the mouse");
            orbit.ToggleMouseCapture();
            Require(!orbit.IsMouseCaptured, "second Esc capture toggle did not release the mouse");
            orbit.SetMouseCaptured(true);

            var initialPitch = orbit.Pitch;
            await DispatchMouseMotionAsync(orbit, new Vector2(0f, 80f));
            Require(orbit.Pitch < initialPitch, "mouse down did not pitch the camera down");
            var pitchedForward = -camera.GlobalBasis.Z;
            Require(Mathf.Abs(pitchedForward.Y) > 0.01f,
                "real Camera3D forward did not contain the expected pitch component");
            var downwardPitch = orbit.Pitch;
            await DispatchMouseMotionAsync(orbit, new Vector2(0f, -160f));
            Require(orbit.Pitch > downwardPitch, "mouse up did not pitch the camera up");
            await DispatchMouseMotionAsync(orbit, new Vector2(0f, 10000f));
            Require(orbit.Pitch == orbit.MinimumPitch, "orbit pitch minimum was not clamped");
            await DispatchMouseMotionAsync(orbit, new Vector2(0f, -20000f));
            Require(orbit.Pitch == orbit.MaximumPitch, "orbit pitch maximum was not clamped");
            pitchChecks = 1;

            var adapter = new AlsPlayerInputAdapter();
            var frameId = 0L;
            var targetYaws = new[] { 0f, Mathf.Pi * 0.5f, -Mathf.Pi * 0.5f };
            var actions = new[]
            {
                "move_forward",
                "move_left",
                "move_back",
                "move_right",
            };

            foreach (var targetYaw in targetYaws)
            {
                foreach (var action in actions)
                {
                    orbit.SetMouseCaptured(true);
                    var deltaYaw = Mathf.Wrap(targetYaw - orbit.Yaw, -Mathf.Pi, Mathf.Pi);
                    await DispatchMouseMotionAsync(
                        orbit,
                        new Vector2(-deltaYaw / orbit.MouseSensitivity, 0f));
                    Require(Mathf.Abs(Mathf.Wrap(
                            orbit.Yaw - targetYaw,
                            -Mathf.Pi,
                            Mathf.Pi)) < 1e-4f,
                        $"real mouse input did not reach target yaw {targetYaw:R}: " +
                        $"actual={orbit.Yaw:R}");

                    var forward = -camera.GlobalBasis.Z;
                    forward.Y = 0f;
                    forward = forward.Normalized();
                    var right = camera.GlobalBasis.X;
                    right.Y = 0f;
                    right = right.Normalized();
                    var basisYaw = Mathf.Atan2(-forward.X, -forward.Z);
                    Require(Mathf.Abs(Mathf.Wrap(
                            orbit.Yaw - basisYaw,
                            -Mathf.Pi,
                            Mathf.Pi)) < 1e-4f,
                        $"orbit yaw did not match the real Camera3D basis: " +
                        $"target={targetYaw:R} orbit={orbit.Yaw:R} " +
                        $"basis={basisYaw:R} forward={forward}");

                    Input.ActionPress(action);
                    try
                    {
                        frameId++;
                        adapter.CaptureGodotFrame(frameId, orbit.Yaw, orbit.Pitch);
                        var command = adapter.GetCommand(frameId);
                        var resolved = AlsLocomotionCommandResolver.Resolve(
                            command,
                            AlsStance.Standing);
                        var expectedDirection = action switch
                        {
                            "move_forward" => forward,
                            "move_left" => -right,
                            "move_back" => -forward,
                            "move_right" => right,
                            _ => throw new InvalidOperationException(
                                $"unsupported movement action {action}"),
                        };
                        Require(NumericsVector3.Distance(
                                resolved.WorldDirection,
                                ToNumerics(expectedDirection)) < 1e-4f,
                            $"camera-relative movement mismatch for yaw={targetYaw:R} " +
                            $"action={action}");
                        directionCount++;
                    }
                    finally
                    {
                        Input.ActionRelease(action);
                    }
                }
            }

            Require(directionCount == 12,
                $"camera-relative movement matrix completed {directionCount} directions");
            cameraBasisChecks = 1;

            frameId++;
            adapter.CaptureGodotFrame(frameId, orbit.Yaw, orbit.Pitch);
            var cleared = adapter.GetCommand(frameId);
            var resolvedCleared = AlsLocomotionCommandResolver.Resolve(
                cleared,
                AlsStance.Standing);
            Require(cleared.MovementAxes == NumericsVector2.Zero &&
                cleared.RequestedGait == AlsGait.Running &&
                cleared.RequestedStance == AlsStance.Standing &&
                cleared.RequestedRotationMode == AlsRotationMode.LookingDirection &&
                cleared.JumpPressed == 0 &&
                resolvedCleared.WorldDirection == NumericsVector3.Zero,
                "released Godot actions contaminated the next command frame");
            clearChecks = 1;

            Input.ActionPress("aim");
            Input.ActionPress("move_forward");
            try
            {
                frameId++;
                adapter.CaptureGodotFrame(frameId, orbit.Yaw, orbit.Pitch);
                var aiming = adapter.GetCommand(frameId);
                var resolvedAiming = AlsLocomotionCommandResolver.Resolve(
                    aiming,
                    AlsStance.Standing);
                var aimingForward = -camera.GlobalBasis.Z;
                aimingForward.Y = 0f;
                aimingForward = aimingForward.Normalized();
                Require(aiming.RequestedRotationMode == AlsRotationMode.Aiming,
                    "RMB+W did not request aiming rotation mode");
                Require(NumericsVector3.Distance(
                        resolvedAiming.WorldDirection,
                        ToNumerics(aimingForward)) < 1e-4f,
                    "RMB+W did not remain camera-forward");
                aimingChecks = 1;
            }
            finally
            {
                Input.ActionRelease("move_forward");
                Input.ActionRelease("aim");
            }

            return new InputSmokeEvidence(
                directionCount,
                cameraBasisChecks,
                pitchChecks,
                aimingChecks,
                clearChecks);
        }
        finally
        {
            ReleaseControlledActions();
            Input.MouseMode = previousMouseMode;
            if (orbit is not null && GodotObject.IsInstanceValid(orbit))
            {
                if (orbit.GetParent() == this)
                {
                    RemoveChild(orbit);
                }
                orbit.Free();
            }
            if (target is not null && GodotObject.IsInstanceValid(target))
            {
                if (target.GetParent() == this)
                {
                    RemoveChild(target);
                }
                target.Free();
            }
            Require(Input.MouseMode == previousMouseMode,
                "input smoke did not restore the prior mouse mode");
            foreach (var action in ControlledActions)
            {
                Require(!Input.IsActionPressed(action),
                    $"headless input smoke leaked pressed action {action}");
            }
        }
    }

    private async Task DispatchMouseMotionAsync(AlsOrbitCamera orbit, Vector2 relative)
    {
        Require(orbit.IsMouseCaptured,
            "synthetic mouse motion was dispatched while the orbit was uncaptured");
        Input.ParseInputEvent(new InputEventMouseMotion { Relative = relative });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static NumericsVector3 ToNumerics(in Vector3 value) =>
        new(value.X, value.Y, value.Z);

    private static readonly string[] ControlledActions =
    [
        "move_left",
        "move_right",
        "move_forward",
        "move_back",
        "walk",
        "sprint",
        "crouch_toggle",
        "jump",
        "rotation_mode_toggle",
        "aim",
        "mouse_capture_toggle",
    ];

    private static void ReleaseControlledActions()
    {
        foreach (var action in ControlledActions)
        {
            Input.ActionRelease(action);
        }
    }

    private void ValidateHudSemantics()
    {
        var hud = new AlsLocomotionHud { Name = "LocomotionHud" };
        AddChild(hud);
        var result = AlsFrameResult.CreateDefault(new AlsFrameIdentity(42, 0, 1));
        result.BlendCoordinates = new NumericsVector2(0.25f, -0.5f);
        result.Stride = 1.2f;
        result.PlayRate = 0.9f;
        result.Lean = new NumericsVector2(0.1f, -0.2f);
        result.AnimationPhase = 0.75f;
        var diagnostics = new AlsP3FrameDiagnostics(
            result.Identity,
            42,
            42,
            42,
            42,
            42,
            new NumericsVector3(3f, 0f, 4f),
            result,
            PoseDigest: 0,
            FullPoseDigest: 0);
        hud.Refresh(diagnostics, framesPerSecond: 60d, errors: 0);
        Require(hud.StateText.Contains("Grounded", StringComparison.Ordinal),
            "HUD did not expose locomotion state");
        Require(hud.StateText.Contains("Speed 5.00", StringComparison.Ordinal) &&
            hud.StateText.Contains("Blend (0.25, -0.50)", StringComparison.Ordinal) &&
            hud.StateText.Contains("Stride 1.20", StringComparison.Ordinal) &&
            hud.StateText.Contains("Rate 0.90", StringComparison.Ordinal) &&
            hud.StateText.Contains("Lean (0.10, -0.20)", StringComparison.Ordinal) &&
            hud.StateText.Contains("Phase 0.75", StringComparison.Ordinal),
            "HUD did not expose committed movement and animation diagnostics");
        Require(hud.PerformanceText.Contains("FPS 60", StringComparison.Ordinal) &&
            hud.PerformanceText.Contains("Errors 0", StringComparison.Ordinal),
            "HUD did not expose runtime performance");
        Require(!hud.StateText.Contains("WASD", StringComparison.OrdinalIgnoreCase) &&
            !hud.PerformanceText.Contains("Space", StringComparison.OrdinalIgnoreCase),
            "HUD contained forbidden tutorial text");
        var minimumSize = hud.GetCombinedMinimumSize();
        Require(minimumSize.X <= 502f && minimumSize.Y <= 134f,
            $"HUD text exceeded its unframed demo bounds: {minimumSize}");
        for (var frame = 1; frame < 300; frame++)
        {
            hud.Refresh(diagnostics, framesPerSecond: 60d, errors: 0);
        }
        Require(hud.RefreshCallCount == 300, "HUD did not observe every render refresh request");
        Require(hud.StateFormatCount == 1,
            "HUD reformatted unchanged committed state more than once");
        Require(hud.PerformanceFormatCount <= 21,
            "HUD performance text was formatted more often than 4 Hz at 60 FPS");

        var successPublished = false;
        RequireThrows<InvalidOperationException>(
            () => P3LocomotionDemo.CompleteAfterCleanup(
                () => throw new InvalidOperationException("cleanup rejected"),
                () => successPublished = true),
            "demo completion swallowed a cleanup failure");
        Require(!successPublished, "demo success was published before cleanup completed");
        RemoveChild(hud);
        hud.Free();
    }

    private static void RequireKey(string action, Key physicalKey)
    {
        Require(InputMap.HasAction(action), $"missing input action {action}");
        var events = InputMap.ActionGetEvents(action);
        Require(events.Count == 1, $"action {action} did not have exactly one binding");
        Require(events[0] is InputEventKey key && key.PhysicalKeycode == physicalKey,
            $"action {action} was not bound to physical key {physicalKey}");
    }

    private static void RequireMouseButton(string action, MouseButton button)
    {
        Require(InputMap.HasAction(action), $"missing input action {action}");
        var events = InputMap.ActionGetEvents(action);
        Require(events.Count == 1, $"action {action} did not have exactly one binding");
        Require(events[0] is InputEventMouseButton mouse && mouse.ButtonIndex == button,
            $"action {action} was not bound to mouse button {button}");
    }

    private static void RequireThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
