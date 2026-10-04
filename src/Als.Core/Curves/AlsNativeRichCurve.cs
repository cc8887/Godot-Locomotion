namespace GodotAls.Core.Curves;

public enum AlsNativeBezierEvaluation { Reassociated, NestedLerp }

/// <summary>Immutable, unweighted UE RichCurve with constant extrapolation.
/// Retains the binary32 evaluation boundaries of the native animation source.</summary>
public sealed class AlsNativeRichCurve
{
    private readonly AlsCurveKey[] _keys;
    private readonly AlsNativeBezierEvaluation _bezierEvaluation;

    public AlsNativeRichCurve(ReadOnlySpan<AlsCurveKey> keys,
        AlsNativeBezierEvaluation bezierEvaluation = AlsNativeBezierEvaluation.Reassociated)
    {
        if (!Enum.IsDefined(bezierEvaluation)) throw new ArgumentOutOfRangeException(nameof(bezierEvaluation));
        _bezierEvaluation = bezierEvaluation;
        if (keys.IsEmpty) throw new ArgumentException("A native curve requires at least one key.");
        for (var i = 0; i < keys.Length; i++)
        {
            var k = keys[i];
            if (!float.IsFinite(k.TimeSeconds) || !float.IsFinite(k.Value) ||
                !float.IsFinite(k.ArriveTangent) || !float.IsFinite(k.LeaveTangent) ||
                (uint)k.Interpolation > 2 || i > 0 && k.TimeSeconds <= keys[i - 1].TimeSeconds)
                throw new ArgumentException("Invalid native curve keys.");
        }
        _keys = keys.ToArray();
    }

    public float Sample(float time)
    {
        if (!float.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
        if (time <= _keys[0].TimeSeconds) return _keys[0].Value;
        if (time >= _keys[^1].TimeSeconds) return _keys[^1].Value;
        var lo = 0; var hi = _keys.Length - 1;
        while (hi - lo > 1)
        {
            var mid = lo + (hi - lo) / 2;
            if (_keys[mid].TimeSeconds <= time) lo = mid; else hi = mid;
        }
        var left = _keys[lo]; var right = _keys[hi];
        if (left.Interpolation == AlsCurveInterpolationMode.Constant) return left.Value;
        var duration = right.TimeSeconds - left.TimeSeconds;
        var alpha = (time - left.TimeSeconds) / duration;
        var value = left.Interpolation == AlsCurveInterpolationMode.Linear
            ? left.Value + alpha * (right.Value - left.Value)
            : Bezier(left.Value, left.Value + left.LeaveTangent * duration * (1f / 3f),
                right.Value - right.ArriveTangent * duration * (1f / 3f), right.Value, alpha);
        return float.IsFinite(value) ? value : throw new ArgumentException("Nonfinite native curve sample.");
    }

    private float Bezier(float p0, float p1, float p2, float p3, float alpha)
    {
        if (_bezierEvaluation == AlsNativeBezierEvaluation.NestedLerp)
        {
            // Source model extraction in the current Lyra native trace retains
            // CurveEvaluation.h's six float Lerp boundaries. Select this path
            // explicitly; the ALS reference trace verifies reassociation below.
            var p01Nested = p0 + alpha * (p1 - p0);
            var p12Nested = p1 + alpha * (p2 - p1);
            var p23Nested = p2 + alpha * (p3 - p2);
            var p012Nested = p01Nested + alpha * (p12Nested - p01Nested);
            var p123Nested = p12Nested + alpha * (p23Nested - p12Nested);
            return p012Nested + alpha * (p123Nested - p012Nested);
        }
        // UE's optimized BezierInterp factors the nested Lerp additions. Keep
        // these float intermediates: six separately rounded Lerps and a double
        // Hermite polynomial both change stride/play-rate and accumulate clock drift.
        var d01 = p1 - p0; var d12 = p2 - p1;
        var p01 = p0 + alpha * d01;
        var p12 = p1 + alpha * d12;
        var p23 = p2 + alpha * (p3 - p2);
        var d012 = (p12 - p01) + d01;
        var d123 = (p23 - p12) + d12;
        var p012 = p0 + alpha * d012;
        var p123 = p1 + alpha * d123;
        return p0 + alpha * ((p123 - p012) + d012);
    }
}
