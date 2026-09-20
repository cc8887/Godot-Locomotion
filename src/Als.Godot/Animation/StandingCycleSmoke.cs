using Godot;
using System.Text.Json.Nodes;
using GodotAls.Assets;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

public partial class StandingCycleSmoke : Node
{
    private AlsStandingMovementSettings _movementSettings;
    public override void _Ready()
    {
        try
        {
            var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
            var profile = AlsLocomotionProfileCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/p4_cycle_locomotion_profile.json"), set);
            var pose = AlsPoseProfileCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/p4_pose_profile.json"), set, profile);
            var settings = AlsLocomotionSettings.Load(Godot.FileAccess.GetFileAsString("res://assets/config/p3_locomotion_settings.json"));
            _movementSettings = AlsLocomotionInputCompiler.Compile(Godot.FileAccess.GetFileAsString(
                "res://assets/config/v4_locomotion_inputs.json")).Movement;
            CheckIdleSourceBinding(set, profile, pose);
            CheckFootLockOrientation();
            var waits = 0;
            var transitions = 0;
            var interruptedRollbacks = 0;
            var maxActiveTransitions = 0;
            var directionChecks = 0;
            var poseBridgeChecks = 0;
            var sourceTimingChecks = 0;
            var timingAuthorityChecks = 0;
            var movementChecks = 0;
            var sprintChecks = 0;
            var detailChecks = 0;
            var standingChecks = 0;
            var pivotChecks = 0;
            foreach (var hz in new[] { 30, 60, 120 })
            foreach (var offset in new[] { 0f, 0.23f, 0.57f })
            {
                using var library = AlsAnimationLibraryBuilder.Build(set, profile, pose);
                AddChild(library.Root);
                using var graph = AlsLocomotionGraphBuilder.Build(library, profile, pose, set);
                using var controller = new AlsLocomotionAnimationController(graph, settings, pose, set);
                controller.Warmup();
                if (offset == 0) CheckCycleLifetime(graph.StandingCycle!, hz);
                sourceTimingChecks += CheckSourceTiming(graph.StandingCycle!, profile, set, hz);
                var result = AlsFrameResult.CreateDefault(new AlsFrameIdentity(1, 0, 1));
                result.AnimationState = AlsAnimationState.Grounded;
                result.ResolvedLocomotionState = AlsLocomotionState.Grounded;
                result.ActualStance = AlsStance.Standing;
                result.ActualGait = AlsGait.Walking;
                result.ActualRotationMode = AlsRotationMode.LookingDirection;
                result.Stride = 1;
                result.PlayRate = 1;
                var disabled = AlsP4AnimationInput.Disabled;
                var previous = controller.StandingCycleState;
                var initialPose = AlsPoseDigest.CapturePoses(graph.TargetSkeleton, ["pelvis", "foot_l", "foot_r"]);
                var initialCycleOutput = graph.StandingCycle!.PoseOutput.ToArray();
                var initialSync = controller.StandingSync;
                result.BlendCoordinates = new System.Numerics.Vector2(1.75f, 0);
                var rejectedFirst = PrepareTest(controller, ref result, disabled, hz);
                controller.ApplyPrepared(rejectedFirst);
                controller.RollbackPrepared(rejectedFirst);
                Require(SameSync(initialSync, controller.StandingSync), "First-frame rollback leaked source Sync history.");
                Require(graph.StandingCycle.PoseOutput.SequenceEqual(initialCycleOutput), "First-frame rollback leaked a derived cycle pose.");
                Require(!AlsPoseDigest.HasChanged(initialPose, AlsPoseDigest.CapturePoses(graph.TargetSkeleton,
                    ["pelvis", "foot_l", "foot_r"])), "First-frame rollback changed the skeleton.");
                var phaseChanged = false;
                for (var i = 0; i < hz * 9; i++)
                {
                    result.BlendCoordinates = new System.Numerics.Vector2(i < hz * (3 + offset) ? 1.75f : -1.75f, 0);
                    var decision = PrepareTest(controller, ref result, disabled, hz);
                    controller.SampleFootCurves(decision);
                    controller.ApplyPrepared(decision);
                    controller.CommitPrepared(decision);
                    var state = controller.StandingCycleState;
                    if (i > hz && i % hz == 0)
                    {
                        var expected = graph.StandingCycle!.PoseOutput;
                        for (var bone = 0; bone < graph.TargetSkeleton.GetBoneCount(); bone++)
                        {
                            var position = graph.TargetSkeleton.GetBonePosePosition(bone);
                            var rotation = graph.TargetSkeleton.GetBonePoseRotation(bone);
                            var scale = graph.TargetSkeleton.GetBonePoseScale(bone);
                            var p = expected[bone];
                            Require(System.Numerics.Vector3.Distance(p.Position, new(position.X, position.Y, position.Z)) < .0001f &&
                                MathF.Abs(System.Numerics.Quaternion.Dot(p.Rotation, new(rotation.X, rotation.Y, rotation.Z, rotation.W))) > .99999f &&
                                System.Numerics.Vector3.Distance(p.Scale, new(scale.X, scale.Y, scale.Z)) < .0001f,
                                $"Cycle pose bridge changed bone {bone} before the upper layers.");
                        }
                        poseBridgeChecks++;
                    }
                    if (state.WaitingForFeet) waits++;
                    if (state.HipTransition && state.TransitionsStartedThisFrame > 0)
                    {
                        Require(state.Crossing == 0 && previous.CurrentStateWeight == 1, "Neutral hip change started outside permission window.");
                        transitions++;
                    }
                    phaseChanged |= state.Phase != previous.Phase;
                    if (i == hz * 4)
                    {
                        var committedSync = controller.StandingSync;
                        var before = AlsPoseDigest.CapturePoses(graph.TargetSkeleton, ["pelvis", "foot_l", "foot_r"]);
                        var rejected = PrepareTest(controller, ref result, disabled, hz);
                        controller.ApplyPrepared(rejected);
                        var rejectedPose = AlsPoseDigest.CapturePoses(graph.TargetSkeleton, ["pelvis", "foot_l", "foot_r"]);
                        controller.RollbackPrepared(rejected);
                        Require(SameSync(committedSync, controller.StandingSync), "Rollback changed source clocks or marker history.");
                        Require(controller.StandingCycleState == state, "Rollback changed the cycle clock or transition.");
                        var after = AlsPoseDigest.CapturePoses(graph.TargetSkeleton, ["pelvis", "foot_l", "foot_r"]);
                        Require(!AlsPoseDigest.HasChanged(before, after), "Rollback did not restore the sampled cycle pose.");
                        var retry = PrepareTest(controller, ref result, disabled, hz);
                        controller.ApplyPrepared(retry);
                        Require(!AlsPoseDigest.HasChanged(rejectedPose, AlsPoseDigest.CapturePoses(graph.TargetSkeleton,
                            ["pelvis", "foot_l", "foot_r"])), "Retry changed the sampled pose or filter history.");
                        controller.RollbackPrepared(retry);
                        Require(SameSync(committedSync, controller.StandingSync), "Retry rollback changed source Sync history.");
                    }
                    previous = state;
                }
                Require(phaseChanged && AlsPoseDigest.HasChanged(initialPose,
                    AlsPoseDigest.CapturePoses(graph.TargetSkeleton, ["pelvis", "foot_l", "foot_r"])), "Cycle graph is blank or frozen.");
                var checkedInterruption = false;
                for (var i = 0; i < hz * 2; i++)
                {
                    result.BlendCoordinates = (i / Math.Max(1, hz / 10) % 4) switch
                    {
                        0 => new System.Numerics.Vector2(0, 1.75f),
                        1 => new System.Numerics.Vector2(-1.75f, 0),
                        2 => new System.Numerics.Vector2(0, -1.75f),
                        _ => new System.Numerics.Vector2(1.75f, 0),
                    };
                    ApplyTest(controller, ref result, disabled, hz);
                    var committed = controller.StandingTransitions;
                    var committedSync = controller.StandingSync;
                    maxActiveTransitions = Math.Max(maxActiveTransitions, committed.Count);
                    if (committed.Count < 2 || checkedInterruption) continue;
                    var snapshot = AlsPoseDigest.CapturePoses(graph.TargetSkeleton, ["pelvis", "foot_l", "foot_r"]);
                    var decision = PrepareTest(controller, ref result, disabled, hz);
                    controller.ApplyPrepared(decision);
                    var candidate = AlsPoseDigest.CapturePoses(graph.TargetSkeleton, ["pelvis", "foot_l", "foot_r"]);
                    controller.RollbackPrepared(decision);
                    Require(SameSync(committedSync, controller.StandingSync), "Interrupted rollback leaked source Sync history.");
                    var restored = controller.StandingTransitions;
                    Require(restored.Count == committed.Count && restored.CurrentState == committed.CurrentState, "Rollback changed stack topology.");
                    for (var j = 0; j < committed.Count; j++)
                        Require(restored.GetTransition(j) == committed.GetTransition(j), "Rollback changed an interrupted transition.");
                    Require(!AlsPoseDigest.HasChanged(snapshot, AlsPoseDigest.CapturePoses(graph.TargetSkeleton,
                        ["pelvis", "foot_l", "foot_r"])), "Interrupted rollback changed the pose.");
                    decision = PrepareTest(controller, ref result, disabled, hz);
                    controller.ApplyPrepared(decision);
                    Require(!AlsPoseDigest.HasChanged(candidate, AlsPoseDigest.CapturePoses(graph.TargetSkeleton,
                        ["pelvis", "foot_l", "foot_r"])), "Interrupted retry changed the candidate pose.");
                    controller.RollbackPrepared(decision);
                    checkedInterruption = true;
                    interruptedRollbacks++;
                }
                Require(checkedInterruption, "Rapid input did not exercise overlapping transitions.");
                foreach (var (yaw, gait, mode, expected) in new[]
                {
                    (MathF.PI / 2, AlsGait.Walking, AlsRotationMode.LookingDirection, AlsMovementDirection.Right),
                    (-MathF.PI / 2, AlsGait.Walking, AlsRotationMode.Aiming, AlsMovementDirection.Left),
                    (MathF.PI / 2, AlsGait.Walking, AlsRotationMode.VelocityDirection, AlsMovementDirection.Forward),
                    (MathF.PI / 2, AlsGait.Sprinting, AlsRotationMode.LookingDirection, AlsMovementDirection.Forward),
                })
                {
                    result.BlendCoordinates = new System.Numerics.Vector2(0, 1.75f);
                    result.AimRelativeYaw = yaw;
                    result.ActualGait = gait;
                    result.ActualRotationMode = mode;
                    ApplyTest(controller, ref result, disabled, hz);
                    var committedDirection = controller.StandingCycleState;
                    Require(committedDirection.MovementDirection == expected, "Cycle did not consume aim/gait/rotation direction.");
                    result.AimRelativeYaw = -yaw;
                    var pending = PrepareTest(controller, ref result, disabled, hz);
                    controller.ApplyPrepared(pending);
                    controller.RollbackPrepared(pending);
                    Require(controller.StandingCycleState == committedDirection, "Rollback changed movement direction.");
                    directionChecks++;
                }
                result.AimRelativeYaw = 0;
                result.ActualGait = AlsGait.Walking;
                result.ActualRotationMode = AlsRotationMode.LookingDirection;
                result.BlendCoordinates = System.Numerics.Vector2.Zero;
                for (var i = 0; i < hz; i++) ApplyTest(controller, ref result, disabled, hz);
                var idle = AlsPoseDigest.CapturePoses(graph.TargetSkeleton, ["pelvis", "foot_l", "foot_r"]);
                var turn = pose.Turns[0];
                var turnInput = AlsP4AnimationInput.Turn(turn.AnimationId, 1, 0.2f, 0, 0, 0, 0);
                for (var i = 0; i < hz; i++) ApplyTest(controller, ref result, turnInput, hz);
                for (var i = 0; i < hz; i++) ApplyTest(controller, ref result, disabled, hz);
                Require(!AlsPoseDigest.HasChanged(idle, AlsPoseDigest.CapturePoses(graph.TargetSkeleton,
                    ["pelvis", "foot_l", "foot_r"])), "Turn did not return to the standing cycle idle pose.");
                var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 1000; i++) ApplyTest(controller, ref result, disabled, hz);
                Require(GC.GetAllocatedBytesForCurrentThread() == allocatedBefore, "Steady cycle controller allocated managed memory.");
                result.BlendCoordinates = new System.Numerics.Vector2(1.75f, 0);
                for (var i = 0; i < 64; i++) ApplyTest(controller, ref result, disabled, hz);
                allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 256; i++) ApplyTest(controller, ref result, disabled, hz);
                Require(GC.GetAllocatedBytesForCurrentThread() == allocatedBefore, "Active cycle pose sampling allocated managed memory.");
                timingAuthorityChecks += CheckTimingAuthority(controller, settings, hz);
                movementChecks += CheckMovementGate(controller, hz);
                sprintChecks += CheckSprintBranch(controller, graph, hz);
                detailChecks += CheckDetailExecution(controller, graph, hz);
                standingChecks += CheckStandingExecution(controller, graph, pose, hz);
                CheckStandingTurnSlot(controller, graph, pose, hz);
                pivotChecks += CheckPivotExecution(controller, graph, hz, AlsGait.Walking);
                pivotChecks += CheckPivotExecution(controller, graph, hz, AlsGait.Running);
            }
            Require(waits > 0 && transitions == 9, $"Insufficient crossover coverage: waits={waits} transitions={transitions}");
            Require(interruptedRollbacks == 9, "Missing interrupted rollback cases.");
            GD.Print($"SPRINT_BRANCH_OK rates=30,60,120 frames={sprintChecks} rollback=9 placement=forward_input mask_source=previous_cycle");
            GD.Print($"DETAIL_PRODUCTION_OK rates=30,60,120 frames={detailChecks} owner=controller sources=shared_batch pose=actual retry=identical");
            GD.Print($"STANDING_PRODUCTION_OK rates=30,60,120 frames={standingChecks} stop=actual rotate=single_owner retry=identical");
            GD.Print($"PIVOT_PRODUCTION_OK rates=30,60,120 frames={pivotChecks} input=previous_committed events=ordered inactive_delay=advances retry=identical");
            GD.Print($"STANDING_CYCLE_OK rates=30,60,120 phases=3 hip_transitions={transitions} wait_frames={waits} rollback=9 interrupted_rollback={interruptedRollbacks} max_active={maxActiveTransitions} movement_direction={directionChecks} pose_bridge={poseBridgeChecks} alloc=0B active_alloc=0B curves=603 source_timing={sourceTimingChecks} timing_authority={timingAuthorityChecks} movement_frames={movementChecks} sync=asset_runtime");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }

    private int CheckSprintBranch(AlsLocomotionAnimationController controller, AlsLocomotionGraphBuildResult graph, int hz)
    {
        var result = AlsFrameResult.CreateDefault(new(0,0,1));
        result.AnimationState = AlsAnimationState.Grounded;
        result.ActualStance = AlsStance.Standing;
        result.ActualRotationMode = AlsRotationMode.LookingDirection;
        result.BlendCoordinates = new(0,3);
        var partials = 0; var rolledBack = false;
        for (var i = 0; i < hz * 2; i++)
        {
            result.ActualGait = i >= hz / 2 && i < hz ? AlsGait.Sprinting : AlsGait.Running;
            ApplyTest(controller, ref result, AlsP4AnimationInput.Disabled, hz);
            var state = controller.StandingSprintBlend;
            if (state.SecondWeight is > .01f and < .99f) partials++;
            if (rolledBack || state.SecondWeight is <= .01f or >= .99f) continue;
            var pose = AlsPoseDigest.CapturePoses(graph.TargetSkeleton, ["pelvis", "foot_l", "foot_r"]);
            var sync = controller.StandingSync; var mask = controller.StandingSprintMask;
            var prepared = PrepareTest(controller, ref result, AlsP4AnimationInput.Disabled, hz);
            controller.ApplyPrepared(prepared); controller.RollbackPrepared(prepared);
            Require(controller.StandingSprintBlend == state && controller.StandingSprintMask == mask && SameSync(sync, controller.StandingSync),
                "Rollback advanced Sprint interpolation, source time or mask.");
            Require(!AlsPoseDigest.HasChanged(pose, AlsPoseDigest.CapturePoses(graph.TargetSkeleton, ["pelvis", "foot_l", "foot_r"])),
                "Rollback leaked Sprint pose changes.");
            rolledBack = true;
        }
        Require(rolledBack && partials > 0 && controller.StandingSprintBlend.SecondWeight == 0,
            "Sprint fixture missed interpolation, rollback or completed exit.");
        return hz * 2;
    }

    private int CheckDetailExecution(AlsLocomotionAnimationController controller, AlsLocomotionGraphBuildResult graph, int hz)
    {
        var result = AlsFrameResult.CreateDefault(new(0, 0, 1));
        result.ActualStance = AlsStance.Standing;
        result.ActualRotationMode = AlsRotationMode.LookingDirection;
        result.AnimationState = AlsAnimationState.Grounded;
        result.ActualGait = AlsGait.Running;
        var sources = graph.StandingCycle!.SourceBindings.Sources;
        var sourceFrames = 0; var runStartFrames = 0; var requests = 0; var retries = 0;
        for (var i = 0; i < 3 * hz; i++)
        {
            result.BlendCoordinates = i < hz / 2 || i >= 2 * hz ? System.Numerics.Vector2.Zero : new(1.5f, 3f);
            var prepared = PrepareTest(controller, ref result, AlsP4AnimationInput.Disabled, hz);
            controller.ApplyPrepared(prepared);
            controller.CommitPrepared(prepared);
            var detail = controller.StandingDetail;
            Require(detail.HasPose && detail.BoneCount == graph.TargetSkeleton.GetBoneCount(), "Controller omitted Detail pose output.");
            if (detail.Updated && detail.Update.State.CurrentState == AlsDetailState.RunStart) runStartFrames++;
            if (detail.Updated && detail.Update.InertializationSeconds >= 0) requests++;
            var sync = controller.StandingSync;
            var seen = 0UL; var detailSources = 0;
            for (var n = 0; n < sync.PlayerCount; n++)
            {
                var player = sync.Players[n];
                Require((seen & (1UL << player.PlayerId)) == 0, "A source was ticked twice in the shared batch.");
                seen |= 1UL << player.PlayerId;
                if (sources.Players[player.PlayerId].Domain == AlsLocomotionSourceDomain.Detail) detailSources++;
                Require(sync.Times[player.PlayerId] == player.Time, "Detail introduced a separate player clock.");
            }
            if (detailSources > 0) sourceFrames++;
            if (!detail.Updated || detail.Update.InertializationSeconds < 0) continue;
            var candidate = PrepareTest(controller, ref result, AlsP4AnimationInput.Disabled, hz);
            controller.ApplyPrepared(candidate);
            var candidatePose = graph.StandingCycle.PoseOutput.ToArray();
            controller.RollbackPrepared(candidate);
            Require(SameSync(sync, controller.StandingSync), "Detail rollback leaked group/epoch/relevancy history.");
            var restored = controller.StandingDetail;
            ReadOnlySpan<AlsLocalPose> beforePose = detail.Pose, restoredPose = restored.Pose;
            ReadOnlySpan<AlsInertialCurve> beforeCurves = detail.Curves, restoredCurves = restored.Curves;
            Require(beforePose.SequenceEqual(restoredPose) && beforeCurves.SequenceEqual(restoredCurves), "Detail rollback changed committed output.");
            candidate = PrepareTest(controller, ref result, AlsP4AnimationInput.Disabled, hz);
            controller.ApplyPrepared(candidate);
            Require(graph.StandingCycle.PoseOutput.SequenceEqual(candidatePose), "Detail inertial history advanced on retry.");
            controller.RollbackPrepared(candidate);
            retries++;
        }
        Require(sourceFrames > 0 && runStartFrames > 0 && requests > 0 && retries > 0,
            $"Missing actual Detail coverage: sources={sourceFrames} run_start={runStartFrames} requests={requests} retries={retries}");
        return hz * 3;
    }

    private void CheckStandingTurnSlot(AlsLocomotionAnimationController controller, AlsLocomotionGraphBuildResult graph,
        AlsPoseAnimationProfile pose, int hz)
    {
        var turn = pose.Turns.Where(t => t.Stance == AlsPoseStance.Standing).MaxBy(t =>
        {
            Require(graph.Handles.P4!.TryGetTurnBinding(t.AnimationId, out var binding), "Turn binding missing.");
            return binding.DurationSeconds;
        });
        graph.Handles.P4!.TryGetTurnBinding(turn.AnimationId, out var turnBinding);
        var seconds = turnBinding.DurationSeconds * .8f;
        Require(seconds > 1, "Turn slot fixture must cover a source time beyond one second.");
        var p4 = AlsP4AnimationInput.Disabled with { ActiveTurnAnimationId = turn.AnimationId, TurnPlayRate = 1, TurnPhase = seconds };
        var result = AlsFrameResult.CreateDefault(new(0, 0, 1));
        result.AnimationState = AlsAnimationState.Grounded;
        result.ActualStance = AlsStance.Standing;
        result.ActualRotationMode = AlsRotationMode.LookingDirection;
        var movingChecks = 0; var idleChecks = 0;
        for (var i = 0; i < 3 * hz; i++)
        {
            result.BlendCoordinates = i < hz ? new(1.75f, 0) : System.Numerics.Vector2.Zero;
            var before = controller.StandingDetail.Standing;
            var prepared = PrepareTest(controller, ref result, p4, hz);
            Require(prepared.ActionBlendAmount == 0, "Standing turn still overlays the whole base graph.");
            var withTurnEvents = result.TypedEvents;
            var footCurves = controller.SampleFootCurves(prepared);
            controller.ApplyPrepared(prepared);
            var candidate = graph.StandingCycle!.PoseOutput.ToArray();
            controller.RollbackPrepared(prepared);
            Require(SameStanding(before, controller.StandingDetail.Standing), "Standing slot candidate leaked after rollback.");
            var withoutTurn = PrepareTest(controller, ref result, AlsP4AnimationInput.Disabled, hz);
            var withoutTurnEvents = result.TypedEvents;
            var alternateFeet = controller.SampleFootCurves(withoutTurn);
            controller.ApplyPrepared(withoutTurn);
            var alternate = graph.StandingCycle.PoseOutput.ToArray();
            controller.RollbackPrepared(withoutTurn);
            prepared = PrepareTest(controller, ref result, p4, hz);
            controller.ApplyPrepared(prepared);
            Require(candidate.AsSpan().SequenceEqual(graph.StandingCycle.PoseOutput), "Standing turn slot retry changed the pose.");
            controller.CommitPrepared(prepared);
            if (i > hz / 2 && i < hz)
            {
                Require(candidate.AsSpan().SequenceEqual(alternate), "Turn slot changed fully Moving pose.");
                Require(footCurves == alternateFeet, "Turn slot changed fully Moving foot curves.");
                Require(System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(in withTurnEvents, 1))
                    .SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(in withoutTurnEvents, 1))),
                    "Idle turn slot suppressed Moving source events.");
                movingChecks++;
            }
            if (i > 2 * hz)
            {
                Require(!candidate.AsSpan().SequenceEqual(alternate), "Idle did not consume its turn slot pose.");
                idleChecks++;
            }
        }
        Require(movingChecks > 0 && idleChecks > 0, "Standing slot did not exercise both branches.");
        GD.Print($"STANDING_TURN_SLOT_OK hz={hz} moving={movingChecks} idle={idleChecks} retry=identical outer_override=0");
    }

    private int CheckPivotExecution(AlsLocomotionAnimationController controller, AlsLocomotionGraphBuildResult graph, int hz, AlsGait gait)
    {
        var result = AlsFrameResult.CreateDefault(new(0, 0, 1));
        result.AnimationState = AlsAnimationState.Grounded;
        result.ActualStance = AlsStance.Standing;
        result.ActualRotationMode = AlsRotationMode.LookingDirection;
        result.ActualGait = gait;
        var events = 0; var detailFrames = 0; var retries = 0; var inactive = false;
        for (var i = 0; i < 4 * hz; i++)
        {
            result.AnimationState = AlsAnimationState.Grounded;
            result.BlendCoordinates = new(0, (i / (hz / 2) % 2 == 0 ? 1 : -1) * 1.75f);
            var before = controller.StandingDetail.Standing;
            var sync = controller.StandingSync;
            var prepared = PrepareTest(controller, ref result, AlsP4AnimationInput.Disabled, hz);
            controller.ApplyPrepared(prepared);
            var candidatePose = graph.StandingCycle!.PoseOutput.ToArray();
            controller.RollbackPrepared(prepared);
            Require(SameStanding(before, controller.StandingDetail.Standing) && SameSync(sync, controller.StandingSync),
                "Pivot candidate leaked its event, timer, hips or source history.");
            prepared = PrepareTest(controller, ref result, AlsP4AnimationInput.Disabled, hz);
            controller.ApplyPrepared(prepared);
            Require(candidatePose.AsSpan().SequenceEqual(graph.StandingCycle.PoseOutput), "Pivot retry changed the actual pose.");
            controller.CommitPrepared(prepared);
            retries++;
            var standing = controller.StandingDetail.Standing;
            Require(standing.PivotInput == before.Feedback.Pivot, "Detail read uncommitted same-frame Pivot feedback.");
            for (var e = 0; e < standing.DirectionEvents.Count; e++)
                if (standing.DirectionEvents[e].Pivot)
                {
                    events++;
                    Require(e > 0 && !standing.DirectionEvents[e - 1].Pivot && standing.Feedback.Pivot,
                        "Pivot lost its target-state event order or low-speed predicate.");
                }
            if (controller.StandingDetail.Update.State.CurrentState is AlsDetailState.FirstPivot or AlsDetailState.SecondPivot)
                detailFrames++;
            if (inactive || !standing.Feedback.DelayPending) continue;
            var remaining = standing.Feedback.DelayRemaining;
            result.AnimationState = AlsAnimationState.FallLoop;
            result.PlayRate = 1;
            result.Stride = 1;
            for (var frame = 0; frame < hz / 2; frame++)
            {
                ApplyTest(controller, ref result, AlsP4AnimationInput.Disabled, hz);
                remaining = MathF.Max(0, remaining - 1f / hz);
                Require(controller.StandingDetail.Standing.Feedback.DelayRemaining == remaining,
                    "Leaving Standing froze or reset the latent Pivot action.");
            }
            Require(!controller.StandingDetail.Standing.Feedback.Pivot, "Inactive Pivot delay never cleared.");
            inactive = true;
        }
        Require(events >= 3 && (gait == AlsGait.Running ? detailFrames > 0 : detailFrames == 0) && inactive && retries == 4 * hz,
            $"Pivot fixture missed actual behavior: gait={gait} events={events} detail={detailFrames} inactive={inactive} retries={retries}");
        return 4 * hz;
    }

    private static void CheckFootLockOrientation()
    {
        var previous = AlsFootLockState.CreateDefault();
        var current = previous with { Locked = 1, Amount = 1 };
        var stored = System.Numerics.Quaternion.Identity;
        var previousWorld = System.Numerics.Quaternion.Identity;
        const float limit = 40 * MathF.PI / 180;
        for (var i = 0; i < 360; i++)
        {
            var animated = new Transform3D(new Basis(Vector3.Up, i * MathF.PI / 180), Vector3.Zero);
            var checkpoint = stored;
            var first = GodotAls.Locomotion.AlsP3WorkerRoot.PrepareFootLockPoseRotation(previous, current, animated, limit, ref stored);
            var candidate = stored;
            stored = checkpoint;
            var retry = GodotAls.Locomotion.AlsP3WorkerRoot.PrepareFootLockPoseRotation(previous, current, animated, limit, ref stored);
            Require(first == retry && stored == candidate, "Foot lock orientation retry changed its history.");
            var dot = Math.Clamp(MathF.Abs(System.Numerics.Quaternion.Dot(previousWorld, first)), 0, 1);
            Require(2 * MathF.Acos(dot) < .02f, "Foot lock clamp flipped across a half turn.");
            previous = current; previousWorld = first;
        }
        current = current with { Amount = 0 };
        var released = GodotAls.Locomotion.AlsP3WorkerRoot.PrepareFootLockPoseRotation(previous, current, Transform3D.Identity, limit, ref stored);
        Require(released == default && stored == default, "Released physical foot rotation was retained.");
        GD.Print("FOOT_LOCK_ORIENTATION_OK frames=360 half_turn=continuous retry=identical release=cleared");
    }

    private int CheckStandingExecution(AlsLocomotionAnimationController controller, AlsLocomotionGraphBuildResult graph,
        AlsPoseAnimationProfile pose, int hz)
    {
        var result = AlsFrameResult.CreateDefault(new(0, 0, 1));
        result.AnimationState = AlsAnimationState.Grounded;
        result.ActualStance = AlsStance.Standing;
        result.ActualGait = AlsGait.Walking;
        result.ActualRotationMode = AlsRotationMode.Aiming;
        var runtime = AlsRuntimeState.CreateDefault();
        var stopFrames = 0; var rotations = 0; var retries = 0; var idleFrames = 0;
        var sources = graph.StandingCycle!.SourceBindings.Sources;
        for (var i = 0; i < 8 * hz; i++)
        {
            result.BlendCoordinates = i >= hz && i < 3 * hz ? new(1.75f, 0) : System.Numerics.Vector2.Zero;
            result.RotateActive = i >= 4 * hz && i < 6 * hz ? (byte)1 : (byte)0;
            result.RotateDirection = i < 5 * hz ? (sbyte)-1 : (sbyte)1;
            result.RotatePlayRate = 1.25f;
            var source = sources.Players.ToArray().Single(p => p.Domain == AlsLocomotionSourceDomain.Standing && p.LoopInput ==
                (result.RotateDirection < 0 ? AlsSourceLoopInput.RotateLeft : AlsSourceLoopInput.RotateRight));
            var animationId = sources.Samples[source.SampleStart].AnimationId;
            var rotation = pose.Rotates.Single(p => p.AnimationId == animationId);
            result.RotateAnimationId = animationId; result.RotateCurveId = rotation.CurveId;
            result.RotatePhase = 0;
            var p4 = result.RotateActive == 0 ? AlsP4AnimationInput.Disabled : AlsP4AnimationInput.Rotate(animationId, 1.25f, 0, 0, 0, 0, 0);
            var prepared = PrepareTest(controller, ref result, p4, hz);
            var input = AlsFrameInput.CreateDefault(result.Identity, 1f / hz);
            controller.CompleteSourceRotation(prepared, input, ref runtime, ref result);
            Require(prepared.ActionBlendAmount == 0, "Standing Rotate was covered by the legacy P4 action channel.");
            controller.ApplyPrepared(prepared); controller.CommitPrepared(prepared);
            var standing = controller.StandingDetail.Standing;
            var state = standing.Update.State.Standing.CurrentState;
            if (state == 0) idleFrames++;
            if (state == 2)
            {
                stopFrames++;
                Require(standing.Stop.Machine.State.HasUpdated && standing.Update.DetailUpdated && standing.Update.CycleUpdated,
                    "Stop omitted its actual Detail/Cycle input.");
            }
            var sync = controller.StandingSync;
            var seen = 0UL;
            for (var n = 0; n < sync.PlayerCount; n++)
            {
                var history = sync.Players[n];
                Require((seen & (1UL << history.PlayerId)) == 0, "Standing ticked a source twice.");
                seen |= 1UL << history.PlayerId;
                if (history.PlayerId != source.PlayerId || result.RotateActive == 0) continue;
                Require(result.RotatePhase == history.Time && runtime.RotateInPlace.Phase == history.Time,
                    "Rotate feedback kept a second playback clock.");
                rotations++;
            }
            if (state is not (2 or 3 or 4) || i % (hz / 5) != 0) continue;
            var before = controller.StandingDetail;
            var candidate = PrepareTest(controller, ref result, p4, hz);
            controller.ApplyPrepared(candidate);
            var candidatePose = graph.StandingCycle.PoseOutput.ToArray();
            controller.RollbackPrepared(candidate);
            var restored = controller.StandingDetail;
            Require(SameSync(sync, controller.StandingSync) && SameStanding(before.Standing, restored.Standing),
                "Standing rollback changed Stop/Rotate state or source history.");
            candidate = PrepareTest(controller, ref result, p4, hz);
            controller.ApplyPrepared(candidate);
            Require(graph.StandingCycle.PoseOutput.SequenceEqual(candidatePose), "Standing retry changed pose or inertial history.");
            controller.RollbackPrepared(candidate);
            retries++;
        }
        Require(stopFrames > 0 && rotations > hz && retries > 0 && idleFrames > hz &&
            controller.StandingDetail.Standing.Update.State.Standing.CurrentState == 0,
            $"Standing coverage incomplete: stop={stopFrames} rotate={rotations} retry={retries} idle={idleFrames}");
        return 8 * hz;
    }

    private static bool SameStanding(in AlsStandingBaseFrame left, in AlsStandingBaseFrame right) =>
        System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(in left, 1))
            .SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(in right, 1)));

    private AlsPreparedAnimationFrame PrepareTest(AlsLocomotionAnimationController controller,
        ref AlsFrameResult result, in AlsP4AnimationInput input, int hz)
    {
        result.Identity = new(controller.SourceEventState.Identity.FrameId + 1, 0, 1);
        result.TypedEvents.Clear();
        var prepared = controller.PrepareFrame(result, input, 1.0 / hz, Movement(result));
        controller.CompleteSourceEvents(prepared, ref result);
        return prepared;
    }

    private void ApplyTest(AlsLocomotionAnimationController controller, ref AlsFrameResult result,
        in AlsP4AnimationInput input, int hz)
    {
        var prepared = PrepareTest(controller, ref result, input, hz);
        controller.ApplyPrepared(prepared);
        controller.CommitPrepared(prepared);
    }

    private static int CheckTimingAuthority(AlsLocomotionAnimationController controller, AlsLocomotionSettings settings, int hz)
    {
        var runtime = AlsRuntimeState.CreateDefault();
        runtime.AnimationPhase = controller.StandingCycleState.Phase;
        var result = AlsFrameResult.CreateDefault(new(0, 0, 1));
        var disabled = AlsP4AnimationInput.Disabled;
        for (var i = 0; i < hz * 2; i++)
        {
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var input = AlsFrameInput.CreateDefault(new(controller.SourceEventState.Identity.FrameId + 1, 0, 1), 1f / hz);
            input = input with { Floor = input.Floor with { IsGrounded = 1 },
                ActualVelocity = i < hz ? System.Numerics.Vector3.Zero : new System.Numerics.Vector3(1.75f, 0, 0),
                MaxAcceleration = 20, MaxBrakingDeceleration = 15 };
            var state = runtime; var candidate = result;
            AlsLocomotionModel.Evaluate(input, ref state, ref candidate, settings, controller.TimingPolicy);
            Require(AlsLocomotionModel.HasPendingSourceTiming(candidate) && state.AnimationPhase == runtime.AnimationPhase,
                "Source candidate advanced the legacy clock or hid pending timing.");
            var pendingResult = candidate; var pendingState = state;
            var committedSync = controller.StandingSync;
            var prepared = controller.PrepareFrame(candidate, disabled, input);
            if (i == 0)
            {
                var rejected = false;
                try { controller.ApplyPrepared(prepared); }
                catch (InvalidOperationException) { rejected = true; }
                Require(rejected && SameSync(committedSync, controller.StandingSync), "Unresolved source timing was applied.");
            }
            controller.CompleteSourceTiming(prepared, ref state, ref candidate);
            Require(float.IsFinite(candidate.Stride) && float.IsFinite(candidate.PlayRate) &&
                candidate.AnimationPhase == state.AnimationPhase, "Source timing completion failed.");
            var firstTiming = new AlsLocomotionSourceTiming(candidate.Stride, candidate.PlayRate, candidate.AnimationPhase);
            if (i == 0)
            {
                var rejected = false;
                try { controller.CompleteSourceTiming(prepared, ref pendingState, ref pendingResult); }
                catch (InvalidOperationException) { rejected = true; }
                Require(rejected, "The same prepared frame completed source timing twice.");
            }
            controller.DiscardPrepared(prepared);
            Require(SameSync(committedSync, controller.StandingSync), "Timing completion published source history early.");
            if (i == 0)
            {
                var rejected = false;
                try { controller.CompleteSourceTiming(prepared, ref pendingState, ref pendingResult); }
                catch (InvalidOperationException) { rejected = true; }
                Require(rejected, "Discarded timing candidate remained usable.");
            }
            prepared = controller.PrepareFrame(pendingResult, disabled, input);
            controller.CompleteSourceTiming(prepared, ref pendingState, ref pendingResult);
            controller.CompleteSourceEvents(prepared, ref pendingResult);
            Require(firstTiming == new AlsLocomotionSourceTiming(pendingResult.Stride, pendingResult.PlayRate, pendingResult.AnimationPhase),
                "Discard/retry advanced source time twice.");
            controller.ApplyPrepared(prepared);
            controller.CommitPrepared(prepared);
            runtime = pendingState; result = pendingResult;
            var cycle = controller.StandingCycleState;
            Require(result.Stride == cycle.Stride && result.PlayRate == cycle.PlayRate &&
                result.AnimationPhase == cycle.Phase && runtime.AnimationPhase == cycle.Phase,
                "Result/runtime/source graph do not share the committed timing.");
            if (i > 16) Require(GC.GetAllocatedBytesForCurrentThread() == allocatedBefore,
                "Source timing prepare/complete/retry/commit path allocated managed memory.");
        }
        return hz * 2;
    }

    private AlsStandingMovementInput Movement(in AlsFrameResult result, float inputAmount = 1) =>
        AlsStandingMovementInputModel.Evaluate(result.Identity, new(result.BlendCoordinates.X, 0, result.BlendCoordinates.Y),
            inputAmount, _movementSettings);

    private int CheckMovementGate(AlsLocomotionAnimationController controller, int hz)
    {
        var disabled = AlsP4AnimationInput.Disabled;
        var result = AlsFrameResult.CreateDefault(new(10000, 0, 1));
        result.AnimationState = AlsAnimationState.Grounded;
        result.ActualStance = AlsStance.Standing;
        result.ActualGait = AlsGait.Walking;
        result.ActualRotationMode = AlsRotationMode.LookingDirection;
        var checks = 0;
        foreach (var (speed, amount, maxAcceleration, shouldMove) in new[]
        {
            (.65f, 1f, 20f, true), (.65f, 0f, 20f, false),
            (1.5f, 0f, 20f, false), (1.5001f, 0f, 20f, true),
            (0f, 1f, 20f, false), (.01f, 1f, 20f, false),
            (.0101f, 1f, 20f, true), (.65f, 1f, 0f, false),
        })
        {
            result.BlendCoordinates = new(speed, 0);
            for (var i = 0; i < hz; i++)
            {
                result.Identity = new(controller.SourceEventState.Identity.FrameId + 1, 0, 1);
                result.TypedEvents.Clear();
                var input = AlsFrameInput.CreateDefault(result.Identity, 1f / hz) with
                {
                    ActualVelocity = new(speed, 0, 0), ActualAcceleration = new(-15, 0, 0),
                    MaxAcceleration = maxAcceleration,
                    Command = AlsLocomotionCommand.CreateDefault() with { MovementAxes = new(amount, 0) },
                };
                var committed = controller.StandingMovementInput;
                var cycle = controller.StandingCycleState;
                var sync = controller.StandingSync;
                if (i == 0)
                {
                    var rejected = 0;
                    try { controller.PrepareFrame(result, disabled, input.DeltaTime); }
                    catch (ArgumentException) { rejected++; }
                    try { controller.PrepareFrame(result, disabled, input with { Identity = new(9999, 0, 1) }); }
                    catch (ArgumentException) { rejected++; }
                    var invalid = Movement(result, amount) with { ShouldMove = !Movement(result, amount).ShouldMove };
                    try { controller.PrepareFrame(result, disabled, input.DeltaTime, invalid); }
                    catch (ArgumentException) { rejected++; }
                    Require(rejected == 3, "Missing, stale or inconsistent standing movement was accepted.");
                }
                var prepared = controller.PrepareFrame(result, disabled, input);
                Require(controller.StandingMovementInput == committed, "Preparation published movement input early.");
                controller.CompleteSourceEvents(prepared, ref result);
                controller.ApplyPrepared(prepared);
                controller.RollbackPrepared(prepared);
                Require(controller.StandingMovementInput == committed && controller.StandingCycleState == cycle &&
                    SameSync(controller.StandingSync, sync), "Rollback changed movement input, blend or Sync history.");
                result.TypedEvents.Clear();
                prepared = controller.PrepareFrame(result, disabled, input);
                controller.CompleteSourceEvents(prepared, ref result);
                controller.ApplyPrepared(prepared);
                controller.CommitPrepared(prepared);
                var actual = controller.StandingMovementInput;
                Require(actual.Identity == input.Identity && actual.Speed == speed && actual.ShouldMove == shouldMove &&
                    actual.MovementInputAmount == (maxAcceleration > 0 ? amount : 0),
                    "Standing input confused braking with input or changed a native threshold.");
                checks++;
            }
            Require(controller.StandingCycleState.MovingWeight == (shouldMove ? 1 : 0),
                "The actual Cycle blend did not consume ShouldMove.");
        }
        return checks;
    }

    private void CheckIdleSourceBinding(AlsAnimationSetDefinition set, AlsLocomotionAnimationProfile profile, AlsPoseAnimationProfile pose)
    {
        static string Read(string name) => Godot.FileAccess.GetFileAsString($"res://assets/config/{name}");
        var root = JsonNode.Parse(Read("v4_locomotion_source_graph.json"))!;
        var node = root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == "(N) Not Moving")!["nodes"]!.AsArray()
            .Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_SequenceEvaluator")!;
        node["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "ExplicitTime")!["value"] = "0.01";
        var sources = AlsLocomotionSourceCompiler.Compile(root.ToJsonString(), set, profile.SkeletonId);
        var p5 = AlsP5aAnimationRuntimeProfileCompiler.Compile(Read("p5a_animation_runtime.json"), set);
        var inventory = GodotAls.Import.Inspection.AlsP5SourceInventoryCompiler.Compile(Read("v4_anim_graph_inventory.json"), set, sources);
        var layout = AlsP5OccurrenceLayoutCompiler.CompileSourceAware(profile, pose, p5, sources, inventory);
        var snapshot = AlsP5CoreRuntimeBindingCompiler.CompileSourceAware(set, profile, pose, p5, layout, sources, inventory);
        using var library = AlsAnimationLibraryBuilder.Build(set, profile, pose);
        AddChild(library.Root);
        // Only this in-memory fixture is changed: a constant authored pose cannot expose a frozen sampler.
        using var idle = library.Library.GetAnimation(library.ClipNames[profile.StandingIdleAnimationId]);
        var pelvis = library.Skeleton.FindBone("pelvis");
        var positionTrack = -1;
        for (var i = 0; i < idle.GetTrackCount(); i++)
        {
            using var path = idle.TrackGetPath(i);
            if (idle.TrackGetType(i) == Godot.Animation.TrackType.Position3D && path.GetSubName(0) == "pelvis") positionTrack = i;
        }
        Require(pelvis >= 0 && positionTrack >= 0, "Idle fixture has no pelvis position track.");
        var expected = idle.PositionTrackInterpolate(positionTrack, 0) + new Vector3(.02f,0,0);
        idle.PositionTrackInsertKey(positionTrack, .01, expected);
        using var graph = AlsLocomotionGraphBuilder.Build(library, profile, pose, set, snapshot);
        var cycle = graph.StandingCycle!;
        var frame = default(AlsStandingCycleFrame);
        var result = AlsFrameResult.CreateDefault(new(1,0,1));
        result.ActualRotationMode = AlsRotationMode.LookingDirection;
        for (var i = 0; i < 10; i++)
        {
            result.Identity = new(i + 1, 0, 1);
            frame = cycle.Prepare(frame, result, 1f / 60, new(1.75f,3.75f,6.5f), Movement(result));
            Require(MathF.Abs(frame.Times[0] * set.Animations[profile.StandingIdleAnimationId].PlayLength - .01f) < .000001f,
                "Idle ignored the source evaluator explicit time.");
            Require(frame.Sync.PlayerCount == 0 && frame.Sync.SampleCount == 0 && frame.Sync.NotifyTickCount == 0,
                "Teleport Idle acquired a timed player or notify identity.");
            cycle.Apply(graph.Tree, frame);
            Require(System.Numerics.Vector3.Distance(cycle.PoseOutput[pelvis].Position, new(expected.X, expected.Y, expected.Z)) < .000001f,
                "Idle pose sampling ignored the bound explicit time.");
        }
        GD.Print("STANDING_IDLE_SOURCE_OK explicit_time=0.01 frames=10 timed_players=0 notify_ticks=0 pose_fixture=sampled");
    }

    private void CheckCycleLifetime(AlsStandingCycleGraph graph, int hz)
    {
        var result = AlsFrameResult.CreateDefault(new(1, 0, 1));
        result.ActualStance = AlsStance.Standing; result.ActualRotationMode = AlsRotationMode.LookingDirection;
        result.ResolvedLocomotionState = AlsLocomotionState.Grounded;
        var previous = graph.Prepare(default, result, 1f / hz, new(1.75f, 3.75f, 6.5f), Movement(result), includeDetail: true);
        Require(!previous.Lifetime.HasUpdated && !previous.DirectionInitialized, "Idle falsely updated the direction lifetime.");
        foreach (var player in graph.SourceBindings.Sources.Players)
            if (player.Domain == AlsLocomotionSourceDomain.Cycle)
                Require(previous.Sync.Epochs[player.PlayerId] == 0, "Unvisited Cycle source was initialized during Idle.");
        result.ActualGait = AlsGait.Sprinting; result.BlendCoordinates = new(0, 5.5f);
        for (var frame = 2; frame <= hz + 1; frame++)
        {
            result.Identity = new(frame, 0, 1);
            previous = graph.Prepare(previous, result, 1f / hz, new(1.75f, 3.75f, 6.5f), Movement(result), includeDetail: true);
        }
        Require(previous.Lifetime.HasUpdated && previous.SprintBlend.SecondWeight > 0, "Lifetime fixture did not reach Sprint.");
        result.Identity = new(hz + 10, 0, 1); result.ActualGait = AlsGait.Walking;
        result.BlendCoordinates = new(0, 1.75f);
        var next = graph.Prepare(previous, result, 1f / hz, new(1.75f, 3.75f, 6.5f), Movement(result), includeDetail: true);
        Require(next.InitializeOuterCycleSources && next.Lifetime.Epoch == previous.Lifetime.Epoch + 1,
            "Whole Cycle reentry did not initialize its outer sources.");
        Require(next.SprintBlend.Initialized && next.SprintBlend.SecondWeight > 0 && next.SprintBlend.ActiveChild == 0,
            "Reentry incorrectly cleared the cached Sprint blend instead of blending back to walking.");
        var sources = graph.SourceBindings.Sources; var count = 0; var last = -1;
        foreach (var player in sources.Players)
        {
            if (player.Domain != AlsLocomotionSourceDomain.Cycle || player.Kind == AlsLocomotionSourceKind.TeleportEvaluator) continue;
            Require(next.Sync.Epochs[player.PlayerId] == previous.Sync.Epochs[player.PlayerId] + 1, "Whole Cycle source epoch did not advance exactly once.");
            last = player.PlayerId; count++;
        }
        Require(count == 9, "Whole Cycle source closure differs.");
        var brokenSources = previous.Sync; brokenSources.Epochs[last] = long.MaxValue;
        var broken = previous with { Sync = brokenSources }; var rejected = false;
        try { graph.Prepare(broken, result, 1f / hz, new(1.75f, 3.75f, 6.5f), Movement(result), includeDetail: true); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Require(rejected, "Reentry accepted source epoch overflow.");
        var retry = graph.Prepare(previous, result, 1f / hz, new(1.75f, 3.75f, 6.5f), Movement(result), includeDetail: true);
        Require(next.Lifetime == retry.Lifetime && next.SprintBlend == retry.SprintBlend && SameSync(next.Sync, retry.Sync),
            "Rejected source initialization contaminated the next lifetime candidate.");
        graph.RestorePose();
        GD.Print($"STANDING_CYCLE_LIFETIME_OK hz={hz} outer_sources={count} reentry_epoch={next.Lifetime.Epoch} sprint_preserved=1 overflow_rejected=1 retry=identical");
    }

    private int CheckSourceTiming(AlsStandingCycleGraph graph, AlsLocomotionAnimationProfile profile,
        AlsAnimationSetDefinition set, int hz)
    {
        var sources = AlsLocomotionSourceCompiler.Compile(Godot.FileAccess.GetFileAsString(
            "res://assets/config/v4_locomotion_source_graph.json"), set, profile.SkeletonId);
        var sourceSamples = sources.RuntimeSamples;
        var players = sources.RuntimePlayers;
        int[] ids = [profile.StandingIdleAnimationId,
            .. profile.StandingWalkRun.SelectMany(p => new[] { p.WalkPoseId, p.WalkId, p.RunPoseId, p.RunId }),
            profile.StandingSamples.MaxBy(p => p.Y)!.AnimationId];
        var poseIndices = new Dictionary<int, int>();
        foreach (var player in players.Where(p => p.Domain == AlsLocomotionSourceDomain.Cycle &&
            (p.InputX == AlsSourceAxisInput.StrideBlend || p.Kind == AlsLocomotionSourceKind.Sequence &&
                sourceSamples[p.SampleStart].AnimationId == ids[25])))
        {
            var offset = player.Kind == AlsLocomotionSourceKind.Sequence ? 25 : Enumerable.Range(0, 6).Select(d => 1 + 4 * d)
                .Single(o => sourceSamples.Skip(player.SampleStart).Take(player.SampleCount).Any(s => s.AnimationId == ids[o + 1]));
            foreach (var sample in sourceSamples.Skip(player.SampleStart).Take(player.SampleCount))
                poseIndices.Add(sample.SampleId, Array.IndexOf(ids, sample.AnimationId, offset, player.SampleCount));
        }
        var frame = default(AlsStandingCycleFrame);
        var result = AlsFrameResult.CreateDefault(new AlsFrameIdentity(1, 0, 1));
        result.ActualRotationMode = AlsRotationMode.LookingDirection;
        var leaders = new HashSet<int>();
        var poseSamples = 0; var checks = 0; var sequenceSeen = false; var cleared = false; var cacheOverrides = 0;
        for (var index = 0; index < hz * 7; index++)
        {
            result.Identity = new(index + 1, 0, 1);
            var section = index / hz;
            result.ActualGait = section == 4 ? AlsGait.Sprinting : section == 2 ? AlsGait.Running : AlsGait.Walking;
            result.BlendCoordinates = section switch
            {
                0 => new(0, .65f), 1 => new(1.75f, 0), 2 => new(0, -3.75f),
                3 => new(-.85f, 0), 4 => new(0, 5.5f), 5 => default, _ => new(.7f, .7f)
            };
            var next = graph.Prepare(frame, result, 1f / hz, new(1.75f, 3.75f, 6.5f), Movement(result));
            var velocity = AlsStandingCyclePose.NormalizeVelocityWeights(next.State.VelocityBlend);
            for (var direction = 0; direction < 6; direction++)
            {
                var maximum = 0f; var sum = 0f;
                for (var state = 0; state < 6; state++)
                {
                    var local = AlsStandingCycle.DirectionWeight((AlsCycleDirection)state, direction, velocity);
                    if (local <= AlsPoseBlender.WeightThreshold) continue;
                    var weight = next.State.MovingWeight * AlsTransitionStack.Weight(next.Transitions, state) * local;
                    maximum = MathF.Max(maximum, weight); sum += weight;
                }
                Require(MathF.Abs(next.CachedUpdates[direction].Weight - maximum) < .000001f,
                    "Cycle cache update weight is not the highest state path.");
                if (sum - maximum > .0001f) cacheOverrides++;
            }
            var sprintUpdate = AlsStandingSprint.Weights(next.SprintBlend, next.SprintMask);
            for (var n = 0; n < next.Sync.NotifyTickCount; n++)
            {
                var tick = next.Sync.NotifyTicks[n]; var poseIndex = poseIndices[tick.SampleId];
                var direction = poseIndex == 25 ? 0 : (poseIndex - 1) / 4;
                var expected = next.CachedUpdates[direction];
                var multiplier = poseIndex == 25 ? sprintUpdate.SprintUpdate : direction == 0 ? sprintUpdate.ForwardUpdate : 1;
                Require(MathF.Abs(tick.Weight - expected.Weight * multiplier) < .000001f,
                    "Source notify used summed pose weight instead of cached update weight.");
                var active = expected.Active && (poseIndex == 25 ? sprintUpdate.SprintActive : direction != 0 || sprintUpdate.ForwardActive);
                Require(tick.ActiveContext == active, "Source notify lost the winning cached path's inactive state.");
            }
            Require(next.Sync.BindingStamp == sources.RuntimeStamp, "Cycle candidate lost its source snapshot identity.");
            Require(next.Sync.BindingDigest != 0 && next.Sync.LayoutDigest != 0, "Cycle candidate lost its main P5 binding identity.");
            if (index == 1)
            {
                for (var mutation = 0; mutation < 3; mutation++)
                {
                    var foreign = frame;
                    var foreignSync = foreign.Sync;
                    if (mutation == 0) foreignSync.BindingStamp = foreignSync.BindingStamp with { Digest3 = foreignSync.BindingStamp.Digest3 ^ 1 };
                    else if (mutation == 1) foreignSync.BindingDigest ^= 1;
                    else foreignSync.LayoutDigest ^= 1;
                    foreign.Sync = foreignSync;
                    var rejected = false;
                    try { graph.Prepare(foreign, result, 1f / hz, new(1.75f, 3.75f, 6.5f), Movement(result)); }
                    catch (InvalidOperationException) { rejected = true; }
                    Require(rejected, "Cycle accepted source history from another binding snapshot.");
                }
            }
            var retry = graph.Prepare(frame, result, 1f / hz, new(1.75f, 3.75f, 6.5f), Movement(result));
            Require(SameSync(next.Sync, retry.Sync), "Source Sync candidate retry changed its histories.");
            var firstBuffer = next.Times; var retryBuffer = retry.Times;
            ReadOnlySpan<float> firstTimes = firstBuffer;
            ReadOnlySpan<float> retryTimes = retryBuffer;
            Require(firstTimes.SequenceEqual(retryTimes), "Source sampling times differ on candidate retry.");
            if (next.Sync.Group.HasLeader)
            {
                leaders.Add(next.Sync.Group.LeaderPlayerId);
                Require(next.State.Phase == next.Sync.Group.Ratio % 1f, "Cycle kept a second phase clock.");
            }
            for (var p = 0; p < next.Sync.PlayerCount; p++)
            {
                var history = next.Sync.Players[p]; var source = players[history.PlayerId];
                Require(source.Domain == AlsLocomotionSourceDomain.Cycle && source.SyncGroupId == next.Sync.Group.GroupId,
                    "Cycle player lost its compiled source identity.");
                sequenceSeen |= source.Kind == AlsLocomotionSourceKind.Sequence;
                for (var s = history.SampleStart; s < history.SampleStart + history.SampleCount; s++)
                {
                    var sample = next.Sync.Samples[s]; var binding = sourceSamples[sample.SampleId];
                    Require(binding.PlayerId == history.PlayerId && binding.AnimationId == sample.AnimationId,
                        "BlendSpace sample was reassigned to another player.");
                    var poseIndex = poseIndices[sample.SampleId];
                    Require(poseIndex > 0 && MathF.Abs(next.Times[poseIndex] * binding.DurationSeconds - sample.Time) < .000001f,
                        "Pose/curve sampling does not use the synchronized source time.");
                    if (poseIndex < 25 && (poseIndex - 1) % 2 == 0 && sample.Time > .001f) poseSamples++;
                    checks++;
                }
            }
            if (section == 5 && index % hz == hz - 1)
            {
                Require(!next.Sync.Group.HasLeader && next.Sync.PlayerCount == 0 && next.Sync.SampleCount == 0,
                    "Inactive Cycle did not expire group and marker history.");
                cleared = true;
            }
            frame = next;
        }
        Require(leaders.Count >= 3 && poseSamples > 0 && sequenceSeen && cleared && frame.Sync.Group.HasLeader,
            "Source timing coverage missed leader changes, Pose samples, Sprint, idle or rejoining.");
        Require(cacheOverrides > 0, "Source timing did not cover shared directional cache updates.");
        return checks;
    }

    internal static bool SameSync(in AlsCycleSyncFrame left, in AlsCycleSyncFrame right)
    {
        ReadOnlySpan<float> leftTimes = left.Times; ReadOnlySpan<float> rightTimes = right.Times;
        ReadOnlySpan<AlsAssetPlayerHistory> leftPlayers = left.Players, rightPlayers = right.Players;
        ReadOnlySpan<AlsAssetSampleHistory> leftSamples = left.Samples, rightSamples = right.Samples;
        ReadOnlySpan<AlsP5SourceNotifyTick> leftNotifies = left.NotifyTicks, rightNotifies = right.NotifyTicks;
        ReadOnlySpan<AlsAssetSyncBatchGroupHistory> leftGroups = left.Groups, rightGroups = right.Groups;
        ReadOnlySpan<long> leftEpochs = left.Epochs, rightEpochs = right.Epochs;
        ReadOnlySpan<float> leftWeights = left.CachedWeights, rightWeights = right.CachedWeights;
        return left.Initialized == right.Initialized && left.BindingStamp == right.BindingStamp &&
            left.BindingDigest == right.BindingDigest && left.LayoutDigest == right.LayoutDigest &&
            left.PlayerCount == right.PlayerCount && left.SampleCount == right.SampleCount &&
            left.Group == right.Group && leftTimes.SequenceEqual(rightTimes) && leftPlayers.SequenceEqual(rightPlayers) &&
            leftSamples.SequenceEqual(rightSamples) && left.NotifyTickCount == right.NotifyTickCount &&
            leftNotifies.SequenceEqual(rightNotifies) && left.GroupCount == right.GroupCount &&
            leftGroups.SequenceEqual(rightGroups) && leftEpochs.SequenceEqual(rightEpochs) && leftWeights.SequenceEqual(rightWeights);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
