using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsStandingSlotCompilerTests
{
    [Fact]
    public void CompilesActualIdleSlotAndItsSourceOverrides()
    {
        var result = AlsStandingSlotCompiler.Compile(Read());
        Assert.Equal("(N) Turn/Rotate", result.SlotName);
        Assert.Equal("RotationScale", result.RotationScaleInput);
        Assert.Equal(3, result.SourceCurveOverrides.Count);
        Assert.Equal(1, result.SourceCurveOverrides["FootLock_L"]);
        Assert.Equal(1, result.SourceCurveOverrides["FootLock_R"]);
        Assert.Equal(1, result.SourceCurveOverrides["Enable_Transition"]);
    }

    [Fact]
    public void SourceOverrideUsesTheExposedPinRatherThanTheStructDefault()
    {
        var root = JsonNode.Parse(Read())!;
        var node = Node(root, "AnimGraphNode_ModifyCurve_0");
        Assert.Equal(0, node["properties"]!["Node"]!["curveValues"]![0]!.GetValue<float>());
        node["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "CurveValues_0")!["value"] = "0.25";
        Assert.Equal(.25f, AlsStandingSlotCompiler.Compile(root.ToJsonString()).SourceCurveOverrides["FootLock_L"]);
    }

    [Theory]
    [InlineData("slotName", "Grounded Slot")]
    [InlineData("bAlwaysUpdateSourcePose", "true")]
    public void RejectsDifferentSlotOwnershipOrUpdatePolicy(string property, string value)
    {
        var root = JsonNode.Parse(Read())!;
        var data = Node(root, "AnimGraphNode_Slot_1")["properties"]!["Node"]!;
        data[property] = property == "slotName" ? JsonValue.Create(value) : JsonValue.Create(true);
        Assert.Throws<FormatException>(() => AlsStandingSlotCompiler.Compile(root.ToJsonString()));
    }

    [Fact]
    public void RejectsTheModifierMovedAfterTheSlot()
    {
        var root = JsonNode.Parse(Read())!;
        var pin = Node(root, "AnimGraphNode_Slot_1")["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "Source")!;
        pin["links"]![0]!["node"] = "AnimGraphNode_SequenceEvaluator_0";
        Assert.Throws<FormatException>(() => AlsStandingSlotCompiler.Compile(root.ToJsonString()));
    }

    [Fact]
    public void RejectsDifferentOutputRotationScaleMode()
    {
        var root = JsonNode.Parse(Read())!;
        Node(root, "AnimGraphNode_ModifyCurve_2")["properties"]!["Node"]!["applyMode"] = "Blend";
        Assert.Throws<FormatException>(() => AlsStandingSlotCompiler.Compile(root.ToJsonString()));
    }

    private static JsonNode Node(JsonNode root, string name) => root["graphs"]!.AsArray()
        .Single(g => g!["name"]!.GetValue<string>() == "(N) Not Moving")!["nodes"]!.AsArray()
        .Single(n => n!["name"]!.GetValue<string>() == name)!;
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "v4_locomotion_source_graph.json"));
}
