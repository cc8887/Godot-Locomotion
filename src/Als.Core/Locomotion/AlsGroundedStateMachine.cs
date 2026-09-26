using System.Runtime.CompilerServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public enum AlsGroundedMachineKind : byte { Main, Standing, Stop, Crouching, CrouchingDirection, CrouchingCycles, MainMovement, Jump, Ragdoll, RefactoredStandingDirection, RefactoredCrouchingDirection, RefactoredMovementDetails, RefactoredStanding, RefactoredStop, RefactoredCrouching, RefactoredGrounded, RefactoredLocomotion, RefactoredJump }
public enum AlsMovementStateInput : byte { None, Grounded, InAir, Ragdoll, Mantling }
public enum AlsGroundedBlendProfile : byte { None, QuickFeet, ChangeDirection }
public enum AlsGroundedCondition : byte
{
    Never, Always, ShouldMove, NotShouldMove, RotateLeft, RotateRight, MovingFullAndStopping,
    StopFull, FeetUp, FeetDown, FeetLeft, FeetRight, Standing, Crouching, NoAction, FromRoll,
    BaseStanding, BaseCrouching, MoveOrRotate, Automatic,
    DirectionForward, DirectionRight, DirectionLeft, DirectionBackward,
    HipsNegativeUncrossed, HipsPositiveUncrossed, HipsNeutralStateFullUncrossed,
    MovementGrounded, MovementNotGrounded, MovementInAir, MovementNotInAir, Jumped,
    LeaveLanding, LandMoveOrRotate, LandMoveRotateOrFast, LandAnimationFinished,
    FeetNonNegative, JumpLeftAnimationFinished, JumpRightAnimationFinished,
    MovementRagdoll, MovementNotRagdoll,
}
public readonly record struct AlsGroundedStateDefinition(bool Conduit, AlsGroundedCondition EntryCondition,
    int ExitStart, int ExitCount, int StartNotify, int EndNotify, int FullyBlendedNotify,
    bool AlwaysResetOnEntry = false)
{
    public AlsRefactoredLocomotionRule? RefactoredLocomotionEntryRule { get; init; }
}
public readonly record struct AlsGroundedEdge(int From, int To, AlsGroundedCondition Condition,
    float Threshold, float Duration, AlsTransitionBlend Blend, bool Inertialization,
    int StartNotify, int EndNotify, int InterruptNotify, AlsGroundedBlendProfile BlendProfile = AlsGroundedBlendProfile.None,
    int WeightState = -1)
{
    public AlsRefactoredDirectionRule? RefactoredDirectionRule { get; init; }
    public AlsRefactoredMovementDetailsRule? RefactoredMovementDetailsRule { get; init; }
    public AlsRefactoredStandingRule? RefactoredStandingRule { get; init; }
    public AlsRefactoredStopRule? RefactoredStopRule { get; init; }
    public AlsRefactoredCrouchingRule? RefactoredCrouchingRule { get; init; }
    public AlsRefactoredGroundedRule? RefactoredGroundedRule { get; init; }
    public AlsRefactoredLocomotionRule? RefactoredLocomotionRule { get; init; }
}

public sealed class AlsGroundedMachineDefinition
{
    private readonly AlsGroundedStateDefinition[] _states;
    private readonly AlsGroundedEdge[] _edges;
    public AlsGroundedMachineKind Kind { get; }
    public int InitialState { get; }
    public int MaxTransitionsPerFrame { get; }
    public bool SkipFirstBlend { get; }
    public bool UsesRefactoredDirectionRules => Kind is AlsGroundedMachineKind.RefactoredStandingDirection or AlsGroundedMachineKind.RefactoredCrouchingDirection;
    public bool UsesRefactoredMovementDetailsRules => Kind == AlsGroundedMachineKind.RefactoredMovementDetails;
    public bool UsesRefactoredStandingRules => Kind == AlsGroundedMachineKind.RefactoredStanding;
    public bool UsesRefactoredStopRules => Kind == AlsGroundedMachineKind.RefactoredStop;
    public bool UsesRefactoredCrouchingRules => Kind == AlsGroundedMachineKind.RefactoredCrouching;
    public bool UsesRefactoredGroundedRules => Kind == AlsGroundedMachineKind.RefactoredGrounded;
    public bool UsesRefactoredLocomotionRules => Kind is AlsGroundedMachineKind.RefactoredLocomotion or AlsGroundedMachineKind.RefactoredJump;
    public ReadOnlySpan<AlsGroundedStateDefinition> States => _states;
    public ReadOnlySpan<AlsGroundedEdge> Edges => _edges;

    public AlsGroundedMachineDefinition(AlsGroundedMachineKind kind, int initialState, int maxTransitions,
        bool skipFirstBlend, AlsGroundedStateDefinition[] states, AlsGroundedEdge[] edges)
    {
        var direction = kind is AlsGroundedMachineKind.RefactoredStandingDirection or AlsGroundedMachineKind.RefactoredCrouchingDirection;
        var details = kind == AlsGroundedMachineKind.RefactoredMovementDetails;
        var standing = kind == AlsGroundedMachineKind.RefactoredStanding;
        var stop = kind == AlsGroundedMachineKind.RefactoredStop;
        var crouching = kind == AlsGroundedMachineKind.RefactoredCrouching;
        var grounded = kind == AlsGroundedMachineKind.RefactoredGrounded;
        var locomotion = kind is AlsGroundedMachineKind.RefactoredLocomotion or AlsGroundedMachineKind.RefactoredJump;
        var jump = kind == AlsGroundedMachineKind.RefactoredJump;
        if ((uint)kind > (uint)AlsGroundedMachineKind.RefactoredJump || states.Length is < 1 or > 8 || (uint)initialState >= states.Length ||
            !grounded && !jump && states[initialState].Conduit || maxTransitions is < 1 or > 3)
            throw new ArgumentException("Invalid grounded machine policy.");
        if (locomotion && (states.Length != (jump ? 5 : 6) || initialState != 0 || maxTransitions != 3 || !skipFirstBlend ||
            states.Where((s, i) => s.Conduit != (i == (jump ? 0 : 5))).Any()))
            throw new ArgumentException("Invalid original Locomotion/Jump policy.");
        if (grounded && (states.Length != 6 || initialState != 0 || !states[0].Conduit || states[0].EntryCondition != AlsGroundedCondition.Always ||
            states.Skip(1).Any(s => s.Conduit) || maxTransitions != 1 || !skipFirstBlend))
            throw new ArgumentException("Invalid original Grounded policy.");
        if (direction && (states.Length != 6 || initialState != 0 || maxTransitions != 3 || !skipFirstBlend || states.Any(s => s.Conduit)))
            throw new ArgumentException("Invalid Refactored direction machine policy.");
        if (details && (states.Length != 6 || initialState != 0 || maxTransitions != 1 || skipFirstBlend || states.Any(s => s.Conduit)))
            throw new ArgumentException("Invalid Refactored movement details policy.");
        if (standing && (states.Length != 5 || initialState != 0 || maxTransitions != 3 || !skipFirstBlend || states.Any(s => s.Conduit)))
            throw new ArgumentException("Invalid Refactored Standing policy.");
        if (stop && (states.Length != 5 || initialState != 0 || maxTransitions != 3 || skipFirstBlend || states.Any(s => s.Conduit)))
            throw new ArgumentException("Invalid Refactored Stop policy.");
        if (crouching && (states.Length != 5 || initialState != 0 || maxTransitions != 3 || !skipFirstBlend || states.Any(s => s.Conduit)))
            throw new ArgumentException("Invalid Refactored Crouching policy.");
        var nextExit = 0;
        for (var i = 0; i < states.Length; i++)
        {
            var state = states[i];
            if ((state.RefactoredLocomotionEntryRule is not null) != (locomotion && state.Conduit) ||
                locomotion && state.EntryCondition != AlsGroundedCondition.Never)
                throw new ArgumentException("Mixed Locomotion conduit rule domains.");
            if (state.ExitStart != nextExit || state.ExitCount < 0 || state.ExitCount > edges.Length - nextExit ||
                ((uint)state.EntryCondition > (uint)AlsGroundedCondition.Automatic &&
                    !(kind == AlsGroundedMachineKind.MainMovement && state.EntryCondition == AlsGroundedCondition.MovementGrounded)) ||
                state.StartNotify < -1 || state.EndNotify < -1 || state.FullyBlendedNotify < -1)
                throw new ArgumentException("Invalid grounded state.");
            for (var j = 0; j < state.ExitCount; j++)
            {
                var edge = edges[nextExit++];
                if (edge.From != i || (uint)edge.To >= states.Length || !grounded && edge.To == i ||
                    !float.IsFinite(edge.Duration) || edge.Duration < 0 || !float.IsFinite(edge.Threshold) ||
                    (uint)edge.Condition > (uint)AlsGroundedCondition.MovementNotRagdoll || (uint)edge.Blend > (uint)AlsTransitionBlend.QuadraticInOut ||
                    edge.StartNotify < -1 || edge.EndNotify < -1 || edge.InterruptNotify < -1 || (uint)edge.BlendProfile > 2 ||
                    (edge.Condition > AlsGroundedCondition.Automatic && edge.Condition <= AlsGroundedCondition.HipsNeutralStateFullUncrossed &&
                        kind != AlsGroundedMachineKind.CrouchingDirection) ||
                    (edge.Condition > AlsGroundedCondition.HipsNeutralStateFullUncrossed && edge.Condition <= AlsGroundedCondition.LandAnimationFinished &&
                        kind != AlsGroundedMachineKind.MainMovement) ||
                    (edge.Condition > AlsGroundedCondition.LandAnimationFinished && edge.Condition <= AlsGroundedCondition.JumpRightAnimationFinished &&
                        kind != AlsGroundedMachineKind.Jump) ||
                    (edge.Condition > AlsGroundedCondition.JumpRightAnimationFinished && kind != AlsGroundedMachineKind.Ragdoll) ||
                    (edge.Condition == AlsGroundedCondition.HipsNeutralStateFullUncrossed
                        ? (uint)edge.WeightState >= states.Length : edge.WeightState != -1))
                    throw new ArgumentException("Invalid grounded transition.");
                if ((edge.RefactoredGroundedRule is not null) != grounded || grounded &&
                    (edge.Condition is not (AlsGroundedCondition.Never or AlsGroundedCondition.Automatic) ||
                     edge.Blend is not (AlsTransitionBlend.HermiteCubic or AlsTransitionBlend.Custom or AlsTransitionBlend.Cubic) ||
                     edge.BlendProfile != AlsGroundedBlendProfile.None || edge.To == 0))
                    throw new ArgumentException("Mixed original Grounded rule domains.");
                if ((edge.RefactoredLocomotionRule is not null) != locomotion || locomotion &&
                    (edge.Condition is not (AlsGroundedCondition.Never or AlsGroundedCondition.Automatic) ||
                     edge.Blend is not (AlsTransitionBlend.HermiteCubic or AlsTransitionBlend.Cubic) ||
                     edge.BlendProfile == AlsGroundedBlendProfile.ChangeDirection))
                    throw new ArgumentException("Mixed original Locomotion rule domains.");
                if (edge.RefactoredDirectionRule.HasValue != direction || direction &&
                    (edge.Condition != AlsGroundedCondition.Never || edge.Inertialization || edge.Blend == AlsTransitionBlend.Custom ||
                     !Enum.IsDefined(edge.RefactoredDirectionRule!.Value.Kind) ||
                     (edge.RefactoredDirectionRule.Value.Kind == AlsRefactoredDirectionRuleKind.Unlock
                         ? (uint)edge.RefactoredDirectionRule.Value.RequiredFullState >= 6 : edge.RefactoredDirectionRule.Value.RequiredFullState != -1)))
                    throw new ArgumentException("Mixed grounded rule domains.");
                if (edge.RefactoredMovementDetailsRule.HasValue != details || details &&
                    (!Enum.IsDefined(edge.RefactoredMovementDetailsRule!.Value) || edge.Blend != AlsTransitionBlend.HermiteCubic ||
                     edge.BlendProfile != AlsGroundedBlendProfile.None || edge.Condition !=
                        (edge.RefactoredMovementDetailsRule == AlsRefactoredMovementDetailsRule.AutomaticRemainingTime ? AlsGroundedCondition.Automatic : AlsGroundedCondition.Never)))
                    throw new ArgumentException("Mixed movement details rule domains.");
                if (edge.RefactoredStandingRule.HasValue != standing || standing &&
                    (!Enum.IsDefined(edge.RefactoredStandingRule!.Value) || edge.Blend != AlsTransitionBlend.HermiteCubic ||
                     edge.BlendProfile != AlsGroundedBlendProfile.None || edge.Condition !=
                     (edge.RefactoredStandingRule == AlsRefactoredStandingRule.Automatic ? AlsGroundedCondition.Automatic : AlsGroundedCondition.Never)))
                    throw new ArgumentException("Mixed Standing rule domains.");
                if (edge.RefactoredStopRule.HasValue != stop || stop &&
                    (!Enum.IsDefined(edge.RefactoredStopRule!.Value) || edge.Blend != AlsTransitionBlend.HermiteCubic ||
                     edge.BlendProfile != AlsGroundedBlendProfile.None || edge.Inertialization || edge.Condition != AlsGroundedCondition.Never))
                    throw new ArgumentException("Mixed Stop rule domains.");
                if (edge.RefactoredCrouchingRule.HasValue != crouching || crouching &&
                    (!Enum.IsDefined(edge.RefactoredCrouchingRule!.Value) || edge.Blend != AlsTransitionBlend.HermiteCubic ||
                     edge.BlendProfile != (edge.RefactoredCrouchingRule == AlsRefactoredCrouchingRule.StopFull ? AlsGroundedBlendProfile.QuickFeet : AlsGroundedBlendProfile.None) ||
                     edge.Condition != (edge.RefactoredCrouchingRule == AlsRefactoredCrouchingRule.Automatic ? AlsGroundedCondition.Automatic : AlsGroundedCondition.Never)))
                    throw new ArgumentException("Mixed Crouching rule domains.");
            }
        }
        if (nextExit != edges.Length) throw new ArgumentException("Unowned grounded transition.");
        Kind = kind; InitialState = initialState; MaxTransitionsPerFrame = maxTransitions; SkipFirstBlend = skipFirstBlend;
        _states = (AlsGroundedStateDefinition[])states.Clone(); _edges = (AlsGroundedEdge[])edges.Clone();
    }
}

public readonly record struct AlsGroundedRuleInput(bool ShouldMove, bool RotateLeft, bool RotateRight,
    AlsStance Stance, bool NoMovementAction, bool FromRoll, float BasePoseClf, float FeetPosition)
{
    public AlsMovementDirection MovementDirection { get; init; }
    public float HipBias { get; init; }
    public float FeetCrossing { get; init; }
    public AlsMovementStateInput MovementState { get; init; }
    public bool Jumped { get; init; }
    public bool HasMovementInput { get; init; }
    public float Speed { get; init; }
    public float RelevantLandTimeRemaining { get; init; }
    public float RelevantJumpLeftTimeRemaining { get; init; }
    public float RelevantJumpRightTimeRemaining { get; init; }
}

// One observation per content state, selected in baked player order using the previous cached weights.
// Automatic exit rules use raw accumulated time, unlike GetRelevantAnimTimeRemaining's rate-adjusted time.
public readonly record struct AlsGroundedAutomaticTime(bool HasAsset, float Length, float Time,
    bool Loop, bool PreviousValid, float PreviousTime, float Delta);
public enum AlsGroundedEventKind : byte { StateEntered, StateExited, StateFullyBlended, TransitionStarted, TransitionEnded, TransitionInterrupted }
public readonly record struct AlsGroundedMachineEvent(AlsGroundedEventKind Kind, int SourceIndex, int NotifyIndex);
public readonly record struct AlsGroundedStateUpdate(int State, float Weight, bool InertializationSync);
[InlineArray(128)] internal struct AlsGroundedActiveEdges { private int _element; }
[InlineArray(32)] internal struct AlsGroundedEvents { private AlsGroundedMachineEvent _element; }
[InlineArray(8)] internal struct AlsGroundedUpdates { private AlsGroundedStateUpdate _element; }
[InlineArray(4)] internal struct AlsGroundedInitializations { private int _element; }
[InlineArray(3)] internal struct AlsGroundedTakenEdges { private int _element; }

public struct AlsGroundedMachineState
{
    public AlsGroundedMachineKind Kind { get; internal set; }
    public AlsTransitionStackState Transitions { get; internal set; }
    public float RecordedWeight { get; internal set; }
    public float ElapsedSeconds { get; internal set; }
    public long LastUpdateSerial { get; internal set; }
    public AlsGraphTraversalCounter? LastUpdateCounter { get; internal set; }
    public bool HasUpdated { get; internal set; }
    public bool HasInitialized { get; internal set; }
    internal AlsGroundedActiveEdges ActiveEdges;
    public readonly int CurrentState => Transitions.CurrentState;
    public readonly int GetActiveEdge(int index) => (uint)index < Transitions.Count
        ? ActiveEdges[index] : throw new ArgumentOutOfRangeException(nameof(index));
    public readonly bool Matches(in AlsGroundedMachineState other)
    {
        if (Kind != other.Kind || RecordedWeight != other.RecordedWeight || ElapsedSeconds != other.ElapsedSeconds ||
            LastUpdateSerial != other.LastUpdateSerial || LastUpdateCounter != other.LastUpdateCounter ||
            HasUpdated != other.HasUpdated || HasInitialized != other.HasInitialized ||
            Transitions.CurrentState != other.Transitions.CurrentState || Transitions.Count != other.Transitions.Count ||
            Transitions.Latest != other.Transitions.Latest) return false;
        for (var i = 0; i < Transitions.Count; i++)
            if (Transitions.GetTransition(i) != other.Transitions.GetTransition(i) || ActiveEdges[i] != other.ActiveEdges[i]) return false;
        return true;
    }
}

public struct AlsGroundedMachineUpdate
{
    public AlsGroundedMachineState State { get; internal set; }
    public bool Reinitialized { get; internal set; }
    public byte InitializeStates { get; internal set; }
    public int InitializationCount { get; internal set; }
    public byte ClearCachedWeightStates { get; internal set; }
    public int TransitionCount { get; internal set; }
    public int EventCount { get; internal set; }
    public int UpdateCount { get; internal set; }
    public float InertializationSeconds { get; internal set; }
    internal AlsGroundedEvents Events;
    internal AlsGroundedUpdates Updates;
    internal AlsGroundedInitializations Initializations;
    internal AlsGroundedTakenEdges TakenEdges;
    public readonly int GetTransitionIndex(int i) => (uint)i < TransitionCount ? TakenEdges[i] : throw new ArgumentOutOfRangeException(nameof(i));
    public readonly AlsGroundedMachineEvent GetEvent(int i) => (uint)i < EventCount ? Events[i] : throw new ArgumentOutOfRangeException(nameof(i));
    public readonly AlsGroundedStateUpdate GetUpdate(int i) => (uint)i < UpdateCount ? Updates[i] : throw new ArgumentOutOfRangeException(nameof(i));
    public readonly int GetInitialization(int i) => (uint)i < InitializationCount ? Initializations[i] : throw new ArgumentOutOfRangeException(nameof(i));
    internal void Initialize(int state)
    {
        if (InitializationCount == 4) throw new InvalidOperationException("Grounded initialization capacity exceeded.");
        InitializeStates |= (byte)(1 << state);
        Initializations[InitializationCount++] = state;
    }
    internal void Notify(AlsGroundedEventKind kind, int source, int notify)
    {
        if (notify < 0) return;
        if (EventCount == 32) throw new InvalidOperationException("Grounded state event capacity exceeded.");
        Events[EventCount++] = new(kind, source, notify);
    }
    internal void AddUpdate(int state, float weight, bool inertial)
    {
        for (var i = 0; i < UpdateCount; i++) if (Updates[i].State == state) return;
        Updates[UpdateCount++] = new(state, weight, inertial);
    }
}

public static class AlsGroundedStateMachine
{
    public static AlsGroundedMachineUpdate Initialize(AlsGroundedMachineDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var state = new AlsGroundedMachineState
        {
            Kind = definition.Kind, Transitions = AlsTransitionStack.Initialize(definition.InitialState), HasInitialized = true
        };
        var result = new AlsGroundedMachineUpdate { State = state, Reinitialized = true, InertializationSeconds = -1 };
        if (!definition.States[state.CurrentState].Conduit) result.Initialize(state.CurrentState);
        result.ClearCachedWeightStates = (byte)(1 << state.CurrentState);
        result.Notify(AlsGroundedEventKind.StateEntered, state.CurrentState, definition.States[state.CurrentState].StartNotify);
        return result;
    }

    public static AlsGroundedMachineUpdate Update(AlsGroundedMachineDefinition definition,
        in AlsGroundedMachineState previous, in AlsGroundedRuleInput input,
        ReadOnlySpan<AlsGroundedAutomaticTime> times, float contextWeight, float delta, long serial,
        Func<float, float>? changeStanceCurve = null, AlsGraphTraversalCounter? updateCounter = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.UsesRefactoredDirectionRules || definition.UsesRefactoredMovementDetailsRules || definition.UsesRefactoredStandingRules || definition.UsesRefactoredStopRules || definition.UsesRefactoredCrouchingRules || definition.UsesRefactoredGroundedRules || definition.UsesRefactoredLocomotionRules) throw new ArgumentException("Refactored machine requires its original input domain.");
        return UpdateCore(definition, previous, input, null, times, contextWeight, delta, serial, changeStanceCurve, updateCounter);
    }

    public static AlsGroundedMachineUpdate UpdateRefactoredDirection(AlsGroundedMachineDefinition definition,
        in AlsGroundedMachineState previous, in AlsRefactoredDirectionInput input, float contextWeight, float delta, long serial,
        AlsGraphTraversalCounter? updateCounter = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!definition.UsesRefactoredDirectionRules || !float.IsFinite(input.HipsLock) || !float.IsFinite(input.FeetCrossing))
            throw new ArgumentException("Invalid original direction input domain.");
        Span<AlsGroundedAutomaticTime> times = stackalloc AlsGroundedAutomaticTime[6]; times.Clear();
        return UpdateCore(definition, previous, default, input, times, contextWeight, delta, serial, null, updateCounter);
    }

    public static AlsGroundedMachineUpdate UpdateRefactoredMovementDetails(AlsGroundedMachineDefinition definition,
        in AlsGroundedMachineState previous, in AlsRefactoredMovementDetailsInput input,
        ReadOnlySpan<AlsGroundedAutomaticTime> times, float contextWeight, float delta, long serial,
        AlsGraphTraversalCounter? updateCounter = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!definition.UsesRefactoredMovementDetailsRules || !float.IsFinite(input.GroundedAmount) ||
            !float.IsFinite(input.UnweightedGaitRunningAmount) || !float.IsFinite(input.StandingMachineWeight))
            throw new ArgumentException("Invalid original movement details input domain.");
        return UpdateCore(definition, previous, default, null, times, contextWeight, delta, serial, null, updateCounter, input);
    }

    public static AlsGroundedMachineUpdate UpdateRefactoredStanding(AlsGroundedMachineDefinition definition,
        in AlsGroundedMachineState previous, in AlsRefactoredStandingInput input,
        ReadOnlySpan<AlsGroundedAutomaticTime> times, float contextWeight, float delta, long serial,
        AlsGraphTraversalCounter? updateCounter = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!definition.UsesRefactoredStandingRules) throw new ArgumentException("Invalid Standing input domain.");
        return UpdateCore(definition, previous, default, null, times, contextWeight, delta, serial, null, updateCounter, standing: input);
    }

    public static AlsGroundedMachineUpdate UpdateRefactoredStop(AlsGroundedMachineDefinition definition,
        in AlsGroundedMachineState previous, float footPlantedAmount, float contextWeight, float delta, long serial,
        AlsGraphTraversalCounter? updateCounter = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!definition.UsesRefactoredStopRules || !float.IsFinite(footPlantedAmount)) throw new ArgumentException("Invalid Stop input domain.");
        Span<AlsGroundedAutomaticTime> times = stackalloc AlsGroundedAutomaticTime[5]; times.Clear();
        return UpdateCore(definition, previous, default, null, times, contextWeight, delta, serial, null, updateCounter, stop: footPlantedAmount);
    }

    public static AlsGroundedMachineUpdate UpdateRefactoredCrouching(AlsGroundedMachineDefinition definition,
        in AlsGroundedMachineState previous, in AlsRefactoredCrouchingInput input,
        ReadOnlySpan<AlsGroundedAutomaticTime> times, float contextWeight, float delta, long serial,
        AlsGraphTraversalCounter? updateCounter = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!definition.UsesRefactoredCrouchingRules) throw new ArgumentException("Invalid Crouching input domain.");
        return UpdateCore(definition, previous, default, null, times, contextWeight, delta, serial, null, updateCounter, crouching: input);
    }

    public static AlsGroundedMachineUpdate UpdateRefactoredGrounded(AlsGroundedMachineDefinition definition,
        in AlsGroundedMachineState previous, in AlsRefactoredGroundedInput input,
        ReadOnlySpan<AlsGroundedAutomaticTime> times, float contextWeight, float delta, long serial,
        Func<float, float> stanceCurve, AlsGraphTraversalCounter? updateCounter = null)
    {
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(stanceCurve);
        if (!definition.UsesRefactoredGroundedRules || !Enum.IsDefined(input.Stance) ||
            !float.IsFinite(input.StandingAmount) || !float.IsFinite(input.CrouchingAmount))
            throw new ArgumentException("Invalid original Grounded input domain.");
        return UpdateCore(definition, previous, default, null, times, contextWeight, delta, serial, stanceCurve, updateCounter, grounded: input);
    }

    public static AlsGroundedMachineUpdate UpdateRefactoredLocomotion(AlsGroundedMachineDefinition definition,
        in AlsGroundedMachineState previous, in AlsRefactoredLocomotionInput input,
        ReadOnlySpan<AlsGroundedAutomaticTime> times, float contextWeight, float delta, long serial,
        AlsGraphTraversalCounter? updateCounter = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!definition.UsesRefactoredLocomotionRules || !Enum.IsDefined(input.Mode) || !Enum.IsDefined(input.Stance) ||
            !float.IsFinite(input.Speed) || input.Speed < 0 || !float.IsFinite(input.FootPlanted))
            throw new ArgumentException("Invalid original Locomotion input domain.");
        return UpdateCore(definition, previous, default, null, times, contextWeight, delta, serial, null, updateCounter, locomotion: input);
    }

    private static AlsGroundedMachineUpdate UpdateCore(AlsGroundedMachineDefinition definition,
        in AlsGroundedMachineState previous, in AlsGroundedRuleInput input, AlsRefactoredDirectionInput? direction,
        ReadOnlySpan<AlsGroundedAutomaticTime> times, float contextWeight, float delta, long serial,
        Func<float, float>? changeStanceCurve, AlsGraphTraversalCounter? updateCounter, AlsRefactoredMovementDetailsInput? details = null,
        AlsRefactoredStandingInput? standing = null, float? stop = null, AlsRefactoredCrouchingInput? crouching = null,
        AlsRefactoredGroundedInput? grounded = null, AlsRefactoredLocomotionInput? locomotion = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if(updateCounter is {HasUpdated:false} || previous.HasUpdated &&
            previous.LastUpdateCounter.HasValue!=updateCounter.HasValue)
            throw new ArgumentException("Grounded machine update traversal ownership differs.");
        if (!float.IsFinite(delta) || delta < 0 || serial < 0 ||
            previous.HasUpdated && serial <= previous.LastUpdateSerial || previous.HasInitialized &&
            (previous.Kind != definition.Kind || (uint)previous.CurrentState >= definition.States.Length) ||
            !float.IsFinite(contextWeight) || contextWeight is < 0 or > 1 ||
            !float.IsFinite(input.FeetPosition) || !float.IsFinite(input.BasePoseClf) || (uint)input.Stance > 1 ||
            !float.IsFinite(input.HipBias) || !float.IsFinite(input.FeetCrossing) || (uint)input.MovementDirection > 3 ||
            definition.Kind == AlsGroundedMachineKind.MainMovement && ((uint)input.MovementState > 4 ||
                !float.IsFinite(input.Speed) || input.Speed < 0 || !float.IsFinite(input.RelevantLandTimeRemaining)) ||
            definition.Kind == AlsGroundedMachineKind.Jump &&
                (!float.IsFinite(input.RelevantJumpLeftTimeRemaining) || !float.IsFinite(input.RelevantJumpRightTimeRemaining)) ||
            times.Length != definition.States.Length)
            throw new ArgumentException("Invalid grounded machine update.");
        foreach (var time in times)
            if (!float.IsFinite(time.Length) || !float.IsFinite(time.Time) || !float.IsFinite(time.PreviousTime) ||
                !float.IsFinite(time.Delta) || time.HasAsset && (time.Length <= 0 || time.Time < 0 || time.Time > time.Length ||
                    time.PreviousValid && (time.PreviousTime < 0 || time.PreviousTime > time.Length)))
                throw new ArgumentException("Invalid automatic transition observation.");
        var reset = !previous.HasInitialized || previous.HasUpdated && (updateCounter is { } counter
            ? !previous.LastUpdateCounter!.Value.WasSynchronizedCounter(counter)
            : serial - previous.LastUpdateSerial > 1);
        var firstUpdate = reset || !previous.HasUpdated;
        // AnimInstance's getter reads the proxy's recorded weights from the
        // preceding update, not the in-progress transition stack of this frame.
        Span<float> recordedWeights = stackalloc float[definition.States.Length]; recordedWeights.Clear();
        if ((direction.HasValue || standing.HasValue || crouching.HasValue) && !firstUpdate)
            for (var s = 0; s < recordedWeights.Length; s++) recordedWeights[s] = AlsTransitionStack.Weight(previous.Transitions, s);
        var result = reset ? Initialize(definition) : new AlsGroundedMachineUpdate { State = previous, InertializationSeconds = -1 };
        var state = result.State;
        for (var i = 0; i < definition.MaxTransitionsPerFrame; i++)
        {
            var visited = 0;
            // Initial state players have no cached update weight until the first Update, even if Initialize ran earlier.
            var clearedStates = (byte)(result.ClearCachedWeightStates | (firstUpdate ? 1 << definition.InitialState : 0));
            if (!Find(definition, state.CurrentState, previous, firstUpdate, input, direction, details, standing, stop, crouching, grounded, locomotion, recordedWeights, times, clearedStates,
                ref visited, out var edgeIndex, out var adjustment)) break;
            var edge = definition.Edges[edgeIndex];
            if (edge.To == state.CurrentState) break;
            if (state.Transitions.Count > 0)
            {
                var interrupted = state.ActiveEdges[state.Transitions.Count - 1];
                result.Notify(AlsGroundedEventKind.TransitionInterrupted, interrupted, definition.Edges[interrupted].InterruptNotify);
            }
            result.Notify(AlsGroundedEventKind.StateExited, state.CurrentState, definition.States[state.CurrentState].EndNotify);
            result.Notify(AlsGroundedEventKind.StateEntered, edge.To, definition.States[edge.To].StartNotify);
            result.ClearCachedWeightStates |= (byte)(1 << edge.To);
            if (AlsTransitionStack.Weight(state.Transitions, edge.To) == 0 || definition.States[edge.To].AlwaysResetOnEntry)
                result.Initialize(edge.To);
            var oldCount = state.Transitions.Count;
            state.Transitions = AlsTransitionStack.Start(state.Transitions, edge.To,
                edge.Inertialization ? 0 : MathF.Max(0, edge.Duration - adjustment), edge.Blend);
            state.ActiveEdges[oldCount] = edgeIndex;
            if (edge.Inertialization)
                result.InertializationSeconds = result.InertializationSeconds < 0 ? edge.Duration : MathF.Min(result.InertializationSeconds, edge.Duration);
            if (!firstUpdate || !definition.SkipFirstBlend)
                result.Notify(AlsGroundedEventKind.TransitionStarted, edgeIndex, edge.StartNotify);
            state.ElapsedSeconds = 0;
            result.TakenEdges[result.TransitionCount++] = edgeIndex;
        }
        if (firstUpdate && definition.SkipFirstBlend)
        {
            state.Transitions = AlsTransitionStack.Initialize(state.CurrentState);
            state.ActiveEdges = default;
        }
        state.Transitions = AlsTransitionStack.Advance(state.Transitions, delta, out var beforeCleanup, changeStanceCurve);
        var lastInertial = false;
        for (var i = 0; i < beforeCleanup.Count; i++)
        {
            var transition = beforeCleanup.GetTransition(i);
            if (transition.Complete)
            {
                if (i != beforeCleanup.Count - 1) continue;
                var edgeIndex = state.ActiveEdges[i]; var edge = definition.Edges[edgeIndex];
                result.Notify(AlsGroundedEventKind.TransitionEnded, edgeIndex, edge.EndNotify);
                result.Notify(AlsGroundedEventKind.StateFullyBlended, state.CurrentState, definition.States[state.CurrentState].FullyBlendedNotify);
                lastInertial = edge.Inertialization;
            }
            else
            {
                result.AddUpdate(transition.From, contextWeight * AlsTransitionStack.Weight(beforeCleanup, transition.From), false);
                result.AddUpdate(transition.To, contextWeight * AlsTransitionStack.Weight(beforeCleanup, transition.To), false);
            }
        }
        var removed = beforeCleanup.Count - state.Transitions.Count;
        for (var i = 0; i < state.Transitions.Count; i++) state.ActiveEdges[i] = state.ActiveEdges[i + removed];
        for (var i = state.Transitions.Count; i < beforeCleanup.Count; i++) state.ActiveEdges[i] = 0;
        if (state.Transitions.Count == 0) result.AddUpdate(state.CurrentState, contextWeight, lastInertial);
        state.ElapsedSeconds += delta;
        if (!float.IsFinite(state.ElapsedSeconds)) throw new ArgumentOutOfRangeException(nameof(delta));
        state.HasUpdated = true; state.LastUpdateSerial = serial; state.LastUpdateCounter=updateCounter;
        state.RecordedWeight = contextWeight; result.State = state;
        return result;
    }

    private static bool Find(AlsGroundedMachineDefinition definition, int current, in AlsGroundedMachineState previous,
        bool reset, in AlsGroundedRuleInput input, AlsRefactoredDirectionInput? direction, AlsRefactoredMovementDetailsInput? details,
        AlsRefactoredStandingInput? standing, float? stop, AlsRefactoredCrouchingInput? crouching, AlsRefactoredGroundedInput? grounded,
        AlsRefactoredLocomotionInput? locomotion, ReadOnlySpan<float> recordedWeights,
        ReadOnlySpan<AlsGroundedAutomaticTime> times, byte clearedStates,
        ref int visited, out int selected, out float adjustment)
    {
        selected = -1; adjustment = 0;
        if ((visited & (1 << current)) != 0) return false;
        visited |= 1 << current;
        var state = definition.States[current];
        if (state.Conduit && (locomotion is { } entryInput ? !state.RefactoredLocomotionEntryRule!.Evaluate(entryInput) :
            !Condition(state.EntryCondition, 0, definition.Kind, previous, reset, input))) return false;
        for (var i = state.ExitStart; i < state.ExitStart + state.ExitCount; i++)
        {
            var edge = definition.Edges[i];
            var candidateAdjustment = 0f;
            if (edge.Condition == AlsGroundedCondition.Automatic)
            {
                var time = times[current];
                if (!time.HasAsset || (clearedStates & (1 << current)) != 0) continue;
                var remaining = time.Length - time.Time;
                if (time.Loop && remaining > 0 && time.PreviousValid && (time.Time - time.PreviousTime) * time.Delta < 0) remaining = 0;
                candidateAdjustment = (edge.Threshold >= 0 ? edge.Threshold : definition.States[edge.To].Conduit ? 0 : edge.Duration) - remaining;
                if (candidateAdjustment < 0) continue;
            }
            else
            {
                var relevantState = edge.Condition switch
                {
                    AlsGroundedCondition.LandAnimationFinished => 3,
                    AlsGroundedCondition.JumpLeftAnimationFinished => 1,
                    AlsGroundedCondition.JumpRightAnimationFinished => 2,
                    _ => -1,
                };
                // SetState clears the destination asset-player weights before evaluating its exits.
                // A getter observing that state now returns MAX_flt, not its pre-entry remaining time.
                if (relevantState >= 0 && (clearedStates & (1 << relevantState)) != 0) continue;
                if (direction is { } original ? !edge.RefactoredDirectionRule!.Value.Evaluate(original, recordedWeights) :
                    details is { } movement ? !edge.RefactoredMovementDetailsRule!.Value.Evaluate(movement) :
                    standing is { } stance ? !edge.RefactoredStandingRule!.Value.Evaluate(stance, recordedWeights) :
                    stop is { } planted ? !edge.RefactoredStopRule!.Value.Evaluate(planted) :
                    crouching is { } crouch ? !edge.RefactoredCrouchingRule!.Value.Evaluate(crouch, recordedWeights) :
                    grounded is { } ground ? !edge.RefactoredGroundedRule!.Evaluate(ground) :
                    locomotion is { } outer ? !edge.RefactoredLocomotionRule!.Evaluate(outer) :
                    !Condition(edge.Condition, edge.Threshold, definition.Kind, previous, reset, input, edge.WeightState)) continue;
            }
            if (definition.States[edge.To].Conduit)
            {
                if (Find(definition, edge.To, previous, reset, input, direction, details, standing, stop, crouching, grounded, locomotion, recordedWeights, times, clearedStates, ref visited, out selected, out adjustment)) return true;
            }
            else { selected = i; adjustment = candidateAdjustment; return true; }
        }
        return false;
    }

    private static bool Condition(AlsGroundedCondition condition, float threshold, AlsGroundedMachineKind kind,
        in AlsGroundedMachineState previous, bool reset, in AlsGroundedRuleInput input, int weightState = -1) => condition switch
    {
        AlsGroundedCondition.Never => false, AlsGroundedCondition.Always => true,
        AlsGroundedCondition.ShouldMove => input.ShouldMove, AlsGroundedCondition.NotShouldMove => !input.ShouldMove,
        AlsGroundedCondition.RotateLeft => input.RotateLeft, AlsGroundedCondition.RotateRight => input.RotateRight,
        AlsGroundedCondition.DirectionForward => input.MovementDirection == AlsMovementDirection.Forward,
        AlsGroundedCondition.DirectionRight => input.MovementDirection == AlsMovementDirection.Right,
        AlsGroundedCondition.DirectionLeft => input.MovementDirection == AlsMovementDirection.Left,
        AlsGroundedCondition.DirectionBackward => input.MovementDirection == AlsMovementDirection.Backward,
        AlsGroundedCondition.HipsNegativeUncrossed => input.HipBias < -.5f && input.FeetCrossing == 0,
        AlsGroundedCondition.HipsPositiveUncrossed => input.HipBias > .5f && input.FeetCrossing == 0,
        AlsGroundedCondition.HipsNeutralStateFullUncrossed => !reset && MathF.Abs(input.HipBias) < .5f &&
            input.FeetCrossing == 0 && AlsTransitionStack.Weight(previous.Transitions, weightState) == 1,
        AlsGroundedCondition.MovingFullAndStopping => kind is AlsGroundedMachineKind.Standing or AlsGroundedMachineKind.Crouching && !reset &&
            AlsTransitionStack.Weight(previous.Transitions, 1) == threshold && !input.ShouldMove,
        AlsGroundedCondition.StopFull => kind is AlsGroundedMachineKind.Standing or AlsGroundedMachineKind.Crouching && !reset &&
            AlsTransitionStack.Weight(previous.Transitions, kind == AlsGroundedMachineKind.Crouching ? 4 : 2) == threshold,
        AlsGroundedCondition.FeetUp => MathF.Abs(input.FeetPosition) < threshold,
        AlsGroundedCondition.FeetDown => MathF.Abs(input.FeetPosition) >= threshold,
        AlsGroundedCondition.FeetLeft => input.FeetPosition < threshold,
        AlsGroundedCondition.FeetRight => input.FeetPosition > threshold,
        AlsGroundedCondition.Standing => input.Stance == AlsStance.Standing,
        AlsGroundedCondition.Crouching => input.Stance == AlsStance.Crouching,
        AlsGroundedCondition.NoAction => input.NoMovementAction, AlsGroundedCondition.FromRoll => input.FromRoll,
        AlsGroundedCondition.BaseStanding => input.BasePoseClf < threshold,
        AlsGroundedCondition.BaseCrouching => input.BasePoseClf >= threshold,
        AlsGroundedCondition.MoveOrRotate => input.ShouldMove || input.RotateLeft || input.RotateRight,
        AlsGroundedCondition.MovementGrounded => input.MovementState == AlsMovementStateInput.Grounded,
        AlsGroundedCondition.MovementNotGrounded => input.MovementState != AlsMovementStateInput.Grounded,
        AlsGroundedCondition.MovementInAir => input.MovementState == AlsMovementStateInput.InAir,
        AlsGroundedCondition.MovementNotInAir => input.MovementState != AlsMovementStateInput.InAir,
        AlsGroundedCondition.MovementRagdoll => input.MovementState == AlsMovementStateInput.Ragdoll,
        AlsGroundedCondition.MovementNotRagdoll => input.MovementState != AlsMovementStateInput.Ragdoll,
        AlsGroundedCondition.Jumped => input.Jumped,
        AlsGroundedCondition.LeaveLanding => input.MovementState != AlsMovementStateInput.Grounded || input.Stance != AlsStance.Standing,
        AlsGroundedCondition.LandMoveOrRotate => input.HasMovementInput || input.RotateLeft || input.RotateRight,
        AlsGroundedCondition.LandMoveRotateOrFast => input.HasMovementInput || input.RotateLeft || input.RotateRight || input.Speed > threshold,
        AlsGroundedCondition.LandAnimationFinished => input.RelevantLandTimeRemaining == 0,
        AlsGroundedCondition.FeetNonNegative => input.FeetPosition >= 0,
        AlsGroundedCondition.JumpLeftAnimationFinished => input.RelevantJumpLeftTimeRemaining == 0,
        AlsGroundedCondition.JumpRightAnimationFinished => input.RelevantJumpRightTimeRemaining == 0,
        _ => throw new ArgumentException("Unsupported grounded condition."),
    };
}
