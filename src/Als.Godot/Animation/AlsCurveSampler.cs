using GodotAls.Core.Curves;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

public sealed class AlsCurveSampler
{
    private readonly AlsCurveBinding[] _bindings;
    private readonly AlsCurveKey[] _keys;

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
        var ordered = new AlsFloatCurveDefinition[curves.Length];
        for (var curveIndex = 0; curveIndex < curves.Length; curveIndex++)
        {
            ordered[curveIndex] = curves[curveIndex]
                ?? throw new ArgumentException("Curve entries cannot be null.", nameof(curves));
        }
        Array.Sort(
            ordered,
            static (left, right) => left.CurveId.CompareTo(right.CurveId));

        var sourceKeys = new AlsFloatCurveKeyDefinition[ordered.Length][];
        var totalKeyCount = 0;
        var previousId = -1;
        for (var curveIndex = 0; curveIndex < ordered.Length; curveIndex++)
        {
            var curve = ordered[curveIndex];
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
            if (keys.Length > int.MaxValue - totalKeyCount)
            {
                throw new ArgumentException("Curve key table is too large.", nameof(curves));
            }

            for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            {
                ref readonly var key = ref keys[keyIndex];
                if (!float.IsFinite(key.TimeSeconds) ||
                    !float.IsFinite(key.Value) ||
                    !float.IsFinite(key.ArriveTangent) ||
                    !float.IsFinite(key.LeaveTangent) ||
                    !TryMapInterpolation(key.Interpolation, out _) ||
                    (keyIndex != 0 && key.TimeSeconds <= keys[keyIndex - 1].TimeSeconds))
                {
                    throw new ArgumentException(
                        $"Curve {curve.CurveId} key {keyIndex} must be finite, strictly ordered, " +
                        "unique and use a supported interpolation.", nameof(curves));
                }
            }

            sourceKeys[curveIndex] = keys;
            totalKeyCount += keys.Length;
            previousId = curve.CurveId;
        }

        _bindings = new AlsCurveBinding[ordered.Length];
        _keys = new AlsCurveKey[totalKeyCount];
        var keyOffset = 0;
        for (var curveIndex = 0; curveIndex < ordered.Length; curveIndex++)
        {
            var curve = ordered[curveIndex];
            var keys = sourceKeys[curveIndex];
            _bindings[curveIndex] = new AlsCurveBinding(
                curve.CurveId,
                keyOffset,
                keys.Length,
                0f,
                1,
                0);
            for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            {
                ref readonly var source = ref keys[keyIndex];
                if (!TryMapInterpolation(source.Interpolation, out var interpolation))
                {
                    throw new InvalidOperationException(
                        "Validated curve contained an unsupported interpolation.");
                }
                _keys[keyOffset + keyIndex] = new AlsCurveKey(
                    source.TimeSeconds,
                    source.Value,
                    source.ArriveTangent,
                    source.LeaveTangent,
                    interpolation);
            }
            keyOffset += keys.Length;
        }
    }

    public bool TrySample(int curveId, float timeSeconds, out float value)
    {
        value = 0f;
        if (!float.IsFinite(timeSeconds) || !TryFindBindingIndex(curveId, out var bindingIndex))
        {
            return false;
        }
        return AlsCurveRuntime.TrySample(
            _bindings[bindingIndex], _keys, 0, timeSeconds, out value, out _);
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
            if (!TryFindBindingIndex(curveIds[index], out var bindingIndex) ||
                !AlsCurveRuntime.TrySample(
                    _bindings[bindingIndex], _keys, 0, timeSeconds, out _, out _))
            {
                return false;
            }
        }

        for (var index = 0; index < curveIds.Length; index++)
        {
            TryFindBindingIndex(curveIds[index], out var bindingIndex);
            AlsCurveRuntime.TrySample(
                _bindings[bindingIndex], _keys, 0, timeSeconds, out destination[index], out _);
        }
        return true;
    }

    private bool TryFindBindingIndex(int curveId, out int bindingIndex)
    {
        var low = 0;
        var high = _bindings.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var candidateId = _bindings[middle].CurveId;
            if (candidateId == curveId)
            {
                bindingIndex = middle;
                return true;
            }
            if (candidateId < curveId)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }
        bindingIndex = -1;
        return false;
    }

    private static bool TryMapInterpolation(
        AlsCurveInterpolation source,
        out AlsCurveInterpolationMode destination)
    {
        switch (source)
        {
            case AlsCurveInterpolation.Constant:
                destination = AlsCurveInterpolationMode.Constant;
                return true;
            case AlsCurveInterpolation.Linear:
                destination = AlsCurveInterpolationMode.Linear;
                return true;
            case AlsCurveInterpolation.Cubic:
                destination = AlsCurveInterpolationMode.Cubic;
                return true;
            default:
                destination = default;
                return false;
        }
    }
}
