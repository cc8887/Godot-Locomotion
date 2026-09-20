using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsBaseLayerCompilerTests
{
    [Fact]
    public void CompilesActualOuterChainAndItsCompiledIdentities()
    {
        var profile = AlsBaseLayerCompiler.Compile(Read(false), Read(true));
        Assert.Equal("BaseLayer", profile.SlotName);
        Assert.Equal(101, profile.SlotNodeIndex);
        Assert.Equal(98, profile.InertializationNodeIndex);
        Assert.Equal(245, profile.MovementNodeIndex);
    }

    [Theory]
    [InlineData("bResetOnBecomingRelevant", "true")]
    [InlineData("bForwardRequestsThroughSkippedCachedPoseNodes", "false")]
    [InlineData("defaultBlendProfile", "\"QuickFeet\"")]
    [InlineData("filteredCurves", "[\"FootLock_L\"]")]
    [InlineData("filteredBones", "[{}]")]
    [InlineData("tag", "\"Other\"")]
    public void RejectsUnsupportedInertiaPolicy(string key, string value)
    {
        var root = JsonNode.Parse(Read(false))!;
        Node(root, "AnimGraphNode_Inertialization_0")["properties"]!["Node"]![key] = JsonNode.Parse(value);
        Assert.Throws<FormatException>(() => AlsBaseLayerCompiler.Compile(root.ToJsonString(), Read(true)));
    }

    [Theory]
    [InlineData("AnimGraphNode_Root_0", "AnimGraphNode_Inertialization_0")]
    [InlineData("AnimGraphNode_Slot_1", "AnimGraphNode_StateMachine_9")]
    [InlineData("AnimGraphNode_Inertialization_0", "AnimGraphNode_StateMachine_11")]
    public void RejectsReorderedOrDifferentSourceChain(string name, string target)
    {
        var root = JsonNode.Parse(Read(false))!;
        Node(root, name)["pins"]![0]!["links"]![0]!["node"] = target;
        Assert.Throws<FormatException>(() => AlsBaseLayerCompiler.Compile(root.ToJsonString(), Read(true)));
    }

    [Fact]
    public void RejectsStaleCompiledNodePolicy()
    {
        var cache = JsonNode.Parse(Read(true))!;
        cache["compiledNodeInventory"]!.AsArray().Single(n => n!["compiledNodeIndex"]!.GetValue<int>() == 101)!
            ["properties"]!["Node"]!["bAlwaysUpdateSourcePose"] = true;
        Assert.Throws<FormatException>(() => AlsBaseLayerCompiler.Compile(Read(false), cache.ToJsonString()));
    }

    [Fact]
    public void RejectsWrongAssetProvenance()
    {
        var cache = JsonNode.Parse(Read(true))!; cache["source"] = "Other";
        Assert.Throws<FormatException>(() => AlsBaseLayerCompiler.Compile(Read(false), cache.ToJsonString()));
    }

    [Theory]
    [InlineData("slotName", "\"Grounded Slot\"")]
    [InlineData("bAlwaysUpdateSourcePose", "true")]
    public void RejectsWrongSlotPolicy(string key, string value)
    {
        var root = JsonNode.Parse(Read(false))!;
        Node(root, "AnimGraphNode_Slot_1")["properties"]!["Node"]![key] = JsonNode.Parse(value);
        Assert.Throws<FormatException>(() => AlsBaseLayerCompiler.Compile(root.ToJsonString(), Read(true)));
    }

    [Fact]
    public void RejectsUnimplementedLifecycleCallback()
    {
        var root = JsonNode.Parse(Read(false))!;
        Node(root, "AnimGraphNode_Inertialization_0")["properties"]!["Node"]!["updateFunction"]!["functionName"] = "Tick";
        Assert.Throws<FormatException>(() => AlsBaseLayerCompiler.Compile(root.ToJsonString(), Read(true)));
    }

    private static JsonNode Node(JsonNode root, string name) => root["graphs"]!.AsArray()
        .Single(g => g!["name"]!.GetValue<string>() == "BaseLayer")!["nodes"]!.AsArray()
        .Single(n => n!["name"]!.GetValue<string>() == name)!;
    private static string Read(bool cache) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config",
        cache ? "v4_pose_cache_graph.json" : "v4_main_movement_graph.json"));
}
