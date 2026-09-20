using System.Numerics;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public enum AlsCycleDirection : byte { Forward, Backward, LeftForward, LeftBackward, RightForward, RightBackward }

public readonly record struct AlsStandingCycleState(
    Vector4 VelocityBlend,
    AlsCycleDirection Direction,
    AlsCycleDirection PreviousDirection,
    float TransitionElapsed,
    float TransitionDuration,
    bool HipTransition,
    float Phase,
    float MovingWeight,
    float GaitWeight,
    float Stride,
    float PlayRate,
    float Crossing,
    float HipBias,
    float BlendAlpha,
    bool WaitingForFeet)
{
    public bool IsTransitioning => TransitionDuration > 0 && TransitionElapsed < TransitionDuration;
    public Vector2 FilteredBlendInput { get; init; }
    public int ActiveTransitionCount { get; init; }
    public int TransitionsStartedThisFrame { get; init; }
    public float CurrentStateWeight { get; init; }
    public AlsMovementDirection MovementDirection { get; init; }
}

public static class AlsStandingCycle
{
    public static AlsStandingCycleState AdvanceDirection(in AlsStandingCycleState previous,
        Vector2 velocity, float delta, float crossing, float hipBias, float currentStateWeight = 1,
        float aimRelativeYaw = 0, AlsGait gait = AlsGait.Walking,
        AlsRotationMode rotationMode = AlsRotationMode.LookingDirection, Vector4? globalVelocityBlend = null,
        AlsMovementDirection? globalMovementDirection = null)
    {
        if (!float.IsFinite(delta) || delta < 0 || !float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y) ||
            !float.IsFinite(crossing) || !float.IsFinite(hipBias) || !float.IsFinite(currentStateWeight) ||
            currentStateWeight is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(delta));
        var sum = MathF.Abs(velocity.X) + MathF.Abs(velocity.Y);
        var weights = globalVelocityBlend.GetValueOrDefault();
        if (globalVelocityBlend.HasValue)
        {
            if (!float.IsFinite(weights.LengthSquared()) || weights.X < 0 || weights.Y < 0 || weights.Z < 0 || weights.W < 0)
                throw new ArgumentException("Invalid global direction input.");
        }
        else
        {
            // Legacy entry; the unified owner supplies its already updated weights above.
            var target = sum > 1e-6f
                ? new Vector4(MathF.Max(velocity.Y, 0), MathF.Max(-velocity.Y, 0),
                    MathF.Max(-velocity.X, 0), MathF.Max(velocity.X, 0)) / sum
                : previous.VelocityBlend;
            var amount = System.Math.Clamp(delta * 12f, 0, 1);
            weights = new Vector4(Interpolate(previous.VelocityBlend.X, target.X, amount),
                Interpolate(previous.VelocityBlend.Y, target.Y, amount),
                Interpolate(previous.VelocityBlend.Z, target.Z, amount),
                Interpolate(previous.VelocityBlend.W, target.W, amount));
            if (weights == Vector4.Zero) weights = new Vector4(1, 0, 0, 0);
            weights /= weights.X + weights.Y + weights.Z + weights.W;
        }
        var movement = globalMovementDirection ?? AlsMovementDirectionModel.Calculate(previous.MovementDirection, velocity, aimRelativeYaw, gait, rotationMode);
        if ((uint)movement > 3) throw new ArgumentException("Invalid global movement direction.");
        var next = previous with { VelocityBlend = weights, Crossing = crossing, HipBias = hipBias, MovementDirection = movement,
            TransitionElapsed = MathF.Min(previous.TransitionElapsed + delta, previous.TransitionDuration), WaitingForFeet = false };
        return sum > 1e-6f ? EvaluateDirection(next, currentStateWeight) : next;
    }

    public static AlsStandingCycleState EvaluateDirection(in AlsStandingCycleState state,
        float currentStateWeight)
    {
        if (!float.IsFinite(currentStateWeight) || currentStateWeight < 0 || currentStateWeight > 1 ||
            (uint)state.MovementDirection > 3) throw new ArgumentOutOfRangeException(nameof(currentStateWeight));
        var next = state;
        var hipBias = state.HipBias;
        var crossing = state.Crossing;
        var direction = next.Direction;
        var requested = state.MovementDirection == AlsMovementDirection.Forward ? direction switch
            {
                AlsCycleDirection.LeftBackward => AlsCycleDirection.LeftForward,
                AlsCycleDirection.RightBackward => AlsCycleDirection.RightForward,
                _ => AlsCycleDirection.Forward,
            } : state.MovementDirection == AlsMovementDirection.Backward ? direction switch
            {
                AlsCycleDirection.LeftForward => AlsCycleDirection.LeftBackward,
                AlsCycleDirection.RightForward => AlsCycleDirection.RightBackward,
                _ => AlsCycleDirection.Backward,
            } :
            state.MovementDirection == AlsMovementDirection.Left ? direction switch
            {
                AlsCycleDirection.Backward or AlsCycleDirection.RightForward or AlsCycleDirection.LeftBackward => AlsCycleDirection.LeftBackward,
                _ => AlsCycleDirection.LeftForward,
            } : direction switch
            {
                AlsCycleDirection.Backward or AlsCycleDirection.LeftForward or AlsCycleDirection.RightBackward => AlsCycleDirection.RightBackward,
                _ => AlsCycleDirection.RightForward,
            };
        var hipTransition = false;
        if (requested == direction && state.MovementDirection is AlsMovementDirection.Left or AlsMovementDirection.Right)
        {
            // V4 Hips_Right/Hips_Left refer to the hips, not movement direction.
            // Exact +/-0.5 values satisfy neither the neutral nor biased rules.
            var neutral = MathF.Abs(hipBias) < 0.5f && currentStateWeight == 1f;
            var preferred = direction switch
            {
                AlsCycleDirection.LeftForward when hipBias > 0.5f => AlsCycleDirection.LeftBackward,
                AlsCycleDirection.LeftBackward when neutral || hipBias < -0.5f => AlsCycleDirection.LeftForward,
                AlsCycleDirection.RightForward when hipBias < -0.5f => AlsCycleDirection.RightBackward,
                AlsCycleDirection.RightBackward when neutral || hipBias > 0.5f => AlsCycleDirection.RightForward,
                _ => direction,
            };
            if (preferred != direction)
            {
                next = next with { WaitingForFeet = crossing != 0f };
                if (!next.WaitingForFeet) { requested = preferred; hipTransition = true; }
            }
        }
        if (requested == direction) return next;
        return next with { PreviousDirection = direction, Direction = requested, TransitionElapsed = 0,
            TransitionDuration = hipTransition ? 0.75f : OrdinaryTransitionDuration(direction, requested), HipTransition = hipTransition };
    }

    // ALS_AnimBP (N) Directional States: six Pivot edges, twelve direction edges.
    // The profile is present on both groups, not only custom hip transitions.
    private static float OrdinaryTransitionDuration(AlsCycleDirection from, AlsCycleDirection to) => (from, to) switch
    {
        (AlsCycleDirection.Forward, AlsCycleDirection.Backward) or
        (AlsCycleDirection.Backward, AlsCycleDirection.Forward) or
        (AlsCycleDirection.LeftForward, AlsCycleDirection.RightBackward) or
        (AlsCycleDirection.RightBackward, AlsCycleDirection.LeftForward) or
        (AlsCycleDirection.RightForward, AlsCycleDirection.LeftBackward) or
        (AlsCycleDirection.LeftBackward, AlsCycleDirection.RightForward) => 0.5f,
        _ => 0.7f,
    };

    public static float TransitionAlpha(in AlsStandingCycleState state, bool profileBones) =>
        profileBones ? ProfileAlpha(state.BlendAlpha, 2) : state.BlendAlpha;

    public static float DirectionWeight(AlsCycleDirection state, int clipDirection, Vector4 velocity)
    {
        if (clipDirection == 0) return velocity.X;
        if (clipDirection == 1) return velocity.Y;
        var leftBack = state is AlsCycleDirection.Backward or AlsCycleDirection.RightForward or AlsCycleDirection.LeftBackward;
        var rightBack = state is AlsCycleDirection.Backward or AlsCycleDirection.LeftForward or AlsCycleDirection.RightBackward;
        return clipDirection switch
        {
            2 => leftBack ? 0 : velocity.Z,
            3 => leftBack ? velocity.Z : 0,
            4 => rightBack ? 0 : velocity.W,
            5 => rightBack ? velocity.W : 0,
            _ => throw new ArgumentOutOfRangeException(nameof(clipDirection)),
        };
    }

    // Keep native minimum weights at both endpoints; callers composing poses must also use
    // the independently normalized outgoing weight rather than reconstructing it as 1-alpha.
    public static float ProfileAlpha(float alpha, float factor) =>
        AlsGroundedPoseBlend.WeightFactor(alpha, factor, true).X;

    private static float Interpolate(float current, float target, float alpha) =>
        (target - current) * (target - current) < 1e-8f ? target : current + (target - current) * alpha;
}
