using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredStandingObservation(int PropertyIndex, float CachedWeight, float Time, bool Loop,
    bool PreviousValid = false, float PreviousTime = 0, float Delta = 0);
public readonly record struct AlsRefactoredStandingStateCallback(int State, bool Entry, string Function);

/// <summary>Original five-state outer Standing update. Source clocks, Stop's
/// inner graph, callback execution and notify dispatch are owned by the host.</summary>
public sealed class AlsRefactoredStandingRuntime
{
    public AlsRefactoredStandingResources Resources { get; }
    private readonly AlsGroundedMachineDefinition _definition;
    private AlsGroundedMachineState _committed;
    private AlsGroundedMachineUpdate _candidate;
    private readonly AlsRefactoredStandingStateCallback[] _callbacks = new AlsRefactoredStandingStateCallback[6];
    private int _callbackCount;
    private long _frame, _lastFrame = -1;
    private bool _prepared;
    public AlsGroundedMachineState CommittedState => _committed;
    public AlsGroundedMachineUpdate Candidate => _prepared ? _candidate : throw new InvalidOperationException("No Standing candidate.");
    public ReadOnlySpan<AlsRefactoredStandingStateCallback> StateCallbacks => _prepared ? _callbacks.AsSpan(0, _callbackCount) : throw new InvalidOperationException("No Standing candidate.");
    public AlsOverlayInertialRequest? InertializationRequest => !_prepared ? throw new InvalidOperationException("No Standing candidate.") :
        _candidate.InertializationSeconds < 0 ? null : new(Resources.MachinePropertyIndex, _candidate.InertializationSeconds, true, AlsTransitionBlend.HermiteCubic);

    public AlsRefactoredStandingRuntime(AlsRefactoredStandingResources resources)
    {
        Resources = resources;
        var edges = resources.Edges.ToArray().Select(e => new AlsGroundedEdge(e.From,e.To,
            e.Rule == AlsRefactoredStandingRule.Automatic ? AlsGroundedCondition.Automatic : AlsGroundedCondition.Never,
            -1,e.Seconds,AlsTransitionBlend.HermiteCubic,e.Inertialization,e.StartNotify,-1,-1) { RefactoredStandingRule = e.Rule }).ToArray();
        var states = resources.States.ToArray().Select(s => new AlsGroundedStateDefinition(false,AlsGroundedCondition.Never,s.Exits[0],s.Exits.Length,-1,-1,-1)).ToArray();
        _definition = new(AlsGroundedMachineKind.RefactoredStanding,0,3,true,states,edges);
    }
    public void Prepare(long frame, in AlsRefactoredStandingInput input, ReadOnlySpan<AlsRefactoredStandingObservation> observations,
        float delta, float weight = 1, bool reinitialize = false, AlsGraphTraversalCounter? updateCounter = null)
    {
        if (_prepared || frame < 0 || frame <= _lastFrame || observations.Length != 2) throw new ArgumentException("Invalid Standing frame/rotation observations.");
        Span<AlsGroundedAutomaticTime> times = stackalloc AlsGroundedAutomaticTime[5]; times.Clear();
        for (var i = 0; i < 2; i++)
        {
            var o = observations[i]; var length = Resources.RotateLengths[i];
            if (o.PropertyIndex != Resources.RotatePlayers.Players[i].PropertyIndex || !float.IsFinite(o.CachedWeight) || o.CachedWeight is < 0 or > 1 ||
                !float.IsFinite(o.Time) || o.Time < 0 || o.Time > length || !float.IsFinite(o.PreviousTime) || !float.IsFinite(o.Delta) ||
                o.PreviousValid && (o.PreviousTime < 0 || o.PreviousTime > length)) throw new ArgumentException("Invalid Standing asset-player observation.");
            if (o.CachedWeight > 0) times[3 + i] = new(true,length,o.Time,o.Loop,o.PreviousValid,o.PreviousTime,o.Delta);
        }
        var next = AlsGroundedStateMachine.UpdateRefactoredStanding(_definition,reinitialize ? default : _committed,input,times,weight,delta,frame,updateCounter);
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
    { if (function != "None") _callbacks[_callbackCount++] = new(state,entry,function); }
    public void ValidateCommit(long frame) { if (!_prepared || frame != _frame) throw new ArgumentException("Invalid Standing commit."); }
    public void Commit(long frame) { ValidateCommit(frame); _committed = _candidate.State; _lastFrame = frame; Cancel(); }
    public void Cancel() { _prepared = false; _callbackCount = 0; }
}
