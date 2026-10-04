using System.Text.Json;
using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraMainInertiaDiagnosticSmoke:Node
{
    private static void Require(bool value,string label){if(!value)throw new InvalidOperationException(label);}
    public override void _Ready(){try{Run();GetTree().Quit();}catch(Exception e){GD.PushError("Main Slot composition failed: "+e);GetTree().Quit(1);}}
    private static string Signature(LyraCompositionPoseBuffer b)=>JsonSerializer.Serialize(new{b.Pose,b.Curves,b.Attributes,b.RootMotion});
    private static void Run()
    {
        const string root="res://assets/generated/lyra_als/";
        var prefix=OS.GetEnvironment("LYRA_INERTIA_DIAGNOSTIC_PREFIX");if(prefix.Length==0)prefix="main_inertia_debug_v1";
        Require(prefix is "main_inertia_debug_v1" or "main_inertia_stages_v2","Unknown diagnostic capture.");
        using var requests=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+prefix+"_requests.json"));
        using var native=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+prefix+"_native.json"));
        using var probes=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"cycle_layer_pose_native_v2.json"));
        var data=native.RootElement;Require(data.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+prefix+"_requests.json")),"Stale Main Slot composition requests.");
        foreach(var d in data.GetProperty("dependencies").EnumerateObject())Require(d.Value.GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+d.Name)),"Stale Main Slot composition dependencies.");
        var bank=LyraLogicalSourceBank.Load(includeMainLean:true,includeLocomotionExtras:true,includeMontageActions:true);var catalog=new LyraMontageCatalog();
        using var sampler=new LyraMontageTrackSampler(bank,catalog);var graphs=LyraMainLayerGraphCatalog.Load();
        using var recoveryPolicy=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"additives_layer_v1_policy.json"));
        var rootData=JsonSerializer.SerializeToElement(new{schemaVersion=1,assets=recoveryPolicy.RootElement.GetProperty("resources").EnumerateObject()
            .ToDictionary(p=>p.Value.GetProperty("path").GetString()!,p=>p.Value.GetProperty("compressedRoot"))});
        using var recoveryRoots=LyraCompressedRootBank.Load(rootData,bank);
        var expected=data.GetProperty("traces").EnumerateArray().Sum(t=>t.GetProperty("frames").EnumerateArray().Sum(f=>f.GetProperty("outputs").GetArrayLength()));
        var expectedAttrs=data.GetProperty("traces").EnumerateArray().Sum(t=>t.GetProperty("frames").EnumerateArray().Sum(f=>f.GetProperty("outputs").EnumerateArray().Sum(o=>o.GetProperty("attributes").GetArrayLength())));
        var comparison=new LyraCycleLayerPoseComparison(bank,probes.RootElement,expectedFrames:expected*2,expectedAttributes:expectedAttrs*2,stage:"OriginalMainInertia");
        var diagnosticFailures=0;var diagnostic=new List<object>();JsonElement diagnosticInput=default;var frames=0;var poses=0;var covered=0;var roots=0;var retries=0;var faults=0;var rejected=0;var caches=0;
        double rootP=0,rootQ=0,rootS=0;
        foreach(var (trace,ti) in data.GetProperty("traces").EnumerateArray().Select((v,i)=>(v,i)))
        {
            var committedInertia=new LyraMainInertialization(bank);var candidateInertia=new LyraMainInertialization(bank);
            var profile=trace.GetProperty("profile").GetString()!;Require(trace.GetProperty("proxySkeletonMatchesTarget").GetBoolean(),"Foreign native Skeleton proxy.");
            using(var maskPolicy=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"main_composition_v2_policy.json")))Require(trace.GetProperty("mask").EnumerateArray().Select(v=>v.GetSingle()).SequenceEqual(maskPolicy.RootElement.GetProperty("policies").GetProperty(profile).GetProperty("mask").EnumerateArray().Select(v=>v.GetSingle())),"Native Main mask mismatch.");var runtime=catalog.CreateRuntime();var slots=new LyraMainSlotsTraversal(runtime);var cache=new LyraMainPoseCacheTraversal(graphs,profile);
            using var composition=new LyraMainSlotComposition(bank,catalog,profile);
            var recovery=bank.CreateSampler(profile+"_jump_recovery_additive");var basis=new LyraCompositionPoseBuffer(bank);var additive=new LyraCompositionPoseBuffer(bank);var output=new LyraCompositionPoseBuffer(bank);
            var recoveryDefinition=bank.Get(profile+"_jump_recovery_additive");
            var recoveryRoot=recoveryRoots.CreateSampler(recoveryDefinition.Slot,bank.Reference[0],recoveryDefinition.NormalizedRootMotionScale);
            foreach(var (row,fi) in trace.GetProperty("frames").EnumerateArray().Select((v,i)=>(v,i)))
            {
                var q=requests.RootElement.GetProperty("traces")[ti].GetProperty("frames")[fi];var id=new AlsFrameIdentity(fi,(uint)(ti+71),29);string? first=null;
                for(var attempt=0;attempt<2;attempt++)
                {
                    candidateInertia.CopyFrom(committedInertia);if(q.GetProperty("initialize").GetBoolean())candidateInertia.Reset();
                    if(q.GetProperty("visited").GetBoolean()){candidateInertia.Update(q.GetProperty("delta").GetSingle());foreach(var request in q.GetProperty("inertia").EnumerateArray())candidateInertia.Request(request.GetSingle());}
                    runtime.Begin(id,q.GetProperty("delta").GetSingle());var context=new AlsPoseUpdateContext(id,q.GetProperty("weight").GetSingle(),q.GetProperty("delta").GetSingle(),q.GetProperty("rootModifier").GetSingle()).WithInertialization(75,true);
                    if(!q.GetProperty("active").GetBoolean())context=context.AsInactive();
                    slots.Begin(runtime.Frame,context,q.GetProperty("visited").GetBoolean(),q.GetProperty("initialize").GetBoolean(),q.GetProperty("dynamicWeight").GetSingle());
                    var readers=new List<LyraMainCacheUpdate>();var blend=q.GetProperty("aimBlend").GetSingle();
                    if(slots.AimingSource is {} aim)
                    {
                        var a=blend<1-AlsPoseBlender.WeightThreshold;var b=blend>AlsPoseBlender.WeightThreshold;
                        if(a)readers.Add(new(76,a&&b?aim.WithWeight(aim.Weight*(1-blend)):aim));
                        if(b)readers.Add(new(75,a&&b?aim.WithWeight(aim.Weight*blend):aim));
                    }
                    var updates=cache.Resolve(id,readers.ToArray(),slots);var label=$"MainInertia/{ti}/{fi}";var signature=JsonSerializer.Serialize(slots.Visits)+JsonSerializer.Serialize(updates);
                    if(q.GetProperty("sample").GetBoolean())
                    {
                        var ai=q.GetProperty("basis").GetInt32();var a=catalog.Definitions[ai];
                        var record=new AlsMontageEvaluation(1,a.AnimationId,a.Slot,new AlsMontageTrack(a.AnimationId,a.Slot,a.ClipStart,a.ClipRate,a.AdditiveType){ClipEnd=a.ClipEnd}.SamplePosition(.27f),1,ai)
                        {MontagePosition=.27f,AdditiveType=a.AdditiveType,DeltaTimeRecord=new(.233f,.037f)};
                        sampler.Sample(record,catalog.Metadata[ai].GetProperty("rootMotion").GetBoolean(),basis);
                        var di=bank.Curves.Index("Distance");basis.Curves[di]=new(basis.Curves[di].Value,true,q.GetProperty("curveFlags").GetUInt32());
                        recovery.Sample(.27f,additive.Pose,additive.Curves,additive.Attributes);
                        additive.RootMotion=LyraRootMotionAttribute.Sample(recoveryDefinition,recoveryRoot,.233f,.037f,true);
                        var unchanged=Signature(basis)+Signature(additive);var before=Signature(output);
                        var cp=q.GetProperty("component");var yaw=q.GetProperty("componentYaw").GetDouble()*Math.PI/180d/2d;
                        var component=new AlsPrecisePose(new(cp[0].GetDouble(),cp[1].GetDouble(),cp[2].GetDouble()),new(0,0,Math.Sin(yaw),Math.Cos(yaw)),AlsDoubleVector.One);
                        void Evaluate(Action<LyraCompositionPoseBuffer>? injected=null)=>composition.Evaluate(runtime.Frame,id,q.GetProperty("dynamicWeight").GetSingle(),q.GetProperty("rootYaw").GetSingle(),
                            injected??(d=>d.Copy(basis.Input)),(input,d)=>d.Copy(input.Input),d=>d.Copy(additive.Input),output,(input,d)=>{if(OS.GetEnvironment("LYRA_INERTIA_NATIVE_INPUT")=="1")
                            {var nativeInput=row.GetProperty("inertiaInput")[0];for(var b=0;b<81;b++){var rotation=nativeInput.GetProperty("pose")[b].GetProperty("rotation");input.Pose[b]=input.Pose[b] with{Rotation=new(rotation[0].GetDouble(),rotation[1].GetDouble(),rotation[2].GetDouble(),rotation[3].GetDouble())};}}
                            diagnosticInput=JsonSerializer.SerializeToElement(new{pose=input.Pose.ToArray(),curves=input.Curves.ToArray()});candidateInertia.Evaluate(input.Input,component,0,500,d);});
                        if(fi%137==0&&slots.FullBodySource is not null)
                        {
                            try{Evaluate(_=>throw new InvalidOperationException("Injected Main source failure."));throw new Exception("Source failure ignored.");}
                            catch(InvalidOperationException e)when(e.Message=="Injected Main source failure."){faults++;}
                            Require(before==Signature(output),label+"/failed evaluation published output");
                        }
                        string? previous=null;
                        for(var pass=0;pass<2;pass++)
                        {
                            Evaluate();
                            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                            var kernel=typeof(AlsPoseDataInertialization).GetField("_kernel",flags)!.GetValue(candidateInertia.Runtime)!;
                            var bones=(Array)typeof(AlsInertialization).GetField("_bones",flags)!.GetValue(kernel)!;
                            if(attempt==1)diagnostic.Add(new{fi,pass,upper=((LyraCompositionPoseBuffer[])typeof(LyraMainSlotComposition).GetField("_inputs",flags)!.GetValue(composition)!)[2].Pose.ToArray(),basis=basis.Pose.ToArray(),additive=additive.Pose.ToArray(),input=diagnosticInput,bones=JsonSerializer.SerializeToElement(bones,new JsonSerializerOptions{IncludeFields=true}),candidateInertia.Elapsed,candidateInertia.Duration,pose=output.Pose.ToArray()});
                            try{comparison.Compare(row.GetProperty("outputs")[pass],output.Pose,output.Curves,output.Attributes,label);}catch(InvalidOperationException e){if(diagnosticFailures++<10)GD.Print(e.Message);}
                            var o=row.GetProperty("outputs")[pass];var present=o.TryGetProperty("rootMotion",out var rm);Require(present==output.RootMotion.Present,label+"/root presence");
                            if(present)
                            {
                                var e=LyraLogicalSourceBank.ParsePose(rm);var v=output.RootMotion.Value;var sign=AlsQuaternion.Dot(e.Rotation,v.Rotation)<0?-1:1;
                                var p=(e.Position-v.Position).LengthSquared;var r=(e.Rotation+v.Rotation*-sign).LengthSquared;var s=(e.Scale-v.Scale).LengthSquared;
                                rootP=Math.Max(rootP,Math.Sqrt(p));rootQ=Math.Max(rootQ,Math.Sqrt(r));rootS=Math.Max(rootS,Math.Sqrt(s));Require(p<=1e-16&&r<=1e-20&&s<=1e-24,$"{label}/root TRS P={Math.Sqrt(p):R} Q={Math.Sqrt(r):R} S={Math.Sqrt(s):R} actual={JsonSerializer.Serialize(v)} expected={rm}");roots++;
                            }
                            int[] actual=[composition.LastInputEvaluations,composition.LastSplitEvaluations,composition.LastLocomotionEvaluations];
                            for(var n=0;n<3;n++)Require(actual[n]*2==row.GetProperty("cacheEvaluations")[n].GetInt32(),label+"/cache source evaluations");
                            for(var n=0;n<5;n++)Require(composition.SourceEvaluations(n)*2==row.GetProperty("sourceEvaluations")[n].GetInt32(),label+"/Slot source evaluations");
                            Require(unchanged==Signature(basis)+Signature(additive),label+"/input mutation");var current=Signature(output);
                            previous=current;
                            signature+=current;if(attempt==1){poses++;caches+=actual.Sum();covered+=actual[2]==0?1:0;}
                        }
                        try{composition.Evaluate(runtime.Frame,new(fi,id.CharacterId,30),0,0,d=>d.Copy(basis.Input),(i,d)=>d.Copy(i.Input),d=>d.Copy(additive.Input),output,(input,d)=>{if(OS.GetEnvironment("LYRA_INERTIA_NATIVE_INPUT")=="1")
                            {var nativeInput=row.GetProperty("inertiaInput")[0];for(var b=0;b<81;b++){var rotation=nativeInput.GetProperty("pose")[b].GetProperty("rotation");input.Pose[b]=input.Pose[b] with{Rotation=new(rotation[0].GetDouble(),rotation[1].GetDouble(),rotation[2].GetDouble(),rotation[3].GetDouble())};}}
                            diagnosticInput=JsonSerializer.SerializeToElement(new{pose=input.Pose.ToArray(),curves=input.Curves.ToArray()});candidateInertia.Evaluate(input.Input,component,0,500,d);});throw new Exception("Foreign identity accepted.");}catch(InvalidOperationException){rejected++;}
                    }
                    foreach(var command in q.GetProperty("commands").EnumerateArray())
                    {
                        var asset=command.GetProperty("asset").GetInt32();
                        if(command.GetProperty("stop").GetBoolean())
                        {
                            var explicitStop=command.TryGetProperty("instanceStop",out var stop)&&stop.GetBoolean();var active=runtime.Candidate.ToArray().LastOrDefault(v=>v.MontageId==asset&&(explicitStop||v.OwnsActiveActionLookup));
                            if(active.InstanceId>0)Require(runtime.StopInstance(active.InstanceId,command.GetProperty("blend").GetSingle(),catalog.Definitions[asset].Lifecycle.BlendOutOption),"Stop rejected.");
                        }
                        else Require(runtime.PlayAction(asset,command.GetProperty("rate").GetSingle(),command.GetProperty("start").GetSingle(),stopGroup:command.GetProperty("stopGroup").GetBoolean()),"Play rejected.");
                    }
                    signature+=JsonSerializer.Serialize(runtime.Candidate.ToArray());
                    if(attempt==0){first=signature;slots.Cancel();runtime.Discard();retries++;}
                    else{Require(first==signature,label+"/late cancellation changed complete output");slots.ValidateCommit(id);runtime.ValidateCommit(id);slots.Commit(id);runtime.Commit(id);committedInertia.CopyFrom(candidateInertia);}
                }
                frames++;
            }
        }
        System.IO.File.WriteAllText(ProjectSettings.GlobalizePath("res://artifacts/lyra-analysis/main-inertia-godot-debug-data.json"),JsonSerializer.Serialize(diagnostic));Require(frames==data.GetProperty("traces").EnumerateArray().Sum(t=>t.GetProperty("frames").GetArrayLength())&&poses==expected&&covered>0&&retries==frames&&faults>0&&roots>0,"Incomplete Main Slot composition coverage.");
        GD.Print($"LYRA_MAIN_INERTIA_DIAGNOSTIC_DONE failures={diagnosticFailures} frames={frames} poses={poses} covered={covered} caches={caches} retry={retries} faults={faults} rejected={rejected} roots={roots} rootP={rootP:R} rootQ={rootQ:R} rootS={rootS:R} fullChannels=true originalNodes=true nativeWholeMain=false production=false");
    }
}
