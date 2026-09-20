using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;
using Kind = GodotAls.Core.Contracts.AlsP5OccurrenceSourceKind;

namespace GodotAls.Import.Tests;

public sealed class AlsP5SourceRuntimeBindingTests
{
    [Fact]
    public void GraphSourceProfileKeepsTheRuntimeIdentityAndDoesNotExposeMutableArrays()
    {
        var fixture = Fixture.Create(); var snapshot = fixture.Compile();
        var profile = snapshot.SourceProfile;
        Assert.Same(fixture.Sources, profile);
        Assert.Equal(snapshot.CreateCoreView().Sources.Stamp, profile.RuntimeStamp);
        Assert.Equal(snapshot.CreateGraphBuildView().Sources.Stamp, profile.RuntimeStamp);
        Assert.Equal(snapshot.AnimationSetDefinitionDigest, profile.AnimationSetDefinitionDigest);
        var players = profile.Players; var samples = profile.Samples;
        var firstPlayer = players[0]; var firstSample = samples[0];
        var digest = snapshot.Digest;
        players[0] = firstPlayer with { StartPosition = firstPlayer.StartPosition + 5 };
        samples[0] = firstSample with { SampleRateScale = firstSample.SampleRateScale + 5 };
        Assert.Equal(firstPlayer, snapshot.SourceProfile.Players[0]);
        Assert.Equal(firstSample, snapshot.SourceProfile.Samples[0]);
        Assert.Equal(digest, snapshot.Digest);
        Assert.Equal(profile.RuntimeStamp, snapshot.CreateCoreView().Sources.Stamp);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void MainStanceTransitionsUseConstantRateIndependentClocksAndRetryableNotifications(int hz)
    {
        var f = Fixture.Create(); var bindings = f.Compile().CreateCoreView(); var sources = bindings.Sources;
        var transitions = sources.Players.ToArray().Where(p => p.Domain == AlsLocomotionSourceDomain.MainGrounded &&
            p.Kind == AlsLocomotionSourceKind.Sequence).ToArray();
        Assert.Equal(2, transitions.Length);
        var ticks = new AlsAssetSyncPlayer[2]; var samples = new AlsAssetSyncSample[2]; var groups = new int[2];
        var previous = new AlsAssetPlayerHistory[2]; var previousSamples = new AlsAssetSampleHistory[2];
        var next = new AlsAssetPlayerHistory[2]; var nextSamples = new AlsAssetSampleHistory[2];
        var contexts = new AlsAssetPlayerTickContext[2]; var notifyTicks = new AlsP5SourceNotifyTick[2];
        var groupHistory = new AlsAssetSyncBatchGroupHistory[sources.GroupIds.Length];
        var nextGroups = new AlsAssetSyncBatchGroupHistory[sources.GroupIds.Length];
        var updates = transitions.Select((p, i) => new AlsLocomotionSourceUpdate(p.PlayerId, 1, p.StartPosition, 1, i, 1)).ToArray();
        var sampleUpdates = transitions.Select(p => new AlsLocomotionSampleUpdate(p.SampleStart, 1, 1)).ToArray();
        var state = default(AlsP5SourceEventState); var handles = new HashSet<int>(); var totalEvents = 0;
        for (var frame = 1; frame <= 2 * hz; frame++)
        {
            var delta = 1f / hz;
            Assert.True(AlsLocomotionSourceRuntime.TryBuildTicks(sources, sources.Stamp, updates, sampleUpdates, frame % 2 == 0 ? 0 : 3.7f,
                ticks, samples, groups, out _));
            Assert.Equal(new[] { -1, -1 }, groups);
            Assert.All(ticks, p => { Assert.Equal(1.2f, p.PlayRate); Assert.False(p.Looping); });
            Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncBatch(sources.GroupIds, groups, ticks, samples,
                sources.Sequences, sources.Markers, frame == 1 ? [] : groupHistory, frame == 1 ? [] : previous,
                frame == 1 ? [] : previousSamples, delta, nextGroups, next, nextSamples, out var syncFailure, contexts), syncFailure.ToString());
            Assert.True(AlsP5Runtime.TryBuildSourceNotifyTicks(bindings, ticks, samples, next, nextSamples,
                contexts, notifyTicks, out var count, out _));
            Assert.Equal(2, count);
            Assert.True(AlsP5Runtime.TryPrepareSourceEvents(bindings, new(frame, 0, 1), delta, notifyTicks, 1,
                state, out var candidate, out var events, out _));
            Assert.True(AlsP5Runtime.TryPrepareSourceEvents(bindings, new(frame, 0, 1), delta, notifyTicks, 1,
                state, out var retry, out var retryEvents, out _));
            Assert.Equal(candidate.RandomSeed, retry.RandomSeed); Assert.Equal(events.Count, retryEvents.Count);
            for (var i = 0; i < events.Count; i++)
            {
                Assert.Equal(events[i], retryEvents[i]); handles.Add(events[i].OccurrenceHandleId);
            }
            totalEvents += events.Count; state = candidate;
            for (var i = 0; i < updates.Length; i++)
            {
                Assert.InRange(MathF.Abs(next[i].Time - MathF.Min(frame * delta * 1.2f, 1)), 0, .00001f);
                updates[i] = updates[i] with { Time = next[i].Time };
            }
            (previous, next) = (next, previous); (previousSamples, nextSamples) = (nextSamples, previousSamples);
            (groupHistory, nextGroups) = (nextGroups, groupHistory);
        }
        Assert.Equal(4, totalEvents); Assert.Equal(2, handles.Count);
        var recovery = sources.Players.ToArray().Single(p => p.Domain == AlsLocomotionSourceDomain.MainGrounded &&
            p.Kind == AlsLocomotionSourceKind.TeleportEvaluator);
        var recoveryMap = bindings.SourceOccurrences.Mappings.ToArray().Single(m => m.PlayerId == recovery.PlayerId);
        Assert.DoesNotContain(bindings.TimelineDefinitions.ToArray(), d => d.RequiredOccurrenceHandleId == recoveryMap.OccurrenceHandleId);
    }

    [Theory]
    [InlineData(AlsLocomotionSourceDomain.Standing, .9f, .2f, 2)]
    [InlineData(AlsLocomotionSourceDomain.Crouching, .35f, .5f, 4)]
    public void RealRotationSourcesShareSyncAndNotifyTransactionWithoutLoopingPastRelease(
        AlsLocomotionSourceDomain domain, float start, float delta, int firstEvents)
    {
        var f = Fixture.Create(); var bindings = f.Compile().CreateCoreView(); var sources = bindings.Sources;
        var rotations = sources.Players.ToArray().Where(p => p.Domain == domain && p.LoopInput != AlsSourceLoopInput.Constant).ToArray();
        var ticks = new AlsAssetSyncPlayer[2]; var samples = new AlsAssetSyncSample[2]; var groups = new int[2];
        var next = new AlsAssetPlayerHistory[2]; var nextSamples = new AlsAssetSampleHistory[2];
        var contexts = new AlsAssetPlayerTickContext[2]; var notifyTicks = new AlsP5SourceNotifyTick[2];
        var groupHistory = new AlsAssetSyncBatchGroupHistory[sources.GroupIds.Length];
        var updates = rotations.Select((p, i) => new AlsLocomotionSourceUpdate(p.PlayerId, 1, start, 1, i, 1)).ToArray();
        var sampleUpdates = rotations.Select(p => new AlsLocomotionSampleUpdate(p.SampleStart, 1, 1)).ToArray();
        var state = default(AlsP5SourceEventState);
        for (var frame = 1; frame <= 2; frame++)
        {
            Assert.True(AlsLocomotionSourceRuntime.TryBuildTicks(sources, sources.Stamp, updates, sampleUpdates, 2.4f,
                ticks, samples, groups, out _, new(1, true, false)));
            Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncBatch(sources.GroupIds, groups, ticks, samples,
                sources.Sequences, sources.Markers, [], [], [], delta, groupHistory, next, nextSamples, out _, contexts));
            Assert.True(AlsP5Runtime.TryBuildSourceNotifyTicks(bindings, ticks, samples, next, nextSamples,
                contexts, notifyTicks, out var count, out _));
            Assert.Equal(2, count);
            Assert.True(AlsP5Runtime.TryPrepareSourceEvents(bindings, new(frame,0,1), delta, notifyTicks, 1,
                state, out var candidate, out var events, out _));
            Assert.Equal(frame == 1 ? firstEvents : 0, events.Count);
            if (frame == 1)
            {
                Assert.InRange(MathF.Abs(next[0].Time - (start + delta) % 1), 0, .00001f);
                Assert.Equal(MathF.Min(start + delta, 1), next[1].Time);
                Assert.True(notifyTicks[0].Loop); Assert.False(notifyTicks[1].Loop);
                Assert.NotEqual(events[0].OccurrenceHandleId, events[firstEvents - 1].OccurrenceHandleId);
                Assert.True(AlsP5Runtime.TryPrepareSourceEvents(bindings, new(frame,0,1), delta, notifyTicks, 1,
                    state, out var retry, out var retryEvents, out _));
                Assert.Equal(candidate.RandomSeed, retry.RandomSeed);
                for (var i = 0; i < firstEvents; i++) Assert.Equal(events[i], retryEvents[i]);
            }
            else
            {
                // UE retains requested leader delta even when non-looping time is clamped.
                Assert.Equal(MathF.Min(start + delta, 1), notifyTicks[1].PreviousTime); Assert.Equal(1, notifyTicks[1].ContextTime);
                Assert.Equal(delta, notifyTicks[1].Delta);
            }
            state = candidate;
            for (var i = 0; i < updates.Length; i++) updates[i] = updates[i] with { Time = next[i].Time };
        }
    }

    [Fact]
    public void SourceSnapshotCarriesExactPhysicalOwnershipAndCompleteSourceTables()
    {
        var f = Fixture.Create(); var snapshot = f.Compile();
        var core = snapshot.CreateCoreView(); var graph = snapshot.CreateGraphBuildView();
        Assert.Equal(3, snapshot.Version); Assert.Equal(3, snapshot.LayoutVersion); Assert.Equal(2, graph.Version);
        Assert.Equal(f.Layout.Digest, snapshot.LayoutDigest); Assert.Equal(f.Inventory.Digest, snapshot.SourceInventoryDigest);
        Assert.Equal(f.Sources.RuntimeStamp, core.Sources.Stamp);
        Assert.Equal(f.Sources.RuntimePlayers, core.Sources.Players.ToArray());
        Assert.Equal(f.Sources.RuntimeSamples, core.Sources.Samples.ToArray());
        Assert.Equal(f.Sources.CreateCoreView().SyncPlayers.ToArray(), core.Sources.SyncPlayers.ToArray());
        Assert.Equal(f.Sources.CreateCoreView().GroupIds.ToArray(), core.Sources.GroupIds.ToArray());
        Assert.Equal(f.Sources.CreateCoreView().Sequences.ToArray(), core.Sources.Sequences.ToArray());
        Assert.Equal(f.Sources.CreateCoreView().Markers.ToArray(), core.Sources.Markers.ToArray());
        Assert.Equal(f.Sources.CreateCoreView().NotifyRanges.ToArray(), core.Sources.NotifyRanges.ToArray());
        Assert.Equal(f.Sources.CreateCoreView().NotifyDefinitions.ToArray(), core.Sources.NotifyDefinitions.ToArray());
        Assert.Equal(f.Sources.CreateCoreView().NotifyPolicies.ToArray(), core.Sources.NotifyPolicies.ToArray());
        Assert.Equal(f.Layout.SourceMappings, core.SourceOccurrences.Mappings.ToArray());
        Assert.Equal(f.Layout.UnboundNativeSourceIndices, snapshot.UnboundNativeSourceIndices.ToArray());
        AlsP5SourceOccurrenceContract.Validate(core.SourceOccurrences, core.Sources);
        Assert.Equal(core.Sources.Stamp, graph.Sources.Stamp);
        Assert.Equal(core.Sources.NotifyPolicies.ToArray(), graph.Sources.NotifyPolicies.ToArray());
        Assert.Equal(f.Locomotion.StandingWalkRun, graph.StandingWalkRun.ToArray());
        foreach (var sample in core.Sources.Samples)
        {
            Assert.Contains(sample.AnimationId, graph.AllAnimationIds.ToArray());
            if (sample.AdditiveBaseAnimationId >= 0) Assert.Contains(sample.AdditiveBaseAnimationId, graph.AllAnimationIds.ToArray());
        }
        Assert.Equal(4, core.SyncMembers.Length); Assert.Equal(4, core.SyncOccurrences.Length);
        Assert.Equal(8, core.SyncMarkers.Length);
        for (var i = 0; i < core.SyncOccurrences.Length; i++)
        {
            var binding = core.SyncOccurrences[i]; var entry = core.SourceOccurrences.Entries[binding.OccurrenceHandleId];
            Assert.Equal(i, binding.GroupMemberIndex); Assert.Equal(Kind.Base, entry.SourceKind);
            Assert.Equal(f.Layout.SyncMappings[i].AnimationId, binding.AnimationId);
        }
    }

    [Fact]
    public void TimelineExpandsEachTimedSampleButNeverTeleportEvaluators()
    {
        var f = Fixture.Create(); var core = f.Compile().CreateCoreView();
        var definitions = core.TimelineDefinitions.ToArray(); var entries = core.SourceOccurrences.Entries.ToArray();
        foreach (var map in core.SourceOccurrences.Mappings)
        {
            var actual = definitions.Where(d => d.RequiredOccurrenceHandleId == map.OccurrenceHandleId).ToArray();
            if (entries[map.OccurrenceHandleId].SourceKind == Kind.SourceEvaluator)
            {
                Assert.Empty(actual); continue;
            }
            var expected = f.Set.Animations[map.AnimationId].Timeline;
            Assert.Equal(expected.Length, actual.Length);
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.Equal(map.AnimationId, actual[i].SourceAnimationId);
                Assert.Equal(expected[i].EventId, actual[i].EventId);
                Assert.Equal(expected[i].TimeSeconds, actual[i].TimeSeconds);
                Assert.Equal(expected[i].DurationSeconds, actual[i].DurationSeconds);
                Assert.Equal(expected[i].TriggerWeightThreshold, actual[i].TriggerWeightThreshold);
            }
        }
        var shared = core.SourceOccurrences.Mappings.ToArray().GroupBy(m => m.AnimationId)
            .First(g => g.Count() > 1 && f.Set.Animations[g.Key].Timeline.Length > 0).ToArray();
        var firstEvent = f.Set.Animations[shared[0].AnimationId].Timeline[0].EventId;
        Assert.All(shared.Where(m => entries[m.OccurrenceHandleId].SourceKind == Kind.SourceSample),
            m => Assert.Contains(definitions, d => d.EventId == firstEvent && d.RequiredOccurrenceHandleId == m.OccurrenceHandleId));
        Assert.Equal(Kind.ActionMontage, entries[core.ActionDefinitions[0].OccurrenceHandleId].SourceKind);
        Assert.Equal(Kind.ActionSequence, entries[core.ActionSegments[0].OccurrenceHandleId].SourceKind);
        Assert.Equal(Kind.Transition, entries[core.DynamicTransition.OccurrenceHandleId].SourceKind);
    }

    [Fact]
    public void SnapshotViewsAreImmutableAndDoNotAllocateAfterWarmup()
    {
        var f = Fixture.Create(); var snapshot = f.Compile(); var digest = snapshot.Digest;
        var view = snapshot.CreateCoreView(); var expected = view.SourceOccurrences.Mappings.ToArray();
        var original = f.Locomotion.StandingWalkRun[0];
        f.Locomotion.StandingWalkRun[0] = f.Locomotion.StandingWalkRun[1];
        var maps = f.Layout.SourceMappings; maps[0] = default;
        var unbound = snapshot.UnboundNativeSourceIndices.ToArray(); unbound[0] = -1;
        Assert.Equal(original, snapshot.CreateGraphBuildView().StandingWalkRun[0]);
        Assert.Equal(expected, snapshot.CreateCoreView().SourceOccurrences.Mappings.ToArray());
        Assert.True(snapshot.UnboundNativeSourceIndices[0] >= 0);
        for (var i = 0; i < 1000; i++) Consume(snapshot, digest);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) Consume(snapshot, digest);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void MainSnapshotNotifyWindowsAndPoliciesFeedOneCandidateQueueWithPhysicalHandles()
    {
        var f = Fixture.Create(); var core = f.Compile().CreateCoreView(); var sources = core.Sources;
        var output = new AlsAssetNotifyReference[256]; var scratch = new AlsAssetNotifyReference[256];
        Span<AlsAssetNotifyOccurrence> extracted = stackalloc AlsAssetNotifyOccurrence[16];
        Span<AlsAssetNotifyReference> incoming = stackalloc AlsAssetNotifyReference[16];
        var count = 0; var expected = new List<AlsAssetNotifyReference>(); var seed = AlsTimelineRuntime.InitialAssetNotifyRandomSeed;
        foreach (var map in core.SourceOccurrences.Mappings)
        {
            var entry = core.SourceOccurrences.Entries[map.OccurrenceHandleId];
            if (entry.SourceKind == Kind.SourceEvaluator) continue;
            var sample = sources.Samples[map.SampleId]; var range = sources.NotifyRanges[sample.SequenceIndex];
            Assert.Equal(map.AnimationId, range.AnimationId);
            Assert.True(AlsTimelineRuntime.TryExtractAssetNotifies(sources.NotifyDefinitions.Slice(range.Offset, range.Count),
                sample.DurationSeconds, 0, sample.DurationSeconds, false, extracted, out var found, out _));
            for (var i = 0; i < found; i++)
            {
                incoming[i] = new(range.Offset + extracted[i].DefinitionIndex, map.OccurrenceHandleId,
                    sample.DurationSeconds, true, extracted[i].ReachedEnd);
                expected.Add(incoming[i]);
            }
            Assert.True(AlsTimelineRuntime.TryQueueAssetNotifies(sources.NotifyPolicies, output.AsSpan(0, count), incoming[..found],
                new(true, false, 0, 1), AlsAssetNotifyQueueMode.Filtered, seed, scratch, output, out count, out seed, out _));
        }
        Assert.True(count > 20); Assert.Equal(expected, output.AsSpan(0, count).ToArray());
        Assert.NotEqual(AlsTimelineRuntime.InitialAssetNotifyRandomSeed, seed);
        var entries = core.SourceOccurrences.Entries.ToArray();
        Assert.All(expected, reference => Assert.NotEqual(Kind.SourceEvaluator,
            entries[reference.OccurrenceHandleId].SourceKind));
    }

    [Fact]
    public void RetainedSyncProjectionStillValidatesRemovedLegacyMemberMetadata()
    {
        var f = Fixture.Create(); var groups = f.P5.SyncGroups; var members = groups[0].Members;
        var removed = Array.FindIndex(members, member => !f.Layout.SyncMappings.Any(m => m.GroupMemberIndex == member.GroupMemberIndex));
        Assert.True(removed >= 0);
        members[removed] = members[removed] with { DurationSeconds = members[removed].DurationSeconds + .1f };
        groups[0] = groups[0] with { Members = members };
        Assert.Throws<ArgumentException>(() => AlsP5CoreRuntimeBindingCompiler.CompileSourceAware(
            f.Set, f.Locomotion, f.Pose, f.P5 with { SyncGroups = groups }, f.Layout, f.Sources, f.Inventory));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MainCompilerRejectsMissingOrStaleSourceGraphInputs(int mutation)
    {
        var f = Fixture.Create(); var layout = f.Layout; var locomotion = f.Locomotion;
        if (mutation == 0) layout = layout with { Version = 2 };
        if (mutation == 1) layout = layout with { SourceStamp = layout.SourceStamp with { Digest3 = layout.SourceStamp.Digest3 ^ 1 } };
        if (mutation == 2) locomotion = locomotion with { StandingWalkRun = null! };
        if (mutation == 3) locomotion = locomotion with { AllAnimationIds = locomotion.AllAnimationIds[..^1] };
        Assert.Throws<ArgumentException>(() => AlsP5CoreRuntimeBindingCompiler.CompileSourceAware(
            f.Set, locomotion, f.Pose, f.P5, layout, f.Sources, f.Inventory));
    }

    [Fact]
    public void LegacyPrepareRejectsSourceVersionBeforePublishingCandidateState()
    {
        var f = Fixture.Create(); var core = f.Compile().CreateCoreView();
        var control = new[] { new AlsP5RuntimeScratchControl(1) };
        var scratch = new AlsP5RuntimeScratch(0, 0, control, [], [], [], [], [], [], [], [], []);
        var state = AlsRuntimeState.CreateDefault();
        var input = new AlsP5FrameInput(new AlsFrameIdentity(0, 0, 1), 0, 1d / 60, 1f / 60, 1,
            AlsActionRequest.None, 0, 0, AlsTimelineLocomotionMode.Grounded,
            AlsTimelineRotationMode.VelocityDirection, AlsTimelineStance.Standing,
            new AlsP4CurveFrameInput([], [], [], AlsAnimationState.Grounded, 0, 0));
        Assert.False(AlsP5Runtime.TryPrepare(core, input, state, [], [], [], 1, ref scratch, out var prepared, out var failure));
        Assert.Equal(AlsP5FailureCode.InvalidBinding, failure);
        Assert.Equal(0UL, prepared.BindingDigest);
        Assert.Equal(AlsP5RuntimeScratchPhase.Empty, control[0].Phase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MainCompilerRejectsSwappedDirectionOrStrideCornerRoles(bool swapCorners)
    {
        var f = Fixture.Create(); var directions = f.Locomotion.StandingWalkRun.ToArray();
        if (swapCorners) directions[0] = directions[0] with { WalkPoseId = directions[0].RunPoseId, RunPoseId = directions[0].WalkPoseId };
        else (directions[0], directions[1]) = (directions[1], directions[0]);
        var locomotion = f.Locomotion with { StandingWalkRun = directions };
        Assert.Throws<ArgumentException>(() => AlsP5CoreRuntimeBindingCompiler.CompileSourceAware(
            f.Set, locomotion, f.Pose, f.P5, f.Layout, f.Sources, f.Inventory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceAndInventoryProvenanceChangeBothMainAndGraphDigest(bool changeSource)
    {
        var f = Fixture.Create(); var original = f.Compile();
        var sources = changeSource ? AlsLocomotionSourceCompiler.Compile(f.Read("v4_locomotion_source_graph.json") + " ",
            f.Set, f.Locomotion.SkeletonId) : f.Sources;
        var inventory = AlsP5SourceInventoryCompiler.Compile(f.Read("v4_anim_graph_inventory.json") + " ", f.Set, sources);
        var layout = AlsP5OccurrenceLayoutCompiler.CompileSourceAware(f.Locomotion, f.Pose, f.P5, sources, inventory);
        var changed = AlsP5CoreRuntimeBindingCompiler.CompileSourceAware(f.Set, f.Locomotion, f.Pose, f.P5, layout, sources, inventory);
        Assert.Equal(original.LayoutDigest, changed.LayoutDigest);
        Assert.NotEqual(original.Digest, changed.Digest); Assert.NotEqual(original.GraphDigest, changed.GraphDigest);
        Assert.Equal(original.Digest, f.Compile().Digest); Assert.Equal(original.GraphDigest, f.Compile().GraphDigest);
    }

    [Theory]
    [InlineData("chance")]
    [InlineData("filterLod")]
    [InlineData("filterViaRequest")]
    [InlineData("onDedicatedServer")]
    [InlineData("onFollower")]
    [InlineData("notifyObject")]
    public void NativeNotifyPolicyChangesInvalidateMainBindingsWithoutAllocatingNewPlaybackSlots(string field)
    {
        var f = Fixture.Create(); var original = f.Compile();
        var root = System.Text.Json.Nodes.JsonNode.Parse(f.Read("v4_locomotion_source_graph.json"))!;
        var row = root["syncAssets"]!.AsArray().First(a => a!["notifies"]!.AsArray().Count > 0)!["notifies"]![0]!;
        switch (field)
        {
            case "chance": row[field] = .5f; break;
            case "filterLod": row["filterType"] = 1; row[field] = 2; break;
            case "notifyObject": row[field] = row[field]!.GetValue<string>() + "_Changed"; break;
            default: row[field] = !row[field]!.GetValue<bool>(); break;
        }
        var sources = AlsLocomotionSourceCompiler.Compile(root.ToJsonString(), f.Set, f.Locomotion.SkeletonId);
        var inventory = AlsP5SourceInventoryCompiler.Compile(f.Read("v4_anim_graph_inventory.json"), f.Set, sources);
        var layout = AlsP5OccurrenceLayoutCompiler.CompileSourceAware(f.Locomotion, f.Pose, f.P5, sources, inventory);
        var changed = AlsP5CoreRuntimeBindingCompiler.CompileSourceAware(f.Set, f.Locomotion, f.Pose, f.P5, layout, sources, inventory);
        Assert.Equal(original.LayoutDigest, changed.LayoutDigest); Assert.NotEqual(original.Digest, changed.Digest);
        Assert.NotEqual(original.GraphDigest, changed.GraphDigest);
        Assert.Equal(original.CreateCoreView().TimelineDefinitions.ToArray(), changed.CreateCoreView().TimelineDefinitions.ToArray());
        Assert.NotEqual(original.CreateCoreView().Sources.Stamp, changed.CreateCoreView().Sources.Stamp);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining |
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    private static void Consume(AlsP5CoreRuntimeBindingSnapshot snapshot, ulong digest)
    {
        var core = snapshot.CreateCoreView(); var graph = snapshot.CreateGraphBuildView();
        var layout = snapshot.CreateOccurrenceLayoutView();
        if (core.Digest != digest || core.SourceOccurrences.Digest != layout.Digest || graph.Sources.Stamp != core.Sources.Stamp ||
            core.Sources.NotifyRanges.Length != 45 || core.Sources.NotifyPolicies.Length != 73 ||
            core.Sources.NotifyDefinitions[0].EventId != core.Sources.NotifyPolicies[0].EventId)
            throw new InvalidOperationException();
    }

    private sealed record Fixture(AlsAnimationSetDefinition Set, AlsLocomotionAnimationProfile Locomotion,
        AlsPoseAnimationProfile Pose, AlsP5aAnimationRuntimeProfile P5, AlsLocomotionSourceProfile Sources,
        AlsP5SourceInventory Inventory, AlsP5OccurrenceLayout Layout)
    {
        public string Read(string file) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", file));
        public static Fixture Create()
        {
            var set = P3RepositoryFixtures.LoadAnimationSet(); var root = Path.Combine(RepositoryRoot.Find(), "assets", "config");
            string Read(string file) => File.ReadAllText(Path.Combine(root, file));
            var locomotion = AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"), set);
            var pose = AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"), set, locomotion);
            var p5 = AlsP5aAnimationRuntimeProfileCompiler.Compile(Read("p5a_animation_runtime.json"), set);
            var sources = AlsLocomotionSourceCompiler.Compile(Read("v4_locomotion_source_graph.json"), set, locomotion.SkeletonId);
            var inventory = AlsP5SourceInventoryCompiler.Compile(Read("v4_anim_graph_inventory.json"), set, sources);
            var layout = AlsP5OccurrenceLayoutCompiler.CompileSourceAware(locomotion, pose, p5, sources, inventory);
            return new(set, locomotion, pose, p5, sources, inventory, layout);
        }
        public AlsP5CoreRuntimeBindingSnapshot Compile() => AlsP5CoreRuntimeBindingCompiler.CompileSourceAware(
            Set, Locomotion, Pose, P5, Layout, Sources, Inventory);
    }
}
