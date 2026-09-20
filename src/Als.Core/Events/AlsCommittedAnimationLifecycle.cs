using GodotAls.Core.Contracts;

namespace GodotAls.Core.Events;

// Main-thread ownership only. Never consults a speculative animation bank.
// Apply immediately before each callback; retirement can stop the rest of the
// dispatch and close only ownership whose Begin/Accepted has been delivered.
public sealed class AlsCommittedAnimationLifecycle
{
    private AlsCommittedNotifyStateBuffer _states;
    private AlsActionOutcomeBuffer _actions;
    public AlsFrameIdentity Identity { get; private set; }
    public bool Closed { get; private set; }
    public int StateCount => _states.Count;
    public int ActionCount => _actions.Count;

    public AlsCommittedAnimationLifecycle(uint characterId, uint generation)
    {
        if (generation == 0) throw new ArgumentOutOfRangeException(nameof(generation));
        Identity = new(0, characterId, generation);
    }

    public void ValidateDispatch(in AlsFrameResult result)
    {
        if (Closed || result.Identity.CharacterId != Identity.CharacterId ||
            result.Identity.SlotGeneration != Identity.SlotGeneration || result.Identity.FrameId <= Identity.FrameId)
            throw new InvalidOperationException("Committed callback identity is stale, foreign or retired.");
        var states = _states; var actions = _actions;
        for (var i = 0; i < result.ActionOutcomes.Count; i++) Apply(ref actions, result.ActionOutcomes[i]);
        for (var i = 0; i < result.TypedEvents.Count; i++) Apply(ref states, result.TypedEvents[i]);
    }

    public void BeginDispatch(in AlsFrameResult result)
    {
        ValidateDispatch(result);
        Identity = result.Identity;
    }

    public void Observe(in AlsAnimationEvent item)
    {
        if (Closed) throw new InvalidOperationException("Retired callback owner.");
        Apply(ref _states, item);
    }
    public void Observe(in AlsActionOutcome outcome)
    {
        if (Closed) throw new InvalidOperationException("Retired callback owner.");
        Apply(ref _actions, outcome);
    }

    public AlsFrameResult Close(AlsActionResultCode reason)
    {
        if (reason is not (AlsActionResultCode.InterruptedByLifecycle or AlsActionResultCode.InterruptedByGeneration))
            throw new ArgumentOutOfRangeException(nameof(reason));
        var result = new AlsFrameResult { Identity = Identity, ActionPlayback = AlsActionPlayback.CreateDefault() };
        if (Closed) return result;
        var states = _states;
        if (!states.TryAppendSyntheticEnds(reason, Identity, ref result.TypedEvents))
            throw new InvalidOperationException("Committed Notify teardown overflow or invalid ownership.");
        for (var i = 0; i < _actions.Count; i++)
            if (!result.ActionOutcomes.TryAdd(_actions[i] with { ResultCode = reason }))
                throw new InvalidOperationException("Committed action teardown overflow.");
        _states = states; _actions.Clear(); Closed = true;
        return result;
    }

    private static void Apply(ref AlsCommittedNotifyStateBuffer states, in AlsAnimationEvent item)
    {
        if (item.Phase == AlsAnimationEventPhase.Trigger) return;
        if (!item.NativeContext.Present && item.Phase == AlsAnimationEventPhase.Tick) return;
        if (!(item.NativeContext.Present ? states.TryApplyNative(item) : states.TryApply(item)))
            throw new InvalidOperationException("Committed Notify ownership differs from Begin/Tick/End dispatch.");
    }

    private static void Apply(ref AlsActionOutcomeBuffer actions, in AlsActionOutcome item)
    {
        if (item.ResultCode == AlsActionResultCode.Accepted)
        {
            if (item.RequestId <= 0 || item.PlaybackEpoch <= 0 || item.ActionDefinitionId < 0)
                throw new InvalidOperationException("Invalid committed action owner.");
            for (var i = 0; i < actions.Count; i++)
                if (actions[i].PlaybackEpoch == item.PlaybackEpoch)
                    throw new InvalidOperationException("Duplicate committed action acceptance.");
            if (!actions.TryAdd(item)) throw new InvalidOperationException("Committed action ownership capacity exceeded.");
        }
        else if (item.ResultCode == AlsActionResultCode.Completed || item.ResultCode >= AlsActionResultCode.InterruptedByReplacement)
        {
            var remaining = new AlsActionOutcomeBuffer(); var found = false;
            for (var i = 0; i < actions.Count; i++)
            {
                var old = actions[i];
                if (old.RequestId == item.RequestId && old.ActionDefinitionId == item.ActionDefinitionId && old.PlaybackEpoch == item.PlaybackEpoch)
                    found = true;
                else remaining.TryAdd(old);
            }
            if (!found) throw new InvalidOperationException("Action termination has no committed owner.");
            actions = remaining;
        }
    }
}
