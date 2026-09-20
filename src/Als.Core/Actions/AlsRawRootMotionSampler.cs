using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Actions;

// UAnimSequence::ExtractRootMotionFromRange, on original root keys. This path
// deliberately bypasses pose retargeting, ForceRootLock and additive pose blending.
public sealed class AlsRawRootMotionSampler
{
    private readonly AlsRawAnimationPoseData _data;
    private readonly AlsPrecisePose _reference, _rootToComponent;
    private readonly bool _normalizeScale;
    public AlsRawRootMotionSampler(AlsRawAnimationPoseData data, in AlsPrecisePose rootReference, bool normalizeScale)
    {
        ArgumentNullException.ThrowIfNull(data); rootReference.Validate();
        if (data.LogicalToPhysical[0] != 0) throw new ArgumentException("Root motion requires the physical root track.");
        _data = data; _reference = rootReference; _normalizeScale = normalizeScale;
        var inverseRotation = rootReference.Rotation.Conjugate();
        var inverseScale = new AlsDoubleVector(Reciprocal(rootReference.Scale.X), Reciprocal(rootReference.Scale.Y), Reciprocal(rootReference.Scale.Z));
        _rootToComponent = new((rootReference.Position * -1 * inverseScale).Rotate(inverseRotation), inverseRotation, inverseScale);
    }
    public AlsPrecisePose Extract(double start, double end)
    {
        var first = SampleRoot(start); var last = SampleRoot(end);
        if (_normalizeScale) { first = first with { Scale = AlsDoubleVector.One }; last = last with { Scale = AlsDoubleVector.One }; }
        first = AlsPrecisePose.Compose(_rootToComponent, first); last = AlsPrecisePose.Compose(_rootToComponent, last);
        // FRootMotionMovementParams resets motion scale to one when accumulating.
        return AlsPrecisePose.Relative(last, first) with { Scale = AlsDoubleVector.One };
    }
    private AlsPrecisePose SampleRoot(double seconds)
    {
        var keys = AlsRawSequencePoseSampler.SelectKeys(_data, seconds);
        if (!_data.LogicalTrackPresence[0]) return _reference;
        var first = new AlsPrecisePose(_data.GetPhysicalKey(keys.FirstKey)[0]);
        return keys.Interpolate ? AlsPrecisePose.BlendTransform(first, new(_data.GetPhysicalKey(keys.SecondKey)[0]), keys.Alpha) : first;
    }
    private static double Reciprocal(double value) => System.Math.Abs(value) <= 1e-8f ? 0 : 1 / value;
}
