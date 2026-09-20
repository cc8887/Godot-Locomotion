using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsCrouchingPoseCompilerTests
{
    private static readonly Lazy<AlsAnimationSetDefinition> Set = new(P3RepositoryFixtures.LoadAnimationSet);
    private static readonly Lazy<AlsLocomotionSourceProfile> Sources = new(() => AlsLocomotionSourceCompiler.Compile(Read(), Set.Value,
        AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), Set.Value).SkeletonId));

    [Fact]
    public void CompilesIdleSlotRotateScalingAndIndependentStopLegsFromRealPins()
    {
        var profile = Compile(Read());
        Assert.Equal(43, profile.IdlePlayerId); Assert.Equal(44, profile.RotateLeftPlayerId); Assert.Equal(45, profile.RotateRightPlayerId);
        Assert.Equal("(CLF) Locomotion Cycles", profile.CycleCache); Assert.Equal("(CLF) Turn/Rotate", profile.SlotName);
        Assert.Equal("RotationScale", profile.IdleRotationInput); Assert.Equal("RotateRate", profile.RotateRotationInput);
        Assert.Equal(new[] { "FootLock_L", "FootLock_R", "Enable_Transition" }, profile.IdleSourceOverrides.Keys);
        Assert.All(profile.IdleSourceOverrides.Values, v => Assert.Equal(1, v));
        Assert.Equal(2, profile.StopOverrides.Count); Assert.All(profile.StopOverrides.Values, v => Assert.Equal(1, v));
        Assert.Equal(new[] { 46, 47 }, profile.StopLayers.Select(l => l.PlayerId));
        var skeleton = Set.Value.Skeletons[profile.SkeletonId];
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? "l" : "r"; var mask = profile.StopLayers[i].AffectedPhysicalIds;
            Assert.Contains(skeleton.GetPhysicalBoneId("thigh_" + side), mask);
            Assert.Contains(skeleton.GetPhysicalBoneId("foot_" + side), mask);
            Assert.Contains(skeleton.GetPhysicalBoneId("ik_foot_" + side), mask);
            Assert.DoesNotContain(skeleton.GetPhysicalBoneId("pelvis"), mask);
        }
        Assert.Empty(profile.StopLayers[0].AffectedPhysicalIds.Intersect(profile.StopLayers[1].AffectedPhysicalIds));
        var root = JsonNode.Parse(Read())!;
        var modifier = Node(root, "(CLF) Not Moving", "AnimGraphNode_ModifyCurve", "Blend");
        Assert.Equal(0, modifier["properties"]!["Node"]!["curveValues"]![0]!.GetValue<float>());
        Pin(modifier, "CurveValues_0")["value"] = "0.25";
        Assert.Equal(.25f, Compile(root.ToJsonString()).IdleSourceOverrides["FootLock_L"]);
        Assert.Equal(1, profile.IdleSourceOverrides["FootLock_L"]);
    }

    [Theory]
    [InlineData("cache")] [InlineData("slot")] [InlineData("slot-update")]
    [InlineData("curve-order")] [InlineData("curve-mode")] [InlineData("rotation-input")]
    [InlineData("rotation-owner")] [InlineData("stop-branch")] [InlineData("stop-depth")]
    [InlineData("mesh-space")] [InlineData("curve-override")] [InlineData("update-order")]
    [InlineData("weight")] [InlineData("source-alias")] [InlineData("explicit-time")]
    [InlineData("baked-owner")] [InlineData("lifecycle")] [InlineData("extra-node")]
    public void RejectsUnsupportedContentOrCrossWiredSource(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        var slot = Node(root, "(CLF) Not Moving", "AnimGraphNode_Slot");
        var scale = Node(root, "(CLF) Not Moving", "AnimGraphNode_ModifyCurve", "Scale");
        var layer = Node(root, "(CLF) Stop", "AnimGraphNode_LayeredBoneBlend");
        switch (mutation)
        {
            case "cache": Node(root, "(CLF) Moving", "AnimGraphNode_UseCachedPose")["properties"]!["NameOfCache"] = "(N) Locomotion Cycles"; break;
            case "slot": slot["properties"]!["Node"]!["slotName"] = "(N) Turn/Rotate"; break;
            case "slot-update": slot["properties"]!["Node"]!["bAlwaysUpdateSourcePose"] = true; break;
            case "curve-order": Pin(scale, "SourcePose")["links"] = Pin(slot, "Source")["links"]!.DeepClone(); break;
            case "curve-mode": scale["properties"]!["Node"]!["applyMode"] = "Blend"; break;
            case "rotation-input": Pin(scale, "CurveValues_0")["links"]![0]!["pin"] = "RotateRate"; break;
            case "rotation-owner": Node(root, "(CLF) Not Moving", "K2Node_VariableGet")["properties"]!["VariableReference"]!["memberParent"] = "Other"; break;
            case "stop-branch": layer["properties"]!["Node"]!["layerSetup"]![0]!["branchFilters"]![1]!["boneName"] = "thigh_r"; break;
            case "stop-depth": layer["properties"]!["Node"]!["layerSetup"]![0]!["branchFilters"]![1]!["blendDepth"] = 1; break;
            case "mesh-space": layer["properties"]!["Node"]!["bMeshSpaceRotationBlend"] = false; break;
            case "curve-override": layer["properties"]!["Node"]!["curveBlendOption"] = "BlendByWeight"; break;
            case "update-order": layer["properties"]!["Node"]!["bUpdateBasePoseFirst"] = true; break;
            case "weight": Pin(layer, "BlendWeights_0")["value"] = "0.5"; break;
            case "source-alias": Pin(layer, "BlendPoses_1")["links"] = Pin(layer, "BlendPoses_0")["links"]!.DeepClone(); break;
            case "explicit-time": Pin(Node(root, "(CLF) Not Moving", "AnimGraphNode_SequenceEvaluator"), "ExplicitTime")["value"] = "0.1"; break;
            case "baked-owner": root["bakedMachines"]!.AsArray().Single(m => m!["machineName"]!.GetValue<string>() == "(CLF) Locomotion States")!["states"]![4]!["playerNodeIndices"]![0] = 999; break;
            case "lifecycle": layer["properties"]!["Node"]!["updateFunction"]!["functionName"] = "Other"; break;
            case "extra-node": var extra = slot.DeepClone(); extra["name"] = "DisconnectedSlot"; Graph(root, "(CLF) Not Moving")["nodes"]!.AsArray().Add(extra); break;
        }
        Assert.Throws<FormatException>(() => Compile(root.ToJsonString()));
    }

    private static AlsCrouchingPoseProfile Compile(string json) => AlsCrouchingPoseCompiler.Compile(json, Sources.Value, Set.Value);
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "v4_grounded_dependencies.json"));
    private static JsonNode Graph(JsonNode root, string state) => root["graphs"]!.AsArray().Single(g =>
        g!["name"]!.GetValue<string>() == state && g["path"]!.GetValue<string>().Contains(".(CLF) Locomotion States."))!;
    private static JsonNode Node(JsonNode root, string state, string type, string? mode = null) => Graph(root, state)["nodes"]!.AsArray()
        .Single(n => n!["class"]!.GetValue<string>() == type && (mode == null || n["properties"]!["Node"]!["applyMode"]!.GetValue<string>() == mode))!;
    private static JsonNode Pin(JsonNode node, string name) => node["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!;
}
