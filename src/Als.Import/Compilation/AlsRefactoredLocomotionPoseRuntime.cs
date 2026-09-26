using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredAirPoseInput(AlsRefactoredLocomotionInput Rules,float VerticalVelocity,
    float Prediction,float JumpRate,Vector2 Lean,bool Aiming)
{
    internal float Read(AlsRefactoredAirValue value)=>value switch
    {AlsRefactoredAirValue.Speed=>Rules.Speed,AlsRefactoredAirValue.VerticalVelocity=>VerticalVelocity,
        AlsRefactoredAirValue.Prediction=>Prediction,AlsRefactoredAirValue.Aiming=>Aiming?1:0,_=>1};
    internal void Validate()
    {if(!float.IsFinite(VerticalVelocity)||!float.IsFinite(Prediction)||!float.IsFinite(JumpRate)||JumpRate<=0||
        !float.IsFinite(Lean.X)||!float.IsFinite(Lean.Y))throw new ArgumentException("Invalid original air pose input.");}
}

/// <summary>The character owns this sink's Parent/Grounded candidates and must
/// validate/commit or discard them together with the Locomotion pose runtime.</summary>
public interface IAlsRefactoredLocomotionPoseSink
{
    AlsRefactoredAirPoseInput Input {get;}
    void Validate(in AlsPoseUpdateContext context);
    void Callback(string function,in AlsPoseUpdateContext context);
    void StateCallback(in AlsRefactoredLocomotionCallback callback,in AlsPoseUpdateContext context);
    void Notify(in AlsGroundedMachineEvent notification,in AlsPoseUpdateContext context);
    void PrepareGrounded(in AlsPoseUpdateContext context,bool initialize);
    void EvaluateGrounded(in AlsPrecisePose component);
    ReadOnlySpan<AlsPrecisePose> GroundedPose {get;}
    ReadOnlySpan<AlsInertialCurve> GroundedCurves {get;}
}

/// <summary>Original Locomotion graph traversal and pose evaluation. Sources,
/// nested machines, alpha/filter histories, cache selection and inertia share
/// one candidate. Parent callbacks run at their original graph traversal sites.</summary>
public sealed class AlsRefactoredLocomotionPoseRuntime : IAlsPoseCacheUpdateSink
{
    private readonly AlsRefactoredLocomotionPoseProfile _p;
    private readonly AlsRefactoredLocomotionRuntime _main,_jump;
    private readonly AlsRefactoredSourcePlayerRuntime _players;
    private readonly AlsRefactoredBlendEvaluatorRuntime[] _lean;
    private readonly AlsRefactoredPoseInertia _outer,_inner;
    private readonly AlsPoseCacheTraversal _cache;
    private readonly AlsPrecisePose[][] _pose;
    private readonly AlsInertialCurve[][] _curves;
    private readonly int[][] _maps=new int[84][];
    private readonly bool[] _pending=new bool[84],_nextPending=new bool[84],_alphaInitialized=new bool[84],_nextAlphaInitialized=new bool[84],_updated=new bool[84],_sampled=new bool[84];
    private readonly float[] _history=new float[84],_nextHistory=new float[84],_alpha=new float[84];
    private readonly AlsRefactoredSourcePlayerInput[] _ticks=new AlsRefactoredSourcePlayerInput[13];
    private readonly AlsPoseUpdateContext[] _tickContexts=new AlsPoseUpdateContext[13];
    private readonly AlsRefactoredLocomotionObservation[] _mainClocks,_nextMainClocks,_jumpClocks,_nextJumpClocks;
    private readonly AlsQuaternion[] _meshScratch;
    private AlsGraphTraversalCounter _counter,_nextCounter;
    private AlsPoseUpdateContext _context;
    private AlsFrameIdentity _last;
    private IAlsRefactoredLocomotionPoseSink? _sink;
    private int _tickCount;
    private long _attachParent;
    private float _teleportDistance;
    private bool _prepared,_evaluated,_faulted,_groundedInitialized,_nextGroundedInitialized;
    public AlsRefactoredLocomotionPoseProfile Profile=>_p;
    public AlsGroundedMachineUpdate MainUpdate {get{Check();return _main.Candidate;}}
    public AlsGroundedMachineUpdate? JumpUpdate {get{Check();return _updated[64]?_jump.Candidate:null;}}
    public bool GroundedUpdated {get;private set;}
    public ReadOnlySpan<AlsRefactoredSourcePlayerInput> SourceInputs {get{Check();return _ticks.AsSpan(0,_tickCount);}}
    public ReadOnlySpan<AlsPoseUpdateContext> SourceContexts {get{Check();return _tickContexts.AsSpan(0,_tickCount);}}
    public ReadOnlySpan<AlsRefactoredLocomotionObservation> MainClocks {get{Check();return _nextMainClocks;}}
    public ReadOnlySpan<AlsRefactoredLocomotionObservation> JumpClocks {get{Check();return _nextJumpClocks;}}
    public ReadOnlySpan<AlsPrecisePose> Pose=>_evaluated&&!_faulted?_pose[0]:throw new InvalidOperationException("No Locomotion pose.");
    public ReadOnlySpan<AlsInertialCurve> Curves=>_evaluated&&!_faulted?_curves[0]:throw new InvalidOperationException("No Locomotion curves.");
    public float Alpha(int node){Check();if(!_updated[node])throw new ArgumentException("Node was not traversed.");return _alpha[node];}

    public AlsRefactoredLocomotionPoseRuntime(AlsRefactoredLocomotionPoseProfile profile,AlsRefactoredSyncBank sync)
    {
        _p=profile;_main=new(profile.Main);_jump=new(profile.Jump);_cache=new(profile.Caches,16);
        var groups=new Dictionary<string,int>{{"Fall",0},{"Jump",1},{"Flail",2},{"Land",3},{"None",-1}};
        _players=new(profile.Catalog,sync,new Dictionary<string,AlsRefactoredTriangulationProfile>(),profile.Players.ToArray().Select((p,i)=>
            new AlsRefactoredSourcePlayerDefinition(i,p.Source,groups[p.Group],p.Start,p.Loop)));
        _lean=[new(profile.Lean),new(profile.Lean)];_outer=new(profile.BoneNames.Length,profile.CurveNames,"RotationYawSpeed");_inner=new(profile.BoneNames.Length,profile.CurveNames);
        _pose=Enumerable.Range(0,84).Select(_=>new AlsPrecisePose[profile.BoneNames.Length]).ToArray();
        _curves=Enumerable.Range(0,84).Select(_=>new AlsInertialCurve[profile.CurveNames.Length]).ToArray();_meshScratch=new AlsQuaternion[profile.BoneNames.Length*2];
        var names=profile.CurveNames.ToArray();int[] Map(ReadOnlySpan<string> source)=>source.ToArray().Select(n=>Array.FindIndex(names,x=>x.Equals(n,StringComparison.OrdinalIgnoreCase))).ToArray();
        _maps[1]=Map(profile.GroundedCurveNames);
        for(var i=0;i<profile.Players.Length;i++)
        {Require(_players.BoneNames(i).SequenceEqual(profile.BoneNames),"Foreign air source skeleton.");_maps[profile.Players[i].PropertyIndex]=Map(_players.CurveNames(i));}
        foreach(var id in new[]{38,68})_maps[id]=Map(profile.Lean.CurveNames);
        foreach(var id in new[]{43,42,72,71})_maps[id]=Map(profile.PredictionNames[profile.Node(id).A]);
        foreach(var pair in profile.ModifiedCurves)_maps[pair.Key]=Map(pair.Value);
        Require(_maps.Where(m=>m is not null).All(m=>m.All(i=>i>=0)),"Foreign air source curve.");
        _mainClocks=profile.Main.TimingPlayers.ToArray().Select(p=>new AlsRefactoredLocomotionObservation(p.PropertyIndex,0,0)).ToArray();_nextMainClocks=new AlsRefactoredLocomotionObservation[4];
        _jumpClocks=profile.Jump.TimingPlayers.ToArray().Select(p=>new AlsRefactoredLocomotionObservation(p.PropertyIndex,0,0)).ToArray();_nextJumpClocks=new AlsRefactoredLocomotionObservation[4];
    }
    public void Prepare(in AlsPoseUpdateContext context,IAlsRefactoredLocomotionPoseSink sink,bool initialize=false)
    {
        if(_prepared||context.UpdateCounter is not{HasUpdated:true}||!context.HasSharedContext||_last.SlotGeneration!=0&&
            (context.Identity.FrameId<=_last.FrameId||context.Identity.CharacterId!=_last.CharacterId||context.Identity.SlotGeneration!=_last.SlotGeneration))
            throw new ArgumentException("Invalid Locomotion pose frame.");
        sink.Validate(context);sink.Input.Validate();_sink=sink;_context=context;_nextCounter=context.UpdateCounter.Value;
        _pending.CopyTo(_nextPending,0);_alphaInitialized.CopyTo(_nextAlphaInitialized,0);_history.CopyTo(_nextHistory,0);
        _mainClocks.CopyTo(_nextMainClocks,0);_jumpClocks.CopyTo(_nextJumpClocks,0);Array.Clear(_updated);Array.Clear(_sampled);
        _tickCount=0;GroundedUpdated=false;_nextGroundedInitialized=!initialize&&_groundedInitialized;_prepared=true;_evaluated=_faulted=false;
        try
        {
            if(initialize||!_counter.HasUpdated)Initialize(0);
            _cache.Begin(context.Identity);Update(0,context);_cache.Drain(this);
            _players.Prepare(context.Identity.FrameId,_ticks.AsSpan(0,_tickCount),context.Delta,initialize);
            foreach(var tick in _ticks.AsSpan(0,_tickCount))
            {
                var property=_p.Players[tick.PlayerId].PropertyIndex;var time=-1f;
                foreach(var player in _players.Players)if(player.PlayerId==tick.PlayerId)time=player.Time;
                Require(time>=0,"Missing Locomotion source clock.");
                for(var i=0;i<4;i++)
                {if(_nextMainClocks[i].PropertyIndex==property)_nextMainClocks[i]=new(property,tick.Weight,time);
                    if(_nextJumpClocks[i].PropertyIndex==property)_nextJumpClocks[i]=new(property,tick.Weight,time);}
            }
        }
        catch{Cancel();throw;}
    }
    private void Initialize(int id)
    {
        var n=_p.Node(id);_nextPending[id]=true;_nextAlphaInitialized[id]=false;_nextHistory[id]=0;
        if(n.Kind is AlsRefactoredLocomotionPoseKind.MainMachine or AlsRefactoredLocomotionPoseKind.JumpMachine)
        {foreach(var state in (id==83?_p.Main:_p.Jump).States)if(state.RootPropertyIndex>=0)Initialize(state.RootPropertyIndex);return;}
        if(n.Kind is AlsRefactoredLocomotionPoseKind.Player or AlsRefactoredLocomotionPoseKind.Frame or AlsRefactoredLocomotionPoseKind.Lean or
            AlsRefactoredLocomotionPoseKind.CacheRead or AlsRefactoredLocomotionPoseKind.GroundedInput)return;
        if(n.A>=0)Initialize(n.A);if(n.B>=0)Initialize(n.B);
    }
    private void Update(int id,in AlsPoseUpdateContext context)
    {
        Require(!_updated[id],"Repeated uncached Locomotion source update.");_updated[id]=true;var n=_p.Node(id);var reset=_nextPending[id];_nextPending[id]=false;
        var frame=context.Identity.FrameId;var input=_sink!.Input;input.Validate();
        switch(n.Kind)
        {
            case AlsRefactoredLocomotionPoseKind.Link:Update(n.A,context);break;
            case AlsRefactoredLocomotionPoseKind.GroundedInput:
                _sink.PrepareGrounded(context,!_nextGroundedInitialized);_nextGroundedInitialized=true;GroundedUpdated=true;break;
            case AlsRefactoredLocomotionPoseKind.CacheRead:_cache.Use(id,context);break;
            case AlsRefactoredLocomotionPoseKind.Callback:
                if(id!=3||reset||!_counter.HasUpdated||!_counter.WasSynchronizedCounter(_nextCounter))_sink.Callback(n.Function,context);
                Update(n.A,context);break;
            case AlsRefactoredLocomotionPoseKind.Inertia:
                (id==4?_outer:_inner).Prepare(context,reset);Update(n.A,context.WithInertialization(id,true));break;
            case AlsRefactoredLocomotionPoseKind.MainMachine:case AlsRefactoredLocomotionPoseKind.JumpMachine:
                var machine=id==83?_main:_jump;var resources=id==83?_p.Main:_p.Jump;var clocks=id==83?_nextMainClocks:_nextJumpClocks;
                machine.Prepare(frame,input.Rules,id==83?_mainClocks:_jumpClocks,context.Delta,context.Weight,reset,context.UpdateCounter);
                var update=machine.Candidate;
                foreach(var callback in machine.Callbacks)_sink.StateCallback(callback,context);
                for(var i=0;i<update.EventCount;i++)_sink.Notify(update.GetEvent(i),context);
                for(var i=0;i<4;i++)if((update.ClearCachedWeightStates&(1<<resources.TimingPlayers[i].State))!=0)clocks[i]=clocks[i] with{CachedWeight=0};
                for(var i=0;i<update.InitializationCount;i++)Initialize(resources.States[update.GetInitialization(i)].RootPropertyIndex);
                if(machine.InertializationRequest is{} request)(id==83?_outer:_inner).Request(request.Duration);
                for(var i=0;i<update.UpdateCount;i++)
                {var state=update.GetUpdate(i);Update(resources.States[state.State].RootPropertyIndex,context.WithWeight(state.Weight).WithState(id,state.State,state.InertializationSync));}
                break;
            case AlsRefactoredLocomotionPoseKind.Player:
                var p=_p.Players[n.A];_ticks[_tickCount]=new(n.A,default,p.JumpRate?input.JumpRate:p.Rate,context.Weight,reset,p.Start,context.InertializationSync,p.Loop);
                _tickContexts[_tickCount++]=context;break;
            case AlsRefactoredLocomotionPoseKind.Frame:break;
            case AlsRefactoredLocomotionPoseKind.Lean:_lean[n.A].Prepare(frame,input.Lean,context.Delta,0,reset);break;
            case AlsRefactoredLocomotionPoseKind.ModifyCurve:_alpha[id]=input.Read(n.Value);Update(n.A,context);break;
            default:
                var alpha=AlsOverlayPoseWeights.Alpha(input.Read(n.Value),n.Policy,context.Delta,ref _nextAlphaInitialized[id],ref _nextHistory[id]);_alpha[id]=alpha;
                if(n.Kind==AlsRefactoredLocomotionPoseKind.TwoWay)
                {if(1-alpha>AlsPoseBlender.WeightThreshold)Update(n.A,context.WithWeight(context.Weight*(1-alpha)));
                    if(alpha>AlsPoseBlender.WeightThreshold)Update(n.B,context.WithWeight(context.Weight*alpha));}
                else{Update(n.A,context);if(alpha>AlsPoseBlender.WeightThreshold)Update(n.B,context.WithWeight(context.Weight*alpha));}
                break;
        }
    }
    void IAlsPoseCacheUpdateSink.UpdateCachedSource(int cache,in AlsPoseUpdateContext context)
    {Require(cache==2,"Foreign Locomotion cache.");Update(2,context);}
    void IAlsPoseCacheUpdateSink.OnCachedUpdatesSkipped(int handler,ReadOnlySpan<AlsPoseUpdateContext> skipped)
    {Require(handler==4,"Foreign Locomotion skipped handler.");}
    public void RequestOuterInertia(in AlsFrameIdentity identity,float seconds)
    {Check();Require(identity==_context.Identity&&_updated[4],"Foreign Locomotion inertia request.");if(_evaluated)throw new InvalidOperationException("Late Locomotion inertia request.");_outer.Request(seconds);}
    public void Evaluate(in AlsPrecisePose component, long attachParent = 0, float teleportDistance = 0)
    {
        Check();_sink!.Validate(_context);_evaluated=false;Array.Clear(_sampled);
        try
        {
            component.Validate();
            if (!float.IsFinite(teleportDistance) || teleportDistance < 0) throw new ArgumentException("Invalid component teleport threshold.");
            _attachParent=attachParent;_teleportDistance=teleportDistance;
            if(GroundedUpdated)_sink.EvaluateGrounded(component);Sample(0,component);_evaluated=true;
        }
        catch{_faulted=true;throw;}
    }
    private void Sample(int id,in AlsPrecisePose component)
    {
        if(_sampled[id])return;Require(_updated[id],"Evaluating a non-updated Locomotion node.");_sampled[id]=true;
        var n=_p.Node(id);var output=_pose[id];var curves=_curves[id];var frame=_context.Identity.FrameId;
        void Copy(int child){_pose[child].CopyTo(output,0);_curves[child].CopyTo(curves,0);}
        void Source(ReadOnlySpan<AlsPrecisePose> pose,ReadOnlySpan<AlsInertialCurve> values)
        {Require(pose.Length==output.Length&&values.Length==_maps[id].Length,"Foreign Locomotion source output.");Array.Clear(curves);pose.CopyTo(output);for(var c=0;c<values.Length;c++)curves[_maps[id][c]]=values[c];}
        switch(n.Kind)
        {
            case AlsRefactoredLocomotionPoseKind.GroundedInput:Source(_sink!.GroundedPose,_sink.GroundedCurves);break;
            case AlsRefactoredLocomotionPoseKind.Player:_players.Evaluate(frame,n.A);Source(_players.Pose(n.A),_players.Curves(n.A));break;
            case AlsRefactoredLocomotionPoseKind.Lean:_lean[n.A].Evaluate(frame);Source(_lean[n.A].Pose,_lean[n.A].Curves);break;
            case AlsRefactoredLocomotionPoseKind.Frame:Source(_p.PredictionPoses[n.A],_p.PredictionCurves[n.A]);break;
            case AlsRefactoredLocomotionPoseKind.MainMachine:case AlsRefactoredLocomotionPoseKind.JumpMachine:
                var resources=id==83?_p.Main:_p.Jump;var state=(id==83?_main:_jump).Candidate.State;var stack=state.Transitions;
                var first=resources.States[stack.Count==0?state.CurrentState:stack.GetTransition(0).From].RootPropertyIndex;Sample(first,component);Copy(first);
                for(var i=0;i<stack.Count;i++)
                {
                    var edge=stack.GetTransition(i);var child=resources.States[edge.To].RootPropertyIndex;Sample(child,component);
                    var quick=resources.Edges[state.GetActiveEdge(i)].QuickFeet;
                    for(var b=0;b<output.Length;b++){var weights=quick?resources.QuickFeet!.Weights(b,edge.Alpha):new Vector2(edge.Alpha,1-edge.Alpha);
                        output[b]=AlsPrecisePoseBlender.Accumulate(AlsPrecisePoseBlender.Scale(output[b],weights.Y),_pose[child][b],weights.X);}
                    for(var c=0;c<curves.Length;c++)curves[c]=AlsStandingCycleCurves.Accumulate(AlsStandingCycleCurves.Scale(curves[c],1-edge.Alpha),_curves[child][c],edge.Alpha);
                }
                if(stack.Count>0)for(var b=0;b<output.Length;b++)output[b]=output[b].Normalized();break;
            case AlsRefactoredLocomotionPoseKind.TwoWay:
                var alpha=_alpha[id];if(alpha<=AlsPoseBlender.WeightThreshold){Sample(n.A,component);Copy(n.A);}
                else if(1-alpha<=AlsPoseBlender.WeightThreshold){Sample(n.B,component);Copy(n.B);}
                else{Sample(n.A,component);Sample(n.B,component);for(var b=0;b<output.Length;b++)output[b]=AlsPrecisePoseBlender.Blend(_pose[n.A][b],_pose[n.B][b],alpha);
                    for(var c=0;c<curves.Length;c++)curves[c]=AlsStandingCycleCurves.Lerp(_curves[n.A][c],_curves[n.B][c],alpha);}break;
            default:
                Sample(n.A,component);Copy(n.A);
                if(n.Kind is AlsRefactoredLocomotionPoseKind.LocalAdditive or AlsRefactoredLocomotionPoseKind.MeshAdditive)
                {
                    if(_alpha[id]<=AlsPoseBlender.WeightThreshold)break;Sample(n.B,component);
                    if(n.Kind==AlsRefactoredLocomotionPoseKind.MeshAdditive)AlsPrecisePoseBlender.MeshApply(_pose[n.A],_pose[n.B],_p.Parents,_meshScratch,output,_alpha[id]);
                    else for(var b=0;b<output.Length;b++)output[b]=AlsPrecisePoseBlender.LocalApply(_pose[n.A][b],_pose[n.B][b],_alpha[id]);
                    for(var c=0;c<curves.Length;c++)curves[c]=AlsStandingCycleCurves.Accumulate(curves[c],_curves[n.B][c],_alpha[id]);
                }
                else if(n.Kind==AlsRefactoredLocomotionPoseKind.ModifyCurve)
                    foreach(var c in _maps[id])curves[c]=AlsStandingCycleCurves.ModifyBlend(curves[c],1,_alpha[id]);
                else if(n.Kind==AlsRefactoredLocomotionPoseKind.Inertia)
                {var inertia=id==4?_outer:_inner;inertia.Evaluate(output,curves,component,_attachParent,_teleportDistance);inertia.Pose.CopyTo(output);inertia.Curves.CopyTo(curves);}
                break;
        }
    }
    public void ValidateCommit(in AlsFrameIdentity identity)
    {
        Check();Require(identity==_context.Identity,"Foreign Locomotion commit.");_sink!.Validate(_context);
        _main.ValidateCommit(identity.FrameId);if(_updated[64])_jump.ValidateCommit(identity.FrameId);_players.ValidateCommit(identity.FrameId);
        _outer.ValidateCommit(identity);if(_updated[39])_inner.ValidateCommit(identity);
        if(_updated[38])_lean[0].ValidateCommit(identity.FrameId);if(_updated[68])_lean[1].ValidateCommit(identity.FrameId);
    }
    public void Commit(in AlsFrameIdentity identity)
    {
        ValidateCommit(identity);_main.Commit(identity.FrameId);if(_updated[64])_jump.Commit(identity.FrameId);_players.Commit(identity.FrameId);
        _outer.Commit(identity);if(_updated[39])_inner.Commit(identity);if(_updated[38])_lean[0].Commit(identity.FrameId);if(_updated[68])_lean[1].Commit(identity.FrameId);
        _nextPending.CopyTo(_pending,0);_nextAlphaInitialized.CopyTo(_alphaInitialized,0);_nextHistory.CopyTo(_history,0);
        _nextMainClocks.CopyTo(_mainClocks,0);_nextJumpClocks.CopyTo(_jumpClocks,0);_groundedInitialized=_nextGroundedInitialized;_counter=_nextCounter;_last=identity;Cancel();
    }
    public void Cancel(){_main.Cancel();_jump.Cancel();_players.Cancel();_outer.Cancel();_inner.Cancel();foreach(var lean in _lean)lean.Cancel();_prepared=_evaluated=_faulted=GroundedUpdated=false;_sink=null;}
    private void Check(){if(!_prepared||_faulted)throw new InvalidOperationException("No valid Locomotion pose candidate.");}
    private static void Require(bool ok,string message){if(!ok)throw new ArgumentException(message);}
}
