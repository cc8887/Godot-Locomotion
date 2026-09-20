using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsBasePosesCompilerTests
{
    private static readonly Lazy<AlsAnimationSetDefinition> AnimationSet = new(P3RepositoryFixtures.LoadAnimationSet);

    [Fact]
    public void CompilesActualIndependentEvaluatorsAndAuthoredZeroWeightDefaults()
    {
        var set = AnimationSet.Value; var result = Compile(Read(), set);
        Assert.EndsWith(":BasePoses", result.Source);
        Assert.Equal(933, result.CompiledPropertyCount);
        Assert.Equal(671, result.RootNodeIndex); Assert.Equal(669, result.BlendNodeIndex);
        Assert.Equal(new[] { 670, 668 }, result.Evaluators.ToArray().Select(e => e.NodeIndex));
        Assert.Equal(new[] { "BasePose_N", "BasePose_CLF" }, result.WeightProperties.ToArray());
        Assert.Equal(new[] { 0f, 0f }, result.DefaultDesiredAlphas.ToArray());
        Assert.Equal(1, result.AlphaScale); Assert.Equal(0, result.AlphaBias);
        Assert.False(result.Additive); Assert.True(result.NormalizeAlphas);
        Assert.Equal(new[] { "ALS_N_Pose", "ALS_CLF_Pose" }, result.Evaluators.ToArray().Select(e => set.Animations[e.AnimationId].Name));
        Assert.All(result.Evaluators.ToArray(), evaluator =>
        {
            var asset = set.Animations[evaluator.AnimationId];
            Assert.Equal(asset.StableId, evaluator.AssetId); Assert.Equal(asset.ObjectPath, evaluator.AssetPath);
            Assert.Equal(asset.PlayLength, evaluator.Length); Assert.Equal(.033333335f, evaluator.Length);
            Assert.Equal(0, evaluator.ExplicitTime); Assert.Equal(0, evaluator.StartPosition);
            Assert.Equal(0, evaluator.ExplicitFrame); Assert.False(evaluator.UseExplicitFrame);
            Assert.True(evaluator.Loop); Assert.True(evaluator.Teleport);
            Assert.False(asset.Loop); // Asset metadata and evaluator node looping remain distinct.
            Assert.Equal("ExplicitTime", evaluator.ReinitializationBehavior);
            Assert.Equal("None", evaluator.SyncGroupName); Assert.Equal("CanBeLeader", evaluator.SyncGroupRole);
            Assert.Equal("DoNotSync", evaluator.SyncMethod); Assert.False(evaluator.IgnoreForRelevancyTest);
            Assert.Empty(asset.Curves); Assert.Empty(asset.Timeline); Assert.Empty(asset.SyncMarkers);
        });
    }

    [Fact]
    public void PreservesExportedCompiledIdentitiesRatherThanInventingPlayerIds()
    {
        var root = JsonNode.Parse(Read())!;
        var items = root["compiledNodeInventory"]!.AsArray()
            .Where(n => n!["path"]!.GetValue<string>().Contains(":BasePoses.", StringComparison.Ordinal));
        foreach (var item in items)
        {
            var compiled = item!["compiledNodeIndex"]!.GetValue<int>() + 1000;
            item["compiledNodeIndex"] = compiled; item["propertyIndex"] = 1932 - compiled;
        }
        root["compiledPropertyCount"] = 1933;
        var result = Compile(root.ToJsonString());
        Assert.Equal(1671, result.RootNodeIndex); Assert.Equal(1669, result.BlendNodeIndex);
        Assert.Equal(new[] { 1670, 1668 }, result.Evaluators.ToArray().Select(e => e.NodeIndex));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("missing-node")]
    [InlineData("duplicate-graph")]
    [InlineData("node-class")]
    [InlineData("aliased-identity")]
    [InlineData("property-index")]
    [InlineData("root-edge")]
    [InlineData("evaluator-edge")]
    [InlineData("weight-edge")]
    [InlineData("getter-owner")]
    [InlineData("weight-default")]
    [InlineData("explicit-time")]
    [InlineData("native-policy")]
    [InlineData("native-asset")]
    [InlineData("desired-default")]
    [InlineData("normalize")]
    [InlineData("additive")]
    [InlineData("scale-bias")]
    [InlineData("teleport")]
    [InlineData("loop")]
    [InlineData("reinit")]
    [InlineData("sync")]
    [InlineData("frame-mode")]
    [InlineData("callback")]
    [InlineData("summary")]
    [InlineData("extra-field")]
    public void RejectsChangedTopologyDefaultsAndEvaluatorPolicies(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        switch (mutation)
        {
            case "schema": root["inventorySchemaVersion"] = 0; break;
            case "missing-node": root["compiledNodeInventory"]!.AsArray().Remove(Inventory(root, "SequenceEvaluator_0")); break;
            case "duplicate-graph": root["graphs"]!.AsArray().Add(Graph(root).DeepClone()); break;
            case "node-class": Inventory(root, "SequenceEvaluator_0")["class"] = "AnimGraphNode_SequencePlayer"; break;
            case "aliased-identity": Inventory(root, "SequenceEvaluator_1")["compiledNodeIndex"] = 670; break;
            case "property-index": Inventory(root, "SequenceEvaluator_1")["propertyIndex"] = 262; break;
            case "root-edge": Native(root, "AnimGraphNode_Root_0", "LinkedTo=(AnimGraphNode_MultiWayBlend_0 ", "LinkedTo=(AnimGraphNode_SequenceEvaluator_0 "); break;
            case "evaluator-edge": Native(root, "AnimGraphNode_MultiWayBlend_0", "LinkedTo=(AnimGraphNode_SequenceEvaluator_1 ", "LinkedTo=(AnimGraphNode_SequenceEvaluator_0 "); break;
            case "weight-edge": Native(root, "AnimGraphNode_MultiWayBlend_0", "LinkedTo=(K2Node_VariableGet_22 ", "LinkedTo=(K2Node_VariableGet_23 "); break;
            case "getter-owner": Native(root, "K2Node_VariableGet_22", "bSelfContext=True", "bSelfContext=False"); break;
            case "weight-default": Native(root, "AnimGraphNode_MultiWayBlend_0", "DefaultValue=\"0.0\"", "DefaultValue=\"1.0\""); break;
            case "explicit-time": Native(root, "AnimGraphNode_SequenceEvaluator_0", "DefaultValue=\"0.000000\"", "DefaultValue=\"0.010000\""); break;
            case "native-policy": Native(root, "AnimGraphNode_MultiWayBlend_0", "Node=(Poses=", "Node=(bNormalizeAlpha=False,Poses="); break;
            case "native-asset": Native(root, "AnimGraphNode_SequenceEvaluator_0", "ALS_N_Pose.ALS_N_Pose", "ALS_CLF_Pose.ALS_CLF_Pose"); break;
            case "desired-default": Settings(root, "MultiWayBlend_0")["desiredAlphas"]![0] = 1; break;
            case "normalize": Settings(root, "MultiWayBlend_0")["bNormalizeAlpha"] = false; break;
            case "additive": Settings(root, "MultiWayBlend_0")["bAdditiveNode"] = true; break;
            case "scale-bias": Settings(root, "MultiWayBlend_0")["alphaScaleBias"]!["bias"] = .5; break;
            case "teleport": Settings(root, "SequenceEvaluator_0")["bTeleportToExplicitTime"] = false; break;
            case "loop": Settings(root, "SequenceEvaluator_0")["bShouldLoop"] = false; break;
            case "reinit": Settings(root, "SequenceEvaluator_0")["reinitializationBehavior"] = "StartPosition"; break;
            case "sync": Settings(root, "SequenceEvaluator_0")["method"] = "Graph"; break;
            case "frame-mode": Settings(root, "SequenceEvaluator_0")["bUseExplicitFrame"] = true; break;
            case "callback": Settings(root, "SequenceEvaluator_0")["initialUpdateFunction"]!["functionName"] = "InitializeEvaluator"; break;
            case "summary": Inventory(root, "SequenceEvaluator_0")["teleport"] = false; break;
            case "extra-field": Settings(root, "MultiWayBlend_0")["unimplementedBlendPolicy"] = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        Assert.ThrowsAny<Exception>(() => Compile(root.ToJsonString()));
    }

    [Theory]
    [InlineData("skeleton")]
    [InlineData("asset-id")]
    [InlineData("array-identity")]
    [InlineData("duration")]
    [InlineData("root-motion")]
    [InlineData("additive")]
    [InlineData("curve")]
    [InlineData("events")]
    [InlineData("missing-asset")]
    public void RejectsManifestChangesRatherThanSilentlySamplingAnotherAsset(string mutation)
    {
        var original = AnimationSet.Value;
        var set = original with { Animations = (AlsAnimationDefinition[])original.Animations.Clone() };
        var index = Array.FindIndex(set.Animations, a => a.Name == "ALS_N_Pose");
        var asset = set.Animations[index];
        set.Animations[index] = mutation switch
        {
            "skeleton" => asset with { SkeletonId = int.MaxValue },
            "asset-id" => asset with { StableId = new string('0', 40) },
            "array-identity" => asset with { Id = 0 },
            "duration" => asset with { PlayLength = 1 },
            "root-motion" => asset with { RootMotionEnabled = true },
            "additive" => asset with { AdditiveType = 1 },
            "curve" => asset with { Curves = original.Animations.First(a => a.Curves.Length > 0).Curves },
            "events" => asset with { Timeline = original.Animations.First(a => a.Timeline.Length > 0).Timeline },
            "missing-asset" => asset with { ObjectPath = asset.ObjectPath + "_Missing" },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };
        Assert.ThrowsAny<Exception>(() => Compile(Read(), set));
    }

    private static AlsBasePosesDefinition Compile(string json, AlsAnimationSetDefinition? set = null)
    {
        set ??= AnimationSet.Value;
        var skeleton = Array.FindIndex(set.Skeletons, s => s.AssetId == "b5b52715012cad50bf7a625ddf01e4335bb4fcf0");
        return AlsBasePosesCompiler.Compile(json, set, skeleton);
    }
    private static JsonNode Graph(JsonNode root) => root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == "BasePoses")!;
    private static JsonNode Inventory(JsonNode root, string name) => root["compiledNodeInventory"]!.AsArray()
        .Single(n => n!["path"]!.GetValue<string>().EndsWith(":BasePoses.AnimGraphNode_" + name, StringComparison.Ordinal))!;
    private static JsonNode Settings(JsonNode root, string name) => Inventory(root, name)["properties"]!["Node"]!;
    private static void Native(JsonNode root, string name, string before, string after)
    {
        var graph = Graph(root); var text = graph["nativeText"]!.GetValue<string>().Replace("\r", "", StringComparison.Ordinal);
        var expression = new Regex(@"(?ms)^   Begin Object Name=""" + Regex.Escape(name) + @"""[^\n]*\n.*?^   End Object");
        var matches = expression.Matches(text); Assert.Single(matches.Cast<Match>());
        Assert.Contains(before, matches[0].Value, StringComparison.Ordinal);
        graph["nativeText"] = expression.Replace(text, m => m.Value.Replace(before, after, StringComparison.Ordinal), 1);
    }
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config/v4_layering_inputs.json"));
}
