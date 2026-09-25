using System.Collections.ObjectModel;
using System.Text.Json;
using GodotAls.Core.Curves;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

/// <summary>Original full precision movement settings; does not evaluate Parent state.</summary>
public sealed class AlsRefactoredMovementSettings
{
    public const string Source = "/ALS/ALS/Data/AnimationInstance/AIS_Als_Default.AIS_Als_Default";
    public string CatalogDigest { get; }
    public float MovingSmoothSpeedThreshold { get; }
    public float LeanHalfLife { get; }
    public float VelocityBlendHalfLife { get; }
    public float WalkSpeed { get; }
    public float RunSpeed { get; }
    public float SprintSpeed { get; }
    public float CrouchSpeed { get; }
    public float PivotThreshold { get; }
    public AlsRefactoredMovementCurve WalkStride { get; }
    public AlsRefactoredMovementCurve RunStride { get; }
    public AlsRefactoredMovementCurve CrouchStride { get; }
    public AlsRefactoredMovementCurve YawForward { get; }
    public AlsRefactoredMovementCurve YawBackward { get; }
    public AlsRefactoredMovementCurve YawLeft { get; }
    public AlsRefactoredMovementCurve YawRight { get; }
    public IReadOnlyDictionary<string, AlsRefactoredMovementCurve> Curves { get; }

    public AlsRefactoredMovementSettings(string json, AlsRefactoredAnimationCatalog catalog)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        Expect(root, new { schemaVersion = 1, source = Source, animationClass = "/ALS/ALS/Character/AB_Als.AB_Als_C" });
        CatalogDigest = catalog.IndexDigest;
        if (!string.Equals(root.GetProperty("catalogSha256").GetString(), CatalogDigest, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Movement settings catalog differs.");
        var original = catalog.Read(Source); Expect(original, new { @class = "AlsAnimationInstanceSettings" });
        var text = original.GetProperty("nativeText").GetString()!;
        var general = root.GetProperty("general"); var grounded = root.GetProperty("grounded");
        var standing = root.GetProperty("standing"); var crouching = root.GetProperty("crouching");
        MovingSmoothSpeedThreshold = Number(general, "movingSmoothSpeedThreshold"); LeanHalfLife = Number(general, "leanInterpolationHalfLife");
        VelocityBlendHalfLife = Number(grounded, "velocityBlendInterpolationHalfLife");
        WalkSpeed = Number(standing, "animatedWalkSpeed", true); RunSpeed = Number(standing, "animatedRunSpeed", true);
        SprintSpeed = Number(standing, "animatedSprintSpeed", true); PivotThreshold = Number(standing, "pivotActivationSpeedThreshold");
        CrouchSpeed = Number(crouching, "animatedCrouchSpeed", true);
        var curves = new Dictionary<string, AlsRefactoredMovementCurve>(StringComparer.Ordinal);
        foreach (var row in root.GetProperty("curves").EnumerateArray())
        {
            var path = row.GetProperty("path").GetString()!;
            Expect(catalog.Read(path), new { @class = "CurveFloat" });
            if (!curves.TryAdd(path, new(row))) throw new ArgumentException("Duplicate movement curve.");
        }
        var used = new HashSet<string>();
        AlsRefactoredMovementCurve Bind(JsonElement owner, string field)
        {
            var path = owner.GetProperty(field).GetString()!;
            var nativeName = char.ToUpperInvariant(field[0]) + field[1..];
            if (!text.Contains(nativeName + "=\"/Script/Engine.CurveFloat'" + path + "'\"", StringComparison.Ordinal) || !curves.TryGetValue(path, out var curve))
                throw new ArgumentException("Original movement curve binding differs.");
            used.Add(path); return curve;
        }
        WalkStride = Bind(standing, "strideBlendAmountWalkCurve"); RunStride = Bind(standing, "strideBlendAmountRunCurve");
        CrouchStride = Bind(crouching, "strideBlendAmountCurve");
        YawForward = Bind(grounded, "rotationYawOffsetForwardCurve"); YawBackward = Bind(grounded, "rotationYawOffsetBackwardCurve");
        YawLeft = Bind(grounded, "rotationYawOffsetLeftCurve"); YawRight = Bind(grounded, "rotationYawOffsetRightCurve");
        if (used.Count != curves.Count) throw new ArgumentException("Unreferenced movement curve.");
        Curves = new ReadOnlyDictionary<string, AlsRefactoredMovementCurve>(curves);
    }
    private static float Number(JsonElement owner, string field, bool positive = false)
    {
        var value = owner.GetProperty(field).GetSingle();
        if (!float.IsFinite(value) || value < 0 || positive && value == 0) throw new ArgumentException("Invalid movement setting.");
        return value;
    }
}

/// <summary>Native scalar sampling with the original constant or cycle-with-offset extrapolation.</summary>
public sealed class AlsRefactoredMovementCurve
{
    private readonly AlsMovementInputCurve _curve;
    private readonly bool _cycle;
    public ReadOnlySpan<AlsCurveKey> Keys => _curve.Keys;
    internal AlsRefactoredMovementCurve(JsonElement row)
    {
        var curve = row.GetProperty("curve"); var mode = curve.GetProperty("preInfinityExtrap").GetString();
        if (mode != curve.GetProperty("postInfinityExtrap").GetString() || mode is not ("RCCE_Constant" or "RCCE_CycleWithOffset"))
            throw new ArgumentException("Unsupported movement curve extrapolation.");
        _cycle = mode == "RCCE_CycleWithOffset";
        var keys = curve.GetProperty("keys").EnumerateArray().Select(k =>
        {
            Expect(k, new { tangentWeightMode = "RCTWM_WeightedNone" });
            return new AlsCurveKey(k.GetProperty("time").GetSingle(), k.GetProperty("value").GetSingle(),
                k.GetProperty("arriveTangent").GetSingle(), k.GetProperty("leaveTangent").GetSingle(),
                k.GetProperty("interpMode").GetString() switch
                {
                    "RCIM_Linear" => AlsCurveInterpolationMode.Linear, "RCIM_Cubic" => AlsCurveInterpolationMode.Cubic,
                    "RCIM_Constant" => AlsCurveInterpolationMode.Constant, _ => throw new ArgumentException("Unsupported movement interpolation.")
                });
        }).ToArray();
        _curve = new(keys, nativePrecision: true);
        var samples = row.GetProperty("verification");
        if (samples.GetArrayLength() != 241 + keys.Length) throw new ArgumentException("Incomplete movement curve reference.");
        var index = 0;
        foreach (var sample in samples.EnumerateArray())
        {
            var input = sample.GetProperty("input").GetSingle(); var value = sample.GetProperty("value").GetSingle();
            // The native exporter compiles division by 200 into float reciprocal multiplication.
            var expected = index < 241 ? keys[0].TimeSeconds + (keys[^1].TimeSeconds - keys[0].TimeSeconds) * ((index - 20) * (1f / 200)) : keys[index - 241].TimeSeconds;
            if (input != expected || !float.IsFinite(value) || MathF.Abs(Sample(input) - value) > 2e-6f)
                throw new ArgumentException($"Movement curve {row.GetProperty("path").GetString()} sample {index} differs: input={input:R}, expected={expected:R}, value={value:R}, actual={Sample(input):R}.");
            index++;
        }
    }
    public float Sample(float input)
    {
        if (!float.IsFinite(input)) throw new ArgumentOutOfRangeException(nameof(input));
        var first = Keys[0]; var last = Keys[^1]; var offset = 0f;
        if (_cycle && (input < first.TimeSeconds || input > last.TimeSeconds))
        {
            var initial = input; var duration = last.TimeSeconds - first.TimeSeconds; var before = input < first.TimeSeconds;
            var cycles = MathF.Floor((before ? input - first.TimeSeconds : last.TimeSeconds - input) / duration);
            if (cycles <= int.MinValue) throw new ArgumentOutOfRangeException(nameof(input));
            input = before ? input - duration * cycles : input + duration * cycles;
            if (input == last.TimeSeconds && initial < first.TimeSeconds) input = first.TimeSeconds;
            if (input == first.TimeSeconds && initial > last.TimeSeconds) input = last.TimeSeconds;
            offset = (before ? first.Value - last.Value : last.Value - first.Value) * MathF.Abs(cycles);
        }
        return _curve.Sample(input) + offset;
    }
}
