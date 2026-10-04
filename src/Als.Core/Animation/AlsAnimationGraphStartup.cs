using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Animation;

// Graph startup is independent of any character, asset format or scene host.
public sealed class AlsAnimationGraphStartup
{
    public AlsGraphTraversalCounter InitializationCounter {get;private set;}
    public AlsGraphTraversalCounter CachedBonesCounter {get;private set;}
    public bool Initialized {get;private set;}
    public AlsAnimationBoneCacheGate BoneCache {get;}=new();
    public void InitializeRoot(ulong frame,Action initialize)
    {
        ArgumentNullException.ThrowIfNull(initialize);
        if(Initialized||frame==ulong.MaxValue)throw new InvalidOperationException("Invalid or repeated graph startup.");
        InitializationCounter=InitializationCounter.Next(frame);initialize();Initialized=true;
    }
    public bool CacheInvalidatedBones(ulong frame,Action cache)
    {
        if(!Initialized||frame==ulong.MaxValue)throw new InvalidOperationException("Invalid graph bone-cache entry.");
        return BoneCache.CacheRoot(()=>{CachedBonesCounter=CachedBonesCounter.Next(frame);cache();});
    }
    public void CacheBonesSubgraph(AlsGraphTraversalCounter counter,Action cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        if(!Initialized||!counter.HasUpdated)throw new InvalidOperationException("Subgraph needs its initialized owner and caller context.");
        CachedBonesCounter=counter;cache();
    }
}
