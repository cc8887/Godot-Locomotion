using GodotAls.Core.Actions;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsTurnNotifyRuntimeTests
{
    [Fact]
    public void HiddenSlotsStillFilterButOnlyCurrentOrPreviousRelevantSlotsDispatch()
    {
        var owner = new AlsTurnNotifyRuntime(Binding());
        owner.Begin(new(1,0,1), [Tick()]); owner.Complete(false, false);
        Assert.Equal(1, owner.FilteredCount); Assert.Empty(owner.Notifies.ToArray());
        Assert.NotEqual(AlsTimelineRuntime.InitialAssetNotifyRandomSeed, owner.Candidate.RandomSeed);
        owner.Commit(new(1,0,1));
        owner.Begin(new(2,0,1), []); owner.Complete(true, false); owner.Commit(new(2,0,1));
        owner.Begin(new(3,0,1), [Tick()]); owner.Complete(false, false);
        Assert.Single(owner.Notifies.ToArray()); owner.Commit(new(3,0,1));
        owner.Begin(new(4,0,1), [Tick()]); owner.Complete(false, false);
        Assert.Empty(owner.Notifies.ToArray());
    }

    [Theory]
    [InlineData(true, false, 1)] [InlineData(false, true, 1)] [InlineData(true, true, 2)]
    public void SlotQueuesRetainFirstTraversalSlotOrder(bool standing, bool crouch, int count)
    {
        var owner = new AlsTurnNotifyRuntime(Binding());
        owner.Begin(new(1,0,1), [Tick(2, 21, AlsTurnSlot.Crouching), Tick()]); owner.Complete(standing, crouch);
        Assert.Equal(count, owner.Notifies.Length);
        if (crouch) Assert.Equal(2, owner.Notifies[0].PlaybackEpoch);
        if (standing && crouch) Assert.Equal(1, owner.Notifies[1].PlaybackEpoch);
    }

    [Fact]
    public void InterruptedTraversalIsSuppressedButNaturalTerminationCanNotify()
    {
        var owner = new AlsTurnNotifyRuntime(Binding());
        owner.Begin(new(1,0,1), [Tick() with { Interrupted = true }, Tick(2) with { Terminated = true }]);
        owner.Complete(true, false); Assert.Single(owner.Notifies.ToArray()); Assert.Equal(2, owner.Notifies[0].PlaybackEpoch);
    }

    [Theory]
    [InlineData(.499f, false, 0, 0)] [InlineData(.5f, false, 0, 1)]
    [InlineData(1f, true, 0, 0)] [InlineData(1f, false, 2, 0)]
    public void QueueAppliesOriginalWeightServerAndLodBeforeSlotVisibility(float weight, bool server, int lod, int expected)
    {
        var owner = new AlsTurnNotifyRuntime(Binding());
        owner.Begin(new(1,0,1), [Tick() with { NotifyWeight = weight }], server, lod);
        owner.Complete(true, false); Assert.Equal(expected, owner.Notifies.Length);
    }

    [Fact]
    public void SameAssetConcurrentInstancesKeepDistinctPlaybackIdentityAndRetry()
    {
        var owner = new AlsTurnNotifyRuntime(Binding());
        var ticks = new[] { Tick(42), Tick(43) };
        owner.Begin(new(1,0,1), ticks); owner.Complete(true, false);
        var state = owner.Candidate; var events = owner.Notifies.ToArray();
        Assert.Equal(new long[] { 42, 43 }, events.Select(e => e.PlaybackEpoch));
        owner.Discard(); Assert.Equal(default, owner.Committed);
        owner.Begin(new(1,0,1), ticks); owner.Complete(true, false);
        Assert.Equal(state, owner.Candidate); Assert.Equal(events, owner.Notifies.ToArray());
        Assert.Throws<InvalidOperationException>(() => owner.Commit(new(2,0,1)));
        Assert.Equal(default, owner.Committed); owner.Commit(new(1,0,1)); Assert.Equal(state, owner.Committed);
    }

    [Fact]
    public void ReverseBoundaryAndStationaryTraversalUseNativeExtraction()
    {
        var owner = new AlsTurnNotifyRuntime(Binding());
        owner.Begin(new(1,0,1), [Tick() with { PreviousPosition = .3f, CurrentPosition = .2f }]); owner.Complete(true, false);
        Assert.Single(owner.Notifies.ToArray()); owner.Discard();
        owner.Begin(new(1,0,1), [Tick() with { PreviousPosition = .2f, CurrentPosition = .2f }]); owner.Complete(true, false);
        Assert.Empty(owner.Notifies.ToArray());
    }

    [Fact]
    public void WarmedPreparationVisibilityAndDiscardAllocateNothing()
    {
        var owner = new AlsTurnNotifyRuntime(Binding()); var ticks = new[] { Tick() };
        for (var i = 0; i < 1000; i++) Run();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) Run();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        void Run() { owner.Begin(new(1,0,1), ticks); owner.Complete(true, false); owner.Discard(); }
    }

    internal static AlsMontageTraversal Tick(long instance = 1, int animation = 20, AlsTurnSlot slot = AlsTurnSlot.Standing) =>
        new(instance, animation, slot, .1f, .3f, 1, false, false);
    internal static AlsTurnNotifyBinding Binding(AlsAssetNotifyPolicy[]? source = null) => new(source ?? [],
        [Policy(100), Policy(101)], [new(100,.2f,.2f), new(101,.2f,.2f)],
        [Timeline(100,20,10), Timeline(101,21,11)],
        [new(new(20,AlsTurnSlot.Standing,1,1),10,0,1), new(new(21,AlsTurnSlot.Crouching,1,1),11,1,1)]);
    private static AlsAssetNotifyPolicy Policy(int id) => new(id,0,0,id,-1,0,.5f,1,AlsAssetNotifyFilterType.Lod,2,
        AlsTimelineTickMode.Queued,true,false,false);
    private static AlsTimelineEventDefinition Timeline(int id, int asset, int handle) => new(id,asset,-1,handle,
        AlsTimelineSourceKind.MontageSegmentAnimation,0,0,0,.2f,0,.5f,AlsTimelineEventKind.Generic,AlsTimelineTickMode.Queued,default);
}
