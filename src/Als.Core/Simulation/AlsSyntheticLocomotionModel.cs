using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Core.Simulation;

public static class AlsSyntheticLocomotionModel
{
    public static void Evaluate(
        in AlsFrameInput input,
        ref AlsRuntimeState state,
        ref AlsFrameResult result)
    {
        result = AlsFrameResult.CreateDefault(input.Identity);
        state.LocomotionState = input.Floor.IsGrounded != 0
            ? AlsLocomotionState.Grounded
            : AlsLocomotionState.InAir;
        state.SmoothedVelocity = input.ActualVelocity;
        state.SmoothedAcceleration = input.ActualAcceleration;
        state.AnimationPhase = (state.AnimationPhase + (input.DeltaTime * (0.5f + input.DesiredSpeed))) % 1f;

        var direction = input.InputDirection.LengthSquared() > 0f
            ? Vector3.Normalize(input.InputDirection)
            : Vector3.Zero;

        result.ResolvedLocomotionState = state.LocomotionState;
        result.RequestedDriveMode = input.CurrentDriveMode;
        result.MovementIntent = direction * input.DesiredSpeed;
        result.RotationIntent = input.AimRotation;
        result.PelvisTarget = new Vector3(
            0f,
            MathF.Sin(state.AnimationPhase * MathF.Tau) * 0.05f,
            0f);

        if (input.Identity.FrameId % 30 == 0 &&
            !result.TypedEvents.TryAdd(new AlsAnimationEvent(
                1,
                -1,
                -1,
                -1,
                0,
                0,
                0,
                0,
                0,
                state.AnimationPhase,
                1f,
                AlsTimelineEventKind.Generic,
                AlsAnimationEventPhase.Trigger,
                default)))
        {
            result.ErrorCode = 1;
        }
    }
}
