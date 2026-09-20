using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsCharacterRotationInputClosureTests
{
    [Fact]
    public void NativeInputAndGaitClosureCompilesAndMatchesGaitBoundaries()
    {
        var model = AlsCharacterRotationCompiler.Compile(Read().ToJsonString());
        foreach (var mode in Enum.GetValues<AlsRotationMode>())
        foreach (var stance in Enum.GetValues<AlsStance>())
        foreach (var allowed in Enum.GetValues<AlsGait>())
        {
            var settings = model.Movement(mode, stance);
            var walk = settings.WalkSpeed + .1f;
            var run = settings.RunSpeed + .1f;
            Assert.Equal(AlsGait.Walking, AlsLocomotionModel.CalculateActualGait(MathF.BitDecrement(walk), settings.WalkSpeed, settings.RunSpeed, allowed));
            Assert.Equal(AlsGait.Running, AlsLocomotionModel.CalculateActualGait(walk, settings.WalkSpeed, settings.RunSpeed, allowed));
            Assert.Equal(AlsGait.Running, AlsLocomotionModel.CalculateActualGait(MathF.BitDecrement(run), settings.WalkSpeed, settings.RunSpeed, allowed));
            Assert.Equal(allowed == AlsGait.Sprinting ? AlsGait.Sprinting : AlsGait.Running,
                AlsLocomotionModel.CalculateActualGait(run, settings.WalkSpeed, settings.RunSpeed, allowed));
        }
    }

    [Theory]
    [InlineData("input_comparison")]
    [InlineData("input_threshold")]
    [InlineData("input_acceleration")]
    [InlineData("aim_absolute")]
    [InlineData("aim_delta")]
    [InlineData("speed_vertical")]
    [InlineData("speed_component")]
    [InlineData("begin_order")]
    [InlineData("movement_state_selection")]
    [InlineData("movement_state_enum")]
    [InlineData("previous_state_scope")]
    [InlineData("gait_comparison")]
    [InlineData("gait_margin")]
    [InlineData("gait_allowed_selection")]
    [InlineData("gait_high_speed_result")]
    [InlineData("settings_order")]
    [InlineData("settings_source")]
    [InlineData("cache_order")]
    [InlineData("cache_axis")]
    public void RejectsChangesToConsumedInputGaitAndHistorySemantics(string mutation)
    {
        var root = Read();
        switch (mutation)
        {
            case "input_comparison":
                ReplaceNode(root, "SetEssentialValues", "K2Node_CallFunction_22", "Greater_DoubleDouble", "GreaterEqual_DoubleDouble"); break;
            case "input_threshold":
                SetLiteral(root, "SetEssentialValues", "K2Node_CallFunction_22", "B", "0.010000"); break;
            case "input_acceleration":
                Rewire(root, "SetEssentialValues", "K2Node_CallFunction_26", "A", "K2Node_CallFunction_4", "ReturnValue"); break;
            case "aim_absolute":
                ReplaceNode(root, "SetEssentialValues", "K2Node_CallFunction_21", "MemberName=\"Abs\"", "MemberName=\"SignOfFloat\""); break;
            case "aim_delta":
                ReplaceNode(root, "SetEssentialValues", "K2Node_CallFunction_46", "Subtract_DoubleDouble", "Add_DoubleDouble"); break;
            case "speed_vertical":
                SetLiteral(root, "SetEssentialValues", "K2Node_CallFunction_5", "A_Z", "1.000000"); break;
            case "speed_component":
                Rewire(root, "SetEssentialValues", "K2Node_CallFunction_5", "A_Y", "K2Node_CallFunction_9", "ReturnValue_Z"); break;
            case "begin_order":
                Rewire(root, "On Begin Play", "K2Node_Knot_2", "InputPin", "K2Node_ExecutionSequence_0", "then_3", history: true);
                Rewire(root, "On Begin Play", "K2Node_CallFunction_2", "execute", "K2Node_ExecutionSequence_0", "then_4", history: true); break;
            case "movement_state_selection":
                Rewire(root, "OnMovementStateChanged", "K2Node_SwitchEnum_0", "Selection", "K2Node_VariableGet_2", "PreviousMovementState"); break;
            case "movement_state_enum":
                ReplaceNode(root, "OnMovementStateChanged", "K2Node_SwitchEnum_0", "ALS_MovementState.ALS_MovementState", "ALS_MovementAction.ALS_MovementAction"); break;
            case "previous_state_scope":
                ReplaceNode(root, "OnMovementStateChanged", "K2Node_VariableGet_0", "MemberScope=\"OnMovementStateChanged\"", "MemberScope=\"UpdateCharacterMovement\""); break;
            case "gait_comparison":
                ReplaceNode(root, "GetActualGait", "K2Node_CallFunction_0", "GreaterEqual_DoubleDouble", "Greater_DoubleDouble"); break;
            case "gait_margin":
                SetLiteral(root, "GetActualGait", "K2Node_CommutativeAssociativeBinaryOperator_0", "B", "0.0"); break;
            case "gait_allowed_selection":
                ReplaceNode(root, "GetActualGait", "K2Node_VariableGet_3", "MemberScope=\"GetActualGait\"", "MemberScope=\"UpdateCharacterMovement\""); break;
            case "gait_high_speed_result":
                SetLiteral(root, "GetActualGait", "K2Node_FunctionResult_5", "ActualGait", "NewEnumerator0"); break;
            case "settings_order":
                Rewire(root, "UpdateCharacterMovement", "K2Node_Knot_1", "InputPin", "K2Node_ExecutionSequence_0", "then_2");
                Rewire(root, "UpdateCharacterMovement", "K2Node_Knot_2", "InputPin", "K2Node_ExecutionSequence_0", "then_1"); break;
            case "settings_source":
                ReplaceNode(root, "UpdateDynamicMovementSettings", "K2Node_CallFunction_0", "GetTargetMovementSettings", "DifferentMovementSettings"); break;
            case "cache_order":
                Rewire(root, "TickGraph", "K2Node_CallFunction_10", "execute", "K2Node_ExecutionSequence_0", "then_1");
                Rewire(root, "TickGraph", "K2Node_CallFunction_1", "execute", "K2Node_ExecutionSequence_0", "then_0"); break;
            case "cache_axis":
                Rewire(root, "CacheValues", "K2Node_VariableSet_2", "PreviousAimYaw", "K2Node_CallFunction_1", "ReturnValue_Pitch"); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        Assert.ThrowsAny<Exception>(() => AlsCharacterRotationCompiler.Compile(root.ToJsonString()));
    }

    // Change reciprocal pins together so a rejection proves consumed semantics,
    // rather than merely detecting an invalid one-sided serialized connection.
    private static void Rewire(JsonNode root, string graphName, string node, string pin,
        string source, string output, bool history = false)
    {
        var graph = Graph(root, graphName, history);
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
        var item = Graph(root, graph);
        var line = Pin(Body(item, node), pin);
        var old = Regex.Match(line, @"(?:^|,)DefaultValue=""([^""]*)""");
        Assert.True(old.Success);
        var changed = line.Replace(old.Value, ",DefaultValue=\"" + value + "\"", StringComparison.Ordinal);
        MutateBody(item, node, body => body.Replace(line, changed, StringComparison.Ordinal));
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
    private static JsonNode Graph(JsonNode root, string name, bool history = false) =>
        root[history ? "rotationHistoryGraphs" : "graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == name)!;
    private static JsonNode Read() => JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config/v4_character_rotation_inputs.json")))!;
}
