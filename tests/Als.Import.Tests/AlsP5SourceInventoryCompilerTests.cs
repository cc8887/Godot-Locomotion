using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;

namespace GodotAls.Import.Tests;

public sealed class AlsP5SourceInventoryCompilerTests
{
    [Fact]
    public void CoversFullNativeGraphWithoutMergingCachesOrRepeatedAssetsIntoPlayers()
    {
        var inventory = Compile(Read());
        Assert.Equal(933, inventory.Nodes.Length);
        Assert.Equal(233, inventory.Nodes.Count(n => n.Kind == AlsP5InventoryNodeKind.AssetPlayer));
        Assert.Equal(277, inventory.Samples.Length);
        Assert.Equal(56, inventory.Nodes.Count(n => n.LocomotionPlayerId >= 0));
        Assert.Equal(Enumerable.Range(0, 82), inventory.Samples.Where(s => s.LocomotionSampleId >= 0)
            .Select(s => s.LocomotionSampleId).Order());
        Assert.Equal(177, inventory.Nodes.Count(n => n.Kind == AlsP5InventoryNodeKind.AssetPlayer && n.LocomotionPlayerId < 0));
        Assert.Equal(105, inventory.Nodes.Count(n => n.Kind == AlsP5InventoryNodeKind.CacheRead));
        Assert.Equal(32, inventory.Nodes.Count(n => n.Kind == AlsP5InventoryNodeKind.CacheWrite));
        Assert.Equal(11, inventory.Nodes.Count(n => n.Kind == AlsP5InventoryNodeKind.Slot));
        Assert.Single(inventory.Nodes, n => n.Kind == AlsP5InventoryNodeKind.PoseSnapshot);
        Assert.All(inventory.Nodes.Where(n => n.Kind != AlsP5InventoryNodeKind.AssetPlayer), n => Assert.Equal(0, n.SampleCount));
        Assert.Equal(5, inventory.Nodes.Count(n => n.NodeClass == "AnimGraphNode_BlendSpaceEvaluator" && n.Evaluator && n.Teleport));
        foreach (var cache in inventory.Nodes.Where(n => n.Kind == AlsP5InventoryNodeKind.CacheRead))
            Assert.Equal(AlsP5InventoryNodeKind.CacheWrite, inventory.Nodes.Single(n => n.PropertyIndex == cache.CacheSourcePropertyIndex).Kind);
        Assert.All(inventory.Samples.GroupBy(s => s.NodeIndex), group =>
            Assert.Equal(Enumerable.Range(0, group.Count()), group.Select(s => s.SourceIndex)));
    }

    [Theory]
    [InlineData("missing_binding")]
    [InlineData("duplicate_node")]
    [InlineData("duplicate_index")]
    [InlineData("wrong_sample")]
    [InlineData("missing_sample")]
    [InlineData("wrong_group")]
    [InlineData("wrong_evaluator")]
    [InlineData("wrong_loop")]
    [InlineData("cache_is_player")]
    [InlineData("unknown_asset")]
    [InlineData("unknown_player_class")]
    [InlineData("missing_unbound")]
    [InlineData("property_index")]
    [InlineData("cache_target")]
    public void RejectsNativeCoverageAndOwnershipDrift(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        var nodes = root["compiledNodeInventory"]!.AsArray();
        var bound = nodes.First(n => n!["assetPlayer"]!.GetValue<bool>() &&
            n["compiledNodeIndex"]!.GetValue<int>() == 199)!;
        switch (mutation)
        {
            case "missing_binding": nodes.Remove(bound); break;
            case "duplicate_node": nodes.Add(bound.DeepClone()); break;
            case "duplicate_index": nodes[0]!["compiledNodeIndex"] = 199; break;
            case "wrong_sample": bound["samples"]![0] = bound["samples"]![1]!.GetValue<string>(); break;
            case "missing_sample": bound["samples"]!.AsArray().RemoveAt(0); break;
            case "wrong_group": bound["groupName"] = "Other"; break;
            case "wrong_evaluator": bound["evaluator"] = true; break;
            case "wrong_loop": bound["loop"] = false; break;
            case "cache_is_player": bound["class"] = "AnimGraphNode_UseCachedPose"; break;
            case "unknown_asset": bound["asset"] = "/Missing"; break;
            case "unknown_player_class": bound["class"] = "AnimGraphNode_NewPlayer"; break;
            case "missing_unbound": nodes.RemoveAt(0); break;
            case "property_index": bound["propertyIndex"] = bound["compiledNodeIndex"]!.GetValue<int>(); break;
            case "cache_target":
                nodes.First(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_UseCachedPose")!["cacheSourcePropertyIndex"] =
                    bound["propertyIndex"]!.GetValue<int>();
                break;
        }
        Assert.Throws<ArgumentException>(() => Compile(root.ToJsonString()));
    }

    [Fact]
    public void SnapshotIsDefensiveAndPreservesDigestProvenance()
    {
        var text = Read(); var first = Compile(text); var second = Compile(text);
        Assert.Equal(first.Digest, second.Digest);
        var nodes = first.Nodes; var samples = first.Samples;
        nodes[0] = nodes[0] with { LocomotionPlayerId = 999 };
        samples[0] = samples[0] with { LocomotionSampleId = 999 };
        Assert.Equal(second.Nodes, first.Nodes); Assert.Equal(second.Samples, first.Samples);
        var root = JsonNode.Parse(text)!;
        root["compiledNodeInventory"]![0]!["properties"]!["inventoryProbe"] = true;
        Assert.NotEqual(first.Digest, Compile(root.ToJsonString()).Digest);
    }

    private static AlsP5SourceInventory Compile(string json)
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var profile = AlsLocomotionProfileCompiler.Compile(File.ReadAllText(Path.Combine(RepositoryRoot.Find(),
            "assets", "config", "p4_cycle_locomotion_profile.json")), set);
        var sources = AlsLocomotionSourceCompiler.Compile(File.ReadAllText(Path.Combine(RepositoryRoot.Find(),
            "assets", "config", "v4_locomotion_source_graph.json")), set, profile.SkeletonId);
        return AlsP5SourceInventoryCompiler.Compile(json, set, sources);
    }

    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(),
        "assets", "config", "v4_anim_graph_inventory.json"));
}
