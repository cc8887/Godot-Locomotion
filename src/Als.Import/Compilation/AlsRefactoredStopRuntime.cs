using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Stop53 selection and blend history; callback execution and poses belong to the enclosing host.</summary>
public sealed class AlsRefactoredStopRuntime
{
    public AlsRefactoredStopResources Resources { get; }
    private readonly AlsGroundedMachineDefinition _definition;
    private AlsGroundedMachineState _committed;
    private AlsGroundedMachineUpdate _candidate;
    private AlsRefactoredStandingStateCallback? _callback;
    private long _frame, _lastFrame = -1;
    private bool _prepared;
    public AlsGroundedMachineState CommittedState => _committed;
    public AlsGroundedMachineUpdate Candidate => _prepared ? _candidate : throw new InvalidOperationException("No Stop candidate.");
    public AlsRefactoredStandingStateCallback? StateCallback => _prepared ? _callback : throw new InvalidOperationException("No Stop candidate.");

    public AlsRefactoredStopRuntime(AlsRefactoredStopResources resources)
    {
        Resources = resources;
        var edges = resources.Edges.ToArray().Select(e => new AlsGroundedEdge(0,e.To,AlsGroundedCondition.Never,0,e.Seconds,
            AlsTransitionBlend.HermiteCubic,false,-1,-1,-1) { RefactoredStopRule = e.Rule }).ToArray();
        var states = Enumerable.Range(0,5).Select(i => new AlsGroundedStateDefinition(false,AlsGroundedCondition.Never,i == 0 ? 0 : 4,i == 0 ? 4 : 0,-1,-1,-1)).ToArray();
        _definition = new(AlsGroundedMachineKind.RefactoredStop,0,3,false,states,edges);
    }
    public void Prepare(long frame,float footPlantedAmount,float delta,float weight = 1,bool reinitialize = false,AlsGraphTraversalCounter? updateCounter = null)
    {
        if (_prepared || frame < 0 || frame <= _lastFrame) throw new ArgumentException("Invalid Stop frame.");
        var next = AlsGroundedStateMachine.UpdateRefactoredStop(_definition,reinitialize ? default : _committed,footPlantedAmount,weight,delta,frame,updateCounter);
        _callback = next.TransitionCount == 0 ? null : new(next.State.CurrentState,true,Resources.States[next.State.CurrentState].EntryFunction);
        _candidate = next; _frame = frame; _prepared = true;
    }
    public void ValidateCommit(long frame) { if (!_prepared || frame != _frame) throw new ArgumentException("Invalid Stop commit."); }
    public void Commit(long frame) { ValidateCommit(frame); _committed = _candidate.State; _lastFrame = frame; Cancel(); }
    public void Cancel() { _prepared = false; _callback = null; }
}
