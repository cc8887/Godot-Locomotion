using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Camera;

public sealed class AlsCameraBlendList
{
    private AlsBinaryBlendChannel[] _channels;
    private float[] _weights;
    public int ActiveChild { get; private set; } = -1;
    public ReadOnlySpan<float> Weights => _weights;
    public AlsCameraBlendList(int children)
    {
        if (children < 1) throw new ArgumentOutOfRangeException(nameof(children));
        _channels = new AlsBinaryBlendChannel[children]; _weights = new float[children];
        for (var i = 0; i < children; i++) _channels[i] = new(0, 1, i == 0 ? 1 : 0, i == 0 ? 1 : 0, 0);
        _weights[0] = 1;
    }
    public AlsCameraBlendList Copy() => new(_weights.Length)
    { ActiveChild = ActiveChild, _channels = (AlsBinaryBlendChannel[])_channels.Clone(), _weights = (float[])_weights.Clone() };

    // Return explicit initialization/zero-weight update work to the graph owner.
    public (int ResetChild, int ZeroWeightPrevious) Advance(int child, float delta, IReadOnlyList<float> times,
        bool resetOnActivation, Func<float, float> curve)
    {
        if ((uint)child >= _weights.Length || times.Count != _weights.Length || !float.IsFinite(delta) || delta < 0 ||
            times.Any(t => !float.IsFinite(t) || t < 0)) throw new ArgumentException("Invalid camera blend input.");
        var reset = -1; var zero = -1;
        if (ActiveChild != child)
        {
            var seconds = ActiveChild < 0 ? 0 : times[child] * System.Math.Clamp(1 - _weights[child], 0, 1);
            if (seconds == 0 && ActiveChild >= 0) zero = ActiveChild;
            if (resetOnActivation && _weights[child] <= AlsPoseBlender.WeightThreshold) reset = child;
            for (var i = 0; i < _channels.Length; i++)
            {
                var begin = _weights[i]; var target = i == child ? 1 : 0;
                var alpha = begin == target ? 1 : System.Math.Clamp((_channels[i].Value - begin) / (target - begin), 0, 1);
                if (seconds == 0) alpha = 1;
                _channels[i] = new(begin, target, alpha, begin + (target - begin) * Sample(alpha), seconds * MathF.Abs(1 - alpha));
            }
            ActiveChild = child;
        }
        var total = 0f;
        for (var i = 0; i < _channels.Length; i++)
        {
            var c = _channels[i];
            if (c.Value != c.Target)
            {
                var alpha = c.Remaining > delta ? c.Alpha + (1 - c.Alpha) / c.Remaining * delta : 1;
                alpha = System.Math.Clamp(alpha, 0, 1);
                c = c with { Alpha = alpha, Remaining = MathF.Max(0, c.Remaining - delta), Value = c.Begin + (c.Target - c.Begin) * Sample(alpha) };
                _channels[i] = c;
            }
            _weights[i] = c.Value; total += c.Value;
        }
        if (total > AlsPoseBlender.WeightThreshold && MathF.Abs(total - 1) > AlsPoseBlender.WeightThreshold)
        { var reciprocal = 1 / total; for (var i = 0; i < _weights.Length; i++) _weights[i] *= reciprocal; }
        return (reset, zero);

        float Sample(float alpha)
        { var value = curve(alpha); if (!float.IsFinite(value)) throw new ArgumentException("Nonfinite camera blend curve."); return System.Math.Clamp(value, 0, 1); }
    }
}
