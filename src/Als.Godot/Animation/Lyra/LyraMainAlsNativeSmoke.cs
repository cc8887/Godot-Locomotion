using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

// Native rows are assertions only. The host receives gameplay/component inputs
// and derives its own rules, state, traversal, Sync clocks and all poses.
public partial class LyraMainAlsNativeSmoke : Node
{
    private const string Root="res://assets/generated/lyra_als/";
    private static JsonDocument Load(string name)=>JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root+name));
    private static void Require(bool value,string label){if(!value)throw new InvalidOperationException(label);}
    private static void Exact(float actual,JsonElement row,string name,string label)
    {var expected=row.GetProperty(name).GetSingle();Require(BitConverter.SingleToInt32Bits(actual)==BitConverter.SingleToInt32Bits(expected),$"{label}/{name}: {actual:R} != {expected:R}");}
    private static AlsDoubleVector Vector(JsonElement a)=>new(a[0].GetDouble(),a[1].GetDouble(),a[2].GetDouble());
    private static void MarkerDistance(float actual,JsonElement row,string name,string label)
    {
        // UE's numeric JSON writer emits both zero signs as 0. Their sign is
        // unobservable in this fixture; every nonzero marker distance is bit exact.
        var expected=row.GetProperty(name).GetSingle();if(actual==0 && expected==0)return;
        Exact(actual,row,name,label);
    }
    internal static void RootMotion(LyraRootMotionAttribute actual,JsonElement output,string label)
    {
        var present=output.TryGetProperty("rootMotion",out var root);Require(actual.Present==present,label+"/root presence");if(!present)return;
        Require(root.GetProperty("name").GetString()=="RootMotionDelta" && root.GetProperty("bone").GetString()=="root" && root.GetProperty("namespace").GetString()=="bone" &&
            root.GetProperty("type").GetString()=="/Script/Engine.TransformAnimationAttribute",label+"/root identity");
        var expected=LyraLogicalSourceBank.ParsePose(root);var sign=AlsQuaternion.Dot(actual.Value.Rotation,expected.Rotation)<0?-1:1;
        Require((actual.Value.Position-expected.Position).LengthSquared<=1e-16 && (actual.Value.Rotation+expected.Rotation*-sign).LengthSquared<=1e-20 &&
            (actual.Value.Scale-expected.Scale).LengthSquared<=1e-24,label+"/root value");
    }
    private static LyraLocomotionRuleInputs NativeRules(JsonElement row)
    {
        var f=row.GetProperty("fields");var source=row.GetProperty("relevant");
        bool B(string n)=>f.GetProperty(n).GetBoolean();double D(string n)=>f.GetProperty(n).GetDouble();int I(string n)=>f.GetProperty(n).GetInt32();
        return new(B("HasAcceleration"),B("HasVelocity"),B("GameplayTag_IsMelee"),B("IsRunningIntoWall"),B("LinkedLayerChanged"),B("CrouchStateChange"),B("ADSStateChanged"),
            B("IsJumping"),B("IsFalling"),B("IsOnGround"),Vector(f.GetProperty("LocalVelocity2D")),Vector(f.GetProperty("LocalAcceleration2D")),I("StartDirection"),I("LocalVelocityDirection"),I("PivotInitialDirection"),
            D("DisplacementSpeed"),D("RootYawOffset"),D("LastPivotTime"),D("TimeToJumpApex"),D("GroundDistance"),row.GetProperty("beforeElapsed").GetSingle(),
            row.GetProperty("pivotNotify").GetBoolean(),row.GetProperty("syncValid").GetBoolean(),source.GetProperty("valid").GetBoolean(),source.GetProperty("length").GetSingle(),
            source.GetProperty("time").GetSingle(),source.GetProperty("looping").GetBoolean(),source.GetProperty("previousValid").GetBoolean(),source.GetProperty("previous").GetSingle(),source.GetProperty("delta").GetSingle());
    }
    public override void _Ready()
    {try{Run();GetTree().Quit();}catch(Exception error){GD.PushError("Main ALS native comparison failed: "+error);GetTree().Quit(1);}}
    private static void Run()
    {
        using var resources=new LyraLocomotionResources();using var requests=Load("main_als_locomotion_v1_requests.json");
        using var native=Load("main_als_locomotion_v1_native.json");using var probes=Load("cycle_layer_pose_native_v2.json");var data=native.RootElement;
        Require(data.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+"main_als_locomotion_v1_requests.json")),"Changed Main ALS request");
        foreach(var dep in data.GetProperty("dependencies").EnumerateObject())Require(dep.Value.GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+dep.Name)),"Changed Main ALS dependency: "+dep.Name);
        var traces=data.GetProperty("traces").EnumerateArray().ToArray();var counts=data.GetProperty("counts");
        var mixed=new LyraCycleLayerPoseComparison(resources.Catalog.Bank,probes.RootElement,true,true,true,true,
            expectedFrames:counts.GetProperty("poses").GetInt32(),stage:"OriginalMainALS_LocomotionSM",
            expectedAttributes:traces.Sum(t=>t.GetProperty("frames").EnumerateArray().Where(r=>r.TryGetProperty("output",out _)).Sum(r=>r.GetProperty("output").GetProperty("attributes").GetArrayLength())));
        var rootCompare=new LyraCycleLayerPoseComparison(resources.Catalog.Bank,probes.RootElement,true,true,true,true,
            expectedFrames:counts.GetProperty("rootPoses").GetInt32(),stage:"OriginalMainALS_TenRoots",
            expectedAttributes:traces.Sum(t=>t.GetProperty("frames").EnumerateArray().Sum(r=>r.GetProperty("evaluatedRoots").EnumerateArray().Sum(e=>e.GetProperty("output").GetProperty("attributes").GetArrayLength()))));
        var main=new LyraMainNativeComparison();var frames=0;var poses=0;var rootPoses=0;var sourceClocks=0;var sparse=0;var hidden=0;var copied=0;var retries=0;
        var states=new HashSet<int>();var roots=new HashSet<int>();var transitions=0;var layerRejected=0;var layerCalls=0;
        var withAdditives=OS.GetCmdlineUserArgs().Contains("--main-additives");
        var withLeft=withAdditives||OS.GetCmdlineUserArgs().Contains("--main-left-hand");var leftPoses=0;var additivePoses=0;var recoveryTicks=0;var recoveryPoses=0;
        void Reject(Action action,string label)
        {
            try{action();}
            catch(InvalidOperationException){layerRejected++;return;}
            catch(NotSupportedException){layerRejected++;return;}
            throw new InvalidOperationException("Unsafe layer invocation was accepted: "+label);
        }
        foreach(var (trace,ti) in traces.Select((v,i)=>(v,i)))
        {
            var profile=trace.GetProperty("profile").GetString()!;
            var full=withLeft?new LyraMainLeftHandHost(resources,profile,700,1700,7,includeAdditives:withAdditives):null;
            var instance=full?.Layers??resources.CreateLayerInstance(profile,700,1700,7);var host=full?.Main??resources.CreateMainHost(instance);
            Require(ReferenceEquals(host.Layers,instance) && ReferenceEquals(host.Sources,instance.Sources),"Main did not bind its actual layer group");
            Reject(()=>resources.CreateMainHost(instance),"group shared by two Main owners");
            // Matching class, player IDs and epoch still do not permit sharing
            // mutable instances between different characters.
            var foreign=resources.CreateMainHost(profile,700,1700,7);var tested=false;
            foreach(var (row,i) in trace.GetProperty("frames").EnumerateArray().Select((v,i)=>(v,i)))
            {
                var frame=requests.RootElement.GetProperty("traces")[ti].GetProperty("frames")[i];var label=$"MainALS/{ti}/{i}";
                var input=LyraMainUpdateSmoke.ReadInput(frame.GetProperty("observation"));var delta=frame.GetProperty("delta").GetSingle();
                var visit=new LyraLocomotionMachineVisit(frame.GetProperty("active").GetBoolean(),frame.GetProperty("weight").GetSingle(),
                    frame.GetProperty("reinitialize").GetBoolean(),frame.GetProperty("contextActive").GetBoolean());
                var component=frame.GetProperty("componentInput");var q=component.GetProperty("rotation");var rel=frame.GetProperty("relativeRotation");var move=frame.GetProperty("movement");
                var transform=new AlsPrecisePose(Vector(component.GetProperty("position")),new(q[0].GetDouble(),q[1].GetDouble(),q[2].GetDouble(),q[3].GetDouble()),AlsDoubleVector.One);
                var physical=new AlsStopMovementSnapshot(Vector(move.GetProperty("lastUpdateVelocity")),move.GetProperty("separate").GetBoolean(),
                    move.GetProperty("brakingFriction").GetSingle(),move.GetProperty("groundFriction").GetSingle(),move.GetProperty("factor").GetSingle(),move.GetProperty("deceleration").GetSingle());
                var oldRelevant=visit.Initialize?default:host.Relevant(host.Machine.State);var oldSync=host.SyncValid(0);var oldFeedback=host.Sources.Hosts.Idle.TurnYawFeedback;
                var oldAdditiveState=instance.AdditivesState;var oldAdditiveElapsed=instance.AdditivesElapsed;
                var retryFrame=i%59==0;var before=retryFrame?LyraMainLocomotionHostSmoke.Snapshot(host):null;
                LyraMainLeftHandCandidate? composite=null;
                LyraMainLocomotionCandidate Prepare()
                {
                    var relative=new AlsQuaternion(rel[0].GetDouble(),rel[1].GetDouble(),rel[2].GetDouble(),rel[3].GetDouble());
                    if(full is null)return host.Prepare(input,delta,visit,frame.GetProperty("hipWeight").GetDouble(),transform,relative,physical,frame.GetProperty("groundDistance").GetDouble());
                    composite=full.Prepare(input,delta,visit,frame.GetProperty("hipWeight").GetDouble(),transform,relative,physical,frame.GetProperty("groundDistance").GetDouble());
                    return composite.Main;
                }
                void Evaluate(LyraMainLocomotionCandidate candidate){if(full is null)host.Evaluate(candidate);else full.Evaluate(composite!);}
                void Cancel(){if(full is null)host.Cancel();else full.Cancel();}
                void Stage(LyraMainLocomotionCandidate candidate,bool cancelledCopy=false)
                {
                    if(full is null)host.StageFinalFeedback(candidate,host.Curves);
                    else full.StageFinalFeedback(composite!,full.Curves,cancelledCopy?[new("DisableLeftHandPoseOverride",new(-.5f,true))]:[]);
                }
                var c=Prepare();
                if(withAdditives)
                {
                    Require(c.Additives is not null && c.Additives.Visited==visit.Visited,label+"/additive Main traversal");
                    foreach(var u in c.Additives!.Updates)
                        Require(u.Weight>=0 && u.Weight<=visit.Weight*resources.LayerGraphs.MainAdditivesAlpha && (!u.Active || visit.Active),
                            label+"/original additive branch update context");
                }
                main.Identity=label;main.Compare("Main",c.Macro.State,LyraMainObservationState.Read(row.GetProperty("observation").GetProperty("after")));
                main.Compare("Tail",c.Macro.Tail,LyraMainTailState.Read(row.GetProperty("observation").GetProperty("tailAfter")));
                main.Compare("Rules",c.Rules,NativeRules(row));
                Require(oldSync==row.GetProperty("syncValid").GetBoolean(),label+"/native previous Sync validity");
                var relevant=row.GetProperty("relevant");Require(oldRelevant.Valid==relevant.GetProperty("valid").GetBoolean(),label+"/relevant validity");
                if(oldRelevant.Valid)
                {
                    Require(resources.Catalog.Path(oldRelevant.Asset)==relevant.GetProperty("asset").GetString() && oldRelevant.Looping==relevant.GetProperty("looping").GetBoolean() &&
                        oldRelevant.PreviousValid==relevant.GetProperty("previousValid").GetBoolean(),label+"/relevant identity");
                    Exact(oldRelevant.Time,relevant,"time",label+"/relevant");Exact(oldRelevant.Length,relevant,"length",label+"/relevant");
                    Exact(oldRelevant.Previous,relevant,"previous",label+"/relevant");Exact(oldRelevant.Delta,relevant,"delta",label+"/relevant");
                }
                Require(c.Rules.PivotNotify==row.GetProperty("pivotNotify").GetBoolean(),label+"/PivotNotify");
                Exact(oldFeedback,row,"idleFeedbackBefore",label);
                var machine=c.Machine;Require(machine.BeforeState==row.GetProperty("beforeState").GetInt32(),label+"/before state");Exact(machine.BeforeElapsed,row,"beforeElapsed",label);
                Require(host.Machine.PreparedState(machine)==row.GetProperty("state").GetInt32(),label+"/selected state");Exact(host.Machine.PreparedElapsed(machine),row,"elapsed",label);
                for(var s=0;s<12;s++)
                {
                    Require(machine.Initializations[s]==row.GetProperty("initializations")[s].GetInt32(),$"{label}/initialization/{s}: {machine.Initializations[s]} != {row.GetProperty("initializations")[s]}");
                    Require(BitConverter.SingleToInt32Bits(host.Machine.PreparedWeight(machine,s))==BitConverter.SingleToInt32Bits(row.GetProperty("weights")[s].GetSingle()),label+"/weight/"+s);
                    Require(BitConverter.SingleToInt32Bits(machine.PreviousWeights[s])==BitConverter.SingleToInt32Bits(row.GetProperty("previousWeights")[s].GetSingle()),label+"/previous weight/"+s);
                }
                var active=row.GetProperty("active");var stack=host.Machine.PreparedStack(machine);Require(stack.Count==active.GetArrayLength(),label+"/stack count");
                for(var j=0;j<stack.Count;j++)
                {var a=stack.GetTransition(j);var e=active[j];Require(a.From==e.GetProperty("previous").GetInt32() && a.To==e.GetProperty("next").GetInt32(),label+"/edge endpoints");Exact(a.Duration,e,"duration",label);Exact(a.Elapsed,e,"elapsed",label);Exact(a.Alpha,e,"alpha",label);}
                var updates=row.GetProperty("updates");Require(updates.GetArrayLength()==machine.Updates.Length,label+"/update count");
                for(var j=0;j<machine.Updates.Length;j++)
                {var a=machine.Updates[j];var e=updates[j];Require(a.State==e.GetProperty("state").GetInt32() && a.Active==e.GetProperty("active").GetBoolean() && a.Inertial==e.GetProperty("inertial").GetBoolean(),label+"/update context/"+j);Exact(a.Weight,e,"weight",label);}
                var evaluate=row.TryGetProperty("output",out var output);
                Require(evaluate==(visit.Visited && frame.GetProperty("evaluate").GetBoolean()),label+"/evaluation contract");
                if(evaluate)
                {
                    Evaluate(c);
                    if(!tested)
                    {
                        var first=Enumerable.Range(0,10).First(n=>c.EvaluationRoots[n]);var hook=LyraItemLayerGraphInstance.HookForRoot(first);
                        var call=instance.Call(hook);
                        Reject(()=>instance.Output(c.Sources,call with{MainNode=call.MainNode+1}),"wrong Main node");
                        Reject(()=>instance.Output(c.Sources,call with{InstanceEpoch=8}),"wrong group epoch");
                        Reject(()=>instance.Output(c.Sources,foreign.Layers.Call(hook)),"foreign group with identical IDs");
                        Reject(()=>foreign.Layers.Output(c.Sources,foreign.Layers.Call(hook)),"foreign frame candidate");
                        foreach(var remaining in new[]{LyraLayerHook.FullBody_Aiming,LyraLayerHook.FullBodyAdditives,
                            LyraLayerHook.FullBody_SkeletalControls,LyraLayerHook.LeftHandPose_OverrideState})
                            Reject(()=>instance.Output(c.Sources,instance.Call(remaining)),"unsupported original closure "+remaining);
                        if(full is not null)
                        {
                            var leftCall=instance.Call(LyraLayerHook.LeftHandPose_OverrideState);
                            var left=composite!.Left;
                            void InvokeLeft(LyraItemLayerGraphInstance group,LyraLayerInvocation invocation,LyraLeftHandLayerCandidate candidate)
                            {
                                var layerInput=new LyraLayerPoseInput(resources.Catalog.Bank,host.Pose,host.Curves,host.Attributes,host.RootMotion);
                                group.EvaluateLeftHand(c.Sources,candidate,invocation,layerInput);
                            }
                            Reject(()=>InvokeLeft(instance,leftCall with{MainNode=leftCall.MainNode+1},left),"wrong input-pose Main node");
                            Reject(()=>InvokeLeft(instance,leftCall with{InstanceEpoch=8},left),"wrong input-pose epoch");
                            Reject(()=>InvokeLeft(instance,foreign.Layers.Call(LyraLayerHook.LeftHandPose_OverrideState),left),"foreign input-pose group");
                            Reject(()=>InvokeLeft(foreign.Layers,foreign.Layers.Call(LyraLayerHook.LeftHandPose_OverrideState),left),"foreign input-pose frame");
                            Reject(()=>InvokeLeft(instance,instance.Call(LyraLayerHook.FullBody_Aiming),left),"wrong input-pose signature");
                            Reject(()=>InvokeLeft(instance,leftCall,left with{}),"copied left-hand candidate identity");
                        }
                        if(withAdditives)
                        {
                            var additive=c.Additives!;var additiveCall=instance.Call(LyraLayerHook.FullBodyAdditives);
                            Reject(()=>instance.EvaluateAdditives(c.Sources,additive,additiveCall with{MainNode=additiveCall.MainNode+1}),"wrong additive Main node");
                            Reject(()=>instance.EvaluateAdditives(c.Sources,additive,additiveCall with{InstanceEpoch=8}),"wrong additive epoch");
                            Reject(()=>instance.EvaluateAdditives(c.Sources,additive,foreign.Layers.Call(LyraLayerHook.FullBodyAdditives)),"foreign additive group");
                            Reject(()=>foreign.Layers.EvaluateAdditives(c.Sources,additive,foreign.Layers.Call(LyraLayerHook.FullBodyAdditives)),"foreign additive frame");
                            Reject(()=>instance.EvaluateAdditives(c.Sources,additive,instance.Call(LyraLayerHook.FullBody_Aiming)),"wrong additive signature");
                            Reject(()=>instance.EvaluateAdditives(c.Sources,additive with{},additiveCall),"copied additive candidate identity");
                        }
                        tested=true;
                    }
                    if(retryFrame)
                    {
                        var first=Enumerable.Range(0,10).First(n=>c.EvaluationRoots[n]);
                        var discarded=instance.Output(c.Sources,instance.Call(LyraItemLayerGraphInstance.HookForRoot(first)));
                        Stage(c,true);Cancel();Require(LyraMainLocomotionHostSmoke.Snapshot(host)==before,label+"/late cancellation published");
                        if(full is not null)Require(instance.CommittedCurve("DisableLeftHandPoseOverride")==0 && instance.LeftHandWeight==0,label+"/late linked control copy published");
                        if(withAdditives)Require(instance.AdditivesState==oldAdditiveState && instance.AdditivesElapsed==oldAdditiveElapsed,label+"/late additive history published");
                        c=Prepare();Evaluate(c);retries++;
                        Reject(()=>{_ = discarded.Pose.Length;},"old view after cancellation and retry");
                    }
                    var er=row.GetProperty("evaluatedRoots").EnumerateArray().ToArray();
                    Require(er.Select(e=>LyraMainLocomotionHost.RootForState(e.GetProperty("state").GetInt32())).Order().SequenceEqual(Enumerable.Range(0,10).Where(n=>c.EvaluationRoots[n])),label+"/evaluated roots");
                    foreach(var e in er)
                    {
                        var n=LyraMainLocomotionHost.RootForState(e.GetProperty("state").GetInt32());var expected=e.GetProperty("output");
                        var layer=instance.MainRootOutput(c.Sources,instance.Call(LyraItemLayerGraphInstance.HookForRoot(n)));
                        rootCompare.Compare(expected,layer.Pose,layer.Curves,layer.Attributes,label+"/root/"+n);
                        RootMotion(layer.RootMotion,expected,label+"/root/"+n);rootPoses++;roots.Add(n);layerCalls++;
                    }
                    mixed.Compare(output,host.Pose,host.Curves,host.Attributes,label+"/mixed");RootMotion(host.RootMotion,output,label+"/mixed");
                    if(full is not null)
                    {
                        // The real three Provider defaults disable the override
                        // and normal locomotion authors no disabling curve.
                        // This actual post-layer boundary must preserve every
                        // channel; nonzero callbacks are checked independently
                        // against the complete original four-node layer trace.
                        Require(composite!.Left.Weight==0 && full.Pose.SequenceEqual(host.Pose) && full.Curves.SequenceEqual(host.Curves) &&
                            full.Attributes.SequenceEqual(host.Attributes) && full.RootMotion==host.RootMotion,label+"/original disabled left-hand boundary");leftPoses++;
                    }
                    if(withAdditives)
                    {
                        var additive=full!.Additives;
                        foreach(var bone in additive.Pose)bone.Validate(.001);
                        Require(additive.Pose.Length==81 && additive.Curves.Length==resources.Catalog.Bank.Curves.Names.Length &&
                            additive.Attributes.Length==resources.Catalog.Bank.Curves.Attributes.Layout.Length,label+"/complete additive channels");
                        recoveryPoses+=additive.Pose.ToArray().Any(b=>b.Position.LengthSquared>1e-10 || Math.Abs(b.Rotation.W)<.99999)?1:0;additivePoses++;
                    }
                    Stage(c);poses++;
                }
                else
                {
                    sparse++;
                    if(retryFrame){Cancel();Require(LyraMainLocomotionHostSmoke.Snapshot(host)==before,label+"/update-only cancellation published");
                        if(withAdditives)Require(instance.AdditivesState==oldAdditiveState && instance.AdditivesElapsed==oldAdditiveElapsed,label+"/sparse additive history published");c=Prepare();retries++;}
                }
                LyraLayerPoseView? completed=evaluate && retryFrame?instance.Output(c.Sources,
                    instance.Call(LyraItemLayerGraphInstance.HookForRoot(Enumerable.Range(0,10).First(n=>c.EvaluationRoots[n])))):null;
                if(full is null)host.Commit(c,!evaluate);else full.Commit(composite!,!evaluate);
                if(withAdditives)Require(instance.AdditivesState==c.Additives!.State && instance.AdditivesElapsed==c.Additives.Elapsed,label+"/common additive commit");
                Exact(host.Sources.Hosts.Idle.TurnYawFeedback,row,"idleFeedbackAfter",label);
                if(completed is {} oldView)Reject(()=>{_ = oldView.Curves.Length;},"view after commit");
                Require(!evaluate || host.Sources.Hosts.Idle.TurnYawFeedback==host.Sources.MainFeedback.Weight,label+"/Main to Linked curve copy");
                copied+=host.Sources.Hosts.Idle.TurnYawFeedback!=oldFeedback?1:0;
                var nativeSources=row.GetProperty("sources").EnumerateArray().ToDictionary(e=>e.GetProperty("node").GetInt32());
                foreach(var p in host.SyncPlayers)
                {
                    // This retained native fixture stops at LocomotionSM; its
                    // node5 row is an unvisited Manny recovery occurrence.
                    // The newly enrolled ALS81 occurrence is independently
                    // checked by the complete additive native fixture.
                    if(withAdditives && p.PlayerId-700==5)
                    {Require(p.AssetId==resources.Catalog.RecoveryId(profile) && p.Epoch==7 && p.SampleCount==1,label+"/common ALS81 recovery identity");recoveryTicks++;continue;}
                    if(!nativeSources.TryGetValue(p.PlayerId-700,out var e))continue;
                    Require(resources.Catalog.Path(p.AssetId)==e.GetProperty("asset").GetString(),label+"/source asset/"+p.PlayerId);
                    Exact(p.Time,e,"time",label+"/source/"+p.PlayerId);Exact(p.DeltaPrevious,e,"previous",label+"/source/"+p.PlayerId);Exact(p.Delta,e,"delta",label+"/source/"+p.PlayerId);
                    Require(p.Marker.PreviousIndex==e.GetProperty("markerPrevious").GetInt32() && p.Marker.NextIndex==e.GetProperty("markerNext").GetInt32(),label+"/source marker/"+p.PlayerId);
                    MarkerDistance(p.Marker.PreviousIndex==-2?0:p.Marker.PreviousDistance,e,"markerPreviousDistance",label+"/source/"+p.PlayerId);
                    MarkerDistance(p.Marker.NextIndex==-2?0:p.Marker.NextDistance,e,"markerNextDistance",label+"/source/"+p.PlayerId);sourceClocks++;
                }
                states.Add(host.Machine.State);transitions+=machine.Selected is not null?1:0;frames++;hidden+=visit.Visited?0:1;
            }
        }
        mixed.Finish();rootCompare.Finish();Require(frames==11340 && poses==9762 && states.Count==10 && roots.Count==10 && copied>0 && sourceClocks>0 && retries>0,"Incomplete continuous Main ALS coverage");
        GD.Print($"LYRA_MAIN_ALS_NATIVE_GODOT_OK frames={frames} poses={poses} rootPoses={rootPoses} states={states.Count} roots={roots.Count} transitions={transitions} sourceClocks={sourceClocks} updateOnly={sparse} hidden={hidden} feedbackChanges={copied} lateRetries={retries} mainVectorCm={main.MaxVector:R} ownRules=true ownSync=true nativeJoint=true boundary=LocomotionSM fixedProvider=true finalLayers=false production=false");
        Require(layerCalls==12595 && layerRejected>=162,"Incomplete typed group execution validation");
        GD.Print($"LYRA_ITEM_LAYER_EXECUTION_OK frames={frames} calls={layerCalls} entries=10 contractEntries=14 rejected={layerRejected} group=ItemAnimLayers sharedClocks=true poseChannels=complete finalLayers=false production=false");
        if(withLeft){Require(leftPoses==9762,"Incomplete Main left-hand boundary");GD.Print($"LYRA_MAIN_LEFT_HAND_PIPELINE_OK frames={frames} poses={leftPoses} graphEntries=11 ownSync=true lateRetries={retries} boundary=LeftHandPose_OverrideState fixedProvider=true nativeMainAndLayerComponents=true nativeJointBoundary=false production=false");}
        if(withAdditives){Require(additivePoses==9762 && recoveryTicks>0 && recoveryPoses>0,"Incomplete Main additive entry");GD.Print($"LYRA_MAIN_ADDITIVES_PIPELINE_OK frames={frames} poses={additivePoses} recoveryTicks={recoveryTicks} recoveryPoses={recoveryPoses} graphEntries=12 ownSync=true lateRetries={retries} originalUpdateWeight=true commonCommit=true appliedAfterAiming=false nativeJointBoundary=false production=false");}
    }
}
