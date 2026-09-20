using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Curves;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public static partial class AlsCharacterMovementCompiler
{
    private const string Source = "/Game/AdvancedLocomotionV4/Blueprints/CharacterLogic/ALS_Base_CharacterBP.ALS_Base_CharacterBP";
    private const string CurveDirectory = "/Game/AdvancedLocomotionV4/Data/Curves/CharacterMovementCurves/";

    public static AlsCharacterMovementModel Compile(string text, string rotationText)
    {
        using var document = JsonDocument.Parse(text);
        using var rotationDocument = JsonDocument.Parse(rotationText);
        var root = document.RootElement; var rotationRoot = rotationDocument.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && Text(root, "source") == Source, "Foreign movement schema/source.");
        // Reuse the already strict GetMappedSpeed/mode/stance/history validators.
        // Validate the newly exported functions themselves. Native T3D can gain
        // unrelated generated property metadata when other assets are loaded.
        var validation = JsonNode.Parse(rotationText)!;
        foreach (var name in new[] { "GetMappedSpeed", "GetTargetMovementSettings", "UpdateCharacterMovement", "TickGraph", "SetEssentialValues" })
        {
            var graph = validation["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == name)!;
            graph["nativeText"] = GraphText(root, name);
        }
        var rotation = AlsCharacterRotationCompiler.Compile(validation.ToJsonString());
        ValidateDynamic(new Graph(GraphText(root, "UpdateDynamicMovementSettings")));
        var model = root.GetProperty("movementModel");
        Require(JsonSerializer.Serialize(model) == JsonSerializer.Serialize(rotationRoot.GetProperty("movementModel")), "Movement table differs from validated character rotation.");
        var curves = ReadCurves(root);
        var row = model.GetProperty("rows").EnumerateArray().Single(r => Text(r, "Name") == Text(model, "row"));
        var entries = new List<AlsCharacterMovementEntry>();
        var modeIndex = 0;
        foreach (var mode in new[] { "VelocityDirection", "LookingDirection", "Aiming" })
        {
            var stanceIndex = 0;
            foreach (var stance in new[] { "Standing", "Crouching" })
            {
                var item = row.GetProperty(mode).GetProperty(stance);
                var reference = Regex.Match(Text(item, "Movement Curve"), "^/Script/Engine.CurveVector'([^']+)'$");
                Require(reference.Success && curves.ContainsKey(reference.Groups[1].Value), "Unbound movement vector curve.");
                var channels = curves[reference.Groups[1].Value];
                var walk = Number(item, "Walk Speed"); var run = Number(item, "Run Speed"); var sprint = Number(item, "Sprint Speed");
                var other = rotation.Movement((AlsRotationMode)modeIndex, (AlsStance)stanceIndex++);
                Require(walk * .01f == other.WalkSpeed && run * .01f == other.RunSpeed && sprint * .01f == other.SprintSpeed,
                    "Physical and rotation speed tables disagree.");
                entries.Add(new(walk, run, sprint, channels[0], channels[1], channels[2]));
            }
            modeIndex++;
        }
        Require(Text(root, "componentPath") == "/Game/AdvancedLocomotionV4/Blueprints/CharacterLogic/ALS_Base_CharacterBP.Default__ALS_Base_CharacterBP_C:CharMoveComp", "Foreign movement component defaults.");
        var defaults = root.GetProperty("componentDefaults");
        return new(entries, new(Number(defaults, "braking_friction_factor"), Number(defaults, "braking_sub_step_time"),
            defaults.GetProperty("use_separate_braking_friction").GetBoolean(), Number(defaults, "braking_friction"), Number(defaults, "min_analog_walk_speed")));
    }

    private static Dictionary<string, AlsMovementInputCurve[]> ReadCurves(JsonElement root)
    {
        var result = new Dictionary<string, AlsMovementInputCurve[]>(StringComparer.Ordinal);
        foreach (var entry in root.GetProperty("movementCurves").EnumerateArray())
        {
            var path = Text(entry, "path");
            Require(path.StartsWith(CurveDirectory, StringComparison.Ordinal), "Foreign movement curve asset.");
            var lines = Regex.Matches(Text(entry, "nativeText"), @"(?m)^   FloatCurves\((\d+)\)=([^\r\n]+)");
            Require(lines.Count == 3, "Movement curve requires all three native channels.");
            var channels = new AlsMovementInputCurve[3];
            for (var axis = 0; axis < 3; axis++)
            {
                Require(lines[axis].Groups[1].Value == axis.ToString(CultureInfo.InvariantCulture), "Reordered movement channels.");
                var line = lines[axis].Groups[2].Value;
                foreach (Match mode in Regex.Matches(line, @"(?:Pre|Post)InfinityExtrap=(\w+)"))
                    Require(mode.Groups[1].Value == "RCCE_Constant", "Unsupported movement curve infinity mode.");
                var keysText = Regex.Match(line, @"Keys=\(\((.*?)\)\)").Groups[1].Value;
                Require(keysText.Length > 0, "Missing movement rich-curve keys.");
                var keys = new List<AlsCurveKey>();
                foreach (var keyText in Regex.Split(keysText, @"\),\("))
                {
                    var fields = keyText.Split(',').Select(f => f.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
                    Require(fields.Keys.All(k => k is "InterpMode" or "TangentMode" or "TangentWeightMode" or "Time" or "Value" or
                        "ArriveTangent" or "LeaveTangent" or "ArriveTangentWeight" or "LeaveTangentWeight"), "Unknown movement key field.");
                    Require(fields.GetValueOrDefault("TangentWeightMode", "RCTWM_WeightedNone") == "RCTWM_WeightedNone", "Weighted movement keys require an explicit implementation.");
                    var interpolation = fields.GetValueOrDefault("InterpMode", "RCIM_Linear") switch
                    {
                        "RCIM_Linear" => AlsCurveInterpolationMode.Linear, "RCIM_Constant" => AlsCurveInterpolationMode.Constant,
                        "RCIM_Cubic" => AlsCurveInterpolationMode.Cubic, _ => throw new FormatException("Unknown movement interpolation.")
                    };
                    keys.Add(new(Read("Time"), Read("Value"), Read("ArriveTangent"), Read("LeaveTangent"), interpolation));
                    float Read(string key) => Parse(fields.GetValueOrDefault(key, "0"));
                }
                channels[axis] = new(keys.ToArray());
            }
            var verification = entry.GetProperty("verification"); Require(verification.GetArrayLength() == 401, "Incomplete vector curve verification.");
            var index = -50;
            foreach (var sample in verification.EnumerateArray())
            {
                var input = Number(sample, "input"); Require(MathF.Abs(input - index++ / 100f) < 1e-6f, "Changed curve verification domain.");
                for (var axis = 0; axis < 3; axis++)
                {
                    var expected = sample.GetProperty("value")[axis].GetSingle();
                    var actual = channels[axis].Sample(input);
                    Require(float.IsFinite(expected) && expected >= 0 && MathF.Abs(actual - expected) <= .001f,
                        "Movement key evaluation differs from native GetVectorValue.");
                }
            }
            result.Add(path, channels);
        }
        Require(result.Count == 3 && new[] { "NormalMovement", "ResponsiveMovement", "SluggishMovement" }
            .All(n => result.ContainsKey(CurveDirectory + n + "." + n)), "Incomplete movement table curve closure.");
        return result;
    }

    private static void ValidateDynamic(Graph graph)
    {
        var entry = graph.One("K2Node_FunctionEntry", "UpdateDynamicMovementSettings");
        var current = graph.One("K2Node_VariableSet", "CurrentMovementSettings"); graph.Self(current);
        var target = graph.One("K2Node_CallFunction", "GetTargetMovementSettings"); graph.Self(target);
        graph.Link(current, "execute", entry, "then"); graph.Link(current, "CurrentMovementSettings", target, "MovementSettings");
        var sequence = graph.Named("K2Node_ExecutionSequence_0"); graph.Link(sequence, "execute", current, "then");
        var sample = graph.One("K2Node_CallFunction", "GetVectorValue"); graph.Function(sample, "GetVectorValue", "Engine.CurveVector");
        var mapped = graph.One("K2Node_CallFunction", "GetMappedSpeed"); graph.Self(mapped);
        graph.Link(sample, "InTime", mapped, "ReturnValue");
        var (curve, curvePin) = graph.Follow(sample, "self");
        Require(curve.Kind == "K2Node_BreakStruct" && curvePin.Name.StartsWith("MovementCurve_", StringComparison.Ordinal), "Wrong vector movement asset binding.");
        var (settings, settingPin) = graph.Follow(curve, "MovementSettings");
        Require(settings.Member == "CurrentMovementSettings" && settingPin.Name == settings.Member, "Curve must read current settings."); graph.Self(settings);
        var acceleration = graph.One("K2Node_VariableSet", "MaxAcceleration");
        var braking = graph.One("K2Node_VariableSet", "BrakingDecelerationWalking");
        var friction = graph.One("K2Node_VariableSet", "GroundFriction");
        var (from, output) = graph.FollowReroutes(acceleration, "execute");
        Require(from == sequence && output.Name == "then_1", "Dynamic force updates must follow speed assignment.");
        graph.Link(braking, "execute", acceleration, "then"); graph.Link(friction, "execute", braking, "then");
        foreach (var (setter, axis) in new[] { (acceleration, "X"), (braking, "Y"), (friction, "Z") })
        {
            graph.Link(setter, setter.Member, sample, "ReturnValue_" + axis);
            ValidateComponent(setter);
        }
        var speed = graph.One("K2Node_VariableSet", "MaxWalkSpeed");
        var crouched = graph.One("K2Node_VariableSet", "MaxWalkSpeedCrouched");
        (from, output) = graph.FollowReroutes(speed, "execute"); Require(from == sequence && output.Name == "then_0", "Wrong speed update order.");
        graph.Link(crouched, "execute", speed, "then"); graph.Link(crouched, "MaxWalkSpeedCrouched", speed, "Output_Get");
        ValidateComponent(speed); ValidateComponent(crouched);
        var (select, selectPin) = graph.Follow(speed, "MaxWalkSpeed"); Require(select.Kind == "K2Node_Select" && selectPin.Name == "ReturnValue", "Missing gait speed selection.");
        var (gait, gaitPin) = graph.Follow(select, "Index"); Require(gait.Member == "AllowedGait" && gaitPin.Name == "AllowedGait", "Speed ignores allowed gait.");
        var index = 0;
        foreach (var field in new[] { "WalkSpeed", "RunSpeed", "SprintSpeed" })
        {
            var (structure, fieldPin) = graph.Follow(select, "NewEnumerator" + index++);
            Require(structure.Kind == "K2Node_BreakStruct" && fieldPin.Name.StartsWith(field + "_", StringComparison.Ordinal), "Wrong gait speed mapping.");
            var (value, valuePin) = graph.Follow(structure, "MovementSettings");
            Require(value.Member == "CurrentMovementSettings" && valuePin.Name == value.Member, "Speed uses stale/foreign movement settings."); graph.Self(value);
        }
        void ValidateComponent(Node setter)
        {
            Require(setter.Body.Contains("/Script/Engine.CharacterMovementComponent", StringComparison.Ordinal), "Foreign movement property owner.");
            var (component, pin) = graph.Follow(setter, "self");
            Require(component.Member == "CharacterMovement" && pin.Name == component.Member, "Movement writes a different component."); graph.Self(component);
        }
    }

    private static string GraphText(JsonElement root, string name)
    {
        var graph = root.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "name") == name);
        Require(Text(graph, "path") == Source + ":" + name, "Foreign movement graph.");
        return Text(graph, "nativeText").Replace("\r\n", "\n", StringComparison.Ordinal);
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static float Number(JsonElement value, string name) => Parse(value.GetProperty(name).GetRawText());
    private static float Parse(string value)
    { var number = float.Parse(value, CultureInfo.InvariantCulture); Require(float.IsFinite(number), "Nonfinite movement input."); return number; }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Character movement: " + message); }
}
