using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsLayerBlendingCompilerTests
{
    [Fact]
    public void CompilesAllNativeBranchesAndRetainsTheCurveOnlyTail()
    {
        var definition = AlsLayerBlendingCompiler.Compile(Read());
        var nodes = definition.Nodes.ToArray();
        Assert.Equal(80, nodes.Length);
        Assert.Equal(3, nodes.Count(n => n.Kind == AlsLayerPoseKind.Input));
        Assert.Equal(11, nodes.Count(n => n.Kind == AlsLayerPoseKind.SaveCache));
        Assert.Equal(34, nodes.Count(n => n.Kind == AlsLayerPoseKind.UseCache));
        Assert.Equal(7, nodes.Count(n => n.Kind == AlsLayerPoseKind.Slot));
        Assert.Equal(6, nodes.Count(n => n.Kind == AlsLayerPoseKind.TwoWayBlend));
        Assert.Equal(751, definition.RootIndex);
        Assert.Equal(new[] { 672 }, definition.Node(definition.RootIndex).Inputs);
        var final = definition.Node(672);
        Assert.Equal(new[] { 681, 730 }, final.Inputs);
        Assert.Single(final.Filters!);
        Assert.Empty(final.Filters![0]);
        Assert.Equal(AlsLayerCurveBlendMode.Override, final.CurveBlendMode);
        var curves = definition.Node(730);
        Assert.Equal(new[] { 685, 682 }, curves.Inputs);
        Assert.Equal(AlsLayerCurveBlendMode.BlendByWeight, curves.CurveBlendMode);
        Assert.Equal(new AlsLayerBranchFilter("VB Curves", 0), Assert.Single(Assert.Single(curves.Filters!)));
    }

    [Fact]
    public void PreservesLocalArmAdditivesAndSeparateMeshAndLocalAssemblyWeights()
    {
        var definition = AlsLayerBlendingCompiler.Compile(Read());
        Assert.Equal(AlsLayerPoseKind.DynamicMeshAdditive, definition.Node(745).Kind);
        Assert.Equal(AlsLayerPoseKind.DynamicLocalAdditive, definition.Node(744).Kind);
        var left = definition.Node(692);
        var right = definition.Node(693);
        Assert.Equal(AlsLayerPoseKind.ApplyLocalAdditive, left.Kind);
        Assert.Equal(new[] { 710, 694 }, left.Inputs);
        Assert.Equal(new[] { 706, 695 }, right.Inputs);
        Assert.Equal("Arm_L_Add", Assert.Single(left.Alphas).Name);
        Assert.Equal("Arm_R_Add", Assert.Single(right.Alphas).Name);
        Assert.True(definition.Node(687).MeshSpaceRotation);
        Assert.False(definition.Node(686).MeshSpaceRotation);
        Assert.Equal("Arm_L_MS", Assert.Single(definition.Node(687).Alphas).Name);
        Assert.Equal("Arm_L_LS", Assert.Single(definition.Node(686).Alphas).Name);
        Assert.Equal("Layering_Arm_L", Assert.Single(definition.Node(711).Alphas).Name);
        Assert.Equal(AlsLayerAlphaKind.Curve, Assert.Single(definition.Node(711).Alphas).Kind);
        // Copied native nodes reuse pin GUIDs; their node names retain identity.
        Assert.Equal(new[] { 709, 692 }, definition.Node(711).Inputs);
        Assert.Equal(new[] { 705, 693 }, definition.Node(707).Inputs);
    }

    [Theory]
    [InlineData("Node=(Layer=\"BaseLayer\")", "Node=(Layer=\"OverlayLayer\")")]
    [InlineData("CacheName=\"Post Layering\"", "CacheName=\"Base Layer Input\"")]
    [InlineData("PinName=\"Base Layer Input\"", "PinName=\"Aim Input\"")]
    [InlineData("NameOfCache=\"Post Layering\"", "NameOfCache=\"Base Layer Input\"")]
    public void RejectsParentLayerMiswiringAndAimUsingTheWrongCache(string before,string after)
    {
        var root=JsonNode.Parse(Read())!;
        var parent=root["graphs"]!.AsArray().Single(g=>g!["name"]!.GetValue<string>()=="AnimGraph")!;
        var native=parent["nativeText"]!.GetValue<string>(); Assert.Contains(before,native);
        parent["nativeText"]=native.Replace(before,after,StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(()=>AlsLayerBlendingCompiler.Compile(root.ToJsonString()));
    }

    [Theory]
    [InlineData("pose-edge")]
    [InlineData("alpha-pin")]
    [InlineData("slot-policy")]
    [InlineData("two-way-policy")]
    [InlineData("old-inventory")]
    [InlineData("native-policy")]
    [InlineData("bone-mask")]
    [InlineData("cache-order")]
    [InlineData("cache-owner")]
    [InlineData("linked-curve-bone")]
    [InlineData("virtual-bone")]
    [InlineData("missing-node")]
    public void RejectsIncompatibleTopologyPoliciesAndSkeletonData(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        switch (mutation)
        {
            case "pose-edge":
                NativeNode(root, "AnimGraphNode_Root_0", s => s.Replace("LinkedTo=(AnimGraphNode_LayeredBoneBlend_1 ",
                    "LinkedTo=(AnimGraphNode_LayeredBoneBlend_10 ", StringComparison.Ordinal)); break;
            case "alpha-pin":
                NativeNode(root, "AnimGraphNode_TwoWayBlend_5", s => s.Replace("DefaultValue=\"Layering_Arm_L\"",
                    "DefaultValue=\"Layering_Arm_R\"", StringComparison.Ordinal)); break;
            case "slot-policy":
                Inventory(root, "AnimGraphNode_Slot_5")["properties"]!["Node"]!["bAlwaysUpdateSourcePose"] = true; break;
            case "two-way-policy":
                Inventory(root, "AnimGraphNode_TwoWayBlend_0")["properties"]!["BlendNode"]!["bAlwaysUpdateChildren"] = true; break;
            case "old-inventory":
                Inventory(root, "AnimGraphNode_TwoWayBlend_0")["properties"] = new JsonObject(); break;
            case "native-policy":
                NativeNode(root, "AnimGraphNode_LayeredBoneBlend_15", s => s.Replace("bMeshSpaceRotationBlend=True",
                    "bMeshSpaceRotationBlend=False", StringComparison.Ordinal)); break;
            case "bone-mask":
                Inventory(root, "AnimGraphNode_LayeredBoneBlend_15")["properties"]!["Node"]!["layerSetup"]![0]!["branchFilters"]![0]!["boneName"] = "spine_01"; break;
            case "cache-order":
                root["orderedSavedPoseNodes"]!.AsArray().Single(n => n!["root"]!.GetValue<string>() == "LayerBlending")!["compiledNodeIndices"]![0] = 743; break;
            case "cache-owner":
                Inventory(root, "AnimGraphNode_UseCachedPose_36")["cacheSourcePropertyIndex"] = 189; break;
            case "linked-curve-bone":
                Replace(root, "skeletonText", "(\"Layering_Arm_L\", ())", "(\"Layering_Arm_L\", (LinkedBones=((BoneName=\"clavicle_l\"))))"); break;
            case "virtual-bone":
                Replace(root, "skeletonText", "SourceBoneName=\"root\",TargetBoneName=\"root\",VirtualBoneName=\"VB Curves\"",
                    "SourceBoneName=\"pelvis\",TargetBoneName=\"root\",VirtualBoneName=\"VB Curves\""); break;
            case "missing-node":
                root["compiledNodeInventory"]!.AsArray().Remove(Inventory(root, "AnimGraphNode_UseCachedPose_36")); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        Assert.ThrowsAny<Exception>(() => AlsLayerBlendingCompiler.Compile(root.ToJsonString()));
    }

    private static JsonNode Inventory(JsonNode root, string name) => root["compiledNodeInventory"]!.AsArray()
        .Single(n => n!["path"]!.GetValue<string>().EndsWith(":LayerBlending." + name, StringComparison.Ordinal))!;

    private static void NativeNode(JsonNode root, string name, Func<string, string> change)
    {
        var graph = root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == "LayerBlending")!;
        var before = graph["nativeText"]!.GetValue<string>();
        var expression = new Regex(@"(?ms)^   Begin Object Name=""" + Regex.Escape(name) + @"""[^\r\n]*\r?\n.*?^   End Object");
        var matches = expression.Matches(before);
        Assert.Single(matches.Cast<Match>());
        var replacement = change(matches[0].Value);
        Assert.NotEqual(matches[0].Value, replacement);
        graph["nativeText"] = expression.Replace(before, _ => replacement, 1);
    }
    private static void Replace(JsonNode node, string field, string before, string after)
    {
        var text = node[field]!.GetValue<string>();
        Assert.Contains(before, text, StringComparison.Ordinal);
        node[field] = text.Replace(before, after, StringComparison.Ordinal);
    }
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config/v4_layering_inputs.json"));
}
