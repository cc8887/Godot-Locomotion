using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsAirPoseCompilerTests
{
    private static readonly Lazy<AlsAnimationSetDefinition> Set = new(P3RepositoryFixtures.LoadAnimationSet);
    private static AlsLocomotionSourceProfile Sources(string json) => AlsLocomotionSourceCompiler.CompileWithMovement(json, Set.Value,
        AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), Set.Value).SkeletonId);
    private static AlsAirPoseProfile Compile(string json) => AlsAirPoseCompiler.Compile(json, Read("v4_pose_cache_graph.json"),
        Read("v4_falling_lean_sampling.json"), Sources(json), Set.Value);

    [Fact]
    public void CompilesActualPoseOrderAndIndependentNestedJumpAndLeanIdentities()
    {
        var result = Compile(Read());
        Assert.False(result.Fall.Jump); Assert.True(result.Jump.Jump);
        Assert.Equal(new[] { 63, 64, 62, 65, 66 }, new[] { result.Fall.Loop, result.Fall.Fast, result.Fall.Flail, result.Fall.Heavy, result.Fall.Light });
        Assert.Equal(new[] { -1, -1, -1, 69, 70 }, new[] { result.Jump.Loop, result.Jump.Fast, result.Jump.Flail, result.Jump.Heavy, result.Jump.Light });
        Assert.Equal(67, result.Fall.Lean.PlayerId); Assert.Equal(68, result.Jump.Lean.PlayerId);
        Assert.Equal(263, result.NestedJump.Runtime.MachineNodeIndex);
        Assert.Equal(245, result.NestedJump.ParentMachineNodeIndex);
    }

    [Theory]
    [InlineData("Fall", "prediction-rate")] [InlineData("Jump", "prediction-rate")]
    [InlineData("Fall", "prediction-order")] [InlineData("Jump", "prediction-order")]
    [InlineData("Fall", "signed-speed")] [InlineData("Jump", "signed-speed")]
    [InlineData("Fall", "relevance")] [InlineData("Jump", "reinitialize")]
    [InlineData("Fall", "additive-alpha")] [InlineData("Jump", "additive-lod")]
    [InlineData("Fall", "curve-write")] [InlineData("Jump", "curve-name")]
    [InlineData("Fall", "flail-range")] [InlineData("Fall", "fast-order")]
    [InlineData("Jump", "nested-graph")] [InlineData("Fall", "lifecycle")]
    public void RejectsChangedAirGraphSemantics(string graphName, string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        var graph = root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == graphName)!;
        JsonNode Node(string name) => graph["nodes"]!.AsArray().Single(n => n!["name"]!.GetValue<string>() == name)!;
        JsonNode Pin(JsonNode node, string name) => node["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!;
        var top = Node("AnimGraphNode_TwoWayBlend_0"); var landing = Node("AnimGraphNode_TwoWayBlend_1");
        var additive = Node("AnimGraphNode_ApplyAdditive_0"); var modify = Node("AnimGraphNode_ModifyCurve_3");
        switch (mutation)
        {
            case "prediction-rate": top["properties"]!["BlendNode"]!["alphaScaleBiasClamp"]!["interpSpeedIncreasing"] = 5; break;
            case "prediction-order": Pin(top, "A")["links"]![0]!["node"] = "AnimGraphNode_TwoWayBlend_1"; break;
            case "signed-speed": landing["properties"]!["BlendNode"]!["alphaScaleBiasClamp"]!["inRange"]!["min"] = 1000; break;
            case "relevance": top["properties"]!["BlendNode"]!["bAlwaysUpdateChildren"] = true; break;
            case "reinitialize": top["properties"]!["BlendNode"]!["bResetChildOnActivation"] = true; break;
            case "additive-alpha": Pin(additive, "Alpha")["value"] = "0.5"; break;
            case "additive-lod": additive["properties"]!["Node"]!["lODThreshold"] = 1; break;
            case "curve-write": Pin(modify, "CurveValues_0")["value"] = "0"; break;
            case "curve-name": modify["properties"]!["Node"]!["curveNames"]![1] = "FootLock_L"; break;
            case "flail-range": Node("AnimGraphNode_TwoWayBlend_2")["properties"]!["BlendNode"]!["alphaScaleBiasClamp"]!["inRange"]!["max"] = -2000; break;
            case "fast-order": Pin(Node("AnimGraphNode_TwoWayBlend_3"), "A")["links"]![0]!["node"] = "AnimGraphNode_SequencePlayer_4"; break;
            case "nested-graph": Node("AnimGraphNode_StateMachine_0")["properties"]!["EditorStateMachineGraph"] = "Other"; break;
            case "lifecycle": top["properties"]!["BlendNode"]!["updateFunction"]!["functionName"] = "Other"; break;
        }
        if (mutation == "nested-graph") Assert.Throws<AlsCompilationException>(() => Compile(root.ToJsonString()));
        else Assert.Throws<FormatException>(() => Compile(root.ToJsonString()));
    }

    [Fact]
    public void RejectsAProfileBorrowedFromADifferentGraphRevision()
    {
        var json = Read();
        Assert.Throws<FormatException>(() => AlsAirPoseCompiler.Compile(json + " ", Read("v4_pose_cache_graph.json"),
            Read("v4_falling_lean_sampling.json"), Sources(json), Set.Value));
    }
    private static string Read(string name = "v4_main_movement_graph.json") => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config", name));
}
