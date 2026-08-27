using Godot;
using GodotAls.Core.Contracts;
using NumericsVector2 = System.Numerics.Vector2;

namespace GodotAls.Locomotion;

public readonly record struct AlsPlayerInputSnapshot(
    NumericsVector2 MovementAxes,
    bool WalkHeld,
    bool SprintHeld,
    bool CrouchTogglePressed,
    bool JumpPressed,
    bool RotationModeTogglePressed,
    bool AimHeld);

public sealed class AlsPlayerInputAdapter : IAlsLocomotionCommandSource
{
    private AlsLocomotionCommand _command = AlsLocomotionCommand.CreateDefault();
    private AlsStance _stance = AlsStance.Standing;
    private AlsRotationMode _rotationMode = AlsRotationMode.LookingDirection;
    private long _capturedFrameId;

    public long CapturedFrameId => _capturedFrameId;

    public void CaptureGodotFrame(long frameId, float viewYaw) =>
        CaptureFrame(frameId, ReadGodotSnapshot(), viewYaw);

    public void CaptureFrame(
        long frameId,
        in AlsPlayerInputSnapshot snapshot,
        float viewYaw)
    {
        if (frameId != _capturedFrameId + 1)
        {
            throw new InvalidOperationException(
                $"Player input frames must be captured consecutively. Last frame was " +
                $"{_capturedFrameId}, received {frameId}.");
        }
        if (!float.IsFinite(viewYaw) ||
            !float.IsFinite(snapshot.MovementAxes.X) ||
            !float.IsFinite(snapshot.MovementAxes.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(snapshot),
                "Player input axes and view yaw must be finite.");
        }

        if (snapshot.CrouchTogglePressed)
        {
            _stance = _stance == AlsStance.Standing
                ? AlsStance.Crouching
                : AlsStance.Standing;
        }
        if (snapshot.RotationModeTogglePressed && !snapshot.AimHeld)
        {
            _rotationMode = _rotationMode == AlsRotationMode.LookingDirection
                ? AlsRotationMode.VelocityDirection
                : AlsRotationMode.LookingDirection;
        }

        var axes = snapshot.MovementAxes;
        var lengthSquared = axes.LengthSquared();
        if (lengthSquared > 1f)
        {
            axes /= MathF.Sqrt(lengthSquared);
        }

        var gait = snapshot.WalkHeld
            ? AlsGait.Walking
            : snapshot.SprintHeld
                ? AlsGait.Sprinting
                : AlsGait.Running;
        _command = new AlsLocomotionCommand(
            axes,
            viewYaw,
            viewYaw,
            gait,
            _stance,
            snapshot.AimHeld ? AlsRotationMode.Aiming : _rotationMode,
            snapshot.JumpPressed ? (byte)1 : (byte)0);
        _capturedFrameId = frameId;
    }

    public AlsLocomotionCommand GetCommand(long frameId)
    {
        if (frameId != _capturedFrameId || _capturedFrameId == 0)
        {
            throw new InvalidOperationException(
                $"Player input command frame {frameId} was requested before its exact frame capture. " +
                $"Last captured frame is {_capturedFrameId}.");
        }
        return _command;
    }

    private static AlsPlayerInputSnapshot ReadGodotSnapshot()
    {
        var movement = Input.GetVector(
            "move_left",
            "move_right",
            "move_forward",
            "move_back");
        return new AlsPlayerInputSnapshot(
            new NumericsVector2(movement.X, movement.Y),
            Input.IsActionPressed("walk"),
            Input.IsActionPressed("sprint"),
            Input.IsActionJustPressed("crouch_toggle"),
            Input.IsActionJustPressed("jump"),
            Input.IsActionJustPressed("rotation_mode_toggle"),
            Input.IsActionPressed("aim"));
    }
}
