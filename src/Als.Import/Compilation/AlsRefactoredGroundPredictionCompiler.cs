using System.Text.Json;
using GodotAls.Core.Curves;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed record AlsRefactoredGroundPredictionProfile(AlsRefactoredGroundPrediction Model,
    AlsMovementInputCurve AmountCurve, string SweepChannel, string[] BlockingObjectChannels);

public static class AlsRefactoredGroundPredictionCompiler
{
    public static AlsRefactoredGroundPredictionProfile Compile(string json)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 &&
            Text(root, "animationClass") == "/ALS/ALS/Character/AB_Als.AB_Als_C" &&
            Text(root, "settings") == "/ALS/ALS/Data/AnimationInstance/AIS_Als_Default.AIS_Als_Default", "Wrong native settings owner.");
        var channel = Text(root, "sweepChannel");
        var responses = root.GetProperty("responseChannels").EnumerateArray().Select(v => v.GetString()!).ToArray();
        Require(channel == "<CollisionChannel.ECC_VISIBILITY: 3>" && responses.SequenceEqual(new[] {
            "<CollisionChannel.ECC_WORLD_STATIC: 0>", "<CollisionChannel.ECC_WORLD_DYNAMIC: 1>", "<CollisionChannel.ECC_DESTRUCTIBLE: 7>" }),
            "Unsupported Refactored sweep channel/response mapping.");
        var data = root.GetProperty("curve");
        Require(Text(data, "path") == "/ALS/ALS/Data/AnimationInstance/Air/CF_Als_GroundPredictionAmount.CF_Als_GroundPredictionAmount" &&
            Text(data, "preInfinityExtrap") == "RCCE_Constant" && Text(data, "postInfinityExtrap") == "RCCE_Constant", "Wrong native prediction curve.");
        var keys = data.GetProperty("keys").EnumerateArray().Select(k =>
        {
            Require(Text(k, "tangentWeightMode") == "RCTWM_WeightedNone", "Weighted prediction tangent unsupported.");
            return new AlsCurveKey(Number(k, "time"), Number(k, "value"), Number(k, "arriveTangent"), Number(k, "leaveTangent"),
                Text(k, "interpMode") switch {
                    "RCIM_Constant" => AlsCurveInterpolationMode.Constant, "RCIM_Linear" => AlsCurveInterpolationMode.Linear,
                    "RCIM_Cubic" => AlsCurveInterpolationMode.Cubic, _ => throw new InvalidDataException("Unsupported prediction interpolation.") });
        }).ToArray();
        Require(keys.Length >= 2 && keys[0].TimeSeconds == 0 && keys[^1].TimeSeconds == 1, "Prediction curve must cover hit fraction 0..1.");
        var curve = new AlsMovementInputCurve(keys);
        var samples = data.GetProperty("verification");
        Require(samples.GetArrayLength() == 1001, "Incomplete native prediction response samples.");
        for (var i = 0; i < samples.GetArrayLength(); i++)
        {
            var sample = samples[i]; var input = Number(sample, "input");
            Require(input == i / 1000f && Math.Abs(curve.Sample(input) - Number(sample, "value")) <= 2e-6f,
                "Prediction response differs from native GetFloatValue.");
        }
        return new(new(curve), curve, channel, responses);
    }
    private static float Number(JsonElement value, string name)
    { var number = value.GetProperty(name).GetSingle(); Require(float.IsFinite(number), "Nonfinite prediction input."); return number; }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
