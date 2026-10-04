using System.Collections.Immutable;
using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// Owner-local compiled indices are mapped only inside the deferred traversal.
// Provider FullBody_Aiming drains before the enclosing Main cache queue.
internal readonly record struct LyraMainCacheSourceWeights(float PreAim=1,float Upper=1,
    bool PreAimInactive=false,bool UpperInactive=false)
{
    public static LyraMainCacheSourceWeights InactiveSlots=>new(1,1);
    public void Validate()
    {if(!float.IsFinite(PreAim)||!float.IsFinite(Upper)||PreAim is <0 or >1||Upper is <0 or >1)throw new ArgumentException("Invalid Main Slot source weights.");}
}
internal readonly record struct LyraMainCacheUpdate(int Cache,AlsPoseUpdateContext Context);
internal readonly record struct LyraMainCacheSkipped(int Cache,int Handler,ImmutableArray<AlsPoseUpdateContext> Contexts);
internal sealed record LyraMainCacheResult(ImmutableArray<LyraMainCacheUpdate> Updates,ImmutableArray<LyraMainCacheSkipped> Skipped,int Reads)
{
    public AlsPoseUpdateContext? Locomotion=>Updates.FirstOrDefault(u=>u.Cache==83) is {Cache:83} u?u.Context:null;
}

// Candidate-local update traversal only. The enclosing Main owns clocks,
// initialization and pose evaluation. Slot weights must come from its bank.
internal sealed class LyraMainPoseCacheTraversal:IAlsPoseCacheUpdateSink
{
    public const int ProviderOffset=103;
    private readonly AlsPoseCacheTraversal _traversal;
    private readonly List<LyraMainCacheUpdate> _updates=[];
    private readonly List<LyraMainCacheSkipped> _skipped=[];
    private LyraMainCacheSourceWeights _source;
    private LyraMainSlotsTraversal? _slots;
    private bool _resolving;
    private readonly Action<int,float>? _recordWeight;
    public LyraMainPoseCacheTraversal(LyraMainLayerGraphCatalog graphs,string profile,Action<int,float>? recordWeight=null)
    {
        _recordWeight=recordWeight;
        const string root="res://assets/generated/lyra_als/";
        using var document=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"main_cache_v1_policy.json"));
        var policy=document.RootElement;
        if(policy.GetProperty("schemaVersion").GetInt32()!=1||policy.GetProperty("stage").GetString()!="OriginalMainCacheUpdate"||
            policy.GetProperty("providerOffset").GetInt32()!=ProviderOffset||policy.GetProperty("mainRootMask").GetSingle()!=0)
            throw new InvalidOperationException("Changed Main pose cache policy.");
        foreach(var d in policy.GetProperty("dependencies").EnumerateObject())
            if(d.Value.GetString()!=LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+d.Name)))throw new InvalidOperationException("Stale Main pose cache dependency.");
        var order=policy.GetProperty("orders").GetProperty(profile);
        if(!order.GetProperty("mainOrder").EnumerateArray().Select(v=>v.GetInt32()).SequenceEqual(new[]{78,83})||
            !order.GetProperty("providerOrder").EnumerateArray().Select(v=>v.GetInt32()).SequenceEqual(new[]{78}))
            throw new InvalidOperationException("Changed original cache update queue.");
        void Bind(JsonElement graph,int id,string type,int child,string name)
        {
            var node=graph.GetProperty("nodes").EnumerateArray().Single(n=>n.GetProperty("index").GetInt32()==id);
            if(node.GetProperty("type").GetString()!="/Script/Engine.AnimNode_"+type||
                node.GetProperty("settings").GetProperty("cachePoseName").GetString()!=name||
                node.GetProperty("links").GetArrayLength()!=1||node.GetProperty("links")[0].GetProperty("index").GetInt32()!=child)
                throw new InvalidOperationException("Changed Main/provider pose cache binding.");
        }
        var main=graphs.MainGraph;var provider=graphs.Graph(profile,LyraLayerHook.FullBody_Aiming);
        var split=main.GetProperty("nodes").EnumerateArray().Single(n=>n.GetProperty("index").GetInt32()==0).GetProperty("settings");
        if(split.GetProperty("bUpdateBasePoseFirst").GetBoolean()||!split.GetProperty("bBlendRootMotionBasedOnRootBone").GetBoolean()||
            split.GetProperty("blendWeights")[0].GetSingle()!=1||split.GetProperty("perBoneBlendWeights")[0].GetProperty("blendWeight").GetSingle()!=0)
            throw new InvalidOperationException("Changed original Main split update order/root mask.");
        Bind(main,78,"SaveCachedPose",2,"UpperbodyLowerbodySplit");Bind(main,83,"SaveCachedPose",1,"Locomotion");
        Bind(main,77,"UseCachedPose",78,"UpperbodyLowerbodySplit");Bind(main,82,"UseCachedPose",83,"Locomotion");Bind(main,80,"UseCachedPose",83,"Locomotion");
        Bind(provider,78,"SaveCachedPose",80,"BasePose");Bind(provider,76,"UseCachedPose",78,"BasePose");Bind(provider,75,"UseCachedPose",78,"BasePose");
        _traversal=new(new AlsPoseCacheDefinition(221,[181,78,83],[new(179,181),new(178,181),new(77,78),new(82,83),new(80,83)]),8);
    }
    public LyraMainCacheResult Resolve(AlsFrameIdentity identity,ReadOnlySpan<LyraMainCacheUpdate> readers,LyraMainCacheSourceWeights source)
        =>Resolve(identity,readers,source,null);
    public LyraMainCacheResult Resolve(AlsFrameIdentity identity,ReadOnlySpan<LyraMainCacheUpdate> readers,LyraMainSlotsTraversal slots)
        =>Resolve(identity,readers,default,slots);
    private LyraMainCacheResult Resolve(AlsFrameIdentity identity,ReadOnlySpan<LyraMainCacheUpdate> readers,LyraMainCacheSourceWeights source,LyraMainSlotsTraversal? slots)
    {
        source.Validate();if(_resolving)throw new InvalidOperationException("Reentrant Main cache update.");
        slots?.ValidateResolve(identity);
        _resolving=true;_source=source;_slots=slots;_updates.Clear();_skipped.Clear();
        try
        {
            _traversal.Begin(identity);
            foreach(var reader in readers)
            {
                if(reader.Cache is not (75 or 76)||reader.Context.Identity!=identity)throw new InvalidOperationException("Foreign Aiming cache reader.");
                _traversal.Use(ProviderOffset+reader.Cache,reader.Context);
            }
            _traversal.Drain(this);
            return new(_updates.ToImmutableArray(),_skipped.ToImmutableArray(),_traversal.CachedCallCount);
        }
        finally{_resolving=false;_slots=null;}
    }
    public LyraMainCacheResult Resolve(LyraAimingCandidate aiming,AlsFrameIdentity identity,LyraMainCacheSourceWeights source)
    {
        var readers=new List<LyraMainCacheUpdate>();
        for(var n=0;n<2;n++)if(aiming.Active[n])
        {
            var context=new AlsPoseUpdateContext(identity,aiming.Nodes[n].Weight,aiming.Delta).WithInertialization(75,true);
            if(!aiming.Visit.Active)context=context.AsInactive();
            readers.Add(new(n==0?76:75,context));
        }
        return Resolve(identity,readers.ToArray(),source);
    }
    public void UpdateCachedSource(int node,in AlsPoseUpdateContext context)
    {
        _recordWeight?.Invoke(node,context.Weight);
        _updates.Add(new(node,context));
        if(_slots is not null){_slots.UpdateCachedSource(node,context,_traversal);return;}
        if(node==181)_traversal.Use(77,context);
        else if(node==78&&_source.PreAim>AlsPoseBlender.WeightThreshold)
        {
            var source=context.WithWeight(context.Weight*_source.PreAim);
            if(_source.PreAimInactive)source=source.AsInactive();
            // Original Main split visits upper first. Its name-mapped root
            // mask is zero: the selected upper path has root modifier zero.
            if(_source.Upper>AlsPoseBlender.WeightThreshold)
            {
                var upper=source.WithWeight(source.Weight*_source.Upper,0);
                if(_source.UpperInactive)upper=upper.AsInactive();
                _traversal.Use(82,upper);
            }
            _traversal.Use(80,source);
        }
    }
    public void OnCachedUpdatesSkipped(int handler,ReadOnlySpan<AlsPoseUpdateContext> contexts)
    {_skipped.Add(new(_updates[^1].Cache,handler,contexts.ToArray().ToImmutableArray()));}
}
