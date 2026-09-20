using System.Runtime.CompilerServices;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Diagnostics;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsP5SourceEventTests
{
    [Fact]
    public void DirectMontageThenProxyThenSlotRetainsFirstStateContextAndActionIdentity()
    {
        var f = new Fixture(); var binding = new MontageStateBinding(f.Policies);
        AlsAssetNotifyDispatchInput[] direct = [MontageStateInput(55), MontageInstantInput(55)];
        AlsAssetNotifyDispatchInput[] slot = [MontageStateInput(77), MontageInstantInput(77)];
        Assert.True(AlsP5Runtime.TryPrepareSourceEvents(f.Bindings,new(1,0,1),.3f,f.Ticks,1,default,
            out var state,out var events,out _,montageBinding:binding,montageNotifies:slot,montageDirectNotifies:direct));
        Assert.Equal(6,events.Count);
        Assert.Equal(new[] {100,10,10,100,11,11},Enumerable.Range(0,events.Count).Select(i=>events[i].EventId));
        Assert.Equal(55,events[0].PlaybackEpoch); Assert.Equal(77,events[3].PlaybackEpoch);
        Assert.Equal(1,state.ActiveCount); Assert.Equal(55,state.ActiveStates[0].Input.PlaybackEpoch);
        Assert.Equal(10,state.ActiveStates[0].Input.Reference.OccurrenceHandleId);
        Assert.Equal(.65f,state.ActiveStates[0].Input.Reference.CurrentTime);
        Assert.Equal(AlsAnimationEventPhase.Begin,events[4].Phase); Assert.Equal(7,events[4].SourceActionId);
        Assert.True(f.Run(default,1,out var sourceOnly,out _,out _)); Assert.Equal(sourceOnly.RandomSeed,state.RandomSeed);
        Assert.True(AlsP5Runtime.TryPrepareSourceEvents(f.Bindings,new(2,0,1),.1f,[],0,state,
            out var ended,out var endEvents,out _,montageBinding:binding));
        Assert.Equal(0,ended.ActiveCount); Assert.Equal(1,endEvents.Count);
        Assert.Equal(AlsAnimationEventPhase.End,endEvents[0].Phase); Assert.Equal(7,endEvents[0].SourceActionId);
        Assert.Equal(55,endEvents[0].PlaybackEpoch);
        Assert.True(AlsP5Runtime.TryPrepareSourceEvents(f.Bindings,new(1,0,1),.3f,f.Ticks,1,default,
            out var retry,out var retryEvents,out _,montageBinding:binding,montageNotifies:slot,montageDirectNotifies:direct));
        SameState(state,retry); SameEvents(events,retryEvents);
    }

    [Fact]
    public void ProxyStateDeduplicatesSlotStateWithoutDroppingOrMisidentifyingFollowingInstant()
    {
        var f = new Fixture(); var binding = new MontageStateBinding(f.Policies);
        Assert.True(AlsP5Runtime.TryPrepareSourceEvents(f.Bindings,new(1,0,1),.3f,f.Ticks,1,default,
            out var state,out var events,out _,montageBinding:binding,
            montageNotifies:[MontageStateInput(77),MontageInstantInput(77)]));
        Assert.Equal(5,events.Count); Assert.Equal(100,events[2].EventId); Assert.Equal(77,events[2].PlaybackEpoch);
        Assert.Equal(AlsAssetNotifySourceKind.AssetPlayer,state.ActiveStates[0].Input.SourceKind);
        Assert.Equal(1,state.ActiveStates[0].Input.PlaybackEpoch);
        Assert.Equal(-1,events[3].SourceActionId);
    }

    [Fact]
    public void MontageStateRestartUsesFullPlaybackEpochEvenWhenNativeInstanceIdWraps()
    {
        var f = new Fixture(); var binding = new MontageStateBinding(f.Policies);
        Assert.True(AlsP5Runtime.TryPrepareSourceEvents(f.Bindings,new(1,0,1),.1f,[],0,default,
            out var first,out _,out _,montageBinding:binding,montageDirectNotifies:[MontageStateInput(55)]));
        var epoch = 55L + (1L << 32);
        Assert.True(AlsP5Runtime.TryPrepareSourceEvents(f.Bindings,new(2,0,1),.1f,[],0,first,
            out var second,out var events,out _,montageBinding:binding,montageDirectNotifies:[MontageStateInput(epoch)]));
        Assert.Equal(3,events.Count); Assert.Equal(AlsAnimationEventPhase.End,events[0].Phase);
        Assert.Equal(AlsAnimationEventPhase.Begin,events[1].Phase); Assert.Equal(AlsAnimationEventPhase.Tick,events[2].Phase);
        Assert.Equal(epoch,second.ActiveStates[0].Input.PlaybackEpoch);
        Assert.Equal(55,events[0].PlaybackEpoch); Assert.Equal(epoch,events[1].PlaybackEpoch);
    }

    [Fact]
    public void DirectMontageOverflowCannotPublishAndValidRetryRetainsStateIdentity()
    {
        var f = new Fixture(); var binding = new MontageStateBinding(f.Policies);
        Assert.True(AlsP5Runtime.TryPrepareSourceEvents(f.Bindings,new(1,0,1),.1f,[],0,default,
            out var first,out _,out _,montageBinding:binding,montageDirectNotifies:[MontageStateInput(55)]));
        var over = Enumerable.Range(0,16).Select(_=>MontageInstantInput(77)).ToArray();
        Assert.False(AlsP5Runtime.TryPrepareSourceEvents(f.Bindings,new(2,0,1),.1f,[],0,first,
            out var failed,out var events,out var failure,montageBinding:binding,montageDirectNotifies:over));
        Assert.Equal(AlsP5FailureCode.EventBufferOverflow,failure); Assert.False(failed.Initialized); Assert.Equal(0,events.Count);
        Assert.True(AlsP5Runtime.TryPrepareSourceEvents(f.Bindings,new(2,0,1),.1f,[],0,first,
            out var next,out events,out _,montageBinding:binding,montageDirectNotifies:[MontageStateInput(55)]));
        Assert.Equal(first.ActiveStates[0].InstanceId,next.ActiveStates[0].InstanceId);
        Assert.Equal(1,events.Count); Assert.Equal(AlsAnimationEventPhase.Tick,events[0].Phase);
    }

    [Fact]
    public void DirectProxyAndSlotStateMergeAllocatesNothingAfterWarmup()
    {
        var f = new Fixture(); var binding = new MontageStateBinding(f.Policies);
        var direct = new[] {MontageStateInput(55),MontageInstantInput(55)};
        var slot = new[] {MontageStateInput(77),MontageInstantInput(77)};
        for(var i=0;i<1000;i++)Run();
        var before=GC.GetAllocatedBytesForCurrentThread(); for(var i=0;i<10000;i++)Run();
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
        void Run()
        {
            if(!AlsP5Runtime.TryPrepareSourceEvents(f.Bindings,new(1,0,1),.3f,f.Ticks,1,default,
                out _,out var events,out _,montageBinding:binding,montageNotifies:slot,montageDirectNotifies:direct) || events.Count!=6)
                throw new InvalidOperationException();
        }
    }

    private static AlsAssetNotifyDispatchInput MontageStateInput(long epoch) =>
        new(new(2,10,.65f,true,false),AlsAssetNotifySourceKind.Montage,unchecked((uint)epoch),true,.4f,epoch,.8f);
    private static AlsAssetNotifyDispatchInput MontageInstantInput(long epoch) =>
        new(new(3,10,.65f,true,false),AlsAssetNotifySourceKind.Montage,unchecked((uint)epoch),false,0,epoch,.8f);
    private sealed class MontageStateBinding(AlsAssetNotifyPolicy[] source) : IAlsMontageNotifyBinding
    {
        private readonly AlsAssetNotifyPolicy[] _policies = [..source,source[1],
            new(100,0,0,100,-1,100,0,1,0,0,AlsTimelineTickMode.Queued,true,true,true)];
        public int SourcePolicyCount => source.Length;
        public ReadOnlySpan<AlsAssetNotifyPolicy> Policies => _policies;
        public bool TryTimeline(in AlsAssetNotifyReference reference,out AlsTimelineEventDefinition definition)
        {
            definition = reference.PolicyIndex==2 ? new(11,7,7,10,AlsTimelineSourceKind.MontageSegmentAnimation,
                1,0,0,.3f,.4f,0,AlsTimelineEventKind.Generic,AlsTimelineTickMode.Queued,default) :
                new(100,7,7,10,AlsTimelineSourceKind.MontageSegmentAnimation,0,0,0,.2f,0,0,AlsTimelineEventKind.Generic,AlsTimelineTickMode.Queued,default);
            return reference.PolicyIndex is 2 or 3 && reference.OccurrenceHandleId==10;
        }
    }

    [Fact]
    public void MontageInstantCallbacksJoinSourceQueueBeforeStateBeginAndTickWithOneAllocator()
    {
        var f = new Fixture(); var binding = AlsTurnNotifyRuntimeTests.Binding(f.Policies);
        var montage = new AlsTurnNotifyRuntime(binding);
        montage.Begin(new(1,0,1), [AlsTurnNotifyRuntimeTests.Tick(55)]); montage.Complete(true, false);
        Assert.True(AlsP5Runtime.TryPrepareSourceEvents(f.Bindings, new(1,0,1), .3f, f.Ticks, 1, default,
            out var state, out var events, out _, montageBinding: binding, montageNotifies: montage.Notifies));
        Assert.Equal(5, events.Count);
        Assert.Equal(100, events[2].EventId); Assert.Equal(55, events[2].PlaybackEpoch);
        Assert.Equal(AlsAnimationEventPhase.Trigger, events[2].Phase);
        Assert.Equal(AlsAnimationEventPhase.Begin, events[3].Phase); Assert.Equal(AlsAnimationEventPhase.Tick, events[4].Phase);
        Assert.Equal(4, state.NextInstanceId);
        Assert.Equal(new long[] { 0,1,2,3,4 }, Enumerable.Range(0,events.Count).Select(i => events[i].EventSequence));
        Assert.True(f.Run(default, 1, out var sourceOnly, out _, out _));
        Assert.Equal(sourceOnly.RandomSeed, state.RandomSeed); // proxy and montage RNG are independent
        Assert.True(AlsP5Runtime.TryPrepareSourceEvents(f.Bindings, new(1,0,1), .3f, f.Ticks, 1, default,
            out var retry, out var retryEvents, out _, montageBinding: binding, montageNotifies: montage.Notifies));
        SameState(state,retry); SameEvents(events,retryEvents);
    }

    [Fact]
    public void CombinedMontageAndSourceOverflowPublishesNeitherEventsNorState()
    {
        var f = new Fixture(); var binding = AlsTurnNotifyRuntimeTests.Binding(f.Policies);
        var montage = new AlsTurnNotifyRuntime(binding);
        montage.Begin(new(1,0,1), Enumerable.Range(1,16).Select(i => AlsTurnNotifyRuntimeTests.Tick(i)).ToArray());
        montage.Complete(true, false);
        Assert.False(AlsP5Runtime.TryPrepareSourceEvents(f.Bindings, new(1,0,1), .3f, f.Ticks, 1, default,
            out var state, out var events, out var failure, montageBinding: binding, montageNotifies: montage.Notifies));
        Assert.Equal(AlsP5FailureCode.EventBufferOverflow, failure); Assert.False(state.Initialized); Assert.Equal(0, events.Count);
        Assert.Equal(default, montage.Committed);
    }

    [Fact]
    public void CombinedSourceAndTurnCallbackPreparationAllocatesNothingAfterWarmup()
    {
        var f = new Fixture(); var binding = AlsTurnNotifyRuntimeTests.Binding(f.Policies);
        var montage = new AlsTurnNotifyRuntime(binding);
        montage.Begin(new(1,0,1), [AlsTurnNotifyRuntimeTests.Tick()]); montage.Complete(true, false);
        var incoming = montage.Notifies.ToArray();
        for (var i = 0; i < 1000; i++) Run();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) Run();
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread() - before);
        void Run()
        {
            if (!AlsP5Runtime.TryPrepareSourceEvents(f.Bindings,new(1,0,1),.3f,f.Ticks,1,default,
                out _,out var events,out _,montageBinding:binding,montageNotifies:incoming) || events.Count != 5)
                throw new InvalidOperationException();
        }
    }

    [Fact]
    public void DynamicRotationBindingAndEventPreparationAreAllocationFree()
    {
        var f = new Fixture();
        for (var i = 0; i < 2; i++)
        {
            f.Players[i] = f.Players[i] with { Domain = AlsLocomotionSourceDomain.Standing, SyncGroupId = -1,
                PlayRateInput = AlsSourceRateInput.RotateRate, LoopInput = i == 0 ? AlsSourceLoopInput.RotateLeft : AlsSourceLoopInput.RotateRight };
            f.Ticks[i] = f.Ticks[i] with { Loop = i == 0 };
        }
        AlsLocomotionSourceUpdate[] updates = [new(0,1,.1f,.7f,0,1),new(1,1,.1f,.3f,1,1)];
        AlsLocomotionSampleUpdate[] samples = [new(0,1,1),new(1,1,1)];
        var output = new AlsAssetSyncPlayer[2]; var sampleOutput = new AlsAssetSyncSample[2]; var groups = new int[2];
        for (var i = 0; i < 1000; i++) Run();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) Run();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        void Run()
        {
            var source = f.Bindings.Sources;
            if (!AlsLocomotionSourceRuntime.TryBuildTicks(source, source.Stamp, updates, samples, 2,
                    output, sampleOutput, groups, out _, new(1, true, false)) || !output[0].Looping || output[1].Looping ||
                !f.Run(default, 1, out _, out var events, out _) || events.Count != 4) throw new InvalidOperationException();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ALoopOverrideDoesNotBypassTheSourceInputPolicy(bool rotation, bool wrongDomain)
    {
        var f = new Fixture();
        if (rotation || wrongDomain) f.Players[0] = f.Players[0] with
        {
            Domain = wrongDomain ? AlsLocomotionSourceDomain.Cycle : AlsLocomotionSourceDomain.Standing,
            SyncGroupId = -1, PlayRateInput = AlsSourceRateInput.RotateRate, LoopInput = AlsSourceLoopInput.RotateLeft,
        };
        AlsAssetSyncPlayer[] players = [new(0,100,1,AlsAssetSyncKind.Sequence,.1f,1,1,0,1,0,false)];
        AlsAssetSyncSample[] samples = [new(0,0,1)];
        AlsAssetPlayerHistory[] mapped = [new(0,100,1,.4f,.1f,.3f,default,0,1)];
        AlsAssetSampleHistory[] mappedSamples = [new(0,7,.4f,.1f,default,.1f,.3f)];
        var destination = new AlsP5SourceNotifyTick[1];
        var allowed = rotation && !wrongDomain;
        Assert.Equal(allowed, AlsP5Runtime.TryBuildSourceNotifyTicks(f.Bindings, players, samples, mapped,
            mappedSamples, [new(0,0,true)], destination, out var count, out _));
        Assert.Equal(allowed ? 1 : 0, count);
        if (!allowed) Assert.Equal(default, destination[0]);
        Assert.Equal(allowed, AlsP5Runtime.TryPrepareSourceEvents(f.Bindings, new(1,0,1), .3f,
            [new(0,1,.1f,.3f,.4f,1,true,false)], 1, default, out var state, out var events, out _));
        Assert.Equal(allowed, state.Initialized); Assert.Equal(allowed ? 3 : 0, events.Count);
    }

    [Fact]
    public void SourceTicksUseNativeSyncOrderLeaderAndPlayerWeight()
    {
        var f = new Fixture();
        AlsAssetSyncPlayer[] players = [new(0, 100, 1, AlsAssetSyncKind.Sequence, .1f, 1, .3f, 0, 1, 0),
            new(1, 101, 1, AlsAssetSyncKind.Sequence, .1f, 1, .7f, 1, 1, 0)];
        AlsAssetSyncSample[] samples = [new(0, 0, 1), new(1, 0, 1)];
        var mapped = new AlsAssetPlayerHistory[2]; var mappedSamples = new AlsAssetSampleHistory[2];
        var contexts = new AlsAssetPlayerTickContext[2]; var groups = new AlsAssetSyncBatchGroupHistory[1];
        Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncBatch([0], [0,0], players, samples, f.Sequences, [], [], [], [], .3f,
            groups, mapped, mappedSamples, out _, contexts));
        Assert.Equal(new(0, 1, false), contexts[0]); Assert.Equal(new(1, 0, true), contexts[1]);
        var ticks = new AlsP5SourceNotifyTick[2];
        Assert.True(AlsP5Runtime.TryBuildSourceNotifyTicks(f.Bindings, players, samples, mapped, mappedSamples, contexts, ticks, out var count, out _));
        Assert.Equal(2, count); Assert.Equal(1, ticks[0].SampleId); Assert.True(ticks[0].Leader); Assert.Equal(.7f, ticks[0].Weight);
        Assert.Equal(0, ticks[1].SampleId); Assert.False(ticks[1].Leader); Assert.Equal(mapped[0].Delta, ticks[1].Delta);
        Assert.True(AlsP5Runtime.TryPrepareSourceEvents(f.Bindings, new(1,0,1), .3f, ticks, 1, default, out _, out var events, out _));
        Assert.Equal(1, events[0].OccurrenceHandleId); Assert.Equal(0, events[1].OccurrenceHandleId);
    }

    [Theory]
    [InlineData(AlsBlendSpaceNotifyMode.AllAnimations, 2)]
    [InlineData(AlsBlendSpaceNotifyMode.HighestWeightedAnimation, 1)]
    [InlineData(AlsBlendSpaceNotifyMode.None, 0)]
    public void BlendSpaceSelectionUsesCacheOrderAndOwnerWeight(AlsBlendSpaceNotifyMode mode, int expected)
    {
        var f = new Fixture();
        f.Players[0] = f.Players[0] with { Kind = AlsLocomotionSourceKind.BlendSpace, SampleCount = 2 };
        f.Samples[1] = f.Samples[1] with { PlayerId = 0, SourceIndex = 1 };
        f.Sync[0] = f.Sync[0] with { NotifyMode = mode };
        AlsAssetSyncPlayer[] players = [new(0,100,1,AlsAssetSyncKind.BlendSpace,.1f,1,.8f,0,2,0)];
        AlsAssetSyncSample[] samples = [new(0,0,.5f),new(1,0,.5f)];
        AlsAssetPlayerHistory[] mapped = [new(0,100,1,.4f,.1f,.3f,default,0,2)];
        AlsAssetSampleHistory[] mappedSamples = [new(0,7,.4f,.1f,default,.1f,.3f),new(1,7,.4f,.1f,default,.1f,.3f)];
        var output = new AlsP5SourceNotifyTick[2];
        Assert.True(AlsP5Runtime.TryBuildSourceNotifyTicks(f.Bindings, players, samples, mapped, mappedSamples,
            [new(0,0,true)], output, out var count, out _));
        Assert.Equal(expected, count);
        for (var i = 0; i < count; i++) { Assert.Equal(i, output[i].SampleId); Assert.Equal(.8f, output[i].Weight); }
        var before = output.ToArray();
        Assert.False(AlsP5Runtime.TryBuildSourceNotifyTicks(f.Bindings, players, samples, mapped, mappedSamples,
            [new(0,1,true)], output, out count, out var failure));
        Assert.Equal(0, count); Assert.NotEqual(AlsP5FailureCode.None, failure); Assert.Equal(before, output);
    }

    [Fact]
    public void QueueAndLifecyclePublishOneTypedCandidateWithOriginalNativeContext()
    {
        var f = new Fixture();
        Assert.True(f.Run(default, 1, out var next, out var events, out _));
        Assert.Equal(4, events.Count); Assert.Equal(1, next.ActiveCount); Assert.Equal(3, next.NextInstanceId);
        Assert.Equal(new[] { AlsAnimationEventPhase.Trigger, AlsAnimationEventPhase.Trigger, AlsAnimationEventPhase.Begin, AlsAnimationEventPhase.Tick },
            Enumerable.Range(0, events.Count).Select(i => events[i].Phase));
        Assert.Equal(new[] { 0,2,1,1 }, Enumerable.Range(0, events.Count).Select(i => events[i].NativeContext.InstanceId));
        for (var i = 0; i < events.Count; i++)
        {
            var item = events[i]; Assert.True(item.NativeContext.Present); Assert.Equal(i, item.EventSequence);
            Assert.Equal(.3f, item.AnimationTime); Assert.Equal(.4f, item.NativeContext.CurrentAnimationTime);
            Assert.Equal(1, item.PlaybackEpoch); Assert.Equal(0, item.PlaybackCycle); Assert.Equal(7, item.SourceAnimationId);
        }
        Assert.Equal(.7f, events[2].Weight); Assert.Equal(.4f, events[2].NativeContext.CallbackSeconds);
        Assert.Equal(.3f, events[3].NativeContext.CallbackSeconds);
        Assert.True(f.Run(default, 1, out var retry, out var retriedEvents, out _));
        SameState(next, retry); SameEvents(events, retriedEvents);
        Assert.True(AlsP5Runtime.TryPrepareSourceEvents(f.Bindings, new(2,0,1), .1f, [], 1, next, out var ended, out var ends, out _));
        Assert.Equal(0, ended.ActiveCount); Assert.Equal(next.RandomSeed, ended.RandomSeed); Assert.Equal(next.NextInstanceId, ended.NextInstanceId);
        Assert.Equal(1, ends.Count); Assert.Equal(AlsAnimationEventPhase.End, ends[0].Phase);
        Assert.Equal(.4f, ends[0].NativeContext.CurrentAnimationTime); Assert.Equal(2UL, ends[0].OwnerToken);
    }

    [Fact]
    public void ActiveStateKeepsLatestSourceContextAndNoMergeIncludesPlaybackActivation()
    {
        var f = new Fixture(); Assert.True(f.Run(default, 1, out var first, out _, out _));
        AlsP5SourceNotifyTick[] tick = [new(1, 2, .4f, .1f, .5f, .6f, true, true, false)];
        Assert.True(AlsP5Runtime.TryPrepareSourceEvents(f.Bindings, new(2,0,1), .1f, tick, 1, first, out var continued, out var events, out _));
        Assert.Equal(1, events.Count); Assert.Equal(AlsAnimationEventPhase.Tick, events[0].Phase);
        Assert.Equal(1, events[0].OccurrenceHandleId); Assert.Equal(2, events[0].PlaybackEpoch); Assert.Equal(.6f, events[0].Weight);
        Assert.False(events[0].NativeContext.ActiveContext); Assert.Equal(first.ActiveStates[0].InstanceId, continued.ActiveStates[0].InstanceId);
        f.Policies[1] = f.Policies[1] with { StateBehaviorFlags = 1 };
        Assert.True(AlsP5Runtime.TryPrepareSourceEvents(f.Bindings, new(2,0,1), .1f, tick, 1, first, out var restarted, out events, out _));
        Assert.Equal(3, events.Count); Assert.Equal(AlsAnimationEventPhase.End, events[0].Phase);
        Assert.Equal(AlsAnimationEventPhase.Begin, events[1].Phase); Assert.Equal(first.NextInstanceId, restarted.ActiveStates[0].InstanceId);
    }

    [Theory]
    [InlineData("frame")]
    [InlineData("character")]
    [InlineData("generation")]
    [InlineData("stamp")]
    [InlineData("binding")]
    [InlineData("layout")]
    [InlineData("count")]
    [InlineData("duplicate-tick")]
    [InlineData("nan-delta")]
    [InlineData("nan-weight")]
    [InlineData("nan-context")]
    [InlineData("bad-sample")]
    [InlineData("bad-later-sample")]
    [InlineData("overflow")]
    [InlineData("missing-timeline")]
    public void FailedAttemptDoesNotPublishPartialStateOrEvents(string mutation)
    {
        var f = new Fixture(); Assert.True(f.Run(default, 1, out var current, out _, out _));
        var identity = new AlsFrameIdentity(2,0,1); var ticks = f.Ticks.ToArray(); var delta = .3f;
        if (mutation == "frame") identity = new(1,0,1);
        if (mutation == "character") identity = new(2,9,1);
        if (mutation == "generation") identity = new(2,0,2);
        if (mutation == "stamp") current.SourceStamp = current.SourceStamp with { Digest0 = 99 };
        if (mutation == "binding") current.BindingDigest++;
        if (mutation == "layout") current.LayoutDigest++;
        if (mutation == "count") current.ActiveCount = 17;
        if (mutation == "duplicate-tick") ticks[1] = ticks[0];
        if (mutation == "nan-delta") delta = float.NaN;
        if (mutation == "nan-weight") ticks[0] = ticks[0] with { Weight = float.NaN };
        if (mutation == "nan-context") ticks[0] = ticks[0] with { ContextTime = float.NaN };
        if (mutation == "bad-sample") ticks[0] = ticks[0] with { SampleId = 99 };
        if (mutation == "bad-later-sample") ticks[1] = ticks[1] with { SampleId = 99 };
        if (mutation == "overflow") ticks[0] = ticks[0] with { Delta = 20 };
        if (mutation == "missing-timeline") f.Timeline[0] = f.Timeline[0] with { EventId = 99 };
        Assert.False(AlsP5Runtime.TryPrepareSourceEvents(f.Bindings, identity, delta, ticks, 1, current, out var candidate, out var events, out var failure));
        Assert.NotEqual(AlsP5FailureCode.None, failure); Assert.False(candidate.Initialized); Assert.Equal(0, events.Count);
    }

    [Fact]
    public void FollowerFilteringAndGlobalGraphWeightConsumeNoPrematureRandomDraws()
    {
        var f = new Fixture(); f.Policies[0] = f.Policies[0] with { OnFollower = false, WeightThreshold = .5f };
        f.Policies[1] = f.Policies[1] with { OnFollower = false, WeightThreshold = .5f };
        Assert.True(AlsP5Runtime.TryPrepareSourceEvents(f.Bindings, new(1,0,1), .3f, f.Ticks, .5f, default, out var candidate, out var events, out _));
        Assert.Equal(0, events.Count); Assert.Equal(AlsTimelineRuntime.InitialAssetNotifyRandomSeed, candidate.RandomSeed);
        Assert.True(f.Run(default, 1, out candidate, out events, out _)); Assert.Equal(3, events.Count);
        Assert.Equal(unchecked(AlsTimelineRuntime.InitialAssetNotifyRandomSeed * 196314165u + 907633515u), candidate.RandomSeed);
    }

    [Fact]
    public void NativeContextContributesToResultDigestAndStateIsUnmanaged()
    {
        var f = new Fixture(); Assert.True(f.Run(default, 1, out _, out var events, out _));
        var result = AlsFrameResult.CreateDefault(new(1,0,1)); result.TypedEvents = events;
        var digest = AlsResultDigest.OffsetBasis; AlsResultDigest.Append(ref digest, result);
        result.TypedEvents.Clear();
        for (var i = 0; i < events.Count; i++) result.TypedEvents.TryAdd(events[i] with { NativeContext = events[i].NativeContext with { ReachedEnd = true } });
        var changed = AlsResultDigest.OffsetBasis; AlsResultDigest.Append(ref changed, result); Assert.NotEqual(digest, changed);
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsP5SourceEventState>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsAnimationEvent>());
    }

    [Fact]
    public void SourceFramePreparationAndRetryAreAllocationFree()
    {
        var f = new Fixture();
        for (var i = 0; i < 1000; i++) Run();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) Run();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        void Run() { if (!f.Run(default, 1, out _, out var events, out _) || events.Count != 4) throw new InvalidOperationException(); }
    }

    private static void SameState(AlsP5SourceEventState a, AlsP5SourceEventState b)
    {
        Assert.Equal(a.Identity, b.Identity); Assert.Equal(a.RandomSeed, b.RandomSeed); Assert.Equal(a.NextInstanceId, b.NextInstanceId);
        Assert.Equal(a.ActiveCount, b.ActiveCount); for (var i = 0; i < a.ActiveCount; i++) Assert.Equal(a.ActiveStates[i], b.ActiveStates[i]);
    }
    private static void SameEvents(AlsEventBuffer a, AlsEventBuffer b)
    { Assert.Equal(a.Count, b.Count); for (var i = 0; i < a.Count; i++) Assert.Equal(a[i], b[i]); }

    private sealed class Fixture
    {
        public readonly AlsAssetNotifyPolicy[] Policies = [new(10,0,0,0,-1,0,0,1,0,0,0,true,true,true), new(11,1,0,-1,1,1,0,1,0,0,0,true,true,true)];
        public readonly AlsAssetNotifyDefinition[] Definitions = [new(10,.2f,.2f), new(11,.3f,.7f)];
        public readonly AlsAssetSyncSequence[] Sequences = [new(7,1,1,0,0)];
        public readonly AlsAssetNotifyRange[] Ranges = [new(7,0,2)];
        public readonly AlsLocomotionSourcePlayerBinding[] Players = [Player(0), Player(1)];
        public readonly AlsLocomotionSourceSampleBinding[] Samples = [Sample(0), Sample(1)];
        public readonly AlsLocomotionSourceSyncBinding[] Sync = [new(0,100,0,false,false,false,AlsBlendSpaceNotifyMode.AllAnimations), new(1,101,0,false,false,false,AlsBlendSpaceNotifyMode.AllAnimations)];
        public readonly AlsP5SourceOccurrenceMapping[] Maps = [new(0,0,0,0,7,0),new(1,1,1,0,7,1)];
        public readonly AlsP5OccurrenceLayoutEntry[] Entries = [new(AlsP5OccurrenceSourceKind.SourceSample,0,0,0,0),new(AlsP5OccurrenceSourceKind.SourceSample,1,0,1,1)];
        public readonly AlsTimelineEventDefinition[] Timeline = [Definition(0,10,0,.2f,0),Definition(0,11,1,.3f,.4f),Definition(1,10,0,.2f,0),Definition(1,11,1,.3f,.4f)];
        public readonly AlsP5SourceNotifyTick[] Ticks = [new(0,1,.1f,.3f,.4f,.7f,true,true),new(1,1,.1f,.3f,.4f,.3f,false,true)];
        public AlsP5RuntimeBindings Bindings
        {
            get
            {
                var source = new AlsLocomotionSourceView(new(AlsLocomotionSourceView.CurrentVersion,0,1,0,0,0), Players, Samples, Sync, [], Sequences, [], Ranges, Definitions, Policies);
                return new(3,11,12,[],[],[],[],default,[],[],0,0,0,0,Timeline,[],default,[],[],default,[],[],[],[],source,
                    new(3,12,source.Stamp,Entries,Maps));
            }
        }
        public bool Run(in AlsP5SourceEventState current, long frame, out AlsP5SourceEventState next, out AlsEventBuffer events, out AlsP5FailureCode failure) =>
            AlsP5Runtime.TryPrepareSourceEvents(Bindings, new(frame,0,1), .3f, Ticks, 1, current, out next, out events, out failure);
        private static AlsLocomotionSourcePlayerBinding Player(int id) => new(id,id,AlsLocomotionSourceKind.Sequence,AlsLocomotionSourceDomain.Cycle,0,0,1,1,AlsSourceRateInput.Constant,true,id,1,-1,0,0);
        private static AlsLocomotionSourceSampleBinding Sample(int id) => new(id,id,0,7,0,0,0,1,1,1,-1,0);
        private static AlsTimelineEventDefinition Definition(int handle, int id, int index, float time, float duration) =>
            new(id,7,-1,handle,AlsTimelineSourceKind.Animation,index,0,0,time,duration,0,AlsTimelineEventKind.Generic,AlsTimelineTickMode.Queued,default);
    }
}
