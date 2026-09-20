using System.Text.Json.Nodes;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;
using AlsP5OccurrenceSourceKind = GodotAls.Core.Contracts.AlsP5OccurrenceSourceKind;

namespace GodotAls.Import.Tests;

public sealed class AlsOverlaySharedSourceTests
{
    private sealed record Fixture(AlsAnimationSetDefinition Set, AlsLocomotionSourceProfile Movement,
        AlsOverlaySourceProfile Overlay, AlsOverlayClockDefinition Clocks, AlsOverlaySharedSourceProfile Shared,
        AlsP5CoreRuntimeBindingSnapshot Binding);
    private static readonly Lazy<Fixture> Data = new(Create);
    private static string Read(string name) => AlsAimPoseCompilerTests.Read(name);
    private static Fixture Create()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var locomotion = AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"), set);
        var pose = AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"), set, locomotion);
        var p5 = AlsP5aAnimationRuntimeProfileCompiler.Compile(Read("p5a_animation_runtime.json"), set);
        var movement = AlsLocomotionSourceCompiler.CompileWithMovement(Read("v4_main_movement_graph.json"), set, locomotion.SkeletonId);
        var overlay = AlsOverlaySourceCompiler.Compile(Read("v4_layering_inputs.json"), Read("v4_overlay_inputs.json"), set);
        var clocks = AlsOverlaySyncCompiler.Compile(Read("v4_overlay_sync_inputs.json"), Read("v4_overlay_inputs.json"), overlay, set);
        var shared = AlsOverlaySharedSourceCompiler.Compile(movement, overlay, clocks, Read("v4_overlay_inputs.json"), Read("v4_overlay_notify_inputs.json"), set);
        var inventory = AlsP5SourceInventoryCompiler.Compile(Read("v4_anim_graph_inventory.json"), set, shared.Sources);
        var layout = AlsP5OccurrenceLayoutCompiler.CompileSourceAware(locomotion, pose, p5, shared.Sources, inventory);
        return new(set, movement, overlay, clocks, shared, AlsP5CoreRuntimeBindingCompiler.CompileSourceAware(set, locomotion, pose, p5, layout, shared.Sources, inventory));
    }

    [Fact]
    public void EveryOverlayOccurrenceHasIndependentAuthorityAndMovementIdsStayStable()
    {
        var f = Data.Value; var source = f.Shared.Sources.CreateCoreView(); var layout = f.Binding.CreateSourceOccurrenceView();
        Assert.Equal(223, source.Players.Length); Assert.Equal(257, source.Samples.Length);
        Assert.True(source.Players[..75].SequenceEqual(f.Movement.CreateCoreView().Players));
        Assert.True(source.Samples[..109].SequenceEqual(f.Movement.CreateCoreView().Samples));
        Assert.True(source.SyncPlayers[..75].SequenceEqual(f.Movement.CreateCoreView().SyncPlayers));
        Assert.Equal(f.Movement.SyncGroups, f.Shared.Sources.SyncGroups.Take(f.Movement.SyncGroups.Length));
        var authorities = new HashSet<int>();
        foreach (var map in f.Shared.Overlay)
        {
            Assert.Equal(map.Source + 75, map.PlayerId); Assert.Equal(map.Source + 109, map.SampleId);
            var occurrence = layout.Mappings[map.SampleId]; var entry = layout.Entries[occurrence.OccurrenceHandleId];
            Assert.True(authorities.Add(entry.AuthorityGroupId));
            Assert.Equal(f.Overlay.Players[map.Source].CompiledIndex, occurrence.CompiledNodeIndex);
            Assert.Equal(f.Overlay.Players[map.Source].Evaluator ? AlsP5OccurrenceSourceKind.SourceEvaluator : AlsP5OccurrenceSourceKind.SourceSample, entry.SourceKind);
            Assert.DoesNotContain(occurrence.CompiledNodeIndex, f.Binding.UnboundNativeSourceIndices.ToArray());
        }
        Assert.Equal(148, authorities.Count);
        Assert.Equal(3, source.SyncPlayers.ToArray().Count(p => p.Role == AlsAssetSyncRole.AlwaysFollower));
        Assert.NotEqual(f.Movement.RuntimeStamp, source.Stamp);
        // All 29 native Overlay sequences contain no asset notifies. State-machine
        // notifications are separate identities and must not be fabricated here.
        Assert.True(source.NotifyDefinitions.SequenceEqual(f.Movement.CreateCoreView().NotifyDefinitions));
        Assert.True(source.NotifyPolicies.SequenceEqual(f.Movement.CreateCoreView().NotifyPolicies));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsMissingOrForeignOverlayNotifyData(bool missing)
    {
        var f = Data.Value; var json = JsonNode.Parse(Read("v4_overlay_notify_inputs.json"))!;
        if (missing) json["syncAssets"]!.AsArray().RemoveAt(0); else json["overlaySha256"] = "foreign";
        Assert.Throws<ArgumentException>(() => AlsOverlaySharedSourceCompiler.Compile(f.Movement, f.Overlay, f.Clocks,
            Read("v4_overlay_inputs.json"), json.ToJsonString(), f.Set));
    }

    [Fact]
    public void NativeMovementAndRifleBindingsShareOneTickAndOneNotifyTransaction()
    {
        var f = Data.Value; var binding = f.Binding.CreateCoreView(); var source = binding.Sources;
        var main = source.Players.ToArray().First(p => p.Domain == AlsLocomotionSourceDomain.Cycle && p.Kind == AlsLocomotionSourceKind.BlendSpace);
        var mainSample = source.Samples.Slice(main.SampleStart, main.SampleCount).ToArray()
            .First(s => f.Set.Animations[s.AnimationId].SyncMarkers.Length == 2 && f.Set.Animations[s.AnimationId].Timeline.Length > 0);
        var overlay = source.Players.ToArray().First(p => p.Domain == AlsLocomotionSourceDomain.Overlay &&
            f.Shared.Sources.RuntimeSyncPlayers[p.PlayerId].Role == AlsAssetSyncRole.AlwaysFollower);
        Assert.Equal(main.SyncGroupId, overlay.SyncGroupId); Assert.Equal(0, overlay.DefaultPlayRate);
        AlsLocomotionSampleUpdate[] samples = [new(overlay.SampleStart, 1, 1), new(mainSample.SampleId, 1, 1)];
        var ticks = new AlsAssetSyncPlayer[2]; var sampleTicks = new AlsAssetSyncSample[2]; var groupIds = new int[2];
        var groups = new AlsAssetSyncBatchGroupHistory[source.GroupIds.Length];
        var players = new AlsAssetPlayerHistory[2]; var sampleHistory = new AlsAssetSampleHistory[2]; var contexts = new AlsAssetPlayerTickContext[2];
        var notifyTicks = new AlsP5SourceNotifyTick[2]; var eventState = default(AlsP5SourceEventState);
        var mainTime = 0f; var overlayTime = 0f; var events = 0; var moved = false;
        for (var frame = 1; frame <= 180; frame++)
        {
            // A heavy zero-rate arm source arrives before a light movement source.
            AlsLocomotionSourceUpdate[] updates = [new(overlay.PlayerId, 1, overlayTime, 1, 0, 1), new(main.PlayerId, 1, mainTime, frame <= 90 ? .1f : .9f, 1, 1)];
            Assert.True(AlsLocomotionSourceRuntime.TryBuildTicks(source, source.Stamp, updates, samples, 1, ticks, sampleTicks, groupIds, out _));
            Assert.Equal(AlsAssetSyncRole.AlwaysFollower, ticks[0].Role);
            Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncBatch(source.GroupIds, groupIds, ticks, sampleTicks, source.Sequences, source.Markers,
                frame == 1 ? [] : groups, frame == 1 ? [] : players, frame == 1 ? [] : sampleHistory, 1f / 60, groups, players, sampleHistory, out _, contexts));
            var group = groups.Single(g => g.Group.GroupId == main.SyncGroupId).Group;
            Assert.Equal(main.PlayerId, group.LeaderPlayerId);
            mainTime = players.Single(p => p.PlayerId == main.PlayerId).Time; overlayTime = players.Single(p => p.PlayerId == overlay.PlayerId).Time;
            moved |= overlayTime > 0;
            Assert.True(AlsP5Runtime.TryBuildSourceNotifyTicks(binding, ticks, sampleTicks, players, sampleHistory, contexts, notifyTicks, out var count, out _));
            Assert.True(AlsP5Runtime.TryPrepareSourceEvents(binding, new(frame, 1, 1), 1f / 60, notifyTicks.AsSpan(0, count), 1, eventState,
                out var candidate, out var batch, out _));
            Assert.True(AlsP5Runtime.TryPrepareSourceEvents(binding, new(frame, 1, 1), 1f / 60, notifyTicks.AsSpan(0, count), 1, eventState,
                out var retry, out var repeated, out _));
            Assert.Equal(candidate, retry); Assert.Equal(batch, repeated); eventState = candidate; events += batch.Count;
        }
        Assert.True(moved, "Rifle arm did not advance with the movement leader."); Assert.True(events > 0, "No authored movement notify was dispatched.");
    }
}
