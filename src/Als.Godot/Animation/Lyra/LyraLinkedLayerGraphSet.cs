using System.Collections.Immutable;
using GodotAls.Core.Animation;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

// Real graph instances are allocated from the ordinary binding result. Main's
// source bridge borrows the selected root hosts; it does not clone their state
// or run their unused sibling functions. Every occurrence joins one Main Sync.
internal sealed class LyraLinkedLayerGraphSet
{
    private readonly LyraLinkedLayerContracts _contracts;
    private readonly ImmutableArray<AlsLinkedLayerTarget> _targets;
    private readonly LyraSourceClass _provider;
    private readonly Dictionary<long,LyraItemLayerGraphInstance> _owners=[];
    internal LyraLinkedLayerCallRoutes Calls {get;}
    internal ImmutableArray<LyraItemLayerGraphInstance> Instances {get;}
    private readonly LyraLocomotionSourceScope? _sources;
    internal LyraLocomotionSourceScope Sources=>_sources??throw new InvalidOperationException("Self layers have no external source scope.");
    internal bool IsSelf=>Instances.IsEmpty;
    internal LyraItemLayerGraphInstance First=>!IsSelf?Instances[0]:throw new InvalidOperationException("Self layers have no Linked instance.");
    internal LyraLinkedLayerClassContract Contract=>First.Contract;

    internal LyraLinkedLayerGraphSet(LyraLocomotionResources resources,LyraLinkedLayerContracts contracts,
        ImmutableArray<AlsLinkedLayerTarget> targets,string profile,int playerBase,int leanPlayerBase,long epoch,
        LyraMainGraphStateOwner mainOwner,object character,LyraItemLayerGraphInstance? supplied=null)
    {
        _contracts=contracts;_targets=targets;
        var classPath=resources.LayerGraphs.ClassPath(profile);
        var contract=contracts.Get(classPath);
        if(targets.Length==contracts.CallSites.Count&&targets.All(t=>t.Kind==AlsLinkedLayerTargetKind.Self))
        {
            if(supplied is not null)throw new ArgumentException("Self targets cannot own an external graph.");
            _provider=LyraSourceNodeCatalog.Load().ForClass(classPath);
            Instances=[];Calls=new(resources.LayerGraphs,contracts,targets,mainOwner,_owners);return;
        }
        if(targets.Length!=contracts.CallSites.Count||targets.Length!=14||targets.Any(t=>
            t.Kind!=AlsLinkedLayerTargetKind.External||t.Class!=classPath||t.Instance<=0))
            throw new NotSupportedException("Graph execution needs complete bindings to a supported provider.");
        var definitions=LyraSourceNodeCatalog.Load().ForClass(classPath);
        _provider=definitions;
        if(definitions.Nodes.Keys.Any(n=>n is <0 or >=256))throw new NotSupportedException("Linked node identity range exceeded.");
        var allocated=new List<LyraItemLayerGraphInstance>();
        try
        {
            for(var n=0;n<targets.Length;n++)
            {
                var call=contracts.CallSites[n];var target=targets[n];var signature=contract.Functions[call.Hook];
                var group=string.Equals(signature.Group,"None",StringComparison.OrdinalIgnoreCase)?"":signature.Group;
                if(!string.Equals(group,target.Group,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Compiled group and actual target differ.");
                if(!_owners.TryGetValue(target.Instance,out var instance))
                {
                    if(supplied is not null&&(allocated.Count!=0||targets.Any(t=>t.Instance!=target.Instance)))
                        throw new ArgumentException("A supplied graph cannot cover several instances.");
                    instance=supplied??resources.CreateLayerInstance(profile,checked(playerBase+allocated.Count*256),
                        leanPlayerBase,epoch,mainOwner,contract);
                    if(!ReferenceEquals(instance.Sources.MainOwner,mainOwner))throw new InvalidOperationException("Foreign Main graph owner.");
                    allocated.Add(instance);_owners.Add(target.Instance,instance);
                }
            }
            Instances=allocated.ToImmutableArray();
            Calls=new(resources.LayerGraphs,contracts,targets,mainOwner,_owners);
            var roots=Enumerable.Range(0,10).Select(n=>Layer(LyraItemLayerGraphInstance.HookForRoot(n))).ToArray();
            _sources=Instances.Length==1?First.Sources:resources.ComposeScope(roots,mainOwner,contract,leanPlayerBase,epoch);
            foreach(var instance in Instances)
                instance.BindMain(character,resources.Catalog,profile,instance.PlayerBase,leanPlayerBase,epoch,Sources,
                    contracts.CallSites.Where(c=>ReferenceEquals(Layer(c.Hook),instance)).Select(c=>c.Hook).ToArray());
        }
        catch
        {
            foreach(var instance in allocated)if(!ReferenceEquals(instance,supplied))instance.Retire();
            throw;
        }
    }

    internal LyraItemLayerGraphInstance Layer(LyraLayerHook hook)=>Calls.External(Calls.Call(hook));
    internal LyraItemLayerGraphInstance ByClass(string classPath)
    {
        // UE's lookup walks Main's linked node property order, not graph-root
        // execution order. This catalog keeps that original property order.
        foreach(var call in _contracts.CallSites)
        {var instance=Layer(call.Hook);if(instance.ClassPath==classPath)return instance;}
        throw new InvalidOperationException("No linked instance of the requested class.");
    }
    internal void ValidateTargets(ImmutableArray<AlsLinkedLayerTarget> targets)
    {
        if(!targets.SequenceEqual(_targets))throw new InvalidOperationException("Graph instance binding plan is stale.");
    }
    internal bool TrySource(in AlsAssetSyncPlayer source,out LyraItemLayerGraphInstance? instance,out int node)
    {
        foreach(var owner in Instances)
        {
            node=source.PlayerId-owner.PlayerBase;
            if(node is <0 or >=256)continue;
            if(!_provider.Nodes.ContainsKey(node))continue;
            if(source.Epoch!=owner.Epoch)throw new InvalidOperationException("Foreign linked source epoch.");
            instance=owner;return true;
        }
        instance=null;node=-1;return false;
    }
    internal void StageFeedback(LyraLocomotionScopeCandidate frame,ReadOnlySpan<LyraCurveSample> curves,
        ReadOnlySpan<LyraNamedCurveSample> controls)
    {foreach(var instance in Instances)instance.StageEnclosingFeedback(frame,curves,controls);}
    internal void ValidateCommit(LyraLocomotionScopeCandidate frame,bool updateOnly,bool feedback)
    {foreach(var instance in Instances)instance.ValidateFrameCommit(frame,updateOnly,feedback);}
    internal void Commit(LyraLocomotionScopeCandidate frame,bool updateOnly,bool feedback)
    {foreach(var instance in Instances)instance.CommitFrame(frame,updateOnly,feedback);}
    internal void Cancel()
    {_sources?.Cancel();foreach(var instance in Instances)instance.CancelFrame();}
    internal void Retire()
    {Cancel();Calls.Retire();_sources?.Hosts.Ground.Scope.ReleaseMain();foreach(var instance in Instances)instance.Retire();}
}
