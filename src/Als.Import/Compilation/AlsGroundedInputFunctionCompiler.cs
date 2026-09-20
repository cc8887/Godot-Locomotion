using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

// Validates five ground input functions, including locals and branch execution. The global
// update gate/order, stride/rate macros and final curve feedback are separate dependencies.
public static class AlsGroundedInputFunctionCompiler
{
    public static AlsGroundedInputFunctions Compile(string json, AlsMovementInputCurveProfile curves)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        Require(Text(root, "source") == AlsMovementInputCurveCompiler.Source && root.GetProperty("movementInputSchemaVersion").GetInt32() == 1, "Wrong grounded input source.");
        var velocity = Graph("CalculateVelocityBlend", 28);
        velocity.Local("K2Node_VariableSet_2", "LocRelativeVelocityDir", "K2Node_FunctionEntry_0.then",
            "LessLess_VectorRotator(Normal(Velocity,0.1),K2_GetActorRotation(Character))");
        velocity.Local("K2Node_VariableSet_1", "Sum", "K2Node_VariableSet_2.then",
            "Add_DoubleDouble(Abs(LocRelativeVelocityDir.X),Abs(LocRelativeVelocityDir.Y),Abs(LocRelativeVelocityDir.Z))");
        velocity.Local("K2Node_VariableSet_8", "RelativeDirection", "K2Node_VariableSet_1.then", "Divide_VectorFloat(LocRelativeVelocityDir,Sum)");
        velocity.Result("K2Node_FunctionResult_1", "ReturnValue", "K2Node_VariableSet_8.then",
            "velocity(FClamp(RelativeDirection.X,0,1),Abs(FClamp(RelativeDirection.X,-1,0)),Abs(FClamp(RelativeDirection.Y,-1,0)),FClamp(RelativeDirection.Y,0,1))");

        var interpolation = Graph("InterpVelocityBlend", 11);
        interpolation.Result("K2Node_FunctionResult_0", "ReturnValue", "K2Node_FunctionEntry_0.then",
            "velocity(" + string.Join(",", AlsMovementInputFunctionCompiler.VelocityComponents.Select(c => $"FInterpTo(Current.{c},Target.{c},DeltaTime,InterpSpeed)")) + ")");

        var acceleration = Graph("CalculateRelativeAccelerationAmount", 28);
        acceleration.Kind("K2Node_IfThenElse_0", "K2Node_IfThenElse");
        acceleration.Flow("K2Node_IfThenElse_0", "execute", "K2Node_FunctionEntry_0.then");
        acceleration.Expression("K2Node_IfThenElse_0", "Condition", "Greater_DoubleDouble(Dot_VectorVector(Acceleration,Velocity),0)");
        acceleration.Kind("K2Node_Knot_1", "K2Node_Knot"); acceleration.Kind("K2Node_Knot_0", "K2Node_Knot");
        acceleration.Flow("K2Node_Knot_1", "InputPin", "K2Node_IfThenElse_0.then");
        acceleration.Flow("K2Node_Knot_0", "InputPin", "K2Node_IfThenElse_0.else");
        acceleration.Result("K2Node_FunctionResult_2", "ReturnValue", "K2Node_Knot_1.OutputPin", Acceleration("GetMaxAcceleration"));
        acceleration.Result("K2Node_FunctionResult_1", "ReturnValue", "K2Node_Knot_0.OutputPin", Acceleration("GetMaxBrakingDeceleration"));

        var diagonal = Graph("CalculateDiagonalScaleAmount", 7);
        diagonal.Result("K2Node_FunctionResult_0", "ReturnValue", "K2Node_FunctionEntry_0.then",
            $"GetFloatValue(DiagonalScaleAmountCurve,Abs(Add_DoubleDouble(VelocityBlend.{AlsMovementInputFunctionCompiler.VelocityComponents[0]},VelocityBlend.{AlsMovementInputFunctionCompiler.VelocityComponents[1]})))");

        var gait = Graph("CalculateWalkRunBlend", 5);
        gait.Kind("K2Node_SwitchEnum_0", "K2Node_SwitchEnum");
        gait.Flow("K2Node_SwitchEnum_0", "execute", "K2Node_FunctionEntry_0.then");
        gait.Expression("K2Node_SwitchEnum_0", "Selection", "Gait");
        gait.Result("K2Node_FunctionResult_2", "WalkRunBlend", "K2Node_SwitchEnum_0.NewEnumerator0", "0");
        gait.Result("K2Node_FunctionResult_3", "WalkRunBlend", "K2Node_SwitchEnum_0.NewEnumerator1|K2Node_SwitchEnum_0.NewEnumerator2", "1");
        Require(gait.Nodes["K2Node_SwitchEnum_0"].GetProperty("pins").GetArrayLength() == 6 &&
            Pin(gait.Nodes["K2Node_SwitchEnum_0"], "NotEqual_ByteByte").GetProperty("links").GetArrayLength() == 0, "Unexpected gait switch shape.");
        return new(.1f, curves.Curves["DiagonalScaleAmountCurve"]);

        CheckedGraph Graph(string name, int count) => new(One(root.GetProperty("graphs").EnumerateArray().Where(g =>
            Text(g, "path") == AlsMovementInputCurveCompiler.Source + ":" + name)), name, count);
        static string Acceleration(string limit)
        {
            var maximum = limit + "(CharacterMovement(Character))";
            return $"LessLess_VectorRotator(Divide_VectorFloat(Vector_ClampSizeMax(Acceleration,{maximum}),{maximum}),K2_GetActorRotation(Character))";
        }
    }

    private sealed class CheckedGraph
    {
        public Dictionary<string, JsonElement> Nodes { get; }
        private readonly string _name;
        public CheckedGraph(JsonElement graph, string name, int count)
        {
            _name = name;
            Nodes = graph.GetProperty("nodes").EnumerateArray().Where(n => Text(n, "class") != "EdGraphNode_Comment").ToDictionary(n => Text(n, "name"));
            Require(Nodes.Count == count, "Unexpected ground function node closure: " + name);
            Kind("K2Node_FunctionEntry_0", "K2Node_FunctionEntry");
        }
        public void Kind(string node, string kind) => Require(Text(Nodes[node], "class") == kind, "Wrong ground function node kind.");
        public void Flow(string node, string pin, string expected)
        {
            var value = Pin(Nodes[node], pin);
            Require(Text(value, "direction") == "input", "Wrong flow pin direction.");
            var links = value.GetProperty("links").EnumerateArray().Select(l => Text(l, "node") + "." + Text(l, "pin")).Order().ToArray();
            Require(links.SequenceEqual(expected.Split('|').Order()), "Ground function execution differs: " + _name + "." + node);
        }
        public void Expression(string node, string pin, string expected)
        {
            var actual = AlsMovementInputFunctionCompiler.Expr(Nodes, Nodes[node], pin, _name);
            Require(actual == expected, "Ground expression differs: " + _name + "." + node + " = " + actual);
        }
        public void Local(string node, string variable, string incoming, string expected)
        {
            Kind(node, "K2Node_VariableSet");
            var reference = Nodes[node].GetProperty("properties").GetProperty("VariableReference");
            Require(Text(reference, "memberParent") == "" && Text(reference, "memberScope") == _name &&
                Text(reference, "memberName") == variable && !reference.GetProperty("bSelfContext").GetBoolean(), "Wrong ground local setter.");
            Flow(node, "execute", incoming); Expression(node, variable, expected);
        }
        public void Result(string node, string pin, string incoming, string expected)
        {
            Kind(node, "K2Node_FunctionResult"); Flow(node, "execute", incoming); Expression(node, pin, expected);
        }
    }
    private static JsonElement Pin(JsonElement node, string name) => One(node.GetProperty("pins").EnumerateArray().Where(p => Text(p, "name") == name));
    private static JsonElement One(IEnumerable<JsonElement> values)
    { var items = values.ToArray(); Require(items.Length == 1, "Missing or ambiguous ground function element."); return items[0]; }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static void Require(bool value, string message) { if (!value) throw new FormatException(message); }
}
