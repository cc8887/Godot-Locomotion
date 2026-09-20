using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsGroundedEntryNotifyTests
{
    [Fact]
    public void CompletesGenericSemanticsWithoutChangingPhysicalIdentityOrOrder()
    {
        var original = Event(AlsTimelineEventKind.Generic, AlsAnimationEventPhase.Trigger, 0) with
        { NativeContext = new(true, 91, .94f, .016f, true, false) };
        var buffer = Buffer(original, original with { SourceAnimationId = 31 }, original with { EventId = 32 });
        var binding = new AlsGroundedEntryNotifyBinding(original.EventId, original.SourceAnimationId, AlsTimelineGroundedEntryMode.FromRoll, 3);
        binding.Apply(ref buffer);
        Assert.Equal(original with { Kind = AlsTimelineEventKind.SetGroundedEntry,
            Payload = original.Payload with { SemanticId = 3, EnumValue0 = (int)AlsTimelineGroundedEntryMode.FromRoll } }, buffer[0]);
        Assert.Equal(original with { SourceAnimationId = 31 }, buffer[1]);
        Assert.Equal(original with { EventId = 32 }, buffer[2]);
        var first = buffer; binding.Apply(ref buffer);
        for (var i = 0; i < first.Count; i++) Assert.Equal(first[i], buffer[i]);
    }

    [Theory]
    [InlineData(AlsAnimationEventPhase.Begin)]
    [InlineData(AlsAnimationEventPhase.End)]
    public void InvalidCallbackDoesNotPartiallyPublish(AlsAnimationEventPhase phase)
    {
        var item = Event(AlsTimelineEventKind.Generic, AlsAnimationEventPhase.Trigger, 0);
        var buffer = Buffer(item, item with { Phase = phase }); var prior = buffer;
        var binding = new AlsGroundedEntryNotifyBinding(item.EventId, item.SourceAnimationId, AlsTimelineGroundedEntryMode.FromRoll, 3);
        Assert.Throws<InvalidOperationException>(() => binding.Apply(ref buffer));
        for (var i = 0; i < buffer.Count; i++) Assert.Equal(prior[i], buffer[i]);
    }

    [Fact]
    public void EntryResetAndAssetNotificationsRespectDispatchOrderWithoutLeakingCandidates()
    {
        var committed = new AlsMovementNotifyState(AlsTimelineAction.Rolling, AlsTimelineGroundedEntryMode.FromRoll);
        var events = Buffer(Event(AlsTimelineEventKind.SetGroundedEntry, AlsAnimationEventPhase.Trigger, (int)AlsTimelineGroundedEntryMode.FromRoll),
            Event(AlsTimelineEventKind.SetAction, AlsAnimationEventPhase.End, (int)AlsTimelineAction.Rolling));
        var candidate = committed.Advance(events, true);
        Assert.Equal(new(AlsTimelineAction.None, AlsTimelineGroundedEntryMode.FromRoll), candidate);
        Assert.Equal(AlsTimelineAction.Rolling, committed.Action);
        Assert.Equal(candidate, committed.Advance(events, true));
        Assert.Equal(new(AlsTimelineAction.None, AlsTimelineGroundedEntryMode.None), candidate.Advance(default, true));
        Assert.Equal(candidate, candidate.Advance(default, false)); // An unvisited graph must not reset.
    }

    [Fact]
    public void OldDifferentActionEndDoesNotClearNewAction()
    {
        var events = Buffer(Event(AlsTimelineEventKind.SetAction, AlsAnimationEventPhase.Begin, (int)AlsTimelineAction.Mantling),
            Event(AlsTimelineEventKind.SetAction, AlsAnimationEventPhase.End, (int)AlsTimelineAction.Rolling));
        Assert.Equal(AlsTimelineAction.Mantling, default(AlsMovementNotifyState).Advance(events, false).Action);
    }

    [Theory]
    [InlineData(-1)] [InlineData(256)] [InlineData(99)]
    public void RejectsOutOfRangeEnumValues(int value)
    {
        var events = Buffer(Event(AlsTimelineEventKind.SetGroundedEntry, AlsAnimationEventPhase.Trigger, value));
        Assert.Throws<ArgumentException>(() => default(AlsMovementNotifyState).Advance(events, false));
    }

    [Fact]
    public void WarmedNotifyTranslationAndConsumptionAllocateNothing()
    {
        var binding = new AlsGroundedEntryNotifyBinding(17, 19, AlsTimelineGroundedEntryMode.FromRoll, 3);
        var events = Buffer(Event(AlsTimelineEventKind.Generic, AlsAnimationEventPhase.Trigger, 0),
            Event(AlsTimelineEventKind.SetAction, AlsAnimationEventPhase.Begin, (int)AlsTimelineAction.Rolling) with { EventId = 18 });
        var state = default(AlsMovementNotifyState);
        for (var i = 0; i < 300; i++) Tick();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) Tick();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated); Assert.Equal(AlsTimelineGroundedEntryMode.FromRoll, state.Entry);
        void Tick() { var candidate = events; binding.Apply(ref candidate); state = state.Advance(candidate, true); }
    }

    private static AlsAnimationEvent Event(AlsTimelineEventKind kind, AlsAnimationEventPhase phase, int value) =>
        new(17, 19, 23, 29, 91, 3, 101, 211, 7, .004f, .75f, kind, phase, new(13, value, 0, 0, 0, 0, 0));
    private static AlsEventBuffer Buffer(params AlsAnimationEvent[] events)
    { var buffer = new AlsEventBuffer(); foreach (var item in events) Assert.True(buffer.TryAdd(item)); return buffer; }
}
