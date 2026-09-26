using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredLocomotionObservation(int PropertyIndex, float CachedWeight,
    float Time, bool PreviousValid = false, float PreviousTime = 0, float Delta = 0);
public readonly record struct AlsRefactoredLocomotionCallback(int State, bool Entry, string Function);

/// <summary>Transactional Locomotion/Jump state history. Reads committed source
/// clocks, emits candidates, and never advances private replacement clocks or
/// dispatches Parent actions. Pose/source owners consume this update separately.</summary>
public sealed class AlsRefactoredLocomotionRuntime
{
    public AlsRefactoredLocomotionResources Resources { get; }
    internal AlsGroundedMachineDefinition Definition { get; }
    private AlsGroundedMachineState _committed;
    private AlsGroundedMachineUpdate _candidate;
    private readonly int[] _selected;
    private readonly AlsRefactoredLocomotionCallback[] _callbacks = new AlsRefactoredLocomotionCallback[3];
    private int _callbackCount;
    private long _frame, _lastFrame = -1;
    private bool _prepared;
    public AlsGroundedMachineState CommittedState => _committed;
    public AlsGroundedMachineUpdate Candidate => _prepared ? _candidate : throw new InvalidOperationException("No Locomotion candidate.");
    public ReadOnlySpan<int> SelectedPlayerProperties => _prepared ? _selected : throw new InvalidOperationException("No Locomotion candidate.");
    public ReadOnlySpan<AlsRefactoredLocomotionCallback> Callbacks => _prepared ? _callbacks.AsSpan(0,_callbackCount) : throw new InvalidOperationException("No Locomotion candidate.");
    public AlsOverlayInertialRequest? InertializationRequest => !_prepared ? throw new InvalidOperationException("No Locomotion candidate.") :
        _candidate.InertializationSeconds < 0 ? null : new(Resources.MachinePropertyIndex,_candidate.InertializationSeconds,true,AlsTransitionBlend.HermiteCubic);

    public AlsRefactoredLocomotionRuntime(AlsRefactoredLocomotionResources resources)
    {
        Resources=resources; _selected=new int[resources.States.Length];
        var edges=resources.Edges.ToArray().Select(e=>new AlsGroundedEdge(e.From,e.To,
            e.Automatic?AlsGroundedCondition.Automatic:AlsGroundedCondition.Never,e.TriggerTime,e.Seconds,
            e.QuickFeet?AlsTransitionBlend.Cubic:AlsTransitionBlend.HermiteCubic,e.Inertialization,e.StartNotify,-1,-1,
            e.QuickFeet?AlsGroundedBlendProfile.QuickFeet:AlsGroundedBlendProfile.None){RefactoredLocomotionRule=e.Rule}).ToArray();
        var states=resources.States.ToArray().Select(s=>new AlsGroundedStateDefinition(s.EntryRule is not null,AlsGroundedCondition.Never,
            s.ExitStart,s.ExitCount,-1,-1,-1,s.AlwaysResetOnEntry){RefactoredLocomotionEntryRule=s.EntryRule}).ToArray();
        Definition=new(resources.Jump?AlsGroundedMachineKind.RefactoredJump:AlsGroundedMachineKind.RefactoredLocomotion,0,3,true,states,edges);
    }
    public void Prepare(long frame,in AlsRefactoredLocomotionInput input,ReadOnlySpan<AlsRefactoredLocomotionObservation> observations,
        float delta,float weight=1,bool initialize=false,AlsGraphTraversalCounter? updateCounter=null)
    {
        if(_prepared||frame<0||frame<=_lastFrame||observations.Length!=Resources.TimingPlayers.Length)
            throw new ArgumentException("Invalid Locomotion frame/source scope.");
        Span<AlsGroundedAutomaticTime> times=stackalloc AlsGroundedAutomaticTime[Resources.States.Length];times.Clear();
        Span<float> weights=stackalloc float[Resources.States.Length];weights.Clear();
        Span<int> selected=stackalloc int[Resources.States.Length];selected.Fill(-1);
        for(var i=0;i<observations.Length;i++)
        {
            var p=Resources.TimingPlayers[i];var o=observations[i];
            if(o.PropertyIndex!=p.PropertyIndex||!float.IsFinite(o.CachedWeight)||o.CachedWeight is <0 or >1||
                !float.IsFinite(o.Time)||o.Time<0||o.Time>p.Length||!float.IsFinite(o.PreviousTime)||!float.IsFinite(o.Delta)||
                o.PreviousValid&&(o.PreviousTime<0||o.PreviousTime>p.Length))throw new ArgumentException("Foreign Locomotion source observation.");
            if(o.CachedWeight<=weights[p.State])continue;
            weights[p.State]=o.CachedWeight;selected[p.State]=p.PropertyIndex;
            times[p.State]=new(true,p.Length,o.Time,p.Loop,o.PreviousValid,o.PreviousTime,o.Delta);
        }
        var previous=initialize?default:_committed;
        var next=AlsGroundedStateMachine.UpdateRefactoredLocomotion(Definition,previous,input,times,weight,delta,frame,updateCounter);
        _callbackCount=0;
        // A conduit supplies the final edge's blend policy, but is never the
        // exiting content state. Parent callbacks belong to the actual state.
        var from=next.Reinitialized?0:previous.CurrentState;
        for(var i=0;i<next.TransitionCount;i++)
        {
            var exit=Resources.States[from].ExitFunction;
            if(exit!="None")_callbacks[_callbackCount++]=new(from,false,exit);
            from=Resources.Edges[next.GetTransitionIndex(i)].To;
        }
        for(var s=0;s<selected.Length;s++)if((next.ClearCachedWeightStates&(1<<s))!=0)selected[s]=-1;
        selected.CopyTo(_selected);_candidate=next;_frame=frame;_prepared=true;
    }
    public void ValidateCommit(long frame){if(!_prepared||frame!=_frame)throw new ArgumentException("Invalid Locomotion commit.");}
    public void Commit(long frame){ValidateCommit(frame);_committed=_candidate.State;_lastFrame=frame;Cancel();}
    public void Cancel(){_prepared=false;_callbackCount=0;}
}
