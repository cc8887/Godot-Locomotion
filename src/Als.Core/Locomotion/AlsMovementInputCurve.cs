using GodotAls.Core.Curves;

namespace GodotAls.Core.Locomotion;

/// <summary>Authored scalar input curve. Its domain may be speed, signed fall speed or
/// a blend weight; it is not an animation playback timeline.</summary>
public sealed class AlsMovementInputCurve
{
    private readonly AlsCurveKey[] _keys;
    private readonly bool _oscillate;
    private readonly bool _nativePrecision;
    public ReadOnlySpan<AlsCurveKey> Keys => _keys;

    public AlsMovementInputCurve(ReadOnlySpan<AlsCurveKey> keys, bool oscillate = false, bool nativePrecision = false)
    {
        if (keys.Length < 2) throw new ArgumentException("Input curve needs an interval.");
        for (var i = 0; i < keys.Length; i++)
        {
            var key = keys[i];
            if (!float.IsFinite(key.TimeSeconds) || !float.IsFinite(key.Value) || !float.IsFinite(key.ArriveTangent) ||
                !float.IsFinite(key.LeaveTangent) || (uint)key.Interpolation > 2 || i > 0 && key.TimeSeconds <= keys[i - 1].TimeSeconds)
                throw new ArgumentException("Invalid input curve keys.");
        }
        _keys = keys.ToArray(); _oscillate = oscillate; _nativePrecision = nativePrecision;
    }

    public float Sample(float input)
    {
        if (!float.IsFinite(input)) throw new ArgumentOutOfRangeException(nameof(input));
        var first = _keys[0].TimeSeconds; var last = _keys[^1].TimeSeconds;
        if (_oscillate && (input < first || input > last))
        {
            var width = (double)last - first; var period = width * 2;
            var wrapped = ((double)input - first) % period;
            if (wrapped < 0) wrapped += period;
            input = (float)(first + (wrapped <= width ? wrapped : period - wrapped));
        }
        if (_nativePrecision) return SampleNative(input);
        if (!AlsCurveRuntime.TrySample(new(0, 0, _keys.Length, 0, 1, 0), _keys, 0, input, out var value, out var failure))
            throw new ArgumentException($"Invalid movement curve sample: {failure}.");
        return value;
    }
    private float SampleNative(float input)
    {
        if (input <= _keys[0].TimeSeconds) return _keys[0].Value;
        if (input >= _keys[^1].TimeSeconds) return _keys[^1].Value;
        var lo = 0; var hi = _keys.Length - 1;
        while (hi - lo > 1) { var mid = lo + (hi - lo) / 2; if (_keys[mid].TimeSeconds <= input) lo = mid; else hi = mid; }
        var left = _keys[lo]; var right = _keys[hi]; var duration = right.TimeSeconds - left.TimeSeconds;
        var alpha = (input - left.TimeSeconds) / duration;
        float Lerp(float a, float b) => a + alpha * (b - a);
        if (left.Interpolation == AlsCurveInterpolationMode.Constant) return left.Value;
        if (left.Interpolation == AlsCurveInterpolationMode.Linear) return Finite(Lerp(left.Value, right.Value));
        // UE::Curves::EvalForTwoKeys/BezierInterp: float control points and
        // de Casteljau stages, including the multiply by float OneThird.
        var p1 = left.Value + left.LeaveTangent * duration * (1f / 3f);
        var p2 = right.Value - right.ArriveTangent * duration * (1f / 3f);
        var p01 = Lerp(left.Value, p1); var p12 = Lerp(p1, p2); var p23 = Lerp(p2, right.Value);
        var result = Lerp(Lerp(p01, p12), Lerp(p12, p23));
        return Finite(result);
        static float Finite(float value) => float.IsFinite(value) ? value : throw new ArgumentException("Nonfinite native input curve.");
    }
}
