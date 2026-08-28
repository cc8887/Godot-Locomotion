using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

public sealed class AlsCurveSampler
{
    private readonly BoundCurve[] _curves;

    public AlsCurveSampler(AlsFloatCurveKeyDefinition[] keys)
        : this(0, keys)
    {
    }

    public AlsCurveSampler(int curveId, AlsFloatCurveKeyDefinition[] keys)
        : this([
            new AlsFloatCurveDefinition(
                curveId,
                AlsCanonicalCurveKind.None,
                "Runtime",
                AlsCurveProvenance.SourceCurve,
                keys ?? throw new ArgumentNullException(nameof(keys))),
        ])
    {
    }

    public AlsCurveSampler(AlsFloatCurveDefinition[] curves)
    {
        ArgumentNullException.ThrowIfNull(curves);
        _curves = new BoundCurve[curves.Length];
        var previousId = -1;
        var ordered = curves.OrderBy(value => value.CurveId).ToArray();
        for (var curveIndex = 0; curveIndex < ordered.Length; curveIndex++)
        {
            var curve = ordered[curveIndex]
                ?? throw new ArgumentException("Curve entries cannot be null.", nameof(curves));
            if (curve.CurveId < 0 || curve.CurveId == previousId)
            {
                throw new ArgumentException(
                    $"Curve IDs must be unique and non-negative: {curve.CurveId}", nameof(curves));
            }

            var keys = curve.Keys;
            if (keys.Length == 0)
            {
                throw new ArgumentException(
                    $"Curve {curve.CurveId} must contain at least one key.", nameof(curves));
            }
            for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            {
                ref readonly var key = ref keys[keyIndex];
                if (!float.IsFinite(key.TimeSeconds) ||
                    !float.IsFinite(key.Value) ||
                    !float.IsFinite(key.ArriveTangent) ||
                    !float.IsFinite(key.LeaveTangent) ||
                    (uint)key.Interpolation > (uint)AlsCurveInterpolation.Cubic ||
                    (keyIndex != 0 && key.TimeSeconds <= keys[keyIndex - 1].TimeSeconds))
                {
                    throw new ArgumentException(
                        $"Curve {curve.CurveId} key {keyIndex} must be finite, strictly ordered, " +
                        "unique and use a supported interpolation.", nameof(curves));
                }
            }

            _curves[curveIndex] = new BoundCurve(curve.CurveId, keys);
            previousId = curve.CurveId;
        }
    }

    public bool TrySample(int curveId, float timeSeconds, out float value)
    {
        value = 0f;
        if (!float.IsFinite(timeSeconds) || !TryFindCurve(curveId, out var curve))
        {
            return false;
        }
        return TryCalculate(curve.Keys, timeSeconds, out value);
    }

    public bool TrySample(
        ReadOnlySpan<int> curveIds,
        float timeSeconds,
        Span<float> destination)
    {
        if (!float.IsFinite(timeSeconds) || destination.Length < curveIds.Length)
        {
            return false;
        }

        for (var index = 0; index < curveIds.Length; index++)
        {
            if (!TryFindCurve(curveIds[index], out var curve) ||
                !TryCalculate(curve.Keys, timeSeconds, out _))
            {
                return false;
            }
        }

        for (var index = 0; index < curveIds.Length; index++)
        {
            TryFindCurve(curveIds[index], out var curve);
            TryCalculate(curve.Keys, timeSeconds, out destination[index]);
        }
        return true;
    }

    private bool TryFindCurve(int curveId, out BoundCurve curve)
    {
        var low = 0;
        var high = _curves.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var candidate = _curves[middle];
            if (candidate.CurveId == curveId)
            {
                curve = candidate;
                return true;
            }
            if (candidate.CurveId < curveId)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }
        curve = default;
        return false;
    }

    private static bool TryCalculate(
        AlsFloatCurveKeyDefinition[] keys,
        float timeSeconds,
        out float value)
    {
        value = 0f;
        if (timeSeconds <= keys[0].TimeSeconds)
        {
            value = keys[0].Value;
            return true;
        }
        if (timeSeconds >= keys[^1].TimeSeconds)
        {
            value = keys[^1].Value;
            return true;
        }

        var low = 0;
        var high = keys.Length - 1;
        while (high - low > 1)
        {
            var middle = low + ((high - low) >> 1);
            if (keys[middle].TimeSeconds <= timeSeconds)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        ref readonly var left = ref keys[low];
        ref readonly var right = ref keys[high];
        if (timeSeconds == left.TimeSeconds)
        {
            value = left.Value;
            return true;
        }
        if (timeSeconds == right.TimeSeconds)
        {
            value = right.Value;
            return true;
        }

        var duration = (double)right.TimeSeconds - left.TimeSeconds;
        var alpha = ((double)timeSeconds - left.TimeSeconds) / duration;
        var calculated = left.Interpolation switch
        {
            AlsCurveInterpolation.Constant => left.Value,
            AlsCurveInterpolation.Linear =>
                ((double)left.Value * (1d - alpha)) + ((double)right.Value * alpha),
            AlsCurveInterpolation.Cubic => Hermite(in left, in right, duration, alpha),
            _ => throw new InvalidOperationException("Validated curve contained an unsupported interpolation."),
        };
        if (!double.IsFinite(calculated) ||
            calculated < -float.MaxValue || calculated > float.MaxValue)
        {
            return false;
        }
        value = (float)calculated;
        return float.IsFinite(value);
    }

    private static double Hermite(
        in AlsFloatCurveKeyDefinition left,
        in AlsFloatCurveKeyDefinition right,
        double duration,
        double alpha)
    {
        var alpha2 = alpha * alpha;
        var alpha3 = alpha2 * alpha;
        var h00 = (2d * alpha3) - (3d * alpha2) + 1d;
        var h10 = alpha3 - (2d * alpha2) + alpha;
        var h01 = (-2d * alpha3) + (3d * alpha2);
        var h11 = alpha3 - alpha2;
        return (h00 * left.Value) +
            (h10 * left.LeaveTangent * duration) +
            (h01 * right.Value) +
            (h11 * right.ArriveTangent * duration);
    }

    private readonly record struct BoundCurve(
        int CurveId,
        AlsFloatCurveKeyDefinition[] Keys);
}
