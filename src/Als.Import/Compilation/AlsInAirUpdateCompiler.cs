using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public static class AlsInAirUpdateCompiler
{
    public static AlsInAirAnimationInputModel Compile(string json, AlsMovementInputCurveProfile curves,
        AlsMovementInputFunctions functions, AlsLandPredictionModel landing)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        Require(Text(root, "source") == AlsMovementInputCurveCompiler.Source && root.GetProperty("movementInputSchemaVersion").GetInt32() == 1, "Wrong air update source.");
        var graph = One(root.GetProperty("graphs").EnumerateArray().Where(g => Text(g, "path") == AlsMovementInputCurveCompiler.Source + ":UpdateInAirValues"));
        var nodes = graph.GetProperty("nodes").EnumerateArray().Where(n => Text(n, "class") != "EdGraphNode_Comment").ToDictionary(n => Text(n, "name"));
        Require(nodes.Count == 15, "Unsupported additional air update nodes.");
        Kind("K2Node_FunctionEntry_0", "K2Node_FunctionEntry"); Kind("K2Node_ExecutionSequence_0", "K2Node_ExecutionSequence");
        Require(nodes["K2Node_ExecutionSequence_0"].GetProperty("pins").GetArrayLength() == 4, "Air update sequence differs.");
        Variable("K2Node_VariableGet_4", "Velocity"); Variable("K2Node_VariableGet_0", "LeanAmount");
        Variable("K2Node_VariableGet_1", "InAirLeanInterpSpeed"); Variable("K2Node_VariableGet_2", "DeltaTimeX");
        Variable("K2Node_VariableSet_5", "FallSpeed", true); Variable("K2Node_VariableSet_1", "LandPrediction", true);
        Variable("K2Node_VariableSet_3", "LeanAmount", true);
        Function("K2Node_CallFunction_1", "CalculateLandPrediction"); Function("K2Node_CallFunction_2", "CalculateInAirLeanAmount");
        Function("K2Node_CallFunction_6", "InterpLeanAmount");
        for (var i = 0; i < 3; i++) Kind("K2Node_Knot_" + i, "K2Node_Knot");
        foreach (var edge in new[]
        {
            "K2Node_ExecutionSequence_0.execute=K2Node_FunctionEntry_0.then",
            "K2Node_Knot_1.InputPin=K2Node_ExecutionSequence_0.then_0",
            "K2Node_VariableSet_5.execute=K2Node_Knot_1.OutputPin",
            "K2Node_VariableSet_5.FallSpeed=K2Node_VariableGet_4.Velocity_Z",
            "K2Node_Knot_0.InputPin=K2Node_ExecutionSequence_0.then_1",
            "K2Node_VariableSet_1.execute=K2Node_Knot_0.OutputPin",
            "K2Node_VariableSet_1.LandPrediction=K2Node_CallFunction_1.LandPrediction",
            "K2Node_Knot_2.InputPin=K2Node_ExecutionSequence_0.then_2",
            "K2Node_VariableSet_3.execute=K2Node_Knot_2.OutputPin",
            "K2Node_VariableSet_3.LeanAmount=K2Node_CallFunction_6.ReturnValue",
            "K2Node_CallFunction_6.Current=K2Node_VariableGet_0.LeanAmount",
            "K2Node_CallFunction_6.Target=K2Node_CallFunction_2.LeanAmount",
            "K2Node_CallFunction_6.InterpSpeed=K2Node_VariableGet_1.InAirLeanInterpSpeed",
            "K2Node_CallFunction_6.DeltaTime=K2Node_VariableGet_2.DeltaTimeX",
        })
        {
            var halves = edge.Split('='); var input = halves[0].Split('.'); var link = One(Pin(nodes[input[0]], input[1]).GetProperty("links").EnumerateArray());
            Require(Text(link, "node") + "." + Text(link, "pin") == halves[1], "Air update order/source differs: " + halves[0]);
        }
        return new(functions, landing, curves.InAirLeanInterpSpeed);

        void Kind(string node, string kind) => Require(Text(nodes[node], "class") == kind, "Unexpected air update node.");
        void Variable(string node, string name, bool setter = false)
        {
            Kind(node, setter ? "K2Node_VariableSet" : "K2Node_VariableGet");
            var reference = nodes[node].GetProperty("properties").GetProperty("VariableReference");
            Require(Text(reference, "memberName") == name && Text(reference, "memberParent") == "" && Text(reference, "memberScope") == "" &&
                reference.GetProperty("bSelfContext").GetBoolean(), "Wrong air update variable.");
            Require(Pin(nodes[node], "self").GetProperty("links").GetArrayLength() == 0, "Foreign air update variable instance.");
            if (setter) Require(Pin(nodes[node], "then").GetProperty("links").GetArrayLength() == 0, "Additional air setter execution.");
        }
        void Function(string node, string name)
        {
            Kind(node, "K2Node_CallFunction"); var reference = nodes[node].GetProperty("properties").GetProperty("FunctionReference");
            Require(Text(reference, "memberName") == name && Text(reference, "memberParent") == "" && reference.GetProperty("bSelfContext").GetBoolean() &&
                Pin(nodes[node], "self").GetProperty("links").GetArrayLength() == 0, "Wrong air update function instance.");
        }
    }
    private static JsonElement Pin(JsonElement node, string name) => One(node.GetProperty("pins").EnumerateArray().Where(p => Text(p, "name") == name));
    private static JsonElement One(IEnumerable<JsonElement> values)
    { var items = values.ToArray(); Require(items.Length == 1, "Missing or ambiguous air update element."); return items[0]; }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static void Require(bool value, string message) { if (!value) throw new FormatException(message); }
}
