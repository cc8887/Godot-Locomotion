using System.Globalization;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

// Validates only this implemented formula slice. Exporting the other input graphs does not
// imply their calculation, execution order or physics gathering has been implemented.
public static class AlsMovementInputFunctionCompiler
{
    private const string Lr = "LR_17_ADF99333493B27F5B49BA89100DC4C05";
    private const string Fb = "FB_15_297866804FB14F4B81FB4A976A7F57D1";
    internal static readonly string[] VelocityComponents = ["F_3_2154ABAD4BD15DAC904154B63D704219", "B_5_0A0855774CB13BB3E4B0A6847E7154F6",
        "L_8_DFEBB8584D28F158D2562CA60EB07B6D", "R_9_79E6E09B4A52B442B9FE6DB7192CFBEE"];
    public static AlsMovementInputFunctions Compile(string json, AlsMovementInputCurveProfile curves)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        Require(Text(root, "source") == AlsMovementInputCurveCompiler.Source && root.GetProperty("movementInputSchemaVersion").GetInt32() == 1,
            "Wrong runtime input source.");
        Validate("CalculateCrouchingPlayRate", "PlayRate",
            "FClamp(Divide_DoubleDouble(Divide_DoubleDouble(Divide_DoubleDouble(Speed,AnimatedCrouchSpeed),StrideBlend),K2_GetComponentScale(GetOwningComponent).ReturnValue_Z),0,2)");
        Validate("CalculateInAirLeanAmount", "LeanAmount",
            "lean(Multiply_Vector2DFloat(MakeVector2D(Divide_VectorFloat(LessLess_VectorRotator(Velocity,K2_GetActorRotation(Character)),350).ReturnValue_Y,Divide_VectorFloat(LessLess_VectorRotator(Velocity,K2_GetActorRotation(Character)),350).ReturnValue_X),GetFloatValue(LeanInAirCurve,FallSpeed)).ReturnValue_X," +
            "Multiply_Vector2DFloat(MakeVector2D(Divide_VectorFloat(LessLess_VectorRotator(Velocity,K2_GetActorRotation(Character)),350).ReturnValue_Y,Divide_VectorFloat(LessLess_VectorRotator(Velocity,K2_GetActorRotation(Character)),350).ReturnValue_X),GetFloatValue(LeanInAirCurve,FallSpeed)).ReturnValue_Y)");
        Validate("InterpLeanAmount", "ReturnValue",
            $"lean(FInterpTo(Current.{Lr},Target.{Lr},DeltaTime,InterpSpeed),FInterpTo(Current.{Fb},Target.{Fb},DeltaTime,InterpSpeed))");
        return new(curves.AnimatedCrouchingSpeed, 3.5f, curves.Curves["LeanInAirCurve"]);

        void Validate(string name, string output, string expected)
        {
            var graph = One(root.GetProperty("graphs").EnumerateArray().Where(g => Text(g, "path") == AlsMovementInputCurveCompiler.Source + ":" + name));
            var nodes = graph.GetProperty("nodes").EnumerateArray().ToDictionary(n => Text(n, "name"));
            var result = One(nodes.Values.Where(n => Text(n, "class") == "K2Node_FunctionResult"));
            var entry = One(nodes.Values.Where(n => Text(n, "class") == "K2Node_FunctionEntry"));
            Require(Link(Pin(result, "execute")) == (Text(entry, "name"), "then"), "Input function has unsupported execution flow.");
            var actual = Expr(nodes, result, output, name);
            Require(actual == expected, $"Unsupported {name} expression: {actual}.");
        }
    }

    internal static string Expr(Dictionary<string, JsonElement> nodes, JsonElement consumer, string input, string scope, int depth = 0)
    {
        Require(depth < 24, "Cyclic movement input expression.");
        var pin = Pin(consumer, input); Require(Text(pin, "direction") == "input", "Expected expression input.");
        if (pin.GetProperty("links").GetArrayLength() == 0)
        {
            var number = double.Parse(Text(pin, "value"), CultureInfo.InvariantCulture);
            Require(double.IsFinite(number), "Non-finite input constant.");
            return number.ToString("R", CultureInfo.InvariantCulture);
        }
        var (nodeName, output) = Link(pin); var node = nodes[nodeName];
        Require(Text(Pin(node, output), "direction") == "output", "Expected expression output.");
        var kind = Text(node, "class"); var properties = node.GetProperty("properties");
        string Read(string name) => Expr(nodes, node, name, scope, depth + 1);
        if (kind == "K2Node_Tunnel")
        {
            Require(scope == "GetAnimCurve_Clamped" && Text(node, "name") == "K2Node_Tunnel_0" &&
                output is "Name" or "Bias" or "ClampMin" or "ClampMax", "Unsupported macro argument.");
            return "$" + output;
        }
        if (kind == "K2Node_MacroInstance")
        {
            var reference = properties.GetProperty("MacroGraphReference");
            Require(Text(reference, "macroGraph") == AlsMovementInputCurveCompiler.Source + ":GetAnimCurve_Clamped" &&
                Text(reference, "graphBlueprint") == AlsMovementInputCurveCompiler.Source &&
                Text(reference, "graphGuid") == "D8A28103432B8B42CDE369B4904E61E5" && output == "Value" &&
                node.GetProperty("pins").GetArrayLength() == 5, "Wrong rate curve macro reference.");
            foreach (var argument in new[] { "Name", "Bias", "ClampMin", "ClampMax" })
                Require(Pin(node, argument).GetProperty("links").GetArrayLength() == 0, "Dynamic rate macro argument is unsupported.");
            Require(Text(Pin(node, "Name"), "value") == "Weight_Gait", "Wrong rate curve name.");
            var minimum = Text(Pin(node, "ClampMin"), "value");
            Require(minimum is "" or "0" or "0.0" or "0.000000", "Unexpected macro clamp minimum.");
            return "curve_clamped(Weight_Gait," + Read("Bias") + ",0," + Read("ClampMax") + ")";
        }
        if (kind == "K2Node_Knot")
        {
            Require(output == "OutputPin" && node.GetProperty("pins").GetArrayLength() == 2, "Unexpected reroute.");
            return Read("InputPin");
        }
        if (kind == "K2Node_VariableGet")
        {
            var reference = properties.GetProperty("VariableReference"); var name = Text(reference, "memberName");
            if (name == "CharacterMovement")
            {
                Require(Text(reference, "memberParent") == "/Script/Engine.Character" && Text(reference, "memberScope") == "" &&
                    !reference.GetProperty("bSelfContext").GetBoolean() && output == name, "Wrong movement component source.");
                return "CharacterMovement(" + Read("self") + ")";
            }
            Require(Text(reference, "memberParent") == "", "Unexpected input variable owner.");
            if (reference.GetProperty("bSelfContext").GetBoolean())
            {
                Require(Text(reference, "memberScope") == "" && Pin(node, "self").GetProperty("links").GetArrayLength() == 0, "Unexpected self variable owner.");
                if (name == "VelocityBlend" && VelocityComponents.Any(c => output == name + "_" + c))
                    return name + "." + output[(name.Length + 1)..];
                Require(output == name, "Unexpected self variable output.");
                return name;
            }
            Require(Text(reference, "memberScope") == scope, "Unexpected local input variable.");
            if (scope == "CalculateVelocityBlend")
            {
                Require(name is "LocRelativeVelocityDir" or "Sum" or "RelativeDirection", "Unexpected velocity local.");
                if (output == name) return name;
                Require(name != "Sum" && (output == name + "_X" || output == name + "_Y" || output == name + "_Z"), "Wrong local vector component.");
                return name + "." + output[(name.Length + 1)..];
            }
            Require(scope is "InterpLeanAmount" or "InterpVelocityBlend", "Unexpected local input scope.");
            if (name is "Current" or "Target")
            {
                Require(scope == "InterpLeanAmount" ? output == name + "_" + Lr || output == name + "_" + Fb :
                    VelocityComponents.Any(c => output == name + "_" + c), "Unexpected interpolation component.");
                return name + "." + output[(name.Length + 1)..];
            }
            Require(name is "DeltaTime" or "InterpSpeed" && output == name, "Unexpected interpolation argument.");
            return name;
        }
        if (kind == "K2Node_MakeStruct")
        {
            if (output == "VelocityBlend")
            {
                Require(node.GetProperty("pins").GetArrayLength() == 5, "Unexpected VelocityBlend structure.");
                return "velocity(" + string.Join(",", VelocityComponents.Select(Read)) + ")";
            }
            Require(output == "LeanAmount" && node.GetProperty("pins").GetArrayLength() == 3, "Unexpected Lean structure.");
            return $"lean({Read(Lr)},{Read(Fb)})";
        }
        Require(kind is "K2Node_CallFunction" or "K2Node_CommutativeAssociativeBinaryOperator", "Unsupported movement input node.");
        var function = properties.GetProperty("FunctionReference"); var method = Text(function, "memberName");
        var parent = Text(function, "memberParent"); var self = function.GetProperty("bSelfContext").GetBoolean();
        if (method == "GetCurveValue")
        {
            Require(parent == "" && self && output == "ReturnValue" && node.GetProperty("pins").GetArrayLength() == 3 &&
                Pin(node, "self").GetProperty("links").GetArrayLength() == 0, "Wrong animation curve owner.");
            var namePin = Pin(node, "CurveName");
            var name = namePin.GetProperty("links").GetArrayLength() == 0 ? Text(namePin, "value") : Read("CurveName");
            Require(name is "$Name" or "BasePose_CLF", "Unsupported animation curve input.");
            return "GetCurveValue(" + name + ")";
        }
        if (method == "GetOwningComponent")
        {
            Require(parent == "" && self && output == "ReturnValue" && Pin(node, "self").GetProperty("links").GetArrayLength() == 0,
                "Wrong owning component source.");
            return method;
        }
        Require(!self, "Unexpected self-context function.");
        string[] args; string owner;
        switch (method)
        {
            case "GetMaxAcceleration": case "GetMaxBrakingDeceleration": owner = "/Script/Engine.CharacterMovementComponent"; args = ["self"]; break;
            case "K2_GetComponentScale": owner = "/Script/Engine.SceneComponent"; args = ["self"]; break;
            case "K2_GetActorRotation": owner = "/Script/Engine.Actor"; args = ["self"]; break;
            case "GetFloatValue": owner = "/Script/Engine.CurveFloat"; args = ["self", "InTime"]; break;
            default:
                owner = "/Script/Engine.KismetMathLibrary";
                args = method switch
                {
                    "FClamp" => ["Value", "Min", "Max"], "FInterpTo" => ["Current", "Target", "DeltaTime", "InterpSpeed"],
                    "Lerp" => ["A", "B", "Alpha"],
                    "MakeVector2D" => ["X", "Y"],
                    "Abs" => ["A"], "Normal" => ["A", "Tolerance"], "Vector_ClampSizeMax" => ["A", "Max"],
                    "Dot_VectorVector" or "Greater_DoubleDouble" => ["A", "B"],
                    "Add_DoubleDouble" => node.GetProperty("pins").EnumerateArray().Any(p => Text(p, "name") == "C") ? ["A", "B", "C"] : ["A", "B"],
                    "Divide_DoubleDouble" or "Divide_VectorFloat" or "LessLess_VectorRotator" or "Multiply_Vector2DFloat" => ["A", "B"],
                    _ => throw new FormatException("Unsupported movement input function: " + method),
                };
                Require(Pin(node, "self").GetProperty("links").GetArrayLength() == 0, "Static math source is linked.");
                break;
        }
        Require(kind != "K2Node_CommutativeAssociativeBinaryOperator" || method == "Add_DoubleDouble", "Unsupported associative operator.");
        Require(parent == owner, "Unexpected movement function owner.");
        Require(node.GetProperty("pins").EnumerateArray().Where(p => Text(p, "direction") == "input")
            .All(p => Text(p, "name") == "self" || args.Contains(Text(p, "name"))), "Extra movement function input.");
        var expression = method + "(" + string.Join(",", args.Select(Read)) + ")";
        if (output == "ReturnValue") return expression;
        Require(method is "K2_GetComponentScale" or "Divide_VectorFloat" or "Multiply_Vector2DFloat" &&
            output is "ReturnValue_X" or "ReturnValue_Y" or "ReturnValue_Z", "Unsupported split function output.");
        return expression + "." + output;
    }
    private static JsonElement Pin(JsonElement node, string name) => One(node.GetProperty("pins").EnumerateArray().Where(p => Text(p, "name") == name));
    private static (string, string) Link(JsonElement pin)
    { var link = One(pin.GetProperty("links").EnumerateArray()); return (Text(link, "node"), Text(link, "pin")); }
    private static JsonElement One(IEnumerable<JsonElement> values)
    { var array = values.ToArray(); Require(array.Length == 1, "Missing or ambiguous movement input node/pin."); return array[0]; }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static void Require(bool value, string message) { if (!value) throw new FormatException(message); }
}
