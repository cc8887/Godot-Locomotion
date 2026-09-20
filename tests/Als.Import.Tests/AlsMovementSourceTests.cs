using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsMovementSourceTests
{
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config/v4_main_movement_graph.json"));
    private static AlsLocomotionSourceProfile Compile(string json)
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        return AlsLocomotionSourceCompiler.CompileWithMovement(json, set,
            AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), set).SkeletonId);
    }

    [Fact]
    public void LandingProfileRetainsPoseOrderAndMeshAdditiveBase()
    {
        var json = Read(); var set = P3RepositoryFixtures.LoadAnimationSet();
        var cache = File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config/v4_pose_cache_graph.json"));
        var sources = Compile(json); var profile = AlsLandingPoseCompiler.Compile(json, cache, sources, set);
        Assert.Equal(new[] { 291, 290, 297, 298 }, new[] { profile.Light, profile.Heavy, profile.MovingLight, profile.MovingHeavy }
            .Select(id => sources.Players[id].CompiledNodeIndex));
        Assert.Equal("ALS_N_Pose", set.Animations[profile.AdditiveBaseAnimationId].Name);
        Assert.True(profile.GroundedReadNodeIndex >= 0);
    }

    [Theory]
    [InlineData("range")] [InlineData("curve")] [InlineData("additive")] [InlineData("cache")] [InlineData("speed")]
    public void RejectsChangedLandingPoseContracts(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        var graph = root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == "Land Movement")!;
        var nodes = graph["nodes"]!.AsArray();
        if (mutation == "range") nodes.Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_TwoWayBlend")!["properties"]!["BlendNode"]!["alphaScaleBiasClamp"]!["outRange"]!["max"] = 1;
        if (mutation == "curve") nodes.Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_ModifyCurve")!["pins"]![1]!["value"] = "0";
        if (mutation == "additive") nodes.Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_ApplyMeshSpaceAdditive")!["properties"]!["Node"]!["bRootSpaceAdditive"] = true;
        if (mutation == "cache") nodes.Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_UseCachedPose")!["properties"]!["NameOfCache"] = "Other";
        if (mutation == "speed") nodes.Single(n => n!["class"]!.GetValue<string>() == "K2Node_VariableGet")!["properties"]!["VariableReference"]!["memberName"] = "Speed";
        var json = root.ToJsonString(); var sources = Compile(json);
        Assert.Throws<FormatException>(() => AlsLandingPoseCompiler.Compile(json,
            File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config/v4_pose_cache_graph.json")), sources, P3RepositoryFixtures.LoadAnimationSet()));
    }

    [Fact]
    public void AppendsAllOuterSourcesWithoutAliasingSharedAssets()
    {
        var json = Read(); var profile = Compile(json); var set = P3RepositoryFixtures.LoadAnimationSet();
        var jump = AlsLocomotionSourceCompiler.CompileWithJump(json, set, profile.SkeletonId);
        Assert.Equal(75, profile.Players.Length); Assert.Equal(109, profile.Samples.Length);
        Assert.Equal(jump.RuntimePlayers, profile.RuntimePlayers.Take(62));
        Assert.Equal(jump.RuntimeSamples, profile.RuntimeSamples.Take(88));
        Assert.Equal(new[] { "Fall", "Land" }, profile.SyncGroups.Skip(6));
        var outer = profile.Players.Skip(62).ToArray();
        Assert.All(outer, p => Assert.Equal(AlsLocomotionSourceDomain.MainMovement, p.Domain));
        Assert.Equal(new[] { 251, 252, 253, 257, 258, 260, 282, 285, 286, 290, 291, 297, 298 }, outer.Select(p => p.CompiledNodeIndex));
        Assert.Equal(4, outer.Count(p => p.Kind == AlsLocomotionSourceKind.TeleportEvaluator));
        Assert.Equal(new[] { 1.75f, 1.5f }, outer.TakeLast(2).Select(p => p.DefaultPlayRate));
        Assert.Equal(profile.Samples[outer[3].SampleStart].AnimationId, profile.Samples[outer[7].SampleStart].AnimationId);
        Assert.NotEqual(outer[3].PlayerId, outer[7].PlayerId);
        Assert.NotEqual(outer[5].SampleStart, outer[6].SampleStart);
    }

    [Theory]
    [InlineData("owner")] [InlineData("rate")] [InlineData("loop")] [InlineData("group")]
    [InlineData("evaluator")] [InlineData("time")] [InlineData("lean-input")] [InlineData("lean-sample")]
    public void RejectsChangedOuterSourceContracts(string mutation)
    {
        var root = JsonNode.Parse(Read())!; var graphs = root["graphs"]!.AsArray();
        var fall = graphs.Single(g => g!["name"]!.GetValue<string>() == "Fall")!;
        var seq = fall["nodes"]!.AsArray().Single(n => n!["name"]!.GetValue<string>() == "AnimGraphNode_SequencePlayer_0")!;
        var evaluator = fall["nodes"]!.AsArray().Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_SequenceEvaluator" && n["compiledNodeIndex"]!.GetValue<int>() == 257)!;
        var lean = fall["nodes"]!.AsArray().Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_BlendSpacePlayer")!;
        if (mutation == "owner") root["bakedMachines"]!.AsArray().Single(m => m!["machineName"]!.GetValue<string>() == "Main Movement States")!["states"]![1]!["playerNodeIndices"]![0] = 290;
        if (mutation == "rate") seq["properties"]!["Node"]!["playRate"] = 2;
        if (mutation == "loop") seq["properties"]!["Node"]!["bLoopAnimation"] = false;
        if (mutation == "group") seq["properties"]!["Node"]!["groupName"] = "Jump";
        if (mutation == "evaluator") evaluator["properties"]!["Node"]!["bTeleportToExplicitTime"] = false;
        if (mutation == "time") evaluator["pins"]![0]!["value"] = "0.01";
        if (mutation == "lean-input") fall["nodes"]!.AsArray().Single(n => n!["name"]!.GetValue<string>() == "K2Node_VariableGet_0")!["properties"]!["VariableReference"]!["memberName"] = "GroundedLean";
        if (mutation == "lean-sample") lean["runtimePlayer"]!["samples"]![0]!["rateScale"] = 2;
        Assert.Throws<AlsCompilationException>(() => Compile(root.ToJsonString()));
    }
}
