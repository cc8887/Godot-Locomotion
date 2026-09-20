using System.Numerics;

namespace GodotAls.Core.Locomotion;

/// <summary>Source-curve payload composition. Direction ModifyCurve inputs are a separate graph stage.
/// Preserve native curve presence and node ordering; bone BlendProfiles do not weight curves.</summary>
public static class AlsStandingCycleCurves
{
    public static AlsInertialCurve ModifyBlend(AlsInertialCurve source, float value, float alpha)
    {
        if (!float.IsFinite(value) || !float.IsFinite(alpha)) throw new ArgumentException("Invalid ModifyCurve input.");
        var current = source.Present ? source.Value : 0;
        // ModifyCurve uses FMath::Lerp even at the endpoints, and inserts a missing name.
        return new(current + System.Math.Clamp(alpha, 0, 1) * (value - current));
    }

    public static AlsInertialCurve Scale(AlsInertialCurve source, float weight) =>
        source.Present ? new(source.Value * weight) : default;

    // ModifyCurve/Scale writes its named output even when the input is absent.
    // Ordinary pose contribution scaling above must preserve absence instead.
    public static AlsInertialCurve ModifyScale(AlsInertialCurve source,float scale) =>
        float.IsFinite(scale) ? new((source.Present ? source.Value : 0)*scale) :
        throw new ArgumentException("Invalid ModifyCurve scale.");

    public static AlsInertialCurve Accumulate(AlsInertialCurve value, AlsInertialCurve source, float weight)
    {
        // FBlendedCurve::Accumulate does not union the incoming names below relevance.
        if (weight <= AlsPoseBlender.WeightThreshold || !source.Present) return value;
        return new((value.Present ? value.Value : 0) + source.Value * weight);
    }

    public static AlsInertialCurve Lerp(AlsInertialCurve first, AlsInertialCurve second, float alpha)
    {
        if (MathF.Abs(alpha) <= AlsPoseBlender.WeightThreshold) return first;
        if (MathF.Abs(alpha - 1) <= AlsPoseBlender.WeightThreshold) return second;
        if (!first.Present && !second.Present) return default;
        var a = first.Present ? first.Value : 0;
        var b = second.Present ? second.Value : 0;
        return new(a + alpha * (b - a));
    }

    public static void Compose(ReadOnlySpan<AlsInertialCurve> directions, ReadOnlySpan<AlsInertialCurve> idle,
        Vector4 velocity, in AlsTransitionStackState transitions, float movingWeight,
        ReadOnlySpan<int> directionInputs, Span<AlsInertialCurve> output, ReadOnlySpan<Vector4> stateWeights = default,
        int yawCurveIndex = -1, ReadOnlySpan<float> stateYaw = default)
    {
        if (directions.Length != output.Length * 6 || idle.Length != output.Length || directionInputs.Length != 24 ||
            (uint)transitions.CurrentState >= 6 || !float.IsFinite(movingWeight) || movingWeight is < 0 or > 1 ||
            !float.IsFinite(velocity.LengthSquared()) || velocity.X < 0 || velocity.Y < 0 || velocity.Z < 0 || velocity.W < 0 ||
            !float.IsFinite(velocity.X + velocity.Y + velocity.Z + velocity.W))
            throw new ArgumentException("Invalid Standing source curve layout or state.");
        foreach (var role in directionInputs) if ((uint)role >= 6) throw new ArgumentException("Invalid direction curve input.");
        if (!stateWeights.IsEmpty && stateWeights.Length != 6) throw new ArgumentException("Invalid independent direction curve inputs.");
        if (yawCurveIndex < -1 || yawCurveIndex >= output.Length ||
            (yawCurveIndex >= 0 ? stateYaw.Length != 6 : !stateYaw.IsEmpty))
            throw new ArgumentException("Invalid direction YawOffset layout.");
        foreach (var yaw in stateYaw) if (!float.IsFinite(yaw)) throw new ArgumentException("Invalid direction YawOffset value.");
        velocity = AlsStandingCyclePose.NormalizeVelocityWeights(velocity);
        Span<AlsInertialCurve> states = stackalloc AlsInertialCurve[6];
        for (var curve = 0; curve < output.Length; curve++)
        {
            for (var state = 0; state < 6; state++)
            {
                var value = default(AlsInertialCurve); var first = true;
                for (var axis = 0; axis < 4; axis++)
                {
                    var weight = stateWeights.IsEmpty ? velocity[axis] : stateWeights[state][axis];
                    if (weight <= AlsPoseBlender.WeightThreshold) continue;
                    var source = directions[directionInputs[state * 4 + axis] * output.Length + curve];
                    value = first ? Scale(source, weight) : Accumulate(value, source, weight);
                    first = false;
                }
                states[state] = curve == yawCurveIndex ? ModifyBlend(value, stateYaw[state], 1) : value;
            }
            var result = states[transitions.Count == 0 ? transitions.CurrentState : transitions.GetTransition(0).From];
            for (var i = 0; i < transitions.Count; i++)
            {
                var edge = transitions.GetTransition(i);
                // StateMachine uses Override + Accumulate, not the Lerp used by TwoWayBlend.
                result = Accumulate(Scale(result, 1 - edge.Alpha), states[edge.To], edge.Alpha);
            }
            output[curve] = Lerp(idle[curve], result, movingWeight);
        }
    }
}
