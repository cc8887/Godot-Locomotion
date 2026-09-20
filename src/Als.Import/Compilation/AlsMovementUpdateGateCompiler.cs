using System.Text.Json;

namespace GodotAls.Import.Compilation;

public static class AlsMovementUpdateGateCompiler
{
    public static void Validate(string json)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        Require(Text(root, "source") == AlsMovementInputCurveCompiler.Source, "Wrong movement gate source.");
        const string macroPath = "/Game/AdvancedLocomotionV4/Blueprints/Libraries/ALS_MacroLibrary.ALS_MacroLibrary:ML_DoWhile(TrueFalse)";
        const string oncePath = "/Engine/EditorBlueprintResources/StandardMacros.StandardMacros:DoOnce";
        var macro = Graph(macroPath); var once = Graph(oncePath); var update = Graph(AlsMovementInputCurveCompiler.Source + ":UpdateGraph");
        Require(macro.Count == 13 && once.Count == 13, "Unsupported conditional macro closure.");
        Kind(macro, "K2Node_IfThenElse_0", "K2Node_IfThenElse");
        for (var i = 0; i < 4; i++)
        { Kind(macro, "K2Node_ExecutionSequence_" + i, "K2Node_ExecutionSequence"); Kind(macro, "K2Node_Knot_" + i, "K2Node_Knot"); }
        for (var i = 0; i < 2; i++)
        {
            Kind(macro, "K2Node_Tunnel_" + i, "K2Node_Tunnel"); Kind(once, "K2Node_Tunnel_" + i, "K2Node_Tunnel");
            var id = "K2Node_MacroInstance_" + i; Kind(macro, id, "K2Node_MacroInstance");
            Require(Text(macro[id].GetProperty("properties").GetProperty("MacroGraphReference"), "macroGraph") == oncePath,
                "Wrong conditional DoOnce macro.");
            Require(Literal(macro, id, "Start Closed") is "" or "false", "Movement DoOnce must start open.");
        }
        foreach (var edge in new[]
        {
            "K2Node_IfThenElse_0.execute=K2Node_Tunnel_0.execute", "K2Node_IfThenElse_0.Condition=K2Node_Tunnel_0.Condition",
            "K2Node_ExecutionSequence_2.execute=K2Node_IfThenElse_0.then", "K2Node_ExecutionSequence_0.execute=K2Node_IfThenElse_0.else",
            "K2Node_MacroInstance_1.execute=K2Node_ExecutionSequence_2.then_0", "K2Node_MacroInstance_0.execute=K2Node_ExecutionSequence_0.then_0",
            "K2Node_ExecutionSequence_3.execute=K2Node_MacroInstance_1.Completed", "K2Node_ExecutionSequence_1.execute=K2Node_MacroInstance_0.Completed",
            "K2Node_MacroInstance_0.Reset=K2Node_ExecutionSequence_3.then_0", "K2Node_MacroInstance_1.Reset=K2Node_ExecutionSequence_1.then_0",
            "K2Node_Tunnel_1.ChangedToTrue=K2Node_ExecutionSequence_3.then_1", "K2Node_Tunnel_1.ChangedToFalse=K2Node_ExecutionSequence_1.then_1",
            "K2Node_Knot_0.InputPin=K2Node_ExecutionSequence_2.then_1", "K2Node_Knot_1.InputPin=K2Node_ExecutionSequence_0.then_1",
            "K2Node_Knot_3.InputPin=K2Node_Knot_0.OutputPin", "K2Node_Knot_2.InputPin=K2Node_Knot_1.OutputPin",
            "K2Node_Tunnel_1.WhileTrue=K2Node_Knot_3.OutputPin", "K2Node_Tunnel_1.WhileFalse=K2Node_Knot_2.OutputPin",
        }) Edge(macro, edge);
        foreach (var id in new[] { 3, 103, 108 }) Kind(once, "K2Node_IfThenElse_" + id, "K2Node_IfThenElse");
        foreach (var id in new[] { 7, 17 }) Kind(once, "K2Node_TemporaryVariable_" + id, "K2Node_TemporaryVariable");
        Kind(once, "K2Node_ExecutionSequence_74", "K2Node_ExecutionSequence");
        foreach (var id in new[] { 4, 5, 11, 56, 57 })
        {
            Kind(once, "K2Node_AssignmentStatement_" + id, "K2Node_AssignmentStatement");
            Require(Literal(once, "K2Node_AssignmentStatement_" + id, "Value") == (id == 57 ? "" : "true"), "DoOnce assignment changed.");
        }
        foreach (var edge in new[]
        {
            "K2Node_ExecutionSequence_74.execute=K2Node_Tunnel_0.execute",
            "K2Node_IfThenElse_108.execute=K2Node_ExecutionSequence_74.then_0", "K2Node_IfThenElse_108.Condition=K2Node_TemporaryVariable_7.Variable",
            "K2Node_AssignmentStatement_11.execute=K2Node_IfThenElse_108.else", "K2Node_AssignmentStatement_11.Variable=K2Node_TemporaryVariable_7.Variable",
            "K2Node_IfThenElse_103.execute=K2Node_AssignmentStatement_11.then", "K2Node_IfThenElse_103.Condition=K2Node_Tunnel_0.Start Closed",
            "K2Node_AssignmentStatement_5.execute=K2Node_IfThenElse_103.then", "K2Node_AssignmentStatement_5.Variable=K2Node_TemporaryVariable_17.Variable",
            "K2Node_IfThenElse_3.execute=K2Node_ExecutionSequence_74.then_1", "K2Node_IfThenElse_3.Condition=K2Node_TemporaryVariable_17.Variable",
            "K2Node_AssignmentStatement_56.execute=K2Node_IfThenElse_3.else", "K2Node_AssignmentStatement_56.Variable=K2Node_TemporaryVariable_17.Variable",
            "K2Node_Tunnel_1.Completed=K2Node_AssignmentStatement_56.then", "K2Node_AssignmentStatement_57.execute=K2Node_Tunnel_0.Reset",
            "K2Node_AssignmentStatement_57.Variable=K2Node_TemporaryVariable_17.Variable", "K2Node_AssignmentStatement_4.execute=K2Node_AssignmentStatement_57.then",
            "K2Node_AssignmentStatement_4.Variable=K2Node_TemporaryVariable_7.Variable",
        }) Edge(once, edge);
        // The reset side effects execute before the moving branch. ChangedToFalse is unconnected.
        Require(Text(update["K2Node_MacroInstance_1"].GetProperty("properties").GetProperty("MacroGraphReference"), "macroGraph") == macroPath,
            "Wrong UpdateGraph movement macro.");
        foreach (var (id, name) in new[] { (21, "ElapsedDelayTime"), (2, "Rotate_L"), (3, "Rotate_R") })
        {
            var node = update["K2Node_VariableSet_" + id]; Kind(update, Text(node, "name"), "K2Node_VariableSet");
            var r = node.GetProperty("properties").GetProperty("VariableReference");
            Require(Text(r, "memberName") == name && Text(r, "memberParent") == "" && Text(r, "memberScope") == "" && r.GetProperty("bSelfContext").GetBoolean() &&
                Literal(update, Text(node, "name"), "self") == "" && Literal(update, Text(node, "name"), name) == (id == 21 ? "0.0" : "false"),
                "Moving entry reset differs.");
        }
        foreach (var edge in new[] { "K2Node_VariableSet_21.execute=K2Node_MacroInstance_1.ChangedToTrue",
            "K2Node_VariableSet_2.execute=K2Node_VariableSet_21.then", "K2Node_VariableSet_3.execute=K2Node_VariableSet_2.then",
            "K2Node_CallFunction_1.execute=K2Node_MacroInstance_1.WhileTrue", "K2Node_CallFunction_2.execute=K2Node_CallFunction_1.then" }) Edge(update, edge);
        Require(Pin(update["K2Node_VariableSet_3"], "then").GetProperty("links").GetArrayLength() == 0 &&
            Pin(update["K2Node_MacroInstance_1"], "ChangedToFalse").GetProperty("links").GetArrayLength() == 0, "Additional movement change side effects.");

        Dictionary<string, JsonElement> Graph(string path) => root.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "path") == path)
            .GetProperty("nodes").EnumerateArray().Where(n => Text(n, "class") != "EdGraphNode_Comment").ToDictionary(n => Text(n, "name"));
    }
    private static string Literal(Dictionary<string, JsonElement> nodes, string node, string pinName)
    {
        var pin = Pin(nodes[node], pinName); Require(Text(pin, "direction") == "input" && pin.GetProperty("links").GetArrayLength() == 0,
            "Connected movement gate literal."); return Text(pin, "value");
    }
    private static void Edge(Dictionary<string, JsonElement> nodes, string edge)
    {
        var halves = edge.Split('='); var input = halves[0].Split('.'); var pin = Pin(nodes[input[0]], input[1]); var links = pin.GetProperty("links");
        Require(Text(pin, "direction") == "input" && links.GetArrayLength() == 1 && Text(links[0], "node") + "." + Text(links[0], "pin") == halves[1],
            "Movement gate execution or state link differs: " + halves[0]);
    }
    private static JsonElement Pin(JsonElement node, string name) => node.GetProperty("pins").EnumerateArray().Single(p => Text(p, "name") == name);
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static void Kind(Dictionary<string, JsonElement> nodes, string name, string kind) => Require(Text(nodes[name], "class") == kind, "Wrong gate node kind.");
    private static void Require(bool valid, string message) { if (!valid) throw new FormatException(message); }
}
