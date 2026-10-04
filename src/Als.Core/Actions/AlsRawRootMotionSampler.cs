using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Actions;

// UAnimSequence::ExtractRootMotionFromRange, on original root keys. This path
// deliberately bypasses pose retargeting, ForceRootLock and additive pose blending.
public sealed class AlsRawRootMotionSampler
{
    private readonly AlsRawAnimationPoseData _data;
    private readonly AlsPrecisePose _reference, _rootToComponent;
    private readonly bool _normalizeScale;
    private readonly int _absoluteKeyCount;
    public AlsRawRootMotionSampler(AlsRawAnimationPoseData data, in AlsPrecisePose rootReference, bool normalizeScale,
        int? absoluteKeyCount = null)
    {
        ArgumentNullException.ThrowIfNull(data); rootReference.Validate();
        if (data.LogicalToPhysical[0] != 0) throw new ArgumentException("Root motion requires the physical root track.");
        _data = data; _reference = rootReference; _normalizeScale = normalizeScale;
        _absoluteKeyCount = absoluteKeyCount ?? data.SampledKeyCount;
        if (_absoluteKeyCount < 0 || _absoluteKeyCount > data.SampledKeyCount) throw new ArgumentOutOfRangeException(nameof(absoluteKeyCount));
        _rootToComponent = AlsPrecisePose.Inverse(rootReference);
    }
    public AlsPrecisePose Extract(double start, double end)
    {
        var first = SampleRangeRoot(start); var last = SampleRangeRoot(end);
        if (_normalizeScale) { first = first with { Scale = AlsDoubleVector.One }; last = last with { Scale = AlsDoubleVector.One }; }
        first = AlsPrecisePose.Compose(_rootToComponent, first); last = AlsPrecisePose.Compose(_rootToComponent, last);
        // FRootMotionMovementParams resets motion scale to one when accumulating.
        return AlsPrecisePose.Relative(last, first) with { Scale = AlsDoubleVector.One };
    }
    // Absolute source root transform for ALS mantle warping. Unlike Extract,
    // this preserves authored scale and does not remove the reference/start pose.
    public AlsPrecisePose SampleAbsolute(double seconds)
        => SampleAbsolute(seconds, AlsRawFrameTimeRounding.OptimizedCancellation);

    public AlsPrecisePose SampleAbsolute(double seconds, AlsRawFrameTimeRounding rounding)
    {
        if (rounding is not (AlsRawFrameTimeRounding.OptimizedCancellation or AlsRawFrameTimeRounding.RoundSubframe))
            throw new ArgumentOutOfRangeException(nameof(rounding));
        if (!double.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (!_data.LogicalTrackPresence[0]) return _reference;
        // GetBoneTransform -> UAnimDataModel::EvaluateBoneTrackTransform is not
        // GetBonePose's clamped selector. Out-of-range keys are identity and
        // Step rounds to the nearest frame. Use one FFrameTime conversion.
        var frame = (seconds * _data.FrameRateNumerator) / _data.FrameRateDenominator;
        if (!double.IsFinite(frame) || frame < int.MinValue || frame >= int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        var (first, alpha) = AlsAnimationFrameTime.FromFramePosition(frame, rounding);
        if (_data.Interpolation == AlsRawAnimationInterpolation.Step) alpha = MathF.Floor(alpha + .5f);
        if (MathF.Abs(alpha - 1) <= 1e-8f) return AbsoluteKey(first + 1);
        if (MathF.Abs(alpha) <= 1e-8f) return AbsoluteKey(first);
        return AlsPrecisePose.BlendTransform(AbsoluteKey(first), AbsoluteKey(first + 1), alpha);
    }
    // Provider intervals use the direct track entry, with its own time rounding.
    // Keep Extract's existing clamped interval API for validated ALS consumers.
    public AlsPrecisePose ExtractAbsoluteRange(double start, double end, AlsRawFrameTimeRounding rounding)
    {
        var first = SampleAbsolute(start, rounding); var last = SampleAbsolute(end, rounding);
        if (_normalizeScale) { first = first with { Scale = AlsDoubleVector.One }; last = last with { Scale = AlsDoubleVector.One }; }
        first = AlsPrecisePose.Compose(_rootToComponent, first); last = AlsPrecisePose.Compose(_rootToComponent, last);
        return AlsPrecisePose.Relative(last, first);
    }
    private AlsPrecisePose AbsoluteKey(int index) => (uint)index < (uint)_absoluteKeyCount
        ? new(_data.GetPhysicalKey(index)[0]) : AlsPrecisePose.Identity;

    // Preserve the existing interval-extraction contract independently of the
    // native absolute track evaluator used by mantle.
    private AlsPrecisePose SampleRangeRoot(double seconds)
    {
        var keys = AlsRawSequencePoseSampler.SelectKeys(_data, seconds);
        if (!_data.LogicalTrackPresence[0]) return _reference;
        var first = new AlsPrecisePose(_data.GetPhysicalKey(keys.FirstKey)[0]);
        return keys.Interpolate ? AlsPrecisePose.BlendTransform(first, new(_data.GetPhysicalKey(keys.SecondKey)[0]), keys.Alpha) : first;
    }
}
