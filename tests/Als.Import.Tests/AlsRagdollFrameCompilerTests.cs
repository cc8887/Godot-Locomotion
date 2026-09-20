using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRagdollFrameCompilerTests
{
    private static readonly Lazy<AlsAnimationSetDefinition> Set = new(P3RepositoryFixtures.LoadAnimationSet);
    private static string Read(string name) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config", name));
    private static AlsRagdollFrameDefinition Compile(string input, string layer) => AlsRagdollFrameCompiler.Compile(input, layer,
        Read("v4_movement_runtime_inputs.json"), AlsRagdollPoseCompiler.Compile(input, layer, Set.Value), Set.Value);
    [Fact]
    public void BindsRealMachineAndIndependentFlailTiming()
    {
        var frame = Compile(Read("v4_ragdoll_inputs.json"), Read("v4_layering_inputs.json"));
        Assert.Equal(14, frame.MachineNodeIndex); Assert.Equal(16, frame.PlayerNodeIndex);
        Assert.Equal(AlsGroundedMachineKind.Ragdoll, frame.Machine.Kind); Assert.True(frame.Machine.SkipFirstBlend);
        Assert.Equal(0, frame.Machine.Edges[0].Duration); Assert.Equal(0, frame.Machine.Edges[1].Duration);
        Assert.Equal(1, frame.Sequence.RateScale); Assert.Equal(0.6666666865348816f, frame.Sequence.DurationSeconds);
    }
    [Theory] [InlineData("duration")] [InlineData("destination")] [InlineData("delegate")] [InlineData("condition")] [InlineData("relevance")]
    [InlineData("disabled")] [InlineData("reset")]
    public void RejectsChangedStateLifecycleOrGraphWiring(string change)
    {
        var input = JsonNode.Parse(Read("v4_ragdoll_inputs.json"))!; var layer = JsonNode.Parse(Read("v4_layering_inputs.json"))!;
        var machine = input["bakedMachines"]![0]!;
        switch (change)
        {
            case "duration": machine["transitions"]![0]!["crossfadeDuration"] = .5; break;
            case "destination": machine["transitions"]![0]!["nextState"] = 0; break;
            case "delegate": machine["states"]![0]!["transitions"]![0]!["canTakeDelegateIndex"] = 20; break;
            case "condition":
                var graph = input["graphs"]!.AsArray().Single(g => g!["path"]!.GetValue<string>().EndsWith("AnimStateTransitionNode_0.Transition", StringComparison.Ordinal))!;
                graph["nativeText"] = graph["nativeText"]!.GetValue<string>().Replace("DefaultValue=\"NewEnumerator3\"", "DefaultValue=\"NewEnumerator1\"", StringComparison.Ordinal); break;
            case "relevance":
                var node = layer["compiledNodeInventory"]!.AsArray().Single(n => n!["path"]!.GetValue<string>().EndsWith("AnimGraphNode_StateMachine_10", StringComparison.Ordinal))!;
                node["properties"]!["Node"]!["bReinitializeOnBecomingRelevant"] = false; break;
            case "disabled":
                var edge = input["editorStateNodes"]!.AsArray().Single(n => n!["path"]!.GetValue<string>().EndsWith("AnimStateTransitionNode_0", StringComparison.Ordinal))!;
                edge["properties"]!["bDisabled"] = true; break;
            case "reset":
                var state = input["editorStateNodes"]!.AsArray().Single(n => n!["path"]!.GetValue<string>().EndsWith("AnimStateNode_0", StringComparison.Ordinal))!;
                state["properties"]!["bAlwaysResetOnEntry"] = true; break;
        }
        Assert.ThrowsAny<Exception>(() => Compile(input.ToJsonString(), layer.ToJsonString()));
    }
}
