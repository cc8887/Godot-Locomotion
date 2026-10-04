using System.Text.Json;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraLocomotionHosts(LyraMainGroundHosts Ground,LyraIdleLayerHost Idle,LyraAirLayerPoseHost[] Air);

// Resource definitions are shared. Every Create allocates the provider's ten
// mutable root histories in one group lifetime, on the same sequence IDs.
internal sealed class LyraLocomotionResources : IDisposable
{
    public LyraLocomotionResourceCatalog Catalog {get;}
    public LyraMainLayerGraphCatalog LayerGraphs {get;}=LyraMainLayerGraphCatalog.Load();
    private readonly LyraSourceNodeCatalog _sources=LyraSourceNodeCatalog.Load();
    private readonly LyraRuntimeGraphCatalog _runtime=LyraRuntimeGraphCatalog.Load();
    private readonly LyraLocomotionLayerInventory _inventory;
    private readonly LyraMainGroundResources _ground;
    private readonly JsonDocument _distance;
    private readonly LyraStopDistanceBank _distances;
    private LyraMontageMovementReader? _movement;
    private LyraMontageCatalog? _movementCatalog;
    private LyraMotionWarpingProfile? _warping;
    private bool _disposed;
    internal LyraMontageMovementReader Movement(LyraMontageCatalog catalog)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(_movementCatalog is not null&&!_movementCatalog.Paths.SequenceEqual(catalog.Paths))
            throw new InvalidOperationException("Shared root resources require identical Montage bindings.");
        if(_movement is null){_movement=new(Catalog.Bank,catalog);_movementCatalog=catalog;}return _movement;
    }
    internal LyraMotionWarpingProfile MotionWarping(LyraMontageCatalog catalog)
    { _=Movement(catalog);return _warping??=new(catalog); }
    public LyraLocomotionResources(bool includeMontageActions=false)
    {
        Catalog=new(includeMontageActions);
        _inventory=LyraLocomotionLayerInventory.Load(_sources);_ground=new(shared:Catalog);
        _distance=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/air_runtime_distance.json"));
        _distances=new(_distance.RootElement);
    }
    internal LyraMainGraphStateOwner CreateMainOwner(int leanPlayerBase=1000,long epoch=1)
        =>_ground.CreateMainOwner(leanPlayerBase,epoch);
    public LyraLocomotionHosts Create(string profile,int playerBase=0,int leanPlayerBase=1000,long epoch=1,
        LyraMainGraphStateOwner? mainOwner=null)
    {
        var provider=Catalog.Provider(profile);
        if(provider.GetProperty("class").GetString()!=_inventory.Providers[profile].ClassPath)throw new InvalidOperationException("Changed locomotion provider class.");
        var idle=LyraIdleResources.CreateBound(profile,playerBase,epoch,2,_inventory,_sources,_runtime,
            Catalog.Bank,Catalog.Paths,Catalog.Slots,Catalog.Sequences,Catalog.Masks,Catalog.Roots,provider.GetProperty("idle"),provider.GetProperty("idleBreaks"));
        var air=LyraAirResources.CreateBound(profile,playerBase,epoch,0,_sources,_inventory,Catalog.Bank,
            Catalog.Paths,Catalog.Slots,Catalog.Sequences,Catalog.Masks,Catalog.Roots,_distances,provider.GetProperty("air"));
        return new(_ground.Create(profile,playerBase,leanPlayerBase,epoch,mainOwner),idle,air);
    }
    public LyraLocomotionSourceScope CreateScope(string profile,int playerBase=0,int leanPlayerBase=1000,long epoch=1,
        LyraMainGraphStateOwner? mainOwner=null)
        =>new(Catalog,Create(profile,playerBase,leanPlayerBase,epoch,mainOwner),profile);
    public LyraMainLocomotionHost CreateMainHost(string profile,int playerBase=0,int leanPlayerBase=1000,long epoch=1)
        =>new(this,profile,playerBase,leanPlayerBase,epoch);
    public LyraMainPoseHost CreateMainPoseHost(string profile,int playerBase=0,int leanPlayerBase=1000,long epoch=1,bool enableMainInertia=true)
        =>new(this,profile,playerBase,leanPlayerBase,epoch,enableMainInertia:enableMainInertia);
    public LyraItemLayerGraphInstance CreateLayerInstance(string profile,int playerBase=0,int leanPlayerBase=1000,long epoch=1,
        LyraMainGraphStateOwner? mainOwner=null,LyraLinkedLayerClassContract? contract=null)
        =>new(this,profile,playerBase,leanPlayerBase,epoch,mainOwner,contract);
    internal LyraLocomotionSourceScope ComposeScope(IReadOnlyList<LyraItemLayerGraphInstance> roots,
        LyraMainGraphStateOwner mainOwner,LyraLinkedLayerClassContract contract,int leanPlayerBase,long epoch)
    {
        if(roots.Count!=10||roots.Select(r=>r.Profile).Distinct().Count()!=1)throw new ArgumentException("Incomplete routed graph roots.");
        var ground=_ground.Compose(roots[2].Sources.Hosts.Ground.Start,roots[1].Sources.Hosts.Ground.Cycle,
            roots[3].Sources.Hosts.Ground.Stop,roots[0].Sources.Hosts.Ground.Pivot,mainOwner,contract,leanPlayerBase,epoch);
        return new(Catalog,new(ground,roots[4].Sources.Hosts.Idle,
            Enumerable.Range(0,5).Select(n=>roots[n+5].Sources.Hosts.Air[n]).ToArray()),roots[0].Profile,contract);
    }
    public LyraMainLocomotionHost CreateMainHost(LyraItemLayerGraphInstance instance)
        =>new(this,instance.Profile,instance.PlayerBase,instance.LeanPlayerBase,instance.Epoch,instance);
    internal LyraCompiledMachine AdditivesMachine(string profile)
        =>_runtime.ForClass(Catalog.Provider(profile).GetProperty("class").GetString()!).Single(m=>m.Name=="FullBodyAdditve_SM");
    public void Dispose(){if(_disposed)return;_disposed=true;_movement?.Dispose();_ground.Dispose();_distance.Dispose();Catalog.Dispose();}
}
