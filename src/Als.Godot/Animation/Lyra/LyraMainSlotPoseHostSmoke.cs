using System.Text.Json;
using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
namespace GodotAls.Animation.Lyra;
public partial class LyraMainSlotPoseHostSmoke:Node
{
    private static void Require(bool ok,string label){if(!ok)throw new InvalidOperationException(label);}
    private static void Exact(float a,float b,string label)=>Require(BitConverter.SingleToInt32Bits(a)==BitConverter.SingleToInt32Bits(b),label);
    private sealed class PlaneGround:IAlsFootGroundQuery
    {public AlsFootGroundHit Sweep(int leg,in AlsFootTraceQuery query)=>default;}
    private static string Snapshot(LyraMainLocomotionHost h)=>JsonSerializer.Serialize(new{main=LyraMainLocomotionHostSmoke.Snapshot(h),h.Layers.AimWeights,h.Layers.AimingNodes,h.Layers.AdditivesState,h.Layers.AdditivesElapsed,h.Layers.SkeletalHistory,h.Layers.SkeletalUpdate,
        curves=h.Sources.MainFeedback});
    public override void _Ready(){try{Run();GetTree().Quit();}catch(Exception e){GD.PushError("Main Slot pose host failed: "+e);GetTree().Quit(1);}}
    private static void Run()
    {
        using var resources=new LyraLocomotionResources(includeMontageActions:true);var bank=resources.Catalog.Bank;var catalog=new LyraMontageCatalog();
        using var observations=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/main_als_locomotion_v1_requests.json"));
        using var requests=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/main_slots_v1_requests.json"));
        var frames=0;var covered=0;var locHidden=0;var visited=0;var retries=0;var players=0;var aimActive=0;var additiveActive=0;
        var poses=0;var feedback=0;var fullChecks=0;var initialChecks=0;var repeated=0;var rejected=0;
        using var sampler=new LyraMontageTrackSampler(bank,catalog);var evaluator=new LyraMontageSlotPose(bank,catalog,sampler);
        var full=new LyraCompositionPoseBuffer(bank);var root=new LyraCompositionPoseBuffer(bank);
        // First trace per configuration is the 64-second physical timeline.
        // The separate clock arithmetic traces (including 1090-second deltas)
        // remain fully exercised by the native Slot/cache component test.
        foreach(var trace in requests.RootElement.GetProperty("traces").EnumerateArray()
            .GroupBy(t=>(t.GetProperty("profile").GetString(),t.GetProperty("hz").GetInt32())).Select(g=>g.First()))
        {
            var profile=trace.GetProperty("profile").GetString()!;var hz=trace.GetProperty("hz").GetInt32();var runtime=catalog.CreateRuntime();
            Require(trace.GetProperty("frames").GetArrayLength()==hz*64&&trace.GetProperty("frames").EnumerateArray().All(f=>f.GetProperty("delta").GetSingle()==1f/hz),"Changed physical Main timeline.");
            using var host=new LyraMainPoseHost(resources,profile,700,1700,7,montageRuntime:runtime,montageCatalog:catalog,enableMainInertia:false);
            using var baseline=new LyraMainPoseHost(resources,profile,700,1700,7,enableMainInertia:false);
            host.EnterRootPhases(0);baseline.EnterRootPhases(0);
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
                string? first=null;var old=fi%97==0?Snapshot(host.Main):null;
                for(var attempt=0;attempt<2;attempt++)
                {
                    runtime.Begin(id,delta);
                    var character=new AlsFootCharacterInput(component,input.Observation.Ground,input.Observation.Ground,new(0,0,0),new(0,0,1),input.Observation.Velocity);
                    var candidate=host.Prepare(input,delta,visit,character,r,m,f.GetProperty("groundDistance").GetDouble(),99,new(false,false),montageFrame:runtime.Frame);
                    var c=candidate.Main;
                    var slots=host.Main.SlotTraversal!;Require(c.Macro.Observation.Frame==fi,"Duplicate Main macro update.");
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
                    LyraMainPoseView pose=default;
                    if(visit.Visited)
                    {
                        pose=host.Evaluate(candidate,new PlaneGround());
                        var saved=JsonSerializer.Serialize(new{Pose=pose.Pose.ToArray(),Curves=pose.Curves.ToArray(),Attributes=pose.Attributes.ToArray(),pose.RootMotion});
                        Require(host.LastLocomotionEvaluations==(c.Machine.Visited?1:0),"Main evaluated a covered Locomotion cache.");
                        if(slots.FullBodySource is null)
                        {
                            Require(host.LastSplitEvaluations==0&&host.LastInputEvaluations==0,"FullBody coverage still evaluated lower caches.");
                            evaluator.Evaluate(runtime.Frame,id,new(4),default,full);
                            LyraMainCompositionOperators.RotateRoot(full.Input,(float)c.Macro.State.RootYaw,root);
                            var independent=host.Main.Layers.EvaluateSkeletal(c.Sources,c.Skeletal!,host.Main.Layers.Call(LyraLayerHook.FullBody_SkeletalControls),root.Input,character,new PlaneGround());
                            Require(pose.Pose.SequenceEqual(independent.Pose)&&pose.Curves.SequenceEqual(independent.Curves)&&pose.Attributes.SequenceEqual(independent.Attributes)&&pose.RootMotion==independent.RootMotion,"Covered Main disagrees with standalone Slot/root/skeletal chain.");
                            if(attempt==1)fullChecks++;
                            try{host.Main.StageFinalFeedback(c,pose.Curves);throw new Exception("Hidden locomotion accepted feedback without enclosing Slot proof.");}catch(InvalidOperationException){rejected++;}
                        }
                        if(fi==0)
                        {
                            var b=baseline.Prepare(input,delta,visit,character,r,m,f.GetProperty("groundDistance").GetDouble(),99,new(false,false));
                            var reference=baseline.Evaluate(b,new PlaneGround());Require(pose.Pose.SequenceEqual(reference.Pose)&&pose.Curves.SequenceEqual(reference.Curves)&&pose.Attributes.SequenceEqual(reference.Attributes)&&pose.RootMotion==reference.RootMotion,"Empty Montage bank changed established Main output.");
                            baseline.Cancel();initialChecks++;
                        }
                        if(fi%97==0)
                        {
                            var stale=pose;pose=host.Evaluate(candidate,new PlaneGround());
                            Require(saved==JsonSerializer.Serialize(new{Pose=pose.Pose.ToArray(),Curves=pose.Curves.ToArray(),Attributes=pose.Attributes.ToArray(),pose.RootMotion}),"Repeated Main Slot pose changed.");
                            try{_ = stale.Pose.Length;throw new Exception("Superseded Main pose remained valid.");}catch(InvalidOperationException){rejected++;}
                            repeated++;
                        }
                        try{host.ValidateCommit(candidate);throw new Exception("Main Slot committed without final feedback.");}catch(InvalidOperationException){rejected++;}
                        host.StageFinalFeedback(candidate);
                        for(var k=0;k<bank.Curves.Names.Length;k++)Require(float.IsFinite(pose.Curves[k].Value),"Nonfinite Slot feedback.");
                        sig+=saved;
                        if(attempt==1){poses++;feedback+=pose.Curves.Length;}
                    }
                    else
                    {
                        try{host.Evaluate(candidate,new PlaneGround());throw new Exception("Hidden enclosing Main evaluated.");}catch(InvalidOperationException){rejected++;}
                    }
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
                    host.ValidateCommit(candidate,updateOnly:!visit.Visited);runtime.ValidateCommit(id);
                    if(attempt==0)
                    {
                        first=sig;host.Cancel();runtime.Discard();retries++;
                        if(old is not null)Require(old==Snapshot(host.Main),"Cancelled Main history published.");
                    }
                    else
                    {
                        Require(first==sig,"Main/Montage/Sync cancellation replay changed.");
                        players+=c.Sources.Players.Length;locHidden+=!c.Machine.Visited?1:0;visited+=c.Machine.Visited?1:0;
                        aimActive+=c.Aiming.Active.Count(v=>v);additiveActive+=c.Additives!.Updates.Length;
                        var finalCurves=visit.Visited?pose.Curves.ToArray():null;
                        host.Commit(candidate,updateOnly:!visit.Visited);runtime.Commit(id);
                        if(finalCurves is not null)for(var k=0;k<bank.Curves.Names.Length;k++)Require(host.Main.Layers.CommittedCurve(bank.Curves.Names[k])==(finalCurves[k].Present?finalCurves[k].Value:0),"Final Slot curve feedback was not committed.");
                    }
                }
                frames++;fi++;
            }
        }
        Require(frames==40320&&retries==frames&&covered>0&&locHidden>covered&&visited>0&&players>0&&aimActive>0&&additiveActive>0&&poses>0&&fullChecks==covered&&initialChecks==18&&repeated>0,"Incomplete real Main update coverage.");
        GD.Print($"LYRA_MAIN_SLOT_POSE_HOST_GODOT_OK frames={frames} poses={poses} feedbackCurves={feedback} fullChecks={fullChecks} initialChecks={initialChecks} repeated={repeated} rejected={rejected} retry={retries} covered={covered} hiddenLocomotion={locHidden} visitedLocomotion={visited} players={players} aiming={aimActive} additives={additiveActive} commonSync=true realHost=true nativeWholeMain=false pose=true fullChannels=true production=false");
    }

}
