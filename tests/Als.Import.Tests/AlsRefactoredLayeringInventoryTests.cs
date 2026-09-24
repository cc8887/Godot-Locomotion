using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredLayeringInventoryTests
{
    private static string Graph => MantlingHostFixture.Read("refactored_layering_graphs");
    private static string Inventory => MantlingHostFixture.Read("refactored_layering_inventory");
    [Fact]
    public void OriginalCompiledGraphSeparatesEditorOnlyNodesAndRetainsNativeCacheOrder()
    {
        var result = AlsRefactoredLayeringInventoryCompiler.Compile(Graph, Inventory);
        Assert.Equal(106, result.Nodes.Count);
        Assert.Equal(94, result.CompiledPropertyCount);
        Assert.Equal(12, result.Nodes.Values.Count(n => n.Index < 0));
        Assert.Equal(new[] {90,3,71,70,56,69,68,57,36,51,52}, result.CacheUpdateOrder);
        Assert.Equal(2, result.Nodes.Values.Count(n => n.Class == "AlsAnimGraphNode_CurvesBlend" && n.Index >= 0));
        Assert.Equal(2, result.Nodes.Values.Count(n => n.Class == "AnimGraphNode_SequenceEvaluator" && n.Index >= 0));
        Assert.Single(result.Nodes.Values, n => n.Class == "AnimGraphNode_MultiWayBlend" && n.Index >= 0);
        Assert.Equal(7, result.Nodes.Values.Count(n => n.Class == "AnimGraphNode_Slot" && n.Index >= 0));
        Assert.All(result.Nodes.Values.Where(n => n.Index >= 0), n => Assert.Equal(System.Text.Json.JsonValueKind.Object,n.Runtime.ValueKind));
    }
    [Theory]
    [InlineData("hash")][InlineData("guid")][InlineData("index")][InlineData("cache")]
    [InlineData("order")][InlineData("missing")][InlineData("custom")]
    public void RefusesStaleOrIncompleteCompiledMetadata(string change)
    {
        var json = JsonNode.Parse(Inventory)!;
        var layer = json["blueprints"]![2]!;
        var nodes = layer["nodes"]!.AsArray();
        var use = nodes.Single(n => n!["path"]!.GetValue<string>().EndsWith(".AnimGraphNode_UseCachedPose_0",StringComparison.Ordinal))!;
        switch(change)
        {
            case "hash": json["graphsSha256"] = "wrong"; break;
            case "guid": use["nodeGuid"] = new string('0',32); break;
            case "index": use["propertyIndex"] = 0; break;
            case "cache": use["runtime"]!["linkToCachingNode"]!["linkId"] = use["propertyIndex"]!.GetValue<int>(); break;
            case "order": layer["orderedSavedPoseNodes"]![0]!["compiledNodeIndices"]![0] = 3; break;
            case "missing": nodes.Remove(use); break;
            case "custom": nodes.Remove(nodes.First(n => n!["class"]!.GetValue<string>() == "AlsAnimGraphNode_CurvesBlend")); break;
        }
        Assert.Throws<ArgumentException>(() => AlsRefactoredLayeringInventoryCompiler.Compile(Graph,json.ToJsonString()));
    }
}
