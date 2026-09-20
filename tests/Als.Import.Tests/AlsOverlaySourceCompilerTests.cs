using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsOverlaySourceCompilerTests
{
    private static readonly Lazy<AlsAnimationSetDefinition> Manifest = new(P3RepositoryFixtures.LoadAnimationSet);
    private static readonly Lazy<AlsOverlaySourceProfile> Profile = new(() => Compile());
    private static string Read(string name) => AlsAimPoseCompilerTests.Read(name);
    private static AlsOverlaySourceProfile Compile(string? layer = null, string? native = null) =>
        AlsOverlaySourceCompiler.Compile(layer ?? Read("v4_layering_inputs.json"), native ?? Read("v4_overlay_inputs.json"), Manifest.Value);

    [Fact]
    public void EveryOriginalOccurrenceRetainsItsTimeAndSyncIdentity()
    {
        var profile = Profile.Value; var players = profile.Players.ToArray();
        Assert.Equal(148, players.Length); Assert.Equal(148, players.Select(p => p.CompiledIndex).Distinct().Count());
        Assert.Equal(29, profile.AnimationIds.Length); Assert.Equal(122, players.Count(p => p.Evaluator));
        Assert.Equal(12, players.Count(p => p.AimSweep));
        Assert.Equal(19, players.Count(p => p.SyncGroup == "SecondaryMotion"));
        Assert.Equal(4, players.Count(p => p.SyncGroup == "IdleAdditive"));
        Assert.Equal(3, players.Count(p => p.SyncGroup == "Locomotion" && p.SyncRole == 1));
        Assert.Equal(3, players.Count(p => !p.Evaluator && p.PlayRate == 0));
        Assert.Equal(26, profile.States.Length); Assert.Single(profile.States.ToArray(), s => s.Conduit);
        Assert.Equal(new[] { 10, 11, 12, 13, 14 }, profile.States.ToArray().Select(s => s.MachineIndex).Distinct());
        Assert.Equal(Enumerable.Range(0, 148), profile.States.ToArray().Where(s => s.Machine == "Overlay States")
            .SelectMany(s => s.Sources.ToArray()).Order());
        Assert.Contains(players.GroupBy(p => p.AnimationId), g => g.Count() > 10);
        foreach (var player in players)
        {
            Assert.Equal(player.Evaluator ? player.AimSweep ? .625f : player.ExplicitTime : .75f,
                player.ResolveTime(.75, .625));
            Assert.Equal(profile.SkeletonId, Manifest.Value.Animations[player.AnimationId].SkeletonId);
        }
    }

    [Theory]
    [InlineData("missing_source")]
    [InlineData("aliased_node")]
    [InlineData("sync_role")]
    [InlineData("teleport")]
    [InlineData("callback")]
    [InlineData("asset")]
    [InlineData("state_owner")]
    [InlineData("graph_hash")]
    [InlineData("loop")]
    [InlineData("connected_pin")]
    public void RejectsChangedSourceSemanticsEvenAfterRebindingTheOuterFile(string mutation)
    {
        var layer = JsonNode.Parse(Read("v4_layering_inputs.json"))!;
        var native = JsonNode.Parse(Read("v4_overlay_inputs.json"))!;
        var nodes = layer["compiledNodeInventory"]!.AsArray();
        var source = nodes.First(n => n!["path"]!.GetValue<string>().Contains(":OverlayLayer.", StringComparison.Ordinal) && n["assetPlayer"]!.GetValue<bool>())!;
        var policy = source["properties"]!["Node"]!;
        switch (mutation)
        {
            case "missing_source": nodes.Remove(source); break;
            case "aliased_node": nodes.Add(source.DeepClone()); break;
            case "sync_role": policy["groupRole"] = "AlwaysFollower"; break;
            case "teleport": policy["bTeleportToExplicitTime"] = false; break;
            case "callback": policy["updateFunction"]!["functionName"] = "HiddenUpdate"; break;
            case "asset": native["assets"]!.AsArray().RemoveAt(0); break;
            case "state_owner":
                native["bakedMachines"]![0]!["states"]!.AsArray().First(s => s!["playerNodeIndices"]!.AsArray().Count > 0)!["playerNodeIndices"]![0] = -1;
                break;
            case "graph_hash": native["graphHashes"]!.AsObject()[native["graphHashes"]!.AsObject().First().Key] = new string('0', 64); break;
            case "loop": policy["bShouldLoop"] = false; break;
            case "connected_pin":
                var graphPath = source["path"]!.GetValue<string>(); graphPath = graphPath[..graphPath.LastIndexOf('.')];
                var graph = layer["graphs"]!.AsArray().Single(g => g!["path"]!.GetValue<string>() == graphPath)!;
                graph["nativeText"] = graph["nativeText"]!.GetValue<string>().Replace("LinkedTo=(", "LinkedTo=(WrongNode ", StringComparison.Ordinal);
                break;
        }
        var text = layer.ToJsonString();
        native["layeringSha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        Assert.Throws<ArgumentException>(() => Compile(text, native.ToJsonString()));
    }

    [Fact]
    public void EvaluationSelectsItsAuthoredTimeWithoutAdvancingAnyClock()
    {
        var players = Profile.Value.Players.ToArray();
        var fixedPose = players.First(p => p.Evaluator && !p.AimSweep);
        var sweep = players.First(p => p.AimSweep); var player = players.First(p => !p.Evaluator);
        Assert.Equal(fixedPose.ExplicitTime, fixedPose.ResolveTime(double.NaN, double.NaN));
        Assert.Equal(.875f, sweep.ResolveTime(double.NaN, .875));
        Assert.Equal(.375f, player.ResolveTime(.375, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => sweep.ResolveTime(0, double.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => player.ResolveTime(double.NaN, 0));
        var copied = Profile.Value.Players.ToArray(); copied[0] = copied[0] with { AnimationId = -1 };
        Assert.NotEqual(copied[0], Profile.Value.Players[0]);
    }

    [Fact]
    public void PartialResourceSharingKeepsNewAssetsAndRejectsChangedOverlappingBytes()
    {
        var profile = Profile.Value; var set = Manifest.Value;
        byte[] Bytes(string path) => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", path));
        var indexText = Read("v4_overlay_source_inputs.json");
        AlsRawAnimationSourceBank Bank(string text, Func<string, byte[]> read) => AlsRawAnimationSourceCompiler.Compile(text, set,
            profile.BindingDigest, 148, 148, profile.AnimationIds, read);
        var original = Bank(indexText, Bytes);
        var aim = AlsAimSamplingCompiler.Compile(Read("v4_aim_sampling.json"), AlsAimPoseCompilerTests.Model(), set);
        var existing = AlsRawAnimationSourceCompiler.Compile(Read("v4_aim_source_inputs.json"), set, aim.BindingDigest, 7, 17, aim.AnimationIds, Bytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => original.ReuseResourcesFrom(existing));
        var shared = original.ReuseOverlappingResourcesFrom(existing); var common = new List<int>();
        foreach (var source in original.Sources)
        {
            var id = source.PoseData.Identity.AnimationId;
            var match = existing.Sources.ToArray().FirstOrDefault(s => s.PoseData.Identity.AnimationId == id);
            Assert.Same(match ?? source, shared.GetSource(id));
            if (match is not null) common.Add(id);
        }
        Assert.NotEmpty(common); Assert.True(common.Count < original.Sources.Length);
        Assert.Equal(36, shared.Sources.Length); Assert.Equal(29, shared.RootAnimationIds.Length);
        Assert.Equal(profile.BindingDigest, shared.BindingDigest); Assert.Equal(148, shared.PlayerCount);
        Assert.Same(existing.GetSkeleton(profile.SkeletonId), shared.GetSkeleton(profile.SkeletonId));
        var edited = JsonNode.Parse(indexText)!;
        var asset = edited["assets"]!.AsArray().Single(a => a!["source"]!.GetValue<string>() == set.Animations[common[0]].ObjectPath)!;
        var file = asset["file"]!.GetValue<string>(); var changedBytes = Bytes(file).Concat(new byte[] { 32 }).ToArray();
        asset["sha256"] = Convert.ToHexString(SHA256.HashData(changedBytes)).ToLowerInvariant();
        var changed = Bank(edited.ToJsonString(), path => path == file ? changedBytes : Bytes(path));
        Assert.Throws<ArgumentException>(() => changed.ReuseOverlappingResourcesFrom(existing));
    }
}
