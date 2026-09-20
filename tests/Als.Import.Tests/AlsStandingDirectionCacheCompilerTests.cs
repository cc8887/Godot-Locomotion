using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsStandingDirectionCacheCompilerTests
{
    private static string Read(string file = "v4_pose_cache_graph.json") => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", file));

    [Fact]
    public void CompilesEightCachesAndTwentySevenReadsInCoreDirectionOrder()
    {
        var profile = AlsStandingDirectionCacheCompiler.Compile(Read("v4_locomotion_source_graph.json"), Read());
        Assert.Equal(new[] { 765, 764, 763, 762, 761, 760, 758, 757 }, profile.CacheNodes.ToArray());
        Assert.Equal(new[] { 763, 760, 765, 757, 758, 764, 762, 761 }, profile.Caches.UpdateOrder.ToArray());
        Assert.Equal(new[] { 770, 771, 772, 773, 777, 778, 779, 780, 799, 800, 796, 797,
            804, 805, 807, 806, 783, 784, 786, 785, 792, 793, 789, 790 }, profile.StateReads.ToArray());
        Assert.Equal(new[] { 752, 754, 753 }, profile.SprintReads.ToArray());
        Assert.Equal(new[] { 0, 1, 2, 4, 0, 1, 3, 5, 0, 1, 2, 5, 0, 1, 3, 4, 0, 1, 3, 4, 0, 1, 2, 5 }, profile.DirectionInputs.ToArray());
        Assert.Equal(27, profile.Caches.Reads.Length);
        Assert.Equal(new[] { 0, 1, 2, 2, 3, 3 }, profile.YawAxes.ToArray());
        var bindings = profile.Caches.Reads.ToArray().ToDictionary(r => r.ReadNodeIndex, r => r.CacheNodeIndex);
        Assert.Equal(bindings[752], bindings[754]); Assert.NotEqual(bindings[752], bindings[753]);
    }

    [Theory]
    [InlineData("read-alias")] [InlineData("axis")] [InlineData("input-role")]
    [InlineData("additive")] [InlineData("order")] [InlineData("extra-read")] [InlineData("cold-input")]
    [InlineData("yaw-axis")] [InlineData("yaw-mode")] [InlineData("yaw-name")] [InlineData("yaw-default")]
    public void RejectsChangedReferenceAxisProducerAndClosure(string mutation)
    {
        var root = JsonNode.Parse(Read("v4_locomotion_source_graph.json"))!;
        var cache = JsonNode.Parse(Read())!;
        var inventory = cache["compiledNodeInventory"]!.AsArray();
        var layer = root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == "(N) CycleBlending")!;
        var state = root["graphs"]!.AsArray().Single(g => g!["path"]!.GetValue<string>().Contains(":(N) CycleBlending.") && g["name"]!.GetValue<string>() == "Move RF")!;
        var blend = state["nodes"]!.AsArray().Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_MultiWayBlend")!;
        var modify = state["nodes"]!.AsArray().Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_ModifyCurve")!;
        switch (mutation)
        {
            case "read-alias": inventory.Single(n => n!["compiledNodeIndex"]!.GetValue<int>() == 786)!["cacheSourcePropertyIndex"] = 171; break;
            case "axis": blend["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "DesiredAlphas_2")!["links"]![0]!["pin"] = "VelocityBlend_R_wrong"; break;
            case "input-role": layer["nodes"]!.AsArray().Single(n => n!["name"]!.GetValue<string>() == "AnimGraphNode_LinkedInputPose_5")!["properties"]!["Node"]!["name"] = "LF"; break;
            case "additive": blend["properties"]!["Node"]!["bAdditiveNode"] = true; break;
            case "cold-input": blend["properties"]!["Node"]!["desiredAlphas"]![0] = 1; break;
            case "yaw-axis": modify["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "CurveValues_0")!["links"]![0]!["pin"] = "FYaw"; break;
            case "yaw-mode": modify["properties"]!["Node"]!["applyMode"] = "Add"; break;
            case "yaw-name": modify["properties"]!["Node"]!["curveNames"]![0] = "Other"; break;
            case "yaw-default": modify["properties"]!["Node"]!["curveValues"]![0] = 1; break;
            case "order": cache["orderedSavedPoseNodes"]!.AsArray().Single(n => n!["root"]!.GetValue<string>() == "(N) CycleBlending")!["compiledNodeIndices"] = new JsonArray(757, 758, 765, 763, 760, 764, 762, 761); break;
            case "extra-read": var extra = inventory.Single(n => n!["compiledNodeIndex"]!.GetValue<int>() == 786)!.DeepClone();
                extra["path"] = extra["path"]!.GetValue<string>() + "_Extra"; extra["compiledNodeIndex"] = 931; inventory.Add(extra); break;
        }
        Assert.Throws<ArgumentException>(() => AlsStandingDirectionCacheCompiler.Compile(root.ToJsonString(), cache.ToJsonString()));
    }
}
