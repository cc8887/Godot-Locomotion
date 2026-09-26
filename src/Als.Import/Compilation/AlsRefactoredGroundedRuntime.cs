using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredGroundedObservation(int PropertyIndex, float CachedWeight, float Time);
public readonly record struct AlsRefactoredGroundedCallback(int State, bool Entry, string Function);

/// <summary>Original Grounded state update with atomic candidate ownership.
/// The pose host must supply the previous committed transition-player clocks,
/// consume callback candidates, and receive inertia; this is not a pose host.</summary>
public sealed class AlsRefactoredGroundedRuntime
{
    public AlsRefactoredGroundedResources Resources { get; }
    internal AlsGroundedMachineDefinition Definition { get; }
    private AlsGroundedMachineState _committed;
    private AlsGroundedMachineUpdate _candidate;
    private readonly AlsRefactoredGroundedCallback[] _callbacks = new AlsRefactoredGroundedCallback[2];
    private readonly Func<float,float> _curve;
    private int _callbackCount;
    private long _frame, _lastFrame = -1;
    private bool _prepared;
    public AlsGroundedMachineState CommittedState => _committed;
    public AlsGroundedMachineUpdate Candidate => _prepared ? _candidate : throw new InvalidOperationException("No Grounded candidate.");
    public ReadOnlySpan<AlsRefactoredGroundedCallback> Callbacks => _prepared ? _callbacks.AsSpan(0,_callbackCount) : throw new InvalidOperationException("No Grounded candidate.");
    public AlsOverlayInertialRequest? InertializationRequest => !_prepared ? throw new InvalidOperationException("No Grounded candidate.") :
        _candidate.InertializationSeconds < 0 ? null : new(Resources.MachinePropertyIndex,_candidate.InertializationSeconds,true,AlsTransitionBlend.HermiteCubic);

    public AlsRefactoredGroundedRuntime(AlsRefactoredGroundedResources resources)
    {
        Resources=resources;_curve=resources.StanceCurve.Sample;
        var edges=resources.Edges.ToArray().Select(e=>new AlsGroundedEdge(e.From,e.To,
            e.Automatic?AlsGroundedCondition.Automatic:AlsGroundedCondition.Never,-1,e.Seconds,e.Blend,e.Inertialization,-1,-1,-1)
            {RefactoredGroundedRule=e.Rule}).ToArray();
        int[] starts=[0,4,6,8,13,18],counts=[4,2,2,5,5,2];
        var states=Enumerable.Range(0,6).Select(i=>new AlsGroundedStateDefinition(i==0,
            i==0?AlsGroundedCondition.Always:AlsGroundedCondition.Never,starts[i],counts[i],-1,-1,-1)).ToArray();
        Definition=new(AlsGroundedMachineKind.RefactoredGrounded,0,1,true,states,edges);
    }
    public void Prepare(long frame,in AlsRefactoredGroundedInput input,ReadOnlySpan<AlsRefactoredGroundedObservation> observations,
        float delta,float weight=1,bool initialize=false,AlsGraphTraversalCounter? updateCounter=null)
    {
        if(_prepared||frame<0||frame<=_lastFrame||observations.Length!=2)throw new ArgumentException("Invalid Grounded frame or source scope.");
        Span<AlsGroundedAutomaticTime> times=stackalloc AlsGroundedAutomaticTime[6];times.Clear();
        for(var i=0;i<2;i++)
        {
            var player=Resources.Players[i];var o=observations[i];
            if(o.PropertyIndex!=player.PropertyIndex||!float.IsFinite(o.CachedWeight)||o.CachedWeight is <0 or >1||
                !float.IsFinite(o.Time)||o.Time<0||o.Time>player.Length)throw new ArgumentException("Foreign Grounded transition-player observation.");
            // The native relevancy getter considers any strictly positive cached
            // weight; the animation relevance threshold must not be used here.
            if(o.CachedWeight>0)times[player.State]=new(true,player.Length,o.Time,false,false,0,0);
        }
        var next=AlsGroundedStateMachine.UpdateRefactoredGrounded(Definition,initialize?default:_committed,input,times,weight,delta,frame,_curve,updateCounter);
        _callbackCount=0;
        for(var i=0;i<next.TransitionCount;i++)
        {
            var edge=Resources.Edges[next.GetTransitionIndex(i)];
            Add(edge.From,false,Resources.States[edge.From].ExitFunction);
            Add(edge.To,true,Resources.States[edge.To].EntryFunction);
        }
        _candidate=next;_frame=frame;_prepared=true;
    }
    private void Add(int state,bool entry,string function){if(function!="None")_callbacks[_callbackCount++]=new(state,entry,function);}
    public void ValidateCommit(long frame){if(!_prepared||frame!=_frame)throw new ArgumentException("Invalid Grounded commit.");}
    public void Commit(long frame){ValidateCommit(frame);_committed=_candidate.State;_lastFrame=frame;Cancel();}
    public void Cancel(){_prepared=false;_callbackCount=0;}
}
