using System.Collections.Immutable;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraIdleRelevant(bool Valid,float Length,float Time,bool Looping,
    bool PreviousValid,float Previous,float Delta);
internal sealed record LyraIdleMachineCandidate(long Frame,int Before,int State,float Elapsed,
    bool Visited,bool First,bool Automatic,ImmutableArray<int> Initializations,ImmutableArray<int> ClearWeights,
    ImmutableArray<float> PreviousWeights,ImmutableArray<LyraStateSourceUpdate> Updates,
    LyraLocomotionTransition? Transition);

// Same original standard stack as Main, with the Idle machine's own baked
// exits. Relevant player timing is supplied by this instance's real sources.
internal class LyraLinkedMachineRuntime
{
    private readonly LyraCompiledMachine _definition;
    private readonly LyraLocomotionPoseState _committed,_pose;
    private LyraIdleMachineCandidate? _pending;
    private long _frame,_lastVisited=-1;
    private bool _initialized,_first=true;
    private bool _startupSourcesPending;
    private ImmutableArray<float> _weights;
    public int State=>_committed.State;
    public float Elapsed=>_committed.Elapsed;
    public AlsTransitionStackState Stack=>_committed.Stack;
    internal IEnumerable<(int Index,string Name,float Weight)> PhaseStates=>
        _definition.States.Select((s,i)=>(i,s.Name,_committed.Weight(i)));
    internal void InitializeBeforeFirstFrame()
    {
        if(_pending is not null||_frame!=0)throw new InvalidOperationException("Linked machine startup needs a fresh idle graph.");
        _committed.ResetMachine();_pose.CopyFrom(_committed);_first=true;_lastVisited=-1;
        // The first Update still supplies child initialization instructions to
        // source hosts. Initialize does not run their InitialUpdate callbacks.
        _initialized=true;_startupSourcesPending=true;
        _weights=Enumerable.Repeat(0f,_definition.States.Count).ToImmutableArray();
    }
    protected LyraLinkedMachineRuntime(LyraCompiledMachine definition,int curves,string expectedName,int expectedStates)
    {
        if(definition.Name!=expectedName || definition.States.Any(s=>s.Conduit || s.EntryDelegate!=-1) ||
            definition.States.Count!=expectedStates)throw new NotSupportedException("Changed linked machine.");
        _definition=definition;_committed=new(definition,curves);_pose=new(definition,curves);
        _weights=Enumerable.Repeat(0f,definition.States.Count).ToImmutableArray();
    }
    public LyraIdleMachineCandidate Prepare(float delta,LyraAirVisit visit,Func<int,bool> predicate,LyraIdleRelevant relevant)
    {
        if(_pending is not null)throw new InvalidOperationException("Idle machine is pending.");
        if(!float.IsFinite(delta) || delta<0 || !float.IsFinite(visit.Weight) || visit.Weight<0)throw new ArgumentException("Invalid Idle machine visit.");
        _pose.CopyFrom(_committed);var first=_first;var frame=checked(_frame+1);var initialized=new int[_definition.States.Count];var cleared=new List<int>();
        void Initialize(){_pose.ResetMachine();initialized[_definition.InitialState]++;cleared.Add(_definition.InitialState);first=true;}
        if(!_initialized || visit.Initialize)Initialize();var before=_pose.State;
        if(_startupSourcesPending&&initialized[_definition.InitialState]==0)
        {initialized[_definition.InitialState]++;cleared.Add(_definition.InitialState);}
        var automatic=visit.Visited && !first && _lastVisited>=0 && frame-_lastVisited>1;if(automatic)Initialize();
        LyraLocomotionTransition? selected=null;
        if(visit.Visited)
        {
            foreach(var exit in _definition.States[_pose.State].Exits)
            {
                if(exit.OnlyWhenActive && !visit.Active)continue;var edge=_definition.Edges[exit.Edge];var adjustment=0f;
                var result=false;
                if(exit.RequiredSyncGroup!="None")throw new NotSupportedException("Idle exit acquired a Sync gate.");
                if(exit.Automatic)
                {
                    var trigger=exit.AutomaticTriggerTime>=0?exit.AutomaticTriggerTime:edge.Duration;var remaining=relevant.Length-relevant.Time;
                    if(relevant.Looping && remaining>0 && relevant.PreviousValid && (relevant.Time-relevant.Previous)*relevant.Delta<0)remaining=0;
                    adjustment=trigger-remaining;result=relevant.Valid && adjustment>=0;
                }
                else result=predicate(exit.Edge);
                if(result!=exit.Desired || edge.Next==_pose.State)continue;
                selected=new(_pose.State,edge.Next,exit.Edge,new[]{exit.Edge},edge.Duration,edge.Inertial,adjustment,first);
                if(!(_pose.Weight(edge.Next)>0) || _definition.States[edge.Next].AlwaysResetOnEntry)initialized[edge.Next]++;
                cleared.Add(edge.Next);break;
            }
            _pose.Prepare(selected,delta);
        }
        var updates=visit.Visited?_pose.Updates.ToArray().Select(u=>u with{Weight=u.Weight*visit.Weight,Active=u.Active&&visit.Active,Inertial=u.Inertial||visit.Inertial}).ToImmutableArray():[];
        return _pending=new(frame,before,_pose.State,_pose.Elapsed,visit.Visited,first,automatic,initialized.ToImmutableArray(),
            cleared.ToImmutableArray(),_weights,updates,selected);
    }
    public float PreparedWeight(LyraIdleMachineCandidate c,int state){Validate(c);return _pose.Weight(state);}
    public AlsTransitionStackState PreparedStack(LyraIdleMachineCandidate c){Validate(c);return _pose.Stack;}
    public void Evaluate(LyraIdleMachineCandidate c,LyraStatePoseEvaluator evaluate,Span<AlsPrecisePose> pose,Span<AlsInertialCurve> curves)
    {Validate(c);if(!c.Visited)throw new InvalidOperationException("Hidden Idle machine.");_pose.EvaluateMachine(evaluate,pose,curves);}
    public void Validate(LyraIdleMachineCandidate c){if(!ReferenceEquals(c,_pending))throw new InvalidOperationException("Stale Idle machine.");}
    public void Commit(LyraIdleMachineCandidate c)
    {Validate(c);_committed.CopyFrom(_pose);_frame=c.Frame;_initialized=true;_first=c.First&&!c.Visited;if(c.Visited)_lastVisited=c.Frame;
     _weights=Enumerable.Range(0,_definition.States.Count).Select(s=>c.Visited?_pose.Weight(s):0).ToImmutableArray();_pending=null;_startupSourcesPending=false;}
    public void Cancel()=>_pending=null;
}

internal sealed class LyraIdleMachineRuntime : LyraLinkedMachineRuntime
{
    public LyraIdleMachineRuntime(LyraCompiledMachine definition,int curves)
        : base(definition,curves,definition.Name=="IdleSM"?"IdleSM":"IdleStance",definition.Name=="IdleSM"?4:2) {}
}
