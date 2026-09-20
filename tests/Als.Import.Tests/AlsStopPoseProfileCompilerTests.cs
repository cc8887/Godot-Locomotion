using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsStopPoseProfileCompilerTests
{
    [Fact]
    public void CompilesAllTwelveNativePlantOccurrencesWithPinTimesAndSeparateIdentities()
    {
        var (profile, set) = Compile(Read());
        Assert.Equal(new[] { .133f, .133f, .2f, .133f, .233f, .233f }, profile.Left.Samples.Select(s => s.TimeSeconds));
        Assert.Equal(new[] { .7f, .7f, .8f, .8f, .766f, .7f }, profile.Right.Samples.Select(s => s.TimeSeconds));
        Assert.Equal(12, profile.Left.Samples.Concat(profile.Right.Samples).Select(s => s.SourceNode).Distinct().Count());
        Assert.Equal(profile.Left.Samples.Select(s => s.AnimationId), profile.Right.Samples.Select(s => s.AnimationId));
        Assert.All(profile.Left.Samples, sample => Assert.StartsWith("ALS_N_Walk_", set.Animations[sample.AnimationId].Name));
        Assert.Equal(new AlsStopLateralSelector(5, "LB", 0, 0), profile.Left.LeftSelector);
        Assert.Equal(new AlsStopLateralSelector(3, "RB", 0, .1f), profile.Left.RightSelector);
        Assert.Equal(profile.Left.LeftSelector, profile.Right.LeftSelector);
        Assert.Equal(profile.Left.RightSelector, profile.Right.RightSelector);
    }

    [Fact]
    public void PreservesMeshSpaceLegBranchesAndPostLayerFootLockWrites()
    {
        var (profile, set) = Compile(Read());
        var skeleton = set.Skeletons[profile.SkeletonId];
        Assert.True(profile.Left.MeshSpaceRotationBlend);
        Assert.True(profile.Right.MeshSpaceRotationBlend);
        Assert.Equal("FootLock_L", profile.Left.FootLockCurve);
        Assert.Equal("FootLock_R", profile.Right.FootLockCurve);
        Assert.Equal(1, profile.Left.FootLockValue);
        Assert.Equal(1, profile.Right.FootLockValue);
        Assert.Equal("Override", profile.Left.CurveBlendOption);
        Assert.Equal(new[] { skeleton.GetPhysicalBoneId("ik_foot_l"), skeleton.GetPhysicalBoneId("thigh_l") }, profile.Left.BranchRootPhysicalIds);
        Assert.Contains(skeleton.GetPhysicalBoneId("foot_l"), profile.Left.AffectedPhysicalIds);
        Assert.DoesNotContain(skeleton.GetPhysicalBoneId("pelvis"), profile.Left.AffectedPhysicalIds);
        Assert.DoesNotContain(skeleton.GetPhysicalBoneId("foot_r"), profile.Left.AffectedPhysicalIds);
        Assert.Empty(profile.Left.AffectedPhysicalIds.Intersect(profile.Right.AffectedPhysicalIds));
        Assert.Contains(skeleton.GetLogicalBoneId("VB ik_foot_l_Offset"), profile.Left.AffectedLogicalIds);
        Assert.Contains(skeleton.GetLogicalBoneId("VB ik_knee_target_l"), profile.Left.AffectedLogicalIds);
        Assert.Contains(skeleton.GetLogicalBoneId("VB ik_foot_r_Offset"), profile.Right.AffectedLogicalIds);
        Assert.Contains(skeleton.GetLogicalBoneId("VB ik_knee_target_r"), profile.Right.AffectedLogicalIds);
        // A virtual bone follows its source parent, not a target name that mentions the same foot.
        Assert.DoesNotContain(skeleton.GetLogicalBoneId("VB foot_target_l"), profile.Left.AffectedLogicalIds);
        Assert.DoesNotContain(skeleton.GetLogicalBoneId("VB foot_target_r"), profile.Right.AffectedLogicalIds);
        Assert.Empty(profile.Left.AffectedLogicalIds.Intersect(profile.Right.AffectedLogicalIds));
    }

    [Theory]
    [InlineData("badTime")]
    [InlineData("nanTime")]
    [InlineData("linkedTime")]
    [InlineData("missingAsset")]
    [InlineData("wrongDirection")]
    [InlineData("syncEvaluator")]
    [InlineData("localLayer")]
    [InlineData("wrongLeg")]
    [InlineData("wrongCurve")]
    [InlineData("hipSource")]
    [InlineData("hipEnum")]
    [InlineData("velocityOrder")]
    [InlineData("disconnectedResult")]
    [InlineData("aliasedEvaluator")]
    [InlineData("duplicateGraph")]
    public void RejectsIncompleteOrUnsupportedPlantSemantics(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        var graphs = root["graphs"]!.AsArray();
        var graph = graphs.Single(g => g!["name"]!.GetValue<string>() == "Plant Left Foot")!;
        var nodes = graph["nodes"]!.AsArray();
        JsonNode Node(string name) => nodes.Single(n => n!["name"]!.GetValue<string>() == name)!;
        JsonNode Pin(JsonNode node, string name) => node["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!;
        var evaluator = Node("AnimGraphNode_SequenceEvaluator_5");
        var time = Pin(evaluator, "ExplicitTime");
        switch (mutation)
        {
            case "badTime": time["value"] = "999"; break;
            case "nanTime": time["value"] = "NaN"; break;
            case "linkedTime": time["links"]!.AsArray().Add(new JsonObject { ["node"] = "unexpected", ["pin"] = "value" }); break;
            case "missingAsset": evaluator["properties"]!["Node"]!["sequence"] = "/missing"; break;
            case "wrongDirection": evaluator["properties"]!["Node"]!["sequence"] = Node("AnimGraphNode_SequenceEvaluator_6")["properties"]!["Node"]!["sequence"]!.DeepClone(); break;
            case "syncEvaluator": evaluator["properties"]!["Node"]!["method"] = "SyncGroup"; break;
            case "localLayer": Node("AnimGraphNode_LayeredBoneBlend_0")["properties"]!["Node"]!["bMeshSpaceRotationBlend"] = false; break;
            case "wrongLeg": Node("AnimGraphNode_LayeredBoneBlend_0")["properties"]!["Node"]!["layerSetup"]![0]!["branchFilters"]![0]!["boneName"] = "ik_foot_r"; break;
            case "wrongCurve": Node("AnimGraphNode_ModifyCurve_0")["properties"]!["Node"]!["curveNames"]![0] = "FootLock_R"; break;
            case "hipSource": Pin(Node("AnimGraphNode_BlendListByEnum_1"), "ActiveEnumValue")["links"]![0]!["pin"] = "MovementDirection"; break;
            case "hipEnum": Node("AnimGraphNode_BlendListByEnum_1")["properties"]!["VisibleEnumEntries"]![0] = "HipsDirection::NewEnumerator1"; break;
            case "velocityOrder": Pin(Node("AnimGraphNode_MultiWayBlend_4"), "DesiredAlphas_0")["links"]![0]!["pin"] = "VelocityBlend_B_wrong"; break;
            case "disconnectedResult": Pin(Node("AnimGraphNode_StateResult_0"), "Result")["links"]!.AsArray().Clear(); break;
            case "aliasedEvaluator": Pin(Node("AnimGraphNode_MultiWayBlend_4"), "Poses_1")["links"]![0]!["node"] = evaluator["name"]!.DeepClone(); break;
            case "duplicateGraph": graphs.Add(graph.DeepClone()); break;
            default: throw new InvalidOperationException(mutation);
        }
        Assert.Throws<AlsCompilationException>(() => Compile(root.ToJsonString()));
    }

    [Fact]
    public void RejectsCrossSkeletonAnimationEvenWhenPathAndNameMatch()
    {
        var (_, set) = Compile(Read());
        var id = Array.FindIndex(set.Animations, a => a.Name == "ALS_N_Walk_F");
        set.Animations[id] = set.Animations[id] with { SkeletonId = int.MaxValue };
        var locomotion = AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), P3RepositoryFixtures.LoadAnimationSet());
        Assert.Throws<AlsCompilationException>(() => AlsStopPoseProfileCompiler.Compile(Read(), set, locomotion.SkeletonId));
    }

    private static (AlsStopPoseProfile, AlsAnimationSetDefinition) Compile(string json)
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var locomotion = AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), set);
        return (AlsStopPoseProfileCompiler.Compile(json, set, locomotion.SkeletonId), set);
    }
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "v4_stop_graph.json"));
}
