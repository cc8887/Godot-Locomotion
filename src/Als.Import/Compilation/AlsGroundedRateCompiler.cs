using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public static class AlsGroundedRateCompiler
{
    public static AlsGroundedRateFunctions Compile(string json, AlsMovementInputCurveProfile curves, AlsMovementInputFunctions functions)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        Require(Text(root, "source") == AlsMovementInputCurveCompiler.Source && root.GetProperty("groundedInputSchemaVersion").GetInt32() == 1,
            "Missing grounded macro dependencies.");
        Validate("GetAnimCurve_Clamped", "K2Node_Tunnel_1", "Value", 5,
            "FClamp(Add_DoubleDouble(GetCurveValue($Name),$Bias),$ClampMin,$ClampMax)", true);
        Validate("CalculateStrideBlend", "K2Node_FunctionResult_0", "ReturnValue", 14,
            "Lerp(Lerp(GetFloatValue(StrideBlend_N_Walk,Speed),GetFloatValue(StrideBlend_N_Run,Speed),curve_clamped(Weight_Gait,-1,0,1)),GetFloatValue(StrideBlend_C_Walk,Speed),GetCurveValue(BasePose_CLF))");
        Validate("CalculateStandingPlayRate", "K2Node_FunctionResult_0", "PlayRate", 20,
            "FClamp(Divide_DoubleDouble(Divide_DoubleDouble(Lerp(Lerp(Divide_DoubleDouble(Speed,AnimatedWalkSpeed),Divide_DoubleDouble(Speed,AnimatedRunSpeed),curve_clamped(Weight_Gait,-1,0,1)),Divide_DoubleDouble(Speed,AnimatedSprintSpeed),curve_clamped(Weight_Gait,-2,0,1)),StrideBlend),K2_GetComponentScale(GetOwningComponent).ReturnValue_Z),0,3)");
        var result = new AlsGroundedRateFunctions(curves.AnimatedStandingSpeeds, curves.Curves["StrideBlend_N_Walk"],
            curves.Curves["StrideBlend_N_Run"], curves.Curves["StrideBlend_C_Walk"], functions);
        var samples = root.GetProperty("groundedRateNativeCases");
        Require(samples.GetArrayLength() == 720, "Incomplete native rate cases.");
        var coverage = new HashSet<(float Speed, float Gait, float Crouch, float Scale)>();
        foreach (var sample in samples.EnumerateArray())
        {
            var speed = sample.GetProperty("speedCm").GetSingle(); var gait = sample.GetProperty("weightGait").GetSingle();
            var crouch = sample.GetProperty("basePoseCrouch").GetSingle(); var scale = sample.GetProperty("meshScaleZ").GetSingle();
            Require(speed is 0 or 75 or 150 or 350 or 600 or 900 && gait is -1 or 0 or 1 or 1.5f or 2 or 2.5f or 3 or 4 &&
                crouch is -.5f or 0 or .5f or 1 or 1.5f && scale is .5f or 1 or 2 && coverage.Add((speed, gait, crouch, scale)), "Invalid native rate coverage.");
            var actual = result.Evaluate(speed * .01f, gait, crouch, scale);
            Check(actual.Stride, "stride"); Check(actual.StandingPlayRate, "standingPlayRate"); Check(actual.CrouchingPlayRate, "crouchingPlayRate");
            void Check(float value, string name)
            {
                var expected = sample.GetProperty(name).GetDouble();
                Require(double.IsFinite(expected) && System.Math.Abs(value - expected) <= 2e-6, "Native ground rate differs: " + name);
            }
        }
        return result;

        void Validate(string name, string outputNode, string outputPin, int count, string expected, bool macro = false)
        {
            var graph = root.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "path") == AlsMovementInputCurveCompiler.Source + ":" + name);
            var nodes = graph.GetProperty("nodes").EnumerateArray().Where(n => Text(n, "class") != "EdGraphNode_Comment").ToDictionary(n => Text(n, "name"));
            Require(nodes.Count == count && Text(nodes[outputNode], "class") == (macro ? "K2Node_Tunnel" : "K2Node_FunctionResult"), "Wrong rate graph closure: " + name);
            if (!macro)
            {
                var link = Pin(nodes[outputNode], "execute").GetProperty("links").EnumerateArray().Single();
                Require(Text(nodes["K2Node_FunctionEntry_0"], "class") == "K2Node_FunctionEntry" &&
                    Text(link, "node") == "K2Node_FunctionEntry_0" && Text(link, "pin") == "then", "Unexpected rate execution flow.");
            }
            else Require(nodes["K2Node_Tunnel_0"].GetProperty("pins").GetArrayLength() == 4 && nodes[outputNode].GetProperty("pins").GetArrayLength() == 1,
                "Unexpected curve macro interface.");
            var actual = AlsMovementInputFunctionCompiler.Expr(nodes, nodes[outputNode], outputPin, name);
            Require(actual == expected, "Rate expression differs: " + name + " = " + actual);
        }
    }
    private static JsonElement Pin(JsonElement node, string name) => node.GetProperty("pins").EnumerateArray().Single(p => Text(p, "name") == name);
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static void Require(bool value, string message) { if (!value) throw new FormatException(message); }
}
