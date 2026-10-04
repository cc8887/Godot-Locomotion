using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraMainPoseHostSmoke:Node
{
    private static void Require(bool value,string label){if(!value)throw new InvalidOperationException(label);}
    private sealed class PlaneGround(AlsFootPlane plane):IAlsFootGroundQuery
    {
        public AlsFootGroundHit Sweep(int leg,in AlsFootTraceQuery query)
        {
            var d=AlsDoubleVector.Dot(plane.Normal,query.Direction);if(Math.Abs(d)<1e-8)return default;
            var t=(plane.W+query.Radius-AlsDoubleVector.Dot(plane.Normal,query.Start))/d;
            return new(t>=query.StartOffset&&t<=query.EndOffset,query.Start+query.Direction*t-plane.Normal*query.Radius,plane.Normal);
        }
    }
    private sealed class FailedGround:IAlsFootGroundQuery
    {public AlsFootGroundHit Sweep(int leg,in AlsFootTraceQuery query)=>throw new InvalidOperationException("Injected Main pose query failure.");}
    public override void _Ready(){try{Run(OS.GetCmdlineUserArgs().Contains("--actual-final-feedback"));GetTree().Quit();}catch(Exception e){GD.PushError("Main pose host failed: "+e);GetTree().Quit(1);}}
    private static void Run(bool actualFeedback)
    {
        using var resources=new LyraLocomotionResources();var bank=resources.Catalog.Bank;
        using var requests=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/main_als_locomotion_v1_requests.json"));
        var frames=0;var poses=0;var repeated=0;var late=0;var faults=0;var rejected=0;var sparse=0;var cacheSources=0;var actualCurves=0;var feedbackFaults=0;var evaluationChecks=0;var entryGuards=0;
        void Reject(Action action,string label){try{action();}catch(InvalidOperationException){rejected++;return;}throw new Exception("Accepted invalid Main pose: "+label);}
        string Snapshot(LyraMainLocomotionHost h)=>JsonSerializer.Serialize(new{main=LyraMainLocomotionHostSmoke.Snapshot(h),h.Layers.AimWeights,h.Layers.AimingNodes,
            h.Layers.AdditivesState,h.Layers.AdditivesElapsed,h.Layers.SkeletalHistory,h.Layers.SkeletalUpdate});
        string CacheHistory(LyraMainLocomotionHost h)=>JsonSerializer.Serialize(new{main=h.CacheLifecycle.History,
            providers=h.LayerInstances.Select(p=>p.CacheLifecycle.History).ToArray()});
        foreach(var trace in requests.RootElement.GetProperty("traces").EnumerateArray())
        {
            var profile=trace.GetProperty("profile").GetString()!;
            // Both sides run the full original startup, including unvisited
            // Provider functions. The reference still evaluates the established
            // composition operators directly, independently of host memoization.
            using var referenceOwner=resources.CreateMainPoseHost(profile,700,1700,7,enableMainInertia:false);
            var reference=referenceOwner.Main;
            var host=resources.CreateMainPoseHost(profile,700,1700,7,enableMainInertia:false);var foreign=resources.CreateMainPoseHost(profile,700,1700,7,enableMainInertia:false);var foreignBefore=Snapshot(foreign.Main);
            // Isolate the existing per-frame transaction checks from the
            // actual, persistent deferred startup boundary. Startup has its
            // own original-component oracle and production Prepare coverage.
            referenceOwner.EnterRootPhases(0);host.EnterRootPhases(0);
            var op=new LyraMainCompositionOperators(bank,profile);var preAim=new LyraCompositionPoseBuffer(bank);var recovery=new LyraCompositionPoseBuffer(bank);var root=new LyraCompositionPoseBuffer(bank);
            var additiveRef=new LyraCompositionPoseBuffer(bank);for(var b=0;b<81;b++)additiveRef.Pose[b]=new(default,AlsQuaternion.Identity,default);
            var i=0;
            foreach(var frame in trace.GetProperty("frames").EnumerateArray())
            {
                var label=$"PoseHost/{profile}/{trace.GetProperty("hz")}/{i}";var input=LyraMainUpdateSmoke.ReadInput(frame.GetProperty("observation"));var delta=frame.GetProperty("delta").GetSingle();
                var proxyBefore=host.ProxyCounters;var foreignProxyBefore=foreign.ProxyCounters;
                var providerProxyBefore=host.Main.LayerInstances.Select(p=>p.ProxyTraversal.Committed).ToArray();
                input=input with{AimPitch=i%29==0?new[]{-90d,0,90}[(i/29)%3]:Math.Sin(i*delta*2.1)*110,
                    RootYawEnabled=true,Observation=input.Observation with{Rotation=input.Observation.Rotation with{Yaw=input.Observation.Rotation.Yaw+Math.Sin(i*delta*1.3)*45}}};
                var visit=new LyraLocomotionMachineVisit(frame.GetProperty("active").GetBoolean(),frame.GetProperty("weight").GetSingle(),frame.GetProperty("reinitialize").GetBoolean(),frame.GetProperty("contextActive").GetBoolean());
                AlsDoubleVector V(JsonElement v)=>new(v[0].GetDouble(),v[1].GetDouble(),v[2].GetDouble());
                var component=frame.GetProperty("componentInput");var q=component.GetProperty("rotation");var r=frame.GetProperty("relativeRotation");var m=frame.GetProperty("movement");
                var transform=new AlsPrecisePose(V(component.GetProperty("position")),new(q[0].GetDouble(),q[1].GetDouble(),q[2].GetDouble(),q[3].GetDouble()),AlsDoubleVector.One);
                var relative=new AlsQuaternion(r[0].GetDouble(),r[1].GetDouble(),r[2].GetDouble(),r[3].GetDouble());
                var movement=new AlsStopMovementSnapshot(V(m.GetProperty("lastUpdateVelocity")),m.GetProperty("separate").GetBoolean(),m.GetProperty("brakingFriction").GetSingle(),m.GetProperty("groundFriction").GetSingle(),m.GetProperty("factor").GetSingle(),m.GetProperty("deceleration").GetSingle());
                var character=new AlsFootCharacterInput(transform,input.Observation.Ground,input.Observation.Ground,new(0,0,Math.Sin(i*delta*1.7)*12),new(0,0,1),input.Observation.Velocity);
                var ground=new PlaneGround(AlsFootPlane.At(character.FloorPoint,character.FloorNormal));var distance=frame.GetProperty("groundDistance").GetDouble();
                var fired=new[]{.49999999999999994,.5,.5000000000000001,99}[(i/31)%4];var settings=new LyraMainSkeletalSettings((i/17)%2==0,(i/37)%4!=0);
                LyraMainPoseCandidate Prepare()=>host.Prepare(input,delta,visit,character,relative,movement,distance,fired,settings);
                var check=i%59==0;var before=check?Snapshot(host.Main):null;
                var cacheBefore=CacheHistory(host.Main);var foreignCacheBefore=CacheHistory(foreign.Main);
                var mainEvaluationBefore=host.Main.CacheLifecycle.History.Select(h=>h.Evaluation).ToArray();
                var providerEvaluationBefore=host.Main.LayerInstances.Select(p=>p.CacheLifecycle.History.Select(h=>h.Evaluation).ToArray()).ToArray();
                var a=reference.Prepare(input,delta,visit,-1,transform,relative,movement,distance,updateLeftHand:true,updateAdditives:true,timeSinceFired:fired,skeletalSettings:settings,fullMainRoot:true);
                var c=Prepare();Require(reference.SyncPlayers.SequenceEqual(host.Main.SyncPlayers),label+"/unchanged committed clocks before evaluation");
                var expectedUpdate=visit.Visited?proxyBefore.Update.Next(checked((ulong)c.Macro.Observation.Frame)):proxyBefore.Update;
                Require(host.ProxyCounters==proxyBefore&&host.PreparedProxyCounters(c)==proxyBefore.WithCounter(GodotAls.Core.Animation.AlsAnimationProxyPhase.Update,expectedUpdate),label+"/Prepare stages only the actual Main root Update");
                void ProviderCounters()
                {
                    var visited=new HashSet<LyraItemLayerGraphInstance>();
                    for(var root=0;root<10;root++)if(c.Main.Sources.Visits[root].Visited)visited.Add(host.Main.Layer(LyraItemLayerGraphInstance.HookForRoot(root)));
                    if(c.Main.Skeletal!.Update.Input.Visited)visited.Add(host.Main.Layer(LyraLayerHook.FullBody_SkeletalControls));
                    if(c.Main.Aiming!.Visit.Visited)visited.Add(host.Main.Layer(LyraLayerHook.FullBody_Aiming));
                    if(c.Main.LeftHand!.Visited)visited.Add(host.Main.Layer(LyraLayerHook.LeftHandPose_OverrideState));
                    if(c.Main.Additives!.Visited)visited.Add(host.Main.Layer(LyraLayerHook.FullBodyAdditives));
                    for(var owner=0;owner<providerProxyBefore.Length;owner++)
                    {
                        var instance=host.Main.LayerInstances[owner];var expected=providerProxyBefore[owner];
                        if(visited.Contains(instance))expected=expected.WithCounter(GodotAls.Core.Animation.AlsAnimationProxyPhase.Update,expectedUpdate);
                        Require(instance.ProxyTraversal.Committed==providerProxyBefore[owner]&&instance.ProxyTraversal.Prepared(c.Macro)==expected,label+"/actual Provider root inherits before worker, hidden roots retain history");
                    }
                }
                ProviderCounters();
                void EvaluationCounters(GodotAls.Core.Locomotion.AlsGraphTraversalCounter? current)
                {
                    var visited=new HashSet<LyraItemLayerGraphInstance>();
                    if(current is not null)
                    {
                        for(var root=0;root<10;root++)if(c.Main.EvaluationRoots[root])visited.Add(host.Main.Layer(LyraItemLayerGraphInstance.HookForRoot(root)));
                        foreach(var hook in new[]{LyraLayerHook.FullBody_SkeletalControls,LyraLayerHook.FullBody_Aiming,LyraLayerHook.LeftHandPose_OverrideState,LyraLayerHook.FullBodyAdditives})visited.Add(host.Main.Layer(hook));
                    }
                    for(var owner=0;owner<providerProxyBefore.Length;owner++)
                    {
                        var instance=host.Main.LayerInstances[owner];var expected=visited.Contains(instance)?current!.Value:providerProxyBefore[owner].Evaluation;
                        Require(instance.ProxyTraversal.Committed==providerProxyBefore[owner]&&instance.ProxyTraversal.Prepared(c.Macro).Evaluation==expected,label+"/actual Provider Evaluation entry, hidden history and pending isolation");
                        Require(instance.ProxyTraversal.EvaluationEntered(c.Macro)==visited.Contains(instance),label+"/numeric counter equality cannot fabricate a function entry");evaluationChecks++;
                    }
                }
                EvaluationCounters(null);
                if(check)
                {
                    Reject(()=>host.Main.EnterLinkedEvaluation(c.Main,LyraLayerHook.FullBody_SkeletalControls,proxyBefore.Evaluation),label+"/entry before actual Main evaluation");
                    var guarded=new LyraMainPoseCacheScope(bank,node=>node==181?host.Main.Layer(LyraLayerHook.FullBody_Aiming).CacheLifecycle:host.Main.CacheLifecycle,host.ValidateCacheEvaluation);
                    guarded.Begin(c,c.Macro,proxyBefore.Evaluation.Next(checked((ulong)c.Macro.Observation.Frame)));
                    var sources=0;var cachePending=JsonSerializer.Serialize(host.Main.CacheLifecycle.Prepared);var aimPending=JsonSerializer.Serialize(host.Main.Layer(LyraLayerHook.FullBody_Aiming).CacheLifecycle.Prepared);
                    foreach(var node in new[]{83,78,181}){Reject(()=>guarded.Read(c,node,_=>sources++),label+"/cache read before Proxy entry");entryGuards++;}
                    Require(sources==0&&guarded.Evaluations(83)==0&&guarded.Evaluations(78)==0&&guarded.Evaluations(181)==0&&JsonSerializer.Serialize(host.Main.CacheLifecycle.Prepared)==cachePending&&JsonSerializer.Serialize(host.Main.Layer(LyraLayerHook.FullBody_Aiming).CacheLifecycle.Prepared)==aimPending,label+"/guard rejects before cache history or source traversal");guarded.End();EvaluationCounters(null);
                    var layer=host.Main.Layer(LyraLayerHook.FullBody_SkeletalControls);var phases=layer.Phases;
                    var bones=phases.CachedBonesCounter;var emitted=0;
                    for(var attempt=0;attempt<2;attempt++)Reject(()=>phases.CacheBones(LyraLayerHook.FullBody_SkeletalControls,
                        bones.Next(checked((ulong)c.Macro.Observation.Frame)),_=>emitted++),label+"/pending Provider CacheBones");
                    Require(emitted==0&&phases.CachedBonesCounter==bones,label+"/rejected phase must not publish a counter or start traversal");
                }
                Require(c.Main.Skeletal!.Update.Input.Counter==expectedUpdate.Counter,label+"/SkeletalControls consumes actual Main root counter");
                Require(CacheHistory(host.Main)==cacheBefore,label+"/cache Prepare cannot publish node history");
                foreach(var update in c.Main.Caches!.Updates)
                {
                    var owner=update.Cache==181?host.Main.Layer(LyraLayerHook.FullBody_Aiming).CacheLifecycle:host.Main.CacheLifecycle;
                    var node=update.Cache==181?78:update.Cache;
                    Require(owner.Prepared.Single(h=>h.Node==node).GlobalWeight==update.Context.Weight,label+"/actual deferred source selected weight");
                }
                var evaluate=visit.Visited&&frame.GetProperty("evaluate").GetBoolean();LyraMainPoseView view=default;LyraCompositionPoseBuffer? saved=null;LyraCurveSample[]? finalFeedback=null;
                if(evaluate)
                {
                    // Independent established composition path is retained as
                    // the reference while the reusable host adds memoization.
                    reference.Evaluate(a);var left=reference.Layers.EvaluateLeftHand(a.Sources,a.LeftHand!,reference.Layers.Call(LyraLayerHook.LeftHandPose_OverrideState),new(bank,reference.Pose,reference.Curves,reference.Attributes,reference.RootMotion));
                    var leftInput=new LyraLayerPoseInput(bank,left.Pose,left.Curves,left.Attributes,left.RootMotion);op.Upper(leftInput,leftInput,additiveRef.Input,(float)a.Macro.Tail.UpperbodyWeight,1,preAim);
                    var aimed=reference.Layers.EvaluateAiming(a.Sources,a.Aiming!,reference.Layers.Call(LyraLayerHook.FullBody_Aiming),preAim.Input);
                    var additive=reference.Layers.EvaluateAdditives(a.Sources,a.Additives!,reference.Layers.Call(LyraLayerHook.FullBodyAdditives));
                    LyraMainCompositionOperators.Additive(new(bank,aimed.Pose,aimed.Curves,aimed.Attributes,aimed.RootMotion),new(bank,additive.Pose,additive.Curves,additive.Attributes,additive.RootMotion),.65f,recovery);
                    LyraMainCompositionOperators.RotateRoot(recovery.Input,(float)a.Macro.State.RootYaw,root);
                    var expected=reference.Layers.EvaluateSkeletal(a.Sources,a.Skeletal!,reference.Layers.Call(LyraLayerHook.FullBody_SkeletalControls),root.Input,character,ground);
                    view=host.Evaluate(c,ground);
                    var firstEvaluation=proxyBefore.Evaluation.Next(checked((ulong)c.Macro.Observation.Frame));
                    Require(host.ProxyCounters==proxyBefore&&host.PreparedProxyCounters(c).Evaluation==firstEvaluation,label+"/actual root advances candidate counter before caches");
                    EvaluationCounters(firstEvaluation);
                    Require(host.Main.CacheLifecycle.Prepared.All(h=>h.Evaluation==firstEvaluation),label+"/Main caches inherit actual root counter");
                    Require(view.Pose.SequenceEqual(expected.Pose)&&view.Curves.SequenceEqual(expected.Curves)&&view.Attributes.SequenceEqual(expected.Attributes)&&view.RootMotion==expected.RootMotion,label+"/all reference channels");
                    Require(host.LastLocomotionEvaluations==1&&host.LastSplitEvaluations==1&&host.LastInputEvaluations==1,label+"/one source evaluation per cache");cacheSources+=3;
                    saved=new(bank);saved.Copy(new(bank,view.Pose,view.Curves,view.Attributes,view.RootMotion));
                    if(check)
                    {
                        Reject(()=>foreign.Evaluate(c,ground),label+"/foreign owner");Reject(()=>host.Evaluate(c with{},ground),label+"/copied candidate");Reject(()=>host.Commit(c),label+"/feedback not staged");
                        var oldView=view;view=host.Evaluate(c,ground);Reject(()=>{_ = oldView.Pose.Length;},label+"/superseded evaluation view");
                        Require(host.PreparedProxyCounters(c).Evaluation==firstEvaluation.Next(firstEvaluation.GlobalFrame),label+"/repeated actual root advances within same external frame");
                        EvaluationCounters(firstEvaluation.Next(firstEvaluation.GlobalFrame));
                        Require(view.Pose.SequenceEqual(saved.Pose)&&view.Curves.SequenceEqual(saved.Curves)&&view.Attributes.SequenceEqual(saved.Attributes)&&view.RootMotion==saved.RootMotion,label+"/new evaluation lifetime");repeated++;
                    }
                    poses++;
                }
                else{Reject(()=>host.Commit(c),label+"/missing current evaluation");sparse++;}
                if(check)
                {
                    if(evaluate)Require((c.Main.Skeletal!.Update.Updated.Alphas[5]>AlsPoseBlender.WeightThreshold)==
                        (a.Skeletal!.Update.Updated.Alphas[5]>AlsPoseBlender.WeightThreshold),label+"/reference query eligibility");
                    if(evaluate&&c.Main.Skeletal!.Update.Updated.Alphas[5]>AlsPoseBlender.WeightThreshold)
                    {Reject(()=>host.Evaluate(c,new FailedGround()),label+"/late shared query failure");Reject(()=>host.Commit(c),label+"/failed commit");faults++;}
                    else if(actualFeedback&&evaluate)
                    {
                        try{host.StageFinalFeedback(c,[new("UnknownMainControl",new(1,true))]);throw new Exception("Accepted invalid enclosing control curve.");}
                        catch(ArgumentException){feedbackFaults++;}
                        Reject(()=>host.Commit(c),label+"/failed feedback commit");Reject(()=>host.StageFinalFeedback(c),label+"/failed feedback retry before cancel");
                    }
                    host.Cancel();Require(Snapshot(host.Main)==before,label+"/cancelled history and feedback");
                    Require(host.ProxyCounters==proxyBefore,label+"/Cancel restores committed Proxy counters");
                    Require(host.Main.LayerInstances.Select(p=>p.ProxyTraversal.Committed).SequenceEqual(providerProxyBefore),label+"/Cancel preserves every Provider Proxy history");
                    var layer=host.Main.Layer(LyraLayerHook.FullBody_SkeletalControls);var cached=0;
                    layer.Phases.CacheBones(LyraLayerHook.FullBody_SkeletalControls,layer.Phases.CachedBonesCounter,_=>cached++);
                    Require(cached>0,label+"/rejected pending phase cannot poison the next idle CacheBones traversal");
                    Require(CacheHistory(host.Main)==cacheBefore,label+"/cancelled cache history");
                    if(evaluate){var discarded=view;Reject(()=>{_ = discarded.Curves.Length;},label+"/cancelled final view");}
                    c=Prepare();ProviderCounters();EvaluationCounters(null);if(evaluate)
                    {
                        view=host.Evaluate(c,ground);Require(view.Pose.SequenceEqual(saved!.Pose)&&view.Curves.SequenceEqual(saved.Curves)&&view.Attributes.SequenceEqual(saved.Attributes)&&view.RootMotion==saved.RootMotion,label+"/late retry");
                        Require(host.PreparedProxyCounters(c).Evaluation==proxyBefore.Evaluation.Next(checked((ulong)c.Macro.Observation.Frame)),label+"/retry gets clean root counter independent of discarded views");
                        EvaluationCounters(proxyBefore.Evaluation.Next(checked((ulong)c.Macro.Observation.Frame)));
                    }
                    late++;
                }
                if(evaluate)
                {
                    var final=view.Curves.ToArray();finalFeedback=final;if(!actualFeedback)final[bank.Curves.Index("DisableLegIK")]=new(new[]{-.25f,0,.25f,.75f,1f}[(i/19)%5],true);
                    LyraNamedCurveSample[] controls=[new("applyHipfireOverridePose",new((i/23)%4==0?.125f:0,true)),new("DisableRHandIK",new((i/11)%3*.25f,true)),new("DisableLHandIK",new((i/13)%4*.25f,true)),new("DisableHandIKRetargeting",new((i/23)%3*.25f,true)),new("ScaleDownWeaponR",new((i/17)%3*.005f,true))];
                    if(actualFeedback){reference.StageFinalFeedback(a,final);host.StageFinalFeedback(c);}
                    else{reference.StageFinalFeedback(a,final,controls);host.StageFinalFeedback(c,controls,final);}
                    if(check){Reject(()=>host.StageFinalFeedback(c),label+"/duplicate feedback");Reject(()=>host.Evaluate(c,ground),label+"/evaluate after feedback");}
                }
                Require(CacheHistory(host.Main)==cacheBefore,label+"/Evaluate and feedback cannot publish cache history");
                if(!evaluate)
                {
                    EvaluationCounters(null);
                    Require(host.PreparedProxyCounters(c).Evaluation==proxyBefore.Evaluation,label+"/update-only cannot advance Evaluation");
                    Require(host.Main.CacheLifecycle.Prepared.Select(h=>h.Evaluation).SequenceEqual(mainEvaluationBefore),label+"/update-only Main evaluation counters");
                    for(var owner=0;owner<host.Main.LayerInstances.Count;owner++)
                        Require(host.Main.LayerInstances[owner].CacheLifecycle.Prepared.Select(h=>h.Evaluation).SequenceEqual(providerEvaluationBefore[owner]),label+"/update-only Provider evaluation counters");
                }
                var cachePrepared=JsonSerializer.Serialize(new{main=host.Main.CacheLifecycle.Prepared,
                    providers=host.Main.LayerInstances.Select(p=>p.CacheLifecycle.Prepared).ToArray()});
                var providerProxyPrepared=host.Main.LayerInstances.Select(p=>p.ProxyTraversal.Prepared(c.Macro)).ToArray();
                reference.ValidateCommit(a,!evaluate);host.ValidateCommit(c,!evaluate);reference.Commit(a,!evaluate);host.Commit(c,!evaluate);
                Require(host.Main.LayerInstances.Select(p=>p.ProxyTraversal.Committed).SequenceEqual(providerProxyPrepared),label+"/only accepted Provider counters commit");
                Require(host.ProxyCounters.Evaluation==(evaluate?proxyBefore.Evaluation.Next(checked((ulong)c.Macro.Observation.Frame)):proxyBefore.Evaluation),label+"/only accepted root evaluation commits");
                Require(host.ProxyCounters.Update==expectedUpdate,label+"/accepted Main root Update commits independently of Evaluation");
                Require(foreign.ProxyCounters==foreignProxyBefore,label+"/Proxy counter character isolation");
                Require(CacheHistory(host.Main)==cachePrepared&&CacheHistory(foreign.Main)==foreignCacheBefore,label+"/actual cache Commit and character isolation");
                Require(Snapshot(reference)==Snapshot(host.Main)&&Snapshot(foreign.Main)==foreignBefore,label+"/committed source, linked history and character isolation");
                if(actualFeedback&&evaluate)
                {
                    for(var curve=0;curve<bank.Curves.Names.Length;curve++)
                    {
                        var name=bank.Curves.Names[curve];var expected=finalFeedback![curve].Present?finalFeedback[curve].Value:0;
                        Require(host.Main.Layers.CommittedCurve(name)==expected&&reference.Layers.CommittedCurve(name)==expected,label+"/actual enclosing curve "+name);actualCurves++;
                    }
                }
                if(check&&evaluate){var committed=view;Reject(()=>{_ = committed.Attributes.Length;},label+"/committed final view");}
                frames++;i++;
            }
        }
        Require(frames==11340&&poses==9762&&repeated==174&&late==207&&faults==(actualFeedback?129:84)&&feedbackFaults==(actualFeedback?45:0)&&sparse==1578,
            $"Incomplete Main pose host coverage: frames={frames} poses={poses} repeated={repeated} late={late} faults={faults} feedbackFaults={feedbackFaults} sparse={sparse}");
        Require(!actualFeedback||(actualCurves>0&&feedbackFaults>0),"Missing actual enclosing feedback coverage");
        var marker=actualFeedback?"LYRA_MAIN_POSE_HOST_FEEDBACK_GODOT_OK":"LYRA_MAIN_POSE_HOST_GODOT_OK";
        Require(evaluationChecks>0&&entryGuards==621,"Missing Provider evaluation or cache entry coverage");
        GD.Print($"LYRA_PROVIDER_EVALUATION_HISTORY_OK frames={frames} ownerChecks={evaluationChecks} rejectedCacheEntries={entryGuards} repeated={repeated} retry={late} updateOnly={sparse} actualFinalFeedback={actualFeedback}");
        GD.Print($"{marker} frames={frames} poses={poses} cacheSources={cacheSources} repeated={repeated} lateRetry={late} faults={faults} feedbackFaults={feedbackFaults} rejected={rejected} updateOnly={sparse} feedbackCurves={actualCurves} actualFinalFeedback={actualFeedback} groupEntries=14 commonSync=true fullChannels=true cachePoseEvaluation=true inactiveSlots=true inertia=false controlRig=false nativeCombined=false production=false");
    }
}
