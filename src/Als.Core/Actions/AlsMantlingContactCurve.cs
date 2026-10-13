using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Actions;

public readonly record struct AlsMantlingHandContact(AlsPrecisePose LedgeAnchor, float Weight)
{
    public float TransitionLiftCm {get;init;}
}

// All times use the physical montage clock. FullSupportTime is the first key
// at the initial acquisition peak (which may be below one). SupportEndTime is
// the last key before the first decrease, not the end of the release tail.
public readonly record struct AlsMantlingAcquisitionWindow(
    bool Available, float ApproachStartTime, float FullSupportTime,
    float SupportEndTime, AlsPrecisePose LedgeAnchor);

// Fixed ledge-local anchors, sampled by the physical montage's existing clock.
// The curve controls approach/release; it never animates a supporting anchor.
public sealed class AlsMantlingContactCurve
{
    private readonly float[] _times, _left, _right;
    private readonly AlsPrecisePose _leftAnchor, _rightAnchor;
    private readonly AlsMantlingAcquisitionWindow[] _leftAcquisitions, _rightAcquisitions;
    private readonly float _transitionLift;
    public float ApproachStartTime { get; }
    public AlsMantlingContactCurve(ReadOnlySpan<float> times, ReadOnlySpan<float> left, ReadOnlySpan<float> right,
        in AlsPrecisePose leftAnchor, in AlsPrecisePose rightAnchor,float? bodyBlendReadyTime=null,float transitionLiftCm=0)
    {
        if (times.Length < 2 || left.Length != times.Length || right.Length != times.Length || times[0] != 0)
            throw new ArgumentException("Invalid mantle contact layout.");
        leftAnchor.Validate(); rightAnchor.Validate();
        if(!float.IsFinite(transitionLiftCm)||transitionLiftCm<0||transitionLiftCm>4)
            throw new ArgumentException("Invalid mantle transition clearance.");
        _transitionLift=transitionLiftCm;
        for (var i = 0; i < times.Length; i++)
            if (!float.IsFinite(times[i]) || i > 0 && times[i] <= times[i - 1] ||
                !float.IsFinite(left[i]) || left[i] is < 0 or > 1 || !float.IsFinite(right[i]) || right[i] is < 0 or > 1)
                throw new ArgumentException("Invalid mantle contact key.");
        _times = times.ToArray(); _left = left.ToArray(); _right = right.ToArray();
        _leftAnchor = leftAnchor; _rightAnchor = rightAnchor;
        _leftAcquisitions = BuildAcquisitions(_times, _left, _leftAnchor);
        _rightAcquisitions = BuildAcquisitions(_times, _right, _rightAnchor);
        ApproachStartTime = times[^1];
        for (var i=0;i<times.Length;i++)
            if(left[i]>0 || right[i]>0) { ApproachStartTime=times[System.Math.Max(0,i-1)];break; }
        if(bodyBlendReadyTime is {} ready)
        {
            if(!float.IsFinite(ready)||ready<0||ready>ApproachStartTime)
                throw new ArgumentException("Invalid mantle body preparation time.");
            ApproachStartTime=ready;
        }
    }

    // Returns the current acquisition/support window, or the next one during
    // free motion or a release tail. The exact support-end key still belongs to
    // the current window; any later sample must allow its release to proceed.
    public bool TryGetAcquisitionWindow(float sampleTime, bool left,
        out AlsMantlingAcquisitionWindow window)
    {
        if (!float.IsFinite(sampleTime)) throw new ArgumentException("Nonfinite contact time.");
        var windows = left ? _leftAcquisitions : _rightAcquisitions;
        var first = 0; var last = windows.Length;
        while (first < last)
        {
            var middle = first + (last - first) / 2;
            if (windows[middle].SupportEndTime < sampleTime) first = middle + 1;
            else last = middle;
        }
        window = first < windows.Length ? windows[first] : default;
        return window.Available;
    }

    // This samples a body preference only. It does not change actual hand
    // contact weights or advance time. preparationSeconds is character time;
    // actualMontageRate converts it to the existing montage's time units.
    public static float PreparationWeight(in AlsMantlingAcquisitionWindow window,
        float sampleTime, float actualMontageRate, float preparationSeconds = .2f)
    {
        if (!float.IsFinite(sampleTime) || !float.IsFinite(actualMontageRate) || actualMontageRate <= 0 ||
            !float.IsFinite(preparationSeconds) || preparationSeconds < 0)
            throw new ArgumentException("Invalid mantle preparation sample.");
        if (!window.Available) return 0;
        if (!float.IsFinite(window.ApproachStartTime) || !float.IsFinite(window.FullSupportTime) ||
            !float.IsFinite(window.SupportEndTime) || window.ApproachStartTime < 0 ||
            window.FullSupportTime < window.ApproachStartTime || window.SupportEndTime < window.FullSupportTime)
            throw new ArgumentException("Invalid mantle acquisition window.");
        if (sampleTime > window.SupportEndTime) return 0;
        if (sampleTime >= window.ApproachStartTime) return 1;
        var duration = (double)preparationSeconds * actualMontageRate;
        if (duration == 0) return 0;
        var u = System.Math.Clamp(1 + ((double)sampleTime - window.ApproachStartTime) / duration, 0, 1);
        return (float)(u * u * u * (u * (6 * u - 15) + 10));
    }

    private static AlsMantlingAcquisitionWindow[] BuildAcquisitions(
        float[] times, float[] weights, in AlsPrecisePose anchor)
    {
        var windows = new List<AlsMantlingAcquisitionWindow>();
        var index = 0;
        while (index < weights.Length)
        {
            if (weights[index] == 0) { index++; continue; }
            var approach = System.Math.Max(0, index - 1);
            var full = index; var end = index;
            while (end + 1 < weights.Length && weights[end + 1] >= weights[end])
            {
                if (weights[end + 1] > weights[end]) full = end + 1;
                end++;
            }
            windows.Add(new(true, times[approach], times[full], times[end], anchor));
            // A new acquisition requires returning to zero. Weight fluctuations
            // in the same positive release tail cannot restart body preparation.
            index = end + 1;
            while (index < weights.Length && weights[index] > 0) index++;
        }
        return windows.ToArray();
    }

    public AlsMantlingHandContact Sample(float time, bool left)
    {
        if (!float.IsFinite(time)) throw new ArgumentException("Nonfinite contact time.");
        var keys = left ? _left : _right; var weight = 0f;
        if (time >= _times[0] && time <= _times[^1])
        {
            var key = Array.BinarySearch(_times, time);
            if (key >= 0) weight = keys[key];
            else
            {
                var next = ~key; var previous = next - 1;
                var alpha = (time - _times[previous]) / (_times[next] - _times[previous]);
                weight = keys[previous] + (keys[next] - keys[previous]) * alpha;
            }
        }
        return new(left ? _leftAnchor : _rightAnchor, weight) {TransitionLiftCm=_transitionLift};
    }
}
