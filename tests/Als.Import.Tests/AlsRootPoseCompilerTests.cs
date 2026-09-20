using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRootPoseCompilerTests
{
    [Fact]
    public void CompilesActualRootPinsAndPolicyInsteadOfEditorBackingValues()
    {
        var root = Read(); var p = Node(root);
        Assert.Equal(.1f, p["Node"]!["blendTime"]![0]!.GetValue<float>());
        Assert.Equal(new(21, new(.4f, .5f, AlsTransitionBlend.HermiteCubic)), AlsRootPoseCompiler.Compile(root.ToJsonString()));
        var graph = Graph(root);
        graph["nativeText"] = graph["nativeText"]!.GetValue<string>().Replace("DefaultValue=\"0.400000\"", "DefaultValue=\"0.650000\"", StringComparison.Ordinal);
        Assert.Equal(.65f, AlsRootPoseCompiler.Compile(root.ToJsonString()).Blend.FirstSeconds);
    }
    [Theory]
    [InlineData("transitionType", "Inertialization")] [InlineData("blendType", "Linear")]
    [InlineData("childUpateMode", "ResetChildOnActivate")] [InlineData("blendProfile", "/Unsupported")]
    public void RejectsUnsupportedSelectorSemantics(string name, string value)
    {
        var root = Read(); Node(root)["Node"]![name] = value;
        Assert.Throws<InvalidDataException>(() => AlsRootPoseCompiler.Compile(root.ToJsonString()));
    }
    [Fact]
    public void RejectsForeignEnumOrNonFiniteTimes()
    {
        var root = Read(); Node(root)["VisibleEnumEntries"]![0] = "ALS_MovementState::NewEnumerator2";
        Assert.Throws<InvalidDataException>(() => AlsRootPoseCompiler.Compile(root.ToJsonString()));
        root = Read(); var graph = Graph(root);
        graph["nativeText"] = graph["nativeText"]!.GetValue<string>().Replace("DefaultValue=\"0.400000\"", "DefaultValue=\"NaN\"", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => AlsRootPoseCompiler.Compile(root.ToJsonString()));
    }
    private static JsonNode Read() => JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config/v4_layering_inputs.json")))!;
    private static JsonNode Node(JsonNode root) => root["compiledNodeInventory"]!.AsArray().Single(n => n!["path"]!.GetValue<string>().EndsWith(":AnimGraph.AnimGraphNode_BlendListByEnum_0", StringComparison.Ordinal))!["properties"]!;
    private static JsonNode Graph(JsonNode root) => root["graphs"]!.AsArray().Single(n => n!["path"]!.GetValue<string>().EndsWith(":AnimGraph", StringComparison.Ordinal))!;
}
