using System.Collections.Immutable;
using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Animation;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraMainSelfGraphPhaseResult(ImmutableArray<string> Initialize,
    ImmutableArray<string> CacheBones,ImmutableArray<string> RepeatedCacheBones,
    ImmutableArray<string> LinkedSubgraphs=default);

// Main owns persistent phase histories across binding replacements. Original
// compiled edges also traverse inputs pruned by self roots in Update/Evaluate.
// Provider histories belong to the real Linked instances, never shadow owners.
internal sealed class LyraMainSelfGraphPhases
{
    private readonly Dictionary<int,JsonElement> _nodes;
    private LyraLinkedLayerCallRoutes _calls;
    private readonly LyraLocomotionMachineHost _machine;
    private readonly int _root;
    private readonly uint _character,_generation;
    private readonly Action<int> _initializeNode,_cacheNode;
    private readonly AlsPoseCacheLifecycle _cacheLifecycle;
    private readonly List<string> _trace=[];
    private readonly HashSet<int> _path=[];
    private bool _initialize;
    private long _serial;
    private readonly AlsAnimationGraphStartup _startup=new();
    private AlsGraphTraversalCounter _counter;
    private readonly LyraCompiledMachine _definition=LyraRuntimeGraphCatalog.Load().Locomotion;
    internal AlsGraphTraversalCounter InitializationCounter=>_startup.InitializationCounter;
    internal AlsGraphTraversalCounter CachedBonesCounter=>_startup.CachedBonesCounter;
    internal bool Initialized=>_startup.Initialized;
    internal event Action<bool,AlsGraphTraversalCounter>? RootEntered;

    internal LyraMainSelfGraphPhases(LyraMainLayerGraphCatalog graphs,LyraLinkedLayerCallRoutes calls,
        LyraLocomotionMachineHost machine,int curveCount,uint character,uint generation,
        Action<int> initializeNode,Action<int> cacheNode,AlsPoseCacheLifecycle? cacheLifecycle=null)
    {
        _calls=calls;_machine=machine;_character=character;_generation=generation;
        _initializeNode=initializeNode;_cacheNode=cacheNode;
        var graph=graphs.MainGraph;_root=graph.GetProperty("root").GetInt32();
        _nodes=graph.GetProperty("nodes").EnumerateArray().ToDictionary(n=>n.GetProperty("index").GetInt32());
        if(_root!=85||_nodes.Count!=49)
            throw new NotSupportedException("Graph phases require the original Main topology.");
        _cacheLifecycle=cacheLifecycle??new(78,83);
    }
    internal LyraMainSelfGraphPhaseResult Run(ulong externalFrame=0)
    {
        ImmutableArray<string> initialization=[];
        _startup.InitializeRoot(externalFrame,()=>initialization=Phase(true));
        // UpdateAnimation_WithRoot initializes Main first, then every linked
        // function in property order, while Main's bones counter is still -1.
        var linked=InitializeLinkedRoots(_calls);
        var bones=CacheInvalidatedBones(externalFrame);
        return new(initialization,bones,[],linked);
    }
    internal void ReplaceRoutes(LyraLinkedLayerCallRoutes calls)=>_calls=calls;
    internal ImmutableArray<string> CacheBones(AlsGraphTraversalCounter counter)
    {ImmutableArray<string> trace=[];_startup.CacheBonesSubgraph(counter,()=>trace=Phase(false));return trace;}
    internal void InvalidateBones()=>_startup.BoneCache.Invalidate();
    internal ImmutableArray<string> CacheInvalidatedBones(ulong externalFrame)
    {
        ImmutableArray<string> trace=[];
        _startup.CacheInvalidatedBones(externalFrame,()=>trace=Phase(false));return trace;
    }
    internal ImmutableArray<string> InitializeLinkedRoots(LyraLinkedLayerCallRoutes calls)
    {
        var trace=new List<string>();
        foreach(var call in calls.PhaseCalls)
        {
            calls.InitializeSubGraph(call,(owner,hook)=>owner.Phases.Initialize(hook,InitializationCounter,trace.Add));
            calls.CacheBonesSubGraph(call,(owner,hook)=>owner.Phases.CacheBones(hook,CachedBonesCounter,trace.Add));
        }
        return trace.ToImmutableArray();
    }
    private ImmutableArray<string> Phase(bool initialize)
    {
        _serial=checked(_serial+1);
        _initialize=initialize;_counter=initialize?InitializationCounter:CachedBonesCounter;_trace.Clear();_path.Clear();
        RootEntered?.Invoke(initialize,_counter);Visit(_root);
        return _trace.ToImmutableArray();
    }
    private void Visit(int id)
    {
        if(!_path.Add(id))throw new InvalidOperationException("Recursive Main initialization input.");
        try
        {
            var node=_nodes[id];var type=node.GetProperty("type").GetString()!.Split('.').Last();
            _trace.Add("node:"+id);
            if(_initialize)_initializeNode(id);else _cacheNode(id);
            var links=node.GetProperty("links").EnumerateArray().ToArray();
            if(type=="AnimNode_UseCachedPose")
            {
                _trace.Add("node:"+links.Single().GetProperty("index").GetInt32());
                var cache=links.Single().GetProperty("index").GetInt32();
                if(_initialize)_cacheLifecycle.Initialize(cache,_counter,()=>CachedSource(cache));
                else _cacheLifecycle.CacheBones(cache,_counter,()=>CachedSource(cache));
            }
            else if(type=="AnimNode_StateMachine")
            {
                if(_initialize)_startup.BoneCache.ResetMachine(id);
                foreach(var state in _definition.States.Select((s,i)=>(Name:s.Name,Index:i)))
                {
                    if(_machine.Weight(state.Index)<=0)continue;
                    if(!_initialize&&!_startup.BoneCache.CacheState(id,state.Index,_counter))continue;
                    Visit(links.Single(l=>l.GetProperty("pin").GetString()=="State["+state.Name+"]").GetProperty("index").GetInt32());
                }
            }
            else if(type=="AnimNode_LinkedAnimLayer")
            {
                var hook=Enum.Parse<LyraLayerHook>(node.GetProperty("settings").GetProperty("layer").GetString()!);
                var call=_calls.Call(hook);
                if(call.Target.Kind==GodotAls.Core.Animation.AlsLinkedLayerTargetKind.Self)_trace.Add("self:"+hook);
                Action[] inputs=links.Select<JsonElement,Action>(l=>()=>Visit(l.GetProperty("index").GetInt32())).ToArray();
                void External(LyraItemLayerGraphInstance owner,LyraLayerHook root)
                {if(_initialize)owner.Phases.Initialize(root,_counter,_trace.Add);else owner.Phases.CacheBones(root,_counter,_trace.Add);}
                if(_initialize)_calls.Initialize(call,inputs,External);else _calls.CacheBones(call,inputs,External);
            }
            else if(type is "AnimNode_Root" or "AnimNode_StateResult" or "AnimNode_ControlRig" or "AnimNode_RotateRootBone" or
                "AnimNode_Inertialization" or "AnimNode_Slot" or "AnimNode_ApplyAdditive" or "AnimNode_LayeredBoneBlend" or "AnimNode_RefPose" or "AnimNode_BlendSpacePlayer")
            {foreach(var link in links)Visit(link.GetProperty("index").GetInt32());}
            else throw new NotSupportedException("Unsupported initial Main phase node: "+type);
        }
        finally{_path.Remove(id);}
    }
    private void CachedSource(int id)
    {
        if(_initialize)_initializeNode(id);else _cacheNode(id);
        Visit(_nodes[id].GetProperty("links")[0].GetProperty("index").GetInt32());
    }
}
