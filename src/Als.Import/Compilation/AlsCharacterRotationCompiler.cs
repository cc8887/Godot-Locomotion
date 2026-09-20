using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Curves;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public static partial class AlsCharacterRotationCompiler
{
    private const string Source = "/Game/AdvancedLocomotionV4/Blueprints/CharacterLogic/ALS_Base_CharacterBP.ALS_Base_CharacterBP";
    private const string CurveDirectory = "/Game/AdvancedLocomotionV4/Data/Curves/CharacterMovementCurves/";
    public static AlsCharacterRotationModel Compile(string text)
    {
        using var document = JsonDocument.Parse(text); var root = document.RootElement;
        var bridge = AlsCharacterAnimationBridgeCompiler.Compile(text);
        var settings = ValidateGraphs(root, bridge);
        var curves = ReadCurves(root);
        var model = root.GetProperty("movementModel");
        var table = Text(model, "path"); var rowName = Text(model, "row");
        Require(table == "/Game/AdvancedLocomotionV4/Data/DataTables/MovementModelTable.MovementModelTable" && rowName == "Normal",
            "Unexpected default movement model binding.");
        Require(Text(root, "defaultsText").Contains("MovementModel=(DataTable=\"/Script/Engine.DataTable'" + table + "'\",RowName=\"" + rowName + "\")", StringComparison.Ordinal),
            "Movement table differs from the character CDO.");
        var row = model.GetProperty("rows").EnumerateArray().Single(r => Text(r, "Name") == rowName);
        var movement = new List<AlsCharacterRotationMovement>();
        foreach (var mode in new[] { "VelocityDirection", "LookingDirection", "Aiming" })
        foreach (var stance in new[] { "Standing", "Crouching" })
        {
            var entry = row.GetProperty(mode).GetProperty(stance);
            var reference = Regex.Match(Text(entry, "Rotation Rate Curve"), "^/Script/Engine.CurveFloat'([^']+)'$");
            Require(reference.Success && curves.ContainsKey(reference.Groups[1].Value), "Unbound movement rotation curve.");
            movement.Add(new(Number(entry, "Walk Speed") * .01f, Number(entry, "Run Speed") * .01f,
                Number(entry, "Sprint Speed") * .01f, curves[reference.Groups[1].Value]));
        }
        var defaults = Text(root, "defaultsText");
        var desiredGait = Regex.Match(defaults, @"(?m)^   DesiredGait=NewEnumerator([012])\b");
        var initialMovement = Regex.Match(defaults, @"(?m)^   CurrentMovementSettings=([^\r\n]+)").Groups[1].Value;
        Require(desiredGait.Success && initialMovement.Length > 0, "Missing initial character movement settings.");
        var result = new AlsCharacterRotationModel(settings, movement)
        {
            InitialGait = (AlsGait)int.Parse(desiredGait.Groups[1].Value, CultureInfo.InvariantCulture),
            InitialWalkSpeed = InitialSpeed("WalkSpeed"), InitialRunSpeed = InitialSpeed("RunSpeed"),
        };
        float InitialSpeed(string field) => Parse(Regex.Match(initialMovement, field + @"_\w+=([0-9.]+)").Groups[1].Value) * .01f;
        Require(result.InitialWalkSpeed > 0 && result.InitialRunSpeed > result.InitialWalkSpeed, "Invalid initial character gait thresholds.");
        ValidateNativeInterpolation(root);
        return result;
    }

    private static Dictionary<string, AlsMovementInputCurve> ReadCurves(JsonElement root)
    {
        var result = new Dictionary<string, AlsMovementInputCurve>(StringComparer.Ordinal);
        foreach (var entry in root.GetProperty("rotationCurves").EnumerateArray())
        {
            var path = Text(entry, "path");
            Require(path.StartsWith(CurveDirectory, StringComparison.Ordinal), "Foreign rotation curve owner.");
            var source = Text(entry, "nativeText");
            var line = Regex.Match(source, @"(?m)^   FloatCurve=([^\r\n]+)").Groups[1].Value;
            // Exported FRichCurve defaults omit constant extrapolation and unweighted tangents.
            foreach (Match mode in Regex.Matches(line, @"(?:Pre|Post)InfinityExtrap=(\w+)"))
                Require(mode.Groups[1].Value == "RCCE_Constant", "Unsupported rotation extrapolation.");
            var keysText = Regex.Match(line, @"Keys=\(\((.*?)\)\)").Groups[1].Value;
            Require(keysText.Length > 0, "Missing native rotation keys.");
            var keys = new List<AlsCurveKey>();
            foreach (var keyText in Regex.Split(keysText, @"\),\("))
            {
                var fields = keyText.Split(',').Select(field => field.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
                Require(fields.Keys.All(k => k is "InterpMode" or "TangentMode" or "TangentWeightMode" or "Time" or "Value" or
                    "ArriveTangent" or "LeaveTangent" or "ArriveTangentWeight" or "LeaveTangentWeight"), "Unsupported rotation key field.");
                Require(!fields.TryGetValue("TangentWeightMode", out var weighted) || weighted == "RCTWM_WeightedNone", "Weighted rotation curves are unsupported.");
                var mode = fields.GetValueOrDefault("InterpMode", "RCIM_Linear") switch
                {
                    "RCIM_Constant" => AlsCurveInterpolationMode.Constant, "RCIM_Linear" => AlsCurveInterpolationMode.Linear,
                    "RCIM_Cubic" => AlsCurveInterpolationMode.Cubic, _ => throw new FormatException("Unknown rotation interpolation.")
                };
                keys.Add(new(Read("Time"), Read("Value"), Read("ArriveTangent"), Read("LeaveTangent"), mode));
                float Read(string name) => fields.TryGetValue(name, out var value) ? Parse(value) : 0;
            }
            var curve = new AlsMovementInputCurve(keys.ToArray()); result.Add(path, curve);
            var samples = entry.GetProperty("verification"); Require(samples.GetArrayLength() == 401, "Incomplete native rotation curve verification.");
            var index = -50;
            foreach (var sample in samples.EnumerateArray())
            {
                var input = Number(sample, "input"); Require(MathF.Abs(input - index++ / 100f) < 1e-6f, "Rotation verification domain changed.");
                Require(MathF.Abs(curve.Sample(input) - Number(sample, "value")) < .00001f, "Rotation curve differs from UE GetFloatValue.");
            }
        }
        Require(result.Count == 3 && new[] { "NormalRotation", "ResponsiveRotation", "SluggishRotation" }
            .All(name => result.ContainsKey(CurveDirectory + name + "." + name)), "Incomplete movement table rotation curve closure.");
        return result;
    }

    private static void ValidateNativeInterpolation(JsonElement root)
    {
        var samples = root.GetProperty("interpolationVerification"); Require(samples.GetArrayLength() == 320, "Incomplete native rotator verification.");
        foreach (var sample in samples.EnumerateArray())
        {
            var current = sample.GetProperty("current").GetDouble(); var target = sample.GetProperty("target").GetDouble();
            var delta = Number(sample, "delta"); var rate = Number(sample, "rate");
            var constant = sample.GetProperty("constant").GetDouble(); var smooth = sample.GetProperty("smooth").GetDouble();
            Require(double.IsFinite(constant) && double.IsFinite(smooth) &&
                System.Math.Abs(AlsCharacterRotationMath.Constant(current, target, delta, rate) - constant) <= .00001 &&
                System.Math.Abs(AlsCharacterRotationMath.Smooth(current, target, delta, rate) - smooth) <= .00001,
                "Character interpolation differs from UE RInterpTo/RInterpConstantTo.");
        }
    }

    private static Graph Graph(JsonElement root, string name, bool history = false)
    {
        var item = root.GetProperty(history ? "rotationHistoryGraphs" : "graphs").EnumerateArray().Single(g => Text(g, "name") == name);
        Require(Text(item, "path") == Source + ":" + name, "Foreign character function.");
        return new(Text(item, "nativeText"));
    }
    private static Node Member(Graph graph, string name, string member)
    { var node = graph.Named(name); Require(node.Member == member, "Changed rotation function/variable: " + name); return node; }
    private static void Variable(Graph graph, string node, string member)
    { var value = Member(graph, node, member); Require(value.Kind == "K2Node_VariableGet", "Expected character variable."); graph.Self(value); }
    private static void Edge(Graph graph, string node, string pin, string source, string output) => graph.Link(graph.Named(node), pin, graph.Named(source), output);
    private static void Literal(Graph graph, string node, string pin, string expected) =>
        Require(graph.Literal(graph.Named(node), pin) == expected, "Changed rotation literal: " + node + "." + pin);
    private static float Number(Graph graph, string node, string pin) => Parse(graph.Literal(graph.Named(node), pin));
    private static float Number(JsonElement value, string name) => Parse(value.GetProperty(name).GetRawText());
    private static float Parse(string value)
    { var number = float.Parse(value, CultureInfo.InvariantCulture); Require(float.IsFinite(number), "Nonfinite character input."); return number; }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Character rotation: " + message); }
}
