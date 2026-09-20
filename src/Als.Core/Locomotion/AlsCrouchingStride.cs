namespace GodotAls.Core.Locomotion;

public readonly record struct AlsCrouchingStrideSettings(float IncreasingSpeed, float DecreasingSpeed);
public readonly record struct AlsCrouchingStrideState(bool Initialized, float Interpolated, float Alpha)
{
    public bool WalkPoseRelevant => Alpha < 1 - AlsPoseBlender.WeightThreshold;
    public bool DirectionRelevant => Alpha > AlsPoseBlender.WeightThreshold;
    public float WalkPoseUpdateWeight => DirectionRelevant ? (WalkPoseRelevant ? 1 - Alpha : 0) : 1;
    public float DirectionUpdateWeight => DirectionRelevant ? (WalkPoseRelevant ? Alpha : 1) : 0;
}

/// <summary>CLF TwoWayBlend: filtered float alpha, no child reset and no independent source time.
/// The owner initializes with default state and updates relevant children in A/B pin order.</summary>
public static class AlsCrouchingStride
{
    public static AlsCrouchingStrideState Advance(in AlsCrouchingStrideState previous, float stride,
        float delta, in AlsCrouchingStrideSettings settings)
    {
        if (!float.IsFinite(stride) || !float.IsFinite(delta) || delta < 0 ||
            !float.IsFinite(settings.IncreasingSpeed) || !float.IsFinite(settings.DecreasingSpeed))
            throw new ArgumentException("Invalid Crouching stride input/settings.");
        Validate(previous, allowUninitialized: true);
        var result = stride;
        if (previous.Initialized)
        {
            var speed = stride >= previous.Interpolated ? settings.IncreasingSpeed : settings.DecreasingSpeed;
            var distance = stride - previous.Interpolated;
            if (!float.IsFinite(distance)) throw new ArgumentException("Crouching stride difference overflowed.");
            if (speed > 0 && distance * distance >= 1e-8f)
                result = previous.Interpolated + distance * System.Math.Clamp(delta * speed, 0, 1);
        }
        // UE keeps the raw interpolated value; FInputScaleBias clamps only the returned alpha.
        return new(true, result, System.Math.Clamp(result, 0, 1));
    }

    public static void Compose(in AlsCrouchingStrideState state, ReadOnlySpan<AlsLocalPose> walkPose,
        ReadOnlySpan<AlsLocalPose> direction, Span<AlsLocalPose> output)
    {
        Validate(state);
        if (walkPose.Length == 0 || direction.Length != walkPose.Length || output.Length != walkPose.Length ||
            walkPose.Overlaps(output, out var aOffset) && aOffset != 0 ||
            direction.Overlaps(output, out var bOffset) && bOffset != 0)
            throw new ArgumentException("Invalid Crouching stride pose buffers.");
        if (!state.DirectionRelevant) { walkPose.CopyTo(output); return; }
        if (!state.WalkPoseRelevant) { direction.CopyTo(output); return; }
        // BlendTwoPosesTogetherInPlace receives WeightOfPoseOne, then recomputes WeightOfPoseTwo.
        var a = 1 - state.Alpha; var b = 1 - a;
        for (var bone = 0; bone < output.Length; bone++)
            output[bone] = AlsPoseBlender.Normalize(AlsPoseBlender.Accumulate(AlsPoseBlender.Scale(walkPose[bone], a), direction[bone], b));
    }

    public static float Curve(in AlsCrouchingStrideState state, float walkPose, float direction)
    {
        Validate(state);
        if (!float.IsFinite(walkPose) || !float.IsFinite(direction)) throw new ArgumentException("Invalid Crouching stride curve.");
        if (!state.DirectionRelevant) return walkPose;
        if (!state.WalkPoseRelevant) return direction;
        var b = 1 - (1 - state.Alpha);
        if (b <= AlsPoseBlender.WeightThreshold) return walkPose;
        if (MathF.Abs(b - 1) <= AlsPoseBlender.WeightThreshold) return direction;
        return walkPose + b * (direction - walkPose);
    }

    public static AlsInertialCurve CurveWithPresence(in AlsCrouchingStrideState state, AlsInertialCurve walkPose, AlsInertialCurve direction)
    {
        Validate(state);
        if (!state.DirectionRelevant) return walkPose;
        if (!state.WalkPoseRelevant) return direction;
        return AlsStandingCycleCurves.Lerp(walkPose, direction, 1 - (1 - state.Alpha));
    }

    private static void Validate(in AlsCrouchingStrideState state, bool allowUninitialized = false)
    {
        if (!state.Initialized)
        {
            if (!allowUninitialized || state != default) throw new ArgumentException("Crouching stride is not initialized.");
        }
        else if (!float.IsFinite(state.Interpolated) || !float.IsFinite(state.Alpha) ||
            state.Alpha != System.Math.Clamp(state.Interpolated, 0, 1))
            throw new ArgumentException("Invalid Crouching stride state.");
    }

    public static void Compose(in AlsCrouchingStrideState state, ReadOnlySpan<AlsPrecisePose> walkPose,
        ReadOnlySpan<AlsPrecisePose> direction, Span<AlsPrecisePose> output)
    {
        Validate(state);
        if (walkPose.Length == 0 || direction.Length != walkPose.Length || output.Length != walkPose.Length ||
            walkPose.Overlaps(output, out var aOffset) && aOffset != 0 ||
            direction.Overlaps(output, out var bOffset) && bOffset != 0)
            throw new ArgumentException("Invalid Crouching stride pose buffers.");
        if (!state.DirectionRelevant) { walkPose.CopyTo(output); return; }
        if (!state.WalkPoseRelevant) { direction.CopyTo(output); return; }
        // BlendTwoPosesTogetherInPlace receives WeightOfPoseOne, then recomputes WeightOfPoseTwo.
        var a = 1 - state.Alpha; var b = 1 - a;
        for (var bone = 0; bone < output.Length; bone++)
            output[bone] = AlsPrecisePoseBlender.Normalize(AlsPrecisePoseBlender.Accumulate(AlsPrecisePoseBlender.Scale(walkPose[bone], a), direction[bone], b));
    }
}
