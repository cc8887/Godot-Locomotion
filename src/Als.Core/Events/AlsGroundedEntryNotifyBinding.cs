using GodotAls.Core.Contracts;

namespace GodotAls.Core.Events;

// An explicitly compiled native property supplements an older Generic manifest
// entry. Occurrence/instance identity and callback timing remain native-owned.
public sealed record AlsGroundedEntryNotifyBinding(int EventId, int AnimationId, AlsTimelineGroundedEntryMode Mode, int SemanticId)
{
    public void Apply(ref AlsEventBuffer events)
    {
        var candidate = new AlsEventBuffer();
        for (var i = 0; i < events.Count; i++)
        {
            var item = events[i];
            if (item.EventId == EventId && item.SourceAnimationId == AnimationId)
            {
                if (item.Phase != AlsAnimationEventPhase.Trigger ||
                    item.Kind is not (AlsTimelineEventKind.Generic or AlsTimelineEventKind.SetGroundedEntry) ||
                    item.Kind == AlsTimelineEventKind.SetGroundedEntry && item.Payload.EnumValue0 != (int)Mode)
                    throw new InvalidOperationException("Grounded entry notify differs from its compiled native property.");
                item = item with { Kind = AlsTimelineEventKind.SetGroundedEntry,
                    Payload = item.Payload with { SemanticId = SemanticId, EnumValue0 = (int)Mode } };
            }
            if (!candidate.TryAdd(item)) throw new InvalidOperationException("Grounded entry event capacity differs.");
        }
        events = candidate;
    }
}

public readonly record struct AlsMovementNotifyState(AlsTimelineAction Action, AlsTimelineGroundedEntryMode Entry)
{
    // Graph notifications are queued before asset-player notifications in UE.
    // Return a candidate; the enclosing whole-frame owner alone commits it.
    public AlsMovementNotifyState Advance(in AlsEventBuffer events, bool resetGroundedEntry)
    {
        var next = resetGroundedEntry ? this with { Entry = AlsTimelineGroundedEntryMode.None } : this;
        for (var i = 0; i < events.Count; i++)
        {
            var item = events[i];
            if (item.Kind == AlsTimelineEventKind.SetAction)
            {
                var action = (AlsTimelineAction)item.Payload.EnumValue0;
                if (item.Payload.EnumValue0 != (int)action || !Enum.IsDefined(action)) throw new ArgumentException("Unknown movement action notify.");
                if (item.Phase == AlsAnimationEventPhase.Begin) next = next with { Action = action };
                else if (item.Phase == AlsAnimationEventPhase.End && next.Action == action)
                    next = next with { Action = AlsTimelineAction.None };
            }
            else if (item.Kind == AlsTimelineEventKind.SetGroundedEntry && item.Phase == AlsAnimationEventPhase.Trigger)
            {
                var entry = (AlsTimelineGroundedEntryMode)item.Payload.EnumValue0;
                if (item.Payload.EnumValue0 != (int)entry || !Enum.IsDefined(entry)) throw new ArgumentException("Unknown grounded entry notify.");
                next = next with { Entry = entry };
            }
        }
        return next;
    }
}
