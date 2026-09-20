using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsLayeringInputCompilerTests
{
    private const string Source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";
    private const string Update = "UpdateLayerValues";

    [Fact]
    public void NativeUpdateCompilesWithEveryCurveAndNativeFloorVerification()
    {
        var model = AlsLayeringInputCompiler.Compile(Read().ToJsonString());
        string[] expected = ["Mask_AimOffset", "BasePose_N", "BasePose_CLF", "Layering_Spine_Add", "Layering_Head_Add",
            "Layering_Arm_L_Add", "Layering_Arm_R_Add", "Layering_Hand_L", "Layering_Hand_R", "Enable_HandIK_L",
            "Layering_Arm_L", "Enable_HandIK_R", "Layering_Arm_R", "Layering_Arm_L_LS", "Layering_Arm_R_LS"];
        Assert.Equal(expected, model.CurveNames.ToArray());
        var input = model.Evaluate(new(4, 2, 1), new(3, 2, 1),
            ["Mask_AimOffset", "Enable_HandIK_L", "Layering_Arm_L", "Enable_HandIK_R", "Layering_Arm_R", "Layering_Arm_L_LS"],
            [new(.25f), new(.5f), new(.75f), new(.25f), new(.5f), new(.99f)]);
        Assert.Equal(.75, input.GetValue("Enable_AimOffset"));
        Assert.Equal(.375, input.GetValue("Enable_HandIK_L"));
        Assert.Equal(.125, input.GetValue("Enable_HandIK_R"));
        Assert.Equal(1, input.GetValue("Arm_L_MS"));
    }

    [Fact]
    public void AcceptsAbsoluteMacroOwnerSerializationWithoutChangingBindings()
    {
        var root = Read(); var graph = Graph(root, Update);
        graph["nativeText"] = graph["nativeText"]!.GetValue<string>()
            .Replace("/Script/Engine.EdGraph'ALS_AnimBP:GetAnimCurve_Compact'", "/Script/Engine.EdGraph'" + Source + ":GetAnimCurve_Compact'", StringComparison.Ordinal)
            .Replace("/Script/Engine.AnimBlueprint'ALS_AnimBP'", "/Script/Engine.AnimBlueprint'" + Source + "'", StringComparison.Ordinal);
        Assert.Equal(AlsLayeringInputCompiler.Compile(Read().ToJsonString()).CurveNames.ToArray(),
            AlsLayeringInputCompiler.Compile(root.ToJsonString()).CurveNames.ToArray());
    }

    [Theory]
    [InlineData("source")]
    [InlineData("graph_identity")]
    [InlineData("curve_side")]
    [InlineData("compact_function")]
    [InlineData("compact_owner")]
    [InlineData("macro_graph")]
    [InlineData("macro_owner")]
    [InlineData("aim_lerp_start")]
    [InlineData("aim_lerp_end")]
    [InlineData("hand_lerp_start")]
    [InlineData("hand_curve_side")]
    [InlineData("floor_function")]
    [InlineData("floor_side")]
    [InlineData("integer_subtraction")]
    [InlineData("mesh_conversion")]
    [InlineData("branch_order")]
    [InlineData("assignment_order")]
    [InlineData("setter_owner")]
    [InlineData("floor_oracle")]
    [InlineData("floor_domain")]
    [InlineData("floor_count")]
    [InlineData("variable_precision")]
    [InlineData("formula_precision")]
    [InlineData("curve_precision")]
    public void RejectsChangesToConsumedLayeringSemantics(string mutation)
    {
        var root = Read();
        switch (mutation)
        {
            case "source": root["source"] = Source + "Other"; break;
            case "graph_identity": Graph(root, Update)["path"] = Source + ":Other"; break;
            case "curve_side":
                SetLiteral(root, Update, "K2Node_MacroInstance_35", "Name", "Layering_Arm_R_LS"); break;
            case "compact_function":
                ReplaceNode(root, "GetAnimCurve_Compact", "K2Node_CallFunction_0", "MemberName=\"GetCurveValue\"", "MemberName=\"GetCurveValueWithDefault\""); break;
            case "compact_owner":
                ReplaceNode(root, "GetAnimCurve_Compact", "K2Node_CallFunction_0", "bSelfContext=True", "bSelfContext=False"); break;
            case "macro_graph":
                ReplaceNode(root, Update, "K2Node_MacroInstance_35", ":GetAnimCurve_Compact'", ":GetAnimCurve_Clamped'"); break;
            case "macro_owner":
                ReplaceNode(root, Update, "K2Node_MacroInstance_35", "GraphBlueprint=\"/Script/Engine.AnimBlueprint'ALS_AnimBP'\"",
                    "GraphBlueprint=\"/Script/Engine.AnimBlueprint'Other_AnimBP'\""); break;
            case "aim_lerp_start": SetLiteral(root, Update, "K2Node_CallFunction_3", "A", "0.0"); break;
            case "aim_lerp_end": SetLiteral(root, Update, "K2Node_CallFunction_3", "B", "1.000000"); break;
            case "hand_lerp_start": SetLiteral(root, Update, "K2Node_CallFunction_10", "A", "1.000000"); break;
            case "hand_curve_side":
                Rewire(root, Update, "K2Node_CallFunction_10", "Alpha", "K2Node_MacroInstance_1", "Value"); break;
            case "floor_function":
                ReplaceNode(root, Update, "K2Node_CallFunction_8", "MemberName=\"FFloor\"", "MemberName=\"FTrunc\""); break;
            case "floor_side":
                Rewire(root, Update, "K2Node_CallFunction_8", "A", "K2Node_VariableSet_16", "Output_Get"); break;
            case "integer_subtraction": SetLiteral(root, Update, "K2Node_CallFunction_6", "A", "0"); break;
            case "mesh_conversion":
                ReplaceNode(root, Update, "K2Node_CallFunction_7", "MemberName=\"Conv_IntToDouble\"", "MemberName=\"Conv_IntToFloat\""); break;
            case "branch_order":
                Rewire(root, Update, "K2Node_Knot_9", "InputPin", "K2Node_ExecutionSequence_0", "then_1");
                Rewire(root, Update, "K2Node_Knot_0", "InputPin", "K2Node_ExecutionSequence_0", "then_0"); break;
            case "assignment_order":
                Rewire(root, Update, "K2Node_VariableSet_28", "execute", "K2Node_Knot_3", "OutputPin");
                Rewire(root, Update, "K2Node_VariableSet_31", "execute", "K2Node_VariableSet_28", "then");
                Rewire(root, Update, "K2Node_VariableSet_16", "execute", "K2Node_VariableSet_31", "then"); break;
            case "setter_owner":
                ReplaceNode(root, Update, "K2Node_VariableSet_31", "bSelfContext=True", "bSelfContext=False"); break;
            case "floor_oracle": root["floorVerification"]![0]!["floorValue"] = int.MaxValue; break;
            case "floor_domain": root["floorVerification"]![0]!["curveValue"] = 0; break;
            case "floor_count": root["floorVerification"]!.AsArray().RemoveAt(0); break;
            case "variable_precision":
                ReplaceNode(root, Update, "K2Node_VariableSet_31", "PinType.PinSubCategory=\"double\"", "PinType.PinSubCategory=\"float\""); break;
            case "formula_precision":
                ReplaceNode(root, Update, "K2Node_CallFunction_3", "PinType.PinSubCategory=\"double\"", "PinType.PinSubCategory=\"float\""); break;
            case "curve_precision":
                ReplaceNode(root, "GetAnimCurve_Compact", "K2Node_CallFunction_0", "PinType.PinSubCategory=\"float\"", "PinType.PinSubCategory=\"double\""); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        Assert.ThrowsAny<Exception>(() => AlsLayeringInputCompiler.Compile(root.ToJsonString()));
    }

    // Preserve reciprocal links so graph edits exercise semantic rejection.
    private static void Rewire(JsonNode root, string graphName, string node, string pin, string source, string output)
    {
        var graph = Graph(root, graphName);
        var input = Pin(Body(graph, node), pin);
        var inputId = Regex.Match(input, @"PinId=(\w+)").Groups[1].Value;
        var old = Regex.Match(input, @"LinkedTo=\((\w+) (\w+),\)");
        Assert.True(old.Success);
        var target = node + " " + inputId + ",";
        MutateBody(graph, old.Groups[1].Value, body => ChangePinById(body, old.Groups[2].Value,
            line => line.Replace(target, "", StringComparison.Ordinal)));
        var outputId = Regex.Match(Pin(Body(graph, source), output), @"PinId=(\w+)").Groups[1].Value;
        MutateBody(graph, source, body => ChangePinById(body, outputId, line =>
        {
            var links = Regex.Match(line, @"LinkedTo=\(([^)]*)\)");
            return links.Success ? line.Replace(links.Value, "LinkedTo=(" + links.Groups[1].Value + target + ")", StringComparison.Ordinal)
                : line.Insert(line.LastIndexOf(')'), "LinkedTo=(" + target + "),");
        }));
        MutateBody(graph, node, body => ChangePinById(body, inputId,
            line => line.Replace(old.Value, "LinkedTo=(" + source + " " + outputId + ",)", StringComparison.Ordinal)));
    }
    private static void SetLiteral(JsonNode root, string graph, string node, string pin, string value)
    {
        var item = Graph(root, graph); var line = Pin(Body(item, node), pin);
        var old = Regex.Match(line, @"(?:^|,)DefaultValue=""([^""]*)"""); Assert.True(old.Success);
        MutateBody(item, node, body => body.Replace(line,
            line.Replace(old.Value, ",DefaultValue=\"" + value + "\"", StringComparison.Ordinal), StringComparison.Ordinal));
    }
    private static void ReplaceNode(JsonNode root, string graph, string node, string old, string changed) =>
        MutateBody(Graph(root, graph), node, body =>
        {
            Assert.Contains(old, body); return body.Replace(old, changed, StringComparison.Ordinal);
        });
    private static string ChangePinById(string body, string id, Func<string, string> change)
    {
        var line = Regex.Matches(body, @"CustomProperties Pin [^\r\n]+").Single(m => m.Value.Contains("PinId=" + id + ",", StringComparison.Ordinal)).Value;
        return body.Replace(line, change(line), StringComparison.Ordinal);
    }
    private static string Pin(string body, string name) => Regex.Matches(body, @"CustomProperties Pin [^\r\n]+")
        .Single(m => m.Value.Contains("PinName=\"" + name + "\"", StringComparison.Ordinal)).Value;
    private static Regex NodePattern(string name) => new(@"(?ms)^   Begin Object Name=""" + Regex.Escape(name) + @"""[^\r\n]*\r?\n(.*?)^   End Object");
    private static string Body(JsonNode graph, string node)
    {
        var match = NodePattern(node).Match(graph["nativeText"]!.GetValue<string>());
        Assert.True(match.Success); return match.Groups[1].Value;
    }
    private static void MutateBody(JsonNode graph, string node, Func<string, string> mutate)
    {
        var text = graph["nativeText"]!.GetValue<string>(); var match = NodePattern(node).Match(text);
        Assert.True(match.Success); var body = match.Groups[1];
        graph["nativeText"] = text[..body.Index] + mutate(body.Value) + text[(body.Index + body.Length)..];
    }
    private static JsonNode Graph(JsonNode root, string name) => root["graphs"]!.AsArray()
        .Single(g => g!["path"]!.GetValue<string>() == Source + ":" + name)!;
    private static JsonNode Read() => JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config/v4_layering_inputs.json")))!;
}
