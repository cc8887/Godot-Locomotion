using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Original Grounded pose, source clocks and callbacks in the character transaction.</summary>
public sealed class AlsRefactoredGroundedHost : IAlsPoseCacheUpdateSink
{
    private readonly AlsRefactoredCharacterActionProfile _p;
    private readonly AlsRefactoredGroundedHostProfile _g;
    private readonly AlsRefactoredCharacterActionRuntime _owner;
    private readonly AlsRefactoredGroundedRuntime _machine;
    private readonly AlsRefactoredSourcePlayerRuntime _players;
    private readonly AlsPoseCacheTraversal _cache;
    private readonly AlsRefactoredPoseInertia _inertia;
    private readonly AlsRefactoredGroundedObservation[] _clocks=new AlsRefactoredGroundedObservation[2],_nextClocks=new AlsRefactoredGroundedObservation[2];
    private readonly AlsRefactoredSourcePlayerInput[] _ticks=new AlsRefactoredSourcePlayerInput[2];
    private readonly AlsPrecisePose[][] _states;
    private readonly AlsInertialCurve[][] _values;
    private readonly AlsPrecisePose[] _raw;
    private readonly AlsInertialCurve[] _rawCurves,_curves;
    private readonly int[][] _maps;
    private readonly int[] _wrapper,_lock;
    private readonly AlsGraphTraversalCounter[] _relevance=new AlsGraphTraversalCounter[6],_nextRelevance=new AlsGraphTraversalCounter[6];
    private AlsGraphTraversalCounter _counter,_nextCounter,_initialization;
    private AlsRefactoredStandingHostInput _input;
    private AlsFrameIdentity _identity;
    private AlsPoseUpdateContext _context;
    private bool _prepared,_evaluated,_initialize;
    private byte _reset;
    internal bool Prepared=>_prepared;
    public int State=>_prepared?_machine.Candidate.State.CurrentState:throw new InvalidOperationException("No Grounded frame.");
    public bool ResetEntryMode {get;private set;}
    public bool StandingUpdated {get;private set;}
    public bool CrouchingUpdated {get;private set;}
    public ReadOnlySpan<AlsPrecisePose> Pose=>_evaluated?_inertia.Pose:throw new InvalidOperationException("No Grounded pose.");
    public ReadOnlySpan<AlsInertialCurve> Curves=>_evaluated?_curves:throw new InvalidOperationException("No Grounded curves.");
    public ReadOnlySpan<AlsRefactoredGroundedObservation> Observations=>_prepared?_nextClocks:throw new InvalidOperationException("No Grounded clocks.");

    internal AlsRefactoredGroundedHost(AlsRefactoredCharacterActionProfile profile,AlsRefactoredCharacterActionRuntime owner)
    {
        _p=profile;_g=profile.Grounded!;_owner=owner;_machine=new(_g.Machine);
        _players=new(profile.Standing.Catalog,profile.Standing.Sync,profile.Standing.Triangles,
            _g.Machine.Players.ToArray().Select((p,i)=>new AlsRefactoredSourcePlayerDefinition(i,p.Source,-1,Looping:false)));
        _cache=new(_g.Caches,16);_inertia=new(profile.BoneNames.Length,profile.CurveNames);
        _states=Enumerable.Range(0,6).Select(_=>new AlsPrecisePose[profile.BoneNames.Length]).ToArray();
        _values=Enumerable.Range(0,6).Select(_=>new AlsInertialCurve[profile.CurveNames.Length]).ToArray();
        _raw=new AlsPrecisePose[profile.BoneNames.Length];_rawCurves=new AlsInertialCurve[profile.CurveNames.Length];_curves=new AlsInertialCurve[profile.CurveNames.Length];
        var names=profile.CurveNames.ToArray();int[] Map(ReadOnlySpan<string> source)=>source.ToArray().Select(n=>Array.IndexOf(names,n)).ToArray();
        _maps=[[],Map(owner.Standing.CurveNames),Map(owner.Crouching!.CurveNames),Map(_players.CurveNames(0)),Map(_players.CurveNames(1)),Map(_g.RollCurveNames)];
        if(_maps.Any(m=>m.Any(c=>c<0))||!_players.BoneNames(0).SequenceEqual(profile.BoneNames)||!_players.BoneNames(1).SequenceEqual(profile.BoneNames))
            throw new ArgumentException("Grounded source layout differs.");
        _wrapper=Map(["PoseGrounded","FootLeftIk","FootRightIk"]);_lock=Map(["FootLeftLock","FootRightLock"]);
        for(var i=0;i<2;i++)_clocks[i]=new(_g.Machine.Players[i].PropertyIndex,0,0);
    }
    public void Prepare(in AlsPoseUpdateContext context,in AlsRefactoredStandingHostInput input,
        float previousStanding,float previousCrouching,bool fromRoll,AlsGraphTraversalCounter initialization,bool initialize=false)
    {
        if(_prepared||context.UpdateCounter is not {HasUpdated:true}||!context.HasSharedContext)throw new ArgumentException("Invalid Grounded traversal.");
        _owner.ValidateUpdate(context);_context=context;_identity=context.Identity;_input=input;_initialization=initialization;_initialize=initialize;
        var frame=_identity.FrameId;_nextCounter=context.UpdateCounter.Value;
        try
        {
            _owner.PrepareParents(context,input,initialize);
            var reentry=initialize||!_counter.HasUpdated||!_counter.WasSynchronizedCounter(_nextCounter);
            if(reentry)_owner.MovementParent.InitializeGrounded(frame);
            _owner.MovementParent.RefreshGrounded(frame);
            var rotate=_owner.RestParent.Candidate.Rotate;
            var stance=input.Rest.Stance==AlsRefactoredRestStance.Crouching?AlsRefactoredGroundedStance.Crouching:AlsRefactoredGroundedStance.Standing;
            _machine.Prepare(frame,new(stance,input.MovingSmooth,rotate.Left,rotate.Right,fromRoll,previousStanding,previousCrouching),
                _clocks,context.Delta,context.Weight,initialize,context.UpdateCounter);
            foreach(var callback in _machine.Callbacks)
                if(callback.Entry)_owner.Queue.QueueStop(_g.StopDuration);
                else _owner.Queue.QueuePlay(stance==AlsRefactoredGroundedStance.Crouching?_g.RollToCrouching:_g.RollToStanding,
                    stance==AlsRefactoredGroundedStance.Crouching?"Als.Stance.Crouching":"Als.Stance.Standing",input.MovingSmooth);
            var update=_machine.Candidate;_reset=update.InitializeStates;_relevance.CopyTo(_nextRelevance,0);
            _cache.Begin(_identity);var count=0;var source=context.WithInertialization(43,true);
            for(var u=0;u<update.UpdateCount;u++)
            {
                var state=update.GetUpdate(u);var reset=initialize||(_reset&(1<<state.State))!=0;
                var path=source.WithWeight(state.Weight).WithState(41,state.State,state.InertializationSync);
                if(state.State is 1 or 2 or 5)
                {
                    if(reset||!_relevance[state.State].HasUpdated||!_relevance[state.State].WasSynchronizedCounter(_nextCounter))ResetEntryMode=true;
                    _nextRelevance[state.State]=_nextCounter;
                }
                switch(state.State)
                {
                    case 1:_cache.Use(39,path);break;
                    case 2:_cache.Use(36,path);break;
                    case 3:case 4:
                        var i=state.State-3;_ticks[count++]=new(i,default,_g.Machine.Players[i].Rate,state.Weight,reset,Looping:false);break;
                }
            }
            _cache.Drain(this);_players.Prepare(frame,_ticks.AsSpan(0,count),context.Delta,initialize);
            _inertia.Prepare(context,initialize);if(_machine.InertializationRequest is {} request)_inertia.Request(request.Duration);
            _clocks.CopyTo(_nextClocks,0);
            for(var i=0;i<2;i++)if(initialize||(update.ClearCachedWeightStates&(1<<(i+3)))!=0)_nextClocks[i]=new(_g.Machine.Players[i].PropertyIndex,0,0);
            foreach(var tick in _players.Ticks)foreach(var player in _players.Players)if(tick.PlayerId==player.PlayerId)
                _nextClocks[tick.PlayerId]=new(_g.Machine.Players[tick.PlayerId].PropertyIndex,tick.Weight,player.Time);
            _prepared=true;
        }
        catch{_owner.Discard();throw;}
    }
    void IAlsPoseCacheUpdateSink.UpdateCachedSource(int cache,in AlsPoseUpdateContext context)
    {
        if(cache==3){_owner.Standing.Prepare(context,_input,_initialization,_initialize||(_reset&2)!=0);StandingUpdated=true;}
        else if(cache==4){_owner.Crouching!.Prepare(context,_input,_initialization,_initialize||(_reset&4)!=0);CrouchingUpdated=true;}
        else throw new ArgumentException("Foreign Grounded cache.");
    }
    void IAlsPoseCacheUpdateSink.OnCachedUpdatesSkipped(int handler,ReadOnlySpan<AlsPoseUpdateContext> skipped)
    {if(handler!=43)throw new ArgumentException("Foreign Grounded inertia receiver.");}
    public void Evaluate(in AlsPrecisePose component)
    {
        Check();_owner.ValidateUpdate(_context);_evaluated=false;
        try
        {
            var frame=_identity.FrameId;var state=_machine.Candidate.State;var stack=state.Transitions;
            if(StandingUpdated)_owner.Standing.Evaluate(component);if(CrouchingUpdated)_owner.Crouching!.Evaluate(component);
            var sampled=0;
            void Sample(int s)
            {
                if((sampled&(1<<s))!=0)return;sampled|=1<<s;Array.Clear(_values[s]);
                ReadOnlySpan<AlsPrecisePose> pose;ReadOnlySpan<AlsInertialCurve> curves;
                switch(s)
                {
                    case 1:pose=_owner.Standing.Pose;curves=_owner.Standing.Curves;break;
                    case 2:pose=_owner.Crouching!.Pose;curves=_owner.Crouching.Curves;break;
                    case 3:case 4:_players.Evaluate(frame,s-3);pose=_players.Pose(s-3);curves=_players.Curves(s-3);break;
                    case 5:pose=_g.Roll;curves=_g.RollCurves;break;
                    default:throw new InvalidOperationException("Conduit is not a Grounded pose.");
                }
                pose.CopyTo(_states[s]);for(var c=0;c<curves.Length;c++)_values[s][_maps[s][c]]=curves[c];
                if(s==5)foreach(var c in _lock)_values[s][c]=new(1);
            }
            var first=stack.Count==0?state.CurrentState:stack.GetTransition(0).From;Sample(first);
            _states[first].CopyTo(_raw,0);_values[first].CopyTo(_rawCurves,0);
            for(var i=0;i<stack.Count;i++)
            {
                var edge=stack.GetTransition(i);Sample(edge.To);
                for(var b=0;b<_raw.Length;b++)_raw[b]=AlsPrecisePoseBlender.Accumulate(AlsPrecisePoseBlender.Scale(_raw[b],1-edge.Alpha),_states[edge.To][b],edge.Alpha);
                for(var c=0;c<_rawCurves.Length;c++)_rawCurves[c]=AlsStandingCycleCurves.Accumulate(AlsStandingCycleCurves.Scale(_rawCurves[c],1-edge.Alpha),_values[edge.To][c],edge.Alpha);
            }
            if(stack.Count>0)for(var b=0;b<_raw.Length;b++)_raw[b]=_raw[b].Normalized();
            _inertia.Evaluate(_raw,_rawCurves,component);_inertia.Curves.CopyTo(_curves);foreach(var c in _wrapper)_curves[c]=new(1);_evaluated=true;
        }
        catch{_owner.Discard();throw;}
    }
    public void ValidateCommit(in AlsFrameIdentity identity)
    {Check();if(identity!=_identity)throw new ArgumentException("Foreign Grounded commit.");_machine.ValidateCommit(identity.FrameId);_players.ValidateCommit(identity.FrameId);_inertia.ValidateCommit(identity);}
    internal void CommitShared(in AlsFrameIdentity identity)
    {ValidateCommit(identity);_machine.Commit(identity.FrameId);_players.Commit(identity.FrameId);_inertia.Commit(identity);_nextClocks.CopyTo(_clocks,0);_nextRelevance.CopyTo(_relevance,0);_counter=_nextCounter;CancelGraph();}
    internal void CancelGraph(){_machine.Cancel();_players.Cancel();_inertia.Cancel();_prepared=_evaluated=ResetEntryMode=StandingUpdated=CrouchingUpdated=false;}
    private void Check(){if(!_prepared)throw new InvalidOperationException("No Grounded candidate.");}
}
