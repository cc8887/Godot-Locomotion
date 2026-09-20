using System.Globalization;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public static class AlsLandPredictionCompiler
{
    public static AlsLandPredictionModel Compile(string json, AlsMovementInputCurveProfile curves)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        Require(Text(root, "source") == AlsMovementInputCurveCompiler.Source && root.GetProperty("movementInputSchemaVersion").GetInt32() == 1,
            "Wrong landing input source.");
        var graph = One(root.GetProperty("graphs").EnumerateArray().Where(g => Text(g, "path") == AlsMovementInputCurveCompiler.Source + ":CalculateLandPrediction"));
        var nodes = graph.GetProperty("nodes").EnumerateArray().ToDictionary(n => Text(n, "name"));
        var seen = new HashSet<string>();
        void Kind(string node, string kind) { Require(Text(nodes[node], "class") == kind, "Unexpected landing node type."); seen.Add(node); }
        void Function(string node, string method, string owner, bool self = false, string kind = "K2Node_CallFunction")
        {
            Kind(node, kind); var reference = nodes[node].GetProperty("properties").GetProperty("FunctionReference");
            Require(Text(reference, "memberName") == method && Text(reference, "memberParent") == owner &&
                reference.GetProperty("bSelfContext").GetBoolean() == self, "Wrong landing function owner.");
        }
        void Variable(int id, string name, string owner = "", bool self = true)
        {
            var node = "K2Node_VariableGet_" + id; Kind(node, "K2Node_VariableGet");
            var reference = nodes[node].GetProperty("properties").GetProperty("VariableReference");
            Require(Text(reference, "memberName") == name && Text(reference, "memberParent") == owner &&
                Text(reference, "memberScope") == "" && reference.GetProperty("bSelfContext").GetBoolean() == self, "Wrong landing variable owner.");
        }
        void Edge(string input, string source)
        {
            var parts = input.Split('.'); var pin = Pin(nodes[parts[0]], parts[1]);
            Require(Text(pin, "direction") == "input", "Wrong landing input direction.");
            var link = One(pin.GetProperty("links").EnumerateArray());
            Require(Text(link, "node") + "." + Text(link, "pin") == source, "Landing graph connection differs: " + input);
            Require(Text(Pin(nodes[Text(link, "node")], Text(link, "pin")), "direction") == "output", "Wrong landing output direction.");
        }
        void Constant(string node, string pin, string value)
        { var item = Pin(nodes[node], pin); Require(item.GetProperty("links").GetArrayLength() == 0 && Text(item, "value") == value, "Unsupported landing constant: " + pin); }
        float Number(string node, string pin)
        {
            var item = Pin(nodes[node], pin); Require(item.GetProperty("links").GetArrayLength() == 0, "Expected landing numeric constant.");
            var value = float.Parse(Text(item, "value"), CultureInfo.InvariantCulture);
            Require(float.IsFinite(value), "Non-finite landing parameter."); return value;
        }
        const string math = "/Script/Engine.KismetMathLibrary";
        Function("K2Node_CallFunction_10", "CapsuleTraceSingleByProfile", "/Script/Engine.KismetSystemLibrary");
        Function("K2Node_CallFunction_8", "K2_GetComponentLocation", "/Script/Engine.SceneComponent");
        Function("K2Node_CallFunction_14", "Vector_NormalUnsafe", math);
        Function("K2Node_CallFunction_16", "FClamp", math);
        Function("K2Node_CallFunction_17", "MapRangeClamped", math);
        Function("K2Node_CallFunction_18", "Multiply_VectorFloat", math);
        Function("K2Node_CallFunction_25", "GetFloatValue", "/Script/Engine.CurveFloat");
        Function("K2Node_CallFunction_4", "Less_DoubleDouble", math);
        Function("K2Node_CallFunction_12", "GetCurveValue", "", true);
        Function("K2Node_CallFunction_22", "IsWalkable", "/Script/Engine.CharacterMovementComponent");
        Function("K2Node_CallFunction_23", "BreakHitResult", "/Script/Engine.GameplayStatics");
        Function("K2Node_CallFunction_3", "Lerp", math);
        Function("K2Node_CallFunction_6", "GetDebugTraceType", "", true);
        Function("K2Node_CommutativeAssociativeBinaryOperator_0", "Add_VectorVector", math, kind: "K2Node_CommutativeAssociativeBinaryOperator");
        Function("K2Node_CommutativeAssociativeBinaryOperator_3", "BooleanAND", math, kind: "K2Node_CommutativeAssociativeBinaryOperator");
        Variable(11, "CapsuleComponent", "/Script/Engine.Character", false);
        Variable(10, "Velocity"); Variable(3, "CapsuleRadius", "/Script/Engine.CapsuleComponent", false);
        Variable(4, "CapsuleHalfHeight", "/Script/Engine.CapsuleComponent", false);
        Variable(8, "CharacterMovement", "/Script/Engine.Character", false);
        Variable(9, "LandPredictionCurve"); Variable(6, "Character"); Variable(12, "Character"); Variable(0, "FallSpeed");
        Kind("K2Node_FunctionEntry_0", "K2Node_FunctionEntry");
        foreach (var id in new[] { 0, 1 }) Kind("K2Node_IfThenElse_" + id, "K2Node_IfThenElse");
        foreach (var id in new[] { 0, 1, 2 }) Kind("K2Node_FunctionResult_" + id, "K2Node_FunctionResult");
        foreach (var edge in new[]
        {
            "K2Node_IfThenElse_1.execute=K2Node_FunctionEntry_0.then",
            "K2Node_IfThenElse_1.Condition=K2Node_CallFunction_4.ReturnValue",
            "K2Node_CallFunction_4.A=K2Node_VariableGet_0.FallSpeed",
            "K2Node_CallFunction_10.execute=K2Node_IfThenElse_1.then",
            "K2Node_FunctionResult_2.execute=K2Node_IfThenElse_1.else",
            "K2Node_VariableGet_11.self=K2Node_VariableGet_6.Character",
            "K2Node_VariableGet_3.self=K2Node_VariableGet_11.CapsuleComponent",
            "K2Node_VariableGet_4.self=K2Node_VariableGet_11.CapsuleComponent",
            "K2Node_CallFunction_8.self=K2Node_VariableGet_11.CapsuleComponent",
            "K2Node_CallFunction_10.Start=K2Node_CallFunction_8.ReturnValue",
            "K2Node_CallFunction_10.Radius=K2Node_VariableGet_3.CapsuleRadius",
            "K2Node_CallFunction_10.HalfHeight=K2Node_VariableGet_4.CapsuleHalfHeight",
            "K2Node_CallFunction_10.End=K2Node_CommutativeAssociativeBinaryOperator_0.ReturnValue",
            "K2Node_CommutativeAssociativeBinaryOperator_0.A=K2Node_CallFunction_8.ReturnValue",
            "K2Node_CommutativeAssociativeBinaryOperator_0.B=K2Node_CallFunction_18.ReturnValue",
            "K2Node_CallFunction_18.A=K2Node_CallFunction_14.ReturnValue",
            "K2Node_CallFunction_18.B=K2Node_CallFunction_17.ReturnValue",
            "K2Node_CallFunction_14.A_X=K2Node_VariableGet_10.Velocity_X",
            "K2Node_CallFunction_14.A_Y=K2Node_VariableGet_10.Velocity_Y",
            "K2Node_CallFunction_14.A_Z=K2Node_CallFunction_16.ReturnValue",
            "K2Node_CallFunction_16.Value=K2Node_VariableGet_10.Velocity_Z",
            "K2Node_CallFunction_17.Value=K2Node_VariableGet_10.Velocity_Z",
            "K2Node_IfThenElse_0.execute=K2Node_CallFunction_10.then",
            "K2Node_IfThenElse_0.Condition=K2Node_CommutativeAssociativeBinaryOperator_3.ReturnValue",
            "K2Node_FunctionResult_1.execute=K2Node_IfThenElse_0.then",
            "K2Node_FunctionResult_0.execute=K2Node_IfThenElse_0.else",
            "K2Node_CommutativeAssociativeBinaryOperator_3.A=K2Node_CallFunction_22.ReturnValue",
            "K2Node_CommutativeAssociativeBinaryOperator_3.B=K2Node_CallFunction_23.bBlockingHit",
            "K2Node_CallFunction_22.self=K2Node_VariableGet_8.CharacterMovement",
            "K2Node_VariableGet_8.self=K2Node_VariableGet_12.Character",
            "K2Node_CallFunction_22.Hit=K2Node_CallFunction_10.OutHit",
            "K2Node_CallFunction_23.Hit=K2Node_CallFunction_10.OutHit",
            "K2Node_FunctionResult_1.LandPrediction=K2Node_CallFunction_3.ReturnValue",
            "K2Node_CallFunction_3.A=K2Node_CallFunction_25.ReturnValue",
            "K2Node_CallFunction_3.Alpha=K2Node_CallFunction_12.ReturnValue",
            "K2Node_CallFunction_25.self=K2Node_VariableGet_9.LandPredictionCurve",
            "K2Node_CallFunction_25.InTime=K2Node_CallFunction_23.Time",
        }) { var parts = edge.Split('='); Edge(parts[0], parts[1]); }
        Constant("K2Node_CallFunction_10", "ProfileName", "ALS_Character");
        Constant("K2Node_CallFunction_10", "bTraceComplex", "false");
        Constant("K2Node_CallFunction_10", "bIgnoreSelf", "true");
        Constant("K2Node_CallFunction_10", "ActorsToIgnore", "");
        Constant("K2Node_CallFunction_10", "WorldContextObject", "");
        Constant("K2Node_CallFunction_12", "self", "");
        Constant("K2Node_CallFunction_14", "A", "0, 0, 0");
        Constant("K2Node_CallFunction_12", "CurveName", "Mask_LandPrediction");
        foreach (var id in new[] { 0, 6, 9, 10, 12 }) Constant("K2Node_VariableGet_" + id, "self", "");
        foreach (var id in new[] { 0, 3 })
            Require(nodes["K2Node_CommutativeAssociativeBinaryOperator_" + id].GetProperty("pins").EnumerateArray()
                .Where(p => Text(p, "direction") == "input").All(p => Text(p, "name") is "self" or "A" or "B"),
                "Additional landing operands are unsupported.");
        foreach (var node in new[] { "K2Node_FunctionResult_0", "K2Node_FunctionResult_2" }) Require(Number(node, "LandPrediction") == 0, "Nonzero no-hit prediction.");
        Require(Number("K2Node_CallFunction_3", "B") == 0, "Landing mask must blend to zero.");
        Require(nodes.Values.All(n => seen.Contains(Text(n, "name")) || Text(n, "class") == "EdGraphNode_Comment"), "Additional landing graph nodes are unsupported.");
        var settings = new AlsLandPredictionSettings(Number("K2Node_CallFunction_4", "B") * .01f,
            Number("K2Node_CallFunction_16", "Min") * .01f, Number("K2Node_CallFunction_16", "Max") * .01f,
            Number("K2Node_CallFunction_17", "InRangeA") * .01f, Number("K2Node_CallFunction_17", "InRangeB") * .01f,
            Number("K2Node_CallFunction_17", "OutRangeA") * .01f, Number("K2Node_CallFunction_17", "OutRangeB") * .01f);
        Require(settings.IsValid, "Invalid landing sweep parameters.");
        return new(settings, curves.Curves["LandPredictionCurve"]);
    }
    private static JsonElement Pin(JsonElement node, string name) => One(node.GetProperty("pins").EnumerateArray().Where(p => Text(p, "name") == name));
    private static JsonElement One(IEnumerable<JsonElement> values)
    { var array = values.ToArray(); Require(array.Length == 1, "Missing or ambiguous landing graph element."); return array[0]; }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static void Require(bool value, string message) { if (!value) throw new FormatException(message); }
}
