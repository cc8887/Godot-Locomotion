using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsGroundedMovementCurveTests
{
    [Fact]
    public void GroundedFootEnableUsesExposedPinsInsteadOfZeroBackingArray()
    {
        var root = Read(); var modify = Modify(root);
        Assert.All(modify["properties"]!["Node"]!["curveValues"]!.AsArray(), n => Assert.Equal(0, n!.GetValue<float>()));
        Assert.Equal(new(1, 1), AlsGroundedMovementCurveCompiler.Compile(root.ToJsonString()));
        Pin(modify, "CurveValues_0")["value"] = "0.400000";
        Assert.Equal(new(.4f, 1), AlsGroundedMovementCurveCompiler.Compile(root.ToJsonString()));
    }
    [Theory]
    [InlineData("mode")] [InlineData("alpha")] [InlineData("name")] [InlineData("source")] [InlineData("dynamic")]
    public void RejectsUnsupportedGroundedWrapperSemantics(string mutation)
    {
        var root = Read(); var modify = Modify(root); var node = modify["properties"]!["Node"]!;
        switch (mutation)
        {
            case "mode": node["applyMode"] = "Add"; break;
            case "alpha": node["alpha"] = .5; break;
            case "name": node["curveNames"]![0] = "FootLock_L"; break;
            case "source": Pin(modify, "SourcePose")["links"]![0]!["node"] = "AnimGraphNode_StateResult_0"; break;
            default: Pin(modify, "CurveValues_0")["links"] = new JsonArray(new JsonObject { ["node"] = "Unsupported", ["pin"] = "Value" }); break;
        }
        Assert.ThrowsAny<Exception>(() => AlsGroundedMovementCurveCompiler.Compile(root.ToJsonString()));
    }
    private static JsonNode Read() => JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config/v4_main_movement_graph.json")))!;
    private static JsonNode Modify(JsonNode root) => root["graphs"]!.AsArray().Single(g => g!["path"]!.GetValue<string>().EndsWith(".Main Movement States.AnimStateNode_0.Grounded"))!["nodes"]!.AsArray().Single(n => n!["name"]!.GetValue<string>() == "AnimGraphNode_ModifyCurve_1")!;
    private static JsonNode Pin(JsonNode node, string name) => node["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!;
}
