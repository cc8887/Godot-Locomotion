using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public static class AlsPelvisIkInputCompiler
{
    public static AlsPelvisIkInputModel Compile(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        const string source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && root.GetProperty("source").GetString() == source,
            "Foreign Foot IK input source/schema.");
        var text = root.GetProperty("graphs").EnumerateArray().Single(g =>
            g.GetProperty("path").GetString() == source + ":SetPelvisIKOffset").GetProperty("nativeText").GetString()!;
        var graph = new Graph(text, true);
        var entry = graph.One("K2Node_FunctionEntry", "SetPelvisIKOffset");
        var alpha = graph.One("K2Node_VariableSet", "PelvisAlpha"); graph.Self(alpha);
        graph.Link(alpha, "execute", entry, "then");
        var divide = graph.Named("K2Node_CallFunction_10"); Math(divide, "Divide_DoubleDouble");
        graph.Link(alpha, "PelvisAlpha", divide, "ReturnValue"); Literal(divide, "B", "2.000000");
        var add = graph.Named("K2Node_CommutativeAssociativeBinaryOperator_0");
        Require(add.Kind == "K2Node_CommutativeAssociativeBinaryOperator" && add.Member == "Add_DoubleDouble" &&
            add.Body.Contains("/Script/Engine.KismetMathLibrary'", StringComparison.Ordinal), "Changed pelvis alpha addition.");
        graph.Link(divide, "A", add, "ReturnValue");
        var left = Curve("K2Node_CallFunction_1", "Enable_FootIK_L");
        var right = Curve("K2Node_CallFunction_6", "Enable_FootIK_R");
        graph.Link(add, "A", left, "ReturnValue"); graph.Link(add, "B", right, "ReturnValue");
        var enabled = graph.Named("K2Node_IfThenElse_0"); Branch(enabled);
        graph.Link(enabled, "execute", alpha, "then");
        var positive = graph.Named("K2Node_CallFunction_12"); Math(positive, "Greater_DoubleDouble");
        graph.Link(enabled, "Condition", positive, "ReturnValue");
        graph.Link(positive, "A", alpha, "Output_Get"); Literal(positive, "B", "0.0");
        var zero = graph.Named("K2Node_VariableSet_3"); SelfVariable(zero, "K2Node_VariableSet", "PelvisOffset");
        graph.Link(zero, "execute", enabled, "else"); Literal(zero, "PelvisOffset", "0, 0, 0");
        var sequence = graph.Named("K2Node_ExecutionSequence_0");
        Require(sequence.Kind == "K2Node_ExecutionSequence", "Changed pelvis execution sequence.");
        graph.Link(sequence, "execute", enabled, "then");
        var select = graph.Named("K2Node_IfThenElse_1"); Branch(select);
        LinkReroutes(select, "execute", sequence, "then_0");
        var less = graph.Named("K2Node_CallFunction_3"); Math(less, "Less_DoubleDouble");
        graph.Link(select, "Condition", less, "ReturnValue");
        LocalRead(less, "A", "K2Node_VariableGet_2", "FootOffset_L_Target", "FootOffset_L_Target_Z");
        LocalRead(less, "B", "K2Node_VariableGet_3", "FootOffset_R_Target", "FootOffset_R_Target_Z");
        SetTarget("K2Node_VariableSet_2", "then", "K2Node_VariableGet_0", "FootOffset_L_Target");
        SetTarget("K2Node_VariableSet_0", "else", "K2Node_VariableGet_1", "FootOffset_R_Target");
        var upward = graph.Named("K2Node_IfThenElse_2"); Branch(upward);
        LinkReroutes(upward, "execute", sequence, "then_1");
        var greater = graph.Named("K2Node_CallFunction_11"); Math(greater, "Greater_DoubleDouble");
        graph.Link(upward, "Condition", greater, "ReturnValue");
        LocalRead(greater, "A", "K2Node_VariableGet_5", "PelvisTarget", "PelvisTarget_Z");
        SelfRead(greater, "B", "K2Node_VariableGet_4", "PelvisOffset", "PelvisOffset_Z");
        var up = Interp("K2Node_VariableSet_4", "K2Node_CallFunction_14", "then", "K2Node_VariableGet_6", "K2Node_VariableGet_7", "K2Node_VariableGet_11");
        var down = Interp("K2Node_VariableSet_6", "K2Node_CallFunction_13", "else", "K2Node_VariableGet_8", "K2Node_VariableGet_9", "K2Node_VariableGet_10");
        var defaults = root.GetProperty("defaultsText").GetString()!;
        var offset = Regex.Match(Default("PelvisOffset"), @"^\(X=([^,]+),Y=([^,]+),Z=([^\)]+)\)$");
        Require(offset.Success, "Invalid original pelvis offset default.");
        return new(graph.Literal(left, "CurveName"), graph.Literal(right, "CurveName"), up, down,
            new(Number(Default("PelvisAlpha")), new(Number(offset.Groups[1].Value), Number(offset.Groups[2].Value), Number(offset.Groups[3].Value))));

        string Default(string name)
        {
            var matches = Regex.Matches(defaults, @"(?m)^   " + name + @"=([^\r\n]+)");
            Require(matches.Count == 1, "Missing/duplicate pelvis default: " + name);
            return matches[0].Groups[1].Value;
        }
        void Math(Node node, string member) => graph.Function(node, member, "Engine.KismetMathLibrary");
        void Branch(Node node) => Require(node.Kind == "K2Node_IfThenElse", "Changed pelvis branch class.");
        void LinkReroutes(Node node, string input, Node expected, string output)
        {
            var (other, pin) = graph.FollowReroutes(node, input);
            Require(other == expected && pin.Name == output, "Changed pelvis execution order.");
        }
        void Literal(Node node, string pin, string value) => Require(graph.Literal(node, pin) == value, "Changed pelvis literal: " + pin);
        Node Curve(string name, string curve)
        {
            var node = graph.Named(name);
            Require(node.Kind == "K2Node_CallFunction" && node.Member == "GetCurveValue", "Changed pelvis curve read.");
            graph.Self(node); Literal(node, "CurveName", curve); return node;
        }
        void SelfVariable(Node node, string kind, string member)
        { Require(node.Kind == kind && node.Member == member, "Changed pelvis property."); graph.Self(node); }
        void LocalVariable(Node node, string kind, string member)
        {
            Require(node.Kind == kind && node.Member == member && node.Body.Contains("MemberScope=\"SetPelvisIKOffset\"", StringComparison.Ordinal),
                "Changed pelvis function-local variable.");
        }
        void LocalRead(Node node, string pin, string name, string member, string output)
        { var variable = graph.Named(name); LocalVariable(variable, "K2Node_VariableGet", member); graph.Link(node, pin, variable, output); }
        void SelfRead(Node node, string pin, string name, string member, string output)
        { var variable = graph.Named(name); SelfVariable(variable, "K2Node_VariableGet", member); graph.Link(node, pin, variable, output); }
        void SetTarget(string name, string branch, string input, string member)
        {
            var setter = graph.Named(name); LocalVariable(setter, "K2Node_VariableSet", "PelvisTarget");
            graph.Link(setter, "execute", select, branch); LocalRead(setter, "PelvisTarget", input, member, member);
        }
        float Interp(string setterName, string functionName, string branch, string current, string target, string delta)
        {
            var setter = graph.Named(setterName); SelfVariable(setter, "K2Node_VariableSet", "PelvisOffset");
            LinkReroutes(setter, "execute", upward, branch);
            var interp = graph.Named(functionName); Math(interp, "VInterpTo"); graph.Link(setter, "PelvisOffset", interp, "ReturnValue");
            SelfRead(interp, "Current", current, "PelvisOffset", "PelvisOffset");
            LocalRead(interp, "Target", target, "PelvisTarget", "PelvisTarget");
            SelfRead(interp, "DeltaTime", delta, "DeltaTimeX", "DeltaTimeX");
            return float.Parse(graph.Literal(interp, "InterpSpeed"), CultureInfo.InvariantCulture);
        }
    }
    private static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
