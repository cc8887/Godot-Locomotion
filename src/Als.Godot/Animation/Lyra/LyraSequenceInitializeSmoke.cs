using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Godot;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

// Controlled seeds are written only by this scene to the real occurrence
// fields. Production has no seed API and owns exactly one copy of each clock.
public partial class LyraSequenceInitializeSmoke:Node
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    public override void _Ready()
    {
        try{Run();GetTree().Quit();}catch(Exception e){GD.PushError("Sequence Initialize native smoke failed: "+e);GetTree().Quit(1);}
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static object Field(object owner,string name)=>owner.GetType().GetField(name,Flags)!.GetValue(owner)!;
    private static void SetProperty(object owner,string name,object value)=>owner.GetType().GetProperty(name,Flags)!.SetValue(owner,value);
    private static object Property(object owner,string name)=>owner.GetType().GetProperty(name,Flags)!.GetValue(owner)!;
    private sealed record Access(Func<object> Read,Action<object> Write);
    private static Access PropertyAccess(object owner,string name)=>new(()=>Property(owner,name),v=>SetProperty(owner,name,v));
    private static Access Source(LyraItemLayerGraphInstance owner,int node)
    {
        var h=owner.Sources.Hosts;var g=h.Ground;
        object SourceHost(object pose)=>Field(pose,"_sources");
        switch(node)
        {
            case 43:return PropertyAccess(Field(SourceHost(g.Start),"_start"),"State");
            case 51:return PropertyAccess(Field(SourceHost(g.Cycle),"_cycle"),"State");
            case 55:return PropertyAccess(Field(SourceHost(g.Stop),"_stop"),"State");
            case 61:case 67:
                var pair=Field(g.Pivot.Machine,"_sources");var sources=(Array)Field(pair,"_sources");
                return PropertyAccess(sources.GetValue(node==61?0:1)!,"State");
            case 41:return PropertyAccess(SourceHost(g.Start),"HipFire");
            case 49:return PropertyAccess(SourceHost(g.Cycle),"HipFire");
            case 53:return PropertyAccess(SourceHost(g.Stop),"HipFire");
            case 57:return PropertyAccess(SourceHost(g.Pivot),"HipFire");
            case 17:case 19:case 24:case 26:case 28:
                var index=Array.IndexOf(new[]{17,19,24,26,28},node);
                return new(()=>h.Idle.Sources[index],v=>SetProperty(h.Idle,"Sources",h.Idle.Sources.SetItem(index,(LyraIdleOccurrence)v)));
            case 5:
                var additive=Field(owner,"_additives");
                return new(()=>Field(additive,"_source"),v=>additive.GetType().GetField("_source",Flags)!.SetValue(additive,v));
            case 114:return PropertyAccess(Field(owner,"_leftHand"),"Source");
        }
        foreach(var air in h.Air)
        {
            var host=SourceHost(air);var graph=(LyraAirLayerGraph)Field(host,"_graph");
            if(node==graph.Base.Index)return new(()=>air.State.Base,v=>SetProperty(host,"State",air.State with{Base=(LyraAirOccurrence)v}));
            if(node==graph.Hip.Index)return new(()=>air.State.Hip,v=>SetProperty(host,"State",air.State with{Hip=(LyraAirOccurrence)v}));
        }
        throw new InvalidOperationException("Unbound native source: "+node);
    }
    private static float F(JsonElement row,string name)=>row.GetProperty(name).GetSingle();
    private static object Seed(object source,JsonElement before,int asset)
    {
        var time=F(before,"internal");var pub=F(before,"public");var weight=F(before,"weight");
        var marker=new AlsAssetMarkerRecord(before.GetProperty("previousIndex").GetInt32(),before.GetProperty("nextIndex").GetInt32(),F(before,"previousDistance"),F(before,"nextDistance"));
        var previous=F(before,"deltaPrevious");var delta=F(before,"delta");
        return source switch
        {
            LyraStartState s=>s with{AssetId=asset,Time=time,ExplicitTime=pub,CachedWeight=weight,Marker=marker,DeltaPrevious=previous,Delta=delta,ResetPending=false},
            LyraStopState s=>s with{AssetId=asset,Time=time,ExplicitTime=pub,CachedWeight=weight,Marker=marker,DeltaPrevious=previous,Delta=delta,ResetPending=false},
            LyraPivotSourceState s=>s with{AssetId=asset,Time=time,ExplicitTime=pub,CachedWeight=weight,Marker=marker,DeltaPrevious=previous,Delta=delta,ResetPending=false},
            LyraCycleState s=>s with{AssetId=asset,Time=time,Marker=marker,DeltaPrevious=previous,Delta=delta},
            LyraHipFireSourceState s=>s with{AssetId=asset,Time=time,Marker=marker,DeltaPrevious=previous,Delta=delta,ResetPending=false},
            LyraIdleOccurrence s=>s with{AssetId=asset,Time=time,PublicTime=pub,Weight=weight,Marker=marker,Previous=previous,Delta=delta,PreviousValid=true,ResetPending=false},
            LyraAirOccurrence s=>s with{AssetId=asset,Time=time,PublicTime=pub,Weight=weight,Marker=marker,Previous=previous,Delta=delta},
            _=>throw new InvalidOperationException("Unknown occurrence state.")
        };
    }
    private static string AssetName(string path)=>path.Split('/').Last().Split('.')[0].Replace("LY_","");
    private static int Asset(LyraLocomotionResourceCatalog resources,string original)
    {
        if(original.Length==0)return -1;
        // Synthetic Main Lean IDs have no Sequence path. This probe inventory
        // consists of locomotion clips and the three recovery assets.
        var matches=Enumerable.Range(0,resources.Paths.Length).Concat(Enumerable.Range(resources.RecoveryBase,3))
            .Where(id=>AssetName(resources.Path(id))==AssetName(original)).ToArray();
        Require(matches.Length==1,"Ambiguous original/ALS asset: "+original);return matches[0];
    }
    private static void Compare(object source,JsonElement after,int node,LyraLocomotionResourceCatalog resources)
    {
        float Number(string name)=>Convert.ToSingle(Property(source,name));
        var marker=(AlsAssetMarkerRecord)Property(source,"Marker");
        Require(Number("Time")==F(after,"internal"),"Internal clock differs: "+node);
        var pub=source switch{LyraStartState s=>s.ExplicitTime,LyraStopState s=>s.ExplicitTime,LyraPivotSourceState s=>s.ExplicitTime,
            LyraIdleOccurrence s=>s.PublicTime,LyraAirOccurrence s=>s.PublicTime,LyraHipFireSourceState=>0,_=>Number("Time")};
        Require(pub==F(after,"public"),"Public clock differs: "+node);
        Require(marker.PreviousIndex==after.GetProperty("previousIndex").GetInt32()&&marker.NextIndex==after.GetProperty("nextIndex").GetInt32()&&
            marker.PreviousDistance==F(after,"previousDistance")&&marker.NextDistance==F(after,"nextDistance")&&!marker.Initialized,"Marker differs: "+node);
        var previous=source is LyraIdleOccurrence or LyraAirOccurrence?"Previous":"DeltaPrevious";
        Require(Number(previous)==F(after,"deltaPrevious")&&Number("Delta")==F(after,"delta"),"Delta history differs: "+node);
        var id=(int)Property(source,"AssetId");Require(id==Asset(resources,after.GetProperty("asset").GetString()!),"Initialized asset differs: "+node);
        if(source is LyraStartState or LyraStopState or LyraPivotSourceState)
            Require(Number("CachedWeight")==F(after,"weight"),"Cached weight differs: "+node);
        if(source is LyraIdleOccurrence or LyraAirOccurrence)
            Require(Number("Weight")==F(after,"weight"),"Cached weight differs: "+node);
    }
    private static void Run()
    {
        var bytes=Godot.FileAccess.GetFileAsBytes("res://artifacts/lyra-analysis/source-initialize-v1-native.json");
        using var closure=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://artifacts/lyra-analysis/source-initialize-v1-closure.json"));
        Require(LyraLogicalSourceBank.Sha(bytes)==closure.RootElement.GetProperty("nativeSha256").GetString(),"Stale Sequence Initialize native reference.");
        using var native=JsonDocument.Parse(bytes);using var resources=new LyraLocomotionResources();var rows=0;
        foreach(var c in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var profile=c.GetProperty("profile").GetString()!;using var host=new LyraMainPoseHost(resources,profile);var owner=host.Main.LayerInstances.Single();
            // Recovery's initial machine state is additive identity; its leaf
            // is not visited by startup, but standalone initialization is also
            // tested against the original leaf method for future state entry.
            owner.InitializePhaseNode(12);owner.InitializePhaseNode(117);
            foreach(var row in c.GetProperty("sources").EnumerateArray())
            {
                var node=row.GetProperty("node").GetInt32();var access=Source(owner,node);var before=row.GetProperty("before");
                access.Write(Seed(access.Read(),before,Asset(resources.Catalog,before.GetProperty("asset").GetString()!)));
                var fields=owner.Sources.Hosts.Idle.Fields;var worker=Property(Field(owner,"_worker"),"State");
                owner.InitializePhaseNode(node);Compare(access.Read(),row.GetProperty("after"),node,resources.Catalog);
                Require(owner.Sources.Hosts.Idle.Fields==fields&&Equals(Property(Field(owner,"_worker"),"State"),worker),"Initialize ran a callback or worker update.");
                Require(host.Main.SyncPlayers.IsEmpty&&host.Main.SyncSamples.IsEmpty,"Initialize registered a source tick.");rows++;
            }
            var start=(LyraStartLayerSourceHost)Field(owner.Sources.Hosts.Ground.Start,"_sources");
            var committed=start.Start;var hip=start.HipFire;
            var candidate=start.Prepare(default,0,0,0,false,false);
            try{owner.InitializePhaseNode(43);throw new InvalidOperationException("Pending source accepted Initialize.");}
            catch(InvalidOperationException e)when(e.Message=="Start initialization needs an idle source."||e.Message=="Start phase initialization needs an idle layer."){}
            Require(start.Start==committed&&start.HipFire==hip,"Rejected Initialize mutated committed history.");
            start.Cancel();var retry=start.Prepare(default,0,0,0,false,false);
            Require(candidate.Start.State==retry.Start.State&&candidate.HipFire==retry.HipFire,"Phase-initialized hidden frame changed on retry.");
            start.Commit(retry,[]);
            Require(start.HipFire.ResetPending,"Hidden frame consumed an unticked HipFire initialization.");
            var active=start.Prepare(default,1f/60,1,1,true,false);
            Require(active.Start.BecameRelevant&&active.Start.State.AssetId>=0,"First Update did not execute original Setup.");
            Require(!active.Start.State.ResetPending&&!active.HipFire.ResetPending,"First actual Update did not consume initialization.");
            start.Cancel();Require(start.HipFire.ResetPending,"Cancelled Update consumed committed initialization.");
            SetProperty(start,"HipFire",start.HipFire with{ResetPending=false});
            var hiddenReset=start.Prepare(default,0,0,0,false,true);
            Require(hiddenReset.HipFire.ResetPending,"Hidden graph reinitialization was not retained.");
            start.Cancel();Require(!start.HipFire.ResetPending,"Cancelled hidden reinitialization changed committed flags.");
            hiddenReset=start.Prepare(default,0,0,0,false,true);start.Commit(hiddenReset,[]);
            Require(start.HipFire.ResetPending,"Committed hidden reinitialization was lost.");
            var left=(LyraLeftHandLayerHost)Field(owner,"_leftHand");var nullSource=left.Source;
            var nullCandidate=left.Prepare(true,false,1,0,true);
            Require(!nullCandidate.Source.ResetPending,"Null evaluator Update did not consume initialization.");
            left.Cancel();Require(left.Source==nullSource,"Cancelled null evaluator mutated committed history.");
            nullCandidate=left.Prepare(true,false,1,0,true);left.Commit(nullCandidate,true);
            Require(!left.Source.ResetPending&&left.Source.Time==nullSource.Time,"Null evaluator Update advanced its internal clock.");
        }
        Require(rows==156,"Incomplete original source inventory.");
        GD.Print($"LYRA_SEQUENCE_INITIALIZE_NATIVE_GODOT_OK profiles=3 nodes=78 seededRows={rows} retry=3 deferredHip=3 pendingRejected=3 sourceUpdateDuringInitialize=false evaluate=false fullWeightFieldCompared=false");
    }
}
