using System.Text.Json;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal sealed class LyraAirResources : IDisposable
{
    private const string Root="res://assets/generated/lyra_als/";
    private readonly JsonDocument _native,_requests,_distance,_rootData;
    private readonly LyraSourceNodeCatalog _catalog=LyraSourceNodeCatalog.Load();
    private readonly LyraLocomotionLayerInventory _inventory;
    private readonly LyraCompressedRootBank _roots;
    private readonly LyraStopDistanceBank _distances;
    private readonly string[] _paths,_slots;
    private readonly ulong[] _masks;
    public LyraLogicalSourceBank Bank {get;}=LyraLogicalSourceBank.Load(includeLocomotionExtras:true);
    public AlsAssetSyncSequence[] Sequences {get;}
    public AlsAssetSyncMarker[] Markers {get;}
    public JsonElement Native=>_native.RootElement;
    public JsonElement Requests=>_requests.RootElement;
    public string Path(int id)=>id<0?"":_paths[id];
    private static JsonDocument Load(string name)=>JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root+name));
    public LyraAirResources()
    {
        _inventory=LyraLocomotionLayerInventory.Load(_catalog);
        _native=Load("air_runtime_native.json");_requests=Load("air_runtime_requests.json");_distance=Load("air_runtime_distance.json");_rootData=Load("air_runtime_roots.json");
        var data=Native;
        foreach(var (file,field) in new[]{("air_runtime_requests.json","requestSha256"),("air_runtime_distance.json","distanceSha256"),("air_runtime_roots.json","rootSha256")})
            if(data.GetProperty(field).GetString()!=LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+file)))throw new InvalidOperationException("Stale Air resource: "+file);
        foreach(var dep in data.GetProperty("dependencies").EnumerateObject())
            if(dep.Value.GetString()!=LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+dep.Name)))throw new InvalidOperationException("Changed Air dependency: "+dep.Name);
        if(_distance.RootElement.GetProperty("requestSha256").GetString()!=data.GetProperty("requestSha256").GetString() ||
            _rootData.RootElement.GetProperty("requestSha256").GetString()!=data.GetProperty("requestSha256").GetString())throw new InvalidOperationException("Air input identity differs.");
        var rows=data.GetProperty("assets").EnumerateArray().ToArray();_paths=rows.Select(r=>r.GetProperty("path").GetString()!).ToArray();
        if(!_paths.SequenceEqual(Requests.GetProperty("sequencePaths").EnumerateArray().Select(r=>r.GetString()!)))throw new InvalidOperationException("Changed Air sequence address space.");
        _slots=_paths.Select(p=>Bank.Slots.Single(s=>Bank.Get(s).Data.Identity.AssetPath==p)).ToArray();
        var symbols=rows.SelectMany(r=>r.GetProperty("markers").EnumerateArray()).Select(m=>m.GetProperty("name").GetString()!).Distinct().Order(StringComparer.Ordinal).ToArray();
        if(symbols.Length>=64)throw new NotSupportedException("Air marker capacity.");
        Sequences=new AlsAssetSyncSequence[rows.Length];_masks=new ulong[rows.Length];var markers=new List<AlsAssetSyncMarker>();
        for(var id=0;id<rows.Length;id++)
        {
            var r=rows[id];var ms=r.GetProperty("markers").EnumerateArray().ToArray();
            Sequences[id]=new(id,r.GetProperty("length").GetSingle(),r.GetProperty("rateScale").GetSingle(),markers.Count,ms.Length);
            foreach(var m in ms){var symbol=Array.IndexOf(symbols,m.GetProperty("name").GetString()!)+1;_masks[id]|=1UL<<symbol;markers.Add(new(symbol,m.GetProperty("time").GetSingle()));}
        }
        Markers=markers.ToArray();_distances=new(_distance.RootElement);_roots=LyraCompressedRootBank.Load(_rootData.RootElement,Bank);
    }
    public LyraAirLayerPoseHost[] Create(string profile,int playerBase=0,long epoch=1)
    {
        var bindings=Requests.GetProperty("traces").EnumerateArray().First(t=>t.GetProperty("profile").GetString()==profile).GetProperty("bindings");
        return CreateBound(profile,playerBase,epoch,0,_catalog,_inventory,Bank,_paths,_slots,Sequences,_masks,_roots,_distances,bindings);
    }
    internal static LyraAirLayerPoseHost[] CreateBound(string profile,int playerBase,long epoch,int group,
        LyraSourceNodeCatalog _catalog,LyraLocomotionLayerInventory _inventory,LyraLogicalSourceBank Bank,
        string[] _paths,string[] _slots,AlsAssetSyncSequence[] Sequences,ulong[] _masks,LyraCompressedRootBank _roots,
        LyraStopDistanceBank _distances,JsonElement bindings)
    {
        var keys=new[]{"Jump_Start","Jump_StartLoop","Jump_Apex","Jump_FallLoop","Jump_FallLand"};
        int Id(string key)=>Array.IndexOf(_paths,bindings.GetProperty(key).GetString()!);
        int Hip(bool crouching)=>Id(crouching?"Aim_HipFirePose_Crouch":"Aim_HipFirePose");
        LyraStopAsset Distance(int id)
        {var d=_distances.Asset(_paths[id]);return d with {Id=id,Sequence=Sequences[id],MarkerMask=_masks[id]};}
        return Enumerable.Range(0,5).Select(i=>
        {
            var graph=LyraAirLayerGraph.Load(profile,(LyraAirLayer)i,_catalog,_inventory);
            var sources=new LyraAirLayerSourceHost(graph,Id(keys[i]),playerBase,epoch,Hip,Sequences,_masks,Distance,group);
            return new LyraAirLayerPoseHost(sources,graph,Bank,LyraCycleLayerPosePolicy.Load(profile,Bank),_slots,_roots);
        }).ToArray();
    }
    public void Dispose(){_roots.Dispose();_native.Dispose();_requests.Dispose();_distance.Dispose();_rootData.Dispose();}
}
