using System.Text.Json;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraMainGroundHosts(LyraMainSourceScope Scope,LyraStartLayerPoseHost Start,
    LyraCycleLayerPoseHost Cycle,LyraStopLayerPoseHost Stop,LyraPivotLayerPoseHost Pivot);

// One immutable resource address space for the four real ground roots.
// Mutable machines, source occurrences and Warp histories belong to Create().
internal sealed class LyraMainGroundResources : IDisposable
{
    private const string Root="res://assets/generated/lyra_als/";
    private readonly List<JsonDocument> _documents=[];
    private JsonElement Load(string name)
    { var document=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root+name)); _documents.Add(document); return document.RootElement; }
    public LyraLogicalSourceBank Bank { get; }
    public LyraSourceNodeCatalog Catalog { get; }=LyraSourceNodeCatalog.Load();
    private readonly LyraLinkedLayerInventory _inventory=LyraLinkedLayerInventory.Load();
    private readonly LyraLinkedLayerContracts _contracts=LyraLinkedLayerContracts.Load();
    private readonly LyraLocomotionLayerInventory _closures;
    private readonly JsonElement _startRequests,_stopRequests,_pivotRequests,_cycleDefinitions;
    private readonly LyraStartDistanceBank _startDistance;
    private readonly LyraStopDistanceBank _stopDistance;
    private readonly LyraPivotDistanceBank _pivotDistance;
    private readonly Dictionary<string,int> _ids;
    private readonly string[] _paths,_slots;
    private readonly int _leanBase;
    private readonly ulong[] _masks;
    private readonly Dictionary<string,string[]> _targets;
    private readonly LyraCompressedRootBank _roots;
    private readonly LyraLocomotionResourceCatalog? _shared;
    public AlsAssetSyncSequence[] Sequences { get; }
    public AlsAssetSyncMarker[] Markers { get; }
    public string Path(int id)=>id<0 ? "" : _paths[id];
    public int Id(string path)=>_ids[path];

    public LyraMainGroundResources(bool nativeGround=false,LyraLocomotionResourceCatalog? shared=null)
    {
        _shared=shared;Bank=shared?.Bank??LyraLogicalSourceBank.Load(includeMainLean:true,includeLocomotionExtras:true);
        _closures=LyraLocomotionLayerInventory.Load(Catalog);
        if(shared is not null)
        {
            _paths=shared.Paths;_slots=shared.Slots;_masks=shared.Masks;_leanBase=shared.LeanBase;_roots=shared.Roots;
            Sequences=shared.Sequences;Markers=shared.Markers;
            _ids=_paths.Select((p,i)=>(p,i)).ToDictionary(p=>p.p,p=>p.i,StringComparer.Ordinal);
            var sharedEntries=Load("logical_controls/catalog.json").GetProperty("entries").EnumerateArray().ToArray();
            _targets=sharedEntries.GroupBy(e=>e.GetProperty("source").GetString()!).ToDictionary(g=>g.Key,
                g=>g.Select(e=>e.GetProperty("target").GetString()!).ToArray(),StringComparer.Ordinal);
            _startRequests=_stopRequests=_pivotRequests=default;_cycleDefinitions=Load("cycle_source_definitions.json");
            _startDistance=new(Load("start_runtime_distance.json"));_stopDistance=new(Load("stop_runtime_distance.json"));_pivotDistance=new(Load("pivot_runtime_distance.json"));
            return;
        }
        using var groundDocument=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root+"main_state_history_native.json"));
        using var pivotDocument=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root+"main_pivot_native.json"));
        var ground=groundDocument.RootElement;var pivot=pivotDocument.RootElement;
        using var jointDocument=nativeGround ? JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root+"main_ground_scope_v2_native.json")) : null;
        foreach (var native in jointDocument is null ? new[]{ground,pivot} : new[]{ground,pivot,jointDocument.RootElement})
        foreach (var dep in native.GetProperty("dependencies").EnumerateObject())
            if (dep.Value.GetString()!=LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+dep.Name)))
                throw new InvalidOperationException("Changed ground resource dependency: "+dep.Name);
        // Prefer the actual transient Pivot marker inventory for overlapping
        // assets. Existing generated JSON bytes and distance codecs stay intact.
        var assets=ground.GetProperty("assets").EnumerateArray().ToList();
        foreach (var asset in pivot.GetProperty("assets").EnumerateArray())
        {
            var path=asset.GetProperty("path").GetString();
            var index=assets.FindIndex(a=>a.GetProperty("path").GetString()==path);
            if (index<0) assets.Add(asset); else assets[index]=asset;
        }
        if(jointDocument is not null)
        {
            var actual=jointDocument.RootElement.GetProperty("assets").EnumerateArray().ToList();
            if(!actual.Select(a=>a.GetProperty("path").GetString()).SequenceEqual(assets.Select(a=>a.GetProperty("path").GetString())))
                throw new InvalidOperationException("Different actual four-root resource address space.");
            if(jointDocument.RootElement.GetProperty("requestSha256").GetString()!=LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+"main_ground_scope_v2_requests.json")))
                throw new InvalidOperationException("Changed actual ground requests.");
            assets=actual;
        }
        _paths=assets.Select(a=>a.GetProperty("path").GetString()!).ToArray();
        _ids=_paths.Select((p,i)=>(p,i)).ToDictionary(p=>p.p,p=>p.i,StringComparer.Ordinal);
        var entries=Load("logical_controls/catalog.json").GetProperty("entries").EnumerateArray().ToArray();
        _slots=_paths.Select(p=>entries.Single(e=>e.GetProperty("target").GetString()==p).GetProperty("slot").GetString()!).ToArray();
        _targets=entries.GroupBy(e=>e.GetProperty("source").GetString()!).ToDictionary(g=>g.Key,
            g=>g.Select(e=>e.GetProperty("target").GetString()!).ToArray(),StringComparer.Ordinal);
        var symbols=assets.SelectMany(a=>a.GetProperty("markers").EnumerateArray()).Select(m=>m.GetProperty("name").GetString()!)
            .Distinct().OrderBy(s=>s,StringComparer.Ordinal).ToArray();
        if (symbols.Length>=64) throw new NotSupportedException("Ground marker symbol capacity exceeded.");
        var sequences=new List<AlsAssetSyncSequence>(); var markers=new List<AlsAssetSyncMarker>(); _masks=new ulong[assets.Count+3];
        for (var id=0;id<assets.Count;id++)
        {
            var a=assets[id]; var ms=a.GetProperty("markers").EnumerateArray().ToArray();
            sequences.Add(new(id,a.GetProperty("length").GetSingle(),a.GetProperty("rateScale").GetSingle(),markers.Count,ms.Length));
            foreach (var m in ms)
            { var symbol=Array.IndexOf(symbols,m.GetProperty("name").GetString()!)+1; _masks[id]|=1UL<<symbol;
                markers.Add(new(symbol,m.GetProperty("time").GetSingle())); }
        }
        _leanBase=sequences.Count;
        foreach (var slot in new[]{"main_lean_center","main_lean_left","main_lean_right"})
        { var d=Bank.Get(slot).Data; sequences.Add(new(d.Identity.AnimationId,(float)d.PlayLength,1,0,0)); }
        Sequences=sequences.ToArray(); Markers=markers.ToArray();
        _startRequests=Load("main_start_lean_requests.json"); _stopRequests=Load("main_stop_runtime_requests.json");
        _pivotRequests=Load("main_pivot_requests.json"); _cycleDefinitions=Load("cycle_source_definitions.json");
        _startDistance=new(Load("start_runtime_distance.json")); _stopDistance=new(Load("stop_runtime_distance.json"));
        _pivotDistance=new(Load("pivot_runtime_distance.json"));
        var roots=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach (var name in new[]{"main_state_history_roots.json","pivot_runtime_roots.json"})
        foreach (var row in Load(name).GetProperty("assets").EnumerateObject())
        {
            if (roots.TryGetValue(row.Name,out var old) && old.GetRawText()!=row.Value.GetRawText())
                throw new InvalidOperationException("Different compressed root definitions: "+row.Name);
            roots[row.Name]=row.Value;
        }
        using var combined=JsonDocument.Parse(JsonSerializer.Serialize(new { schemaVersion=1,assets=roots }));
        _roots=LyraCompressedRootBank.Load(combined.RootElement,Bank);
        foreach (var path in _paths) if (!roots.ContainsKey(path)) throw new InvalidOperationException("Missing ground compressed root: "+path);
    }

    internal LyraMainGraphStateOwner CreateMainOwner(int leanPlayerBase=1000,long epoch=1)
        =>new(Bank,Sequences,leanPlayerBase,9000,_leanBase,epoch);
    internal LyraMainGroundHosts Compose(LyraStartLayerPoseHost start,LyraCycleLayerPoseHost cycle,
        LyraStopLayerPoseHost stop,LyraPivotLayerPoseHost pivot,LyraMainGraphStateOwner mainOwner,
        LyraLinkedLayerClassContract contract,int leanPlayerBase,long epoch)
    {
        var signatures=contract.Functions;
        var scope=new LyraMainSourceScope(start,cycle,Bank,Sequences,_leanBase,
            signatures[LyraLayerHook.FullBody_StartState],signatures[LyraLayerHook.FullBody_CycleState],
            leanPlayerBase:leanPlayerBase,epoch:epoch,stop:stop,stopSignature:signatures[LyraLayerHook.FullBody_StopState],
            stateRoots:true,pivot:pivot,pivotSignature:signatures[LyraLayerHook.FullBody_PivotState],mainOwner:mainOwner);
        return new(scope,start,cycle,stop,pivot);
    }
    public LyraMainGroundHosts Create(string profile,int playerBase=0,int leanPlayerBase=1000,long epoch=1,
        LyraMainGraphStateOwner? mainOwner=null)
    {
        var provider=_inventory.Get(profile); var signatures=_contracts.Get(provider.ClassPath).Functions;
        static string Key(LyraCardinalDirection d)=>d switch { LyraCardinalDirection.Forward=>"forward",LyraCardinalDirection.Backward=>"backward",
            LyraCardinalDirection.Left=>"left",LyraCardinalDirection.Right=>"right",_=>throw new ArgumentException("Direction") };
        JsonElement Bindings(JsonElement data)=>data.GetProperty("traces").EnumerateArray().First(t=>t.GetProperty("profile").GetString()==profile).GetProperty("bindings");
        var startBindings=_shared is null?Bindings(_startRequests):_shared.Provider(profile).GetProperty("start");
        var stopBindings=_shared is null?Bindings(_stopRequests):_shared.Provider(profile).GetProperty("stop");
        var pivotBindings=_shared is null?Bindings(_pivotRequests):_shared.Provider(profile).GetProperty("pivot");
        string Target(string source)
        {
            var choices=_targets[source];
            if (choices.Length==1) return choices[0];
            // Same canonical ambiguity rule as the previously validated scope.
            var slot=profile=="unarmed" ? "hipfire_crouch" : "pistol_crouch_idle";
            var index=Array.IndexOf(_slots,slot);
            if(index<0) throw new InvalidOperationException("Missing canonical shared HipFire slot: "+slot);
            var target=_paths[index];
            return choices.Single(p=>p==target);
        }
        int Hip(bool crouch)=>Id(pivotBindings.GetProperty(crouch ? "Aim_HipFirePose_Crouch" : "Aim_HipFirePose").GetString()!);
        LyraStartAsset StartByPath(string path)
        { var local=_startDistance.Asset(path);var id=Id(path);return local with { Id=id,Sequence=Sequences[id],MarkerMask=_masks[id] }; }
        LyraStartAsset StartById(int id)=>StartByPath(Path(id));
        LyraStartAsset StartSelect(string group,LyraCardinalDirection d)=>StartByPath(startBindings.GetProperty(group).GetProperty(Key(d)).GetString()!);
        LyraStopAsset StopByPath(string path)
        { var local=_stopDistance.Asset(path);var id=Id(path);return local with { Id=id,Sequence=Sequences[id],MarkerMask=_masks[id] }; }
        LyraStopAsset StopById(int id)=>StopByPath(Path(id));
        LyraStopAsset StopSelect(string group,LyraCardinalDirection d)=>StopByPath(stopBindings.GetProperty(group).GetProperty(Key(d)).GetString()!);
        LyraPivotAsset PivotByPath(string path)
        { var local=_pivotDistance.Asset(path);var id=Id(path);return new(local.Advance with { Id=id,Sequence=Sequences[id],MarkerMask=_masks[id] },
            local.Match with { Id=id,Sequence=Sequences[id],MarkerMask=_masks[id] }); }
        LyraPivotAsset PivotById(int id)=>PivotByPath(Path(id));
        LyraPivotAsset PivotSelect(string group,LyraCardinalDirection d)=>PivotByPath(pivotBindings.GetProperty(group).GetProperty(Key(d)).GetString()!);
        LyraCycleAsset CycleSelect(string group,LyraCardinalDirection d)
        { var target=Target(provider.Cardinal(group,d)!);var row=_cycleDefinitions.GetProperty("assets").GetProperty(target);
            return new(Id(target),LyraStartDistanceBank.Float(row,"length"),LyraStartDistanceBank.Float(row,"rootDistance")); }
        var policy=LyraCycleLayerPosePolicy.Load(profile,Bank);
        var startGraph=LyraStartLayerGraph.Load(profile,Catalog);var cycleGraph=LyraCycleLayerGraph.Load(profile,Catalog);
        var stopGraph=LyraStopLayerGraph.Load(profile,Catalog);var pivotGraph=LyraPivotLayerGraph.Load(profile,Catalog,_closures);
        var start=new LyraStartLayerPoseHost(new(startGraph,playerBase,epoch,StartSelect,StartById,_startDistance.Policy(profile),Hip,Sequences,_masks),
            Bank,policy,_slots,LyraOrientationWarpingPolicy.Load(profile,Bank,"start_layer_graph.json"),
            LyraStrideWarpingPolicy.Load(profile,Bank,"start_layer_graph.json"),startGraph.HipFire.Looping,_roots);
        var clamp=_cycleDefinitions.GetProperty("clamps").GetProperty(profile);
        var cycle=new LyraCycleLayerPoseHost(new(cycleGraph,playerBase,epoch,CycleSelect,Hip,Sequences,_masks,
            LyraStartDistanceBank.Double(clamp,"clampMin"),LyraStartDistanceBank.Double(clamp,"clampMax")),Bank,policy,_slots,true,
            LyraOrientationWarpingPolicy.Load(profile,Bank),LyraStrideWarpingPolicy.Load(profile,Bank),LyraCycleRuntimeBindings.Load(profile),_roots);
        var stop=new LyraStopLayerPoseHost(new(stopGraph,playerBase,epoch,StopSelect,StopById,Hip,Sequences,_masks,groupId:1),
            Bank,policy,_slots,stopGraph.HipFire.Looping,_roots);
        var pair=new LyraPivotSourcePair(new(pivotGraph.PivotA,checked(playerBase+pivotGraph.PivotA.Index),epoch,PivotSelect,PivotById,_pivotDistance.Policy(profile)),
            new(pivotGraph.PivotB,checked(playerBase+pivotGraph.PivotB.Index),epoch,PivotSelect,PivotById,_pivotDistance.Policy(profile)));
        var machine=new LyraPivotMachineRuntime(pivotGraph,pair);
        var pivot=new LyraPivotLayerPoseHost(new(pivotGraph,machine,playerBase,epoch,Hip,Sequences,_masks),Bank,policy,_slots,
            pivotGraph.Warps.Select(w=>LyraOrientationWarpingPolicy.Load(profile,Bank,"pivot_layer_graph.json",w.Orientation)).ToArray(),
            pivotGraph.Warps.Select(w=>LyraStrideWarpingPolicy.Load(profile,Bank,"pivot_layer_graph.json",w.Stride)).ToArray(),pivotGraph.HipFire.Looping,_roots);
        var scope=new LyraMainSourceScope(start,cycle,Bank,Sequences,_leanBase,signatures[LyraLayerHook.FullBody_StartState],
            signatures[LyraLayerHook.FullBody_CycleState],leanPlayerBase:leanPlayerBase,epoch:epoch,stop:stop,
            stopSignature:signatures[LyraLayerHook.FullBody_StopState],stateRoots:true,pivot:pivot,pivotSignature:signatures[LyraLayerHook.FullBody_PivotState],mainOwner:mainOwner);
        return new(scope,start,cycle,stop,pivot);
    }
    public void Dispose() { if(_shared is null)_roots.Dispose();foreach(var document in _documents) document.Dispose(); }
}
