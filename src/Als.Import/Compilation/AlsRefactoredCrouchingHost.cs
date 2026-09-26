using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Original Crouching pose closure, including deferred Movement cache1,
/// direction sources, Idle Slot28, Stop leg layers and inertia34.</summary>
public sealed class AlsRefactoredCrouchingHost : IAlsPoseCacheUpdateSink
{
    private readonly AlsRefactoredCrouchingHostProfile _p;
    private readonly uint _character,_generation;
    private readonly AlsRefactoredCrouchingRuntime _machine;
    private readonly AlsRefactoredDirectionRuntime _direction;
    private readonly AlsRefactoredDirectionSourceRuntime _sources;
    private readonly AlsRefactoredDirectionPose.Sampler _directionPose;
    private readonly AlsRefactoredSourcePlayerRuntime _players,_rotate;
    private readonly AlsRefactoredMovementParentRuntime _parent;
    private readonly AlsRefactoredRestParentRuntime _rest;
    private readonly AlsRefactoredStandingRestTraversal _restTraversal;
    private readonly AlsRefactoredStanceCallbackRuntime _callbacks;
    private readonly AlsRefactoredStandingIdleSlot _idle;
    private readonly AlsRefactoredStandingRestPose.RotateSampler _rotatePose;
    private readonly AlsRefactoredBlendEvaluatorRuntime _lean;
    private readonly AlsPoseCacheTraversal _cache;
    private readonly AlsRefactoredPoseInertia _inertia;
    private readonly AlsMontageRuntime _bank;
    private readonly AlsTransitionQueueRuntime _queue;
    private readonly AlsRefactoredCharacterActionRuntime? _shared;
    private readonly AlsRefactoredCrouchingObservation[] _clocks=new AlsRefactoredCrouchingObservation[2],_nextClocks=new AlsRefactoredCrouchingObservation[2];
    private readonly AlsRefactoredSourcePlayerInput[] _rotateInputs=new AlsRefactoredSourcePlayerInput[2];
    private readonly AlsPrecisePose[][] _states;
    private readonly AlsInertialCurve[][] _stateCurves;
    private readonly AlsPrecisePose[] _directionBuffer,_movement,_raw,_pose;
    private readonly AlsInertialCurve[] _directionCurves,_movementCurves,_rawCurves,_curves;
    private readonly AlsQuaternion[] _meshScratch;
    private readonly int[] _directionMap,_leanMap;
    private AlsFrameIdentity _identity;
    private AlsPoseUpdateContext _context;
    private AlsGraphTraversalCounter _initialization,_movementCounter,_nextMovementCounter;
    private AlsRefactoredStandingHostInput _input;
    private float _strideHistory,_nextStrideHistory,_stride;
    private bool _strideInitialized,_nextStrideInitialized;
    private bool _prepared,_evaluated,_post,_hasIdle,_hasMovement,_hasDirection,_initialize;
    public AlsFrameIdentity CommittedIdentity {get;private set;}
    public int State => _prepared?_machine.Candidate.State.CurrentState:throw new InvalidOperationException("No Crouching frame.");
    public ReadOnlySpan<string> CurveNames=>_p.CurveNames;
    public ReadOnlySpan<string> BoneNames=>_p.BoneNames;
    public ReadOnlySpan<AlsPrecisePose> Pose=>_evaluated?_pose:throw new InvalidOperationException("No Crouching pose.");
    public ReadOnlySpan<AlsInertialCurve> Curves=>_evaluated?_curves:throw new InvalidOperationException("No Crouching curves.");
    public AlsRefactoredMovementState MovementState=>_parent.MovementCandidate;
    public AlsRefactoredRestState RestState=>_rest.Candidate;
    public AlsMontageFrame MontageFrame=>_bank.Frame;
    internal bool Prepared=>_prepared;

    internal AlsRefactoredCrouchingHost(AlsRefactoredCrouchingHostProfile profile,uint character,uint generation,AlsRefactoredCharacterActionRuntime? shared=null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(generation); _p=profile; _character=character; _generation=generation; _shared=shared;
        var s=profile.Shared; _machine=new(profile.Machine); _direction=new(profile.Direction.Graph.Resources);
        _sources=new(profile.Direction,0); _directionPose=profile.DirectionPose.CreateSampler();
        _players=new(s.Catalog,s.Sync,s.Triangles,profile.Direction.Players.Bind(0,new Dictionary<string,int>{{"Movement",0}}));
        _rotate=new(s.Catalog,s.Sync,s.Triangles,profile.Machine.RotatePlayers.Bind(0));
        _bank=shared?.Bank??new([],sequences:s.Assets); _queue=shared?.Queue??new(_bank,character,generation);
        _parent=shared?.MovementParent??new(profile.Callbacks,s.MovementSettings); _rest=shared?.RestParent??new(s.Montages.Settings,s.Montages,_bank,_queue,profile.Callbacks);
        _restTraversal=new(profile.RestGraph,profile.Callbacks); _callbacks=new(profile.Callbacks);
        _idle=new(s.Catalog,profile.RestGraph,profile.Rest,profile.Montages); _rotatePose=profile.Rest.BindRotate(_rotate,0);
        _lean=new(profile.Lean); _cache=new(profile.Caches,32); _inertia=new(BoneNames.Length,CurveNames,"RotationYawSpeed");
        _states=Enumerable.Range(0,5).Select(_=>new AlsPrecisePose[BoneNames.Length]).ToArray();
        _stateCurves=Enumerable.Range(0,5).Select(_=>new AlsInertialCurve[CurveNames.Length]).ToArray();
        _directionBuffer=new AlsPrecisePose[BoneNames.Length]; _movement=new AlsPrecisePose[BoneNames.Length]; _raw=new AlsPrecisePose[BoneNames.Length]; _pose=new AlsPrecisePose[BoneNames.Length];
        _directionCurves=new AlsInertialCurve[profile.DirectionPose.CurveNames.Length]; _movementCurves=new AlsInertialCurve[CurveNames.Length];
        _rawCurves=new AlsInertialCurve[CurveNames.Length]; _curves=new AlsInertialCurve[CurveNames.Length]; _meshScratch=new AlsQuaternion[BoneNames.Length*3];
        var names=CurveNames.ToArray(); _directionMap=profile.DirectionPose.CurveNames.ToArray().Select(n=>Array.IndexOf(names,n)).ToArray();
        _leanMap=profile.Lean.CurveNames.ToArray().Select(n=>Array.IndexOf(names,n)).ToArray();
        for(var i=0;i<2;i++) _clocks[i]=new(profile.Machine.RotatePlayers.Players[i].PropertyIndex,0,0,false);
    }
    public void Prepare(in AlsPoseUpdateContext context,in AlsRefactoredStandingHostInput input,AlsGraphTraversalCounter initialization,bool initialize=false)
    {
        if(_prepared||context.Identity.CharacterId!=_character||context.Identity.SlotGeneration!=_generation||context.UpdateCounter is not {HasUpdated:true}||
            !context.HasSharedContext||_shared is null&&(input.Rest.Stance!=AlsRefactoredRestStance.Crouching||!input.QuickStop.Crouching)||context.Delta!=input.Movement.Delta||context.Delta!=input.Rest.Delta)
            throw new ArgumentException("Invalid Crouching context.");
        _context=context; _identity=context.Identity; _input=input; _initialization=initialization; _initialize=initialize; var frame=_identity.FrameId;
        try
        {
            if(_shared is null){_queue.Begin(_identity);_bank.Begin(_identity,context.Delta);}else _shared.ValidateUpdate(context);
            if(_shared is not null)_shared.PrepareParents(context,input,initialize);
            else{_parent.Prepare(_identity,input.Movement,initialize); _parent.RefreshGrounded(frame); _rest.Prepare(_identity,input.Rest,initialize);}
            var source=context.WithInertialization(34,true); _restTraversal.Begin(source,_rest,initialize);
            var rotate=_rest.Candidate.Rotate; _machine.Prepare(frame,new(input.MovingSmooth,rotate.Left,rotate.Right),_clocks,context.Delta,context.Weight,initialize,context.UpdateCounter);
            foreach(var callback in _machine.StateCallbacks)
                if(callback.Function=="PlayStopTransitionAnimation") _queue.QueuePlay(_p.StopCommand,"Als.Stance.Crouching",input.MovingSmooth);
                else _queue.QueueStop();
            _cache.Begin(_identity); _callbacks.Prepare(frame,context.UpdateCounter.Value,initialize); var rotateCount=0;
            var update=_machine.Candidate;
            for(var i=0;i<update.UpdateCount;i++)
            {
                var state=update.GetUpdate(i); var path=source.WithWeight(state.Weight).WithState(33,state.State,state.InertializationSync);
                var reset=(update.InitializeStates&(1<<state.State))!=0;
                switch(state.State)
                {
                    case 0: _hasIdle=true; _restTraversal.BeginIdle(frame); _idle.Prepare(_bank.Frame,path,initialize||reset); _restTraversal.CompleteIdle(frame); break;
                    case 1: _cache.Use(23,path); break;
                    case 4: _cache.Use(11,path); break;
                    default: _rotateInputs[rotateCount++]=_p.Machine.RotatePlayers.Input(0,state.State-2,rotate.PlayRate,rotate.Left,rotate.Right,state.Weight,reset); break;
                }
            }
            _restTraversal.Complete(frame); _cache.Drain(this); _callbacks.ValidateCommit(frame);
            if(_hasDirection)_sources.CompleteShared(frame);
            _players.Prepare(frame,_hasDirection?_sources.SourceInputs:[],context.Delta,initialize);
            _rotate.Prepare(frame,_rotateInputs.AsSpan(0,rotateCount),context.Delta,initialize);
            _inertia.Prepare(context,initialize);
            if(_machine.InertializationRequest is {} request)_inertia.Request(request.Duration);
            if(_hasIdle&&_bank.Frame.TryGetInertializationRequest(_p.Shared.Montages.HostGroupId,out var slot))_inertia.Request(slot.Duration);
            _clocks.CopyTo(_nextClocks,0);
            for(var i=0;i<2;i++) if(initialize||(update.ClearCachedWeightStates&(1<<(2+i)))!=0)_nextClocks[i]=new(_p.Machine.RotatePlayers.Players[i].PropertyIndex,0,0,false);
            foreach(var tick in _rotate.Ticks) foreach(var player in _rotate.Players) if(tick.PlayerId==player.PlayerId)
                _nextClocks[tick.PlayerId]=new(_p.Machine.RotatePlayers.Players[tick.PlayerId].PropertyIndex,tick.Weight,player.Time,tick.Looping);
            _prepared=true;
        }
        catch {Cancel();throw;}
    }
    void IAlsPoseCacheUpdateSink.UpdateCachedSource(int cache,in AlsPoseUpdateContext context)
    {
        if(cache!=1){((IAlsPoseCacheUpdateSink)_sources).UpdateCachedSource(cache,context);return;}
        _hasMovement=true; var frame=_identity.FrameId;
        Enter(44);Enter(109);
        void Enter(int id){var command=_callbacks.Enter(frame,id);if(command is not null)_parent.Apply(_identity,_p.CatalogDigest,command);}
        var reset=_initialize||!_movementCounter.HasUpdated||!_movementCounter.WasSynchronizedCounter(context.UpdateCounter!.Value);
        _nextStrideInitialized=!reset&&_strideInitialized; _nextStrideHistory=reset?0:_strideHistory;
        _stride=AlsOverlayPoseWeights.Alpha(_parent.MovementCandidate.CrouchingStride,new(1,0,false,0,1,true,10,10),context.Delta,ref _nextStrideInitialized,ref _nextStrideHistory);
        _hasDirection=_stride>AlsPoseBlender.WeightThreshold;
        if(_hasDirection)
        {
            var child=context.WithWeight(context.Weight*_stride);
            _direction.Prepare(frame,_parent.DirectionInput(_input.Details.FeetCrossing),context.Delta,child.Weight,reset,context.UpdateCounter);
            _sources.PrepareShared(_direction,child,_initialization,_parent.MovementCandidate.VelocityBlend,_parent.PlayerInput(true),default,_initialize,
                _parent.MovementCandidate.YawOffsets,_cache,_parent);
        }
        _lean.Prepare(frame,_parent.MovementCandidate.Lean,context.Delta,0,reset);
        _callbacks.Leave(frame,109); _callbacks.Leave(frame,44); _nextMovementCounter=context.UpdateCounter!.Value;
    }
    void IAlsPoseCacheUpdateSink.OnCachedUpdatesSkipped(int handler,ReadOnlySpan<AlsPoseUpdateContext> skipped)
    { if(handler!=34)throw new ArgumentException("Foreign Crouching skipped-update handler."); }
    public void Evaluate(in AlsPrecisePose component)
    {
        Check(); if(_post)throw new InvalidOperationException("Late Crouching evaluation."); var frame=_identity.FrameId;
        try
        {
            _evaluated=false;
            if(_hasMovement)
            {
                _p.Walk.CopyTo(_movement,0); _p.WalkCurves.CopyTo(_movementCurves,0);
                if(_hasDirection)
                {
                    _directionPose.Sample(frame,_direction,_sources,_players,_directionBuffer,_directionCurves);
                    for(var b=0;b<_movement.Length;b++)_movement[b]=AlsPrecisePoseBlender.Accumulate(AlsPrecisePoseBlender.Scale(_movement[b],1-_stride),_directionBuffer[b],_stride).Normalized();
                    for(var c=0;c<_movementCurves.Length;c++)_movementCurves[c]=AlsStandingCycleCurves.Scale(_movementCurves[c],1-_stride);
                    for(var c=0;c<_directionCurves.Length;c++)_movementCurves[_directionMap[c]]=AlsStandingCycleCurves.Accumulate(_movementCurves[_directionMap[c]],_directionCurves[c],_stride);
                }
                _lean.Evaluate(frame);for(var b=0;b<_movement.Length;b++)_movement[b]=AlsPrecisePoseBlender.LocalApply(_movement[b],_lean.Pose[b],1);
                for(var c=0;c<_leanMap.Length;c++)_movementCurves[_leanMap[c]]=AlsStandingCycleCurves.Accumulate(_movementCurves[_leanMap[c]],_lean.Curves[c],1);
                Set(_movementCurves,"PoseMoving",1);
            }
            var state=_machine.Candidate.State; var stack=state.Transitions; var first=stack.Count==0?state.CurrentState:stack.GetTransition(0).From;
            Sample(first); _states[first].CopyTo(_raw,0); _stateCurves[first].CopyTo(_rawCurves,0);
            for(var i=0;i<stack.Count;i++)
            {
                var edge=stack.GetTransition(i);Sample(edge.To);var quick=_p.Machine.Edges[state.GetActiveEdge(i)].QuickFeet;
                for(var b=0;b<_raw.Length;b++)
                {var weights=quick?_p.Machine.QuickFeet.Weights(b,edge.Alpha):new System.Numerics.Vector2(edge.Alpha,1-edge.Alpha);
                 _raw[b]=AlsPrecisePoseBlender.Accumulate(AlsPrecisePoseBlender.Scale(_raw[b],weights.Y),_states[edge.To][b],weights.X);}
                for(var c=0;c<_rawCurves.Length;c++)_rawCurves[c]=AlsStandingCycleCurves.Accumulate(AlsStandingCycleCurves.Scale(_rawCurves[c],1-edge.Alpha),_stateCurves[edge.To][c],edge.Alpha);
            }
            if(stack.Count>0)for(var b=0;b<_raw.Length;b++)_raw[b]=_raw[b].Normalized();
            _inertia.Evaluate(_raw,_rawCurves,component);_inertia.Pose.CopyTo(_pose);_inertia.Curves.CopyTo(_curves);Set(_curves,"PoseCrouching",1);_evaluated=true;
        }
        catch{Cancel();throw;}
        void Sample(int state)
        {
            switch(state)
            {
                case 0:_idle.Evaluate();_p.Rest.FinishIdleSlot(_idle.Pose,_idle.Curves,_rest.Candidate.TurnPlayRate,_states[0],_stateCurves[0]);break;
                case 1:_movement.CopyTo(_states[1],0);_movementCurves.CopyTo(_stateCurves[1],0);break;
                case 4:
                    AlsMeshSpacePoseBlend.Blend(_movement,_p.Walk,_p.Parents,_p.StopMask,_meshScratch,_states[4]);
                    AlsLayeringCurves.BlendLayers(_movementCurves,_p.WalkCurves,[1],AlsLayerCurveBlendMode.Override,_stateCurves[4]);
                    Set(_stateCurves[4],"FootLeftLock",1);Set(_stateCurves[4],"FootRightLock",1);break;
                default:_rotate.Evaluate(frame,state-2);_rotatePose.Sample(frame,state==2,_rest.Candidate.Rotate.PlayRate,_states[state],_stateCurves[state]);break;
            }
        }
    }
    private void Set(AlsInertialCurve[] curves,string name,float value)=>curves[_p.CurveNames.IndexOf(name)]=new(value);
    public void PostUpdateActions()
    {
        if(_shared is not null)throw new InvalidOperationException("Shared actions require character PostUpdate.");
        PostUpdateSharedActions();
    }
    internal void PostUpdateSharedActions()
    {
        Check();if(_post)throw new InvalidOperationException("Crouching actions consumed twice.");
        if(_shared is null)_p.Shared.Montages.PostUpdate(_bank,_rest,_queue,_identity);
        var update=_machine.Candidate;
        for(var i=0;i<update.EventCount;i++)if(update.GetEvent(i).NotifyIndex==0)_queue.PlayImmediate(_p.Shared.QuickStop.Command(_input.QuickStop),"Als.Stance.Crouching",_input.MovingSmooth);
        _post=true;
    }
    public void ValidateCommit(in AlsFrameIdentity identity)
    {
        Check();if(identity!=_identity||!_post)throw new ArgumentException("Incomplete Crouching frame.");var frame=identity.FrameId;
        _bank.ValidateCommit(identity);_queue.ValidateCommit(identity);_machine.ValidateCommit(frame);_parent.ValidateCommit(frame);_rest.ValidateCommit(frame);
        _restTraversal.ValidateCommit(frame);_callbacks.ValidateCommit(frame);_players.ValidateCommit(frame);_rotate.ValidateCommit(frame);_inertia.ValidateCommit(identity);
        if(_hasIdle)_idle.ValidateCommit(identity);if(_hasMovement)_lean.ValidateCommit(frame);if(_hasDirection){_direction.ValidateCommit(frame);_sources.ValidateCommit(frame);}
    }
    public void Commit(in AlsFrameIdentity identity)
    {
        if(_shared is not null)throw new InvalidOperationException("Shared Crouching commits through character.");
        CommitShared(identity);
    }
    internal void CommitShared(in AlsFrameIdentity identity)
    {
        ValidateCommit(identity);var frame=identity.FrameId;
        _restTraversal.Commit(frame);_callbacks.Commit(frame);
        _players.Commit(frame);_rotate.Commit(frame);_inertia.Commit(identity);if(_hasIdle)_idle.Commit(identity);if(_hasMovement)_lean.Commit(frame);
        if(_hasDirection){_sources.Commit(frame);_direction.Commit(frame);}
        _machine.Commit(frame);if(_shared is null){_parent.Commit(frame);_rest.Commit(frame);}
        if(_shared is null){_queue.Commit(identity);_bank.Commit(identity);}
        _nextClocks.CopyTo(_clocks,0);if(_hasMovement){_strideHistory=_nextStrideHistory;_strideInitialized=_nextStrideInitialized;_movementCounter=_nextMovementCounter;}
        CommittedIdentity=identity;CancelGraph();
    }
    public void Cancel(){if(_shared is not null)_shared.Discard();else CancelGraph();}
    internal void CancelGraph()
    {
        _machine.Cancel();if(_shared is null){_parent.Cancel();_rest.Cancel();}_restTraversal.Cancel();_callbacks.Cancel();_players.Cancel();_rotate.Cancel();_idle.Cancel();_lean.Cancel();_sources.Cancel();_direction.Cancel();_inertia.Cancel();
        if(_shared is null){_queue.Discard();_bank.Discard();}_prepared=_evaluated=_post=_hasIdle=_hasMovement=_hasDirection=false;
    }
    private void Check(){if(!_prepared)throw new InvalidOperationException("No Crouching frame.");}
}
