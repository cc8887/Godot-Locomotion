using ScalarMath = System.Math;

namespace GodotAls.Core.Locomotion;

/// <summary>UE raw GetBoneTransform / ExtractRootMotion path. This reads the
/// unlocked root track, never the retargeted or force-locked pose output.
/// An explicit codec may supply absolute samples and range extraction.</summary>
public sealed class AlsRawRootMotionIntervalSampler
{
    private readonly Func<double, AlsPrecisePose> _sample;
    private readonly Func<double, double, AlsPrecisePose> _range;
    private readonly float _length;

    public AlsRawRootMotionIntervalSampler(AlsRawAnimationPoseData data, AlsPrecisePose referenceRoot, bool normalizedScale)
    {
        ArgumentNullException.ThrowIfNull(data); referenceRoot.Validate();
        if (data.LogicalToPhysical[0] < 0 || !float.IsFinite((float)data.PlayLength) || data.PlayLength <= 0)
            throw new ArgumentException("Root extraction requires a raw root and positive finite length.");
        var track = new GodotAls.Core.Actions.AlsRawRootMotionSampler(data, referenceRoot, normalizedScale);
        _sample = seconds => track.SampleAbsolute(seconds, AlsRawFrameTimeRounding.RoundSubframe);
        _range = (start, end) => track.ExtractAbsoluteRange(start, end, AlsRawFrameTimeRounding.RoundSubframe);
        _length = (float)data.PlayLength;
    }
    public AlsRawRootMotionIntervalSampler(float length, Func<double, AlsPrecisePose> sample,
        Func<double, double, AlsPrecisePose> range)
    {
        if (!float.IsFinite(length) || length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        ArgumentNullException.ThrowIfNull(sample); ArgumentNullException.ThrowIfNull(range);
        _length = length; _sample = sample; _range = range;
    }
    public AlsPrecisePose SampleRoot(double seconds)
        => _sample(seconds);

    public AlsPrecisePose Extract(float previous, float delta, bool looping, bool retainedEvaluatorClock = false)
    {
        // An original nonloop evaluator may retain a longer sequence's clock
        // across a resource switch. Native extraction accepts its reverse/zero
        // interval; ordinary players keep the existing bounded contract.
        if (!float.IsFinite(previous) || previous < 0 || !float.IsFinite(delta) ||
            previous > _length && !(retainedEvaluatorClock && !looping && delta <= 0))
            throw new ArgumentOutOfRangeException(nameof(previous));
        if (delta == 0) return AlsPrecisePose.Identity;
        var reverse = delta < 0; var current = previous; var remaining = delta;
        var output = AlsPrecisePose.Identity; var first = true;
        while (true)
        {
            var next = current + remaining;
            if (!float.IsFinite(next)) throw new ArgumentOutOfRangeException(nameof(delta));
            var finished = next < 0 || next > _length; next = ScalarMath.Clamp(next, 0, _length);
            var range = ExtractRange(current, next);
            output = first ? range : AlsPrecisePose.Compose(range, output);
            output = output with { Scale = AlsDoubleVector.One }; first = false;
            if (!finished || !looping) return output;
            // UE subtracts a binary32 position difference promoted to double,
            // then stores DesiredDeltaMove back to binary32 after each segment.
            var actual = (double)(next - current); var nextRemaining = (float)(remaining - actual);
            if (nextRemaining == remaining && actual != 0)
                throw new ArgumentOutOfRangeException(nameof(delta), "Root interval cannot progress at binary32 precision.");
            remaining = nextRemaining; current = reverse ? _length : 0;
        }
    }
    public AlsPrecisePose ExtractRange(double start, double end)
        => _range(start, end);
}
