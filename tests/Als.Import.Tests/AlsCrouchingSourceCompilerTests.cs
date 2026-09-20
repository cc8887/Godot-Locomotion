using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsCrouchingSourceCompilerTests
{
    [Fact]
    public void AppendsAllBakedCrouchingIdentitiesWithoutCollapsingSharedAssets()
    {
        var root = Read(); var set = P3RepositoryFixtures.LoadAnimationSet(); var source = Compile(root, set);
        var players = source.RuntimePlayers.Where(p => p.Domain == AlsLocomotionSourceDomain.Crouching).ToArray();
        Assert.Equal(Enumerable.Range(43, 13), players.Select(p => p.PlayerId));
        Assert.Equal(new[] { 105, 113, 116, 120, 121, 134, 135, 136, 137, 138, 139, 141, 142 }, players.Select(p => p.CompiledNodeIndex));
        Assert.Equal(new[] { 65, 66, 67, 68, 69, 70, 75, 76, 77, 78, 79, 80, 81 }, players.Select(p => p.SampleStart));
        Assert.Equal(17, players.Sum(p => p.SampleCount));
        Assert.Equal(8, players.Count(p => p.Kind == AlsLocomotionSourceKind.Sequence));
        Assert.Equal(4, players.Count(p => p.Kind == AlsLocomotionSourceKind.TeleportEvaluator));
        var walking = players.Where(p => p.PlayRateInput == AlsSourceRateInput.CrouchingPlayRate).ToArray();
        Assert.Equal(new[] { "ALS_CLF_Walk_F", "ALS_CLF_Walk_B", "ALS_CLF_Walk_L", "ALS_CLF_Walk_R", "ALS_CRF_Walk_R", "ALS_CRF_Walk_L" },
            walking.Select(p => set.Animations[source.RuntimeSamples[p.SampleStart].AnimationId].Name));
        Assert.All(walking, p => { Assert.Equal(0, p.SyncGroupId); Assert.True(p.Loop); Assert.Equal(1, p.PlayRateBasis); });
        Assert.All(players.Where(p => p.Kind == AlsLocomotionSourceKind.TeleportEvaluator), p =>
        { Assert.Equal(0, p.StartPosition); Assert.Equal(0, p.DefaultPlayRate); Assert.Equal(-1, p.SyncGroupId); });
        var lean = Assert.Single(players, p => p.Kind == AlsLocomotionSourceKind.BlendSpace);
        var standingLean = source.RuntimePlayers.Single(p => p.Domain == AlsLocomotionSourceDomain.Cycle && p.InputX == AlsSourceAxisInput.LeanLeftRight);
        Assert.Equal(5, lean.SampleCount); Assert.NotEqual(standingLean.PlayerId, lean.PlayerId);
        Assert.Equal(source.RuntimeSyncPlayers[standingLean.PlayerId].AssetId, source.RuntimeSyncPlayers[lean.PlayerId].AssetId);
        Assert.Equal(AlsSourceAxisInput.LeanForwardBack, lean.InputY);
        var walkPose = set.Animations.Single(a => a.Name == "ALS_CLF_WalkPose").Id;
        var poses = source.RuntimeSamples.Where(s => s.AnimationId == walkPose).ToArray();
        Assert.Equal(3, poses.Length); Assert.Equal(3, poses.Select(s => s.PlayerId).Distinct().Count());
        Pin(Node(root, 120), "ExplicitTime")["value"] = "0.01";
        var changed = Compile(root, set);
        Assert.Equal(.01f, changed.RuntimePlayers[46].StartPosition); Assert.Equal(0, changed.RuntimePlayers[47].StartPosition);
        Assert.Equal(source.RuntimePlayers.Take(43), changed.RuntimePlayers.Take(43));
        Assert.NotEqual(source.RuntimeStamp, changed.RuntimeStamp);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("missing")]
    [InlineData("owner")]
    [InlineData("alias")]
    [InlineData("group")]
    [InlineData("method")]
    [InlineData("lifecycle")]
    [InlineData("rate-clamp")]
    [InlineData("rate-variable")]
    [InlineData("rate-member")]
    [InlineData("rate-self")]
    [InlineData("rotate-loop")]
    [InlineData("rotate-asset")]
    [InlineData("teleport")]
    [InlineData("time")]
    [InlineData("dynamic-time")]
    [InlineData("extra-input")]
    [InlineData("asset-rate")]
    [InlineData("lean-axis")]
    [InlineData("lean-sample")]
    [InlineData("lean-mirror")]
    [InlineData("lean-filter")]
    public void RejectsMissingOrUnsupportedCrouchingSourceSemantics(string mutation)
    {
        var root = Read(); var node = Node(root, 135); var data = node["properties"]!["Node"]!;
        var lean = Node(root, 134); var evaluator = Node(root, 120); var rotate = Node(root, 113);
        switch (mutation)
        {
            case "schema": root.AsObject().Remove("groundedSourceSchemaVersion"); break;
            case "missing": node["class"] = "Unsupported"; break;
            case "owner": root["bakedMachines"]!.AsArray().Single(m => m!["machineName"]!.GetValue<string>() == "(CLF) Locomotion Cycles")!["states"]![0]!["playerNodeIndices"]![0] = 199; break;
            case "alias": node["compiledNodeIndex"] = 134; break;
            case "group": data["groupName"] = "None"; break;
            case "method": data["method"] = "DoNotSync"; break;
            case "lifecycle": data["becomeRelevantFunction"]!["functionName"] = "Other"; break;
            case "rate-clamp": data["playRateScaleBiasClampConstants"]!["bClampResult"] = true; break;
            case "rate-variable": Pin(node, "PlayRate")["links"]![0]!["pin"] = "StandingPlayRate"; break;
            case "rate-member": Getter(root, node, "PlayRate")["properties"]!["VariableReference"]!["memberName"] = "StandingPlayRate"; break;
            case "rate-self": Getter(root, node, "PlayRate")["properties"]!["VariableReference"]!["bSelfContext"] = false; break;
            case "rotate-loop": Pin(rotate, "bLoopAnimation")["links"]![0]!["pin"] = "Rotate_R"; break;
            case "rotate-asset": rotate["assetObjectPath"] = "Other"; break;
            case "teleport": evaluator["properties"]!["Node"]!["bTeleportToExplicitTime"] = false; break;
            case "time": Pin(evaluator, "ExplicitTime")["value"] = "NaN"; break;
            case "dynamic-time": Pin(evaluator, "ExplicitTime")["links"]!.AsArray().Add(new JsonObject { ["node"] = "Other", ["pin"] = "Time" }); break;
            case "extra-input": var pin = Pin(node, "PlayRate").DeepClone(); pin["name"] = "StartPosition"; node["pins"]!.AsArray().Add(pin); break;
            case "asset-rate": node["assetRateScale"] = 2; break;
            case "lean-axis": Pin(lean, "X")["links"]![0]!["pin"] = "LeanAmount_FB_wrong"; break;
            case "lean-sample": lean["runtimePlayer"]!["samples"]![0]!["sourceIndex"] = 1; break;
            case "lean-mirror": lean["runtimePlayer"]!["samples"]![0]!["bMirror"] = true; break;
            case "lean-filter": lean["runtimePlayer"]!["TargetWeightInterpolationSpeedPerSec"] = 1; break;
        }
        Assert.Throws<AlsCompilationException>(() => Compile(root, P3RepositoryFixtures.LoadAnimationSet()));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(.75f)]
    [InlineData(2f)]
    [InlineData(-1f)]
    public void CrouchingRateDoesNotBorrowStandingRate(float rate)
    {
        var source = Compile(Read(), P3RepositoryFixtures.LoadAnimationSet()); var view = source.CreateCoreView();
        var players = view.Players.ToArray().Where(p => p.PlayRateInput == AlsSourceRateInput.CrouchingPlayRate).ToArray();
        var updates = players.Select((p, i) => new AlsLocomotionSourceUpdate(p.PlayerId, 1, 0, 1, i, 1)).ToArray();
        var samples = players.Select(p => new AlsLocomotionSampleUpdate(p.SampleStart, 1, 1)).ToArray();
        var ticks = new AlsAssetSyncPlayer[6]; var sampleTicks = new AlsAssetSyncSample[6]; var groups = new int[6];
        Assert.True(AlsLocomotionSourceRuntime.TryBuildTicks(view, view.Stamp, updates, samples, 3.7f,
            ticks, sampleTicks, groups, out var failure, crouchingPlayRate: rate), failure.ToString());
        Assert.All(ticks, tick => Assert.Equal(rate, tick.PlayRate)); Assert.All(groups, group => Assert.Equal(0, group));
        long allocated = -1; Exception? workerError = null;
        // Isolate the hot-path measurement from the concurrent test-runner thread.
        var worker = new Thread(() =>
        {
            try
            {
                for (var i = 0; i < 100; i++) Build();
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 2000; i++) Build();
                allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            catch (Exception error) { workerError = error; }
        }) { IsBackground = true };
        worker.Start(); Assert.True(worker.Join(TimeSpan.FromSeconds(30)));
        Assert.Null(workerError); Assert.Equal(0, allocated);
        void Build()
        {
            var borrowed = source.CreateCoreView();
            if (!AlsLocomotionSourceRuntime.TryBuildTicks(borrowed, borrowed.Stamp, updates, samples, 3.7f,
                ticks, sampleTicks, groups, out _, crouchingPlayRate: rate)) throw new InvalidOperationException();
        }
    }

    [Fact]
    public void ConnectedCrouchingRateOverridesSerializedDefaultBeforeApplyingBasis()
    {
        var source = Compile(Read(), P3RepositoryFixtures.LoadAnimationSet()); var view = source.CreateCoreView();
        var players = view.Players.ToArray(); var player = players.First(p => p.PlayRateInput == AlsSourceRateInput.CrouchingPlayRate);
        players[player.PlayerId] = player with { DefaultPlayRate = 7, PlayRateBasis = 2 };
        var changed = new AlsLocomotionSourceView(view.Stamp, players, view.Samples, view.SyncPlayers, view.GroupIds, view.Sequences, view.Markers);
        var ticks = new AlsAssetSyncPlayer[1];
        Assert.True(AlsLocomotionSourceRuntime.TryBuildTicks(changed, view.Stamp,
            [new(player.PlayerId, 1, 0, 1, 0, 1)], [new(player.SampleStart, 1, 1)], 9,
            ticks, new AlsAssetSyncSample[1], new int[1], out _, crouchingPlayRate: 1.5f));
        Assert.Equal(.75f, ticks[0].PlayRate);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("nan")]
    [InlineData("infinity")]
    [InlineData("domain")]
    [InlineData("group")]
    [InlineData("kind")]
    [InlineData("axis")]
    public void InvalidLateCrouchingInputDoesNotPublishEarlierStandingTick(string mutation)
    {
        var source = Compile(Read(), P3RepositoryFixtures.LoadAnimationSet()); var view = source.CreateCoreView(); var players = view.Players.ToArray();
        var standing = players.Single(p => p.Domain == AlsLocomotionSourceDomain.Cycle && p.Kind == AlsLocomotionSourceKind.Sequence && p.PlayRateBasis == 1);
        var crouching = players.First(p => p.PlayRateInput == AlsSourceRateInput.CrouchingPlayRate);
        float? rate = mutation == "missing" ? null : mutation == "nan" ? float.NaN : mutation == "infinity" ? float.PositiveInfinity : 1;
        if (mutation == "domain") crouching = crouching with { Domain = AlsLocomotionSourceDomain.Cycle };
        if (mutation == "group") crouching = crouching with { SyncGroupId = -1 };
        if (mutation == "kind") crouching = crouching with { Kind = AlsLocomotionSourceKind.BlendSpace };
        if (mutation == "axis") crouching = crouching with { InputX = AlsSourceAxisInput.StrideBlend };
        players[crouching.PlayerId] = crouching;
        var changed = new AlsLocomotionSourceView(view.Stamp, players, view.Samples, view.SyncPlayers, view.GroupIds, view.Sequences, view.Markers);
        var output = new AlsAssetSyncPlayer[2]; var samples = new AlsAssetSyncSample[2]; int[] groups = [91, 92];
        Assert.False(AlsLocomotionSourceRuntime.TryBuildTicks(changed, view.Stamp,
            [new(standing.PlayerId, 1, 0, 1, 0, 1), new(crouching.PlayerId, 1, 0, 1, 1, 1)],
            [new(standing.SampleStart, 1, 1), new(crouching.SampleStart, 1, 1)], 1, output, samples, groups, out var failure, crouchingPlayRate: rate));
        Assert.Equal(mutation is "nan" or "infinity" ? AlsP5FailureCode.NonFiniteInput : AlsP5FailureCode.InvalidBinding, failure);
        Assert.All(output, tick => Assert.Equal(default, tick)); Assert.All(samples, sample => Assert.Equal(default, sample));
        Assert.Equal(new[] { 91, 92 }, groups);
    }

    private static JsonNode Getter(JsonNode root, JsonNode node, string pin) => root["graphs"]!.AsArray()
        .Single(g => g!["nodes"]!.AsArray().Contains(node))!["nodes"]!.AsArray()
        .Single(n => n!["name"]!.GetValue<string>() == Pin(node, pin)["links"]![0]!["node"]!.GetValue<string>())!;
    private static JsonNode Node(JsonNode root, int index) => root["graphs"]!.AsArray().SelectMany(g => g!["nodes"]!.AsArray())
        .Single(n => n!["compiledNodeIndex"]?.GetValue<int>() == index)!;
    private static JsonNode Pin(JsonNode node, string name) => node["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!;
    private static JsonNode Read() => JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "v4_locomotion_source_graph.json")))!;
    private static AlsLocomotionSourceProfile Compile(JsonNode root, AlsAnimationSetDefinition set) =>
        AlsLocomotionSourceCompiler.Compile(root.ToJsonString(), set, AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), set).SkeletonId);
}
