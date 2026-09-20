using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsStandingSprintCompilerTests
{
    [Fact]
    public void PinsOverrideStructDefaultsAndCompiledSourceRetainsTheBranch()
    {
        var root = Read(); var profile = Compile(root);
        Assert.Equal(new(.3f, .2f, AlsTransitionBlend.Cubic), profile.Blend);
        Assert.Equal("Mask_Sprint", profile.MaskCurveName);
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var locomotion = AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), set);
        var source = AlsLocomotionSourceCompiler.Compile(root.ToJsonString(), set, locomotion.SkeletonId);
        Assert.Equal(profile, source.Sprint);
        var oldDigest = source.Digest;
        Pin(Node(root, "AnimGraphNode_BlendListByEnum_1"), "BlendTime_0")["value"] = "0.4";
        source = AlsLocomotionSourceCompiler.Compile(root.ToJsonString(), set, locomotion.SkeletonId);
        Assert.Equal(.4f, source.Sprint.Blend.FirstSeconds); Assert.NotEqual(oldDigest, source.Digest);
    }

    [Theory]
    [InlineData("missing-runtime")]
    [InlineData("alpha-type")]
    [InlineData("mask-name")]
    [InlineData("mask-input")]
    [InlineData("reset")]
    [InlineData("always-update")]
    [InlineData("clamp")]
    [InlineData("interp")]
    [InlineData("mode")]
    [InlineData("update-mode")]
    [InlineData("pose-order")]
    [InlineData("gait")]
    [InlineData("negative-time")]
    [InlineData("nan-time")]
    public void UnsupportedBranchCannotSilentlyUseAParameterApproximation(string mutation)
    {
        var root = Read(); var mask = Node(root, "AnimGraphNode_TwoWayBlend_2");
        var runtime = mask["properties"]!["BlendNode"]!;
        var blend = Node(root, "AnimGraphNode_BlendListByEnum_1");
        if (mutation == "missing-runtime") mask["properties"]!.AsObject().Remove("BlendNode");
        if (mutation == "alpha-type") runtime["alphaInputType"] = "Float";
        if (mutation == "mask-name") Pin(mask, "AlphaCurveName")["value"] = "OtherMask";
        if (mutation == "mask-input") Pin(mask, "AlphaCurveName")["links"]!.AsArray().Add(new JsonObject { ["node"] = "K2Node_VariableGet_9", ["pin"] = "Gait" });
        if (mutation == "reset") runtime["bResetChildOnActivation"] = true;
        if (mutation == "always-update") runtime["bAlwaysUpdateChildren"] = true;
        if (mutation == "clamp") runtime["alphaScaleBiasClamp"]!["clampMin"] = .2;
        if (mutation == "interp") runtime["alphaScaleBiasClamp"]!["bInterpResult"] = true;
        if (mutation == "mode") blend["properties"]!["Node"]!["transitionType"] = "Inertialization";
        if (mutation == "update-mode") blend["properties"]!["Node"]!["childUpateMode"] = "AlwaysTickChildren";
        if (mutation == "pose-order") Pin(blend, "BlendPose_0")["links"]![0]!["node"] = "AnimGraphNode_UseCachedPose_1";
        if (mutation == "gait") Node(root, "K2Node_VariableGet_9")["properties"]!["VariableReference"]!["memberName"] = "Stance";
        if (mutation == "negative-time") Pin(blend, "BlendTime_0")["value"] = "-1";
        if (mutation == "nan-time") Pin(blend, "BlendTime_0")["value"] = "NaN";
        Assert.ThrowsAny<Exception>(() => Compile(root));
    }

    private static AlsStandingSprintProfile Compile(JsonNode root)
    { using var document = JsonDocument.Parse(root.ToJsonString()); return AlsStandingSprintCompiler.Compile(document.RootElement); }
    private static JsonNode Read() => JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Find(),
        "assets", "config", "v4_locomotion_source_graph.json")))!;
    private static JsonNode Node(JsonNode root, string name) => root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == "(N) CycleBlending")!
        ["nodes"]!.AsArray().Single(n => n!["name"]!.GetValue<string>() == name)!;
    private static JsonNode Pin(JsonNode node, string name) => node["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!;
}
