using System.Text.Json;
using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraMainSlotsSmoke:Node
{
    private static void Require(bool ok,string label){if(!ok)throw new InvalidOperationException(label);}
    private static void Exact(float a,float b,string label)=>Require(BitConverter.SingleToInt32Bits(a)==BitConverter.SingleToInt32Bits(b),label+$" {a:R}/{b:R}");
    public override void _Ready(){try{if(OS.GetCmdlineUserArgs().Contains("--host-update"))RunHost();else Run();GetTree().Quit();}catch(Exception e){GD.PushError("Main Slots failed: "+e);GetTree().Quit(1);}}
    private static void Run()
    {
        const string root="res://assets/generated/lyra_als/";
        using var requests=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"main_slots_v1_requests.json"));
        using var oracle=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"main_slots_v1_native.json"));
        var data=oracle.RootElement;
        Require(data.GetProperty("schemaVersion").GetInt32()==1&&data.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+"main_slots_v1_requests.json")),"Stale Main Slots request.");
        foreach(var d in data.GetProperty("dependencies").EnumerateObject())Require(d.Value.GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+d.Name)),"Stale Main Slots dependency.");
        var catalog=new LyraMontageCatalog();var graphs=LyraMainLayerGraphCatalog.Load();
        var frames=0;var events=0;var skipped=0;var covered=0;var hidden=0;var sourceInactive=0;var upperRootZero=0;var additiveSkipped=0;var rejected=0;
        foreach(var (trace,ti) in data.GetProperty("traces").EnumerateArray().Select((v,i)=>(v,i)))
        {
            var runtime=catalog.CreateRuntime();var slots=new LyraMainSlotsTraversal(runtime);
            var cache=new LyraMainPoseCacheTraversal(graphs,trace.GetProperty("profile").GetString()!);
            foreach(var (row,fi) in trace.GetProperty("frames").EnumerateArray().Select((v,i)=>(v,i)))
            {
                var q=requests.RootElement.GetProperty("traces")[ti].GetProperty("frames")[fi];var id=new AlsFrameIdentity(fi,(uint)(61+ti),19);
                string? first=null;
                for(var attempt=0;attempt<2;attempt++)
                {
                    runtime.Begin(id,q.GetProperty("delta").GetSingle());
                    var context=new AlsPoseUpdateContext(id,q.GetProperty("weight").GetSingle(),q.GetProperty("delta").GetSingle(),q.GetProperty("rootModifier").GetSingle()).WithInertialization(75,true);
                    if(!q.GetProperty("active").GetBoolean())context=context.AsInactive();
                    slots.Begin(runtime.Frame,context,q.GetProperty("visited").GetBoolean(),q.GetProperty("initialize").GetBoolean(),q.GetProperty("dynamicWeight").GetSingle());
                    var readers=new List<LyraMainCacheUpdate>();var blend=q.GetProperty("aimBlend").GetSingle();
                    if(slots.AimingSource is {} aiming)
                    {
                        var a=blend<1-AlsPoseBlender.WeightThreshold;var b=blend>AlsPoseBlender.WeightThreshold;
                        if(a)readers.Add(new(76,a&&b?aiming.WithWeight(aiming.Weight*(1-blend)):aiming));
                        if(b)readers.Add(new(75,a&&b?aiming.WithWeight(aiming.Weight*blend):aiming));
                    }
                    var result=cache.Resolve(id,readers.ToArray(),slots);var visits=slots.Visits;var label=$"MainSlots/{ti}/{fi}";
                    Require(visits.Length==row.GetProperty("events").GetArrayLength(),label+$"/event count {visits.Length}/{row.GetProperty("events").GetArrayLength()}");
                    for(var n=0;n<visits.Length;n++)
                    {
                        var v=visits[n];var e=row.GetProperty("events")[n];
                        Require(v.Kind==e.GetProperty("kind").GetString()&&v.Id==e.GetProperty("id").GetInt32(),label+"/ordered node dispatch");
                        Require(v.Context.Identity==id&&v.Context.IsActive==e.GetProperty("active").GetBoolean()&&v.Context.HasSharedContext==e.GetProperty("shared").GetBoolean(),label+"/complete context");
                        Exact(v.Context.Weight,e.GetProperty("weight").GetSingle(),label+"/weight");Exact(v.Context.RootMotionWeight,e.GetProperty("root").GetSingle(),label+"/root modifier");
                        if(attempt==1){events++;sourceInactive+=v.Kind=="source"&&!v.Context.IsActive?1:0;upperRootZero+=v.Kind=="slot"&&v.Id==0&&v.Context.RootMotionWeight==0?1:0;}
                    }
                    for(var s=0;s<5;s++)
                    {
                        var w=slots.Weights(s);var e=row.GetProperty("slots")[s];
                        Exact(w.SourceWeight,e.GetProperty("sourceWeight").GetSingle(),label+"/hidden source history");
                        Exact(w.SlotNodeWeight,e.GetProperty("slotWeight").GetSingle(),label+"/slot history");Exact(w.TotalNodeWeight,e.GetProperty("totalWeight").GetSingle(),label+"/total history");
                    }
                    Require(result.Skipped.Length==row.GetProperty("skipped").GetArrayLength(),label+"/skipped handler count");
                    for(var n=0;n<result.Skipped.Length;n++)
                    {
                        var s=result.Skipped[n];var e=row.GetProperty("skipped")[n];Require(s.Cache==e.GetProperty("cache").GetInt32()&&s.Handler==75&&s.Contexts.Length==e.GetProperty("count").GetInt32(),label+"/skipped paths");
                        if(attempt==1)skipped+=s.Contexts.Length;
                    }
                    if(fi%71==0)
                    {
                        try{cache.Resolve(new(fi,id.CharacterId,20),readers.ToArray(),slots);throw new Exception("Accepted foreign Slot traversal.");}catch(InvalidOperationException){rejected++;}
                        try{cache.Resolve(id,readers.ToArray(),slots);throw new Exception("Accepted duplicate Slot traversal.");}catch(InvalidOperationException){rejected++;}
                    }
                    var frozen=runtime.Evaluation.ToArray();
                    foreach(var command in q.GetProperty("commands").EnumerateArray())
                    {
                        var asset=command.GetProperty("asset").GetInt32();
                        if(command.GetProperty("stop").GetBoolean())
                        {
                            var explicitStop=command.TryGetProperty("instanceStop",out var stop)&&stop.GetBoolean();
                            var active=runtime.Candidate.ToArray().LastOrDefault(v=>v.MontageId==asset&&(explicitStop||v.OwnsActiveActionLookup));
                            if(active.InstanceId>0)Require(runtime.StopInstance(active.InstanceId,command.GetProperty("blend").GetSingle(),catalog.Definitions[asset].Lifecycle.BlendOutOption),"Stop rejected.");
                        }
                        else Require(runtime.PlayAction(asset,command.GetProperty("rate").GetSingle(),command.GetProperty("start").GetSingle(),stopGroup:command.GetProperty("stopGroup").GetBoolean()),"Play rejected.");
                    }
                    Require(frozen.SequenceEqual(runtime.Evaluation.ToArray()),label+"/frozen bank mutated by command");
                    var signature=JsonSerializer.Serialize(visits)+JsonSerializer.Serialize(result)+JsonSerializer.Serialize(runtime.Candidate.ToArray());
                    if(attempt==0){first=signature;slots.Cancel();runtime.Discard();}
                    else
                    {
                        Require(first==signature,label+"/cancel retry");slots.ValidateCommit(id);runtime.ValidateCommit(id);slots.Commit(id);runtime.Commit(id);
                        covered+=q.GetProperty("visited").GetBoolean()&&!visits.Any(v=>v.Id==83)?1:0;
                        hidden+=!q.GetProperty("visited").GetBoolean()?1:0;
                        additiveSkipped+=visits.Any(v=>v.Kind=="cache"&&v.Id==78)&&!visits.Any(v=>v.Kind=="slot"&&v.Id==1)?1:0;
                    }
                }
                frames++;
            }
        }
        Require(frames==47610&&events>0&&covered>0&&hidden>0&&sourceInactive>0&&upperRootZero>0&&additiveSkipped>0&&rejected>0,"Incomplete Main Slots coverage.");
        GD.Print($"LYRA_MAIN_SLOTS_GODOT_OK frames={frames} events={events} skipped={skipped} covered={covered} hidden={hidden} inactive={sourceInactive} upperRootZero={upperRootZero} additiveSkipped={additiveSkipped} retry={frames} rejected={rejected} exact=true originalNodes=true pose=false production=false");
    }
    private static void RunHost()
    {
        using var resources=new LyraLocomotionResources();var catalog=new LyraMontageCatalog();
        using var observations=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/main_als_locomotion_v1_requests.json"));
        using var requests=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/main_slots_v1_requests.json"));
        var frames=0;var covered=0;var locHidden=0;var visited=0;var retries=0;var players=0;var aimActive=0;var additiveActive=0;
        // First trace per configuration is the 64-second physical timeline.
        // The separate clock arithmetic traces (including 1090-second deltas)
        // remain fully exercised by the native Slot/cache component test.
        foreach(var trace in requests.RootElement.GetProperty("traces").EnumerateArray()
            .GroupBy(t=>(t.GetProperty("profile").GetString(),t.GetProperty("hz").GetInt32())).Select(g=>g.First()))
        {
            var profile=trace.GetProperty("profile").GetString()!;var hz=trace.GetProperty("hz").GetInt32();var runtime=catalog.CreateRuntime();
            Require(trace.GetProperty("frames").GetArrayLength()==hz*64&&trace.GetProperty("frames").EnumerateArray().All(f=>f.GetProperty("delta").GetSingle()==1f/hz),"Changed physical Main timeline.");
            var host=new LyraMainLocomotionHost(resources,profile,700,1700,7,montageRuntime:runtime);
            var authored=observations.RootElement.GetProperty("traces").EnumerateArray().First(t=>t.GetProperty("profile").GetString()==profile&&t.GetProperty("hz").GetInt32()==hz).GetProperty("frames");
            var fi=0;
            foreach(var q in trace.GetProperty("frames").EnumerateArray())
            {
                var f=authored[fi%authored.GetArrayLength()];var input=LyraMainUpdateSmoke.ReadInput(f.GetProperty("observation"));var id=new AlsFrameIdentity(fi,0,1);var delta=q.GetProperty("delta").GetSingle();
                var visit=new LyraLocomotionMachineVisit(q.GetProperty("visited").GetBoolean(),q.GetProperty("weight").GetSingle(),q.GetProperty("initialize").GetBoolean(),q.GetProperty("active").GetBoolean());
                AlsDoubleVector V(JsonElement v)=>new(v[0].GetDouble(),v[1].GetDouble(),v[2].GetDouble());
                var cp=f.GetProperty("componentInput");var rotation=cp.GetProperty("rotation");var relative=f.GetProperty("relativeRotation");var movement=f.GetProperty("movement");
                var component=new AlsPrecisePose(V(cp.GetProperty("position")),new(rotation[0].GetDouble(),rotation[1].GetDouble(),rotation[2].GetDouble(),rotation[3].GetDouble()),AlsDoubleVector.One);
                var r=new AlsQuaternion(relative[0].GetDouble(),relative[1].GetDouble(),relative[2].GetDouble(),relative[3].GetDouble());
                var m=new AlsStopMovementSnapshot(V(movement.GetProperty("lastUpdateVelocity")),movement.GetProperty("separate").GetBoolean(),movement.GetProperty("brakingFriction").GetSingle(),movement.GetProperty("groundFriction").GetSingle(),movement.GetProperty("factor").GetSingle(),movement.GetProperty("deceleration").GetSingle());
                string? first=null;var old=fi%97==0?LyraMainLocomotionHostSmoke.Snapshot(host):null;
                for(var attempt=0;attempt<2;attempt++)
                {
                    runtime.Begin(id,delta);
                    var c=host.Prepare(input,delta,visit,-1,component,r,m,f.GetProperty("groundDistance").GetDouble(),updateLeftHand:true,updateAdditives:true,timeSinceFired:99,skeletalSettings:new(false,false),montageFrame:runtime.Frame);
                    var slots=host.SlotTraversal!;Require(c.Macro.Observation.Frame==fi,"Duplicate Main macro update.");
                    var selected=c.Caches!.Locomotion;Require(c.Machine.Visited==(selected is not null),"Slot gate did not reach LocomotionSM.");
                    if(selected is {} selectedContext)Exact(c.Machine.Weight,selectedContext.Weight,"Main selected deferred weight.");
                    Require(c.Aiming!.Visit.Visited==(slots.AimingSource is not null),"HitReact gate did not reach Aiming.");
                    if(slots.AimingSource is {} aim)Exact(c.Aiming.Visit.Weight,aim.Weight,"Aiming inherited Slot weight.");
                    if(slots.FullBodySource is null)
                    {
                        Require(!c.Machine.Visited&&!c.Aiming.Visit.Visited&&!c.Sources.Visits.Any(v=>v.Visited)&&c.Sources.Players.Length==0,"Covered Main still enrolled source players.");
                        if(attempt==1&&visit.Visited)covered++;
                    }
                    if(slots.AdditivesSource is {} add)
                    {
                        foreach(var u in c.Additives!.Updates)Require(u.Weight<=add.Weight+1e-6,"Recovery inherited outer weight instead of FullBody source.");
                    }
                    Require(c.Skeletal!.Update.Input.Visited==visit.Visited,"FullBody Slot incorrectly hid outer skeletal update.");
                    var sig=JsonSerializer.Serialize(new{c.Machine,c.Sources.Visits,c.Sources.Players,c.Sources.Samples,c.Aiming,c.Additives,c.Caches,slotVisits=slots.Visits});
                    foreach(var command in q.GetProperty("commands").EnumerateArray())
                    {
                        var asset=command.GetProperty("asset").GetInt32();
                        if(command.GetProperty("stop").GetBoolean())
                        {
                            var explicitStop=command.TryGetProperty("instanceStop",out var stop)&&stop.GetBoolean();
                            var active=runtime.Candidate.ToArray().LastOrDefault(v=>v.MontageId==asset&&(explicitStop||v.OwnsActiveActionLookup));
                            if(active.InstanceId>0)Require(runtime.StopInstance(active.InstanceId,command.GetProperty("blend").GetSingle(),catalog.Definitions[asset].Lifecycle.BlendOutOption),"Host stop rejected.");
                        }
                        else Require(runtime.PlayAction(asset,command.GetProperty("rate").GetSingle(),command.GetProperty("start").GetSingle(),stopGroup:command.GetProperty("stopGroup").GetBoolean()),"Host play rejected.");
                    }
                    sig+=JsonSerializer.Serialize(runtime.Candidate.ToArray());
                    host.ValidateCommit(c,updateOnly:true);runtime.ValidateCommit(id);
                    if(attempt==0)
                    {
                        first=sig;host.Cancel();runtime.Discard();retries++;
                        if(old is not null)Require(old==LyraMainLocomotionHostSmoke.Snapshot(host),"Cancelled Main history published.");
                    }
                    else
                    {
                        Require(first==sig,"Main/Montage/Sync cancellation replay changed.");
                        players+=c.Sources.Players.Length;locHidden+=!c.Machine.Visited?1:0;visited+=c.Machine.Visited?1:0;
                        aimActive+=c.Aiming.Active.Count(v=>v);additiveActive+=c.Additives!.Updates.Length;
                        host.Commit(c,updateOnly:true);runtime.Commit(id);
                    }
                }
                frames++;fi++;
            }
        }
        Require(frames==40320&&retries==frames&&covered>0&&locHidden>covered&&visited>0&&players>0&&aimActive>0&&additiveActive>0,"Incomplete real Main update coverage.");
        GD.Print($"LYRA_MAIN_SLOTS_HOST_GODOT_OK frames={frames} retry={retries} covered={covered} hiddenLocomotion={locHidden} visitedLocomotion={visited} players={players} aiming={aimActive} additives={additiveActive} commonSync=true realHost=true nativeWholeMain=false pose=false production=false");
    }

}
