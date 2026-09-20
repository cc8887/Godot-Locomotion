using GodotAls.Core.Curves;

namespace GodotAls.Core.Locomotion;

/// <summary>Authored scalar input curve. Its domain may be speed, signed fall speed or
/// a blend weight; it is not an animation playback timeline.</summary>
public sealed class AlsMovementInputCurve
{
    private readonly AlsCurveKey[] _keys;
    private readonly bool _oscillate;
    public ReadOnlySpan<AlsCurveKey> Keys => _keys;

    public AlsMovementInputCurve(ReadOnlySpan<AlsCurveKey> keys, bool oscillate = false)
    {
        if (keys.Length < 2) throw new ArgumentException("Input curve needs an interval.");
        for (var i = 0; i < keys.Length; i++)
        {
            var key = keys[i];
            if (!float.IsFinite(key.TimeSeconds) || !float.IsFinite(key.Value) || !float.IsFinite(key.ArriveTangent) ||
                !float.IsFinite(key.LeaveTangent) || (uint)key.Interpolation > 2 || i > 0 && key.TimeSeconds <= keys[i - 1].TimeSeconds)
                throw new ArgumentException("Invalid input curve keys.");
        }
        _keys = keys.ToArray(); _oscillate = oscillate;
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
        if (!AlsCurveRuntime.TrySample(new(0, 0, _keys.Length, 0, 1, 0), _keys, 0, input, out var value, out var failure))
            throw new ArgumentException($"Invalid movement curve sample: {failure}.");
        return value;
    }
}
