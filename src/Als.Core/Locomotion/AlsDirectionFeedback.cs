using System.Runtime.CompilerServices;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsDirectionFeedbackState(bool Pivot, bool DelayPending, float DelayRemaining, int TrackedHips);
public readonly record struct AlsDirectionFeedbackEvent(int NotifyIndex, int HipsValue, bool Pivot);
[InlineArray(8)] internal struct AlsDirectionFeedbackBuffer { private AlsDirectionFeedbackEvent _element; }

public struct AlsDirectionFeedbackEvents
{
    private AlsDirectionFeedbackBuffer _events;
    public int Count { get; private set; }
    public readonly AlsDirectionFeedbackEvent this[int index] => (uint)index < Count
        ? _events[index] : throw new ArgumentOutOfRangeException(nameof(index));
    public void Add(AlsDirectionFeedbackEvent value)
    {
        if (Count == 8) throw new InvalidOperationException("Directional state event capacity exceeded.");
        if (value.NotifyIndex < 0 || !value.Pivot && (uint)value.HipsValue >= 6)
            throw new ArgumentException("Invalid directional event.");
        _events[Count++] = value;
    }
}

public readonly record struct AlsDirectionFeedbackTransition(AlsCycleDirection From, AlsCycleDirection To,
    bool HipTransition, int StartNotify);

public sealed class AlsDirectionFeedbackDefinition
{
    private readonly AlsDirectionFeedbackEvent[] _entries;
    private readonly AlsDirectionFeedbackTransition[] _transitions;
    public float SpeedLimit { get; }
    public float DelaySeconds { get; }
    public AlsDirectionFeedbackDefinition(float speedLimit, float delaySeconds, AlsDirectionFeedbackEvent[] entries,
        AlsDirectionFeedbackTransition[] transitions)
    {
        if (!float.IsFinite(speedLimit) || speedLimit < 0 || !float.IsFinite(delaySeconds) || delaySeconds < 0 ||
            entries.Length != 6 || entries.Any(e => e.NotifyIndex < 0 || e.Pivot || (uint)e.HipsValue >= 6) ||
            transitions.Any(t => (uint)t.From >= 6 || (uint)t.To >= 6 || t.From == t.To || t.StartNotify < -1))
            throw new ArgumentException("Invalid directional feedback definition.");
        SpeedLimit = speedLimit; DelaySeconds = delaySeconds;
        _entries = (AlsDirectionFeedbackEvent[])entries.Clone();
        _transitions = (AlsDirectionFeedbackTransition[])transitions.Clone();
    }

    public void Enter(AlsCycleDirection direction, ref AlsDirectionFeedbackEvents events)
    {
        if ((uint)direction >= 6) throw new ArgumentOutOfRangeException(nameof(direction));
        events.Add(_entries[(int)direction]);
    }

    public void Transition(AlsCycleDirection from, AlsCycleDirection to, bool hip, bool skipStart,
        ref AlsDirectionFeedbackEvents events)
    {
        var found = false; var notify = -1;
        foreach (var transition in _transitions)
        {
            if (transition.From != from || transition.To != to || transition.HipTransition != hip) continue;
            if (found && notify != transition.StartNotify) throw new InvalidOperationException("Ambiguous directional event.");
            found = true; notify = transition.StartNotify;
        }
        if (!found) throw new InvalidOperationException("Directional transition has no authored event binding.");
        // UE queues target StateEntered before TransitionStarted, including skipped first blends.
        var candidate = events;
        Enter(to, ref candidate);
        if (!skipStart && notify >= 0) candidate.Add(new(notify, 0, true));
        events = candidate;
    }
}

public static class AlsDirectionFeedback
{
    // Called after graph update/notify dispatch, then advanced at the world's latent-action phase.
    public static AlsDirectionFeedbackState Complete(in AlsDirectionFeedbackState previous,
        in AlsDirectionFeedbackEvents events, float speed, float delta, AlsDirectionFeedbackDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!float.IsFinite(speed) || speed < 0 || !float.IsFinite(delta) || delta < 0 ||
            !float.IsFinite(previous.DelayRemaining) || previous.DelayRemaining < 0 || (uint)previous.TrackedHips >= 6 ||
            previous.DelayPending != (previous.DelayRemaining > 0)) throw new ArgumentException("Invalid feedback frame.");
        var state = previous;
        for (var i = 0; i < events.Count; i++)
        {
            var entry = events[i];
            if (!entry.Pivot) { state = state with { TrackedHips = entry.HipsValue }; continue; }
            state = state with { Pivot = speed < definition.SpeedLimit };
            // Kismet Delay ignores another invocation while the same latent action exists.
            if (!state.DelayPending) state = state with { DelayPending = true, DelayRemaining = definition.DelaySeconds };
        }
        if (state.DelayPending)
        {
            var remaining = state.DelayRemaining - delta;
            state = remaining <= 0 ? state with { Pivot = false, DelayPending = false, DelayRemaining = 0 }
                : state with { DelayRemaining = remaining };
        }
        return state;
    }
}
