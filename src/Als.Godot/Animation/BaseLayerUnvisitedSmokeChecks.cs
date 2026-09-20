using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using NVector3=System.Numerics.Vector3;
using NMatrix=System.Numerics.Matrix4x4;

namespace GodotAls.Animation;

public partial class BaseLayerFrameSmoke
{
    private void RunUnvisitedRoot(AlsAnimationSetDefinition set,AlsLocomotionAnimationProfile profile,
        AlsPoseAnimationProfile pose,AlsMovementGraphDefinition definition,AlsStandingMovementSettings settings)
    {
        var frames=0; var hiddenFrames=0; var resumed=0; var hiddenMontages=0; var held=0; var hiddenAim=0;
        var explicitTraversal=OS.GetCmdlineUserArgs().Contains("--graph-traversal");
        var initializationFrames=0; var pausedBoneRefreshes=0; var consecutiveCounterGapFrames=0;
        var nestedUpdates=0; var nestedKinds=0; var nestedGapChecks=0;
        foreach(var hz in new[]{30,60,120})
        {
            using var library=AlsAnimationLibraryBuilder.BuildP5a(set,definition.Binding);
            library.UseMovementSources(set,definition.RawSources); AddChild(library.Root);
            using var graph=AlsLocomotionGraphBuilder.Build(library,profile,pose,set,definition.Binding);
            using var owner=new AlsBaseLayerFrameRuntime(definition,library,graph.StandingCycle!,set,pose);
            var sink=new Sink(owner.ReferencePose.ToArray()); var feedback=default(AlsAnimationInputFeedback);
            var wasHidden=false; var policy=definition.ActionPolicies.Single();
            var committedTraversal=default(AlsAnimationGraphFrame);
            for(var frame=1;frame<=hz*5;frame++)
            {
                var seconds=frame/(float)hz; var delta=1f/hz;
                var id=new AlsFrameIdentity(frame+(explicitTraversal && frame>=hz*3.7f ? 1000 : 0),11,2);
                var traversal=committedTraversal.Next(id,(ulong)frame);
                if(explicitTraversal && frame==1)traversal=traversal with {Update=new(short.MaxValue,(ulong)frame)};
                if(explicitTraversal && frame==hz*2)traversal=traversal with {Bones=traversal.Bones.Next((ulong)frame)};
                if(explicitTraversal && frame==hz*5/2)traversal=traversal with {Initialization=traversal.Initialization.Next((ulong)frame)};
                if(explicitTraversal && frame==hz*13/5)traversal=traversal with {Initialization=new(traversal.Initialization.Counter,(ulong)frame)};
                var initialized=explicitTraversal && !traversal.Initialization.MatchesCounter(committedTraversal.Initialization);
                var state=seconds<.3f || seconds is >=1.3f and <2.3f || seconds>=4 ? AlsLocomotionState.Ragdoll :
                    seconds<.5f || seconds is >=2.3f and <2.7f ? AlsLocomotionState.Mantling :
                    seconds is >=2.7f and <3.3f ? AlsLocomotionState.InAir : AlsLocomotionState.Grounded;
                var hide=state is AlsLocomotionState.Ragdoll or AlsLocomotionState.Mantling || seconds is >=2.7f and <2.9f;
                var velocity=seconds>=3.8f ? NVector3.Zero : new NVector3(MathF.Sin(seconds)*2,state==AlsLocomotionState.InAir ? -3 : 0,-3.5f);
                var input=AlsFrameInput.CreateDefault(id,delta) with
                {
                    ActualVelocity=velocity,ActualAcceleration=new(2,0,-3),MaxAcceleration=8,MaxBrakingDeceleration=16,
                    // Deliberately disagree with state: physics contact is not the
                    // AnimBP MovementState selector, including during physics blend.
                    Floor=new((byte)(frame%2),NVector3.UnitY,-1,NMatrix.Identity,default),
                    LandPrediction=new AlsLandPredictionSample{Queried=1,BlockingHit=1,Walkable=1,Time=.3f},
                    JumpAccepted=(byte)(frame==hz ? 1 : 0),
                    ActionRequest=frame==hz ? new(frame,AlsActionCommand.Start,policy.DefinitionId,policy.StartSectionId,100,2) : AlsActionRequest.None,
                };
                input=input with {Command=input.Command with {AimYaw=MathF.Sin(seconds*2),AimPitch=.5f*MathF.Cos(seconds)}};
                var result=AlsFrameResult.CreateDefault(id); result.ResolvedLocomotionState=state;
                result.ActualGait=AlsGait.Running; result.ActualRotationMode=AlsRotationMode.LookingDirection;
                result.BlendCoordinates=new(velocity.X,-velocity.Z); result.PlayRate=result.Stride=1;
                var movement=AlsStandingMovementInputModel.Evaluate(id,velocity,velocity.LengthSquared()>0 ? 1 : 0,settings);
                var rules=new AlsGroundedRuleInput(movement.ShouldMove,false,false,AlsStance.Standing,true,false,0,1)
                {HasMovementInput=movement.HasMovementInput,Speed=movement.Speed,FeetCrossing=1};
                var ground=new AlsGroundedFrameInputs(delta,new(1.75f,3.75f,6.5f),1,1,1,default,default,
                    AlsSlotWeights.Passthrough,new(0,0),new(0,0),new((short)frame,(ulong)frame));
                var context=new AlsPoseUpdateContext(id,1,delta);
                var priorId=owner.CommittedIdentity; var priorSources=owner.CommittedSources;
                var priorGround=owner.CommittedGroundInput; var priorAir=owner.CommittedGlobalInput;
                var priorAim=owner.CommittedAimingInput; var priorControl=owner.CommittedControlInput;
                var priorMachine=owner.Movement.CommittedMovement; var priorTail=owner.CommittedPoseGraphIdentity;
                var priorInertia=owner.CommittedInertiaHistoryCount; var priorMontages=owner.Montages.Committed.ToArray();
                var priorMachineUpdate=owner.Movement.CommittedMachineUpdate;
                Prepare();
                if(explicitTraversal)
                {
                    if(!hide && owner.SourceUpdated)
                    {
                        var nested=owner.Grounded.Update;
                        if(nested.MainUpdated)CheckNested(nested.Main,1);
                        if(nested.Standing.StandingUpdated)CheckNested(nested.Standing.Standing,2);
                        if(nested.Standing.StopUpdated)CheckNested(nested.Standing.Stop,4);
                        if(nested.CrouchingUpdated)CheckNested(nested.Crouching.Machine,8);
                        if(nested.Standing.DetailUpdated)
                        {
                            Require(nested.Standing.Detail.State.LastUpdateCounter==traversal.Update,"Detail lost the outer update counter.");
                            nestedUpdates++; nestedKinds|=16;
                        }
                        if(nested.Standing.CycleUpdated)
                        {
                            Require(owner.Grounded.StandingFrame.Lifetime.LastUpdateCounter==traversal.Update &&
                                owner.Grounded.StandingFrame.Lifetime.SprintInitialization.MatchesCounter(traversal.Initialization),
                                "Standing Cycle lost the outer update or Sprint initialization counter.");
                            nestedUpdates++; nestedKinds|=32;
                        }
                        if(id.FrameId>priorId.FrameId+1 && priorId!=default && priorMachineUpdate.Next(traversal.Update.GlobalFrame).MatchesCounter(traversal.Update))
                        {
                            Require(nested.MainUpdated && !nested.Main.Reinitialized &&
                                (!nested.Standing.StandingUpdated || !nested.Standing.Standing.Reinitialized) &&
                                (!nested.Standing.DetailUpdated || !nested.Standing.Detail.Reinitialized),
                                "FrameId gap incorrectly reset a continuously visited nested machine.");
                            nestedGapChecks++;
                        }
                    }
                    if(initialized){Require(owner.Movement.MachineReinitialized,"Root initialization failed to reach Main Movement."); initializationFrames++;}
                    else if(!hide && owner.SourceUpdated && priorMachine.HasUpdated &&
                        priorMachineUpdate.Next(traversal.Update.GlobalFrame).MatchesCounter(traversal.Update))
                    {
                        Require(!owner.Movement.MachineReinitialized,"Consecutive update counters reinitialized Main Movement.");
                        if(id.FrameId>priorId.FrameId+1)consecutiveCounterGapFrames++;
                    }
                    if(hide && frame==hz*2){Require(!owner.Movement.MachineReinitialized,"CacheBones reset the machine."); pausedBoneRefreshes++;}
                }
                var nextGround=owner.CandidateGroundInput; var nextAir=owner.CandidateGlobalInput;
                var nextControl=owner.CandidateControlInput; var nextAim=owner.CandidateAimingInput;
                var sources=owner.Sources; var events=owner.SourceEvents;
                var montages=owner.Montages.Candidate.ToArray(); var nextNotify=owner.TurnNotifies.Candidate;
                var output=hide ? Array.Empty<AlsLocalPose>() : owner.Pose.ToArray();
                var curves=hide ? new AlsInertialCurve[owner.CurveNames.Length] : owner.Curves.ToArray();
                if(state is AlsLocomotionState.Ragdoll or AlsLocomotionState.Mantling)
                {
                    Require(nextGround==priorGround with {Identity=id} && nextControl.State==priorControl with {Identity=id} &&
                        nextControl.Execution==new AlsMovementUpdateGate(priorControl.Gate,false,false,false,false),
                        "Non-grounded enum branch changed ground properties or DoOnce history.");
                    Require(nextAir.Lean==priorAir.Lean && nextAir.FallSpeed==priorAir.FallSpeed && nextAir.LandPrediction==priorAir.LandPrediction,
                        "Ragdoll/Mantling updated airborne properties."); held++;
                }
                Require(nextAir.Speed==movement.Speed,"UpdateCharacterInfo speed did not update outside Grounded/InAir.");
                if(hide)
                {
                    Require(!owner.SourceUpdated && sources.PlayerCount==0 && sources.SampleCount==0,"Unvisited ordinary graph ticked a source.");
                    var initializedSources=0;
                    for(var p=0;p<definition.Sources.Players.Length;p++)
                    {
                        if(initialized && sources.Epochs[p]>priorSources.Epochs[p])initializedSources++;
                        else Require(sources.Times[p]==priorSources.Times[p] && sources.Epochs[p]==priorSources.Epochs[p],"Unvisited source history changed without initialization.");
                    }
                    if(initialized)Require(initializedSources==0 && definition.Grounded.MainPose[0].Kind==AlsMainGroundedPoseKind.Reference,
                        "The authored Main entry is a reference pose and must not initialize an unrelated player.");
                    Reject(()=>owner.Evaluate(AlsLocalPose.Identity,0,0)); Reject(()=> { _=owner.Pose.Length; });
                    Reject(()=>owner.PrepareUnvisitedGraph());
                    if(owner.Montages.Traversal.Length>0)hiddenMontages++;
                    if(nextAim.SmoothedRotation!=priorAim.SmoothedRotation)hiddenAim++;
                    hiddenFrames++;
                }
                owner.Discard(); CheckOld();
                Prepare();
                Require(owner.CandidateGroundInput==nextGround && owner.CandidateGlobalInput==nextAir && owner.CandidateControlInput==nextControl &&
                    owner.CandidateAimingInput==nextAim && StandingCycleSmoke.SameSync(owner.Sources,sources) &&
                    owner.Montages.Candidate.SequenceEqual(montages) && owner.TurnNotifies.Candidate==nextNotify && owner.SourceEvents.Count==events.Count,
                    "Unvisited/global transaction changed on retry.");
                for(var e=0;e<events.Count;e++)Require(owner.SourceEvents[e]==events[e],"Unvisited notify retirement changed on retry.");
                if(!hide)Require(owner.Pose.SequenceEqual(output) && owner.Curves.SequenceEqual(curves),"Ordinary branch resumed differently on retry.");
                owner.Commit(id);
                Require(owner.CommittedIdentity==id && owner.Movement.CommittedIdentity==id && owner.CommittedAimingInput==nextAim &&
                    owner.CommittedGlobalInput==nextAir,"Global and shared-source identities did not commit on an unvisited frame.");
                if(hide)Require(owner.CommittedPoseGraphIdentity==priorTail && owner.CommittedInertiaHistoryCount==(initialized ? 0 : priorInertia) &&
                    (initialized ? owner.Movement.CommittedMovement.HasInitialized && !owner.Movement.CommittedMovement.HasUpdated :
                        SameMachine(owner.Movement.CommittedMovement,priorMachine)),"Unvisited graph committed unexpected pose/inertia/machine updates.");
                if(explicitTraversal)Require(owner.Movement.CommittedTraversal==traversal,"Main lifecycle did not commit the outer traversal.");
                if(initialized && hide)Require(owner.Grounded.CommittedMain.Main.HasInitialized &&
                    !owner.Grounded.CommittedMain.Main.HasUpdated && owner.Grounded.CommittedMain.Main.CurrentState==0,
                    "Hidden initialization did not reach the nested Main cache's reference entry.");
                if(!hide && wasHidden)resumed++;
                // The real outer root supplies its own final curves. These empty
                // curves are a controlled alternate-root output, not physics data.
                feedback=AlsAnimationInputFeedback.FromCompletedFrame(id,owner.CurveNames,curves);
                committedTraversal=traversal; wasHidden=hide; frames++;
                void Prepare()
                {
                    sink.Reset(); owner.PrepareGlobalFromFrame(input,result,movement,rules,ground,feedback);
                    if(hide)owner.PrepareUnvisitedGraph(explicitTraversal ? traversal : null,sink);
                    else {owner.PrepareGraph(context,sink,explicitTraversal ? traversal : null); owner.Evaluate(AlsLocalPose.Identity,0,0,sink);}
                }
                void CheckNested(in AlsGroundedMachineUpdate update,int kind)
                {
                    Require(update.State.LastUpdateSerial==id.FrameId && update.State.LastUpdateCounter==traversal.Update,
                        "Nested machine lost its frame identity or animation update counter.");
                    nestedUpdates++; nestedKinds|=kind;
                }
                void CheckOld()=>Require(owner.CommittedIdentity==priorId && owner.CommittedGroundInput==priorGround &&
                    owner.CommittedGlobalInput==priorAir && owner.CommittedAimingInput==priorAim && owner.CommittedControlInput==priorControl &&
                    owner.CommittedPoseGraphIdentity==priorTail && owner.CommittedInertiaHistoryCount==priorInertia &&
                    owner.Montages.Committed.SequenceEqual(priorMontages) && StandingCycleSmoke.SameSync(owner.CommittedSources,priorSources),
                    "Cancelling an unvisited candidate leaked committed history.");
            }
        }
        Require(frames==1050 && hiddenFrames>0 && resumed==6 && held>0 && hiddenMontages>0 && hiddenAim>0,
            $"Unvisited-root coverage missing: frames={frames}, hidden={hiddenFrames}, resumed={resumed}, held={held}, montages={hiddenMontages}, aim={hiddenAim}.");
        if(explicitTraversal)
        {
            Require(initializationFrames==6 && pausedBoneRefreshes==3 && consecutiveCounterGapFrames==3,"Explicit traversal boundary coverage missing.");
            Require(nestedGapChecks==3 && (nestedKinds&51)==51,"Nested traversal coverage missing.");
            GD.Print($"BASE_LAYER_TRAVERSAL_OK initialization_frames={initializationFrames} hidden_bone_refreshes={pausedBoneRefreshes} frame_id_gaps={consecutiveCounterGapFrames} update_wrap=true init_counter_only=true nested_updates={nestedUpdates} nested_kinds={nestedKinds} nested_frame_gap_checks={nestedGapChecks}");
        }
        GD.Print($"BASE_LAYER_UNVISITED_ROOT_OK frames={frames} hidden={hiddenFrames} resumed={resumed} held={held} hidden_montages={hiddenMontages} hidden_aim={hiddenAim} retry_every_frame=true floor_independent=true physics=not_bound root_dispatch=controlled");
    }
}
