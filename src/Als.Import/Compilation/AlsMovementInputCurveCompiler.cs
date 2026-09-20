using System.Collections.ObjectModel;
using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Curves;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed record AlsMovementInputCurveProfile(Vector3 AnimatedStandingSpeeds, float AnimatedCrouchingSpeed,
    float VelocityInterpSpeed, float GroundedLeanInterpSpeed, float InAirLeanInterpSpeed,
    IReadOnlyDictionary<string, float> Defaults, IReadOnlyDictionary<string, AlsMovementInputCurve> Curves);

// Input data only: graph expression validation and frame update ownership are separate.
public static class AlsMovementInputCurveCompiler
{
    public const string Source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";
    public static AlsMovementInputCurveProfile Compile(string json)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        Require(root.GetProperty("movementInputSchemaVersion").GetInt32() == 1 && Text(root, "source") == Source,
            "Missing exact ALS movement input data.");
        var defaults = root.GetProperty("movementInputDefaults").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetSingle());
        string[] names = ["AnimatedWalkSpeed", "AnimatedRunSpeed", "AnimatedSprintSpeed", "AnimatedCrouchSpeed",
            "VelocityBlendInterpSpeed", "GroundedLeanInterpSpeed", "InAirLeanInterpSpeed", "JumpPlayRate", "FallSpeed",
            "LandPrediction", "WalkRunBlend", "StrideBlend", "CrouchingPlayRate"];
        Require(defaults.Count == names.Length && names.All(defaults.ContainsKey) && defaults.Values.All(float.IsFinite), "Invalid movement defaults.");
        foreach (var name in names.Take(7)) Require(defaults[name] > 0, "Movement rate default must be positive.");
        var assets = new Dictionary<string, AlsMovementInputCurve>(StringComparer.Ordinal);
        foreach (var item in root.GetProperty("movementInputCurves").EnumerateArray())
        {
            var name = Text(item, "name"); var path = Text(item, "path"); var data = item.GetProperty("curve");
            Require(path == "/Game/AdvancedLocomotionV4/Data/Curves/AnimationBlendCurves/" + name + "." + name, "Wrong input curve owner.");
            var mode = name == "DiagonalScaleAmount" ? "RCCE_Oscillate" : "RCCE_Constant";
            Require(Text(data, "preInfinityExtrap") == mode && Text(data, "postInfinityExtrap") == mode, "Unsupported input curve extrapolation.");
            var keys = data.GetProperty("keys").EnumerateArray().Select(k =>
            {
                Require(Text(k, "tangentWeightMode") == "RCTWM_WeightedNone", "Weighted input curve tangent is unsupported.");
                return new AlsCurveKey(Number(k, "time"), Number(k, "value"), Number(k, "arriveTangent"), Number(k, "leaveTangent"),
                    Text(k, "interpMode") switch { "RCIM_Constant" => AlsCurveInterpolationMode.Constant,
                        "RCIM_Linear" => AlsCurveInterpolationMode.Linear, "RCIM_Cubic" => AlsCurveInterpolationMode.Cubic,
                        _ => throw new FormatException("Unsupported input curve interpolation.") });
            }).ToArray();
            var curve = new AlsMovementInputCurve(keys, mode == "RCCE_Oscillate"); assets.Add(path, curve);
            var samples = item.GetProperty("verification"); Require(samples.GetArrayLength() == 201, "Incomplete native input curve samples.");
            var prior = float.NegativeInfinity;
            foreach (var sample in samples.EnumerateArray())
            {
                var input = Number(sample, "input");
                Require(input > prior && input >= keys[0].TimeSeconds && input <= keys[^1].TimeSeconds,
                    "Invalid native input sample domain.");
                Require(MathF.Abs(curve.Sample(input) - Number(sample, "value")) <= .000002f,
                    "Input curve differs from native GetFloatValue.");
                prior = input;
            }
            Require(samples[0].GetProperty("input").GetSingle() == keys[0].TimeSeconds && prior == keys[^1].TimeSeconds,
                "Native verification misses curve endpoints.");
        }
        var bindings = root.GetProperty("movementInputCurveBindings");
        var expected = new Dictionary<string, string>
        {
            ["StrideBlend_N_Walk"] = "StrideBlend_N_Walk", ["StrideBlend_N_Run"] = "StrideBlend_N_Run",
            ["StrideBlend_C_Walk"] = "StrideBlend_N_Walk", ["DiagonalScaleAmountCurve"] = "DiagonalScaleAmount",
            ["LeanInAirCurve"] = "LeanInAirAmount", ["LandPredictionCurve"] = "LandPredictionBlend",
        };
        Require(assets.Count == 5 && bindings.EnumerateObject().Count() == 6, "Wrong native curve closure.");
        var bound = new Dictionary<string, AlsMovementInputCurve>();
        foreach (var (variable, name) in expected)
        {
            var path = "/Game/AdvancedLocomotionV4/Data/Curves/AnimationBlendCurves/" + name + "." + name;
            Require(Text(bindings, variable) == path && assets.ContainsKey(path), "Input curve variable binding differs.");
            bound.Add(variable, assets[path]);
        }
        return new(new(defaults["AnimatedWalkSpeed"] * .01f, defaults["AnimatedRunSpeed"] * .01f, defaults["AnimatedSprintSpeed"] * .01f),
            defaults["AnimatedCrouchSpeed"] * .01f, defaults["VelocityBlendInterpSpeed"], defaults["GroundedLeanInterpSpeed"],
            defaults["InAirLeanInterpSpeed"], new ReadOnlyDictionary<string, float>(defaults),
            new ReadOnlyDictionary<string, AlsMovementInputCurve>(bound));
    }
    private static float Number(JsonElement value, string name)
    { var number = value.GetProperty(name).GetSingle(); Require(float.IsFinite(number), "Non-finite native input value."); return number; }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
}
