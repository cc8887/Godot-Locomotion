using System.Runtime.CompilerServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsDetailMachineInput(AlsGait Gait, float WeightGait, bool Pivot,
    float RelevantTimeRemainingSeconds, float MainGroundedWeight, float DetailWeight, float ContextWeight);

public struct AlsDetailMachineState
{
    public AlsTransitionStackState Transitions { get; internal set; }
    public float ElapsedSeconds { get; internal set; }
    public long LastUpdateSerial { get; internal set; }
    public AlsGraphTraversalCounter? LastUpdateCounter { get; internal set; }
    public bool HasUpdated { get; internal set; }
    public bool HasInitialized { get; internal set; }
    public readonly AlsDetailState CurrentState => (AlsDetailState)Transitions.CurrentState;
}

public readonly record struct AlsDetailStateUpdate(AlsDetailState State, float Weight, bool InertializationSync);
[InlineArray(6)]
internal struct AlsDetailStateUpdateBuffer { private AlsDetailStateUpdate _element; }
[InlineArray(2)]
internal struct AlsDetailInitializationBuffer { private AlsDetailState _element; }

public struct AlsDetailMachineUpdate
{
    internal AlsDetailStateUpdateBuffer Updates;
    internal AlsDetailInitializationBuffer Initializations;
    public AlsDetailMachineState State { get; internal set; }
    public int FirstEdge { get; internal set; }
    public int TerminalEdge { get; internal set; }
    public float InertializationSeconds { get; internal set; }
    public bool Reinitialized { get; internal set; }
    public byte InitializeStates { get; internal set; }
    public int InitializationCount { get; internal set; }
    public byte ClearCachedWeightStates { get; internal set; }
    public int UpdateCount { get; internal set; }
    public readonly AlsDetailStateUpdate GetUpdate(int index) => (uint)index < UpdateCount
        ? Updates[index] : throw new ArgumentOutOfRangeException(nameof(index));
    public readonly AlsDetailState GetInitialization(int index) => (uint)index < InitializationCount
        ? Initializations[index] : throw new ArgumentOutOfRangeException(nameof(index));

    internal void Initialize(AlsDetailState state)
    {
        // The native Detail machine allows one transition after optional whole-machine initialization.
        if (InitializationCount == 2) throw new InvalidOperationException("Detail initialization capacity exceeded.");
        InitializeStates |= (byte)(1 << (int)state);
        Initializations[InitializationCount++] = state;
    }

    internal void AddUpdate(AlsDetailState state, float weight, bool inertialization)
    {
        for (var i = 0; i < UpdateCount; i++) if (Updates[i].State == state) return;
        Updates[UpdateCount++] = new(state, weight, inertialization);
    }
}

public readonly record struct AlsDetailPlayerObservation(float LengthSeconds, float AdjustedTimeSeconds, float CachedWeight);

/// <summary>V4 Detail policy: one transition per update, first-update transitions enabled,
/// reset on relevance gaps, no forced state reentry resets. Source clocks remain owned by playback/sync.</summary>
public static class AlsLocomotionDetailMachine
{
    public static AlsDetailMachineUpdate Initialize()
    {
        var result = new AlsDetailMachineUpdate
        {
            State = new() { HasInitialized = true, Transitions = AlsTransitionStack.Initialize((int)AlsDetailState.Walking) },
            Reinitialized = true, FirstEdge = -1, TerminalEdge = -1, InertializationSeconds = -1,
            ClearCachedWeightStates = 1 << (int)AlsDetailState.Walking
        };
        result.Initialize(AlsDetailState.Walking);
        return result;
    }

    public static AlsDetailMachineUpdate Update(ReadOnlySpan<AlsDetailTransitionDefinition> definitions,
        in AlsDetailMachineState previous, in AlsDetailMachineInput input, float delta, long updateSerial,
        AlsGraphTraversalCounter? updateCounter = null)
    {
        if(updateCounter is {HasUpdated:false} || previous.HasUpdated &&
            previous.LastUpdateCounter.HasValue!=updateCounter.HasValue)
            throw new ArgumentException("Detail machine update traversal ownership differs.");
        if (!float.IsFinite(delta) || delta < 0 || updateSerial < 0 ||
            !float.IsFinite(input.ContextWeight) || input.ContextWeight is < 0 or > 1 ||
            previous.HasUpdated && updateSerial < previous.LastUpdateSerial)
            throw new ArgumentOutOfRangeException(nameof(delta));
        foreach (var edge in definitions)
            if ((uint)edge.From > (uint)AlsDetailState.RunConduit || (uint)edge.To > (uint)AlsDetailState.RunConduit ||
                edge.From == edge.To || !float.IsFinite(edge.DurationSeconds) || edge.DurationSeconds < 0 ||
                (uint)edge.Logic > (uint)AlsDetailTransitionLogic.Inertialization ||
                (uint)edge.Condition > (uint)AlsDetailCondition.DetailNotFull || !float.IsFinite(edge.Threshold))
                throw new ArgumentException("Invalid Detail transition definition.");

        var reset = !previous.HasInitialized || previous.HasUpdated && (updateCounter is { } counter
            ? !previous.LastUpdateCounter!.Value.WasSynchronizedCounter(counter)
            : updateSerial - previous.LastUpdateSerial > 1);
        var result = reset ? Initialize() : new AlsDetailMachineUpdate
            { State = previous, FirstEdge = -1, TerminalEdge = -1, InertializationSeconds = -1 };
        var state = result.State;
        var ruleInput = new AlsDetailRuleInput(input.Gait, input.WeightGait, input.Pivot, state.ElapsedSeconds,
            input.RelevantTimeRemainingSeconds, input.MainGroundedWeight, input.DetailWeight);
        if (AlsLocomotionDetailRules.TrySelect(definitions, state.CurrentState, ruleInput, out var first, out var terminal))
        {
            var edge = definitions[terminal];
            var target = (int)edge.To;
            if (target != state.Transitions.CurrentState)
            {
                result.FirstEdge = first; result.TerminalEdge = terminal;
                // SetState clears cached weights even when a still-weighted state is not reinitialized.
                result.ClearCachedWeightStates |= (byte)(1 << target);
                if (AlsTransitionStack.Weight(state.Transitions, target) == 0) result.Initialize(edge.To);
                var inertial = edge.Logic == AlsDetailTransitionLogic.Inertialization;
                if (inertial) result.InertializationSeconds = edge.DurationSeconds;
                state.Transitions = AlsTransitionStack.Start(state.Transitions, target, inertial ? 0 : edge.DurationSeconds,
                    AlsTransitionBlend.HermiteCubic);
                state.ElapsedSeconds = 0;
            }
        }

        state.Transitions = AlsTransitionStack.Advance(state.Transitions, delta, out var beforeCleanup);
        // UE updates unfinished older transitions before removing those superseded by a newer completion.
        for (var i = 0; i < beforeCleanup.Count; i++)
        {
            var edge = beforeCleanup.GetTransition(i);
            if (edge.Complete) continue;
            result.AddUpdate((AlsDetailState)edge.From,
                input.ContextWeight * AlsTransitionStack.Weight(beforeCleanup, edge.From), false);
            result.AddUpdate((AlsDetailState)edge.To,
                input.ContextWeight * AlsTransitionStack.Weight(beforeCleanup, edge.To), false);
        }
        if (state.Transitions.Count == 0)
            result.AddUpdate(state.CurrentState, input.ContextWeight, result.InertializationSeconds >= 0);
        state.ElapsedSeconds += delta;
        if (!float.IsFinite(state.ElapsedSeconds)) throw new ArgumentOutOfRangeException(nameof(delta));
        state.HasUpdated = true; state.LastUpdateSerial = updateSerial; state.LastUpdateCounter=updateCounter;
        result.State = state;
        return result;
    }

    /// <summary>Observations must follow baked PlayerNodeIndices order, not F/B/L/R pin order.
    /// Cached weights and synchronized times are those visible before the current update.</summary>
    public static float RelevantTimeRemaining(ReadOnlySpan<AlsDetailPlayerObservation> observations, out int relevantIndex)
    {
        relevantIndex = -1;
        var maxWeight = 0f;
        var remaining = float.MaxValue;
        for (var i = 0; i < observations.Length; i++)
        {
            var player = observations[i];
            if (!float.IsFinite(player.LengthSeconds) || player.LengthSeconds <= 0 ||
                !float.IsFinite(player.AdjustedTimeSeconds) || player.AdjustedTimeSeconds < 0 || player.AdjustedTimeSeconds > player.LengthSeconds ||
                !float.IsFinite(player.CachedWeight) || player.CachedWeight < 0)
                throw new ArgumentException("Invalid Detail player observation.");
            if (player.CachedWeight <= maxWeight) continue;
            maxWeight = player.CachedWeight;
            remaining = player.LengthSeconds - player.AdjustedTimeSeconds;
            relevantIndex = i;
        }
        return remaining;
    }
}
