namespace GodotAls.Core.Locomotion;

public readonly record struct AlsRefactoredDefaultOverlayInput(float Walking, float Standing,
    float Crouching, float InAir, float GroundPrediction);
public readonly record struct AlsRefactoredDefaultOverlayState(bool PredictionInitialized, float Prediction);

/// <summary>The original Default Overlay's three TwoWayBlend nodes and normalized
/// MultiWayBlend. State belongs to the character's candidate frame.</summary>
public static class AlsRefactoredDefaultOverlay
{
    public static AlsRefactoredDefaultOverlayState Advance(in AlsRefactoredDefaultOverlayState previous,
        in AlsRefactoredDefaultOverlayInput input, float delta)
    {
        if (!float.IsFinite(delta) || delta < 0 || !Unit(input.Walking) || !Unit(input.Standing) ||
            !Unit(input.Crouching) || !Unit(input.InAir) || !Unit(input.GroundPrediction))
            throw new ArgumentException("Invalid Default Overlay input.");
        // TwoWayBlend does not update its irrelevant child. Reactivation does not
        // reset this node (bResetChildOnActivation=false).
        if (input.InAir <= AlsPoseBlender.WeightThreshold) return previous;
        var target = input.GroundPrediction;
        if (!previous.PredictionInitialized) return new(true, target);
        var distance = target - previous.Prediction;
        var speed = distance >= 0 ? 20f : 5f;
        var next = distance * distance < 1e-8f ? target :
            previous.Prediction + distance * System.Math.Clamp(delta * speed, 0, 1);
        return new(true, next);
    }

    public static AlsPrecisePose Bone(ReadOnlySpan<AlsPrecisePose> poses, in AlsPrecisePose reference,
        in AlsRefactoredDefaultOverlayInput input, in AlsRefactoredDefaultOverlayState state)
    {
        if (poses.Length != 3) throw new ArgumentException("Default Overlay needs three authored frames.");
        var total = input.Standing + input.Crouching;
        var ground = reference;
        if (total > AlsPoseBlender.WeightThreshold)
        {
            var a = input.Standing / total; var b = input.Crouching / total;
            var hasA = a > AlsPoseBlender.WeightThreshold; var hasB = b > AlsPoseBlender.WeightThreshold;
            if (hasA) ground = AlsPrecisePoseBlender.Scale(Two(poses[0], poses[1], input.Walking), a);
            if (hasB) ground = hasA ? AlsPrecisePoseBlender.Accumulate(ground, poses[2], b) : AlsPrecisePoseBlender.Scale(poses[2], b);
            ground = ground.Normalized();
        }
        return Two(ground, Two(poses[1], poses[0], state.Prediction), input.InAir);
    }

    public static AlsInertialCurve Curve(ReadOnlySpan<AlsInertialCurve> curves,
        in AlsRefactoredDefaultOverlayInput input, in AlsRefactoredDefaultOverlayState state)
    {
        if (curves.Length != 3) throw new ArgumentException("Default Overlay needs three authored curve frames.");
        var total = input.Standing + input.Crouching;
        var ground = default(AlsInertialCurve);
        if (total > AlsPoseBlender.WeightThreshold)
        {
            var a = input.Standing / total; var b = input.Crouching / total;
            if (a > AlsPoseBlender.WeightThreshold)
                ground = AlsStandingCycleCurves.Scale(TwoCurve(curves[0], curves[1], input.Walking), a);
            ground = AlsStandingCycleCurves.Accumulate(ground, curves[2], b);
        }
        return TwoCurve(ground, TwoCurve(curves[1], curves[0], state.Prediction), input.InAir);
    }

    private static AlsPrecisePose Two(in AlsPrecisePose a, in AlsPrecisePose b, float alpha)
    {
        if (alpha <= AlsPoseBlender.WeightThreshold) return a;
        if (alpha >= 1 - AlsPoseBlender.WeightThreshold) return b;
        // TwoWayBlend passes 1-alpha into BlendTwoPosesTogetherInPlace. That
        // routine subtracts again in float for B; using alpha directly changes
        // both translations and scales even though the algebra looks identical.
        var weightA = 1f - alpha;
        var weightB = 1f - weightA;
        return AlsPrecisePoseBlender.Accumulate(AlsPrecisePoseBlender.Scale(a, weightA), b, weightB).Normalized();
    }
    private static AlsInertialCurve TwoCurve(AlsInertialCurve a, AlsInertialCurve b, float alpha) =>
        alpha <= AlsPoseBlender.WeightThreshold ? a : alpha >= 1 - AlsPoseBlender.WeightThreshold ? b :
            AlsStandingCycleCurves.Lerp(a, b, 1f - (1f - alpha));
    private static bool Unit(float x) => float.IsFinite(x) && x is >= 0 and <= 1;
}
