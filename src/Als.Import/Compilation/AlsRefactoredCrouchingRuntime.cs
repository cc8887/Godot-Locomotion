using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredCrouchingObservation(int PropertyIndex, float CachedWeight, float Time, bool Loop,
    bool PreviousValid = false, float PreviousTime = 0, float Delta = 0);
public readonly record struct AlsRefactoredCrouchingStateCallback(int State, bool Entry, string Function);

/// <summary>Candidate-owned outer Crouching state update. The future pose host
/// consumes callbacks/notifications, ticks sources, and applies QuickFeet to bones.
/// Nothing here mutates Parent state or dispatches external side effects.</summary>
public sealed class AlsRefactoredCrouchingRuntime
{
    public AlsRefactoredCrouchingResources Resources { get; }
    internal AlsGroundedMachineDefinition Definition { get; }
    private AlsGroundedMachineState _committed;
    private AlsGroundedMachineUpdate _candidate;
    private readonly AlsRefactoredCrouchingStateCallback[] _callbacks = new AlsRefactoredCrouchingStateCallback[6];
    private int _callbackCount;
    private long _frame, _lastFrame = -1;
    private bool _prepared;
    public AlsGroundedMachineState CommittedState => _committed;
    public AlsGroundedMachineUpdate Candidate => _prepared ? _candidate : throw new InvalidOperationException("No Crouching candidate.");
    public ReadOnlySpan<AlsRefactoredCrouchingStateCallback> StateCallbacks => _prepared ? _callbacks.AsSpan(0, _callbackCount) : throw new InvalidOperationException("No Crouching candidate.");
    public AlsOverlayInertialRequest? InertializationRequest => !_prepared ? throw new InvalidOperationException("No Crouching candidate.") :
        _candidate.InertializationSeconds < 0 ? null : new(Resources.MachinePropertyIndex, _candidate.InertializationSeconds, true, AlsTransitionBlend.HermiteCubic);

    public AlsRefactoredCrouchingRuntime(AlsRefactoredCrouchingResources resources)
    {
        Resources = resources;
        var edges = resources.Edges.ToArray().Select(e => new AlsGroundedEdge(e.From, e.To,
            e.Rule == AlsRefactoredCrouchingRule.Automatic ? AlsGroundedCondition.Automatic : AlsGroundedCondition.Never,
            -1, e.Seconds, AlsTransitionBlend.HermiteCubic, e.Inertialization, e.StartNotify, -1, -1,
            e.QuickFeet ? AlsGroundedBlendProfile.QuickFeet : AlsGroundedBlendProfile.None) { RefactoredCrouchingRule = e.Rule }).ToArray();
        var states = resources.States.ToArray().Select(s => new AlsGroundedStateDefinition(false, AlsGroundedCondition.Never, s.Exits[0], s.Exits.Length, -1, -1, -1)).ToArray();
        Definition = new(AlsGroundedMachineKind.RefactoredCrouching, 0, 3, true, states, edges);
    }
    public void Prepare(long frame, in AlsRefactoredCrouchingInput input, ReadOnlySpan<AlsRefactoredCrouchingObservation> observations,
        float delta, float weight = 1, bool reinitialize = false, AlsGraphTraversalCounter? updateCounter = null)
    {
        if (_prepared || frame < 0 || frame <= _lastFrame || observations.Length != 2) throw new ArgumentException("Invalid Crouching frame/rotation observations.");
        Span<AlsGroundedAutomaticTime> times = stackalloc AlsGroundedAutomaticTime[5]; times.Clear();
        for (var i = 0; i < 2; i++)
        {
            var o = observations[i]; var length = Resources.RotateLengths[i];
            if (o.PropertyIndex != Resources.RotatePlayers.Players[i].PropertyIndex || !float.IsFinite(o.CachedWeight) || o.CachedWeight is < 0 or > 1 ||
                !float.IsFinite(o.Time) || o.Time < 0 || o.Time > length || !float.IsFinite(o.PreviousTime) || !float.IsFinite(o.Delta) ||
                o.PreviousValid && (o.PreviousTime < 0 || o.PreviousTime > length)) throw new ArgumentException("Invalid Crouching asset-player observation.");
            if (o.CachedWeight > 0) times[2 + i] = new(true, length, o.Time, o.Loop, o.PreviousValid, o.PreviousTime, o.Delta);
        }
        var next = AlsGroundedStateMachine.UpdateRefactoredCrouching(Definition, reinitialize ? default : _committed, input, times, weight, delta, frame, updateCounter);
        _callbackCount = 0;
        for (var i = 0; i < next.TransitionCount; i++)
        {
            var edge = Resources.Edges[next.GetTransitionIndex(i)];
            Add(edge.From, false, Resources.States[edge.From].ExitFunction);
            Add(edge.To, true, Resources.States[edge.To].EntryFunction);
        }
        _candidate = next; _frame = frame; _prepared = true;
    }
    private void Add(int state, bool entry, string function)
    { if (function != "None") _callbacks[_callbackCount++] = new(state, entry, function); }
    public void ValidateCommit(long frame) { if (!_prepared || frame != _frame) throw new ArgumentException("Invalid Crouching commit."); }
    public void Commit(long frame) { ValidateCommit(frame); _committed = _candidate.State; _lastFrame = frame; Cancel(); }
    public void Cancel() { _prepared = false; _callbackCount = 0; }
}
