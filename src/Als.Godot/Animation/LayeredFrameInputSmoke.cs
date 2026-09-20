using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NVector3=System.Numerics.Vector3;
using NMatrix=System.Numerics.Matrix4x4;

namespace GodotAls.Animation;

public partial class LayeredFrameInputSmoke : Node, IAlsGroundedFrameRuntimeSink
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch(Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private void Run()
    {
        var set=ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
        var profile=AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"),set);
        var pose=AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"),set,profile);
        var definition=AlsMovementGraphDefinition.Load(set,profile,pose).WithSharedOverlaySources(set);
        var settings=AlsLocomotionInputCompiler.Compile(Read("v4_locomotion_inputs.json")).Movement;
        if(OS.GetCmdlineUserArgs().Contains("--full-graph-capture"))
        { AlsFullGraphParityCapture.Run(this,set,profile,pose,definition.WithSharedRootSources(set),settings,this); return; }
        if(OS.GetCmdlineUserArgs().Contains("--unvisited-upper")) {RunUpperLifecycle(set,profile,pose,definition); return;}
        if(OS.GetCmdlineUserArgs().Contains("--unvisited-feet")) {RunFootLifecycle(pose,definition); return;}
        if(OS.GetCmdlineUserArgs().Contains("--root-dispatch")) {RunRootDispatch(set,profile,pose,definition.WithSharedRootSources(set),settings); return;}
        var frames=0; var airFrames=0; var crouched=0; var events=0; var failures=0; var nonzeroAcceleration=0;
        foreach(var hz in new[]{30,60,120})
        {
            using var library=AlsAnimationLibraryBuilder.BuildP5a(set,definition.Binding);
            library.UseMovementSources(set,definition.RawSources); AddChild(library.Root);
            using var graph=AlsLocomotionGraphBuilder.Build(library,profile,pose,set,definition.Binding);
            using var owner=new AlsLayeredAnimationFrameRuntime(definition,library,graph.StandingCycle!,set,pose,11,2);
            for(var frame=1;frame<=hz*6;frame++)
            {
                var seconds=frame/(float)hz; var delta=1f/hz; var id=new AlsFrameIdentity(frame,11,2);
                var airborne=seconds is >=2 and <2.8f; var moving=seconds is >.3f and <4.8f;
                var velocity=moving ? new NVector3(2*MathF.Sin(seconds*2),airborne ? -3 : 0,-3) : NVector3.Zero;
                var mode=seconds<1 ? AlsRotationMode.Aiming : seconds<3 ? AlsRotationMode.LookingDirection : AlsRotationMode.VelocityDirection;
                var input=AlsFrameInput.CreateDefault(id,delta) with
                {
                    ActualVelocity=velocity,ActualAcceleration=new(3,1,-4),MaxAcceleration=8,MaxBrakingDeceleration=16,
                    InputDirection=moving ? new(.6f,0,-.8f) : default,
                    CharacterTransform=NMatrix.CreateRotationY(.6f),CharacterYaw=.6f,
                    Floor=new(airborne ? (byte)0 : (byte)1,NVector3.UnitY,-1,NMatrix.Identity,default),
                    LandPrediction=new AlsLandPredictionSample{Queried=1,BlockingHit=1,Walkable=1,Time=.25f},
                    Stance=seconds>=4 ? AlsStance.Crouching : AlsStance.Standing,RotationMode=mode
                };
                input=input with {Command=input.Command with {AimYaw=MathF.Sin(seconds*2),AimPitch=MathF.Cos(seconds)*.9f}};
                var result=AlsFrameResult.CreateDefault(id);
                result.ActualStance=input.Stance; result.ActualGait=AlsGait.Running; result.ActualRotationMode=mode;
                result.ResolvedLocomotionState=airborne ? AlsLocomotionState.InAir : AlsLocomotionState.Grounded;
                var local=NVector3.Transform(velocity,System.Numerics.Quaternion.CreateFromAxisAngle(NVector3.UnitY,-.6f));
                result.BlendCoordinates=new(local.X,-local.Z);
                var movement=AlsStandingMovementInputModel.Evaluate(id,velocity,moving ? 1 : 0,settings);
                var rules=new AlsGroundedRuleInput(movement.ShouldMove,false,false,input.Stance,true,false,0,0)
                {MovementState=airborne ? AlsMovementStateInput.InAir : AlsMovementStateInput.Grounded,HasMovementInput=movement.HasMovementInput,Speed=movement.Speed};
                var ground=new AlsGroundedFrameInputs(delta,new(1.75f,3.75f,6.5f),1,1,1,default,default,AlsSlotWeights.Passthrough,
                    new(0,0),new(0,0),new((short)frame,(ulong)frame));
                var overlay=seconds<2 ? AlsOverlayKind.Rifle : seconds<4 ? AlsOverlayKind.Bow : AlsOverlayKind.Barrel;
                var oldIdentity=owner.CommittedIdentity; var oldCurves=owner.CommittedCurves.ToArray();
                var oldAim=owner.CommittedAimInput; var oldGround=owner.Base.CommittedGroundInput;
                var oldAir=owner.Base.CommittedGlobalInput; var oldSync=owner.Base.CommittedSources;
                var feedback=oldIdentity==default ? default : AlsAnimationInputFeedback.FromCompletedFrame(oldIdentity,owner.CurveNames,oldCurves);
                var expectedGround=definition.GroundedInput.Evaluate(input,movement,result.ActualGait,oldGround,oldAir.Lean,feedback,1);
                var expectedAim=definition.AimingInput.Evaluate(input,mode,movement.HasMovementInput,oldAim);
                Prepare();
                Require(owner.Base.CandidateGroundInput==expectedGround.State,"Ground input did not consume previous final feedback.");
                Require(owner.CandidateAimInput==expectedAim && owner.Base.CandidateAimingInput==expectedAim,"Aim was updated twice or read stale state.");
                var values=owner.CandidateOverlayValues; var acceleration=expectedGround.State.RelativeAcceleration;
                Require(values.Velocity==expectedGround.State.VelocityBlend && values.AccelerationX==-acceleration.Z &&
                    values.AccelerationY==acceleration.X && values.AccelerationZ==acceleration.Y &&
                    values.LandPrediction==owner.Base.CandidateGlobalInput.LandPrediction,"Overlay did not consume mapped global properties in UE axes.");
                if(acceleration.LengthSquared()>0) nonzeroAcceleration++;
                owner.Evaluate(AlsLocalPose.Identity,0,0);
                var finalPose=owner.Pose.ToArray(); var finalCurves=owner.Curves.ToArray();
                var sync=owner.Base.Sources; var sourceEvents=owner.Base.SourceEvents;
                owner.Discard(); CheckOld();
                if(frame%109==0)
                {
                    Prepare();
                    try {owner.Evaluate(AlsLocalPose.Identity with {Position=new(float.NaN,0,0)},0,0);throw new Exception("Expected mapped frame rejection.");}
                    catch(ArgumentException){failures++;}
                    CheckOld();
                }
                Prepare(); owner.Evaluate(AlsLocalPose.Identity,0,0);
                Require(owner.Pose.SequenceEqual(finalPose) && owner.Curves.SequenceEqual(finalCurves) &&
                    StandingCycleSmoke.SameSync(owner.Base.Sources,sync) && owner.Base.SourceEvents.Count==sourceEvents.Count,"Mapped frame retry differs.");
                for(var e=0;e<sourceEvents.Count;e++) Require(owner.Base.SourceEvents[e]==sourceEvents[e],"Mapped event retry differs.");
                owner.Commit(id); Require(owner.CommittedCurves.SequenceEqual(finalCurves),"Mapped feedback did not commit final curves.");
                frames++; events+=sourceEvents.Count; if(airborne)airFrames++; if(input.Stance==AlsStance.Crouching)crouched++;

                void Prepare()=>owner.PrepareFromFrame(input,result,movement,rules,ground,new(id,1,delta),this,AlsLocalPose.Identity,0,0,overlay);
                void CheckOld()=>Require(owner.CommittedIdentity==oldIdentity && owner.Base.CommittedIdentity==oldIdentity &&
                    owner.CommittedCurves.SequenceEqual(oldCurves) && owner.CommittedAimInput==oldAim && owner.Base.CommittedAimingInput==oldAim &&
                    owner.Base.CommittedGroundInput==oldGround && owner.Base.CommittedGlobalInput==oldAir &&
                    StandingCycleSmoke.SameSync(owner.Base.CommittedSources,oldSync),"Mapped rejected frame leaked committed history.");
            }
        }
        Require(frames==1260 && airFrames>0 && crouched>0 && events>0 && failures>0 && nonzeroAcceleration>0,"Incomplete mapped frame coverage.");
        GD.Print($"LAYERED_FRAME_INPUT_OK frames={frames} airborne={airFrames} crouched={crouched} events={events} retries={frames} failures={failures} acceleration={nonzeroAcceleration} global_aim=once feedback=final");
    }
    public void UpdateGroundedSlot(int slot,in AlsSlotWeights weights,in AlsSlotSourceUpdate source,in AlsPoseUpdateContext context)
    {if(weights!=AlsSlotWeights.Passthrough)throw new InvalidOperationException("Escaped physical Grounded slot.");}
    public void RefreshSourceBones(int cache) { }
    public void RequestInertialization(in AlsPoseUpdateContext context,float seconds)=>throw new InvalidOperationException("Escaped BaseLayer inertialization.");
    public void OnCachedUpdatesSkipped(int handler,ReadOnlySpan<AlsPoseUpdateContext> skipped)=>throw new InvalidOperationException("Unknown skipped update handler.");
    private void RunUpperLifecycle(AlsAnimationSetDefinition set,AlsLocomotionAnimationProfile profile,
        AlsPoseAnimationProfile pose,AlsMovementGraphDefinition definition)
    {
        var frames=0; var hidden=0; var resets=0; var boneRefreshes=0; var faults=0;
        foreach(var hz in new[]{30,60,120})
        {
            using var library=AlsAnimationLibraryBuilder.BuildP5a(set,definition.Binding);
            library.UseMovementSources(set,definition.RawSources); AddChild(library.Root);
            using var graph=AlsLocomotionGraphBuilder.Build(library,profile,pose,set,definition.Binding);
            using var layout=new AlsLayeredAnimationFrameRuntime(definition,library,graph.StandingCycle!,set,pose,11,2);
            var names=layout.CurveNames.ToArray(); var feedback=new AlsInertialCurve[names.Length];
            var upper=new AlsAimLayerFrameStage(definition,set,names,11,2);
            var post=new AlsLayerBlendingFrameStage(definition,set,library,names,layout.Base.CurveNames);
            var source=new UpperSource(post,layout.Base.ReferencePose.ToArray(),new AlsInertialCurve[layout.Base.CurveNames.Length],feedback);
            var committed=default(AlsAnimationGraphFrame);
            for(var frame=1;frame<=hz*3;frame++)
            {
                var seconds=frame/(float)hz; var hide=seconds<.3f || seconds is >=1 and <1.8f;
                var id=new AlsFrameIdentity(frame+(seconds>=2.2f ? 1000 : 0),11,2);
                var traversal=committed.Next(id,(ulong)frame);
                if(frame==1)traversal=traversal with {Update=new(short.MaxValue,1)};
                if(frame==hz*6/5)traversal=traversal with {Bones=traversal.Bones.Next((ulong)frame)};
                if(frame==hz*13/10)traversal=traversal with {Initialization=traversal.Initialization.Next((ulong)frame)};
                var initialize=!traversal.Initialization.MatchesCounter(committed.Initialization);
                var context=new AlsPoseUpdateContext(id,1,1f/hz);
                var input=definition.LayeringInput.Evaluate(id,committed.Identity,committed.Identity==default ? [] : names,
                    committed.Identity==default ? [] : feedback) with {EnableAimOffset=.7,BasePoseNormal=1};
                var observation=new AlsAimingObservation(id,context.Delta,AlsRotationMode.LookingDirection,true,
                    default,new(30*Math.Sin(seconds),80*Math.Cos(seconds),0),3,0);
                var oldAim=upper.CommittedAim; var oldInput=upper.CommittedInput; var oldBases=post.CommittedBasePosesState;
                Prepare();
                if(hide)
                {
                    Require(!upper.AimRelevant && upper.PostSourceEvaluations==0 && upper.PostCacheReads==0 && post.InputUpdateOrder.IsEmpty,
                        "Unvisited upper graph updated or evaluated a source.");
                    Require(post.BasePosesState.Normal.UpdateCount==oldBases.Normal.UpdateCount &&
                        post.BasePosesState.Crouching.UpdateCount==oldBases.Crouching.UpdateCount &&
                        post.BasePosesState.Normal.EvaluationCount==oldBases.Normal.EvaluationCount,
                        "Unvisited BasePoses advanced its evaluators.");
                    if(initialize)
                    {
                        Require(post.BasePosesState.Normal.InitializationEpoch==oldBases.Normal.InitializationEpoch+1 &&
                            !upper.CandidateAim.GetMachine(AlsAimMachineKind.Behavior).Updated,"Hidden initialization failed to reach upper sources."); resets++;
                    }
                    else for(var e=0;e<7;e++)Require(upper.CandidateAim.GetEvaluator(e)==oldAim.GetEvaluator(e),"Hidden Aim changed evaluator history.");
                    if(frame==hz*6/5)
                    {
                        Require(post.BasePosesState.Bones==traversal.Bones && post.BasePosesState.Normal.InitializationEpoch==oldBases.Normal.InitializationEpoch,
                            "Hidden CacheBones reset BasePoses."); boneRefreshes++;
                    }
                    Reject(()=>upper.Evaluate(source)); Reject(()=>{_=post.BaseContext;}); hidden++;
                }
                else upper.Evaluate(source);
                var nextAim=upper.CandidateAim; var nextBases=post.BasePosesState;
                var output=hide ? [] : upper.Pose.ToArray(); var curves=hide ? [] : upper.Curves.ToArray();
                Cancel(); CheckOld();
                if(!hide && frame%47==0)
                {
                    Prepare(); source.Fail=true; Reject(()=>upper.Evaluate(source)); source.Fail=false;
                    Cancel(); CheckOld(); faults++;
                }
                Prepare(); if(!hide)upper.Evaluate(source);
                AimFrameSmokeChecks.Same(nextAim,upper.CandidateAim);
                Require(post.BasePosesState==nextBases,"Upper initialization/evaluator retry changed history.");
                if(!hide)Require(upper.Pose.SequenceEqual(output) && upper.Curves.SequenceEqual(curves),"Upper resumed pose retry differs.");
                upper.ValidateCommit(id); post.ValidateCommit(id); upper.Commit(id); post.Commit(id);
                Require(upper.CommittedIdentity==id && post.CommittedIdentity==id && upper.CommittedInput.Identity==id,"Hidden upper commit lost global identity.");
                if(!hide)curves.CopyTo(feedback,0);
                committed=traversal; frames++;
                void Prepare()
                {
                    upper.Prepare(context,input,observation,feedback,traversal,updateSource:!hide);
                    post.Prepare(hide ? context : upper.PostContext,input,committed.Identity==default ? [] : feedback,traversal,updateSource:!hide);
                }
                void Cancel(){upper.Cancel(); post.Cancel();}
                void CheckOld()
                {
                    Require(upper.CommittedInput==oldInput && post.CommittedBasePosesState==oldBases && upper.CommittedIdentity==committed.Identity,
                        "Cancelled upper candidate leaked history."); AimFrameSmokeChecks.Same(oldAim,upper.CommittedAim);
                }
            }
        }
        Require(frames==630 && hidden>0 && resets==6 && boneRefreshes==3 && faults>0,"Upper lifecycle coverage missing.");
        GD.Print($"UPPER_UNVISITED_LIFECYCLE_OK frames={frames} hidden={hidden} initializations={resets} bone_refreshes={boneRefreshes} faults={faults} retry_every_frame=true sources=real_aim_baseposes base_overlay=controlled root_dispatch=pending");
        static void Reject(Action action)
        {
            try{action();}catch(InvalidOperationException){return;}
            throw new Exception("Expected unvisited upper rejection or injected source failure.");
        }
    }
    private void RunRootDispatch(AlsAnimationSetDefinition set, AlsLocomotionAnimationProfile profile, AlsPoseAnimationProfile poseProfile,
        AlsMovementGraphDefinition definition, AlsStandingMovementSettings settings)
    {
        var skeleton=definition.OverlayRawSources.GetSkeleton(poseProfile.SkeletonId);
        var mesh=set.SkeletalMeshes[definition.MannequinMeshId];
        var boneNames=skeleton.LogicalBoneNames.ToArray();
        var left=Find("ik_foot_l"); var right=Find("ik_foot_r"); var rootBone=Find("root");
        var frames=0; var hidden=0; var mixed=0; var flail=0; var snapshots=0; var failures=0; var observationsRejected=0;
        var sourcesSeen=0; var sourceEvents=0; var resumed=0;
        foreach(var hz in new[]{30,60,120})
        {
            using var library=AlsAnimationLibraryBuilder.BuildP5a(set,definition.Binding);
            library.UseMovementSources(set,definition.RawSources); AddChild(library.Root);
            using var graph=AlsLocomotionGraphBuilder.Build(library,profile,poseProfile,set,definition.Binding);
            using var owner=new AlsLayeredAnimationFrameRuntime(definition,library,graph.StandingCycle!,set,poseProfile,11,2,true);
            var lastPose=skeleton.ReferencePose.ToArray(); var components=new AlsPrecisePose[lastPose.Length];
            var previousState=AlsLocomotionState.Grounded; var wasHidden=false;
            AlsNamedPoseSnapshot? snapshot=null;
            for(var frame=1;frame<=hz*4;frame++)
            {
                var id=new AlsFrameIdentity(frame,11,2); var delta=1f/hz; var seconds=(frame-1)/(float)hz;
                var state=seconds<.4f || seconds is >=1.2f and <2 || seconds is >=3 and <3.7f ? AlsLocomotionState.Ragdoll :
                    seconds is >=2 and <2.4f ? AlsLocomotionState.InAir : seconds is >=2.7f and <3 ? AlsLocomotionState.Mantling : AlsLocomotionState.Grounded;
                if(previousState==AlsLocomotionState.Ragdoll && state!=previousState)
                {
                    var physical=new AlsLocalPose[skeleton.PhysicalBoneCount];
                    for(var bone=0;bone<physical.Length;bone++)physical[bone]=lastPose[skeleton.PhysicalToLogical[bone]];
                    snapshot=new(id,definition.RagdollPose.SnapshotName,mesh.Name,skeleton.RawBoneNames,physical);
                }
                for(var bone=0;bone<lastPose.Length;bone++)
                {
                    var parent=skeleton.LogicalParents[bone];
                    components[bone]=parent<0 ? new(lastPose[bone]) : AlsPrecisePose.Compose(new(lastPose[bone]),components[parent]);
                }
                var velocity=new NVector3(1,state==AlsLocomotionState.InAir ? -3 : 0,-3);
                var input=AlsFrameInput.CreateDefault(id,delta) with
                {
                    ActualVelocity=velocity,ActualAcceleration=new(2,0,-3),MaxAcceleration=8,MaxBrakingDeceleration=16,
                    InputDirection=new(.2f,0,-.8f),Floor=new((byte)(state==AlsLocomotionState.InAir ? 0 : 1),NVector3.UnitY,-1,NMatrix.Identity,default),
                    LandPrediction=new(){Queried=1,BlockingHit=1,Walkable=1,Time=.3f},
                    FootIk=new(1,owner.CommittedIdentity,AlsLocalPose.Identity,components[left].ToSingle(),components[right].ToSingle(),
                        components[rootBone].Position.ToSingle(),System.Numerics.Quaternion.Identity,velocity,delta),
                    LeftFootHit=AlsFootHit.Invalid with {Valid=1,Walkable=1,Position=components[left].Position.ToSingle()},
                    RightFootHit=AlsFootHit.Invalid with {Valid=1,Walkable=1,Position=components[right].Position.ToSingle()}
                };
                input=input with {Command=input.Command with {AimYaw=.4f*MathF.Sin(seconds),AimPitch=.3f*MathF.Cos(seconds)}};
                var result=AlsFrameResult.CreateDefault(id); result.ResolvedLocomotionState=state;
                result.ActualStance=seconds>=2.5f ? AlsStance.Crouching : AlsStance.Standing;
                result.ActualGait=AlsGait.Running; result.ActualRotationMode=AlsRotationMode.LookingDirection;
                result.BlendCoordinates=new(velocity.X,-velocity.Z); result.PlayRate=result.Stride=1;
                var movement=AlsStandingMovementInputModel.Evaluate(id,velocity,1,settings);
                var rules=new AlsGroundedRuleInput(movement.ShouldMove,false,false,result.ActualStance,true,false,0,1)
                    {HasMovementInput=true,Speed=movement.Speed,FeetCrossing=1};
                var ground=new AlsGroundedFrameInputs(delta,new(1.75f,3.75f,6.5f),1,1,1,default,default,
                    AlsSlotWeights.Passthrough,new(0,0),new(0,0),new((short)frame,(ulong)frame));
                var observation=new AlsRagdollFrameObservation(id,new(120+frame%7*30,40,350),snapshot);
                var oldId=owner.CommittedIdentity; var oldSources=owner.Base.CommittedSources; var oldFeet=owner.CommittedFeet;
                var oldRagdoll=owner.CommittedRagdoll; var oldRoot=owner.CommittedRoot;
                var oldAim=owner.CommittedAim; var oldBasePoses=owner.CommittedBasePoses;
                var oldNormalId=owner.CommittedNormalPoseIdentity; var oldFootId=owner.CommittedFootPoseIdentity;
                var oldCurves=owner.CommittedCurves.ToArray();
                Prepare(observation); owner.Evaluate(AlsLocalPose.Identity,0,0);
                var normal=owner.NormalVisited; var ragdoll=owner.RagdollVisited;
                var expectedPose=owner.Pose.ToArray(); var expectedCurves=owner.Curves.ToArray(); var expectedFeet=owner.CandidateFeet;
                var expectedSources=owner.Base.Sources; var expectedEvents=owner.Base.SourceEvents;
                var expectedRagdoll=owner.CandidateRagdoll; var expectedAim=owner.CandidateAimInput;
                if(!normal)
                {
                    Require(owner.PostCacheReads==0 && owner.PostSourceEvaluations==0 && expectedSources.PlayerCount==1 && expectedSources.SampleCount==1,
                        "Hidden normal graph updated sources or evaluated a cached pose."); hidden++;
                }
                if(normal && ragdoll)mixed++;
                if(ragdoll && expectedRagdoll.State==0)
                {
                    Require(expectedRagdoll.PlayerTicked && expectedRagdoll.FlailRate==definition.RagdollFrame.Input.FlailRate(observation.RootPhysicsVelocityCm),
                        "Root did not use its physics observation or shared Flail tick."); flail++;
                }
                if(ragdoll && expectedRagdoll.State==1){Require(snapshot is not null && !expectedRagdoll.PlayerTicked,"Recovery snapshot unexpectedly ticked Flail."); snapshots++;}
                Require(expectedSources.Epochs[definition.RootSharedSources.RagdollPlayerId]==expectedRagdoll.PlayerEpoch &&
                    expectedSources.Times[definition.RootSharedSources.RagdollPlayerId]==expectedRagdoll.Time,"Root and shared Flail histories diverged.");
                owner.Discard(); CheckOld();
                if(state==AlsLocomotionState.Ragdoll && frame%31==1)
                {
                    try {Prepare(null); throw new Exception("Expected missing root observation rejection.");}
                    catch(InvalidOperationException e) when(e.Message=="Ragdoll root traversal requires a captured scene observation."){observationsRejected++;}
                    CheckOld();
                }
                if(ragdoll && expectedRagdoll.State==1 && frame%13==0)
                {
                    var foreign=new AlsNamedPoseSnapshot(new(frame,12,2),definition.RagdollPose.SnapshotName,mesh.Name,snapshot!.BoneNames,snapshot.LocalPoses);
                    Prepare(observation with {Snapshot=foreign});
                    try {owner.Evaluate(AlsLocalPose.Identity,0,0); throw new Exception("Expected late foreign snapshot rejection.");}
                    catch(ArgumentException){failures++;}
                    CheckOld();
                }
                Prepare(observation); owner.Evaluate(AlsLocalPose.Identity,0,0);
                Require(owner.Pose.SequenceEqual(expectedPose) && owner.Curves.SequenceEqual(expectedCurves) &&
                    owner.CandidateFeet==expectedFeet && owner.CandidateRagdoll==expectedRagdoll && owner.CandidateAimInput==expectedAim &&
                    StandingCycleSmoke.SameSync(owner.Base.Sources,expectedSources) && owner.Base.SourceEvents.Count==expectedEvents.Count,
                    "Root dispatch retry changed pose, curves, properties, clocks or events.");
                for(var e=0;e<expectedEvents.Count;e++)Require(owner.Base.SourceEvents[e]==expectedEvents[e],"Root source event retry differs.");
                owner.ValidateCommit(id); owner.Commit(id);
                Require(owner.CommittedIdentity==id && owner.Base.CommittedIdentity==id && owner.CommittedRootIdentity==id && owner.CommittedRagdoll.Traversal.Identity==id &&
                    owner.CommittedNormalPoseIdentity==(normal ? id : oldNormalId) && owner.CommittedFootPoseIdentity==(normal ? id : oldFootId),
                    "Root children did not commit global and visited-pose identities together.");
                if(!normal)
                {
                    var bases=owner.CommittedBasePoses;
                    Require(bases.Normal.UpdateCount==oldBasePoses.Normal.UpdateCount && bases.Crouching.UpdateCount==oldBasePoses.Crouching.UpdateCount &&
                        bases.Normal.EvaluationCount==oldBasePoses.Normal.EvaluationCount && bases.Crouching.EvaluationCount==oldBasePoses.Crouching.EvaluationCount &&
                        bases.Normal.Time==oldBasePoses.Normal.Time && bases.Crouching.Time==oldBasePoses.Crouching.Time &&
                        bases.Normal.InitializationEpoch==oldBasePoses.Normal.InitializationEpoch+(oldId==default ? 1 : 0) &&
                        bases.Crouching.InitializationEpoch==oldBasePoses.Crouching.InitializationEpoch+(oldId==default ? 1 : 0),
                        "Hidden normal branch changed BasePoses outside its initialization lifecycle.");
                    if(oldId!=default)Require(bases==oldBasePoses with {Identity=id},"Hidden normal branch changed retained BasePoses node inputs.");
                }
                if(normal && wasHidden)resumed++;
                expectedPose.CopyTo(lastPose,0); previousState=state; wasHidden=!normal; frames++;
                sourcesSeen+=expectedSources.PlayerCount; sourceEvents+=expectedEvents.Count;
                void Prepare(AlsRagdollFrameObservation? observed)=>owner.PrepareFromFrame(input,result,movement,rules,ground,new(id,1,delta),this,
                    AlsLocalPose.Identity,0,0,seconds<2.5f ? AlsOverlayKind.Rifle : AlsOverlayKind.Barrel,ragdollObservation:observed);
                void CheckOld()
                {
                    Require(owner.CommittedIdentity==oldId && owner.Base.CommittedIdentity==oldId && owner.CommittedFeet==oldFeet &&
                        owner.CommittedRagdoll==oldRagdoll && owner.CommittedRoot==oldRoot && owner.CommittedBasePoses==oldBasePoses &&
                        owner.CommittedNormalPoseIdentity==oldNormalId && owner.CommittedFootPoseIdentity==oldFootId &&
                        owner.CommittedCurves.SequenceEqual(oldCurves) && StandingCycleSmoke.SameSync(owner.Base.CommittedSources,oldSources),
                        "Failed root candidate leaked a committed bank.");
                    AimFrameSmokeChecks.Same(oldAim,owner.CommittedAim);
                }
            }
        }
        Require(frames==840 && hidden>0 && mixed>0 && flail>0 && snapshots>0 && failures>0 && observationsRejected>0 && sourceEvents>0 && resumed==9,
            $"Root dispatch coverage missing: frames={frames} hidden={hidden} mixed={mixed} flail={flail} snapshots={snapshots} failures={failures} observation_rejections={observationsRejected} events={sourceEvents} resumed={resumed}.");
        GD.Print($"LAYERED_ROOT_DISPATCH_OK frames={frames} hidden={hidden} mixed={mixed} flail={flail} snapshots={snapshots} failures={failures} observation_rejections={observationsRejected} source_observations={sourcesSeen} source_events={sourceEvents} resumes={resumed} retry_every_frame=true animation_sources=real clocks=one_shared_batch physics=controlled snapshots=controlled default_demo=base_layer");
        int Find(string name)=>Array.FindIndex(boneNames,b=>b.Equals(name,StringComparison.OrdinalIgnoreCase));
    }
    private void RunFootLifecycle(AlsPoseAnimationProfile poseProfile, AlsMovementGraphDefinition definition)
    {
        var skeleton=definition.OverlayRawSources.GetSkeleton(poseProfile.SkeletonId);
        string[] names=["Enable_FootIK_L","Enable_FootIK_R","FootLock_L","FootLock_R"];
        var normal=skeleton.ReferencePose.ToArray(); var alternate=normal.ToArray();
        var boneNames=skeleton.LogicalBoneNames.ToArray();
        var left=Find("ik_foot_l"); var right=Find("ik_foot_r");
        var rootBone=Find("root"); var pelvis=Find("pelvis");
        alternate[pelvis]=alternate[pelvis] with {Position=new(0,.6f,0)};
        int Find(string name)
        {
            var index=Array.FindIndex(boneNames,b=>b.Equals(name,StringComparison.OrdinalIgnoreCase));
            return index>=0 ? index : throw new ArgumentException("Missing foot/root fixture bone: "+name);
        }
        AlsInertialCurve[] normalCurves=[new(0),new(0),new(0),new(0)];
        AlsInertialCurve[] alternateCurves=[new(1),new(1),new(1),new(1)];
        var frames=0; var hidden=0; var mixed=0; var failures=0; var active=0; var feedbackChecks=0; var stampChecks=0;
        foreach(var hz in new[]{30,60,120})
        {
            var feet=new AlsFootIkFrameRuntime(definition.FootIkInput,definition.FootIk,skeleton.LogicalBoneNames,skeleton.LogicalParents,names);
            var root=new AlsRootPoseRuntime(definition.RootPose,normal.Length,names.Length);
            var component=new AlsPrecisePose[normal.Length];
            Capture(normal);
            var lastLeft=component[left].ToSingle(); var lastRight=component[right].ToSingle();
            var lastRoot=component[rootBone].Position.ToSingle();
            var committedTraversal=default(AlsAnimationGraphFrame); var priorFinal=new AlsInertialCurve[4];
            for(var frame=1;frame<=hz*3;frame++)
            {
                var seconds=frame/(float)hz; var delta=1f/hz; var id=new AlsFrameIdentity(frame,11,2);
                var traversal=committedTraversal.Next(id,(ulong)frame);
                if(frame==hz*3/5)traversal=traversal with {Initialization=new(traversal.Initialization.Counter,(ulong)frame),Bones=new(traversal.Bones.Counter,(ulong)frame)};
                if(frame==hz*2)traversal=traversal with {Initialization=traversal.Initialization.Next((ulong)frame)};
                var state=seconds<.5f || seconds is >=1.25f and <2.25f ? AlsMovementStateInput.Ragdoll :
                    seconds>=2.25f ? AlsMovementStateInput.Mantling : AlsMovementStateInput.Grounded;
                var input=AlsFrameInput.CreateDefault(id,delta) with
                {
                    Floor=new((byte)(frame%2),NVector3.UnitY,-1,NMatrix.Identity,default),
                    FootIk=new(1,root.CommittedIdentity,AlsLocalPose.Identity,lastLeft,lastRight,lastRoot,
                        System.Numerics.Quaternion.Identity,new(.4f,0,-.8f),delta),
                    LeftFootHit=AlsFootHit.Invalid with {Valid=1,Walkable=1,Position=lastLeft.Position+new NVector3(0,.05f,0)},
                    RightFootHit=AlsFootHit.Invalid with {Valid=1,Walkable=1,Position=lastRight.Position+new NVector3(0,.1f,0)}
                };
                var oldId=feet.CommittedIdentity; var oldState=feet.CommittedState; var oldPoseId=feet.CommittedPoseIdentity;
                var oldRoot=root.CommittedState;
                Prepare(); Evaluate();
                var visits=root.Visits(0); var properties=feet.CandidateState;
                var expectedPose=root.Pose.ToArray(); var expectedCurves=root.Curves.ToArray(); var expectedRoot=root.CandidateState;
                if(!visits)
                {
                    Require(feet.EvaluatedControls==0,"Unvisited Foot IK evaluated controls."); hidden++;
                    Reject(()=>feet.Evaluate(normal,normalCurves)); Reject(()=>{_=feet.Pose.Length;});
                }
                if(root.Visits(0)&&root.Visits(1))mixed++;
                if(feet.EvaluatedControls>0)active++;
                if(frame==hz*3/5)
                {
                    Require(!root.InitializeChildren && !root.CacheChildBones && root.Visits(0)&&root.Visits(1),
                        "Same traversal count reset the in-progress root blend."); stampChecks++;
                }
                if(frame>1 && priorFinal[0].Present && priorFinal[0].Value>0)
                {
                    var curve=priorFinal[2].Present ? priorFinal[2].Value : 0;
                    var expected=curve>=definition.FootIkInput.Lock.CaptureThreshold || curve<oldState.LeftLock.Alpha ? curve : oldState.LeftLock.Alpha;
                    Require(properties.LeftLock.Alpha==expected,"Foot properties did not read the previous final root curve."); feedbackChecks++;
                }
                Cancel(); CheckOld();
                if(frame%47==0)
                {
                    Prepare(); EvaluateGraph(); var invalid=root.Curves.ToArray(); invalid[3]=new(float.NaN);
                    try {feet.CompleteFinalOutput(id,invalid); throw new Exception("Expected final root feedback failure.");}
                    catch(ArgumentException){failures++;}
                    Cancel(); CheckOld();
                }
                Prepare(); Evaluate();
                Require(feet.CandidateState==properties && root.CandidateState==expectedRoot &&
                    root.Pose.SequenceEqual(expectedPose) && root.Curves.SequenceEqual(expectedCurves),"Foot/root retry changed candidate output.");
                feet.ValidateCommit(id); root.ValidateCommit(id);
                Capture(root.Pose);
                lastLeft=component[left].ToSingle(); lastRight=component[right].ToSingle();
                lastRoot=component[rootBone].Position.ToSingle(); root.Curves.CopyTo(priorFinal);
                feet.Commit(id); root.Commit(id);
                Require(feet.CommittedIdentity==root.CommittedIdentity && feet.CommittedPoseIdentity==(visits ? id : oldPoseId),
                    "Global Foot IK identity and last visited pose were conflated.");
                committedTraversal=traversal; frames++;
                void Prepare()
                {
                    feet.PrepareGlobal(input,state);
                    root.Prepare(state,new(id,1,delta),traversal);
                    feet.PrepareGraph(root.Visits(0));
                }
                void EvaluateGraph()
                {
                    if(root.Visits(0))feet.Evaluate(normal,normalCurves);
                    root.Evaluate(root.Visits(0)?feet.Pose:[],root.Visits(0)?feet.Curves:[],
                        root.Visits(1)?alternate:[],root.Visits(1)?alternateCurves:[]);
                }
                void Evaluate(){EvaluateGraph(); feet.CompleteFinalOutput(id,root.Curves);}
                void Cancel(){feet.Cancel(); root.Cancel();}
                void CheckOld()=>Require(feet.CommittedIdentity==oldId && feet.CommittedState==oldState &&
                    feet.CommittedPoseIdentity==oldPoseId && root.CommittedIdentity==oldId && root.CommittedState==oldRoot,
                    "Cancelled foot/root candidate leaked history.");
            }
            void Capture(ReadOnlySpan<AlsLocalPose> locals)
            {
                for(var bone=0;bone<locals.Length;bone++)
                {
                    var parent=skeleton.LogicalParents[bone];
                    component[bone]=parent<0 ? new(locals[bone]) : AlsPrecisePose.Compose(new(locals[bone]),component[parent]);
                }
            }
        }
        Require(frames==630 && hidden>0 && mixed>0 && failures>0 && active>0 && feedbackChecks>0 && stampChecks==3,"Foot/root lifecycle coverage missing.");
        GD.Print($"FOOT_GLOBAL_ROOT_FEEDBACK_OK frames={frames} hidden={hidden} mixed={mixed} active={active} failures={failures} feedback_checks={feedbackChecks} stamp_checks={stampChecks} retry_every_frame=true root=authored feet=authored child_poses=controlled physics=not_bound");
        static void Reject(Action action)
        {
            try{action();}catch(InvalidOperationException){return;}
            throw new Exception("Expected hidden foot pose rejection.");
        }
    }
    private sealed class UpperSource(AlsLayerBlendingFrameStage stage,AlsLocalPose[] pose,AlsInertialCurve[] baseCurves,
        AlsInertialCurve[] overlayCurves):IAlsPostLayeringPoseSource
    {
        public bool Fail;
        public void EvaluatePostLayering(Span<AlsLocalPose> output,Span<AlsInertialCurve> curves)
        {
            stage.Evaluate(stage.BasePosesState.Identity,pose,baseCurves,pose,overlayCurves);
            stage.Pose.CopyTo(output); stage.Curves.CopyTo(curves);
            if(Fail)throw new InvalidOperationException("Injected upper failure after post cache source evaluation.");
        }
    }
    private static string Read(string name)=>Godot.FileAccess.GetFileAsString("res://assets/config/"+name);
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
