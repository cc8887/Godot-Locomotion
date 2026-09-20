using System.Text.Json.Nodes;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;
using GodotAls.Core.Sync;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsLocomotionSourceCompilerTests
{
    [Theory]
    [InlineData("cache")]
    [InlineData("pose-link")]
    [InlineData("axis")]
    [InlineData("getter")]
    [InlineData("normalize")]
    [InlineData("additive")]
    [InlineData("scale")]
    public void DirectionCacheWeightPolicyRequiresTheActualSourcePins(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        var graph = root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == "Move RF" &&
            g["path"]!.GetValue<string>().Contains(":(N) CycleBlending."))!;
        var nodes = graph["nodes"]!.AsArray();
        var blend = nodes.Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_MultiWayBlend")!;
        var poseLink = blend["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "Poses_2")!["links"]![0]!;
        var alpha = blend["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "DesiredAlphas_2")!["links"]![0]!;
        if (mutation == "cache") nodes.Single(n => n!["name"]!.GetValue<string>() == poseLink["node"]!.GetValue<string>())!["properties"]!["NameOfCache"] = "(N) LF Movement";
        if (mutation == "pose-link") poseLink["pin"] = "Other";
        if (mutation == "axis") alpha["pin"] = "VelocityBlend_R_9";
        if (mutation == "getter") nodes.Single(n => n!["name"]!.GetValue<string>() == alpha["node"]!.GetValue<string>())!["properties"]!["VariableReference"]!["bSelfContext"] = false;
        if (mutation == "normalize") blend["properties"]!["Node"]!["bNormalizeAlpha"] = false;
        if (mutation == "additive") blend["properties"]!["Node"]!["bAdditiveNode"] = true;
        if (mutation == "scale") blend["properties"]!["Node"]!["alphaScaleBias"]!["scale"] = 2;
        Assert.Throws<AlsCompilationException>(() => Compile(root.ToJsonString()));
    }

    [Fact]
    public void StandingLeavesPreserveBakedOwnershipAndDynamicInputsWithoutRenumberingExistingPlayers()
    {
        var (sources, set) = Compile(Read());
        var players = sources.RuntimePlayers.Where(p => p.Domain == AlsLocomotionSourceDomain.Standing).ToArray();
        Assert.Equal(new[] { 37, 38, 39 }, players.Select(p => p.PlayerId));
        Assert.Equal(new[] { 39, 86, 89 }, players.Select(p => p.CompiledNodeIndex));
        Assert.Equal(new[] { 59, 60, 61 }, players.Select(p => p.SampleStart));
        Assert.Equal(new[] { "ALS_N_Pose", "ALS_N_Rotate_L90", "ALS_N_Rotate_R90" },
            players.Select(p => set.Animations[sources.RuntimeSamples[p.SampleStart].AnimationId].Name));
        Assert.Equal(AlsLocomotionSourceKind.TeleportEvaluator, players[0].Kind);
        Assert.Equal(AlsSourceLoopInput.Constant, players[0].LoopInput);
        Assert.Equal(0, players[0].StartPosition);
        Assert.Equal(AlsSourceLoopInput.RotateLeft, players[1].LoopInput);
        Assert.Equal(AlsSourceLoopInput.RotateRight, players[2].LoopInput);
        Assert.All(players, p => { Assert.Equal(-1, p.SyncGroupId); Assert.Equal(1, p.SampleCount); });
        foreach (var player in players.Skip(1))
        {
            Assert.Equal(AlsLocomotionSourceKind.Sequence, player.Kind);
            Assert.Equal(AlsSourceRateInput.RotateRate, player.PlayRateInput);
            var sequence = sources.RuntimeSamples[player.SampleStart].SequenceIndex;
            Assert.Equal(2, sources.CreateCoreView().NotifyRanges[sequence].Count);
        }
        var root = JsonNode.Parse(Read())!;
        StandingNode(root, "(N) Not Moving")["pins"]!.AsArray()
            .Single(p => p!["name"]!.GetValue<string>() == "ExplicitTime")!["value"] = "0.01";
        var changed = Compile(root.ToJsonString()).Sources;
        Assert.Equal(.01f, changed.RuntimePlayers[37].StartPosition);
        Assert.NotEqual(sources.RuntimeStamp, changed.RuntimeStamp);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("rate-link")]
    [InlineData("loop-link")]
    [InlineData("missing-link")]
    [InlineData("extra-input")]
    [InlineData("getter")]
    [InlineData("baked-owner")]
    [InlineData("sync-group")]
    [InlineData("rate-clamp")]
    [InlineData("asset-rate")]
    [InlineData("idle-time")]
    [InlineData("idle-teleport")]
    public void RejectsMissingOrDifferentStandingSourceSemantics(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        var node = StandingNode(root, "(N) Rotate Left 90");
        var data = node["properties"]!["Node"]!;
        var ratePin = node["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "PlayRate")!;
        switch (mutation)
        {
            case "schema": root.AsObject().Remove("standingSourceSchemaVersion"); break;
            case "rate-link": ratePin["links"]![0]!["pin"] = "StandingPlayRate"; break;
            case "loop-link": node["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "bLoopAnimation")!["links"]![0]!["pin"] = "Rotate_R"; break;
            case "missing-link": ratePin["links"]!.AsArray().Clear(); break;
            case "extra-input":
                var extra = ratePin.DeepClone(); extra["name"] = "StartPosition"; node["pins"]!.AsArray().Add(extra); break;
            case "getter":
                var getterName = ratePin["links"]![0]!["node"]!.GetValue<string>();
                root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == "(N) Rotate Left 90")!["nodes"]!.AsArray()
                    .Single(n => n!["name"]!.GetValue<string>() == getterName)!["properties"]!["VariableReference"]!["bSelfContext"] = false;
                break;
            case "baked-owner": root["bakedMachines"]!.AsArray().Single(m => m!["machineName"]!.GetValue<string>() == "(N) Locomotion States")!["states"]!.AsArray()
                .Single(s => s!["stateName"]!.GetValue<string>() == "(N) Rotate Left 90")!["playerNodeIndices"]![0] = 89; break;
            case "sync-group": data["groupName"] = "Locomotion"; break;
            case "rate-clamp": data["playRateScaleBiasClampConstants"]!["bClampResult"] = true; break;
            case "asset-rate": node["assetRateScale"] = 2; break;
            case "idle-time": StandingNode(root, "(N) Not Moving")["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "ExplicitTime")!["value"] = "9"; break;
            case "idle-teleport": StandingNode(root, "(N) Not Moving")["properties"]!["Node"]!["bTeleportToExplicitTime"] = false; break;
        }
        Assert.Throws<AlsCompilationException>(() => Compile(root.ToJsonString()));
    }

    private static JsonNode StandingNode(JsonNode root, string graph) => root["graphs"]!.AsArray()
        .Single(g => g!["name"]!.GetValue<string>() == graph)!["nodes"]!.AsArray()
        .Single(n => n!["class"]!.GetValue<string>() is "AnimGraphNode_SequencePlayer" or "AnimGraphNode_SequenceEvaluator")!;

    [Fact]
    public void WorkerBindingsHaveSequentialUnmanagedLayoutAndNoSourceStrings()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsLocomotionSourcePlayerBinding>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsLocomotionSourceSampleBinding>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsLocomotionSourceSyncBinding>());
        Assert.Equal(LayoutKind.Sequential, typeof(AlsLocomotionSourcePlayerBinding).StructLayoutAttribute!.Value);
        Assert.Equal(LayoutKind.Sequential, typeof(AlsLocomotionSourceSampleBinding).StructLayoutAttribute!.Value);
        var (sources, _) = Compile(Read());
        Assert.Equal(sources.Players.Length, sources.RuntimePlayers.Length);
        Assert.Equal(sources.Samples.Length, sources.RuntimeSamples.Length);
        for (var i = 0; i < sources.Players.Length; i++)
        {
            var source = sources.Players[i]; var runtime = sources.RuntimePlayers[i];
            Assert.Equal((source.PlayerId, source.CompiledNodeIndex, source.Kind, source.Domain, source.SyncGroupId,
                source.StartPosition, source.DefaultPlayRate, source.PlayRateBasis, source.SampleStart, source.SampleCount),
                (runtime.PlayerId, runtime.CompiledNodeIndex, runtime.Kind, runtime.Domain, runtime.SyncGroupId,
                runtime.StartPosition, runtime.DefaultPlayRate, runtime.PlayRateBasis, runtime.SampleStart, runtime.SampleCount));
        }
        var leans = sources.RuntimePlayers.Where(p => p.InputX == AlsSourceAxisInput.LeanLeftRight).ToArray();
        Assert.Equal(2, leans.Length);
        Assert.All(leans, lean => { Assert.Equal(AlsSourceAxisInput.LeanForwardBack, lean.InputY);
            Assert.Equal(AlsSourceRateInput.Constant, lean.PlayRateInput); Assert.Equal(-1, lean.SyncGroupId); });
        var copy = sources.RuntimePlayers;
        copy[0] = copy[0] with { PlayerId = 100 };
        Assert.Equal(0, sources.RuntimePlayers[0].PlayerId);
    }

    [Fact]
    public void PreservesNativePlayerOwnershipRatherThanDeduplicatingAnimationIds()
    {
        var (sources, set) = Compile(Read());
        Assert.Equal(56, sources.Players.Length);
        Assert.Equal(82, sources.Samples.Length);
        Assert.Equal(Enumerable.Range(0, 56), sources.Players.Select(p => p.PlayerId));
        Assert.Equal(Enumerable.Range(0, 82), sources.Samples.Select(s => s.SampleId));
        Assert.Equal(56, sources.Players.Select(p => p.SourceNode).Distinct().Count());
        Assert.Equal(56, sources.Players.Select(p => p.CompiledNodeIndex).Distinct().Count());
        Assert.Equal(new[] { "Locomotion", "Pivot 1", "Pivot 2", "Run Start" }, sources.SyncGroups);
        foreach (var player in sources.Players)
        {
            var samples = sources.Samples.AsSpan(player.SampleStart, player.SampleCount).ToArray();
            Assert.All(samples, sample => Assert.Equal(player.PlayerId, sample.PlayerId));
            Assert.Equal(Enumerable.Range(0, player.SampleCount), samples.Select(s => s.SourceIndex));
        }
        var walk = set.Animations.Single(a => a.Name == "ALS_N_Walk_F").Id;
        var uses = sources.Samples.Where(s => s.AnimationId == walk).ToArray();
        Assert.Equal(3, uses.Length);
        Assert.Equal(3, uses.Select(s => s.PlayerId).Distinct().Count());
        Assert.Equal(2, uses.Count(s => sources.Players[s.PlayerId].Kind == AlsLocomotionSourceKind.TeleportEvaluator));
        var detail = sources.Players.Where(p => p.Domain == AlsLocomotionSourceDomain.Detail).ToArray();
        Assert.Equal(Enumerable.Range(0, 16), detail.Select(p => p.DetailSlot));
        Assert.Equal(4, detail.Select(p => sources.Samples[p.SampleStart].AnimationId).Distinct().Count());
        Assert.Equal(8, detail.Count(p => sources.SyncGroups[p.SyncGroupId] == "Run Start"));
    }

    [Fact]
    public void CompilesBakedCycleOrderInitialPhasesAssetRatesAndSprintImpulse()
    {
        var (sources, set) = Compile(Read());
        var cycle = sources.Players.Where(p => p.Domain == AlsLocomotionSourceDomain.Cycle).ToArray();
        Assert.Equal(new[] { 199, 200, 201, 202, 203, 205, 206, 207, 209 }, cycle.Select(p => p.CompiledNodeIndex));
        Assert.Equal(new[] { .93f, .33f, .2f, .2f, .2f, 0f, 0f, 0f, .2f }, cycle.Select(p => p.StartPosition));
        Assert.Equal(7, cycle.Count(p => p.Kind == AlsLocomotionSourceKind.BlendSpace));
        Assert.Equal(31, cycle.Sum(p => p.SampleCount));
        var impulse = cycle.Single(p => p.SampleCount == 1 && set.Animations[sources.Samples[p.SampleStart].AnimationId].Name == "ALS_N_Sprint_F_Impulse");
        Assert.Equal(.833f, impulse.PlayRateBasis);
        Assert.Equal("StandingPlayRate", impulse.PlayRateInput);
        var poses = sources.Samples.Where(s => set.Animations[s.AnimationId].Name.StartsWith("ALS_N_RunPose_", StringComparison.Ordinal)).ToArray();
        Assert.Equal(6, poses.Length);
        Assert.All(poses, p => Assert.Equal(.0416f, p.AssetRateScale));
        Assert.All(poses, p => Assert.InRange(p.DurationSeconds / (p.AssetRateScale * p.SampleRateScale), .8f, .802f));
        Assert.All(sources.Players.Where(p => p.Domain == AlsLocomotionSourceDomain.Stop), p =>
        {
            Assert.Equal(-1, p.SyncGroupId); Assert.True(p.Loop);
            Assert.Equal(0, p.DefaultPlayRate); Assert.Equal(1, p.SampleCount);
        });
    }

    [Fact]
    public void SnapshotIsDefensiveAndDigestIncludesSourceProvenanceAndNativeRate()
    {
        var (sources, _) = Compile(Read());
        Assert.Equal(64, sources.Digest.Length);
        Assert.Equal(sources.Digest, Compile(Read()).Sources.Digest);
        var players = sources.Players; var samples = sources.Samples; var groups = sources.SyncGroups;
        players[0] = players[0] with { SyncGroupId = -1 };
        samples[0] = samples[0] with { AnimationId = -1 };
        groups[0] = "bad";
        Assert.Equal(0, sources.Players[0].SyncGroupId);
        Assert.True(sources.Samples[0].AnimationId >= 0);
        Assert.Equal("Locomotion", sources.SyncGroups[0]);
        var root = JsonNode.Parse(Read())!;
        var node = Blend(root);
        var animationPath = node["runtimePlayer"]!["samples"]![3]!["animation"]!.GetValue<string>();
        foreach (var sample in root["graphs"]!.AsArray().SelectMany(g => g!["nodes"]!.AsArray())
            .Where(n => n!["runtimePlayer"] is not null).SelectMany(n => n!["runtimePlayer"]!["samples"]!.AsArray())
            .Where(s => s!["animation"]!.GetValue<string>() == animationPath)) sample!["assetRateScale"] = .05f;
        root["syncAssets"]!.AsArray().Single(a => a!["path"]!.GetValue<string>() == animationPath)!["rateScale"] = .05f;
        var changed = Compile(root.ToJsonString()).Sources;
        Assert.NotEqual(sources.SourceGraphDigest, changed.SourceGraphDigest);
        Assert.NotEqual(sources.Digest, changed.Digest);
        Assert.Equal(.05f, changed.Samples[3].AssetRateScale);
    }

    [Theory]
    [InlineData("alias")]
    [InlineData("missing")]
    [InlineData("sample-order")]
    [InlineData("animation")]
    [InlineData("length")]
    [InlineData("rate")]
    [InlineData("alias-rate")]
    [InlineData("mirror")]
    [InlineData("single-frame")]
    [InlineData("notify")]
    [InlineData("phase")]
    [InlineData("match-sync-phases")]
    [InlineData("missing-timing-flag")]
    [InlineData("length-mode")]
    [InlineData("marker-time")]
    [InlineData("marker-name")]
    [InlineData("marker-track")]
    [InlineData("marker-index")]
    [InlineData("missing-marker")]
    [InlineData("missing-sync-asset")]
    [InlineData("effective-markers")]
    [InlineData("missing-sync-schema")]
    [InlineData("input")]
    [InlineData("baked")]
    [InlineData("scope")]
    public void RejectsIncompleteOrUnsupportedSourceContracts(string mutation)
    {
        var root = JsonNode.Parse(Read())!; var node = Blend(root);
        var native = node["runtimePlayer"]!; var sample = native["samples"]![0]!;
        switch (mutation)
        {
            case "alias": node["compiledNodeIndex"] = 200; break;
            case "missing": native.AsObject().Remove("samples"); break;
            case "sample-order": sample["sourceIndex"] = 1; break;
            case "animation": sample["animation"] = "/Game/missing.missing"; break;
            case "length": sample["playLength"] = 9; break;
            case "rate": sample["assetRateScale"] = 0; break;
            case "alias-rate": native["samples"]![3]!["assetRateScale"] = .05f; break;
            case "mirror": sample["bMirror"] = true; break;
            case "single-frame": sample["bUseSingleFrameForBlending"] = true; break;
            case "notify": native["NotifyTriggerMode"] = "AllAnimations"; break;
            case "phase": native["startPosition"] = 0; break;
            case "match-sync-phases": native["bShouldMatchSyncPhases"] = true; break;
            case "missing-timing-flag": native.AsObject().Remove("bUseLegacySamplePointAnimationLengthCalculations"); break;
            case "length-mode": native["bUseLegacySamplePointAnimationLengthCalculations"] = false; break;
            case "marker-time": FirstMarker(root)["time"] = .0123f; break;
            case "marker-name": FirstMarker(root)["name"] = "Unexported"; break;
            case "marker-track": FirstMarker(root)["track"] = 99; break;
            case "marker-index": FirstMarker(root)["index"] = 99; break;
            case "missing-marker": root["syncAssets"]!.AsArray().First(a => a!["markers"]!.AsArray().Count > 0)!["markers"]!.AsArray().RemoveAt(0); break;
            case "missing-sync-asset": root["syncAssets"]!.AsArray().RemoveAt(0); break;
            case "effective-markers": native["syncMarkerNames"] = new JsonArray(); break;
            case "missing-sync-schema": root.AsObject().Remove("syncSchemaVersion"); break;
            case "input": node["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "X")!["links"]![0]!["pin"] = "WalkRunBlend"; break;
            case "baked": root["bakedMachines"]!.AsArray().Single(m => m!["machineName"]!.GetValue<string>() == "(N) Locomotion Cycles")!["states"]![0]!["playerNodeIndices"]![0] = 999; break;
            case "scope": root["scope"] = "Detail only"; break;
        }
        Assert.Throws<AlsCompilationException>(() => Compile(root.ToJsonString()));
    }

    [Fact]
    public void DuplicatePropertiesAndUnexportedAnimationsFailWithoutFallback()
    {
        Assert.Throws<AlsCompilationException>(() => Compile(Read().Insert(Read().IndexOf('{') + 1, "\"schemaVersion\":1,")));
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var id = set.Animations.Single(a => a.Name == "ALS_N_Sprint_F_Impulse").Id;
        var animations = set.Animations.ToArray();
        animations[id] = animations[id] with { ObjectPath = "/Game/removed.removed" };
        var profile = AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), set);
        Assert.Throws<AlsCompilationException>(() => AlsLocomotionSourceCompiler.Compile(Read(), set with { Animations = animations }, profile.SkeletonId));
    }

    [Fact]
    public void CompilesNativeTimingAndMarkersWithDefensiveNumericTables()
    {
        var (sources, set) = Compile(Read());
        var samples = sources.RuntimeSamples; var sequences = sources.SyncSequences; var markers = sources.SyncMarkers;
        var sync = sources.RuntimeSyncPlayers;
        Assert.Equal(56, sync.Length); Assert.Equal(45, sequences.Length); Assert.Equal(54, markers.Length);
        Assert.Equal(new[] { "", "Left", "Right" }, sources.MarkerSymbols);
        foreach (var sample in samples)
        {
            var sequence = sequences[sample.SequenceIndex];
            Assert.Equal(sample.AnimationId, sequence.AnimationId); Assert.Equal(sample.AssetRateScale, sequence.RateScale);
            Assert.Equal(sample.DurationSeconds, sequence.DurationSeconds);
        }
        foreach (var player in sources.RuntimePlayers)
        {
            var binding = sync[player.PlayerId]; Assert.Equal(player.PlayerId, binding.PlayerId);
            if (player.Kind == AlsLocomotionSourceKind.BlendSpace)
            {
                Assert.True(binding.AllowMarkers); Assert.True(binding.LegacyLength); Assert.False(binding.MatchSyncPhases);
                Assert.True(binding.AssetId >= set.Animations.Length);
                Assert.Equal(AlsBlendSpaceNotifyMode.HighestWeightedAnimation, binding.NotifyMode);
                Assert.Equal(player.InputX == AlsSourceAxisInput.LeanLeftRight ? 0UL : 6UL, binding.MarkerMask);
            }
            else Assert.Equal(samples[player.SampleStart].AnimationId, binding.AssetId);
        }
        var unbound = sequences.Where(s => !samples.Any(sample => sample.AnimationId == s.AnimationId)).ToArray();
        Assert.Empty(unbound);
        sequences[0] = sequences[0] with { RateScale = 123 }; markers[0] = new(63, 999);
        sync[0] = sync[0] with { MarkerMask = 0 }; var symbols = sources.MarkerSymbols; symbols[1] = "changed";
        Assert.NotEqual(123, sources.SyncSequences[0].RateScale); Assert.NotEqual(999, sources.SyncMarkers[0].TimeSeconds);
        Assert.Equal(6UL, sources.RuntimeSyncPlayers[0].MarkerMask); Assert.Equal("Left", sources.MarkerSymbols[1]);
    }

    [Fact]
    public void FormalSourceBindingsDriveNativeMixedSyncReplay()
    {
        var (sources, set) = Compile(Read());
        var bindings = sources.RuntimePlayers; var syncBindings = sources.RuntimeSyncPlayers; var sourceSamples = sources.RuntimeSamples;
        var sequences = sources.SyncSequences; var markers = sources.SyncMarkers; var symbols = sources.MarkerSymbols;
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "tests", "Als.Core.Tests", "Fixtures", "P3", "v4_blendspace_tick_native.json")));
        var nativeAssets = fixture.RootElement.GetProperty("assets").EnumerateArray().ToArray();
        var playerByAsset = bindings.Where(p => p.Domain == AlsLocomotionSourceDomain.Cycle).ToDictionary(p =>
            p.Kind == AlsLocomotionSourceKind.BlendSpace ? set.BlendSpaces[syncBindings[p.PlayerId].AssetId - set.Animations.Length].ObjectPath
                : set.Animations[syncBindings[p.PlayerId].AssetId].ObjectPath, p => p, StringComparer.Ordinal);
        var frames = 0;
        foreach (var trace in fixture.RootElement.GetProperty("traces").EnumerateArray())
        {
            var independent = trace.GetProperty("scenario").GetInt32() == 6;
            var group = default(AlsAssetSyncGroupHistory);
            AlsAssetPlayerHistory[] history = []; AlsAssetSampleHistory[] sampleHistory = [];
            var epochs = Enumerable.Repeat(1L, bindings.Length).ToArray();
            var frameNumber = 0;
            foreach (var frame in trace.GetProperty("frames").EnumerateArray())
            {
                var players = new List<AlsAssetSyncPlayer>(); var samples = new List<AlsAssetSyncSample>();
                var playerBySlot = new Dictionary<int, int>();
                foreach (var input in frame.GetProperty("input").EnumerateArray())
                {
                    var slot = input.GetProperty("slot").GetInt32(); var asset = nativeAssets[input.GetProperty("asset").GetInt32()];
                    var binding = playerByAsset[asset.GetProperty("path").GetString()!]; var sync = syncBindings[binding.PlayerId];
                    playerBySlot.Add(slot, binding.PlayerId);
                    var reset = input.GetProperty("reset").GetBoolean();
                    if (reset) epochs[binding.PlayerId]++;
                    var previousIndex = Array.FindIndex(history, h => h.PlayerId == binding.PlayerId && h.Epoch == epochs[binding.PlayerId]);
                    var time = previousIndex >= 0 ? history[previousIndex].Time : input.GetProperty("time").GetSingle();
                    // The native standalone Lean probe deliberately starts at .2, not its node default 0.
                    if (frameNumber == 0 && !independent) Assert.Equal(binding.StartPosition, time);
                    if (independent) Assert.Equal(-1, binding.SyncGroupId);
                    var start = samples.Count;
                    if (binding.Kind == AlsLocomotionSourceKind.BlendSpace)
                    {
                        var expected = frame.GetProperty("output").EnumerateArray().Single(o => o.GetProperty("slot").GetInt32() == slot);
                        foreach (var weight in expected.GetProperty("samples").EnumerateArray())
                        {
                            var sample = sourceSamples[binding.SampleStart + weight.GetProperty("index").GetInt32()];
                            samples.Add(new(sample.SampleId, sample.SequenceIndex, weight.GetProperty("weight").GetSingle(),
                                sample.SampleRateScale, weight.GetProperty("sampleRate").GetSingle()));
                        }
                    }
                    else
                    {
                        var sample = sourceSamples[binding.SampleStart]; samples.Add(new(sample.SampleId, sample.SequenceIndex, 1));
                    }
                    // The probe supplies node play rate and resolved weights. All asset metadata,
                    // identities, initialization values and persistent histories come from bindings/Core.
                    players.Add(new(binding.PlayerId, sync.AssetId, epochs[binding.PlayerId],
                        binding.Kind == AlsLocomotionSourceKind.BlendSpace ? AlsAssetSyncKind.BlendSpace : AlsAssetSyncKind.Sequence,
                        time, input.GetProperty("rate").GetSingle(), input.GetProperty("weight").GetSingle(), start, samples.Count - start,
                        sync.MarkerMask, binding.Loop, sync.LegacyLength, sync.MatchSyncPhases, reset));
                }
                var output = new AlsAssetPlayerHistory[players.Count]; var sampleOutput = new AlsAssetSampleHistory[samples.Count];
                var groupId = Array.IndexOf(sources.SyncGroups, "Locomotion");
                var candidate = new AlsAssetSyncGroupHistory(0, false, -1, -1, 0, -1, 0, 0, 0, 0, default, default);
                AlsP5FailureCode error;
                Assert.True(independent ? AlsSyncRuntime.TryEvaluateIndependentAssetPlayers(players.ToArray(), samples.ToArray(), sequences, markers,
                    history, sampleHistory, frame.GetProperty("delta").GetSingle(), output, sampleOutput, out error) :
                    AlsSyncRuntime.TryEvaluateAssetSyncGroup(groupId, group, players.ToArray(), samples.ToArray(), sequences, markers,
                    history, sampleHistory, frame.GetProperty("delta").GetSingle(), output, sampleOutput, out candidate, out error), error.ToString());
                var leaderSlot = frame.GetProperty("leader").GetInt32();
                Assert.Equal(leaderSlot < 0 ? -1 : playerBySlot[leaderSlot], candidate.LeaderPlayerId);
                Near(frame.GetProperty("previousRatio").GetSingle(), candidate.PreviousRatio);
                Near(frame.GetProperty("ratio").GetSingle(), candidate.Ratio);
                Assert.Equal(frame.GetProperty("markerSync").GetBoolean(), candidate.ValidMarkerMask != 0);
                if (frame.TryGetProperty("markerStart", out var startPosition)) CheckPosition(startPosition, candidate.MarkerStart);
                if (frame.TryGetProperty("markerEnd", out var endPosition)) CheckPosition(endPosition, candidate.MarkerEnd);
                foreach (var expected in frame.GetProperty("output").EnumerateArray())
                {
                    var actual = output.Single(o => o.PlayerId == playerBySlot[expected.GetProperty("slot").GetInt32()]);
                    Near(expected.GetProperty("time").GetSingle(), actual.Time);
                    Near(expected.GetProperty("previous").GetSingle(), actual.DeltaPrevious);
                    Near(expected.GetProperty("delta").GetSingle(), actual.Delta);
                    CheckRecord(expected.GetProperty("marker"), actual.Marker);
                    var index = 0;
                    foreach (var sample in expected.GetProperty("samples").EnumerateArray())
                    {
                        var result = sampleOutput[actual.SampleStart + index++];
                        Assert.Equal(sample.GetProperty("index").GetInt32(), sourceSamples[result.SampleId].SourceIndex);
                        Near(sample.GetProperty("time").GetSingle(), result.Time);
                        Near(sample.GetProperty("previous").GetSingle(), result.PreviousTime);
                        Near(sample.GetProperty("deltaPrevious").GetSingle(), result.DeltaPrevious);
                        Near(sample.GetProperty("delta").GetSingle(), result.Delta);
                        CheckRecord(sample.GetProperty("marker"), result.Marker);
                    }
                }
                group = candidate; history = output; sampleHistory = sampleOutput; frames++; frameNumber++;
            }
        }
        Assert.Equal(1344, frames);
        void CheckPosition(JsonElement row, AlsAssetMarkerPosition actual)
        {
            Assert.Equal(row.GetProperty("previous").GetString(), actual.PreviousSymbol == 0 ? "None" : symbols[actual.PreviousSymbol]);
            Assert.Equal(row.GetProperty("next").GetString(), actual.NextSymbol == 0 ? "None" : symbols[actual.NextSymbol]);
            Near(row.GetProperty("alpha").GetSingle(), actual.Alpha);
        }
        static void CheckRecord(JsonElement row, AlsAssetMarkerRecord actual)
        {
            Assert.Equal(row.GetProperty("previous").GetInt32(), actual.PreviousIndex); Assert.Equal(row.GetProperty("next").GetInt32(), actual.NextIndex);
            Near(row.GetProperty("previousDistance").GetSingle(), actual.PreviousDistance); Near(row.GetProperty("nextDistance").GetSingle(), actual.NextDistance);
        }
        static void Near(float expected, float actual) => Assert.True(MathF.Abs(expected - actual) <= .00003f, $"expected {expected:R}, actual {actual:R}");
    }

    private static JsonObject Blend(JsonNode root) => root["graphs"]!.AsArray().SelectMany(g => g!["nodes"]!.AsArray())
        .First(n => n!["runtimePlayer"] is not null)!.AsObject();
    private static JsonObject FirstMarker(JsonNode root) => root["syncAssets"]!.AsArray().First(a => a!["markers"]!.AsArray().Count > 0)!["markers"]![0]!.AsObject();
    private static (AlsLocomotionSourceProfile Sources, AlsAnimationSetDefinition Set) Compile(string json)
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var profile = AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), set);
        return (AlsLocomotionSourceCompiler.Compile(json, set, profile.SkeletonId), set);
    }
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "v4_locomotion_source_graph.json"));
}
