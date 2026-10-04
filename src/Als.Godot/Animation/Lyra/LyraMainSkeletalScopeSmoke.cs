using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

// All fourteen typed entries share a fixed Provider, source batch and transaction.
// PreAim/PreSkeletal poses remain controlled boundaries: complete Main upper-body,
// Slots, additive application and final root adjustment are separate open gates.
public partial class LyraMainSkeletalScopeSmoke : Node
{
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private sealed class PlaneGround(AlsFootPlane plane):IAlsFootGroundQuery
    {
        public AlsFootGroundHit Sweep(int leg,in AlsFootTraceQuery query)
        {
            var denominator=AlsDoubleVector.Dot(plane.Normal,query.Direction);
            if(Math.Abs(denominator)<1e-8)return default;
            var t=(plane.W+query.Radius-AlsDoubleVector.Dot(plane.Normal,query.Start))/denominator;
            return new(t>=query.StartOffset&&t<=query.EndOffset,query.Start+query.Direction*t-plane.Normal*query.Radius,plane.Normal);
        }
    }
    private sealed class FailedGround:IAlsFootGroundQuery
    {public AlsFootGroundHit Sweep(int leg,in AlsFootTraceQuery query)=>throw new InvalidOperationException("Injected shared-frame query failure.");}
    public override void _Ready(){try{Run();GetTree().Quit();}catch(Exception error){GD.PushError("Main SkeletalControls scope failed: "+error);GetTree().Quit(1);}}
    private static void Run()
    {
        using var resources=new LyraLocomotionResources();var bank=resources.Catalog.Bank;
        using var requests=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/main_als_locomotion_v1_requests.json"));
        var frames=0;var poses=0;var sources=0;var samples=0;var late=0;var rejected=0;var changes=0;var mixed=0;var hidden=0;var curveDriven=0;var footActive=0;var footPartial=0;var previousControls=0;var faults=0;
        void Reject(Action action,string label)
        {try{action();}catch(InvalidOperationException){rejected++;return;}throw new InvalidOperationException("Accepted invalid Aiming invocation: "+label);}
        string Snapshot(LyraMainLocomotionHost h)=>JsonSerializer.Serialize(new{
            main=LyraMainLocomotionHostSmoke.Snapshot(h),h.Layers.AimWeights,h.Layers.AimingNodes,
            hipCurve=h.Layers.CommittedCurve("applyHipfireOverridePose"),h.Layers.AdditivesState,h.Layers.AdditivesElapsed,h.Layers.SkeletalHistory,h.Layers.SkeletalUpdate});
        foreach(var trace in requests.RootElement.GetProperty("traces").EnumerateArray())
        {
            var profile=trace.GetProperty("profile").GetString()!;
            var clean=resources.CreateMainHost(profile,700,1700,7);var retry=resources.CreateMainHost(profile,700,1700,7);
            var foreign=resources.CreateMainHost(profile,700,1700,7);
            // Controlled component hosts have no enclosing Main phase owner.
            // Execute the actual layer's required startup before its solver.
            foreach(var component in new[]{clean,retry,foreign})foreach(var layer in component.LayerInstances)
            {var counter=new AlsGraphTraversalCounter(1,0);layer.Phases.Initialize(LyraLayerHook.FullBody_SkeletalControls,counter,_=>{});layer.Phases.CacheBones(LyraLayerHook.FullBody_SkeletalControls,counter,_=>{});}
            var foreignBefore=Snapshot(foreign);var i=0;
            foreach(var frame in trace.GetProperty("frames").EnumerateArray())
            {
                var label=$"MainSkeletal/{profile}/{trace.GetProperty("hz")}/{i}";
                var input=LyraMainUpdateSmoke.ReadInput(frame.GetProperty("observation"));var delta=frame.GetProperty("delta").GetSingle();
                // The movement fixture keeps aim pitch at zero. Add an actual
                // pitch sweep so the combined roster also visits AO edges and
                // multiple samples, rather than only its centre row.
                input=input with{AimPitch=i%29==0?new[]{-90d,0,90}[(i/29)%3]:Math.Sin(i*delta*2.1)*110};
                var visit=new LyraLocomotionMachineVisit(frame.GetProperty("active").GetBoolean(),frame.GetProperty("weight").GetSingle(),
                    frame.GetProperty("reinitialize").GetBoolean(),frame.GetProperty("contextActive").GetBoolean());
                var component=frame.GetProperty("componentInput");var q=component.GetProperty("rotation");var rel=frame.GetProperty("relativeRotation");var move=frame.GetProperty("movement");
                AlsDoubleVector V(JsonElement v)=>new(v[0].GetDouble(),v[1].GetDouble(),v[2].GetDouble());
                var transform=new AlsPrecisePose(V(component.GetProperty("position")),new(q[0].GetDouble(),q[1].GetDouble(),q[2].GetDouble(),q[3].GetDouble()),AlsDoubleVector.One);
                var physical=new AlsStopMovementSnapshot(V(move.GetProperty("lastUpdateVelocity")),move.GetProperty("separate").GetBoolean(),
                    move.GetProperty("brakingFriction").GetSingle(),move.GetProperty("groundFriction").GetSingle(),move.GetProperty("factor").GetSingle(),move.GetProperty("deceleration").GetSingle());
                var relative=new AlsQuaternion(rel[0].GetDouble(),rel[1].GetDouble(),rel[2].GetDouble(),rel[3].GetDouble());
                var fired=new[]{.49999999999999994,.5,.5000000000000001,99}[(i/31)%4];var feedback=(i/23)%4==0?.125f:0;
                LyraMainLocomotionCandidate Prepare(LyraMainLocomotionHost h)=>h.Prepare(input,delta,visit,-1,transform,relative,physical,
                    frame.GetProperty("groundDistance").GetDouble(),updateLeftHand:true,updateAdditives:true,timeSinceFired:fired,skeletalSettings:new((i/17)%2==0,(i/37)%4!=0));
                var check=i%59==0;var old=check?Snapshot(retry):null;
                var a=Prepare(clean);var b=Prepare(retry);var aiming=a.Aiming??throw new InvalidOperationException(label+"/missing Aiming entry");
                Require(b.Aiming is not null && a.LeftHand is not null && a.Additives is not null && a.Skeletal is not null && b.Skeletal is not null,label+"/same group entries");
                if(a.Skeletal!.Update.Input.Feedback!=default)previousControls++;
                if(a.Skeletal.Update.Updated.Alphas[5]>AlsPoseBlender.WeightThreshold)footActive++;
                if(a.Skeletal.Update.Updated.Alphas[5] is >0 and <1)footPartial++;
                // Linked function parameters propagate only when its Update is visited.
                // Hidden visits must retain the committed parameter values.
                var expectedAim=aiming.Visit.Visited?new LyraFullBody_AimingParameters(a.Macro.Tail.AimYaw,a.Macro.Tail.AimPitch):clean.Layers.WorkerAimParameters;
                Require(aiming.Pins.Yaw==(float)expectedAim.AimYaw&&aiming.Pins.Pitch==(float)expectedAim.AimPitch,label+"/visited or retained Main exposed inputs before deferred traversal");
                var state=a.Macro.State;
                var suppress=profile!="rifle"&&state.Crouching || !state.Crouching&&state.Ads&&state.Ground;
                if(clean.Layers.CommittedCurve("applyHipfireOverridePose")>0&&!suppress)
                {Require(aiming.Weights.Weights==new LyraAimWeights(1,1),label+"/previous enclosing Main curve drives provider");curveDriven++;}
                Require(a.Sources.Players.SequenceEqual(b.Sources.Players)&&a.Sources.Samples.SequenceEqual(b.Sources.Samples)&&
                    clean.PreparedTicks(a).SequenceEqual(retry.PreparedTicks(b)),label+"/common Sync retry");
                var expected=aiming.Active[0]&&aiming.Active[1]?Math.Max(aiming.Nodes[0].Weight,aiming.Nodes[1].Weight):visit.Weight;
                Require(a.Machine.Weight==expected,label+"/deferred cache winning context");
                var ao=a.Sources.Players.Where(p=>p.PlayerId is 779 or 774).ToArray();
                Require(ao.Length==aiming.Active.Count(v=>v),label+"/single AO roster");
                for(var n=0;n<ao.Length;n++)Require(a.Sources.Players[n]==ao[n]&&a.Sources.Groups[n]==-1,label+"/original AO-before-input order");
                Require(a.Sources.Players.Select(p=>p.PlayerId).Distinct().Count()==a.Sources.Players.Length &&
                    a.Sources.Samples.Select(s=>s.SampleId).Distinct().Count()==a.Sources.Samples.Length,label+"/source occurrence collisions");
                foreach(var p in ao)
                {
                    Require(p.Kind==AlsAssetSyncKind.BlendSpace && p.Epoch==7,label+"/AO common batch identity");
                    sources++;samples+=p.SampleCount;
                }
                mixed+=aiming.Active[0]&&aiming.Active[1]?1:0;
                var evaluate=visit.Visited&&frame.GetProperty("evaluate").GetBoolean();
                LyraSkeletalControlsPoseView Evaluate(LyraMainLocomotionHost h,LyraMainLocomotionCandidate c,bool gates)
                {
                    h.Evaluate(c);var raw=new LyraLayerPoseInput(bank,h.Pose,h.Curves,h.Attributes,h.RootMotion);
                    var left=h.Layers.EvaluateLeftHand(c.Sources,c.LeftHand!,h.Layers.Call(LyraLayerHook.LeftHandPose_OverrideState),raw);
                    var preAim=new LyraLayerPoseInput(bank,left.Pose,left.Curves,left.Attributes,left.RootMotion);
                    h.Layers.EvaluateAdditives(c.Sources,c.Additives!,h.Layers.Call(LyraLayerHook.FullBodyAdditives));
                    if(gates)
                    {
                        Reject(()=>h.Commit(c),label+"/missing Aiming Evaluate");
                        void Call(LyraItemLayerGraphInstance group,LyraLayerInvocation call,LyraAimingCandidate candidate)
                        {
                            var supplied=new LyraLayerPoseInput(bank,left.Pose,left.Curves,left.Attributes,left.RootMotion);
                            group.EvaluateAiming(c.Sources,candidate,call,supplied);
                        }
                        var invocation=h.Layers.Call(LyraLayerHook.FullBody_Aiming);
                        Reject(()=>Call(h.Layers,invocation with{MainNode=invocation.MainNode+1},c.Aiming!),label+"/Main node");
                        Reject(()=>Call(h.Layers,invocation with{InstanceEpoch=8},c.Aiming!),label+"/epoch");
                        Reject(()=>Call(h.Layers,foreign.Layers.Call(LyraLayerHook.FullBody_Aiming),c.Aiming!),label+"/foreign group");
                        Reject(()=>Call(foreign.Layers,foreign.Layers.Call(LyraLayerHook.FullBody_Aiming),c.Aiming!),label+"/foreign frame");
                        Reject(()=>Call(h.Layers,invocation,c.Aiming! with{}),label+"/copied candidate");
                        Reject(()=>Call(h.Layers,h.Layers.Call(LyraLayerHook.FullBodyAdditives),c.Aiming!),label+"/signature");
                    }
                    var aimed=h.Layers.EvaluateAiming(c.Sources,c.Aiming!,h.Layers.Call(LyraLayerHook.FullBody_Aiming),preAim);
                    var character=new AlsFootCharacterInput(transform,input.Observation.Ground,input.Observation.Ground,new(0,0,Math.Sin(i*delta*1.7)*12),new(0,0,1),input.Observation.Velocity);
                    var ground=new PlaneGround(AlsFootPlane.At(character.FloorPoint,character.FloorNormal));
                    var skeletalInvocation=h.Layers.Call(LyraLayerHook.FullBody_SkeletalControls);
                    if(gates)
                    {
                        Reject(()=>h.Commit(c),label+"/missing SkeletalControls Evaluate");
                        void Call(LyraLayerInvocation call,LyraSkeletalControlsCandidate candidate)
                        {
                            h.Layers.EvaluateSkeletal(c.Sources,candidate,call,new(bank,aimed.Pose,aimed.Curves,aimed.Attributes,aimed.RootMotion),character,ground);
                        }
                        Reject(()=>Call(skeletalInvocation with{MainNode=skeletalInvocation.MainNode+1},c.Skeletal!),label+"/skeletal Main node");
                        Reject(()=>Call(skeletalInvocation with{InstanceEpoch=8},c.Skeletal!),label+"/skeletal epoch");
                        Reject(()=>Call(foreign.Layers.Call(LyraLayerHook.FullBody_SkeletalControls),c.Skeletal!),label+"/skeletal foreign group");
                        Reject(()=>Call(skeletalInvocation,c.Skeletal! with{}),label+"/skeletal copied candidate");
                        Reject(()=>Call(h.Layers.Call(LyraLayerHook.FullBody_Aiming),c.Skeletal!),label+"/skeletal signature");
                    }
                    var output=h.Layers.EvaluateSkeletal(c.Sources,c.Skeletal!,skeletalInvocation,new(bank,aimed.Pose,aimed.Curves,aimed.Attributes,aimed.RootMotion),character,ground);
                    var final=output.Curves.ToArray();var legId=bank.Curves.Names.IndexOf("DisableLegIK");Require(legId>=0,label+"/original leg curve");
                    final[legId]=new(new[]{-.25f,0,.25f,.75f,1f}[(i/19)%5],true);
                    h.StageFinalFeedback(c,final,[new("applyHipfireOverridePose",new(feedback,true)),new("DisableRHandIK",new((i/11)%3*.25f,true)),
                        new("DisableLHandIK",new((i/13)%4*.25f,true)),new("DisableHandIKRetargeting",new((i/23)%3*.25f,true)),new("ScaleDownWeaponR",new((i/17)%3*.005f,true))]);
                    return output;
                }
                LyraSkeletalControlsPoseView? output=null;AlsPrecisePose[]? savedPose=null;LyraCurveSample[]? savedCurves=null;LyraAttributeSample[]? savedAttributes=null;LyraRootMotionAttribute savedRoot=default;
                if(evaluate)
                {
                    var x=Evaluate(clean,a,false);var y=Evaluate(retry,b,check);output=y;
                    Require(x.Pose.SequenceEqual(y.Pose)&&x.Curves.SequenceEqual(y.Curves)&&x.Attributes.SequenceEqual(y.Attributes)&&x.RootMotion==y.RootMotion,label+"/all output channels");
                    Require(x.Curves.SequenceEqual(clean.Curves)&&x.RootMotion.Present==clean.RootMotion.Present,label+"/Aiming curve/root preservation");
                    changes+=x.Pose.SequenceEqual(clean.Pose)?0:1;foreach(var bone in x.Pose)bone.Validate(.001);poses++;
                    if(check){savedPose=y.Pose.ToArray();savedCurves=y.Curves.ToArray();savedAttributes=y.Attributes.ToArray();savedRoot=y.RootMotion;}
                }
                else hidden+=visit.Visited?0:1;
                if(check)
                {
                    if(evaluate&&b.Skeletal!.Update.Updated.Alphas[5]>AlsPoseBlender.WeightThreshold)
                    {
                        var character=new AlsFootCharacterInput(transform,input.Observation.Ground,input.Observation.Ground,default,new(0,0,1),input.Observation.Velocity);
                        Reject(()=>retry.Layers.EvaluateSkeletal(b.Sources,b.Skeletal!,retry.Layers.Call(LyraLayerHook.FullBody_SkeletalControls),
                            new(bank,retry.Pose,retry.Curves,retry.Attributes,retry.RootMotion),character,new FailedGround()),label+"/shared query failure");
                        Reject(()=>retry.Commit(b),label+"/failed shared commit");faults++;
                    }
                    retry.Cancel();Require(Snapshot(retry)==old,label+"/cancelled fields/filter/clocks/feedback");
                    if(output is {} discarded)Reject(()=>{_ = discarded.Pose.Length;},label+"/cancelled view");
                    b=Prepare(retry);
                    if(evaluate)
                    {
                        var y=Evaluate(retry,b,false);
                        Require(y.Pose.SequenceEqual(savedPose)&&y.Curves.SequenceEqual(savedCurves)&&y.Attributes.SequenceEqual(savedAttributes)&&y.RootMotion==savedRoot,label+"/late retry output");
                    }
                    late++;
                }
                clean.ValidateCommit(a,!evaluate);retry.ValidateCommit(b,!evaluate);clean.Commit(a,!evaluate);retry.Commit(b,!evaluate);
                Require(clean.Layers.AimWeights==retry.Layers.AimWeights && clean.SyncPlayers.SequenceEqual(retry.SyncPlayers)&&clean.SyncSamples.SequenceEqual(retry.SyncSamples),label+"/committed common clocks");
                Require(clean.Layers.CommittedCurve("applyHipfireOverridePose")==retry.Layers.CommittedCurve("applyHipfireOverridePose"),label+"/committed feedback");
                if(check)Require(Snapshot(clean)==Snapshot(retry)&&Snapshot(foreign)==foreignBefore,label+"/history/character isolation");
                frames++;i++;
            }
        }
        Require(frames==11340&&poses==9762&&sources>10000&&samples>20000&&late>100&&changes>1000&&mixed>1000&&hidden>0&&curveDriven>1000&&footActive>1000&&footPartial>1000&&previousControls>1000&&faults>0,
            $"Incomplete Main Aiming scope coverage frames={frames} poses={poses} sources={sources} samples={samples} late={late} changed={changes} mixed={mixed} hidden={hidden}");
        GD.Print($"LYRA_MAIN_SKELETAL_SCOPE_GODOT_OK frames={frames} poses={poses} aoTicks={sources} samples={samples} mixed={mixed} changed={changes} hidden={hidden} lateRetry={late} rejected={rejected} curveDriven={curveDriven} footActive={footActive} footPartial={footPartial} previousControls={previousControls} faults={faults} groupEntries=14 commonSync=true previousFeedback=true cachedInputContext=true stagedInput=true nativeCombined=false production=false");
    }
}
