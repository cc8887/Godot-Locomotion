using System.Collections.Immutable;
using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Animation;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// Phase histories belong to the actual Linked instance, including unvisited
// functions. No source Update, Sync, Evaluate, callback or pose publication is
// performed here. Bone mappings use the existing immutable ALS81 layout.
internal sealed class LyraProviderGraphPhases
{
    private readonly LyraItemLayerGraphInstance _owner;
    private readonly Dictionary<int,JsonElement> _nodes=[];
    private readonly Dictionary<LyraLayerHook,int> _roots=[];
    private readonly AlsAnimationBoneCacheGate _boneCache=new();
    private readonly HashSet<int> _path=[];
    private AlsGraphTraversalCounter _phaseCounter;
    private object? _cacheFrame;
    private bool _initialize,_running;
    private Action<string>? _trace;
    internal AlsGraphTraversalCounter InitializationCounter=>_owner.ProxyTraversal.Committed.Initialization;
    internal AlsGraphTraversalCounter CachedBonesCounter=>_owner.ProxyTraversal.Committed.CachedBones;
    internal bool BonesInvalidated=>_boneCache.BonesInvalidated;
    internal bool HasPending=>_boneCache.HasPending;
    internal event Action<LyraLayerHook,bool,AlsGraphTraversalCounter>? RootEntered;
    internal event Action<LyraLayerHook,AlsGraphTraversalCounter,object>? UpdateRootBonesEntered;
    internal LyraProviderGraphPhases(LyraMainLayerGraphCatalog graphs,LyraItemLayerGraphInstance owner,int curveCount)
    {
        _owner=owner;
        foreach(var hook in Enum.GetValues<LyraLayerHook>())
        {
            var graph=graphs.Graph(owner.Profile,hook);_roots.Add(hook,graph.GetProperty("root").GetInt32());
            foreach(var node in graph.GetProperty("nodes").EnumerateArray())
            {
                var id=node.GetProperty("index").GetInt32();
                if(_nodes.TryGetValue(id,out var previous)&&previous.GetRawText()!=node.GetRawText())
                    throw new InvalidOperationException("Provider phase closures disagree on a node.");
                _nodes[id]=node;
            }
        }
    }
    internal void Initialize(LyraLayerHook hook,AlsGraphTraversalCounter counter,Action<string> trace)=>Phase(hook,counter,true,trace);
    internal void CacheBones(LyraLayerHook hook,AlsGraphTraversalCounter counter,Action<string> trace)=>Phase(hook,counter,false,trace);
    internal void Begin(object frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if(HasPending||_running||_owner.IsRetired)throw new InvalidOperationException("Invalid Provider phase frame.");
        _owner.ProxyTraversal.Validate(frame);_owner.CacheLifecycle.Validate(frame);
        _boneCache.Begin(frame);
    }
    internal void Validate(object frame)
    {if(_running||_owner.IsRetired)throw new InvalidOperationException("Incomplete Provider phase frame.");_boneCache.Validate(frame);}
    internal bool PreparedBonesInvalidated(object frame)
    {
        if(_owner.IsRetired)throw new InvalidOperationException("Retired Provider phase frame.");
        return _boneCache.PreparedInvalidated(frame);
    }
    internal void InvalidateBones()
    {
        if(HasPending||_running||_owner.ProxyTraversal.HasPending||_owner.CacheLifecycle.Pending||_owner.IsRetired)
            throw new InvalidOperationException("Provider bone invalidation requires its idle live instance.");
        _boneCache.Invalidate();
    }
    internal bool EnterUpdateRoot(LyraLayerHook hook,object frame,Action<string>? trace=null)
    {
        Validate(frame);_owner.Call(hook);
        if(!_boneCache.PreparedInvalidated(frame))return false;
        // Linked Update inherits only Update. CacheBones_WithRoot uses this
        // target's own bone counter, and a function root does not increment it.
        var counter=_owner.ProxyTraversal.Prepared(frame).CachedBones;
        return _boneCache.CacheRoot(frame,()=>Phase(hook,counter,false,trace??(static _=>{}),frame));
    }
    internal void Commit(object frame)
    {
        Validate(frame);_boneCache.Commit(frame);
    }
    internal void Cancel()=>_boneCache.Cancel();
    private void Phase(LyraLayerHook hook,AlsGraphTraversalCounter counter,bool initialize,Action<string> trace,object? frame=null)
    {
        if(_running||_owner.IsRetired)throw new InvalidOperationException("Invalid Linked graph phase context.");
        _owner.Call(hook); // Includes the real bound-function inventory.
        if(frame is null)
        {
            if(HasPending)throw new InvalidOperationException("Subgraph phases require an idle Provider.");
            _owner.ProxyTraversal.SetIdle(initialize?AlsAnimationProxyPhase.Initialization:AlsAnimationProxyPhase.CachedBones,counter);
        }
        else
        {
            _= _boneCache.PreparedInvalidated(frame);_owner.CacheLifecycle.Validate(frame);
        }
        _phaseCounter=counter;
        _cacheFrame=frame;
        _initialize=initialize;_trace=trace;_running=true;_path.Clear();
        // LinkedAnimGraph synchronizes the target Proxy counters with the
        // caller; function-root Initialize does not increment either counter.
        try
        {
            if(frame is null)RootEntered?.Invoke(hook,initialize,counter);
            else UpdateRootBonesEntered?.Invoke(hook,counter,frame);
            Visit(_roots[hook]);
        }
        finally{_running=false;_trace=null;_cacheFrame=null;}
    }
    private void Visit(int id)
    {
        if(!_path.Add(id))throw new InvalidOperationException("Recursive Provider phase graph.");
        try
        {
            var node=_nodes[id];var type=node.GetProperty("type").GetString()!.Split('.').Last();
            _trace!("provider:"+id);
            var control=type is "AnimNode_ModifyBone" or "AnimNode_LegIK" or "AnimNode_FootPlacement" or
                "AnimNode_TwoBoneIK" or "AnimNode_CopyBone" or "AnimNode_HandIKRetargeting";
            if(_initialize&&!control)_owner.InitializePhaseNode(id);else if(!_initialize)_owner.CacheBonesPhaseNode(id);
            var links=node.GetProperty("links").EnumerateArray().ToArray();
            if(type=="AnimNode_UseCachedPose")
            {
                var cache=links.Single().GetProperty("index").GetInt32();_trace("provider:"+cache);
                if(_initialize)_owner.CacheLifecycle.Initialize(cache,_phaseCounter,()=>CachedSource(cache));
                else if(_cacheFrame is {} frame)_owner.CacheLifecycle.CacheBones(frame,cache,_phaseCounter,()=>CachedSource(cache));
                else _owner.CacheLifecycle.CacheBones(cache,_phaseCounter,()=>CachedSource(cache));
            }
            else if(type=="AnimNode_StateMachine")
            {
                var states=_owner.PhaseMachineStates(id).ToArray();
                if(_initialize)
                {
                    _boneCache.ResetMachine(id);
                    var initial=states.Single(s=>s.Weight==1);
                    StateLink(links,initial.Name);
                }
                else foreach(var state in states)
                {
                    if(state.Weight<=0)continue;
                    if(_boneCache.CacheState(id,state.Index,_phaseCounter,_cacheFrame))StateLink(links,state.Name);
                }
            }
            // LinkedInputPose never recursively initializes or caches the
            // caller's input. The enclosing Linked node handles those inputs.
            else if(type=="AnimNode_LinkedInputPose"){}
            else if(type is "AnimNode_Root" or "AnimNode_StateResult" or "AnimNode_LayeredBoneBlend" or
                "AnimNode_RotationOffsetBlendSpace" or "AnimNode_BlendListByBool" or "AnimNode_TwoWayBlend" or
                "AnimNode_RefPose" or "AnimNode_SequencePlayer" or "AnimNode_SequenceEvaluator" or
                "AnimNode_ConvertLocalToComponentSpace" or "AnimNode_ConvertComponentToLocalSpace" or
                "AnimNode_ModifyBone" or "AnimNode_LegIK" or "AnimNode_FootPlacement" or "AnimNode_TwoBoneIK" or
                "AnimNode_CopyBone" or "AnimNode_HandIKRetargeting" or "AnimNode_StrideWarping" or "AnimNode_OrientationWarping")
            {foreach(var link in links)Visit(link.GetProperty("index").GetInt32());}
            else throw new NotSupportedException("Unsupported original Provider phase node: "+type);
            // SkeletalControlBase initializes its ComponentPose before
            // resetting alpha state; derived Foot/Leg initializers follow it.
            if(_initialize&&control)_owner.InitializePhaseNode(id);
        }
        finally{_path.Remove(id);}
    }
    private void StateLink(JsonElement[] links,string state)=>Visit(links.Single(l=>
        l.GetProperty("pin").GetString()=="State["+state+"]").GetProperty("index").GetInt32());
    private void CachedSource(int cache)=>Visit(_nodes[cache].GetProperty("links")[0].GetProperty("index").GetInt32());
}
