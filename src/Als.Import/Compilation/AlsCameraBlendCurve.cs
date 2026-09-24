using System.Globalization;
using System.Text.RegularExpressions;
using GodotAls.Core.Curves;

namespace GodotAls.Import.Compilation;

public sealed class AlsCameraBlendCurve
{
    private readonly AlsCurveKey[] _keys;
    private AlsCameraBlendCurve(AlsCurveKey[] keys) => _keys = keys;
    public float Sample(float alpha)
    {
        if (!float.IsFinite(alpha)) throw new ArgumentOutOfRangeException(nameof(alpha));
        if (!AlsCurveRuntime.TrySample(new(0, 0, _keys.Length, 1, 1, 0), _keys, 0, System.Math.Clamp(alpha, 0, 1), out var value, out var failure))
            throw new ArgumentException("Invalid camera rich curve: " + failure);
        return System.Math.Clamp(value, 0, 1);
    }
    internal static AlsCameraBlendCurve Compile(string native)
    {
        var match = Regex.Match(native, @"(?m)^   FloatCurve=\(Keys=\((.*?)\)\)\r?$");
        if (!match.Success) throw new ArgumentException("Unsupported camera curve extrapolation/layout.");
        var keys = new List<AlsCurveKey>();
        foreach (Match key in Regex.Matches(match.Groups[1].Value, @"\(([^()]*)\)"))
        {
            var fields = key.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Split('=', 2)).ToDictionary(s => s[0], s => s[1], StringComparer.Ordinal);
            if (fields.Keys.Any(k => k is not ("Time" or "Value" or "ArriveTangent" or "LeaveTangent" or "InterpMode" or "TangentMode")))
                throw new ArgumentException("Unsupported camera weighted curve key.");
            float Number(string name)
            {
                if (!float.TryParse(fields.GetValueOrDefault(name, "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
                    throw new ArgumentException("Nonfinite camera curve key."); return value;
            }
            var mode = fields.GetValueOrDefault("InterpMode", "RCIM_Linear") switch
            {
                "RCIM_Linear" => AlsCurveInterpolationMode.Linear,
                "RCIM_Cubic" => AlsCurveInterpolationMode.Cubic,
                "RCIM_Constant" => AlsCurveInterpolationMode.Constant,
                _ => throw new ArgumentException("Unsupported camera curve interpolation.")
            };
            keys.Add(new(Number("Time"), Number("Value"), Number("ArriveTangent"), Number("LeaveTangent"), mode));
        }
        if (keys.Count < 2 || keys[0].TimeSeconds != 0 || keys[^1].TimeSeconds != 1 || keys[0].Value != 0 || keys[^1].Value != 1 ||
            keys.Zip(keys.Skip(1)).Any(p => p.First.TimeSeconds >= p.Second.TimeSeconds))
            throw new ArgumentException("Invalid camera blend curve domain.");
        return new(keys.ToArray());
    }
}
