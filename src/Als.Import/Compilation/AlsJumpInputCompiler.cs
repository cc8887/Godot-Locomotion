using System.Globalization;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public static class AlsJumpInputCompiler
{
    public static AlsJumpAnimationInputModel Compile(string movementJson, string jumpJson)
    {
        using var movement = JsonDocument.Parse(movementJson); using var jump = JsonDocument.Parse(jumpJson);
        var root = movement.RootElement; var data = jump.RootElement;
        Require(Text(root, "source") == AlsMovementInputCurveCompiler.Source && Text(data, "source") == AlsMovementInputCurveCompiler.Source &&
            data.GetProperty("schemaVersion").GetInt32() == 1, "Wrong jump input source.");
        var nodes = root.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "path") == AlsMovementInputCurveCompiler.Source + ":EventGraph")
            .GetProperty("nodes").EnumerateArray().ToDictionary(n => Text(n, "name"));
        var closure = new HashSet<string>(); var pending = new Stack<string>(); pending.Push("K2Node_Event_6");
        while (pending.TryPop(out var id))
        {
            if (!closure.Add(id)) continue;
            foreach (var pin in nodes[id].GetProperty("pins").EnumerateArray())
                foreach (var link in pin.GetProperty("links").EnumerateArray()) pending.Push(Text(link, "node"));
        }
        Require(closure.SetEquals(["K2Node_Event_6", "K2Node_VariableSet_4", "K2Node_VariableSet_20", "K2Node_CallFunction_31",
            "K2Node_CallFunction_8", "K2Node_VariableSet_1", "K2Node_VariableGet_0"]), "Unsupported jump event closure.");
        var entry = nodes["K2Node_Event_6"]; Kind(entry, "K2Node_Event");
        var properties = entry.GetProperty("properties"); var reference = properties.GetProperty("EventReference");
        Require(Text(reference, "memberName") == "BPI_Jumped" && Text(reference, "memberParent") ==
            "/Game/AdvancedLocomotionV4/Blueprints/Interfaces/ALS_Animation_BPI.ALS_Animation_BPI_C" &&
            !reference.GetProperty("bSelfContext").GetBoolean() && properties.GetProperty("bOverrideFunction").GetBoolean(), "Wrong jump event interface.");
        Variable("K2Node_VariableSet_4", "Jumped", true); Variable("K2Node_VariableSet_1", "Jumped", true);
        Variable("K2Node_VariableSet_20", "JumpPlayRate", true); Variable("K2Node_VariableGet_0", "Speed", false);
        Function("K2Node_CallFunction_8", "MapRangeClamped", "/Script/Engine.KismetMathLibrary");
        Function("K2Node_CallFunction_31", "Delay", "/Script/Engine.KismetSystemLibrary");
        Edge("K2Node_VariableSet_4", "execute", "K2Node_Event_6", "then");
        Edge("K2Node_VariableSet_20", "execute", "K2Node_VariableSet_4", "then");
        Edge("K2Node_CallFunction_31", "execute", "K2Node_VariableSet_20", "then");
        Edge("K2Node_VariableSet_1", "execute", "K2Node_CallFunction_31", "then");
        Edge("K2Node_VariableSet_20", "JumpPlayRate", "K2Node_CallFunction_8", "ReturnValue");
        Edge("K2Node_CallFunction_8", "Value", "K2Node_VariableGet_0", "Speed");
        Require(Literal("K2Node_VariableSet_4", "Jumped") == "true" && Literal("K2Node_VariableSet_1", "Jumped") == "false",
            "Jumped pulse setters differ.");
        Require(Pin(nodes["K2Node_VariableSet_1"], "then").GetProperty("links").GetArrayLength() == 0, "Unsupported jump reset side effect.");
        Require(Literal("K2Node_CallFunction_31", "WorldContextObject") == "", "Foreign jump delay context.");
        var defaults = data.GetProperty("defaults");
        Require(defaults.EnumerateObject().Count() == 3 && defaults.GetProperty("JumpPlayRate").GetSingle() ==
            root.GetProperty("movementInputDefaults").GetProperty("JumpPlayRate").GetSingle() && defaults.GetProperty("Speed").GetDouble() ==
            root.GetProperty("movementInputStateDefaults").GetProperty("Speed").GetProperty("value").GetDouble(), "Jump defaults differ from movement defaults.");
        var model = new AlsJumpAnimationInputModel(defaults.GetProperty("Jumped").GetBoolean(), defaults.GetProperty("JumpPlayRate").GetSingle(),
            Number("K2Node_CallFunction_8", "InRangeA"), Number("K2Node_CallFunction_8", "InRangeB"),
            Number("K2Node_CallFunction_8", "OutRangeA"), Number("K2Node_CallFunction_8", "OutRangeB"), (float)Number("K2Node_CallFunction_31", "Duration"));
        var samples = data.GetProperty("mapRangeCases").EnumerateArray().ToArray();
        Require(samples.Length == 7 && samples.Select(s => s.GetProperty("speedCm").GetDouble()).Order().SequenceEqual([0d, 75, 150, 300, 350, 600, 900]),
            "Incomplete native jump range cases.");
        foreach (var sample in samples)
        {
            var expected = sample.GetProperty("playRate").GetDouble();
            Require(double.IsFinite(expected) && System.Math.Abs(model.PlayRate(sample.GetProperty("speedCm").GetSingle() * .01f) - expected) <= 2e-7,
                "Jump rate differs from native MapRangeClamped.");
        }
        return model;

        void Variable(string id, string name, bool setter)
        {
            var node = nodes[id]; Kind(node, setter ? "K2Node_VariableSet" : "K2Node_VariableGet");
            var r = node.GetProperty("properties").GetProperty("VariableReference");
            Require(Text(r, "memberName") == name && Text(r, "memberParent") == "" && Text(r, "memberScope") == "" &&
                r.GetProperty("bSelfContext").GetBoolean() && Literal(id, "self") == "", "Wrong jump variable owner.");
        }
        void Function(string id, string name, string parent)
        {
            var node = nodes[id]; Kind(node, "K2Node_CallFunction"); var r = node.GetProperty("properties").GetProperty("FunctionReference");
            Require(Text(r, "memberName") == name && Text(r, "memberParent") == parent && !r.GetProperty("bSelfContext").GetBoolean() &&
                Literal(id, "self") == "", "Wrong jump function or delay semantics.");
        }
        void Edge(string id, string pinName, string source, string output)
        {
            var pin = Pin(nodes[id], pinName); var links = pin.GetProperty("links");
            Require(Text(pin, "direction") == "input" && links.GetArrayLength() == 1 && Text(links[0], "node") == source &&
                Text(links[0], "pin") == output, "Jump event execution/data order differs.");
        }
        string Literal(string id, string pinName)
        {
            var pin = Pin(nodes[id], pinName);
            Require(Text(pin, "direction") == "input" && pin.GetProperty("links").GetArrayLength() == 0, "Jump literal is connected.");
            return Text(pin, "value");
        }
        double Number(string id, string pinName)
        {
            Require(double.TryParse(Literal(id, pinName), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
                double.IsFinite(number), "Invalid jump numeric literal."); return number;
        }
    }
    private static JsonElement Pin(JsonElement node, string name) => node.GetProperty("pins").EnumerateArray().Single(p => Text(p, "name") == name);
    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString()!;
    private static void Kind(JsonElement node, string kind) => Require(Text(node, "class") == kind, "Wrong jump node kind.");
    private static void Require(bool valid, string message) { if (!valid) throw new FormatException(message); }
}
