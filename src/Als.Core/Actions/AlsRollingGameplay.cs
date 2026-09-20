using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Actions;

public readonly record struct AlsRollingState(long RequestId, long InstanceId, float TargetYawDegrees)
{
    public bool Active => InstanceId > 0;
}

public readonly record struct AlsRollingStartContext(bool Grounded, AlsTimelineAction Action);

// ALS-Refactored b754d6f: AlsCharacter_Actions.cpp and FAlsRollingSettings C++ defaults.
// Angles use UE world yaw (degrees). Ragdoll transitions need the physical action.
public static class AlsRollingGameplay
{
    public const float RotationHalfLife = .1f;

    public static bool CanStart(in AlsRollingStartContext context, bool montagePlaying) =>
        context.Grounded && (context.Action == AlsTimelineAction.None ||
            context.Action == AlsTimelineAction.Rolling && !montagePlaying);

    public static float Rotate(float current, float target, float delta, float halfLife = RotationHalfLife)
    {
        if (!float.IsFinite(current) || !float.IsFinite(target) || !float.IsFinite(delta) || delta <= 0 ||
            !float.IsFinite(halfLife) || halfLife < 0) throw new ArgumentException("Invalid rolling rotation.");
        if (halfLife == 0) return target;
        var difference = Unwind(target - current);
        if (MathF.Abs(difference) <= .0001f) return target;
        // UAlsRotation deliberately chooses counterclockwise near a half-turn.
        if (difference > 175f) difference -= 360f;
        return Unwind(current + difference * AlsRefactoredRigMath.DamperAlpha(delta, halfLife));
    }

    public static float TargetYaw(in AlsFrameInput frame) => frame.InputDirection.X != 0 || frame.InputDirection.Z != 0
        ? (float)(System.Math.Atan2(frame.InputDirection.X, -frame.InputDirection.Z) * (180 / System.Math.PI))
        : -frame.CharacterYaw * (180f / MathF.PI);

    public static AlsRollingState ApplyOutcomes(AlsRollingState previous, in AlsFrameInput frame,
        in AlsActionOutcomeBuffer outcomes)
    {
        var next = previous;
        for (var i = 0; i < outcomes.Count; i++)
        {
            var outcome = outcomes[i];
            if (outcome.ResultCode == AlsActionResultCode.Accepted)
                next = new(outcome.RequestId, outcome.PlaybackEpoch, frame.ActionParameters.HasTargetYaw
                    ? frame.ActionParameters.TargetYawDegrees : TargetYaw(frame));
            else if (outcome.PlaybackEpoch == next.InstanceId && outcome.RequestId == next.RequestId &&
                (outcome.ResultCode == AlsActionResultCode.Completed || outcome.ResultCode >= AlsActionResultCode.InterruptedByReplacement))
                next = default;
        }
        return next;
    }

    public static AlsRollingState ApplyNotifies(AlsRollingState state, in AlsEventBuffer events)
    {
        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            if (e.Kind == AlsTimelineEventKind.SetAction && e.Phase == AlsAnimationEventPhase.End &&
                e.Payload.EnumValue0 == (int)AlsTimelineAction.Rolling && e.PlaybackEpoch == state.InstanceId)
                state = default;
        }
        return state;
    }

    private static float Unwind(float angle)
    {
        angle %= 360f;
        if (angle > 180f) angle -= 360f;
        if (angle < -180f) angle += 360f;
        return angle;
    }
}
