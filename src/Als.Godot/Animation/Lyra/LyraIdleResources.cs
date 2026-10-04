using System.Text.Json;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal sealed class LyraIdleResources : IDisposable
{
    private const string Root="res://assets/generated/lyra_als/";
    private readonly JsonDocument _native,_requests,_rootData;
    private readonly LyraSourceNodeCatalog _sources=LyraSourceNodeCatalog.Load();
    private readonly LyraRuntimeGraphCatalog _runtime=LyraRuntimeGraphCatalog.Load();
    private readonly LyraLocomotionLayerInventory _inventory;
    private readonly LyraCompressedRootBank _roots;
    private readonly string[] _paths,_slots;
    private readonly ulong[] _masks;
    public LyraLogicalSourceBank Bank {get;}=LyraLogicalSourceBank.Load(includeLocomotionExtras:true);
    public AlsAssetSyncSequence[] Sequences {get;}
    public AlsAssetSyncMarker[] Markers {get;}
    public JsonElement Native=>_native.RootElement;
    public JsonElement Requests=>_requests.RootElement;
    public string Path(int id)=>id<0?"":_paths[id];
    private static JsonDocument Load(string name)=>JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root+name));
    public LyraIdleResources(bool gates=false)
    {
        var prefix=gates?"idle_runtime_gates_":"idle_runtime_v2_";
        _inventory=LyraLocomotionLayerInventory.Load(_sources);_native=Load(prefix+"native.json");_requests=Load(prefix+"requests.json");_rootData=Load(prefix+"roots.json");
        if(Native.GetProperty("schemaVersion").GetInt32()!=2 || Requests.GetProperty("schemaVersion").GetInt32()!=2)throw new InvalidOperationException("Idle requires native double bit patterns.");
        foreach(var (file,key) in new[]{(prefix+"requests.json","requestSha256"),(prefix+"roots.json","rootSha256")})
            if(Native.GetProperty(key).GetString()!=LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+file)))throw new InvalidOperationException("Stale Idle input: "+file);
        foreach(var dep in Native.GetProperty("dependencies").EnumerateObject())
            if(dep.Value.GetString()!=LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+dep.Name)))throw new InvalidOperationException("Changed Idle dependency: "+dep.Name);
        var rows=Native.GetProperty("assets").EnumerateArray().ToArray();_paths=rows.Select(r=>r.GetProperty("path").GetString()!).ToArray();
        if(!_paths.SequenceEqual(Requests.GetProperty("sequencePaths").EnumerateArray().Select(p=>p.GetString()!)))throw new InvalidOperationException("Changed Idle source address space.");
        _slots=_paths.Select(p=>Bank.Slots.Single(s=>Bank.Get(s).Data.Identity.AssetPath==p)).ToArray();
        var symbols=rows.SelectMany(r=>r.GetProperty("markers").EnumerateArray()).Select(m=>m.GetProperty("name").GetString()!).Distinct().Order(StringComparer.Ordinal).ToArray();
        if(symbols.Length>=64)throw new NotSupportedException("Idle marker capacity.");Sequences=new AlsAssetSyncSequence[rows.Length];_masks=new ulong[rows.Length];var markers=new List<AlsAssetSyncMarker>();
        for(var id=0;id<rows.Length;id++)
        {var r=rows[id];var ms=r.GetProperty("markers").EnumerateArray().ToArray();Sequences[id]=new(id,r.GetProperty("length").GetSingle(),r.GetProperty("rateScale").GetSingle(),markers.Count,ms.Length);
         foreach(var m in ms){var symbol=Array.IndexOf(symbols,m.GetProperty("name").GetString()!)+1;_masks[id]|=1UL<<symbol;markers.Add(new(symbol,m.GetProperty("time").GetSingle()));}}
        Markers=markers.ToArray();_roots=LyraCompressedRootBank.Load(_rootData.RootElement,Bank);
    }
    public LyraIdleLayerHost Create(string profile,int playerBase=700,long epoch=1)
    {
        var trace=Requests.GetProperty("traces").EnumerateArray().First(t=>t.GetProperty("profile").GetString()==profile);
        return CreateBound(profile,playerBase,epoch,0,_inventory,_sources,_runtime,Bank,_paths,_slots,Sequences,_masks,_roots,
            trace.GetProperty("bindings"),trace.GetProperty("breaks"));
    }
    internal static LyraIdleLayerHost CreateBound(string profile,int playerBase,long epoch,int group,
        LyraLocomotionLayerInventory _inventory,LyraSourceNodeCatalog _sources,LyraRuntimeGraphCatalog _runtime,
        LyraLogicalSourceBank Bank,string[] _paths,string[] _slots,AlsAssetSyncSequence[] Sequences,ulong[] _masks,
        LyraCompressedRootBank _roots,JsonElement bindings,JsonElement breakPaths)
    {
        var p=_inventory.Providers[profile];var graph=p.Layers[LyraLayerHook.FullBody_IdleState];var owner=_sources.ForClass(p.ClassPath);
        var ids=new[]{17,19,24,26,28};
        if(graph.Root!=36 || graph.Nodes.Count!=14 || !graph.Sources.SequenceEqual(ids) ||
            graph.Nodes[36].Links.Single().Node!=13 || graph.Nodes[13].Functions!=new LyraSourceFunctions("SetUpIdleState","None","None") ||
            graph.Nodes[14].Functions!=new LyraSourceFunctions("None","SetUpIdleState","UpdateIdleState") ||
            graph.Nodes[23].Functions!=new LyraSourceFunctions("None","SetUpTurnInPlaceRotationState","None") ||
            graph.Nodes[25].Functions!=new LyraSourceFunctions("None","SetUpTurnInPlaceRecoveryState","None"))
            throw new NotSupportedException("Changed original Idle graph.");
        var callbacks=new[]{new LyraSourceFunctions("None","None","UpdateIdleAnim"),new("None","SetupIdleTransition","None"),
            new("None","SetupTurnInPlaceAnim","UpdateTurnInPlaceAnim"),new("None","None","UpdateTurnInPlaceRecoveryAnim"),new("None","SetUpIdleBreakAnim","None")};
        var nodes=ids.Select(i=>owner.Nodes[i]).ToArray();
        for(var n=0;n<5;n++)
        {
            var s=nodes[n];var evaluator=n==2;
            if(s.Functions!=callbacks[n] || s.Kind!=(evaluator?LyraSourceKind.SequenceEvaluator:LyraSourceKind.SequencePlayer) ||
                s.Group!=(evaluator?"Test":"None") || s.Method!=(evaluator?LyraSourceSyncMethod.SyncGroup:LyraSourceSyncMethod.DoNotSync) ||
                s.Role!=LyraSourceGroupRole.CanBeLeader || s.IgnoreRelevancy || s.OverridePositionWhenJoining || s.Looping!=(n is 0 or 1 or 4))
                throw new NotSupportedException("Changed Idle source configuration.");
            if(evaluator)
            {if(!s.Settings.GetProperty("teleport").GetBoolean() || s.Settings.GetProperty("reinitialization").GetInt32()!=2 ||
                graph.Nodes[s.Index].Settings.GetProperty("bUseExplicitFrame").GetBoolean())throw new NotSupportedException("Changed Idle evaluator clock.");}
            else
            {var clamp=s.Settings.GetProperty("playRateScaleBiasClamp");if(s.Settings.GetProperty("playRate").GetSingle()!=1 || s.Settings.GetProperty("playRateBasis").GetSingle()!=1 ||
                clamp.GetProperty("bMapRange").GetBoolean() || clamp.GetProperty("bClampResult").GetBoolean() || clamp.GetProperty("bInterpResult").GetBoolean() ||
                clamp.GetProperty("scale").GetSingle()!=1 || clamp.GetProperty("bias").GetSingle()!=0 || s.Settings.GetProperty("startFromMatchingPose").GetBoolean())
                throw new NotSupportedException("Changed Idle player clock.");}
        }
        var machines=_runtime.ForClass(p.ClassPath);var idle=machines.Single(m=>m.Name=="IdleSM");var stance=machines.Single(m=>m.Name=="IdleStance");
        var endpoints=new[]{(0,3),(0,1),(1,2),(2,1),(2,0),(3,0),(3,1),(3,0),(3,0)};
        if(idle.InitialState!=0 || idle.Edges.Count!=9 || stance.InitialState!=0 || stance.Edges.Count!=3 ||
            !idle.States.Select(s=>s.Name).SequenceEqual(new[]{"Idle","TurnInPlaceRotation","TurnInPlaceRecovery","IdleBreak"}))throw new NotSupportedException("Changed Idle machine states.");
        for(var i=0;i<9;i++)if((idle.Edges[i].Previous,idle.Edges[i].Next)!=endpoints[i] || idle.Edges[i].Inertial!=(i is 2 or 3 or 4))throw new NotSupportedException("Changed Idle compiled exit.");
        if(stance.Edges.Any(e=>!e.Inertial) || !stance.States.Select(s=>s.Name).SequenceEqual(new[]{"Idle","StanceTransition"}))throw new NotSupportedException("Changed Idle stance.");
        int Asset(string key)=>Array.IndexOf(_paths,bindings.GetProperty(key).GetString()!);
        var breaks=breakPaths.EnumerateArray().Select(p=>Array.IndexOf(_paths,p.GetString()!)).ToArray();
        return new(idle,stance,nodes,Asset,breaks,Sequences,_masks,Bank,_slots,_roots,playerBase,epoch,group);
    }
    public void Dispose(){_roots.Dispose();_native.Dispose();_requests.Dispose();_rootData.Dispose();}
}
