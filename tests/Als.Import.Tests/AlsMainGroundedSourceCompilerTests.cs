using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsMainGroundedSourceCompilerTests
{
    [Fact]
    public void MainSourcesAppendBakedIdentitiesAndConsumeExposedPinValues()
    {
        var root = Read(); var set = P3RepositoryFixtures.LoadAnimationSet(); var original = Compile(root, set);
        var players = original.RuntimePlayers.Where(p => p.Domain == AlsLocomotionSourceDomain.MainGrounded).ToArray();
        Assert.Equal(new[] { 40, 41, 42 }, players.Select(p => p.PlayerId));
        Assert.Equal(new[] { 223, 225, 229 }, players.Select(p => p.CompiledNodeIndex));
        Assert.Equal(new[] { 62, 63, 64 }, players.Select(p => p.SampleStart));
        Assert.Equal(new[] { "ALS_N_to_CLF", "ALS_CLF_to_N", "ALS_N_LandRoll_F" },
            players.Select(p => set.Animations[original.RuntimeSamples[p.SampleStart].AnimationId].Name));
        Assert.Equal(new[] { 1.2f, 1.2f, 0f }, players.Select(p => p.DefaultPlayRate));
        Assert.Equal(new[] { 0f, 0f, 1.5f }, players.Select(p => p.StartPosition));
        Assert.All(players, p => { Assert.Equal(-1, p.SyncGroupId); Assert.Equal(1, p.SampleCount);
            Assert.Equal(AlsSourceRateInput.Constant, p.PlayRateInput); Assert.Equal(AlsSourceLoopInput.Constant, p.LoopInput); });
        Assert.Equal(AlsLocomotionSourceKind.TeleportEvaluator, players[2].Kind);
        Assert.True(players[2].Loop); Assert.All(players.Take(2), p => Assert.False(p.Loop));
        Pin(Node(root, false), "PlayRate")["value"] = "0.75";
        Pin(Node(root, true), "ExplicitTime")["value"] = "1.25";
        var changed = Compile(root, set);
        Assert.Equal(.75f, changed.RuntimePlayers[40].DefaultPlayRate);
        Assert.Equal(1.25f, changed.RuntimePlayers[42].StartPosition);
        Assert.Equal(original.RuntimePlayers.Take(40), changed.RuntimePlayers.Take(40));
        Assert.NotEqual(original.RuntimeStamp, changed.RuntimeStamp);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("scope")]
    [InlineData("missing-source")]
    [InlineData("owner")]
    [InlineData("group")]
    [InlineData("method")]
    [InlineData("lifecycle")]
    [InlineData("loop")]
    [InlineData("rate-map")]
    [InlineData("asset")]
    [InlineData("asset-rate")]
    [InlineData("linked-rate")]
    [InlineData("extra-pin")]
    [InlineData("nan-rate")]
    [InlineData("teleport")]
    [InlineData("frame")]
    [InlineData("reinitialize")]
    [InlineData("time")]
    public void RejectsMissingOrUnsupportedNativeSourceSemantics(string mutation)
    {
        var root = Read(); var node = Node(root, false); var data = node["properties"]!["Node"]!;
        var evaluator = Node(root, true); var evalData = evaluator["properties"]!["Node"]!;
        switch (mutation)
        {
            case "schema": root.AsObject().Remove("mainSourceSchemaVersion"); break;
            case "scope": root["scope"] = "Standing Cycle, Stop and Detail source graph"; break;
            case "missing-source": node["class"] = "AnimGraphNode_SequenceEvaluator"; break;
            case "owner": node["compiledNodeIndex"] = 225; break;
            case "group": data["groupName"] = "Locomotion"; break;
            case "method": data["method"] = "SyncGroup"; break;
            case "lifecycle": data["updateFunction"]!["functionName"] = "Other"; break;
            case "loop": data["bLoopAnimation"] = true; break;
            case "rate-map": data["playRateScaleBiasClampConstants"]!["bMapRange"] = true; break;
            case "asset": node["assetObjectPath"] = "Other"; break;
            case "asset-rate": node["assetRateScale"] = 2; break;
            case "linked-rate": Pin(node, "PlayRate")["links"]!.AsArray().Add(new JsonObject { ["node"] = "Other", ["pin"] = "Rate" }); break;
            case "extra-pin": var pin = Pin(node, "PlayRate").DeepClone(); pin["name"] = "StartPosition"; node["pins"]!.AsArray().Add(pin); break;
            case "nan-rate": Pin(node, "PlayRate")["value"] = "NaN"; break;
            case "teleport": evalData["bTeleportToExplicitTime"] = false; break;
            case "frame": evalData["bUseExplicitFrame"] = true; break;
            case "reinitialize": evalData["reinitializationBehavior"] = "StartPosition"; break;
            case "time": Pin(evaluator, "ExplicitTime")["value"] = "1.6"; break;
        }
        Assert.Throws<AlsCompilationException>(() => Compile(root, P3RepositoryFixtures.LoadAnimationSet()));
    }

    private static JsonNode Node(JsonNode root, bool evaluator) => root["graphs"]!.AsArray()
        .Single(g => g!["path"]!.GetValue<string>().EndsWith(evaluator ? ".AnimStateNode_5.From Roll" : ".AnimStateNode_4.(N)->(CLF) Transition", StringComparison.Ordinal))!["nodes"]!.AsArray()
        .Single(n => n!["class"]!.GetValue<string>() == (evaluator ? "AnimGraphNode_SequenceEvaluator" : "AnimGraphNode_SequencePlayer"))!;
    private static JsonNode Pin(JsonNode node, string name) => node["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!;
    private static JsonNode Read() => JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "v4_locomotion_source_graph.json")))!;
    private static AlsLocomotionSourceProfile Compile(JsonNode root, AlsAnimationSetDefinition set) =>
        AlsLocomotionSourceCompiler.Compile(root.ToJsonString(), set, AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), set).SkeletonId);
}
