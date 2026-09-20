using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public static class AlsGroundedUpdateCompiler
{
    public static AlsGroundedAnimationInputModel Compile(string json, AlsMovementInputCurveProfile curves,
        AlsGroundedInputFunctions functions, AlsGroundedRateFunctions rates, AlsMovementInputStateDefaults defaults)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        Require(Text(root, "source") == AlsMovementInputCurveCompiler.Source && root.GetProperty("groundedInputSchemaVersion").GetInt32() == 1,
            "Wrong grounded update source.");
        var update = Graph(AlsMovementInputCurveCompiler.Source + ":UpdateMovementValues");
        Require(update.Count == 31, "Unsupported ground update node closure.");
        Kind(update, "K2Node_FunctionEntry_0", "K2Node_FunctionEntry"); Kind(update, "K2Node_ExecutionSequence_0", "K2Node_ExecutionSequence");
        Require(update["K2Node_ExecutionSequence_0"].GetProperty("pins").GetArrayLength() == 5, "Wrong ground update sequence.");
        foreach (var (id, variable) in new[] { (25, "LeanAmount"), (9, "VelocityBlend"), (0, "DeltaTimeX"), (1, "VelocityBlendInterpSpeed"),
            (13, "DeltaTimeX"), (2, "GroundedLeanInterpSpeed"), (3, "RelativeAccelerationAmount") })
            Variable(update, "K2Node_VariableGet_" + id, variable, false);
        foreach (var (id, variable) in new[] { (6, "StandingPlayRate"), (4, "DiagonalScaleAmount"), (19, "LeanAmount"), (0, "VelocityBlend"),
            (2, "WalkRunBlend"), (3, "StrideBlend"), (1, "CrouchingPlayRate"), (5, "RelativeAccelerationAmount") })
            Variable(update, "K2Node_VariableSet_" + id, variable, true);
        foreach (var (id, function) in new[] { (15, "CalculateStandingPlayRate"), (23, "CalculateVelocityBlend"), (8, "CalculateWalkRunBlend"),
            (37, "CalculateStrideBlend"), (5, "CalculateCrouchingPlayRate"), (6, "CalculateDiagonalScaleAmount"), (0, "CalculateRelativeAccelerationAmount"),
            (4, "InterpVelocityBlend"), (7, "InterpLeanAmount") }) Function(update, "K2Node_CallFunction_" + id, function);
        foreach (var knot in new[] { 0, 1, 2, 8 }) Kind(update, "K2Node_Knot_" + knot, "K2Node_Knot");
        Kind(update, "K2Node_MakeStruct_0", "K2Node_MakeStruct");
        Require(update["K2Node_MakeStruct_0"].GetProperty("pins").GetArrayLength() == 3, "Wrong ground Lean structure.");
        foreach (var edge in new[]
        {
            "K2Node_ExecutionSequence_0.execute=K2Node_FunctionEntry_0.then",
            "K2Node_Knot_8.InputPin=K2Node_ExecutionSequence_0.then_0", "K2Node_VariableSet_0.execute=K2Node_Knot_8.OutputPin",
            "K2Node_VariableSet_0.VelocityBlend=K2Node_CallFunction_4.ReturnValue",
            "K2Node_CallFunction_4.Current=K2Node_VariableGet_9.VelocityBlend", "K2Node_CallFunction_4.Target=K2Node_CallFunction_23.ReturnValue",
            "K2Node_CallFunction_4.InterpSpeed=K2Node_VariableGet_1.VelocityBlendInterpSpeed", "K2Node_CallFunction_4.DeltaTime=K2Node_VariableGet_0.DeltaTimeX",
            "K2Node_Knot_2.InputPin=K2Node_ExecutionSequence_0.then_1", "K2Node_VariableSet_4.execute=K2Node_Knot_2.OutputPin",
            "K2Node_VariableSet_4.DiagonalScaleAmount=K2Node_CallFunction_6.ReturnValue",
            "K2Node_Knot_0.InputPin=K2Node_ExecutionSequence_0.then_2", "K2Node_VariableSet_5.execute=K2Node_Knot_0.OutputPin",
            "K2Node_VariableSet_5.RelativeAccelerationAmount=K2Node_CallFunction_0.ReturnValue", "K2Node_VariableSet_19.execute=K2Node_VariableSet_5.then",
            "K2Node_VariableSet_19.LeanAmount=K2Node_CallFunction_7.ReturnValue", "K2Node_CallFunction_7.Current=K2Node_VariableGet_25.LeanAmount",
            "K2Node_CallFunction_7.Target=K2Node_MakeStruct_0.LeanAmount", "K2Node_CallFunction_7.InterpSpeed=K2Node_VariableGet_2.GroundedLeanInterpSpeed",
            "K2Node_CallFunction_7.DeltaTime=K2Node_VariableGet_13.DeltaTimeX",
            "K2Node_MakeStruct_0.LR_17_ADF99333493B27F5B49BA89100DC4C05=K2Node_VariableGet_3.RelativeAccelerationAmount_Y",
            "K2Node_MakeStruct_0.FB_15_297866804FB14F4B81FB4A976A7F57D1=K2Node_VariableGet_3.RelativeAccelerationAmount_X",
            "K2Node_Knot_1.InputPin=K2Node_ExecutionSequence_0.then_3", "K2Node_VariableSet_2.execute=K2Node_Knot_1.OutputPin",
            "K2Node_VariableSet_2.WalkRunBlend=K2Node_CallFunction_8.WalkRunBlend", "K2Node_VariableSet_3.execute=K2Node_VariableSet_2.then",
            "K2Node_VariableSet_3.StrideBlend=K2Node_CallFunction_37.ReturnValue", "K2Node_VariableSet_6.execute=K2Node_VariableSet_3.then",
            "K2Node_VariableSet_6.StandingPlayRate=K2Node_CallFunction_15.PlayRate", "K2Node_VariableSet_1.execute=K2Node_VariableSet_6.then",
            "K2Node_VariableSet_1.CrouchingPlayRate=K2Node_CallFunction_5.PlayRate",
        }) Edge(update, edge);
        foreach (var id in new[] { 0, 4, 19, 1 }) Require(Pin(update["K2Node_VariableSet_" + id], "then").GetProperty("links").GetArrayLength() == 0,
            "Additional ground update setter execution.");

        var graph = Graph(AlsMovementInputCurveCompiler.Source + ":UpdateGraph");
        Kind(graph, "K2Node_SwitchEnum_0", "K2Node_SwitchEnum"); Variable(graph, "K2Node_VariableGet_3", "MovementState", false);
        Require(Text(Pin(graph["K2Node_SwitchEnum_0"], "Selection"), "enumType") ==
            "/Game/AdvancedLocomotionV4/Data/Enums/ALS_MovementState.ALS_MovementState", "Wrong movement state enum.");
        Variable(graph, "K2Node_VariableSet_1", "ShouldMove", true); Function(graph, "K2Node_CallFunction_4", "ShouldMoveCheck");
        Function(graph, "K2Node_CallFunction_1", "UpdateMovementValues");
        const string macroPath = "/Game/AdvancedLocomotionV4/Blueprints/Libraries/ALS_MacroLibrary.ALS_MacroLibrary:ML_DoWhile(TrueFalse)";
        Kind(graph, "K2Node_MacroInstance_1", "K2Node_MacroInstance");
        Require(Text(graph["K2Node_MacroInstance_1"].GetProperty("properties").GetProperty("MacroGraphReference"), "macroGraph") == macroPath,
            "Wrong ShouldMove gate macro.");
        foreach (var edge in new[] { "K2Node_SwitchEnum_0.Selection=K2Node_VariableGet_3.MovementState",
            "K2Node_Knot_1.InputPin=K2Node_SwitchEnum_0.NewEnumerator1", "K2Node_VariableSet_1.execute=K2Node_Knot_1.OutputPin",
            "K2Node_VariableSet_1.ShouldMove=K2Node_CallFunction_4.Return Value", "K2Node_MacroInstance_1.execute=K2Node_VariableSet_1.then",
            "K2Node_MacroInstance_1.Condition=K2Node_VariableSet_1.Output_Get", "K2Node_CallFunction_1.execute=K2Node_MacroInstance_1.WhileTrue" }) Edge(graph, edge);
        // WhileTrue is the unconditional second sequence output of the true branch. DoOnce
        // only gates ChangedToTrue/False; its private state does not gate this input update.
        var macro = Graph(macroPath); Kind(macro, "K2Node_IfThenElse_0", "K2Node_IfThenElse");
        Kind(macro, "K2Node_ExecutionSequence_2", "K2Node_ExecutionSequence");
        Require(macro["K2Node_ExecutionSequence_2"].GetProperty("pins").GetArrayLength() == 3, "Wrong WhileTrue sequence.");
        foreach (var edge in new[] { "K2Node_IfThenElse_0.execute=K2Node_Tunnel_0.execute", "K2Node_IfThenElse_0.Condition=K2Node_Tunnel_0.Condition",
            "K2Node_ExecutionSequence_2.execute=K2Node_IfThenElse_0.then", "K2Node_Knot_0.InputPin=K2Node_ExecutionSequence_2.then_1",
            "K2Node_Knot_3.InputPin=K2Node_Knot_0.OutputPin", "K2Node_Tunnel_1.WhileTrue=K2Node_Knot_3.OutputPin" }) Edge(macro, edge);

        var initial = new AlsGroundedAnimationInput(default, defaults.VelocityBlend, defaults.RelativeAcceleration, defaults.DiagonalScale,
            curves.Defaults["WalkRunBlend"], curves.Defaults["StrideBlend"], defaults.StandingPlayRate, curves.Defaults["CrouchingPlayRate"], defaults.ShouldMove);
        return new(functions, rates, AlsLocomotionInputCompiler.Compile(json).Movement, curves.VelocityInterpSpeed, curves.GroundedLeanInterpSpeed, initial);

        Dictionary<string, JsonElement> Graph(string path) => root.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "path") == path)
            .GetProperty("nodes").EnumerateArray().Where(n => Text(n, "class") != "EdGraphNode_Comment").ToDictionary(n => Text(n, "name"));
    }
    private static void Variable(Dictionary<string, JsonElement> nodes, string node, string name, bool setter)
    {
        Kind(nodes, node, setter ? "K2Node_VariableSet" : "K2Node_VariableGet");
        var reference = nodes[node].GetProperty("properties").GetProperty("VariableReference");
        Require(Text(reference, "memberName") == name && Text(reference, "memberParent") == "" && Text(reference, "memberScope") == "" &&
            reference.GetProperty("bSelfContext").GetBoolean() && Pin(nodes[node], "self").GetProperty("links").GetArrayLength() == 0, "Wrong ground update variable.");
    }
    private static void Function(Dictionary<string, JsonElement> nodes, string node, string name)
    {
        Kind(nodes, node, "K2Node_CallFunction"); var reference = nodes[node].GetProperty("properties").GetProperty("FunctionReference");
        Require(Text(reference, "memberName") == name && Text(reference, "memberParent") == "" && reference.GetProperty("bSelfContext").GetBoolean() &&
            Pin(nodes[node], "self").GetProperty("links").GetArrayLength() == 0, "Wrong ground update function.");
    }
    private static void Kind(Dictionary<string, JsonElement> nodes, string name, string kind) => Require(Text(nodes[name], "class") == kind, "Wrong ground update node kind.");
    private static void Edge(Dictionary<string, JsonElement> nodes, string edge)
    {
        var halves = edge.Split('='); var names = halves[0].Split('.'); var input = Pin(nodes[names[0]], names[1]);
        var links = input.GetProperty("links"); Require(Text(input, "direction") == "input" && links.GetArrayLength() == 1, "Ambiguous ground input edge.");
        var link = links[0]; Require(Text(link, "node") + "." + Text(link, "pin") == halves[1], "Ground update source/order differs: " + halves[0]);
    }
    private static JsonElement Pin(JsonElement node, string name) => node.GetProperty("pins").EnumerateArray().Single(p => Text(p, "name") == name);
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static void Require(bool value, string message) { if (!value) throw new FormatException(message); }
}
