using Godot;
using GodotAls.Assets;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Core.Actions;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace GodotAls.Animation;

public partial class OverlaySharedFrameSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private void Run()
    {
        var ownedFrame = OS.GetCmdlineUserArgs().Contains("--owned-frame");
        var handIk = ownedFrame || OS.GetCmdlineUserArgs().Contains("--hand-ik");
        var aimLayering = handIk || OS.GetCmdlineUserArgs().Contains("--aim-layer");
        var layering = aimLayering || OS.GetCmdlineUserArgs().Contains("--layer-blending");
        var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
        var locomotion = AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"), set);
        var poseProfile = AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"), set, locomotion);
        var definition = AlsMovementGraphDefinition.Load(set, locomotion, poseProfile).WithSharedOverlaySources(set);
        var settings = AlsLocomotionInputCompiler.Compile(Read("v4_locomotion_inputs.json")).Movement;
        var names = definition.OverlayRawSources.Sources.ToArray().SelectMany(s => s.Policy.FloatCurveNames.ToArray())
            .Concat(definition.OverlayPose.Nodes.ToArray().SelectMany(n => n.CurveNames.ToArray()))
            .Concat(["Enable_Transition", "RotationAmount", "Weight_Gait", "Weight_InAir"]).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var skeleton = definition.OverlayRawSources.GetSkeleton(poseProfile.SkeletonId);
        if (OS.GetCmdlineUserArgs().Contains("--unvisited-overlay"))
        { RunUnvisitedOverlay(set,locomotion,poseProfile,definition,settings,names); return; }
        if (aimLayering) names = names.Concat(definition.AimRawSources.Sources.ToArray().SelectMany(s => s.Policy.FloatCurveNames.ToArray()))
            .Append("Enable_SpineRotation").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var frames = 0; var mixed = 0; var hiddenMovement = 0; var faults = 0; var events = 0; var stateNotifies = 0; var transitionCommands = 0;
        var additiveFrames = 0; var changedBones = 0; var montageEvents = 0; var notifyFaults = 0;
        var layeredFrames = 0; var layeredFaults = 0; var layeredBones = 0; var layeredCurves = 0;
        var advancingFollowers = 0; var basePoseUpdates = 0; var basePoseSkipped = 0;
        var aimFrames = 0; var hiddenAim = 0; var spineFrames = 0; var mixedSpine = 0; var aimFaults = 0; var upperChangedBones = 0;
        var handFaults = 0; var leftFrames = 0; var rightFrames = 0; var partialHands = 0; var hiddenHands = 0; var handChangedBones = 0;
        var ownedRetries = 0; var ownedFailures = 0;
        var sharedPostReads=0;
        var stopCommands=0; var stopFaults=0;
        foreach (var hz in new[] { 30, 60, 120 })
        {
            using var library = AlsAnimationLibraryBuilder.BuildP5a(set, definition.Binding);
            library.UseMovementSources(set, definition.RawSources); AddChild(library.Root);
            using var graph = AlsLocomotionGraphBuilder.Build(library, locomotion, poseProfile, set, definition.Binding);
            var collector = new AlsOverlaySharedSourceCollector(definition.OverlaySharedSources, definition.OverlayClocks, 11, 2);
            var deferredOverlay = new DeferredOverlayContributor(collector);
            using var owner = new AlsBaseLayerFrameRuntime(definition, library, graph.StandingCycle!, set, poseProfile, layering ? deferredOverlay : collector);
            if (layering) names = names.Concat(owner.CurveNames.ToArray()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var groundedSlot = owner.GroundedSlot;
            var overlay = new AlsOverlayPoseRuntime(definition.OverlayPose, names, skeleton.ReferencePose, 11, 2, .01f, -NVector3.UnitX, skeleton.PreciseReferencePose);
            var sink = new Sink(skeleton.ReferencePose.ToArray());
            var transitions = new AlsOverlayTransitionRuntime(definition.OverlayTransitions, 11, 2);
            var overlaySink = new OverlaySink(collector, new(definition.OverlaySources, definition.OverlayRawSources, set, names), transitions);
            var overlayPose = new AlsLocalPose[79]; var overlayCurves = new AlsInertialCurve[names.Length]; var feedback = new AlsInertialCurve[names.Length];
            var savedOverlay = new AlsLocalPose[79]; var savedCurves = new AlsInertialCurve[names.Length];
            var savedBase = new AlsLocalPose[79]; var savedBaseCurves = new AlsInertialCurve[owner.CurveNames.Length];
            var stage = layering ? new AlsLayerBlendingFrameStage(definition,set,library,names,owner.CurveNames) : null;
            var savedLayered = new AlsLocalPose[79]; var savedLayeredCurves = new AlsInertialCurve[names.Length];
            var upper = aimLayering ? new AlsAimLayerFrameStage(definition,set,names,11,2) : null;
            var savedUpper = new AlsLocalPose[79]; var savedUpperCurves = new AlsInertialCurve[names.Length];
            var hands = handIk ? new AlsHandIkRuntime(definition.HandIk,skeleton.LogicalBoneNames,skeleton.LogicalParents,names.Length) : null;
            var savedHands = new AlsLocalPose[79]; var savedHandCurves = new AlsInertialCurve[names.Length];
            using var ownedLibrary = ownedFrame ? AlsAnimationLibraryBuilder.BuildP5a(set,definition.Binding) : null;
            if (ownedLibrary is not null) { ownedLibrary.UseMovementSources(set,definition.RawSources); AddChild(ownedLibrary.Root); }
            using var ownedGraph = ownedLibrary is null ? null : AlsLocomotionGraphBuilder.Build(ownedLibrary,locomotion,poseProfile,set,definition.Binding);
            using var owned = ownedGraph is null ? null : new AlsLayeredAnimationFrameRuntime(definition,ownedLibrary!,ownedGraph.StandingCycle!,set,poseProfile,11,2);
            if (owned is not null) Require(owned.CurveNames.SequenceEqual(names),"Owned frame curve layout differs from original composition.");
            var committedTraversal = default(AlsAnimationGraphFrame);
            for (var frame = 1; frame <= hz * 6; frame++)
            {
                var delta = 1f / hz; var seconds = frame / (float)hz; var id = new AlsFrameIdentity(frame, 11, 2);
                var traversal = committedTraversal.Next(id,(ulong)frame);
                var hidden = seconds is >= 1.2f and < 1.4f or >= 2 and < 2.2f;
                var overlayVisible = layering || seconds is < 2 or >= 2.2f;
                // Exercise relaxed rifle locomotion before aiming. With real
                // feedback the gait curve is absent at startup; after aiming,
                // Ready stays active until the native transition allows Relaxed.
                var earlyMove = layering && seconds is >= .05f and < .3f;
                var speed = earlyMove || !(seconds < 1.1f || seconds is >= 4 and < 4.3f) ? 3.5f : 0;
                var result = AlsFrameResult.CreateDefault(id);
                result.ResolvedLocomotionState = AlsLocomotionState.Grounded; result.ActualGait = AlsGait.Running;
                result.ActualStance = seconds >= 4.8f ? AlsStance.Crouching : AlsStance.Standing;
                result.ActualRotationMode = seconds is >= .3f and < .8f ? AlsRotationMode.Aiming : AlsRotationMode.LookingDirection;
                result.BlendCoordinates = new(0, speed); result.PlayRate = result.Stride = 1;
                var movement = AlsStandingMovementInputModel.Evaluate(id, new(speed, 0, 0), speed > 0 ? 1 : 0, settings);
                var rules = new AlsGroundedRuleInput(movement.ShouldMove, false, false, result.ActualStance, true, false, result.ActualStance == AlsStance.Crouching ? 1 : 0, 1)
                { MovementState = AlsMovementStateInput.Grounded, HasMovementInput = speed > 0, Speed = speed, FeetCrossing = 1, MovementDirection = AlsMovementDirection.Forward };
                var ground = new AlsGroundedFrameInputs(delta, new(1.75f, 3.75f, 6.5f), .75f, 1, 1,
                    new(NVector4.UnitX, default, 1, new NVector2(.2f, -.3f)), new(1, 1, default), AlsSlotWeights.Passthrough,
                    new(0, 0), new(0, 0), new((short)frame, (ulong)frame));
                var inputs = new AlsMainMovementInputs(ground, 0, 0, default, 1, speed);
                var context = new AlsPoseUpdateContext(id, 1, delta, 1);
                var slot = hidden ? new AlsSlotWeights(0, 1, 1) : AlsSlotWeights.Passthrough;
                var overlayKind = seconds < 3 ? AlsOverlayKind.Rifle : seconds < 4 ? AlsOverlayKind.Bow : seconds < 5 ? AlsOverlayKind.Pistol2H : AlsOverlayKind.Default;
                // Barrel's authored pose enables the right hand. Rifle and the
                // Bow states reached above do not exercise that controller.
                if (handIk && seconds >= 5.5f) overlayKind = AlsOverlayKind.Barrel;
                var state = new AlsOverlayStateInput(overlayKind, result.ActualRotationMode, result.ActualGait, rules.MovementState, speed > 0, 1);
                var values = new AlsOverlayPoseInput(result.ActualStance == AlsStance.Standing ? 1 : 0,
                    result.ActualStance == AlsStance.Crouching ? 1 : 0, NVector4.UnitX, .2, 0, 0, 0, 0, result.ActualRotationMode == AlsRotationMode.Aiming);
                var overlayContext = new AlsOverlayPoseContext(id, frame, delta, 1, AlsLocalPose.Identity, 0, 0, frame % 61 == 0);
                if (!layering) { Array.Clear(feedback); feedback[Array.IndexOf(names, "Enable_Transition")] = new(1); feedback[Array.IndexOf(names, "Weight_Gait")] = new(2); }
                var committed = owner.CommittedSources; var committedIdentity = owner.CommittedIdentity;
                var committedTransitionIdentity = transitions.CommittedIdentity;
                var committedNotify = owner.TurnNotifies.Committed; var committedMontages = owner.Montages.Committed.ToArray();
                var committedLayer = stage?.CommittedIdentity ?? default;
                var committedBases = stage?.CommittedBasePosesState ?? default;
                var committedUpper = upper?.CommittedIdentity ?? default;
                var committedAim = upper?.CommittedAim ?? default;
                var committedAimingInput = upper?.CommittedInput ?? default;
                var committedHands = hands?.CommittedIdentity ?? default;
                Prepare(); Evaluate();
                if (hands is not null) { hands.Pose.CopyTo(savedHands); hands.Curves.CopyTo(savedHandCurves); }
                var candidateAim = upper?.CandidateAim ?? default;
                if (upper is not null) { upper.Pose.CopyTo(savedUpper); upper.Curves.CopyTo(savedUpperCurves); }
                owner.Pose.CopyTo(savedBase); owner.Curves.CopyTo(savedBaseCurves);
                overlayPose.CopyTo(savedOverlay, 0); overlayCurves.CopyTo(savedCurves, 0);
                var candidate = owner.Sources; var sourceEvents = owner.SourceEvents;
                var commands = transitions.Commands.ToArray(); var queued = transitions.QueuedCount;
                var stopCount = owner.StopTransitionCount;
                var notifyCandidate = owner.TurnNotifies.Candidate;
                if (stage is not null) { stage.Pose.CopyTo(savedLayered); stage.Curves.CopyTo(savedLayeredCurves); }
                var hasMontageEvent = false;
                for (var e = 0; e < sourceEvents.Count; e++)
                    foreach (var asset in definition.OverlayTransitionAssets)
                        if (sourceEvents[e].SourceAnimationId == asset.AnimationId) hasMontageEvent = true;
                var montageInstances = owner.Montages.Candidate.ToArray(); var montageEvaluation = owner.Montages.Evaluation.ToArray();
                if (hidden && overlayVisible) { Require(candidate.PlayerCount > 0, "Hiding movement discarded active Overlay players."); hiddenMovement++; }
                foreach (var map in definition.OverlaySharedSources.Overlay)
                {
                    var policy = definition.Sources.CreateCoreView().SyncPlayers[map.PlayerId];
                    if (policy.Role != AlsAssetSyncRole.AlwaysFollower || candidate.Times[map.PlayerId] <= 0) continue;
                    var groupId = definition.Sources.CreateCoreView().Players[map.PlayerId].SyncGroupId;
                    for (var group = 0; group < candidate.GroupCount; group++)
                        if (candidate.Groups[group].Group.GroupId == groupId && candidate.Groups[group].Group.HasLeader &&
                            candidate.Groups[group].Group.LeaderPlayerId < definition.OverlaySharedSources.MovementPlayerCount)
                        {
                            mixed++;
                            if (candidate.CachedWeights[map.PlayerId] > AlsPoseBlender.WeightThreshold &&
                                candidate.Times[map.PlayerId] != committed.Times[map.PlayerId]) advancingFollowers++;
                        }
                }
                Discard();
                if (overlayVisible && (frame % 71 == 0 || queued > 0 || hasMontageEvent || stopCount > 0))
                {
                    Prepare(); owner.Evaluate(AlsLocalPose.Identity, 0, 0, baseSlot: sink); overlaySink.Fail = true;
                    try { overlay.Evaluate(overlayPose, overlayCurves); throw new Exception("Expected injected Overlay failure."); }
                    catch (InvalidOperationException error) when (error.Message == "Injected Overlay source failure") { faults++; }
                    Require(owner.CommittedIdentity == committedIdentity && StandingCycleSmoke.SameSync(owner.CommittedSources, committed), "Late Overlay failure committed movement sources.");
                    Require(transitions.CommittedIdentity == committedTransitionIdentity, "Late Overlay failure committed state-machine notifications.");
                    Require(owner.TurnNotifies.Committed == committedNotify && owner.Montages.Committed.SequenceEqual(committedMontages), "Late Overlay failure committed physical montage events/state.");
                    if (hasMontageEvent) notifyFaults++;
                    if (stopCount > 0) stopFaults++;
                    Discard(); overlaySink.Fail = false;
                }
                if (stage is not null && frame % 97 == 0)
                {
                    Prepare(); owner.Evaluate(AlsLocalPose.Identity,0,0,baseSlot:sink); overlay.Evaluate(overlayPose,overlayCurves);
                    var previous = overlayCurves[0]; overlayCurves[0] = new(float.NaN);
                    try { stage.Evaluate(id,owner.Pose,owner.Curves,overlayPose,overlayCurves); throw new Exception("Expected late LayerBlending failure."); }
                    catch (ArgumentException) { layeredFaults++; }
                    finally { overlayCurves[0] = previous; }
                    Require(stage.CommittedIdentity==committedLayer && stage.CommittedBasePosesState==committedBases && owner.CommittedIdentity==committedIdentity,
                        "Late LayerBlending failure leaked BasePoses or source history.");
                    Discard();
                }
                if (upper is not null && frame % 101 == 0)
                {
                    Prepare(); owner.Evaluate(AlsLocalPose.Identity,0,0,baseSlot:sink); overlay.Evaluate(overlayPose,overlayCurves);
                    stage!.Evaluate(id,owner.Pose,owner.Curves,overlayPose,overlayCurves);
                    var badCurves = stage.Curves.ToArray(); badCurves[0] = new(float.NaN);
                    try { upper.Evaluate(stage.Pose,badCurves); throw new Exception("Expected late outer Aim failure."); }
                    catch (ArgumentException) { aimFaults++; }
                    Require(upper.CommittedIdentity==committedUpper && upper.CommittedInput==committedAimingInput &&
                        stage.CommittedIdentity==committedLayer && owner.CommittedIdentity==committedIdentity,"Late Aim failure leaked frame state.");
                    AimFrameSmokeChecks.Same(committedAim,upper.CommittedAim);
                    Discard();
                }
                if (hands is not null && frame % 103 == 0)
                {
                    Prepare(); owner.Evaluate(AlsLocalPose.Identity,0,0,baseSlot:sink); overlay.Evaluate(overlayPose,overlayCurves);
                    stage!.Evaluate(id,owner.Pose,owner.Curves,overlayPose,overlayCurves);
                    upper!.Evaluate(stage.Pose,stage.Curves);
                    var badCurves = upper.Curves.ToArray(); badCurves[0] = new(float.NaN);
                    try { hands.Evaluate(upper.Pose,badCurves); throw new Exception("Expected late hand IK failure."); }
                    catch (ArgumentException) { handFaults++; }
                    Require(hands.CommittedIdentity==committedHands,"Late hand IK failure committed hand history.");
                    Require(upper.CommittedIdentity==committedUpper && upper.CommittedInput==committedAimingInput,"Late hand IK failure committed Aim history.");
                    Require(stage.CommittedIdentity==committedLayer && stage.CommittedBasePosesState==committedBases,"Late hand IK failure committed Layer/BasePoses history.");
                    Require(owner.CommittedIdentity==committedIdentity && StandingCycleSmoke.SameSync(owner.CommittedSources,committed),"Late hand IK failure committed movement history.");
                    Require(transitions.CommittedIdentity==committedTransitionIdentity,"Late hand IK failure committed transition history.");
                    Require(owner.TurnNotifies.Committed==committedNotify,"Late hand IK failure committed notify history.");
                    Require(owner.Montages.Committed.SequenceEqual(committedMontages),"Late hand IK failure committed montage history.");
                    AimFrameSmokeChecks.Same(committedAim,upper.CommittedAim);
                    Discard();
                }
                Prepare(); Evaluate();
                if (hands is not null) Require(hands.Pose.SequenceEqual(savedHands) && hands.Curves.SequenceEqual(savedHandCurves),"Hand IK retry differs.");
                if (upper is not null)
                {
                    Require(upper.Pose.SequenceEqual(savedUpper) && upper.Curves.SequenceEqual(savedUpperCurves),"Outer Aim/spine retry differs.");
                    AimFrameSmokeChecks.Same(candidateAim,upper.CandidateAim);
                }
                if (stage is not null) Require(stage.Pose.SequenceEqual(savedLayered) && stage.Curves.SequenceEqual(savedLayeredCurves), "LayerBlending retry differs.");
                Require(owner.Pose.SequenceEqual(savedBase) && owner.Curves.SequenceEqual(savedBaseCurves) &&
                    (!overlayVisible || overlayPose.SequenceEqual(savedOverlay) && overlayCurves.SequenceEqual(savedCurves)) &&
                    StandingCycleSmoke.SameSync(owner.Sources, candidate) && owner.SourceEvents.Count == sourceEvents.Count, "Shared source/pose/notify retry differs.");
                for (var e = 0; e < sourceEvents.Count; e++) Require(owner.SourceEvents[e] == sourceEvents[e], "Shared notify identity/order changed on retry.");
                Require(transitions.QueuedCount == queued && transitions.Commands.SequenceEqual(commands), "Overlay transition command retry differs.");
                Require(owner.Montages.Candidate.SequenceEqual(montageInstances) && owner.Montages.Evaluation.SequenceEqual(montageEvaluation), "Physical transition montage retry differs.");
                Require(owner.StopTransitionCount == stopCount, "Stop notification dispatch changed on retry.");
                Require(owner.TurnNotifies.Candidate == notifyCandidate, "Physical montage notify state retry differs.");
                if (owned is not null)
                {
                    var oldIdentity=owned.CommittedIdentity; var oldFeedback=owned.CommittedCurves.ToArray();
                    var oldAim=owned.CommittedAim; var oldAimInput=owned.CommittedAimInput; var oldBases=owned.CommittedBasePoses;
                    var oldTraversal=owned.CommittedTraversal;
                    var oldMontages=owned.Base.Montages.Committed.ToArray(); var oldNotifies=owned.Base.TurnNotifies.Committed;
                    PrepareOwned(); owned.Evaluate(AlsLocalPose.Identity,0,0,sink); CompareOwned();
                    if(owned.PostCacheReads==2)sharedPostReads++;
                    // Simulate rejection by a downstream pose/physics consumer.
                    // All owners have evaluated, but none may publish history.
                    owned.Discard(); CheckOwnedHistory();
                    if (frame % 107 == 0)
                    {
                        PrepareOwned();
                        try { owned.Evaluate(AlsLocalPose.Identity with {Position=new(float.NaN,0,0)},0,0,sink); throw new Exception("Expected owned evaluation failure."); }
                        catch (ArgumentException) { ownedFailures++; }
                        CheckOwnedHistory();
                    }
                    PrepareOwned(); owned.Evaluate(AlsLocalPose.Identity,0,0,sink); CompareOwned();
                    owned.ValidateCommit(id); owned.Commit(id); ownedRetries++;
                    Require(owned.CommittedCurves.SequenceEqual(savedHandCurves),"Owned committed feedback is not the final output.");

                    void PrepareOwned()
                    {
                        var observation = new AlsAimingObservation(id,delta,result.ActualRotationMode,speed>0,
                            new(0,0,0),new(50*Math.Sin(seconds*1.7),110*Math.Sin(seconds*2.3),0),speed,0);
                        owned.Prepare(result,movement,rules,inputs,context,slot,sink,observation,state,values,overlayContext);
                    }
                    void CompareOwned()
                    {
                        Require(owned.Pose.SequenceEqual(savedHands) && owned.Curves.SequenceEqual(savedHandCurves),"Owned full pose/curve output differs from original composition.");
                        Require(owned.PostCacheReads is 1 or 2 && owned.PostSourceEvaluations==1,"Post Layering was not evaluated once in its root cache scope.");
                        Require(StandingCycleSmoke.SameSync(owned.Base.Sources,owner.Sources) && owned.Base.SourceEvents.Count==owner.SourceEvents.Count,
                            "Owned source timing/event count differs.");
                        for(var e=0;e<owner.SourceEvents.Count;e++) Require(owned.Base.SourceEvents[e]==owner.SourceEvents[e],"Owned event identity/order differs.");
                        Require(owned.Commands.SequenceEqual(commands) && owned.Base.Montages.Candidate.SequenceEqual(montageInstances) &&
                            owned.Base.TurnNotifies.Candidate==notifyCandidate,"Owned physical montage/notify output differs.");
                    }
                    void CheckOwnedHistory()
                    {
                        Require(owned.CommittedIdentity==oldIdentity && owned.Base.CommittedIdentity==oldIdentity && owned.CommittedTraversal==oldTraversal &&
                            owned.CommittedCurves.SequenceEqual(oldFeedback) && owned.CommittedAimInput==oldAimInput &&
                            owned.CommittedBasePoses==oldBases && owned.Base.Montages.Committed.SequenceEqual(oldMontages) &&
                            owned.Base.TurnNotifies.Committed==oldNotifies,"Rejected owned frame leaked committed history.");
                        AimFrameSmokeChecks.Same(oldAim,owned.CommittedAim);
                    }
                }
                for (var e = 0; e < sourceEvents.Count; e++)
                    foreach (var asset in definition.OverlayTransitionAssets)
                        if (sourceEvents[e].SourceAnimationId == asset.AnimationId) montageEvents++;
                owner.ValidateCommit(id); if (overlayVisible) overlay.ValidateCommit();
                transitions.ValidateCommit(id);
                stage?.ValidateCommit(id);
                upper?.ValidateCommit(id);
                hands?.ValidateCommit(id);
                if (owner.Montages.SlotWeights(AlsMontageSlot.Grounded).SlotNodeWeight > AlsPoseBlender.WeightThreshold)
                {
                    Require(owner.Montages.SlotWeights(AlsMontageSlot.Grounded).SourceWeight == 1, "Additive montage suppressed its source.");
                    additiveFrames++; changedBones += groundedSlot.ChangedBones;
                }
                if (stage is not null)
                {
                    if (stage.InputUpdateOrder.Length == 3) basePoseUpdates++; else basePoseSkipped++;
                    for (var bone=0;bone<79;bone++) if(stage.Pose[bone]!=owner.Pose[bone]) layeredBones++;
                    for (var c=0;c<names.Length;c++) if(stage.Curves[c].Present) layeredCurves++;
                    stage.Curves.CopyTo(feedback); layeredFrames++;
                }
                if (upper is not null)
                {
                    if (upper.AimRelevant) aimFrames++; else hiddenAim++;
                    if (upper.SpineAlpha>AlsPoseBlender.WeightThreshold) spineFrames++;
                    if (upper.SpineAlpha>AlsPoseBlender.WeightThreshold && upper.SpineAlpha<1-AlsPoseBlender.WeightThreshold) mixedSpine++;
                    for (var bone=0;bone<79;bone++) if(upper.Pose[bone]!=stage!.Pose[bone]) upperChangedBones++;
                    upper.Curves.CopyTo(feedback);
                }
                if (hands is not null)
                {
                    if (hands.LeftAlpha>AlsPoseBlender.WeightThreshold) leftFrames++;
                    if (hands.RightAlpha>AlsPoseBlender.WeightThreshold) rightFrames++;
                    if (hands.EvaluatedHands==0) hiddenHands++;
                    if (hands.LeftAlpha>AlsPoseBlender.WeightThreshold && hands.LeftAlpha<1-AlsPoseBlender.WeightThreshold) partialHands++;
                    if (hands.RightAlpha>AlsPoseBlender.WeightThreshold && hands.RightAlpha<1-AlsPoseBlender.WeightThreshold) partialHands++;
                    for (var bone=0;bone<79;bone++) if(hands.Pose[bone]!=upper!.Pose[bone]) handChangedBones++;
                    Require(hands.Curves.SequenceEqual(upper!.Curves),"Hand controls changed animation curves.");
                    hands.Curves.CopyTo(feedback);
                }
                owner.Commit(id); if (overlayVisible) overlay.Commit(); collector.Discard(); stage?.Commit(id);
                upper?.Commit(id);
                hands?.Commit(id);
                transitions.Commit(id);
                committedTraversal = traversal;
                frames++; events += sourceEvents.Count; stateNotifies += queued; transitionCommands += commands.Length; stopCommands += stopCount;

                void Prepare()
                {
                    transitions.Begin(id);
                    var overlayValues = values; var sourceContext = context; var overlayUpdate = overlayContext;
                    var sweep = .5;
                    if (stage is not null)
                    {
                        var layerInput = definition.LayeringInput.Evaluate(id,stage.CommittedIdentity,
                            stage.CommittedIdentity==default ? [] : names,stage.CommittedIdentity==default ? [] : feedback);
                        hands?.Prepare(layerInput);
                        var layerContext = context;
                        if (upper is not null)
                        {
                            var observation = new AlsAimingObservation(id,delta,result.ActualRotationMode,speed>0,
                                new(0,0,0),new(50*Math.Sin(seconds*1.7),110*Math.Sin(seconds*2.3),0),speed,0);
                            upper.Prepare(context,layerInput,observation,feedback,traversal,cachePostLayering:false);
                            layerContext = upper.PostContext; sweep = upper.Input.AimSweepTime;
                        }
                        stage.Prepare(layerContext,layerInput,stage.CommittedIdentity==default ? [] : feedback,traversal);
                        Require(stage.InputUpdateOrder.SequenceEqual(new[] {0,1}) || stage.InputUpdateOrder.SequenceEqual(new[] {2,0,1}),
                            "Native linked input cache order differs: " + string.Join(",",stage.InputUpdateOrder.ToArray()));
                        sourceContext = stage.BaseContext;
                        overlayUpdate = overlayContext with { Weight=stage.OverlayContext.Weight,Inactive=!stage.OverlayContext.IsActive };
                        overlayValues = values with { BasePoseN=(float)layerInput.BasePoseNormal,BasePoseClf=(float)layerInput.BasePoseCrouching };
                    }
                    collector.Begin(id,sweep); overlaySink.Sweep=(float)sweep;
                    if (stage is not null)
                        deferredOverlay.PrepareSources = () => overlay.Prepare(overlayUpdate,state,overlayValues,feedback,overlaySink);
                    else if (overlayVisible) overlay.Prepare(overlayUpdate, state, overlayValues, feedback, overlaySink);
                    owner.Prepare(result, movement, rules, inputs, sourceContext, slot, sink, usePhysicalMontages: true);
                }
                void Evaluate()
                {
                    owner.Evaluate(AlsLocalPose.Identity, 0, 0, baseSlot: sink); if (overlayVisible) overlay.Evaluate(overlayPose, overlayCurves);
                    stage?.Evaluate(id,owner.Pose,owner.Curves,overlayPose,overlayCurves);
                    if (upper is not null) upper.Evaluate(stage!.Pose,stage.Curves);
                    if (hands is not null) hands.Evaluate(upper!.Pose,upper.Curves);
                    transitions.Resolve(result.ActualStance, movement.ShouldMove);
                    owner.PlayOverlayTransitions(transitions.Commands);
                }
                void Discard() { owner.Discard(); overlay.Cancel(); collector.Discard(); transitions.Cancel(); stage?.Cancel(); upper?.Cancel(); hands?.Cancel(); deferredOverlay.PrepareSources=null; }
            }
        }
        Require(frames == 1260 && mixed > 0 && hiddenMovement > 0 && faults > 0 && events > 0 && stateNotifies > 0 && transitionCommands > 0 && additiveFrames > 0 && changedBones > 0 && montageEvents == 6 && notifyFaults == 6,
            $"Incomplete shared source scenario coverage: frames={frames} mixed={mixed} hidden={hiddenMovement} faults={faults} events={events} state_notifies={stateNotifies} commands={transitionCommands}.");
        Require(advancingFollowers>0,"Follower observations never advanced a relevant source clock.");
        Require(stopCommands>0 && stopFaults>0,"Stop state notifications did not exercise dispatch and late rollback.");
        GD.Print($"STOP_TRANSITION_FRAME_OK commands={stopCommands} late_failures={stopFaults} retries={frames} shared_montage_owner=1");
        if (layering) Require(layeredFrames==1260 && layeredFaults>0 && layeredBones>0 && layeredCurves>0 && basePoseUpdates>0 && basePoseSkipped>0,"No real Post Layering coverage.");
        GD.Print($"OVERLAY_SHARED_FRAME_OK frames={frames} mixed_follower_observations={mixed} hidden_movement={hiddenMovement} late_failures={faults} notify_failures={notifyFaults} events={events} montage_events={montageEvents} state_notifies={stateNotifies} transition_commands={transitionCommands} additive_frames={additiveFrames} changed_bones={changedBones} retries={frames} players=223 samples=257 clocks=one_shared_batch additive_grounded_slot=physical_owner post_layering_frames={layeredFrames} post_layering_failures={layeredFaults} post_layering_changed_bones={layeredBones} post_layering_curves={layeredCurves} final_demo=pending");
        GD.Print($"POST_LAYERING_COVERAGE advancing_followers={advancingFollowers} base_pose_updates={basePoseUpdates} base_pose_skipped={basePoseSkipped}");
        if (aimLayering)
        {
            Require(aimFrames>0 && hiddenAim>0 && spineFrames>0 && mixedSpine>0 && aimFaults>0 && upperChangedBones>0,"Incomplete outer Aim/spine coverage.");
            GD.Print($"OUTER_AIM_LAYER_OK frames={frames} aim={aimFrames} hidden={hiddenAim} spine={spineFrames} mixed_spine={mixedSpine} late_failures={aimFaults} changed_bones={upperChangedBones} retries={frames} hand_ik={(handIk ? "controlled" : "pending")} final_demo=pending");
        }
        if (handIk)
        {
            GD.Print($"HAND_IK_COVERAGE left={leftFrames} right={rightFrames} partial_hands={partialHands} hidden={hiddenHands} late_failures={handFaults} changed_bones={handChangedBones}");
            Require(leftFrames>0 && rightFrames>0 && partialHands>0 && hiddenHands>0 && handFaults>0 && handChangedBones>0,"Incomplete real hand IK coverage.");
            GD.Print($"HAND_IK_FRAME_OK frames={frames} left={leftFrames} right={rightFrames} partial_hands={partialHands} hidden={hiddenHands} late_failures={handFaults} changed_bones={handChangedBones} retries={frames} foot_ik=pending final_demo=pending");
        }
        if (ownedFrame)
        {
            Require(ownedRetries==frames && ownedFailures>0 && sharedPostReads>0,"Missing owned frame retry/failure/cache coverage.");
            GD.Print($"LAYERED_FRAME_OWNER_OK frames={frames} exact_pose_curves=1 shared_sources=1 events=1 retries={ownedRetries} evaluation_failures={ownedFailures} shared_post_reads={sharedPostReads} post_evaluations_per_frame=1 default_demo=pending");
        }
    }
    private void RunUnvisitedOverlay(AlsAnimationSetDefinition set, AlsLocomotionAnimationProfile profile,
        AlsPoseAnimationProfile poseProfile, AlsMovementGraphDefinition definition, AlsStandingMovementSettings settings, string[] names)
    {
        var skeleton=definition.OverlayRawSources.GetSkeleton(poseProfile.SkeletonId);
        var sourceNodes=definition.OverlayPose.Nodes.ToArray().Where(n=>n.Kind==AlsOverlayPoseKind.Source).ToArray();
        var frames=0; var hiddenFrames=0; var initializations=0; var boneRefreshes=0; var faults=0; var resumes=0;
        foreach(var hz in new[]{30,60,120})
        {
            using var library=AlsAnimationLibraryBuilder.BuildP5a(set,definition.Binding);
            library.UseMovementSources(set,definition.RawSources); AddChild(library.Root);
            using var graph=AlsLocomotionGraphBuilder.Build(library,profile,poseProfile,set,definition.Binding);
            var collector=new AlsOverlaySharedSourceCollector(definition.OverlaySharedSources,definition.OverlayClocks,11,2);
            var deferred=new DeferredOverlayContributor(collector);
            using var owner=new AlsBaseLayerFrameRuntime(definition,library,graph.StandingCycle!,set,poseProfile,deferred);
            var overlay=new AlsOverlayPoseRuntime(definition.OverlayPose,names,skeleton.ReferencePose,11,2,.01f,-NVector3.UnitX,skeleton.PreciseReferencePose);
            var transitions=new AlsOverlayTransitionRuntime(definition.OverlayTransitions,11,2);
            var overlaySink=new OverlaySink(collector,new(definition.OverlaySources,definition.OverlayRawSources,set,names),transitions);
            var sink=new Sink(skeleton.ReferencePose.ToArray());
            var overlayPose=new AlsLocalPose[79]; var overlayCurves=new AlsInertialCurve[names.Length];
            var feedback=new AlsInertialCurve[names.Length]; var baseFeedback=default(AlsAnimationInputFeedback);
            var committedTraversal=default(AlsAnimationGraphFrame); var wasHidden=false;
            for(var frame=1;frame<=hz*3;frame++)
            {
                var seconds=frame/(float)hz; var delta=1f/hz;
                var id=new AlsFrameIdentity(frame+(frame>=hz*5/2 ? 1000 : 0),11,2);
                var traversal=committedTraversal.Next(id,(ulong)frame);
                if(frame==1)traversal=traversal with {Update=new(short.MaxValue,(ulong)frame)};
                if(frame==hz*3/2)traversal=traversal with {Bones=traversal.Bones.Next((ulong)frame)};
                if(frame==hz*7/4)traversal=traversal with {Initialization=traversal.Initialization.Next((ulong)frame)};
                if(frame==hz*9/5)traversal=traversal with {Initialization=new(traversal.Initialization.Counter,(ulong)frame)};
                var initialized=!traversal.Initialization.MatchesCounter(committedTraversal.Initialization);
                var hide=seconds<.25f || seconds is >=1.25f and <2;
                var velocity=new NVector3(1,0,-3.5f);
                var input=AlsFrameInput.CreateDefault(id,delta) with {ActualVelocity=velocity,ActualAcceleration=new(2,0,-3),MaxAcceleration=8,MaxBrakingDeceleration=16};
                var result=AlsFrameResult.CreateDefault(id);
                result.ResolvedLocomotionState=hide ? AlsLocomotionState.Ragdoll : AlsLocomotionState.Grounded;
                result.ActualStance=AlsStance.Standing; result.ActualGait=AlsGait.Running;
                result.ActualRotationMode=seconds is >=.5f and <1 ? AlsRotationMode.Aiming : AlsRotationMode.LookingDirection;
                result.BlendCoordinates=new(velocity.X,-velocity.Z); result.PlayRate=result.Stride=1;
                var movement=AlsStandingMovementInputModel.Evaluate(id,velocity,1,settings);
                var rules=new AlsGroundedRuleInput(movement.ShouldMove,false,false,AlsStance.Standing,true,false,0,1)
                    {HasMovementInput=true,Speed=movement.Speed,FeetCrossing=1};
                var ground=new AlsGroundedFrameInputs(delta,new(1.75f,3.75f,6.5f),1,1,1,default,default,
                    AlsSlotWeights.Passthrough,new(0,0),new(0,0),new((short)frame,(ulong)frame));
                var context=new AlsPoseUpdateContext(id,1,delta);
                var state=new AlsOverlayStateInput(AlsOverlayKind.Rifle,result.ActualRotationMode,AlsGait.Running,
                    hide ? AlsMovementStateInput.Ragdoll : AlsMovementStateInput.Grounded,true,1);
                var values=new AlsOverlayPoseInput(1,0,NVector4.UnitX,.2,0,0,0,0,result.ActualRotationMode==AlsRotationMode.Aiming);
                var overlayContext=new AlsOverlayPoseContext(id,id.FrameId,delta,1,AlsLocalPose.Identity,0,0);
                var priorSources=owner.CommittedSources; var priorId=owner.CommittedIdentity;
                var priorOverlayId=overlay.CommittedIdentity; var priorInertia=overlay.CommittedInertiaHistoryCount;
                var priorMachine=overlay.Machine(AlsOverlayMachineKind.Overlay);
                var priorEpochs=sourceNodes.Select(n=>overlay.SourceInitialization(n.Index)).ToArray();
                var priorTransitionId=transitions.CommittedIdentity;
                Prepare(); Evaluate();
                var expectedSources=owner.Sources; var expectedEvents=owner.SourceEvents;
                var expectedEpochs=sourceNodes.Select(n=>overlay.SourceInitialization(n.Index)).ToArray();
                var expectedPose=overlayPose.ToArray(); var expectedCurves=overlayCurves.ToArray();
                var expectedBase=hide ? [] : owner.Pose.ToArray();
                var expectedBaseCurves=hide ? new AlsInertialCurve[owner.CurveNames.Length] : owner.Curves.ToArray();
                var queued=transitions.QueuedCount; var commands=transitions.Commands.ToArray();
                if(hide)
                {
                    Require(expectedSources.PlayerCount==0 && expectedSources.SampleCount==0 && overlay.SourceUpdates==0 && overlay.SourceEvaluations==0,
                        "Hidden Base/Overlay advanced an animation source.");
                    Require(queued==0 && commands.Length==0,"Hidden Overlay emitted a transition notify.");
                    Require(overlay.Machine(AlsOverlayMachineKind.Overlay).LastUpdateCounter==(initialized ? null : priorMachine.LastUpdateCounter),
                        "Hidden Overlay updated its state machine.");
                    var changed=0;
                    foreach(var map in definition.OverlaySharedSources.Overlay)
                    {
                        var policy=definition.OverlayClocks.Sources[map.Source];
                        if(expectedSources.Epochs[map.PlayerId]!=priorSources.Epochs[map.PlayerId])
                        {
                            Require(initialized,"Hidden source initialized without a root initialization."); changed++;
                            var sequence=definition.OverlayClocks.Sequences[policy.SequenceIndex];
                            var time=policy.Evaluator ? priorSources.Times[map.PlayerId] : AlsAssetSourceInitialization.Time(AlsAssetSyncKind.Sequence,
                                policy.StartPosition,sequence.DurationSeconds,policy.PlayRate,assetRateScale:sequence.RateScale);
                            Require(expectedSources.Times[map.PlayerId]==time,"Hidden initialization advanced a clock or reset an evaluator.");
                        }
                        else Require(expectedSources.Times[map.PlayerId]==priorSources.Times[map.PlayerId],"Hidden source clock advanced.");
                    }
                    if(initialized)Require(changed>0,"Hidden root initialization never reached shared Overlay epochs.");
                    hiddenFrames++;
                }
                Cancel(); CheckOld();
                if(!hide && frame%47==0)
                {
                    Prepare(); owner.Evaluate(AlsLocalPose.Identity,0,0,baseSlot:sink); overlaySink.Fail=true;
                    try {overlay.Evaluate(overlayPose,overlayCurves); throw new Exception("Expected late Overlay failure.");}
                    catch(InvalidOperationException e) when(e.Message=="Injected Overlay source failure") {faults++;}
                    finally {overlaySink.Fail=false; Cancel();}
                    CheckOld();
                }
                Prepare(); Evaluate();
                Require(StandingCycleSmoke.SameSync(expectedSources,owner.Sources) && expectedEpochs.SequenceEqual(sourceNodes.Select(n=>overlay.SourceInitialization(n.Index))) &&
                    expectedEvents.Count==owner.SourceEvents.Count && queued==transitions.QueuedCount && commands.AsSpan().SequenceEqual(transitions.Commands),
                    "Shared Overlay lifecycle retry changed clocks, epochs, or events.");
                for(var e=0;e<expectedEvents.Count;e++)Require(expectedEvents[e]==owner.SourceEvents[e],"Shared source notify retry differs.");
                Require(overlayPose.AsSpan().SequenceEqual(expectedPose) && overlayCurves.AsSpan().SequenceEqual(expectedCurves),"Overlay pose retry differs.");
                if(!hide)Require(owner.Pose.SequenceEqual(expectedBase) && owner.Curves.SequenceEqual(expectedBaseCurves),"Base pose retry differs.");
                owner.ValidateCommit(id); overlay.ValidateCommit(); transitions.ValidateCommit(id);
                owner.Commit(id); overlay.Commit(); transitions.Commit(id); collector.Discard();
                Require(owner.CommittedIdentity==id && overlay.CommittedIdentity==id && overlay.CommittedTraversal==traversal,
                    "Shared Overlay lifecycle identities did not commit together.");
                if(hide)Require(overlay.CommittedInertiaHistoryCount==(initialized ? 0 : priorInertia),"Hidden Overlay changed inertial history.");
                baseFeedback=AlsAnimationInputFeedback.FromCompletedFrame(id,owner.CurveNames,expectedBaseCurves);
                if(!hide)overlayCurves.CopyTo(feedback,0);
                if(!hide && wasHidden)resumes++;
                if(initialized)initializations++;
                if(frame==hz*3/2)boneRefreshes++;
                committedTraversal=traversal; wasHidden=hide; frames++;
                void Prepare()
                {
                    collector.Begin(id,.5); transitions.Begin(id);
                    deferred.PrepareSources=()=>overlay.Prepare(overlayContext,state,values,feedback,overlaySink,traversal,!hide);
                    owner.PrepareGlobalFromFrame(input,result,movement,rules,ground,baseFeedback);
                    if(hide)owner.PrepareUnvisitedGraph(traversal,sink); else owner.PrepareGraph(context,sink,traversal);
                }
                void Evaluate()
                {
                    if(!hide){owner.Evaluate(AlsLocalPose.Identity,0,0,baseSlot:sink); overlay.Evaluate(overlayPose,overlayCurves);}
                    transitions.Resolve(AlsStance.Standing,true);
                    if(!hide)owner.PlayOverlayTransitions(transitions.Commands);
                }
                void Cancel(){owner.Discard(); overlay.Cancel(); transitions.Cancel(); collector.Discard();}
                void CheckOld()=>Require(owner.CommittedIdentity==priorId && overlay.CommittedIdentity==priorOverlayId &&
                    overlay.CommittedInertiaHistoryCount==priorInertia && overlay.CommittedTraversal==(priorOverlayId==default ? null : committedTraversal) &&
                    priorEpochs.SequenceEqual(sourceNodes.Select(n=>overlay.SourceInitialization(n.Index))) &&
                    StandingCycleSmoke.SameSync(owner.CommittedSources,priorSources) && transitions.CommittedIdentity==priorTransitionId,
                    "Cancelled Overlay candidate leaked shared history.");
            }
        }
        Require(frames==630 && hiddenFrames>0 && initializations==6 && boneRefreshes==3 && faults>0 && resumes==6,"Overlay lifecycle coverage missing.");
        GD.Print($"OVERLAY_UNVISITED_SHARED_OK frames={frames} hidden={hiddenFrames} initializations={initializations} bone_refreshes={boneRefreshes} failures={faults} resumes={resumes} retry_every_frame=true raw_sources=true shared_players=223 shared_samples=257 root_dispatch=controlled physics=not_bound");
    }
    // Native LayerBlending drains the BaseLayer input before Overlay. Complete
    // Overlay collection at that boundary, then tick their one shared source bank.
    private sealed class DeferredOverlayContributor(AlsOverlaySharedSourceCollector collector) : IAlsSharedSourceContributor
    {
        public Action? PrepareSources;
        public void Collect(in AlsFrameIdentity identity,ref AlsCycleSyncFrame candidate,Span<AlsLocomotionSourceUpdate> players,
            Span<AlsLocomotionSampleUpdate> samples,Span<bool> active,ref int playerCount,ref int sampleCount)
        {
            var prepare=PrepareSources ?? throw new InvalidOperationException("Missing deferred Overlay update.");
            PrepareSources=null; prepare(); collector.Collect(identity,ref candidate,players,samples,active,ref playerCount,ref sampleCount);
        }
        public void Complete(in AlsFrameIdentity identity,in AlsCycleSyncFrame synchronized) => collector.Complete(identity,synchronized);
    }
    private sealed class OverlaySink(AlsOverlaySharedSourceCollector collector, AlsPreciseOverlayAnimationSourceSampler sampler, AlsOverlayTransitionRuntime transitions) : IAlsPreciseOverlayPoseSink
    {
        public bool Fail;
        public float Sweep = .5f;
        public void InitializeSource(int source, int initialization) => collector.Initialize(source, initialization);
        public void UpdateSource(in AlsOverlaySourceUpdate update) => collector.Update(update);
        public void EvaluateSource(int source, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves) => throw new InvalidOperationException("Expected precise Overlay source.");
        public void EvaluatePreciseSource(int source, Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves)
        { if (Fail) throw new InvalidOperationException("Injected Overlay source failure"); sampler.Sample(source, collector.EvaluationTime(source), Sweep, pose, curves); }
        public void RequestInertialization(in AlsOverlayInertialRequest request) { }
        public void QueueTransitionNotify(AlsOverlayMachineKind machine, in AlsOverlayTransitionNotify notify) => transitions.Queue(machine, notify);
    }
    private sealed class Sink(AlsLocalPose[] reference) : IAlsGroundedFrameRuntimeSink, IAlsBaseLayerSlotPoseSink
    {
        public void UpdateGroundedSlot(int slot, in AlsSlotWeights weights, in AlsSlotSourceUpdate source, in AlsPoseUpdateContext context) { }
        public void RequestInertialization(in AlsPoseUpdateContext context, float seconds) { }
        public void OnCachedUpdatesSkipped(int handler, ReadOnlySpan<AlsPoseUpdateContext> skipped) { }
        public void RefreshSourceBones(int cache) { }
        public void EvaluateSlot(in AlsSlotWeights weights, ReadOnlySpan<AlsLocalPose> source, ReadOnlySpan<AlsInertialCurve> sourceCurves, Span<AlsLocalPose> output, Span<AlsInertialCurve> curves)
        { if (source.IsEmpty) { reference.CopyTo(output); curves.Clear(); } else { source.CopyTo(output); sourceCurves.CopyTo(curves); } }
        public void EvaluateSlot(in AlsSlotWeights weights, ReadOnlySpan<AlsPrecisePose> source, ReadOnlySpan<AlsInertialCurve> sourceCurves, Span<AlsPrecisePose> output, Span<AlsInertialCurve> curves)
        { if (source.IsEmpty) { for (var bone = 0; bone < output.Length; bone++) output[bone] = new(reference[bone]); curves.Clear(); } else { source.CopyTo(output); sourceCurves.CopyTo(curves); } }
    }
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
