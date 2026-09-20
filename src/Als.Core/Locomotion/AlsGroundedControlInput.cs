using System.Numerics;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

// The enclosing idle Rotate/Turn checks supply their candidate outputs. Those
// checks and real Montage playback are separate from the moving update chain.
public readonly record struct AlsGroundedIdleControl(bool RotateLeft, bool RotateRight,
    float RotateRate, float RotationScale, float ElapsedDelayTime);
public readonly record struct AlsGroundedControlInput(AlsFrameIdentity Identity, AlsMovementUpdateGateState Gate,
    AlsMovementDirection MovementDirection, Vector4 Yaw, AlsGroundedIdleControl Idle);
public readonly record struct AlsGroundedControlUpdate(AlsGroundedControlInput State, AlsMovementUpdateGate Execution);

public sealed class AlsGroundedControlInputModel(AlsYawOffset yaw, AlsGroundedControlInput initialState)
{
    public AlsGroundedControlInput InitialState { get; } = initialState;

    public AlsGroundedControlUpdate Evaluate(in AlsFrameInput frame, AlsGait gait, AlsRotationMode mode,
        in AlsGroundedAnimationInput movement, in AlsGroundedControlInput previous, AlsGroundedIdleControl? idleOutput = null,
        AlsMovementStateInput? movementState = null)
    {
        if (frame.Identity.SlotGeneration == 0 || frame.Identity != movement.Identity || frame.Floor.IsGrounded > 1 ||
            (uint)gait > 2 || (uint)mode > 2 || (uint)previous.MovementDirection > 3 ||
            !float.IsFinite(frame.Command.AimYaw) || !float.IsFinite(frame.Command.ViewYaw) || !float.IsFinite(previous.Yaw.LengthSquared()) ||
            previous.Identity.SlotGeneration != 0 && (previous.Identity.CharacterId != frame.Identity.CharacterId ||
                previous.Identity.SlotGeneration != frame.Identity.SlotGeneration || previous.Identity.FrameId >= frame.Identity.FrameId))
            throw new ArgumentException("Invalid grounded control frame or history.");
        Validate(previous.Idle);
        // Leaving the enum's Grounded branch pauses, rather than resets, DoOnce.
        var state = movementState ?? (frame.Floor.IsGrounded == 1 ? AlsMovementStateInput.Grounded : AlsMovementStateInput.InAir);
        var execution = AlsMovementUpdateGate.Evaluate(previous.Gate, state == AlsMovementStateInput.Grounded, movement.ShouldMove);
        var next = previous with { Identity = frame.Identity, Gate = execution.State };
        if (execution.ChangedToTrue)
            next = next with { Idle = next.Idle with { ElapsedDelayTime = 0, RotateLeft = false, RotateRight = false } };
        if (execution.WhileTrue)
        {
            var aimAngle = AlsYawOffset.VelocityRelativeControlDegrees(frame.ActualVelocity, frame.Command.AimYaw);
            var direction = gait == AlsGait.Sprinting || mode == AlsRotationMode.VelocityDirection ? AlsMovementDirection.Forward :
                AlsMovementDirectionModel.CalculateQuadrant(previous.MovementDirection, aimAngle);
            next = next with { MovementDirection = direction,
                Yaw = yaw.Sample(AlsYawOffset.VelocityRelativeControlDegrees(frame.ActualVelocity, frame.Command.ViewYaw)) };
        }
        else if (execution.WhileFalse && idleOutput.HasValue)
        {
            Validate(idleOutput.Value); next = next with { Idle = idleOutput.Value };
        }
        return new(next, execution);
    }
    internal static void Validate(AlsGroundedIdleControl input)
    {
        if (!float.IsFinite(input.RotateRate) || !float.IsFinite(input.RotationScale) || !float.IsFinite(input.ElapsedDelayTime) || input.ElapsedDelayTime < 0)
            throw new ArgumentException("Invalid idle rotation candidate.");
    }
}
