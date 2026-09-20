using System.Numerics;
using System.Runtime.CompilerServices;

namespace GodotAls.Core.Locomotion;

[InlineArray(6)] public struct AlsStandingDirectionWeights { private Vector4 _element; }
[InlineArray(6)] public struct AlsStandingDirectionYawValues { private float _element; }

public struct AlsStandingDirectionInputState
{
    public byte InitializedStates;
    public byte UpdatedStates;
    public AlsStandingDirectionWeights Desired;
    public AlsStandingDirectionWeights Cached;
    public AlsStandingDirectionYawValues Yaw;
}

/// <summary>Six independent MultiWayBlend exposed-input and cached-alpha histories.
/// Initialize recomputes from retained DesiredAlphas; only Update reads the current input.</summary>
public static class AlsStandingDirectionInputs
{
    // ModifyCurve Blend-mode initialization preserves its exposed CurveValues. Only nodes
    // actually visited by Update capture the current AnimInstance variables.
    public static AlsStandingDirectionInputState CaptureYaw(in AlsStandingDirectionInputState input,
        Vector4 yaw, ReadOnlySpan<int> axes)
    {
        if (axes.Length != 6 || (input.InitializedStates | input.UpdatedStates) > 63 ||
            (input.UpdatedStates & ~input.InitializedStates) != 0 ||
            !float.IsFinite(yaw.X) || !float.IsFinite(yaw.Y) || !float.IsFinite(yaw.Z) || !float.IsFinite(yaw.W))
            throw new ArgumentException("Invalid Standing yaw inputs.");
        var result = input;
        for (var state = 0; state < 6; state++)
        {
            if ((uint)axes[state] >= 4) throw new ArgumentException("Invalid Standing yaw axis.");
            if ((input.UpdatedStates & (1 << state)) != 0) result.Yaw[state] = yaw[axes[state]];
        }
        return result;
    }

    public static AlsStandingDirectionInputState Prepare(in AlsStandingDirectionInputState previous, byte initialize,
        bool update, Vector4 desired, in AlsTransitionStackState beforeCleanup, in AlsTransitionStackState evaluated)
    {
        if ((initialize | previous.InitializedStates) > 63 || (uint)evaluated.CurrentState >= 6)
            throw new ArgumentException("Invalid Standing input state.");
        if (update) _ = Normalize(desired);
        var result = previous; result.UpdatedStates = 0;
        for (var state = 0; state < 6; state++)
            if ((initialize & (1 << state)) != 0)
            {
                result.Cached[state] = Normalize(result.Desired[state]);
                result.InitializedStates |= (byte)(1 << state);
            }
        if (!update) return result;
        // Native StateMachine updates unfinished transitions before removing completed entries.
        for (var i = 0; i < beforeCleanup.Count; i++)
        {
            var edge = beforeCleanup.GetTransition(i);
            if (edge.Complete) continue;
            Visit(edge.From, desired, ref result); Visit(edge.To, desired, ref result);
        }
        if (evaluated.Count == 0) Visit(evaluated.CurrentState, desired, ref result);
        return result;
    }

    private static void Visit(int state, Vector4 desired, ref AlsStandingDirectionInputState result)
    {
        if ((uint)state >= 6 || (result.InitializedStates & (1 << state)) == 0)
            throw new ArgumentException("Direction node updated before initialization.");
        var bit = (byte)(1 << state);
        if ((result.UpdatedStates & bit) != 0) return;
        result.UpdatedStates |= bit; result.Desired[state] = desired; result.Cached[state] = Normalize(desired);
    }

    public static Vector4 Normalize(Vector4 value)
    {
        var total = value.X + value.Y + value.Z + value.W;
        if (!float.IsFinite(total) || !float.IsFinite(value.LengthSquared()) ||
            value.X < 0 || value.Y < 0 || value.Z < 0 || value.W < 0)
            throw new ArgumentException("Invalid MultiWayBlend desired alpha.");
        return total > AlsPoseBlender.WeightThreshold ? value / total : Vector4.Zero;
    }
}
