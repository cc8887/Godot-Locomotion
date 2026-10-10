using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Actions;

public readonly record struct AlsMantlingHandContact(AlsPrecisePose LedgeAnchor, float Weight)
{
    public float TransitionLiftCm {get;init;}
}

// Fixed ledge-local anchors, sampled by the physical montage's existing clock.
// The curve controls approach/release; it never animates a supporting anchor.
public sealed class AlsMantlingContactCurve
{
    private readonly float[] _times, _left, _right;
    private readonly AlsPrecisePose _leftAnchor, _rightAnchor;
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
