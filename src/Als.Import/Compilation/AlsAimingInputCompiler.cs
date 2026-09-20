using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public static class AlsAimingInputCompiler
{
    private const string Source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";
    public static AlsAimingInputModel Compile(string layeringJson, string nativeJson)
    {
        using var layering = JsonDocument.Parse(layeringJson); using var native = JsonDocument.Parse(nativeJson);
        var original = layering.RootElement; var root = native.RootElement;
        foreach (var document in new[] { original, root })
        {
            Require(document.GetProperty("schemaVersion").GetInt32() == 1 && document.GetProperty("source").GetString() == Source,
                "Unexpected aiming input source.");
            ValidateFunction(ReadGraph(document, "UpdateAimingValues"));
            ValidateUpdateOrder(ReadGraph(document, "UpdateGraph"));
        }
        Require(root.GetProperty("evaluation").GetString() == "actual_blueprint_UpdateAimingValues", "Aiming data was not evaluated by the Blueprint.");
        var settings = root.GetProperty("settings"); var defaults = original.GetProperty("defaultsText").GetString()!;
        var speed = settings.GetProperty("SmoothedAimingRotationInterpSpeed").GetDouble();
        var inputSpeed = settings.GetProperty("InputYawOffsetInterpSpeed").GetDouble();
        Require(speed == DefaultScalar(defaults, "SmoothedAimingRotationInterpSpeed") &&
            inputSpeed == DefaultScalar(defaults, "InputYawOffsetInterpSpeed"), "Aiming native defaults differ from the graph export.");
        var initial = ReadState(root.GetProperty("initial"));
        foreach (var name in new[] { "AimSweepTime", "InputYawOffsetTime", "LeftYawTime", "RightYawTime", "ForwardYawTime" })
            Require(root.GetProperty("initial").GetProperty(name).GetDouble() == DefaultScalar(defaults, name), "Aiming initial scalar changed: " + name);
        foreach (var name in new[] { "SmoothedAimingRotation", "SpineRotation", "AimingAngle", "SmoothedAimingAngle" })
        {
            var text = Regex.Match(defaults, @"(?m)^   " + name + @"=\(([^\r\n]+)\)").Groups[1].Value;
            var values = Regex.Matches(text, @"(?:Pitch|Yaw|Roll|X|Y)=([^,)]+)").Select(m => Number(m.Groups[1].Value)).ToArray();
            Require(values.SequenceEqual(root.GetProperty("initial").GetProperty(name).EnumerateArray().Select(x => x.GetDouble())),
                "Aiming initial structured property changed: " + name);
        }
        return new(new(speed, inputSpeed), initial);
    }

    private static void ValidateFunction(Graph graph)
    {
        var entry = graph.One("K2Node_FunctionEntry", "UpdateAimingValues");
        var sequence = graph.One("K2Node_ExecutionSequence", ""); graph.Link(sequence, "execute", entry, "then");
        Require(sequence.Pins.Values.Count(p => p.Output && p.Name.StartsWith("then_", StringComparison.Ordinal)) == 5,
            "Aiming execution branch count differs.");
        var smooth = Set(graph, "SmoothedAimingRotation"); Exec(graph, smooth, sequence, "then_0");
        var interp = FunctionInput(graph, smooth, "SmoothedAimingRotation", "RInterpTo");
        graph.Link(interp, "Current", smooth, "Output_Get");
        Getter(graph, interp, "Target", "AimingRotation"); Getter(graph, interp, "DeltaTime", "DeltaTimeX");
        Getter(graph, interp, "InterpSpeed", "SmoothedAimingRotationInterpSpeed");
        PinType(interp, "DeltaTime", "real", "float"); PinType(interp, "InterpSpeed", "real", "float");

        var angle = Set(graph, "AimingAngle"); var smoothedAngle = Set(graph, "SmoothedAimingAngle");
        Exec(graph, angle, sequence, "then_1"); Exec(graph, smoothedAngle, angle, "then");
        Angle(graph, angle, "AimingAngle", "AimingRotation"); Angle(graph, smoothedAngle, "SmoothedAimingAngle", "SmoothedAimingRotation");

        var cameraSwitch = graph.Named("K2Node_SwitchEnum_1"); var inputSwitch = graph.Named("K2Node_SwitchEnum_0");
        RotationSwitch(graph, cameraSwitch, sequence, "then_2"); RotationSwitch(graph, inputSwitch, sequence, "then_3");
        var joined = graph.Named("K2Node_Knot_0");
        graph.Links(joined, "InputPin", (cameraSwitch, "NewEnumerator1"), (cameraSwitch, "NewEnumerator3"));
        var sweep = Set(graph, "AimSweepTime"); graph.Link(sweep, "execute", joined, "OutputPin");
        var sweepMap = MapInput(graph, sweep, "AimSweepTime", -90, 90, 1, 0);
        Getter(graph, sweepMap, "Value", "AimingAngle", "AimingAngle_Y");
        var spine = Set(graph, "SpineRotation"); Exec(graph, spine, sweep, "then");
        Literal(graph, spine, "SpineRotation_Pitch", 0); Literal(graph, spine, "SpineRotation_Roll", 0);
        foreach (var pin in new[] { "SpineRotation_Pitch", "SpineRotation_Yaw", "SpineRotation_Roll" }) PinType(spine, pin, "real", "float");
        var divide = FunctionInput(graph, spine, "SpineRotation_Yaw", "Divide_DoubleDouble");
        Getter(graph, divide, "A", "AimingAngle", "AimingAngle_X"); Literal(graph, divide, "B", 4);

        var gate = graph.Named("K2Node_IfThenElse_1"); Require(gate.Kind == "K2Node_IfThenElse", "Aiming input gate changed.");
        Exec(graph, gate, inputSwitch, "NewEnumerator0"); Getter(graph, gate, "Condition", "HasMovementInput");
        var input = Set(graph, "InputYawOffsetTime"); Exec(graph, input, gate, "then");
        Require(gate.Pins.Values.Single(p => p.Name == "else").Links.Length == 0, "No-input aiming must preserve history.");
        var scalarInterp = FunctionInput(graph, input, "InputYawOffsetTime", "FInterpTo");
        graph.Link(scalarInterp, "Current", input, "Output_Get"); Getter(graph, scalarInterp, "DeltaTime", "DeltaTimeX");
        Getter(graph, scalarInterp, "InterpSpeed", "InputYawOffsetInterpSpeed");
        foreach (var pin in new[] { "Current", "Target", "DeltaTime", "InterpSpeed", "ReturnValue" }) PinType(scalarInterp, pin, "real", "double");
        var inputMap = MapInput(graph, scalarInterp, "Target", -180, 180, 0, 1);
        var (delta, deltaPin) = graph.Follow(inputMap, "Value");
        graph.Function(delta, "NormalizedDeltaRotator", "Engine.KismetMathLibrary");
        Require(deltaPin.Name == "ReturnValue_Yaw", "Input offset uses a different rotator component.");
        PinType(delta, "ReturnValue_Yaw", "real", "float");
        var vectorRotation = FunctionInput(graph, delta, "A", "Conv_VectorToRotator");
        Getter(graph, vectorRotation, "InVec", "MovementInput"); Actor(graph, delta, "B");

        var left = Set(graph, "LeftYawTime"); var right = Set(graph, "RightYawTime"); var forward = Set(graph, "ForwardYawTime");
        Exec(graph, left, sequence, "then_4"); Exec(graph, right, left, "then"); Exec(graph, forward, right, "then");
        var leftMap = MapInput(graph, left, "LeftYawTime", 0, 180, .5, 0);
        var rightMap = MapInput(graph, right, "RightYawTime", 0, 180, .5, 1);
        var abs = FunctionInput(graph, leftMap, "Value", "Abs"); graph.Link(rightMap, "Value", abs, "ReturnValue");
        Getter(graph, abs, "A", "SmoothedAimingAngle", "SmoothedAimingAngle_X");
        var forwardMap = MapInput(graph, forward, "ForwardYawTime", -180, 180, 0, 1);
        Getter(graph, forwardMap, "Value", "SmoothedAimingAngle", "SmoothedAimingAngle_X");
        foreach (var terminal in new[] { smooth, smoothedAngle, spine, input, forward })
            Require(terminal.Pins.Values.Single(p => p.Name == "then").Links.Length == 0, "Unexpected aiming branch tail.");
        Require(cameraSwitch.Pins.Values.Single(p => p.Name == "NewEnumerator0").Links.Length == 0 &&
            inputSwitch.Pins.Values.Single(p => p.Name == "NewEnumerator1").Links.Length == 0 &&
            inputSwitch.Pins.Values.Single(p => p.Name == "NewEnumerator3").Links.Length == 0, "Aiming mode history gate changed.");
    }

    private static void ValidateUpdateOrder(Graph graph)
    {
        var character = graph.One("K2Node_CallFunction", "UpdateCharacterInfo");
        var aiming = graph.One("K2Node_CallFunction", "UpdateAimingValues");
        var layers = graph.One("K2Node_CallFunction", "UpdateLayerValues"); var feet = graph.One("K2Node_CallFunction", "UpdateFootIK");
        foreach (var node in new[] { character, aiming, layers, feet }) graph.Self(node);
        graph.Link(aiming, "execute", character, "then"); graph.Link(layers, "execute", aiming, "then"); graph.Link(feet, "execute", layers, "then");
    }
    private static void Angle(Graph graph, Node setter, string variable, string rotation)
    {
        var make = FunctionInput(graph, setter, variable, "MakeVector2D");
        var (delta, yaw) = graph.Follow(make, "X"); graph.Function(delta, "NormalizedDeltaRotator", "Engine.KismetMathLibrary");
        Require(yaw.Name == "ReturnValue_Yaw", "Aiming yaw axis changed."); graph.Link(make, "Y", delta, "ReturnValue_Pitch");
        PinType(delta, "ReturnValue_Yaw", "real", "float"); PinType(delta, "ReturnValue_Pitch", "real", "float");
        Getter(graph, delta, "A", rotation); Actor(graph, delta, "B");
    }
    private static void Actor(Graph graph, Node node, string pin)
    {
        var (actor, output) = graph.Follow(node, pin); graph.Function(actor, "K2_GetActorRotation", "Engine.Actor");
        Require(output.Name == "ReturnValue", "Aiming character rotation output changed."); Getter(graph, actor, "self", "Character");
    }
    private static void RotationSwitch(Graph graph, Node node, Node sequence, string branch)
    {
        Require(node.Kind == "K2Node_SwitchEnum" && node.Body.Contains("ALS_RotationMode.ALS_RotationMode", StringComparison.Ordinal) &&
            node.Body.Contains("EnumEntries(0)=\"NewEnumerator0\"", StringComparison.Ordinal) &&
            node.Body.Contains("EnumEntries(1)=\"NewEnumerator1\"", StringComparison.Ordinal) &&
            node.Body.Contains("EnumEntries(2)=\"NewEnumerator3\"", StringComparison.Ordinal), "Aiming rotation enum differs.");
        Exec(graph, node, sequence, branch); Getter(graph, node, "Selection", "RotationMode");
    }
    private static Node MapInput(Graph graph, Node node, string pin, double a, double b, double from, double to)
    {
        var map = FunctionInput(graph, node, pin, "MapRangeClamped");
        Literal(graph, map, "InRangeA", a); Literal(graph, map, "InRangeB", b);
        Literal(graph, map, "OutRangeA", from); Literal(graph, map, "OutRangeB", to); return map;
    }
    private static Node FunctionInput(Graph graph, Node node, string pin, string function)
    {
        var (source, output) = graph.Follow(node, pin); graph.Function(source, function, "Engine.KismetMathLibrary");
        Require(output.Name == "ReturnValue", "Aiming expression output differs."); return source;
    }
    private static void Getter(Graph graph, Node node, string pin, string variable, string? output = null)
    {
        var (getter, value) = graph.Follow(node, pin);
        Require(getter.Kind == "K2Node_VariableGet" && getter.Member == variable && value.Name == (output ?? variable), "Aiming property connection differs: " + variable);
        graph.Self(getter);
    }
    private static Node Set(Graph graph, string variable) { var node = graph.One("K2Node_VariableSet", variable); graph.Self(node); return node; }
    private static void Exec(Graph graph, Node node, Node previous, string pin)
    { var (source, output) = graph.FollowReroutes(node, "execute"); Require(source == previous && output.Name == pin, "Aiming execution order differs."); }
    private static void Literal(Graph graph, Node node, string pin, double expected) => Require(Number(graph.Literal(node, pin)) == expected, "Aiming constant differs: " + pin);
    private static void PinType(Node node, string name, string category, string subcategory)
    {
        var id = node.Pins.Values.Single(p => p.Name == name).Id;
        var line = Regex.Matches(node.Body, @"CustomProperties Pin [^\r\n]+").Single(m => m.Value.Contains("PinId=" + id + ",", StringComparison.Ordinal)).Value;
        Require(line.Contains("PinType.PinCategory=\"" + category + "\"", StringComparison.Ordinal) &&
            line.Contains("PinType.PinSubCategory=\"" + subcategory + "\"", StringComparison.Ordinal), "Aiming numeric precision changed.");
    }
    private static Graph ReadGraph(JsonElement root, string name)
    {
        var graph = root.GetProperty("graphs").EnumerateArray().Single(g => g.GetProperty("path").GetString() == Source + ":" + name);
        Require(graph.GetProperty("name").GetString() == name, "Aiming graph identity changed."); return new(graph.GetProperty("nativeText").GetString()!);
    }
    internal static AlsAimingInputState ReadState(JsonElement value) => new(default,
        Rotation(value.GetProperty("SmoothedAimingRotation")), Angle(value.GetProperty("AimingAngle")), Angle(value.GetProperty("SmoothedAimingAngle")),
        Rotation(value.GetProperty("SpineRotation")), value.GetProperty("AimSweepTime").GetDouble(), value.GetProperty("InputYawOffsetTime").GetDouble(),
        value.GetProperty("LeftYawTime").GetDouble(), value.GetProperty("RightYawTime").GetDouble(), value.GetProperty("ForwardYawTime").GetDouble());
    private static AlsAimingRotation Rotation(JsonElement array) => new(array[0].GetDouble(), array[1].GetDouble(), array[2].GetDouble());
    private static AlsAimingAngle Angle(JsonElement array) => new(array[0].GetDouble(), array[1].GetDouble());
    private static double DefaultScalar(string text, string name) => Number(Regex.Match(text, @"(?m)^   " + name + @"=([^\r\n]+)").Groups[1].Value);
    private static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);
    private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
}
