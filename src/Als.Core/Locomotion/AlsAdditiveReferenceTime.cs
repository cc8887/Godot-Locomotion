namespace GodotAls.Core.Locomotion;

// Values follow EAdditiveBasePoseType; None is not a valid additive reference.
public enum AlsAdditiveReferenceKind { ReferencePose = 1, ScaledAnimation = 2, AnimationFrame = 3, SourceFrame = 4 }

public static class AlsAdditiveReferenceTime
{
    /// <summary>GetAdditiveBasePose uses the incoming extraction time, before the target
    /// sampler quantizes it. Frame policies divide by sampled key count, not count - 1.</summary>
    public static double Resolve(AlsAdditiveReferenceKind kind, double currentTime, double sequenceLength,
        double referenceLength, int referenceKeyCount, int referenceFrame)
    {
        if (!Enum.IsDefined(kind) || !double.IsFinite(currentTime) || !double.IsFinite(sequenceLength) || sequenceLength < 0 ||
            !double.IsFinite(referenceLength) || referenceLength < 0 || referenceKeyCount < 0)
            throw new ArgumentException("Invalid additive reference time contract.");
        if (kind == AlsAdditiveReferenceKind.ReferencePose) return 0;
        var fraction = kind == AlsAdditiveReferenceKind.ScaledAnimation
            ? sequenceLength > 0 ? System.Math.Clamp(currentTime / sequenceLength, 0, 1) : 0
            : referenceKeyCount > 0 ? System.Math.Clamp((double)referenceFrame / referenceKeyCount, 0, 1) : 0;
        return referenceLength * fraction;
    }

}
