using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsDirectionFeedbackCompilerTests
{
    [Fact]
    public void CompilesActualEventGraphAndAllSixHipMappings()
    {
        var profile = Compile();
        Assert.Equal(2, profile.Runtime.SpeedLimit);
        Assert.Equal(.1f, profile.Runtime.DelaySeconds);
        int[] hips = [0, 1, 4, 5, 2, 3];
        for (var i = 0; i < 6; i++)
        {
            AlsDirectionFeedbackEvents events = default;
            profile.Runtime.Enter((AlsCycleDirection)i, ref events);
            Assert.Equal(hips[i], events[0].HipsValue);
            Assert.Equal(16 + hips[i], events[0].NotifyIndex);
        }
        Assert.Equal(profile.Digest, Compile().Digest);
    }

    [Theory]
    [InlineData("K2Node_CallFunction_1", "Delay", "RetriggerableDelay")]
    [InlineData("K2Node_CallFunction_20", "Less_DoubleDouble", "LessEqual_DoubleDouble")]
    public void RejectsDifferentPivotSemantics(string node, string before, string after)
    {
        var root = JsonNode.Parse(Read("inputs"))!;
        var function = EventNode(root, node)["properties"]!["FunctionReference"]!;
        Assert.Equal(before, function["memberName"]!.GetValue<string>());
        function["memberName"] = after;
        Assert.Throws<FormatException>(() => AlsDirectionFeedbackCompiler.Compile(root.ToJsonString(), Read("source_graph")));
    }

    [Fact]
    public void AuthoredDelayIsReadRatherThanHardcoded()
    {
        var root = JsonNode.Parse(Read("inputs"))!;
        Pin(EventNode(root, "K2Node_CallFunction_1"), "Duration")["value"] = "0.25";
        var changed = AlsDirectionFeedbackCompiler.Compile(root.ToJsonString(), Read("source_graph"));
        Assert.Equal(.25f, changed.Runtime.DelaySeconds);
        Assert.NotEqual(Compile().Digest, changed.Digest);
    }

    [Theory]
    [InlineData("K2Node_VariableSet_5", "Pivot", "true")]
    [InlineData("K2Node_CallFunction_1", "Duration", "NaN")]
    public void RejectsInvalidContinuationAndDuration(string node, string pin, string value)
    {
        var root = JsonNode.Parse(Read("inputs"))!;
        Pin(EventNode(root, node), pin)["value"] = value;
        Assert.ThrowsAny<Exception>(() => AlsDirectionFeedbackCompiler.Compile(root.ToJsonString(), Read("source_graph")));
    }

    [Fact]
    public void GeneratedNotifyIndexMustMatchTheSourceState()
    {
        var root = JsonNode.Parse(Read("inputs"))!;
        root["bakedMachines"]!.AsArray().Single(m => m!["machineName"]!.GetValue<string>() == "(N) Directional States")!
            ["states"]![0]!["startNotify"] = 17;
        Assert.Throws<FormatException>(() => AlsDirectionFeedbackCompiler.Compile(root.ToJsonString(), Read("source_graph")));
    }

    [Fact]
    public void EditorNotifyCannotDisagreeWithGeneratedTransition()
    {
        var root = JsonNode.Parse(Read("source_graph"))!;
        var graph = root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == "(N) Directional States")!;
        var transition = graph["nodes"]!.AsArray().First(n => n!["class"]!.GetValue<string>() == "AnimStateTransitionNode" &&
            n["properties"]!["TransitionStart"]!["notifyName"]!.GetValue<string>() == "Pivot")!;
        transition["properties"]!["TransitionStart"]!["notifyName"] = "None";
        Assert.Throws<FormatException>(() => AlsDirectionFeedbackCompiler.Compile(Read("inputs"), root.ToJsonString()));
    }

    private static AlsDirectionFeedbackProfile Compile() => AlsDirectionFeedbackCompiler.Compile(Read("inputs"), Read("source_graph"));
    private static JsonNode EventNode(JsonNode root, string name) => root["graphs"]!.AsArray()
        .Single(g => g!["name"]!.GetValue<string>() == "EventGraph")!["nodes"]!.AsArray()
        .Single(n => n!["name"]!.GetValue<string>() == name)!;
    private static JsonNode Pin(JsonNode node, string name) => node["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!;
    private static string Read(string suffix) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "v4_locomotion_" + suffix + ".json"));
}
