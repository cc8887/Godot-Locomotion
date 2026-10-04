using System.Collections.Immutable;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Animation;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraMainRelevantSource(bool Valid,int Node,int Asset,float Weight,
    float Length,float Time,bool Looping,bool PreviousValid,float Previous,float Delta);
internal sealed record LyraMainLocomotionCandidate(LyraLocomotionMachineCandidate Machine,
    LyraLocomotionScopeCandidate Sources,LyraMainUpdateCandidate Macro,LyraLocomotionRuleInputs Rules,ImmutableArray<float> Inertia,ImmutableArray<bool> EvaluationRoots,
    LyraLeftHandLayerCandidate? LeftHand=null,LyraIdleMachineCandidate? Additives=null,LyraAimingCandidate? Aiming=null,LyraSkeletalControlsCandidate? Skeletal=null,
    LyraMainCacheResult? Caches=null,bool LinkedObservationVisited=false);

internal readonly record struct LyraMainSkeletalSettings(bool EnableControlRig,bool UseFootPlacement);
internal sealed record LyraMainLayerReplacement(LyraMainLocomotionHost Owner,LyraLinkedLayerGraphSet Previous,
    LyraItemLayerGraphInstance Next,LyraLinkedLayerGraphSet Graphs,LyraSourceClass Provider,LyraSourceNode[][] RelevantNodes,float[] Weights,LyraAimingGraphDefinition NotifyAiming);

// One character's Main macro, real state selection, ten linked roots and Sync.
// The enclosing upper-body/additive graph must supply its final curve feedback.
// LocomotionSM output deliberately precedes that graph's inertialization node.
internal sealed class LyraMainLocomotionHost
{
    internal AlsAnimationProxyTraversal ProxyTraversal {get;}=new();
    private static readonly int[] StateRoots=[4,2,1,3,0,-1,5,7,9,-1,6,8];
    private static readonly string[] Graphs=["FullBody_PivotState","FullBody_CycleState","FullBody_StartState","FullBody_StopState",
        "FullBody_IdleState","FullBody_JumpStartState","FullBody_JumpStartLoopState","FullBody_JumpApexState","FullBody_FallLoopState","FullBody_FallLandState"];
    private static readonly LyraLayerHook[] LayerRoots=Enumerable.Range(0,10).Select(LyraItemLayerGraphInstance.HookForRoot).ToArray();
    private readonly LyraLocomotionResourceCatalog _resources;
    private readonly LyraLocomotionResources _resourceSet;
    private readonly int _leanPlayerBase;
    internal string Profile {get;private set;}
    internal long LayerEpoch {get;private set;}
    internal bool IsLinked=>!_graphSet.IsSelf;
    private LyraMainDefaultRootFrameHost? _defaultHost;
    private LyraMainDefaultRootFrame? _defaultPending;
    private (float Remaining,float Weight)? _defaultFeedback;
    internal LyraLocomotionResourceCatalog Resources=>_resources;
    private readonly string _mainLeanNotifyAsset;
    private LyraAimingGraphDefinition _notifyAiming;
    private LyraSourceClass _provider;
    private readonly AlsLinkedLayerBindings _layerBindings;
    internal AlsLinkedLayerTarget LayerBinding(string node)=>_layerBindings.Target(node);
    private readonly LyraLinkedLayerContracts _contracts;
    private LyraLinkedLayerGraphSet _graphSet;
    internal IReadOnlyList<LyraItemLayerGraphInstance> LayerInstances=>_graphSet.Instances;
    internal LyraLinkedLayerCallRoutes LayerCalls=>_graphSet.Calls;
    internal LyraItemLayerGraphInstance Layer(LyraLayerHook hook)=>_graphSet.Layer(hook);
    internal LyraItemLayerGraphInstance LayerByClass(string classPath)=>_graphSet.ByClass(classPath);
    internal void ValidateLayerRoutes()=>_graphSet.ValidateTargets(_layerBindings.Targets);
    private LyraSourceNode[][] _relevantNodes;
    private float[] _cachedWeights;
    private bool _initializeLinked;
    // Main's state-machine Update observes the linked instance identity. Hidden
    // LocomotionSM frames do not consume a replacement, and a cancelled update
    // must see the same change again on retry.
    private long _observedLinkedEpoch;
    private bool _linkedLayerChanged;
    private readonly Dictionary<LyraLayerHook,float> _pendingBlendOut=[];
    private readonly HashSet<LyraLayerHook> _blendOutConsumed=[];
    private float[]? _nextWeights;
    // FAnimSync keeps keys in each map when resetting a write buffer. A named
    // empty group can be valid; an unregistered output slot cannot create it.
    private readonly bool[][] _groupExists=[new bool[3],new bool[3]];
    private int _writeIndex;
    private AlsAssetSyncBatchGroupHistory[] _groups=[];
    private AlsAssetPlayerHistory[] _players=[];
    private AlsAssetSampleHistory[] _samples=[];
    private AlsAssetSyncBatchGroupHistory[]? _nextGroups;
    private AlsAssetPlayerHistory[]? _nextPlayers;
    private AlsAssetSampleHistory[]? _nextSamples;
    private AlsAssetPlayerTickContext[]? _nextTicks;
    private bool[]? _nextExists;
    private LyraMainLocomotionCandidate? _pending;
    private bool _failed,_evaluated,_feedback;
    private readonly AlsPrecisePose[] _pose=new AlsPrecisePose[81];
    private readonly AlsInertialCurve[] _poseCurves;
    private readonly LyraCurveSample[] _curves;
    private readonly LyraAttributeSample[] _attributes;
    private readonly bool _rootOverride;
    private readonly float _additivesAlpha;
    internal AlsPoseCacheLifecycle CacheLifecycle {get;}=new(78,83);
    private readonly LyraMainPoseCacheTraversal _poseCaches;
    private readonly LyraMainSlotsTraversal? _slotTraversal;
    private readonly AlsMontageRuntime? _montageRuntime;
    private AlsStopMovementSnapshot _movementBinding;
    private AlsStopMovementSnapshot _nextMovementBinding;
    private AlsFrameIdentity? _montageIdentity;
    private readonly uint _characterId, _slotGeneration;
    private LyraRootMotionAttribute _root;
    public LyraLocomotionMachineHost Machine {get;}
    internal LyraMainGraphStateOwner MainState {get;}
    public LyraItemLayerGraphInstance Layers=>_graphSet.First;
    public LyraLocomotionSourceScope Sources=>_graphSet.Sources;
    public LyraMainSlotsTraversal? SlotTraversal=>_montageIdentity is null?null:_slotTraversal;
    public ReadOnlySpan<AlsAssetSyncBatchGroupHistory> SyncGroups=>_groups;
    public ReadOnlySpan<AlsAssetPlayerHistory> SyncPlayers=>_players;
    public ReadOnlySpan<AlsAssetSampleHistory> SyncSamples=>_samples;
    public int WriteIndex=>_writeIndex;
    public ReadOnlySpan<float> CachedWeights=>_cachedWeights;
    public bool GroupExists(int buffer,int group)=>_groupExists[buffer][group];
    public float CachedWeight(int node)=>_cachedWeights[node];
    internal LyraMainLayerReplacement PrepareReplacement(LyraLocomotionResources resources,string profile,int playerBase,long epoch)
    {
        if(_pending is not null||_defaultPending is not null||!ReferenceEquals(resources.Catalog,_resources)||epoch<=LayerEpoch)
            throw new InvalidOperationException("Linked replacement needs the idle Main and a new epoch.");
        var preview=_layerBindings.PrepareLink(resources.LayerGraphs.ClassPath(profile));
        LyraLinkedLayerGraphSet graphs;
        try
        {
            graphs=new(resources,_contracts,preview.Targets,profile,playerBase,_leanPlayerBase,epoch,MainState,this);
            InitializePreUpdates(graphs);
        }
        finally{_layerBindings.Cancel(preview);}
        var next=graphs.First;
        var provider=LyraSourceNodeCatalog.Load().ForClass(next.ClassPath);
        if(provider.NodeCount>256)throw new NotSupportedException("Replacement source inventory exceeds its identity range.");
        var nodes=Graphs.Select(name=>provider.Graphs.TryGetValue(name,out var ids)?ids.Select(n=>provider.Nodes[n]).ToArray():[]).ToArray();
        int[] counts=[0,1,1,1,0,1,1,1,1,1];
        if(nodes.Where((group,n)=>group.Count(s=>!s.IgnoreRelevancy)!=counts[n]).Any())throw new NotSupportedException("Changed replacement relevant-player inventory.");
        // All supported classes retain the original Main and cache topology.
        _=new LyraMainPoseCacheTraversal(resources.LayerGraphs,profile);
        return new(this,_graphSet,next,graphs,provider,nodes,new float[provider.NodeCount],new(resources.LayerGraphs,profile));
    }
    internal void ApplyReplacement(LyraMainLayerReplacement replacement)
    {
        if(_pending is not null||_defaultPending is not null||!ReferenceEquals(replacement.Owner,this)||!ReferenceEquals(replacement.Previous,_graphSet))
            throw new InvalidOperationException("Stale linked replacement.");
        var previous=IsLinked?Layers:null;
        // Resolve all metadata before publishing the binding transaction. Main
        // is in the compiled catalog, not the external Provider registry.
        var priorBlend=previous is not null?previous.Contract.Functions.ToDictionary(p=>p.Key,p=>p.Value.BlendOutTime):
            _contracts.CallSites.ToDictionary(c=>c.Hook,c=>_contracts.MainBlendOut(c.Hook));
        var bindings=_layerBindings.PrepareLink(replacement.Next.ClassPath);
        try
        {
            replacement.Graphs.ValidateTargets(bindings.Targets);
            if(IsLinked)replacement.Graphs.Sources.AdoptMain(Sources);
        }
        catch{_layerBindings.Cancel(bindings);throw;}
        _layerBindings.Commit(bindings);
        // ReLink replaces the self target, including requests left unconsumed
        // while its empty root pruned their call sites. Prior class is Main.
        foreach(var pair in priorBlend)_pendingBlendOut[pair.Key]=pair.Value;
        _graphSet.Retire();_graphSet=replacement.Graphs;_provider=replacement.Provider;
        _relevantNodes=replacement.RelevantNodes;_cachedWeights=replacement.Weights;_initializeLinked=true;
        _notifyAiming=replacement.NotifyAiming;
        Profile=replacement.Next.Profile;LayerEpoch=replacement.Next.Epoch;_defaultHost=null;
    }
    internal bool Unlink(long epoch)
    {
        if(_pending is not null||_defaultPending is not null)throw new InvalidOperationException("Cannot unlink a pending Main frame.");
        MainState.RequireIdle();if(!IsLinked)return false;
        if(epoch<=LayerEpoch)throw new InvalidOperationException("Unlink requires a new binding epoch.");
        var bindings=_layerBindings.PrepareUnlink(Layers.ClassPath);
        LyraLinkedLayerGraphSet next;LyraMainDefaultRootFrameHost host;
        try
        {
            next=new(_resourceSet,_contracts,bindings.Targets,Profile,0,_leanPlayerBase,epoch,MainState,this);
            if(!next.IsSelf)throw new NotSupportedException("Unlink did not restore the complete Main self target set.");
            host=new(_resourceSet,MainState,this,_characterId,_slotGeneration,next.Calls);
        }
        catch{_layerBindings.Cancel(bindings);throw;}
        _layerBindings.Commit(bindings);
        foreach(var pair in Layers.Contract.Functions)_pendingBlendOut[pair.Key]=pair.Value.BlendOutTime;
        _graphSet.Retire();_graphSet=next;_defaultHost=host;LayerEpoch=epoch;
    return true;
    }
    public LyraMainLocomotionHost(LyraLocomotionResources resources,string profile,int playerBase=0,int leanPlayerBase=1000,long epoch=1,
        LyraItemLayerGraphInstance? layers=null,AlsMontageRuntime? montageRuntime=null,uint characterId=0,uint slotGeneration=1,
        LyraLinkedLayerContracts? contracts=null,AlsStopMovementSnapshot? initialMovement=null,bool linkInitially=true)
    {
        if(slotGeneration==0)throw new ArgumentOutOfRangeException(nameof(slotGeneration));
        if(!linkInitially&&layers is not null)throw new ArgumentException("Initial self cannot borrow a Linked graph.");
        _characterId=characterId;_slotGeneration=slotGeneration;
        _resources=resources.Catalog;
        _resourceSet=resources;_leanPlayerBase=leanPlayerBase;Profile=profile;LayerEpoch=epoch;
        _contracts=contracts??LyraLinkedLayerContracts.LoadRuntime();_layerBindings=_contracts.CreateBindings();
        var initialBindings=linkInitially?_layerBindings.PrepareLink(resources.LayerGraphs.ClassPath(profile)):null;
        _notifyAiming=new(resources.LayerGraphs,profile);
        _poseCaches=new(resources.LayerGraphs,profile,(node,weight)=>
        {var owner=node==181?Layer(LyraLayerHook.FullBody_Aiming).CacheLifecycle:CacheLifecycle;owner.RecordWeight(node==181?78:node,weight);});
        _slotTraversal=montageRuntime is null?null:new(montageRuntime);
        _montageRuntime=montageRuntime;
        _movementBinding=initialMovement??GodotAls.Locomotion.LyraCharacterMovementSettings.Default.Snapshot(AlsDoubleVector.Zero);
        _additivesAlpha=resources.LayerGraphs.MainAdditivesAlpha;
        if(_additivesAlpha!=.65f)throw new NotSupportedException("Changed original Main additive update weight.");
        _provider=LyraSourceNodeCatalog.Load().ForClass(_resources.Provider(profile).GetProperty("class").GetString()!);
        _relevantNodes=Graphs.Select(name=>_provider.Graphs.TryGetValue(name,out var nodes)?nodes.Select(n=>_provider.Nodes[n]).ToArray():[]).ToArray();
        int[] counts=[0,1,1,1,0,1,1,1,1,1];
        if(_relevantNodes.Where((nodes,n)=>nodes.Count(s=>!s.IgnoreRelevancy)!=counts[n]).Any())
            throw new NotSupportedException("Changed Main relevant-player graph inventory.");
        // Original direct Main Lean occurrences precede linked graph players,
        // but all three are ignored for relevance. Reject a changed contract.
        var main=LyraSourceNodeCatalog.Load().ForClass(LyraRuntimeGraphCatalog.MainClass);
        _mainLeanNotifyAsset=main.Nodes[22].Asset;
        if(new[]{12,16,22}.Any(n=>!main.Nodes[n].IgnoreRelevancy))throw new NotSupportedException("Main Lean relevancy changed.");
        MainState=layers?.Sources.MainOwner??resources.CreateMainOwner(leanPlayerBase,epoch);
        _cachedWeights=new float[_provider.NodeCount];
        _graphSet=new(resources,_contracts,initialBindings?.Targets??_layerBindings.Targets,profile,playerBase,leanPlayerBase,epoch,MainState,this,layers);
        InitializePreUpdates(_graphSet);
        if(initialBindings is not null)_layerBindings.Commit(initialBindings);
        else _defaultHost=new(resources,MainState,this,_characterId,_slotGeneration,_graphSet.Calls);
        var count=_resources.Bank.Curves.Names.Length;Machine=new(LyraRuntimeGraphCatalog.Load(),count);
        _poseCurves=new AlsInertialCurve[count];_curves=new LyraCurveSample[count];
        _attributes=new LyraAttributeSample[_resources.Bank.Curves.Attributes.Layout.Length];_rootOverride=LyraRootMotionAttribute.LoadBlendPolicy();
    }
    internal void SetLeftHandPoseOverrideEnabled(bool enabled)
    {
        if(_pending is not null||MainState.Update.HasPending)
            throw new InvalidOperationException("Cannot change linked settings during a character animation frame.");
        foreach(var instance in LayerInstances)instance.ValidateLeftHandSettings(this);
        foreach(var instance in LayerInstances)instance.SetLeftHandPoseOverrideEnabled(this,enabled);
    }
    internal static int RootForState(int state)=>(uint)state<12 && StateRoots[state]>=0?StateRoots[state]:throw new ArgumentException("Conduit has no source root.");
    public bool SyncValid(int group)
    {
        if((uint)group>=3)throw new ArgumentOutOfRangeException(nameof(group));
        if(!_groupExists[1-_writeIndex][group])return false;
        var result=_groups[group].Group;
        return result.ValidMarkerMask==0 || result.MarkerEnd.Valid;
    }
    public LyraMainRelevantSource Relevant(int state)
    {
        var root=RootForState(state);LyraSourceNode? best=null;var weight=0f;
        foreach(var node in _relevantNodes[root])
            if(!node.IgnoreRelevancy && _cachedWeights[node.Index]>weight){best=node;weight=_cachedWeights[node.Index];}
        if(best is null)return default;
        var id=-1;var time=0f;var previous=0f;var delta=0f;
        var h=Sources.Hosts;
        if(root==2){var s=h.Ground.Start.Start;id=s.AssetId;time=s.ExplicitTime;previous=s.DeltaPrevious;delta=s.Delta;}
        else if(root==1){var s=h.Ground.Cycle.Cycle;id=s.AssetId;time=s.Time;previous=s.DeltaPrevious;delta=s.Delta;}
        else if(root==3){var s=h.Ground.Stop.Stop;id=s.AssetId;time=s.ExplicitTime;previous=s.DeltaPrevious;delta=s.Delta;}
        else if(root>=5){var s=h.Air[root-5].State.Base;id=s.AssetId;time=s.PublicTime;previous=s.Previous;delta=s.Delta;}
        else throw new NotSupportedException("Changed Main relevant-player graph map.");
        if(id<0)return default;
        return new(true,best.Index,id,weight,_resources.Sequences[id].DurationSeconds,time,best.Looping,true,previous,delta);
    }
    private void InitializePreUpdates(LyraLinkedLayerGraphSet graphs)
    {
        var main=MainState.Update.State;
        foreach(var instance in graphs.Instances)
            instance.InitializePreUpdate(_movementBinding,_montageRuntime is not null&&_montageRuntime.Committed.Length>0,main.HasVelocity,main.Jumping);
    }
    public LyraMainLocomotionCandidate Prepare(in LyraMainUpdateInput input,float delta,LyraLocomotionMachineVisit visit,
        double hipWeight,AlsPrecisePose component,AlsQuaternion relativeRotation,AlsStopMovementSnapshot movement,double groundDistance,
        bool melee=false,bool pivotNotify=false,bool updateLeftHand=false,bool updateAdditives=false,double? timeSinceFired=null,LyraMainSkeletalSettings? skeletalSettings=null,
        AlsMontageFrame? montageFrame=null,bool linkedMachineCallback=false,bool fullMainRoot=false,ulong? proxyExternalFrame=null)
    {
        if(_pending is not null||_defaultPending is not null||!IsLinked)throw new InvalidOperationException("Main locomotion requires idle external bindings.");
        _failed=_evaluated=_feedback=false;_blendOutConsumed.Clear();
        try
        {
            // Root callbacks own the mode carried into the next Main update.
            var gathered=input with{RootYawMode=Sources.Tail.Mode};
            var previous=Sources.Main;
            _nextMovementBinding=movement;
            bool preUpdateMontage=_montageRuntime is null?input.MontagePlaying:_montageRuntime.IsAnyMontagePlayingBeforeAdvance;
            var macro=Sources.Hosts.Ground.Scope.BeginMain(gathered,delta);var s=macro.State;
            if(fullMainRoot)
            {
                var executionFrame=proxyExternalFrame??checked((ulong)macro.Observation.Frame);
                ProxyTraversal.Begin(macro,executionFrame);
                foreach(var instance in LayerInstances)instance.ProxyTraversal.Begin(macro,executionFrame);
                if(visit.Visited)ProxyTraversal.Advance(macro,AlsAnimationProxyPhase.Update);
            }
            var workerFrame=new object();
            if(timeSinceFired is not null)foreach(var instance in LayerInstances)
                instance.PreparePreUpdate(workerFrame,macro.Observation.Frame,new(preUpdateMontage,previous.HasVelocity,previous.Jumping,
                    new(input.Observation.Acceleration,movement.LastUpdateVelocity,movement.GroundFriction),movement));
            LyraLinkedWorkerState? Worker(LyraLayerHook hook,LyraMainUpdateCandidate observed,bool visited)
            {
                var instance=Layer(hook);
                if(fullMainRoot&&visited)
                {
                    instance.ProxyTraversal.Synchronize(macro,AlsAnimationProxyPhase.Update,ProxyTraversal.Prepared(macro));
                    instance.Phases.EnterUpdateRoot(hook,macro);
                }
                return timeSinceFired is {} firedAtVisit?instance.PrepareWorker(hook,workerFrame,observed,delta,visited,firedAtVisit):null;
            }
            double RootWorkerHip(int root,LyraMainUpdateCandidate observed,bool visited)
                =>Worker(LyraItemLayerGraphInstance.HookForRoot(root),observed,visited)!.Value.Weights.HipFire;
            var outerVisit=visit;
            bool linkedInitialize=visit.Initialize||_initializeLinked;
            var identity=new AlsFrameIdentity(macro.Observation.Frame,_characterId,_slotGeneration);
            CacheLifecycle.Begin(macro);
            foreach(var instance in LayerInstances)
            {
                instance.CacheLifecycle.Begin(macro);
                if(fullMainRoot)instance.Phases.Begin(macro);
            }
            foreach(var instance in LayerInstances)instance.PrepareMontageUpdate(identity,delta);
            if((_slotTraversal is null)!=(montageFrame is null)||montageFrame is not null&&timeSinceFired is null)
                throw new InvalidOperationException("Active Main Slots require the bound physical bank and Aiming traversal.");
            if(montageFrame is not null)
            {
                var context=new AlsPoseUpdateContext(identity,visit.Weight,delta).WithInertialization(75,true);
                if(!visit.Active)context=context.AsInactive();
                _slotTraversal!.Begin(montageFrame,context,visit.Visited,visit.Initialize,(float)macro.Tail.UpperbodyWeight);
                _montageIdentity=identity;
            }
            var skeletal=skeletalSettings is {} control?Layer(LyraLayerHook.FullBody_SkeletalControls).PrepareSkeletal(Layer(LyraLayerHook.FullBody_SkeletalControls).Call(LyraLayerHook.FullBody_SkeletalControls),
                new(delta,fullMainRoot?ProxyTraversal.Prepared(macro).Update.Counter:unchecked((short)((macro.Observation.Frame+1)%65535)),visit.Visited,linkedInitialize,control.EnableControlRig,control.UseFootPlacement,default),
                Worker(LyraLayerHook.FullBody_SkeletalControls,macro,visit.Visited)):null;
            var aimingVisit=montageFrame is null?new LyraAirVisit(visit.Visited,visit.Weight,visit.Initialize,visit.Active):
                _slotTraversal!.AimingSource is {} aimContext?new LyraAirVisit(true,aimContext.Weight,visit.Initialize,aimContext.IsActive):new LyraAirVisit(false,0,visit.Initialize);
            aimingVisit=aimingVisit with{Initialize=aimingVisit.Initialize||_initializeLinked};
            var aiming=timeSinceFired is {} fired?Layer(LyraLayerHook.FullBody_Aiming).PrepareAimingTyped(Layer(LyraLayerHook.FullBody_Aiming).Call(LyraLayerHook.FullBody_Aiming),
                new(s.Crouching,s.Ground,s.Ads,fired,s.RootYaw,s.HasAcceleration,delta,0),new(macro.Tail.AimYaw,macro.Tail.AimPitch),
                aimingVisit,Worker(LyraLayerHook.FullBody_Aiming,macro,aimingVisit.Visited)):null;
            CacheLifecycle.PostUpdate(macro,[]);
            if(aiming is not null&&aiming.Visit.Visited)Layer(LyraLayerHook.FullBody_Aiming).CacheLifecycle.PostUpdate(macro,[]);
            LyraMainCacheResult? caches=null;
            if(aiming is not null)
            {
                hipWeight=aiming.Weights.Weights.HipFire;
                if(montageFrame is null)caches=_poseCaches.Resolve(aiming,identity,LyraMainCacheSourceWeights.InactiveSlots);
                else
                {
                    var readers=new List<LyraMainCacheUpdate>();
                    if(_slotTraversal!.AimingSource is {} inherited)for(var n=0;n<2;n++)if(aiming.Active[n])
                        readers.Add(new(n==0?76:75,inherited.WithWeight(aiming.Nodes[n].Weight)));
                    caches=_poseCaches.Resolve(identity,readers.ToArray(),_slotTraversal);
                }
                if(caches.Locomotion is {} selected)
                    visit=visit with{Weight=selected.Weight,Active=selected.IsActive};
                else if(montageFrame is not null)visit=visit with{Visited=false,Weight=0};
            }
            // ApplyAdditive enqueues its cached base branch, then updates the
            // additive root. LeftHand/Locomotion run when that cache drains.
            var additiveVisit=montageFrame is null?new LyraAirVisit(outerVisit.Visited,outerVisit.Weight*_additivesAlpha,outerVisit.Initialize,outerVisit.Active):
                _slotTraversal!.AdditivesSource is {} additiveContext?new LyraAirVisit(true,additiveContext.Weight,outerVisit.Initialize,additiveContext.IsActive):new LyraAirVisit(false,0,outerVisit.Initialize);
            additiveVisit=additiveVisit with{Initialize=additiveVisit.Initialize||_initializeLinked};
            var additive=updateAdditives?Layer(LyraLayerHook.FullBodyAdditives).PrepareAdditives(Layer(LyraLayerHook.FullBodyAdditives).Call(LyraLayerHook.FullBodyAdditives),s.Ground,delta,
                additiveVisit,s.Falling,s.Jumping,s.Crouching,
                Worker(LyraLayerHook.FullBodyAdditives,macro,additiveVisit.Visited)):null;
            // Thread-safe Main update precedes graph traversal. The original
            // left-hand callback then runs before its input LocomotionSM.
            if(updateLeftHand)_=Worker(LyraLayerHook.LeftHandPose_OverrideState,macro,visit.Visited);
            var left=updateLeftHand?Layer(LyraLayerHook.LeftHandPose_OverrideState).PrepareLeftHand(Layer(LyraLayerHook.LeftHandPose_OverrideState).Call(LyraLayerHook.LeftHandPose_OverrideState),
                visit.Visited,linkedInitialize,visit.Weight):null;
            // Explicit Initialize precedes the native rule observation. The
            // initial Idle state has no direct relevant asset player.
            var graph=Sources.Hosts.Ground.Scope.GraphState;var r=visit.Initialize?default:Relevant(Machine.State);
            // The complete Main calls its node's OnUpdate before evaluating
            // transition rules. Direct state-machine component evaluation does
            // not invoke that outer callback (as in the component UE oracle).
            bool observeLinked=linkedMachineCallback&&visit.Visited;
            bool linkedChanged=observeLinked?Layers.Epoch!=_observedLinkedEpoch:_linkedLayerChanged;
            var rules=new LyraLocomotionRuleInputs(s.HasAcceleration,s.HasVelocity,melee,s.Wall,linkedChanged,s.CrouchChanged,s.AdsChanged,
                s.Jumping,s.Falling,s.Ground,s.LocalVelocity,s.LocalAcceleration,graph.StartDirection,s.Direction,graph.Pivot.InitialDirection,
                s.DisplacementSpeed,s.RootYaw,graph.Pivot.LastPivotTime,macro.Tail.TimeToApex,groundDistance,visit.Initialize?0:Machine.Elapsed,pivotNotify,
                SyncValid(0),r.Valid,r.Length,r.Time,r.Looping,r.PreviousValid,r.Previous,r.Delta);
            var machine=Machine.Prepare(rules,delta,visit);var visits=new LyraAirVisit[10];
            for(var state=0;state<12;state++)if(StateRoots[state]>=0 && machine.Initializations[state]>0)
                visits[StateRoots[state]]=new(false,0,true);
            var order=new List<int>();
            foreach(var update in machine.Updates)
            {
                var root=RootForState(update.State);var initialize=visits[root].Initialize;
                visits[root]=new(true,update.Weight,initialize,update.Active,update.Inertial);order.Add(root);
            }
            order.AddRange(Enumerable.Range(0,10).Except(order));
            var source=Sources.Prepare(gathered,delta,visits,order.ToArray(),hipWeight,component,relativeRotation,movement,
                new(Machine.PreparedState(machine),machine.PreviousWeights[1],machine.PreviousWeights[2],machine.PreviousWeights[3]),
                groundDistance,machine.PreviousWeights[0],macro,_initializeLinked,timeSinceFired is null?null:RootWorkerHip,
                timeSinceFired is null?null:Layer(LyraLayerHook.FullBody_IdleState).PreparedPreUpdateState,
                timeSinceFired is null?null:root=>Layer(LyraItemLayerGraphInstance.HookForRoot(root)).PreparedPreUpdateState);
            _nextWeights=_cachedWeights.ToArray();
            foreach(var state in machine.ClearWeights)
                foreach(var node in _relevantNodes[RootForState(state)])_nextWeights[node.Index]=0;
            foreach(var player in source.Players)
            {
                if(player.Kind==AlsAssetSyncKind.Sequence && _graphSet.TrySource(player,out _,out var node))_nextWeights[node]=player.Weight;
            }
            var requests=source.Inertia;
            if(machine.Selected is {Inertial:true} transition)requests=ImmutableArray.Create(transition.Duration).AddRange(requests);
            var needed=new bool[10];var stack=Machine.PreparedStack(machine);
            if(machine.Visited)
            {
                if(stack.Count==0)needed[RootForState(stack.CurrentState)]=true;
                else for(var n=0;n<stack.Count;n++)
                {var entry=stack.GetTransition(n);if(n==0)needed[RootForState(entry.From)]=true;needed[RootForState(entry.To)]=true;}
            }
            if(aiming is not null)source=Layer(LyraLayerHook.FullBody_Aiming).EnrollAimingSources(aiming,source);
            if(additive is not null)source=Layer(LyraLayerHook.FullBodyAdditives).EnrollAdditiveSources(additive,source);
            // LinkedAnimGraph requests old BlendOut and new BlendIn at the
            // actual call site. Keep unvisited requests until that call runs.
            foreach(var pair in _pendingBlendOut)
            {
                var root=Array.IndexOf(LayerRoots,pair.Key);
                var visited=root>=0?visits[root].Visited:pair.Key==LyraLayerHook.FullBody_Aiming?aiming is not null&&aimingVisit.Visited:
                    pair.Key==LyraLayerHook.FullBodyAdditives?additive is not null&&additiveVisit.Visited:
                    pair.Key==LyraLayerHook.LeftHandPose_OverrideState?left is not null&&visit.Visited:skeletal is not null&&outerVisit.Visited;
                if(visited)
                {
                    _blendOutConsumed.Add(pair.Key);if(pair.Value>=0)requests=requests.Add(pair.Value);
                    // Locomotion source roots already enqueue the new BlendIn.
                    var blendIn=Layers.Contract.Functions[pair.Key].BlendInTime;
                    if(root<0&&blendIn>=0)requests=requests.Add(blendIn);
                }
            }
            _pending=new(machine,source,macro,rules,requests,needed.ToImmutableArray(),left,additive,aiming,skeletal,caches,observeLinked);
            if(left is not null)Layer(LyraLayerHook.LeftHandPose_OverrideState).BindLeftFrame(left,source);
            if(additive is not null)Layer(LyraLayerHook.FullBodyAdditives).BindAdditiveFrame(additive,source);
            if(aiming is not null)Layer(LyraLayerHook.FullBody_Aiming).BindAimingFrame(aiming,source);
            if(skeletal is not null)Layer(LyraLayerHook.FullBody_SkeletalControls).BindSkeletalFrame(skeletal,source);
            _nextGroups=new AlsAssetSyncBatchGroupHistory[3];_nextPlayers=new AlsAssetPlayerHistory[source.Players.Length];
            _nextSamples=new AlsAssetSampleHistory[source.Samples.Length];_nextTicks=new AlsAssetPlayerTickContext[source.Players.Length];
            if(!AlsSyncRuntime.TryEvaluateAssetSyncBatch([0,1,2],source.Groups,source.Players,source.Samples,
                _resources.Sequences,_resources.Markers,_groups,_players,_samples,delta,_nextGroups,_nextPlayers,_nextSamples,out var failure,_nextTicks))
                throw new InvalidOperationException($"Main common Sync failed: {failure}, frame={macro.Observation.Frame}, delta={delta:R}, players={source.Players.Length}, samples={source.Samples.Length}.");
            _nextExists=_groupExists[_writeIndex].ToArray();foreach(var group in source.Groups)if(group>=0)_nextExists[group]=true;
            Sources.Resolve(source,_nextPlayers,_nextSamples);
            if(additive is not null)Layer(LyraLayerHook.FullBodyAdditives).ResolveAdditives(additive,_nextPlayers);
            if(aiming is not null)Layer(LyraLayerHook.FullBody_Aiming).ResolveAiming(aiming,_nextPlayers,_nextSamples);
            return _pending;
        }
        catch{Cancel();throw;}
    }
    internal LyraMainDefaultRootFrame PrepareDefault(in LyraMainUpdateInput input,float delta,bool visited,
        bool initialize,AlsStopMovementSnapshot movement,AlsMontageFrame? montage,bool fullMainRoot=false,ulong? proxyExternalFrame=null)
    {
        if(IsLinked||_defaultHost is null||_pending is not null||_defaultPending is not null)
            throw new InvalidOperationException("Default Main requires idle self bindings.");
        try
        {
            void EnterRoot(LyraMainUpdateCandidate macro)
            {
                if(!fullMainRoot)return;
                ProxyTraversal.Begin(macro,proxyExternalFrame??checked((ulong)macro.Observation.Frame));
                if(visited)ProxyTraversal.Advance(macro,AlsAnimationProxyPhase.Update);
            }
            var frame=_defaultHost.Prepare(input,delta,visited,EnterRoot);_defaultPending=frame;_nextMovementBinding=movement;
            CacheLifecycle.Begin(frame.Macro);CacheLifecycle.PostUpdate(frame.Macro,[]);
            if((_slotTraversal is null)!=(montage is null))throw new InvalidOperationException("Default Main requires its physical Montage bank.");
            if(montage is not null)
            {
                _slotTraversal!.Begin(montage,new(frame.Identity,1,delta),false,initialize,(float)frame.Macro.Tail.UpperbodyWeight);
                _slotTraversal.CompleteUnvisited(frame.Identity);_montageIdentity=frame.Identity;
            }
            _nextGroups=new AlsAssetSyncBatchGroupHistory[3];_nextPlayers=[];_nextSamples=[];_nextTicks=[];
            if(!AlsSyncRuntime.TryEvaluateAssetSyncBatch([0,1,2],[],[],[],_resources.Sequences,_resources.Markers,
                _groups,_players,_samples,delta,_nextGroups,_nextPlayers,_nextSamples,out var failure,_nextTicks))
                throw new InvalidOperationException("Default Main empty common Sync failed: "+failure);
            _nextExists=_groupExists[_writeIndex].ToArray();
            return frame;
        }
        catch{Cancel();throw;}
    }
    internal AlsFrameIdentity PreparedIdentity(LyraMainDefaultRootFrame frame)
    {
        if(!ReferenceEquals(frame,_defaultPending)||_defaultHost is null)throw new InvalidOperationException("Stale default Main source frame.");
        _defaultHost.ValidateFrame(frame);return frame.Identity;
    }
    internal LyraLayerPoseInput EvaluateDefault(LyraMainDefaultRootFrame frame)
    {PreparedIdentity(frame);return _defaultHost!.Evaluate(frame);}
    internal void StageDefaultFeedback(LyraMainDefaultRootFrame frame,ReadOnlySpan<LyraCurveSample> curves)
    {
        PreparedIdentity(frame);
        if(_defaultFeedback is not null||curves.Length!=_resources.Bank.Curves.Names.Length)
            throw new InvalidOperationException("Invalid default Main feedback.");
        var r=curves[_resources.Bank.Curves.Index("RemainingTurnYaw")];var w=curves[_resources.Bank.Curves.Index("TurnYawWeight")];
        var remaining=r.Present?r.Value:0;var weight=w.Present?w.Value:0;
        if(!float.IsFinite(remaining)||!float.IsFinite(weight))throw new ArgumentException("Nonfinite default Main feedback.");
        _defaultFeedback=(remaining,weight);
    }
    internal void ValidateDefaultCommit(LyraMainDefaultRootFrame frame,bool updateOnly)
    {
        PreparedIdentity(frame);_defaultHost!.ValidateCommit(frame,updateOnly);CacheLifecycle.Validate(frame.Macro);
        if(ProxyTraversal.HasPending)ProxyTraversal.Validate(frame.Macro);
        if(updateOnly?_defaultFeedback is not null:_defaultFeedback is null)throw new InvalidOperationException("Incomplete default Main feedback boundary.");
        if(_montageIdentity is {} identity)_slotTraversal!.ValidateCommit(identity);
    }
    internal void CommitDefault(LyraMainDefaultRootFrame frame,bool updateOnly)
    {
        ValidateDefaultCommit(frame,updateOnly);_defaultHost!.Commit(frame,updateOnly);
        if(_montageIdentity is {} identity)_slotTraversal!.Commit(identity);
        MainState.CommitFeedback(MainState.TurnYaw,_defaultFeedback);
        _groups=_nextGroups!;_players=_nextPlayers!;_samples=_nextSamples!;
        _nextExists!.CopyTo(_groupExists[_writeIndex],0);_writeIndex=1-_writeIndex;
        _movementBinding=_nextMovementBinding;
        if(frame.Visited)_pendingBlendOut.Remove(LyraLayerHook.FullBody_SkeletalControls);
        CacheLifecycle.Commit(frame.Macro);
        if(ProxyTraversal.HasPending)ProxyTraversal.Commit(frame.Macro);
        _defaultPending=null;_defaultFeedback=null;ClearPending();
    }
    private void Validate(LyraMainLocomotionCandidate c)
    {if(!ReferenceEquals(c,_pending) || _failed)throw new InvalidOperationException("Stale or failed Main locomotion candidate.");}
    public ReadOnlySpan<AlsAssetPlayerTickContext> PreparedTicks(LyraMainLocomotionCandidate c){Validate(c);return _nextTicks;}
    public ReadOnlySpan<AlsAssetPlayerHistory> PreparedPlayers(LyraMainLocomotionCandidate c){Validate(c);return _nextPlayers;}
    public ReadOnlySpan<AlsAssetSampleHistory> PreparedSamples(LyraMainLocomotionCandidate c){Validate(c);return _nextSamples;}
    public AlsFrameIdentity PreparedIdentity(LyraMainLocomotionCandidate c){Validate(c);return new(c.Macro.Observation.Frame,_characterId,_slotGeneration);}
    internal ImmutableArray<LyraNotifyPlayerContext> PreparedNotifyContexts(LyraMainLocomotionCandidate c,LyraNotifyCatalog catalog)
    {
        Validate(c);if(!ReferenceEquals(catalog,_resources.Notifies))throw new InvalidOperationException("Foreign source notify catalog.");
        var contexts=new Dictionary<int,LyraNotifyPlayerContext>();
        void Linked(ReadOnlySpan<AlsAssetSyncPlayer> players,bool active,int mainState=-1)
        {
            foreach(var p in players)
            {
                if(!_graphSet.TrySource(p,out _,out var node))throw new InvalidOperationException("Foreign Linked notification source.");
                contexts.Add(p.PlayerId,new(p.PlayerId,LyraNotifySourceOwner.Linked,node,p.Epoch,active,false,AlsBlendSpaceNotifyMode.AllAnimations,
                    mainState<0?default:new(LyraNotifyDispatchPolicy.MainMachine,mainState,true)));
            }
        }
        var s=c.Sources;
        Linked(s.Ground.Pivot!.Sources.Players,s.Visits[0].Active,4);
        Linked(s.Ground.Cycle.Sources.Players,s.Visits[1].Active,2);
        Linked(s.Ground.Start.Sources.Players,s.Visits[2].Active,1);
        Linked(s.Ground.Stop!.Sources.Players,s.Visits[3].Active,3);
        foreach(var p in s.Idle.Players)
        {
            var node=p.PlayerId-Layer(LyraLayerHook.FullBody_IdleState).PlayerBase;var occurrence=Array.IndexOf(new[]{17,19,24,26,28},node);
            if(occurrence<0)throw new InvalidOperationException("Foreign nested Idle notify source.");
            var update=s.Idle.Updates.Single(u=>u.Machine==(occurrence<2?1:0)&&u.State==(occurrence<2?occurrence:occurrence-1));
            Linked([p],update.Active,0);
        }
        for(var n=0;n<5;n++)Linked(s.Air[n].Players,s.Visits[n+5].Active,new[]{6,10,7,11,8}[n]);
        foreach(var p in s.Ground.LeanInputs.Players)
        {
            var n=p.PlayerId-Layers.LeanPlayerBase;
            if(n is <0 or >2)throw new InvalidOperationException("Foreign Main Lean notify occurrence.");
            var node=new[]{12,16,22}[n];var active=s.Visits[new[]{2,1,0}[n]].Active;
            contexts.Add(p.PlayerId,new(p.PlayerId,LyraNotifySourceOwner.Main,node,p.Epoch,active,false,catalog.Mode(_mainLeanNotifyAsset),
                new(LyraNotifyDispatchPolicy.MainMachine,new[]{1,2,4}[n],true)));
        }
        if(c.Aiming is {} aim)
        {
            foreach(var p in s.Players.Where(p=>p.Kind==AlsAssetSyncKind.BlendSpace&&p.PlayerId>=Layer(LyraLayerHook.FullBody_Aiming).PlayerBase&&p.PlayerId-Layer(LyraLayerHook.FullBody_Aiming).PlayerBase is 74 or 79))
            {
                var node=p.PlayerId-Layer(LyraLayerHook.FullBody_Aiming).PlayerBase;var path=node==79?_notifyAiming.RelaxedAsset:_notifyAiming.IdleAsset;
                contexts.Add(p.PlayerId,new(p.PlayerId,LyraNotifySourceOwner.Linked,node,p.Epoch,aim.Visit.Active,false,catalog.Mode(path)));
            }
        }
        if(c.Additives is {} additive)foreach(var p in s.Players.Where(p=>p.PlayerId==Layer(LyraLayerHook.FullBodyAdditives).PlayerBase+5))
            Linked([p],additive.Updates.Single(u=>u.State==2).Active);
        if(contexts.Count!=s.Players.Length)throw new InvalidOperationException("Missing source notification traversal context.");
        return s.Players.Select(p=>contexts[p.PlayerId]).ToImmutableArray();
    }
    internal void EnterLinkedEvaluation(LyraMainLocomotionCandidate c,LyraLayerHook hook,AlsGraphTraversalCounter counter)
    {
        Validate(c);
        if(!ProxyTraversal.MainEvaluationEntered(c.Macro)||!counter.HasUpdated||!ProxyTraversal.Prepared(c.Macro).Evaluation.MatchesAll(counter))
            throw new InvalidOperationException("Linked evaluation requires the actual Main root counter.");
        var instance=Layer(hook);instance.Call(hook);
        instance.ProxyTraversal.Synchronize(c.Macro,AlsAnimationProxyPhase.Evaluation,ProxyTraversal.Prepared(c.Macro));
    }
    public void Evaluate(LyraMainLocomotionCandidate c,AlsGraphTraversalCounter? evaluationCounter=null)
    {
        Validate(c);_evaluated=false;
        try
        {
            void StatePose(int state,Span<AlsPrecisePose> pose,Span<AlsInertialCurve> curves)
            {
                var root=RootForState(state);var layer=Layer(LyraItemLayerGraphInstance.HookForRoot(root));var call=layer.Call(LyraItemLayerGraphInstance.HookForRoot(root));
                if(evaluationCounter is {} counter)EnterLinkedEvaluation(c,call.Hook,counter);
                layer.Evaluate(c.Sources,call,_nextPlayers!,_nextSamples!);
                var output=layer.MainRootOutput(c.Sources,call);
                output.Pose.CopyTo(pose);var values=output.Curves;
                for(var n=0;n<curves.Length;n++)curves[n]=values[n].ToInertial();
            }
            Machine.EvaluateMachine(c.Machine,StatePose,_pose,_poseCurves);
            void Copy(int state)
            {
                var root=RootForState(state);var layer=Layer(LyraItemLayerGraphInstance.HookForRoot(root));var output=layer.MainRootOutput(c.Sources,layer.Call(LyraItemLayerGraphInstance.HookForRoot(root)));
                output.Curves.CopyTo(_curves);output.Attributes.CopyTo(_attributes);_root=output.RootMotion;
            }
            var stack=Machine.PreparedStack(c.Machine);
            if(stack.Count==0)Copy(stack.CurrentState);
            else for(var n=0;n<stack.Count;n++)
            {
                var entry=stack.GetTransition(n);if(n==0)Copy(entry.From);var root=RootForState(entry.To);
                var layer=Layer(LyraItemLayerGraphInstance.HookForRoot(root));var output=layer.MainRootOutput(c.Sources,layer.Call(LyraItemLayerGraphInstance.HookForRoot(root)));
                var curves=output.Curves;var attributes=output.Attributes;
                for(var k=0;k<_curves.Length;k++)
                    _curves[k]=LyraCurveSample.BlendStateMachine(_curves[k],curves[k],entry.Alpha);
                for(var k=0;k<_attributes.Length;k++)_attributes[k]=LyraLayeredDataBlend.BlendIntegerUniform(_attributes[k],attributes[k],entry.Alpha,false);
                _root=LyraRootMotionAttribute.BlendUniform(_root,output.RootMotion,entry.Alpha,_rootOverride);
            }
            if(!_curves.Select(v=>v.ToInertial()).SequenceEqual(_poseCurves))throw new InvalidOperationException("Main curve blend disagrees with pose stack.");
            _evaluated=true;
        }
        catch{_failed=true;throw;}
    }
    private void Output(){if(_pending is null || _failed || !_evaluated)throw new InvalidOperationException("Main locomotion has no current pose.");}
    public ReadOnlySpan<AlsPrecisePose> Pose {get{Output();return _pose;}}
    public ReadOnlySpan<LyraCurveSample> Curves {get{Output();return _curves;}}
    public ReadOnlySpan<LyraAttributeSample> Attributes {get{Output();return _attributes;}}
    public LyraRootMotionAttribute RootMotion {get{Output();return _root;}}
    public void StageFinalFeedback(LyraMainLocomotionCandidate c,ReadOnlySpan<LyraCurveSample> finalCurves,
        ReadOnlySpan<LyraNamedCurveSample> controlCurves=default,bool enclosingSlotPoseEvaluated=false)
    {
        Validate(c);
        if(c.Machine.Visited)Output();
        else if(!enclosingSlotPoseEvaluated||_montageIdentity is null||!_slotTraversal!.Visits.Any(v=>v.Kind=="slot"&&v.Id==4))
            throw new InvalidOperationException("Hidden locomotion needs a current enclosing Slot pose.");
        if(enclosingSlotPoseEvaluated&&_montageIdentity is null)throw new InvalidOperationException("No enclosing Slot bank is bound.");
        if(_feedback)throw new InvalidOperationException("Main final feedback is already staged.");
        Sources.StageMainFeedback(c.Sources,finalCurves,c.EvaluationRoots,copyLinkedCurves:true,enclosingSlotPoseEvaluated:enclosingSlotPoseEvaluated);
        _graphSet.StageFeedback(c.Sources,finalCurves,controlCurves);_feedback=true;
    }
    public void ValidateCommit(LyraMainLocomotionCandidate c,bool updateOnly=false)
    {
        Validate(c);Machine.Validate(c.Machine);CacheLifecycle.Validate(c.Macro);
        if(ProxyTraversal.HasPending)
        {
            ProxyTraversal.Validate(c.Macro);
            foreach(var instance in LayerInstances){instance.ProxyTraversal.Validate(c.Macro);instance.Phases.Validate(c.Macro);}
        }
        foreach(var instance in LayerInstances)instance.CacheLifecycle.Validate(c.Macro);
        var enclosingVisited=_montageIdentity is not null&&_slotTraversal!.Visits.Any(v=>v.Kind=="slot"&&v.Id==4);
        if(updateOnly && _feedback || !updateOnly && (c.Machine.Visited||enclosingVisited) && (c.Machine.Visited&&!_evaluated || !_feedback))
            throw new InvalidOperationException("Main commit needs the correct evaluated feedback boundary.");
        Sources.ValidateCommit(c.Sources,_nextPlayers!,_nextSamples!,updateOnly,c.EvaluationRoots);
        _graphSet.ValidateCommit(c.Sources,updateOnly,_feedback);
        if(_montageIdentity is {} identity)_slotTraversal!.ValidateCommit(identity);
    }
    public void Commit(LyraMainLocomotionCandidate c,bool updateOnly=false)
    {
        ValidateCommit(c,updateOnly);Sources.Commit(c.Sources,_nextPlayers!,_nextSamples!,updateOnly,c.EvaluationRoots);
        _graphSet.Commit(c.Sources,updateOnly,_feedback);Machine.Commit(c.Machine);
        if(_montageIdentity is {} identity)_slotTraversal!.Commit(identity);
        _nextWeights!.CopyTo(_cachedWeights,0);_groups=_nextGroups!;_players=_nextPlayers!;_samples=_nextSamples!;
        _nextExists!.CopyTo(_groupExists[_writeIndex],0);_writeIndex=1-_writeIndex;
        foreach(var hook in _blendOutConsumed)_pendingBlendOut.Remove(hook);
        _movementBinding=_nextMovementBinding;
        if(c.LinkedObservationVisited){_observedLinkedEpoch=Layers.Epoch;_linkedLayerChanged=c.Rules.LinkedLayerChanged;}
        CacheLifecycle.Commit(c.Macro);
        foreach(var instance in LayerInstances)instance.CacheLifecycle.Commit(c.Macro);
        if(ProxyTraversal.HasPending)
        {
            foreach(var instance in LayerInstances){instance.Phases.Commit(c.Macro);instance.ProxyTraversal.Commit(c.Macro);}
            ProxyTraversal.Commit(c.Macro);
        }
        ClearPending();
        _initializeLinked=false;
    }
    internal void RetireLayers()=>_graphSet.Retire();
    internal void DispatchLinkedMontageEvents(long frame)
    {
        if(_pending is not null||_defaultPending is not null||MainState.Update.HasPending)throw new InvalidOperationException("Cannot dispatch a pending animation frame.");
        // SkeletalMeshComponent copies its linked-instance list before dispatch.
        foreach(var instance in LayerInstances.ToArray())instance.DispatchQueuedMontageEvents(this,frame);
    }
    private void ClearPending()
    {_pending=null;_nextGroups=null;_nextPlayers=null;_nextSamples=null;_nextTicks=null;_nextWeights=null;_nextExists=null;_montageIdentity=null;_failed=_evaluated=_feedback=false;_blendOutConsumed.Clear();}
    public void Cancel(){ProxyTraversal.Cancel();CacheLifecycle.Cancel();foreach(var instance in LayerInstances){instance.Phases.Cancel();instance.ProxyTraversal.Cancel();instance.CacheLifecycle.Cancel();}_slotTraversal?.Cancel();_defaultHost?.Cancel();_defaultPending=null;_defaultFeedback=null;_graphSet.Cancel();Machine.Cancel();ClearPending();}
}
