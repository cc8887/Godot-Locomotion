using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;

namespace GodotAls.Locomotion;

public partial class P3DemoInputSmoke : Node
{
    private bool _finished;

    public override void _Ready()
    {
        try
        {
            ValidateActionMap();
            ValidateGodotInputBridge();
            ValidateCommandSemantics();
            ValidateCameraSemantics();
            ValidateHudSemantics();
            Require(typeof(P3LocomotionDemo).IsSubclassOf(typeof(Node3D)),
                "demo root was not a Node3D production scene root");
            GD.Print("GODOT_ALS_P3_DEMO_INPUT_OK actions=11 frames=8 camera=1 hud=1");
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
            AimHeld: false), 0.25f);
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

        adapter.CaptureFrame(2, new AlsPlayerInputSnapshot(
            -NumericsVector2.UnitY,
            WalkHeld: false,
            SprintHeld: true,
            CrouchTogglePressed: true,
            JumpPressed: true,
            RotationModeTogglePressed: true,
            AimHeld: false), -0.5f);
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
            AimHeld: false), -0.5f);
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
            AimHeld: true), 1f);
        Require(adapter.GetCommand(4).RequestedRotationMode == AlsRotationMode.Aiming,
            "RMB did not enter aiming");

        adapter.CaptureFrame(5, new AlsPlayerInputSnapshot(
            NumericsVector2.Zero,
            WalkHeld: false,
            SprintHeld: false,
            CrouchTogglePressed: false,
            JumpPressed: false,
            RotationModeTogglePressed: true,
            AimHeld: true), 1f);
        Require(adapter.GetCommand(5).RequestedRotationMode == AlsRotationMode.Aiming,
            "V changed rotation mode while aiming");

        adapter.CaptureFrame(6, new AlsPlayerInputSnapshot(
            NumericsVector2.Zero,
            WalkHeld: false,
            SprintHeld: false,
            CrouchTogglePressed: false,
            JumpPressed: false,
            RotationModeTogglePressed: false,
            AimHeld: false), 1f);
        Require(adapter.GetCommand(6).RequestedRotationMode == AlsRotationMode.VelocityDirection,
            "RMB release did not restore the prior rotation mode");

        adapter.CaptureFrame(7, new AlsPlayerInputSnapshot(
            NumericsVector2.Zero,
            WalkHeld: true,
            SprintHeld: true,
            CrouchTogglePressed: true,
            JumpPressed: false,
            RotationModeTogglePressed: true,
            AimHeld: false), 1f);
        var bothModifiers = adapter.GetCommand(7);
        Require(bothModifiers.RequestedGait == AlsGait.Walking,
            "walk did not take precedence over sprint");
        Require(bothModifiers.RequestedStance == AlsStance.Standing,
            "second Ctrl edge did not restore standing");
        Require(bothModifiers.RequestedRotationMode == AlsRotationMode.LookingDirection,
            "second V edge did not restore looking direction");

        RequireThrows<InvalidOperationException>(
            () => adapter.CaptureFrame(7, default, 0f),
            "duplicate frame capture was accepted");
        RequireThrows<InvalidOperationException>(
            () => adapter.CaptureFrame(9, default, 0f),
            "skipped frame capture was accepted");
        RequireThrows<InvalidOperationException>(
            () => adapter.GetCommand(8),
            "uncaptured command frame was accepted");

        adapter.CaptureFrame(8, default, 0f);
        Require(adapter.GetCommand(8).JumpPressed == 0, "default snapshot emitted a jump edge");
    }

    private static void ValidateGodotInputBridge()
    {
        ReleaseControlledActions();
        try
        {
            var adapter = new AlsPlayerInputAdapter();

            Input.ActionPress("move_forward");
            adapter.CaptureGodotFrame(1, 0f);
            var forward = adapter.GetCommand(1);
            Require(forward.MovementAxes == NumericsVector2.UnitY,
                "W did not map to ALS local forward +Y");
            var resolvedForward = AlsLocomotionCommandResolver.Resolve(
                forward, AlsStance.Standing);
            Require(resolvedForward.WorldDirection.X == 0f &&
                resolvedForward.WorldDirection.Y == 0f &&
                resolvedForward.WorldDirection.Z == -1f,
                "W did not resolve to Godot world forward -Z");
            Input.ActionRelease("move_forward");

            Input.ActionPress("move_back");
            adapter.CaptureGodotFrame(2, 0f);
            var backward = adapter.GetCommand(2);
            Require(backward.MovementAxes == -NumericsVector2.UnitY,
                "S did not map to ALS local backward -Y");
            var resolvedBackward = AlsLocomotionCommandResolver.Resolve(
                backward, AlsStance.Standing);
            Require(resolvedBackward.WorldDirection.X == 0f &&
                resolvedBackward.WorldDirection.Y == 0f &&
                resolvedBackward.WorldDirection.Z == 1f,
                "S did not resolve to Godot world backward +Z");
            Input.ActionRelease("move_back");

            Input.ActionPress("move_forward");
            Input.ActionPress("sprint");
            adapter.CaptureGodotFrame(3, 0f);
            var sprint = adapter.GetCommand(3);
            Require(sprint.RequestedGait == AlsGait.Sprinting,
                "Shift+W did not request sprinting");
            Require(AlsLocomotionCommandResolver.Resolve(sprint, AlsStance.Standing)
                    .MaxAllowedGait == AlsGait.Sprinting,
                "Shift+W did not satisfy LookingDirection forward sprint conditions");
            Input.ActionRelease("sprint");
            Input.ActionRelease("move_forward");

            adapter.CaptureGodotFrame(4, 0f);
            var cleared = adapter.GetCommand(4);
            Require(cleared.MovementAxes == NumericsVector2.Zero &&
                cleared.RequestedGait == AlsGait.Running &&
                cleared.RequestedStance == AlsStance.Standing &&
                cleared.RequestedRotationMode == AlsRotationMode.LookingDirection &&
                cleared.JumpPressed == 0,
                "released Godot actions contaminated the next command frame");
        }
        finally
        {
            ReleaseControlledActions();
        }

        foreach (var action in ControlledActions)
        {
            Require(!Input.IsActionPressed(action),
                $"headless input smoke leaked pressed action {action}");
        }
    }

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

    private void ValidateCameraSemantics()
    {
        var target = new Node3D { Name = "CameraTarget" };
        AddChild(target);
        target.GlobalPosition = new Vector3(3f, 2f, -4f);
        var orbit = new AlsOrbitCamera { Name = "OrbitCamera" };
        var springArm = new SpringArm3D { Name = "SpringArm3D" };
        springArm.AddChild(new Camera3D { Name = "Camera3D" });
        orbit.AddChild(springArm);
        AddChild(orbit);
        orbit.Configure(target);
        orbit._Process(0d);
        Require(orbit.Target == target, "orbit camera did not retain its live target");
        Require(orbit.GlobalPosition == target.GlobalPosition + orbit.FollowOffset,
            "orbit camera did not follow its target at shoulder height");

        var initialPitch = orbit.Pitch;
        orbit.ApplyMouseMotion(new Vector2(0f, 100f));
        Require(orbit.Pitch < initialPitch, "mouse down did not pitch the camera down");
        var downwardPitch = orbit.Pitch;
        orbit.ApplyMouseMotion(new Vector2(0f, -200f));
        Require(orbit.Pitch > downwardPitch, "mouse up did not pitch the camera up");
        orbit.ApplyMouseMotion(new Vector2(1000f, 10000f));
        Require(orbit.Pitch == orbit.MinimumPitch, "orbit pitch minimum was not clamped");
        var firstYaw = orbit.Yaw;
        orbit.ApplyMouseMotion(new Vector2(-1000f, -20000f));
        Require(orbit.Pitch == orbit.MaximumPitch, "orbit pitch maximum was not clamped");
        Require(orbit.Yaw != firstYaw, "horizontal mouse motion did not change orbit yaw");

        orbit.SetMouseCaptured(false);
        orbit.ToggleMouseCapture();
        Require(orbit.IsMouseCaptured, "Esc capture toggle did not capture the mouse");
        orbit.ToggleMouseCapture();
        Require(!orbit.IsMouseCaptured, "second Esc capture toggle did not release the mouse");

        RemoveChild(orbit);
        orbit.Free();
        RemoveChild(target);
        target.Free();
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
            0);
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
