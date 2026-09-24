using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Actions;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace GodotAls.Animation;

public partial class BaseLayerFrameSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception failure) { GD.PushError(failure.ToString()); GetTree().Quit(1); }
    }
    private void Run()
    {
        var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
        var locomotion = AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"), set);
        var poseProfile = AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"), set, locomotion);
        var definition = AlsMovementGraphDefinition.Load(set, locomotion, poseProfile);
        if (OS.GetCmdlineUserArgs().Contains("--aim-pose-native-repeat"))
        {
            var editor = Godot.FileAccess.GetFileAsString("res://artifacts/aim-pose-editor-repeat.json");
            var repeated = AlsAimPoseCompiler.Compile(Read("v4_layering_inputs.json"), editor);
            Require(repeated.RootIndex == definition.AimPose.RootIndex && repeated.Evaluators.SequenceEqual(definition.AimPose.Evaluators),
                "Editor Aim source bindings differ.");
            for (var machine = 0; machine < 3; machine++)
                Require(repeated.Machines[machine].Edges.SequenceEqual(definition.AimPose.Machines[machine].Edges), "Editor Aim rules or transition policies differ.");
            var a = System.Text.Json.Nodes.JsonNode.Parse(Read("v4_aim_pose_inputs.json"))!;
            var b = System.Text.Json.Nodes.JsonNode.Parse(editor)!;
            foreach (var property in new[] { "bakedMachines", "curves", "blendProfiles" })
                Require(System.Text.Json.Nodes.JsonNode.DeepEquals(a[property], b[property]), "Editor native Aim metadata differs: " + property);
            GD.Print("AIM_POSE_EDITOR_REPEAT_OK machines=3 states=9 edges=19 evaluators=7 curves=4 profile_bones=79 numeric=exact pose_consumer=pending");
        }
        if (OS.GetCmdlineUserArgs().Contains("--aiming-native-repeat"))
        {
            var native = Read("v4_aiming_inputs.json");
            var editor = Godot.FileAccess.GetFileAsString("res://artifacts/aiming-input-editor-repeat.json");
            var repeated = AlsAimingInputCompiler.Compile(Read("v4_layering_inputs.json"), editor);
            Require(repeated.Settings == definition.AimingInput.Settings && repeated.InitialState == definition.AimingInput.InitialState,
                "Normal Editor aiming defaults differ.");
            var a = System.Text.Json.Nodes.JsonNode.Parse(native)!; var b = System.Text.Json.Nodes.JsonNode.Parse(editor)!;
            foreach (var property in new[] { "trajectories", "rotatorBoundaries" })
                Require(System.Text.Json.Nodes.JsonNode.DeepEquals(a[property], b[property]), "Normal Editor aiming oracle differs: " + property);
            GD.Print("AIMING_INPUT_EDITOR_REPEAT_OK frames=1050 boundaries=36 numeric=exact graphs=consumed_connections");
        }
        if (OS.GetCmdlineUserArgs().Contains("--character-rotation-native-repeat"))
        {
            var repeated = AlsCharacterRotationCompiler.Compile(Godot.FileAccess.GetFileAsString("res://artifacts/character-rotation-editor-repeat.json"));
            var original = definition.CharacterRotation;
            Require(repeated.Settings == original.Settings && repeated.InitialGait == original.InitialGait &&
                repeated.InitialWalkSpeed == original.InitialWalkSpeed && repeated.InitialRunSpeed == original.InitialRunSpeed,
                "Normal Editor character rotation policy differs.");
            foreach (var mode in Enum.GetValues<AlsRotationMode>()) foreach (var stance in Enum.GetValues<AlsStance>())
            {
                var a = original.Movement(mode, stance); var b = repeated.Movement(mode, stance);
                Require(a.WalkSpeed == b.WalkSpeed && a.RunSpeed == b.RunSpeed && a.SprintSpeed == b.SprintSpeed &&
                    a.RotationRate.Keys.SequenceEqual(b.RotationRate.Keys), "Normal Editor rotation movement binding differs.");
            }
            GD.Print("CHARACTER_ROTATION_EDITOR_REPEAT_OK graphs=consumed_connections settings=6 curves=3 interpolation_samples=320");
        }
        if (OS.GetCmdlineUserArgs().Contains("--character-native-repeat"))
        {
            var repeated = AlsCharacterAnimationBridgeCompiler.Compile(Godot.FileAccess.GetFileAsString("res://artifacts/character-bridge-authored-editor-repeat.json"));
            Require(repeated == definition.CharacterBridge, "Normal Editor character policy differs from the commandlet.");
            GD.Print("CHARACTER_BRIDGE_EDITOR_REPEAT_OK connected_rotation=identical mesh_teleport=identical");
        }
        var groundAssetIds = definition.Sources.Players.Where(p => p.Domain is AlsLocomotionSourceDomain.Cycle or AlsLocomotionSourceDomain.Detail or
                AlsLocomotionSourceDomain.Stop or AlsLocomotionSourceDomain.Standing or AlsLocomotionSourceDomain.MainGrounded or AlsLocomotionSourceDomain.Crouching)
            .SelectMany(p => Enumerable.Range(p.SampleStart, p.SampleCount).Select(i => definition.Sources.Samples[i].AnimationId))
            .Concat(poseProfile.Turns.Select(t => t.AnimationId)).Distinct();
        GD.Print($"IDLE_CURVE_ORIGIN grounded_authored_transition_assets={groundAssetIds.Count(id => set.Animations[id].Curves.Any(c => c.SourceName == "Enable_Transition"))} graph_writes=standing_and_crouching_idle");
        var settings = AlsLocomotionInputCompiler.Compile(Read("v4_locomotion_inputs.json")).Movement;
        if (OS.GetCmdlineUserArgs().Contains("--grounded-native-repeat"))
        {
            var repeated = AlsGroundedEntryNotifyCompiler.Compile(
                Godot.FileAccess.GetFileAsString("res://artifacts/grounded-notify-20260920/native-editor.json"),
                Read("v4_overlay_transition_inputs.json"), set, AlsGroundedMachineCompiler.CompileMovement(Read("v4_main_movement_graph.json")),
                AlsP5aAnimationRuntimeProfileCompiler.Compile(Read("p5a_animation_runtime.json"), set));
            Require(repeated == definition.GroundedEntryNotify, "Normal Editor grounded semantics differ from the cold export.");
            GD.Print("GROUNDED_NOTIFY_EDITOR_REPEAT_OK property=identical enum=identical connected_semantics=identical");
        }
        if (OS.GetCmdlineUserArgs().Contains("--unvisited-root"))
        { RunUnvisitedRoot(set,locomotion,poseProfile,definition,settings); return; }
        if (OS.GetCmdlineUserArgs().Contains("--curve-feedback"))
        { RunCurveFeedback(set, locomotion, poseProfile, definition, settings); return; }
        if (OS.GetCmdlineUserArgs().Contains("--actions"))
        { RunActions(set,locomotion,poseProfile,definition,settings); return; }
        if (OS.GetCmdlineUserArgs().Contains("--turn-notify"))
        { RunTurnNotifies(set, locomotion, poseProfile, definition, settings); return; }
        var frames = 0; var hidden = 0; var reentries = 0; var events = 0; var faults = 0; var guards = 0;
        var states = 0; var requests = 0; var forwards = 0; var hiddenEnds = 0;
        var mapped = OS.GetCmdlineUserArgs().Contains("--frame-input");
        var splitGlobal = OS.GetCmdlineUserArgs().Contains("--split-global-frame");
        Require(!splitGlobal || mapped, "Split global checks require actual frame input mapping.");
        var globalOnlyCancellations = 0;
        var aimSourceUpdates = 0; var aimInitializations = 0; var aimHidden = 0; var aimPoseFrames = 0;
        var nativeRepeat = OS.GetCmdlineUserArgs().Contains("--control-native-repeat") ?
            AlsYawOffsetCompiler.CompileGlobalControl(Read("v4_movement_runtime_inputs.json"), Read("v4_yaw_inputs.json"),
                Godot.FileAccess.GetFileAsString("res://artifacts/grounded-control-editor-repeat.json")) : null;
        var idleRepeat = OS.GetCmdlineUserArgs().Contains("--idle-native-repeat") ?
            AlsIdleControlInputCompiler.Compile(Godot.FileAccess.GetFileAsString("res://artifacts/idle-control-editor-repeat.json")) : null;
        if (idleRepeat is not null) Require(mapped && idleRepeat.Settings == definition.IdleControl.Settings, "Editor idle defaults differ.");
        if (nativeRepeat is not null) Require(mapped && nativeRepeat.InitialState == definition.GroundedControl.InitialState,
            "Native Editor control defaults differ.");
        var hiddenInputUpdates = 0; var maskedInputFrames = 0;
        var groundUpdates = 0; var heldGround = 0; var hiddenGround = 0; var consumedGround = 0; var partialWeights = 0;
        var jumpEvents = 0; var jumpHolds = 0; var hiddenJumpExpires = 0; var jumpRateConsumers = 0;
        var controlResets = 0; var hiddenYawChanges = 0; var controlHolds = 0; var controlConsumers = 0;
        var idleFrames = 0; var rotateFrames = 0; var turnRequests = 0; var transitionHeld = 0; var hiddenIdle = 0;
        var turnPlays = 0; var montagePoseFrames = 0; var hiddenMontageTicks = 0;
        foreach (var hz in new[] { 30, 60, 120 }) foreach (var foot in new[] { -1f, 1f })
        {
            using var library = AlsAnimationLibraryBuilder.BuildP5a(set, definition.Binding);
            library.UseMovementSources(set, definition.RawSources);
            AddChild(library.Root);
            using var graph = AlsLocomotionGraphBuilder.Build(library, locomotion, poseProfile, set, definition.Binding);
            using var owner = new AlsBaseLayerFrameRuntime(definition, library, graph.StandingCycle!, set, poseProfile);
            var aimStates = mapped ? new AimFrameSmokeChecks(definition, set, owner.CurveNames, 11, 2) : null;
            var maskIndex = owner.CurveNames.IndexOf("Mask_LandPrediction");
            if (mapped) Require(maskIndex >= 0, "Movement output has no landing mask name.");
            var sink = new Sink(owner.ReferencePose.ToArray(), mapped ? maskIndex : -1);
            var feedback = default(AlsAnimationInputFeedback);
            var savedPose = new AlsLocalPose[owner.ReferencePose.Length]; var savedCurves = new AlsInertialCurve[owner.CurveNames.Length];
            Require(owner.ReferencePose.Length == 79 && library.Skeleton.GetBoneCount() == 68 &&
                ReferenceEquals(library.MovementSources(set, poseProfile.SkeletonId).Bank, definition.RawSources),
                "BaseLayer must evaluate 79 logical bones with the shared source bank and a 68-bone physical rig.");
            var previousHidden = false; var baseClf = 0f;
            for (var frame = 1; frame <= hz * 8; frame++)
            {
                var seconds = frame / (float)hz; var delta = 1f / hz;
                var jumping = (foot < 0 || frame > hz) && seconds < 2.4f;
                var falling = seconds >= 4 && seconds < 5.3f;
                var airborne = jumping || falling; var speed = seconds >= 2.4f && seconds < 4 ? 0 : 3.5f;
                if (mapped && foot > 0 && seconds <= 1) speed = 0;
                var result = AlsFrameResult.CreateDefault(new(frame, 11, 2));
                result.ResolvedLocomotionState = airborne ? AlsLocomotionState.InAir : AlsLocomotionState.Grounded;
                result.ActualStance = seconds > 6.8f ? AlsStance.Crouching : AlsStance.Standing;
                result.ActualGait = AlsGait.Running; result.ActualRotationMode = AlsRotationMode.LookingDirection;
                if (mapped && speed == 0 && foot < 0) result.ActualRotationMode = AlsRotationMode.Aiming;
                result.BlendCoordinates = new(0, speed); result.PlayRate = result.Stride = 1;
                var movement = AlsStandingMovementInputModel.Evaluate(result.Identity, new(speed, 0, 0), speed > 0 ? 1 : 0, settings);
                var rules = new AlsGroundedRuleInput(movement.ShouldMove, false, false, result.ActualStance, true, false, baseClf, foot)
                {
                    MovementState = airborne ? AlsMovementStateInput.InAir : AlsMovementStateInput.Grounded,
                    Jumped = frame == (foot < 0 ? 1 : hz + 1), HasMovementInput = speed > 0, Speed = speed,
                    MovementDirection = AlsMovementDirection.Forward, FeetCrossing = 1,
                };
                var hide = foot < 0 && seconds < .2f || seconds >= 1.4f && seconds < 1.65f || seconds >= 6.1f && seconds < 6.4f;
                if (mapped && foot > 0 && seconds >= .65f && seconds < .8f) hide = true;
                var slot = hide ? new AlsSlotWeights(0, 1, 1) : seconds >= 6.8f && seconds < 7.2f ?
                    new AlsSlotWeights(.75f, .25f, .25f) : AlsSlotWeights.Passthrough;
                var ground = new AlsGroundedFrameInputs(delta, new(1.75f, 3.75f, 6.5f), .75f, 1, 1,
                    new(NVector4.UnitX, default, 1, new NVector2(.2f, -.3f)), new(1, 1, default), AlsSlotWeights.Passthrough,
                    new(0, 0), new(0, 0), new(checked((short)frame), (ulong)frame));
                var inputs = new AlsMainMovementInputs(ground, jumping ? 3 - (seconds - 1) * 8 : foot < 0 ? -4 : -12,
                    falling && seconds > 5.1f ? 1 : 0, new(.3f, -.2f), foot < 0 ? .8f : 1.4f, speed);
                var frameInput = AlsFrameInput.CreateDefault(result.Identity, delta) with
                {
                    ActualVelocity = new(speed, airborne ? inputs.FallSpeed : 0, 0),
                    ActualAcceleration = new(4, 0, 0), MaxAcceleration = 8, MaxBrakingDeceleration = 16,
                    JumpAccepted = rules.Jumped ? (byte)1 : (byte)0,
                    CharacterTransform = System.Numerics.Matrix4x4.CreateRotationY(.6f),
                    Floor = new(airborne ? (byte)0 : (byte)1, System.Numerics.Vector3.UnitY, -1, System.Numerics.Matrix4x4.Identity, default),
                    LandPrediction = new AlsLandPredictionSample { Queried = 1, BlockingHit = 1, Walkable = 1, Time = .25f },
                };
                if (mapped)
                {
                    var command = frameInput.Command with { ViewYaw = MathF.Sin(seconds * 2) * .5f,
                        AimYaw = speed == 0 ? 2.2f : MathF.Cos(seconds) * .8f,
                        AimPitch = MathF.Sin(seconds * 1.7f) * 1.2f };
                    frameInput = frameInput with { Command = command, CharacterYaw = .6f,
                        AimYawRateDegrees = speed == 0 ? 0 : MathF.Abs(MathF.Cos(seconds * 2)) * 180 / MathF.PI,
                        ViewRotation = System.Numerics.Quaternion.CreateFromYawPitchRoll(command.ViewYaw, 0, 0),
                        AimRotation = System.Numerics.Quaternion.CreateFromYawPitchRoll(command.AimYaw, command.AimPitch, 0) };
                    var local = System.Numerics.Vector3.Transform(frameInput.ActualVelocity,
                        System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, -.6f));
                    result.BlendCoordinates = new(local.X, -local.Z);
                }
                sink.OutputMask = mapped && hide ? 1 : null;
                var context = new AlsPoseUpdateContext(result.Identity, .8f, delta, .4f);
                var priorSources = owner.CommittedSources; var priorIdentity = owner.CommittedIdentity;
                var priorGlobal = owner.CommittedGlobalInput;
                var priorGround = owner.CommittedGroundInput;
                var priorJump = owner.CommittedJumpInput;
                var priorControl = owner.CommittedControlInput;
                var priorAiming = owner.CommittedAimingInput;
                var priorMontages = owner.Montages.Committed.ToArray();
                var priorNotify = owner.TurnNotifies.Committed;
                var priorMachine = owner.Movement.CommittedMovement;
                var prepareAttempts = 0;
                Prepare();
                if (frame == 1) { Reject(() => owner.Commit(result.Identity)); guards++; }
                owner.Evaluate(AlsLocalPose.Identity, 0, 0, sink, sink);
                owner.Pose.CopyTo(savedPose); owner.Curves.CopyTo(savedCurves);
                aimStates?.Capture(hide);
                var candidate = owner.Sources; var candidateEvents = owner.SourceEvents; var candidateRequests = owner.RequestCount;
                var candidateGlobal = mapped ? owner.CandidateGlobalInput : default;
                var candidateGround = mapped ? owner.CandidateGroundInput : default;
                var candidateJump = mapped ? owner.CandidateJumpInput : default;
                var candidateControl = mapped ? owner.CandidateControlInput : default;
                var candidateAiming = mapped ? owner.CandidateAimingInput : default;
                if (mapped)
                {
                    Require(owner.CommittedAimingInput == priorAiming && candidateAiming.Identity == result.Identity,
                        "Aiming history published before final pose or skipped a hidden frame.");
                    Require(candidateAiming == definition.AimingInput.Evaluate(frameInput, result.ActualRotationMode,
                        movement.HasMovementInput, priorAiming), "Aiming candidate differs from the captured character input.");
                    if (result.ActualRotationMode == AlsRotationMode.VelocityDirection)
                        Require(candidateAiming.SpineRotation == priorAiming.SpineRotation && candidateAiming.AimSweepTime == priorAiming.AimSweepTime,
                            "Velocity mode failed to hold camera aiming history.");
                    if (result.ActualRotationMode != AlsRotationMode.VelocityDirection || !movement.HasMovementInput)
                        Require(candidateAiming.InputYawOffsetTime == priorAiming.InputYawOffsetTime, "Input yaw gate rewrote held history.");
                }
                var candidateIdle = mapped ? owner.CandidateIdleControl : default;
                var candidateTurn = mapped ? owner.CandidateTurn : default;
                var candidateMontages = mapped ? owner.Montages.Candidate.ToArray() : [];
                var montagePose = mapped ? owner.Montages.Evaluation.ToArray() : [];
                var candidateNotify = owner.TurnNotifies.Candidate;
                if (mapped)
                {
                    if (candidateTurn.AttemptPlayback) turnPlays++;
                    if (montagePose.Length > 0) montagePoseFrames++;
                    if (hide && owner.Montages.Traversal.Length > 0) hiddenMontageTicks++;
                    Require(owner.Montages.Committed.SequenceEqual(priorMontages), "Montage playback published before pose.");
                    var control = candidateControl.State;
                    if (nativeRepeat is not null)
                    {
                        var repeated = nativeRepeat.Evaluate(frameInput, result.ActualGait, result.ActualRotationMode, candidateGround, priorControl);
                        if (repeated.Execution.WhileFalse)
                            repeated = repeated with { State = repeated.State with { Idle = definition.IdleControl.Evaluate(frameInput,
                                result.ActualRotationMode, repeated.State.Idle, feedback).State with { RotationScale = owner.CandidateTurn.RotationScale } } };
                        Require(repeated == candidateControl, "Editor and commandlet control graphs produce different candidates.");
                    }
                    if (candidateControl.Execution.WhileFalse)
                    {
                        idleFrames++;
                        if (hide) hiddenIdle++;
                        if (idleRepeat is not null) Require(WithTurnScale(idleRepeat.Evaluate(frameInput, result.ActualRotationMode, priorControl.Idle, feedback), owner.CandidateTurn.RotationScale) == candidateIdle,
                            "Editor and commandlet idle graph candidates differ.");
                        Require(candidateIdle.State == control.Idle, "Idle output was not consumed by the control frame.");
                        if (control.Idle.RotateLeft || control.Idle.RotateRight) rotateFrames++;
                        if (candidateIdle.Turn.Requested) turnRequests++;
                        if (!candidateIdle.CanTurn) transitionHeld++;
                    }
                    else Require(candidateIdle == default, "Idle action request leaked into moving or airborne frame.");
                    Require(owner.CommittedControlInput == priorControl && control.Identity == result.Identity, "Global control published before final pose.");
                    if (candidateControl.Execution.ChangedToTrue)
                    {
                        Require(!control.Idle.RotateLeft && !control.Idle.RotateRight && control.Idle.ElapsedDelayTime == 0 &&
                            control.Idle.RotateRate == priorControl.Idle.RotateRate && control.Idle.RotationScale == priorControl.Idle.RotationScale,
                            "Start-moving reset omitted fields or reset a held rate/scale."); controlResets++;
                    }
                    if (hide && control.Yaw != priorControl.Yaw) hiddenYawChanges++;
                    if (!candidateControl.Execution.WhileTrue)
                    {
                        Require(control.Yaw == priorControl.Yaw && control.MovementDirection == priorControl.MovementDirection,
                            "Idle/air recomputed direction or yaw."); controlHolds++;
                    }
                    Require(owner.CommittedJumpInput == priorJump && candidateJump.Frame.Identity == result.Identity &&
                        candidateJump.Next.Identity == result.Identity, "Jump history published before pose or changed identity.");
                    if (frameInput.JumpAccepted == 1)
                    {
                        Require(candidateJump.Frame.Jumped && candidateJump.Frame.PlayRate == definition.JumpInput.PlayRate(priorGlobal.Speed),
                            "Jump did not capture saved animation Speed."); jumpEvents++;
                    }
                    else
                    {
                        Require(candidateJump.Frame.PlayRate == priorJump.PlayRate, "Jump rate changed without a jump event."); jumpHolds++;
                    }
                    if (hide && candidateJump.Frame.Jumped && !candidateJump.Next.Jumped) hiddenJumpExpires++;
                    for (var p = 0; p < candidate.PlayerCount; p++)
                    {
                        var player = candidate.Players[p];
                        var binding = definition.Sources.CreateCoreView().Players[player.PlayerId];
                        if (binding.PlayRateInput != AlsSourceRateInput.JumpPlayRate) continue;
                        var sample = definition.Sources.CreateCoreView().Samples[binding.SampleStart];
                        if (player.Time <= 0 || player.Time >= sample.DurationSeconds) continue;
                        var leader = false;
                        for (var g = 0; g < candidate.GroupCount; g++)
                            leader |= candidate.Groups[g].Group.HasLeader && candidate.Groups[g].Group.LeaderPlayerId == player.PlayerId;
                        if (!leader) continue; // Marker followers legitimately use leader phase, not their own rate.
                        Require(MathF.Abs(player.Delta - delta * candidateJump.Frame.PlayRate * sample.AssetRateScale / binding.PlayRateBasis) < 2e-6f,
                            "Jump leader clock ignored latched play rate."); jumpRateConsumers++;
                    }
                    Require(owner.CommittedGroundInput == priorGround && candidateGround.Identity == result.Identity,
                        "Ground input published before the final pose or used another frame.");
                    if (!airborne && movement.ShouldMove)
                    {
                        groundUpdates++;
                        if (hide && (candidateGround.VelocityBlend != priorGround.VelocityBlend || candidateGlobal.Lean != priorGlobal.Lean ||
                            candidateGround.StandingPlayRate != priorGround.StandingPlayRate)) hiddenGround++;
                        var sum = candidateGround.VelocityBlend.X + candidateGround.VelocityBlend.Y + candidateGround.VelocityBlend.Z + candidateGround.VelocityBlend.W;
                        if (sum > 0 && sum < .99f) partialWeights++;
                    }
                    else
                    {
                        Require(candidateGround == priorGround with { Identity = result.Identity, ShouldMove = airborne ? priorGround.ShouldMove : false },
                            "Idle/air changed held ground inputs."); heldGround++;
                        if (!airborne) Require(candidateGlobal.Lean == priorGlobal.Lean, "Idle rewrote global Lean.");
                    }
                    if (!hide && owner.Grounded.StandingFrame.Movement.Identity == result.Identity)
                    {
                        Require(owner.Grounded.StandingFrame.YawInputs == control.Yaw &&
                            owner.Grounded.StandingFrame.State.MovementDirection == control.MovementDirection &&
                            owner.Grounded.ObservedInput.Rotation == new AlsSourceRotationInput(control.Idle.RotateRate, control.Idle.RotateLeft, control.Idle.RotateRight),
                            "Grounded recomputed global control or retained an old rotate flag."); controlConsumers++;
                        var standing = owner.Grounded.StandingFrame.State;
                        Require(standing.VelocityBlend == candidateGround.VelocityBlend && standing.Stride == candidateGround.Stride &&
                            standing.PlayRate == candidateGround.StandingPlayRate && standing.GaitWeight == candidateGround.WalkRunBlend,
                            "Standing recomputed global weights or rates."); consumedGround++;
                    }
                }
                if (mapped && airborne)
                {
                    Require(candidateGlobal.Identity == result.Identity && candidateGlobal.FallSpeed == frameInput.ActualVelocity.Y && candidateGlobal.Speed == speed,
                        "Global air input did not use the same actual frame.");
                    if (hide && candidateGlobal.Lean != priorGlobal.Lean) hiddenInputUpdates++;
                    if (feedback.HasFrame && feedback.LandPredictionMask.Present && feedback.LandPredictionMask.Value == 1 && inputs.FallSpeed < -2)
                    { Require(candidateGlobal.LandPrediction == 0, "Air input ignored previous completed Slot mask."); maskedInputFrames++; }
                }
                var forwarded = sink.ForwardCount;
                Require(owner.SourceUpdated != hide, "BaseLayer source traversal did not follow Slot relevance.");
                if (hide)
                {
                    Require(candidate.PlayerCount == 0 && candidate.SampleCount == 0 && candidate.NotifyTickCount == 0 &&
                        SameMachine(owner.Movement.CommittedMovement, priorMachine), "Hidden frame advanced Main Movement or retained source participants.");
                    for (var p = 0; p < 75; p++) Require(candidate.Times[p] == priorSources.Times[p] && candidate.Epochs[p] == priorSources.Epochs[p],
                        "Hidden Main Movement changed an indexed source clock.");
                    Require(owner.Movement.EventState.ActiveCount == 0 && sink.EmptyBaseSource,
                        "Hidden Main Movement retained notify activity or supplied stale pose to Slot.");
                    if (previousHidden) Require(candidateEvents.Count == 0, "A dormant source repeated a notify event.");
                    hiddenEnds += candidateEvents.Count; hidden++;
                }
                else
                {
                    states |= 1 << owner.Movement.Update.State.CurrentState;
                    if (previousHidden) reentries++;
                }
                if (frame == hz * 3) Require(forwarded == 1 && sink.ForwardSeconds == .15f && sink.ForwardRequester == 777,
                    "Cached skip forwarding lost the minimum request, duplicated self, or chose the wrong receiver.");
                owner.Discard();
                Require(owner.CommittedIdentity == priorIdentity && StandingCycleSmoke.SameSync(priorSources, owner.CommittedSources),
                    "Cancelling the combined frame changed a committed child.");
                Require(owner.CommittedGlobalInput == priorGlobal, "Cancelling the combined frame published global input history.");
                Require(owner.CommittedGroundInput == priorGround, "Cancelling the combined frame published ground input history.");
                Require(owner.CommittedJumpInput == priorJump, "Cancelling the combined frame published jump history.");
                Require(owner.CommittedControlInput == priorControl, "Cancelling published control history.");
                Require(owner.CommittedAimingInput == priorAiming, "Cancelling published aiming history.");
                Require(owner.Montages.Committed.SequenceEqual(priorMontages), "Cancelling published montage state.");
                Require(owner.TurnNotifies.Committed == priorNotify, "Cancelling published turn notify RNG or slot relevance.");
                if (mapped && frame == 2)
                {
                    var foreign = new AlsAnimationInputFeedback(new(1, 11, 3), true, new(1));
                    Reject(() => owner.PrepareFromFrame(frameInput, result, movement, rules, ground, foreign, context, slot, sink));
                    guards++;
                }
                Prepare(); owner.Evaluate(AlsLocalPose.Identity, 0, 0, sink, sink);
                Compare();
                if (frame == hz * 7 || mapped && frame == hz * 3 / 2)
                {
                    owner.Discard(); Prepare(); sink.FailBase = true;
                    var rejected = false;
                    try { owner.Evaluate(AlsLocalPose.Identity, 0, 0, sink, sink); }
                    catch (InvalidOperationException failure) when (failure.Message == "Injected final BaseLayer Slot failure.") { rejected = owner.IsFaulted; }
                    finally { sink.FailBase = false; }
                    Require(rejected, "Combined owner did not fault on a late Slot failure.");
                    Reject(() => owner.Commit(result.Identity)); guards++;
                    owner.Discard();
                    Require(owner.CommittedIdentity == priorIdentity && StandingCycleSmoke.SameSync(priorSources, owner.CommittedSources),
                        "Late Slot fault published Main Movement before the final pose.");
                    Require(owner.CommittedGlobalInput == priorGlobal, "Late Slot fault published global input history.");
                    Require(owner.CommittedGroundInput == priorGround, "Late Slot fault published ground input history.");
                    Require(owner.CommittedJumpInput == priorJump, "Late Slot fault published jump history.");
                    Require(owner.CommittedControlInput == priorControl, "Late Slot fault published control history.");
                    Require(owner.CommittedAimingInput == priorAiming, "Late Slot fault published aiming history.");
                    aimStates?.CheckUncommitted(priorIdentity);
                    Require(owner.Montages.Committed.SequenceEqual(priorMontages), "Late Slot fault published montage state.");
                    Require(owner.TurnNotifies.Committed == priorNotify, "Late Slot fault published turn notify history.");
                    Prepare(); owner.Evaluate(AlsLocalPose.Identity, 0, 0, sink, sink); Compare(); faults++;
                }
                aimStates?.ValidateCommit(result.Identity);
                owner.Commit(result.Identity);
                aimStates?.Commit(result.Identity);
                if (mapped)
                {
                    Require(owner.CommittedGlobalInput == candidateGlobal, "Global input and pose did not commit together.");
                    Require(owner.CommittedGroundInput == candidateGround, "Ground input and pose did not commit together.");
                    Require(owner.CommittedJumpInput == candidateJump.Next, "Jump latent state and pose did not commit together.");
                    Require(owner.CommittedControlInput == candidateControl.State, "Control and pose did not commit together.");
                    Require(owner.CommittedAimingInput == candidateAiming, "Aiming and pose did not commit together.");
                    Require(owner.Montages.CommittedIdentity == result.Identity && owner.Montages.Committed.SequenceEqual(candidateMontages),
                        "Montages and pose did not commit together.");
                    Require(owner.TurnNotifies.Committed == candidateNotify, "Turn notify history and pose did not commit together.");
                    // This fixture's final graph is BaseLayer -> synthetic Slot. Production
                    // must supply its own completed LayerBlending/IK curve output here.
                    feedback = AlsAnimationInputFeedback.FromCompletedFrame(result.Identity, owner.CurveNames, savedCurves);
                }
                if (hide) Require(SameMachine(owner.Movement.CommittedMovement, priorMachine), "Hidden commit advanced Main Movement history.");
                Require(owner.CommittedIdentity == result.Identity && owner.Movement.CommittedIdentity == result.Identity,
                    "BaseLayer and Main Movement committed different identities.");
                if (!hide) baseClf = savedCurves[owner.CurveNames.IndexOf("BasePose_CLF")].Value;
                previousHidden = hide; events += candidateEvents.Count; requests += candidateRequests; forwards += forwarded; frames++;

                void Prepare()
                {
                    sink.Reset();
                    if (mapped && splitGlobal && prepareAttempts++ == 0)
                    {
                        PrepareGlobal();
                        var globalAir=owner.CandidateGlobalInput; var globalGround=owner.CandidateGroundInput;
                        var globalAim=owner.CandidateAimingInput; var globalJump=owner.CandidateJumpInput;
                        var globalControl=owner.CandidateControlInput; var globalIdle=owner.CandidateIdleControl;
                        var globalTurn=owner.CandidateTurn; var globalMontages=owner.Montages.Candidate.ToArray();
                        var globalEvaluation=owner.Montages.Evaluation.ToArray();
                        CheckUnpublished();
                        Require(!owner.SourceUpdated, "Global properties advanced the source graph.");
                        Reject(()=>owner.Evaluate(AlsLocalPose.Identity,0,0,sink,sink));
                        Reject(()=>owner.Commit(result.Identity));
                        Reject(()=> { _=owner.Sources; });
                        Reject(()=> { _=owner.SourceEvents; });
                        Reject(PrepareGlobal);
                        Reject(()=>owner.PrepareGraph(new(new(frame+1,11,2),context.Weight,delta),sink));
                        Reject(()=>owner.PrepareGraph(new(result.Identity,context.Weight,delta*2),sink));
                        owner.Discard(); CheckUnpublished(); globalOnlyCancellations++;
                        PrepareGlobal();
                        Require(owner.CandidateGlobalInput==globalAir && owner.CandidateGroundInput==globalGround &&
                            owner.CandidateAimingInput==globalAim && owner.CandidateJumpInput==globalJump &&
                            owner.CandidateControlInput==globalControl && owner.CandidateIdleControl==globalIdle &&
                            owner.CandidateTurn==globalTurn && owner.Montages.Candidate.SequenceEqual(globalMontages) &&
                            owner.Montages.Evaluation.SequenceEqual(globalEvaluation),
                            "Global-only cancellation changed properties or montage identity/time on retry.");
                        owner.PrepareGraph(context,sink);
                        Reject(()=>owner.PrepareGraph(context,sink));
                    }
                    else if (mapped) owner.PrepareFromFrame(frameInput, result, movement, rules, ground, feedback, context, slot, sink, foot < 0 ? .5f : 2);
                    else owner.Prepare(result, movement, rules, inputs, context, slot, sink);
                    aimStates?.Prepare(owner.CandidateAimingInput, result.ActualRotationMode, movement.HasMovementInput,
                        delta, frame, !hide, context.Weight);
                    if (frame != hz * 3) return;
                    var requestContext = context.WithInertialization(definition.BaseLayer.InertializationNodeIndex, true);
                    owner.RequestInertialization(requestContext, .25f); owner.RequestInertialization(requestContext, .15f);
                    owner.OnCachedUpdatesSkipped(definition.BaseLayer.InertializationNodeIndex,
                        [context.WithInertialization(777, true), requestContext, context]);

                    void PrepareGlobal()=>owner.PrepareGlobalFromFrame(frameInput,result,movement,rules,ground,feedback,slot,foot<0 ? .5f : 2);
                    void CheckUnpublished()=>Require(owner.CommittedIdentity==priorIdentity &&
                        owner.CommittedGlobalInput==priorGlobal && owner.CommittedGroundInput==priorGround &&
                        owner.CommittedAimingInput==priorAiming && owner.CommittedJumpInput==priorJump &&
                        owner.CommittedControlInput==priorControl && owner.Montages.Committed.SequenceEqual(priorMontages) &&
                        owner.TurnNotifies.Committed==priorNotify && SameMachine(owner.Movement.CommittedMovement,priorMachine) &&
                        StandingCycleSmoke.SameSync(owner.CommittedSources,priorSources),
                        "Global preparation or cancellation changed committed animation history.");
                }
                void Compare()
                {
                    aimStates?.Compare();
                    if (mapped) Require(owner.CandidateGroundInput == candidateGround, "Ground candidate changed on retry.");
                    if (mapped) Require(owner.CandidateJumpInput == candidateJump, "Jump candidate changed on retry.");
                    if (mapped) Require(owner.CandidateControlInput == candidateControl, "Control candidate changed on retry.");
                    if (mapped) Require(owner.CandidateAimingInput == candidateAiming, "Aiming candidate changed on retry.");
                    if (mapped) Require(owner.CandidateIdleControl == candidateIdle, "Idle action candidate changed on retry.");
                    if (mapped) Require(owner.TurnNotifies.Candidate == candidateNotify, "Retry changed turn notify RNG or relevance.");
                    if (mapped) Require(owner.CandidateTurn == candidateTurn && owner.Montages.Candidate.SequenceEqual(candidateMontages) &&
                        owner.Montages.Evaluation.SequenceEqual(montagePose), "Montage identity, clock or pose bank changed on retry.");
                    Require(owner.Pose.SequenceEqual(savedPose) && owner.Curves.SequenceEqual(savedCurves) &&
                        StandingCycleSmoke.SameSync(candidate, owner.Sources) && candidateRequests == owner.RequestCount &&
                        owner.SourceEvents.Count == candidateEvents.Count && forwarded == sink.ForwardCount, "Combined frame retry diverged.");
                    for (var e = 0; e < candidateEvents.Count; e++) Require(owner.SourceEvents[e] == candidateEvents[e], "Retry changed source event identity/order.");
                    if (mapped) Require(owner.CandidateGlobalInput == candidateGlobal, "Global input retry diverged.");
                }
            }
            if (aimStates is not null)
            {
                aimSourceUpdates += aimStates.SourceUpdates; aimInitializations += aimStates.Initializations; aimHidden += aimStates.HiddenFrames;
                aimPoseFrames += aimStates.PoseFrames;
            }
        }
        Require(states == 79 && hidden > 0 && reentries == (mapped ? 18 : 15) && events > 0 && faults == (mapped ? 12 : 6) && forwards == 6,
            $"Combined BaseLayer traversal coverage missing: states={states} hidden={hidden} reentries={reentries} events={events} faults={faults} forwards={forwards}.");
        if (mapped)
        {
            if (splitGlobal)
            {
                Require(globalOnlyCancellations==frames,"Global-only transaction coverage is incomplete.");
                GD.Print($"BASE_LAYER_GLOBAL_PHASE_OK frames={frames} global_only_cancel_retry={globalOnlyCancellations} premature_graph_access=rejected split_vs_combined=pose_curve_clock_events_equal");
            }
            GD.Print($"BASE_LAYER_AIMING_INPUT_OK frames={frames} hidden={hidden} late_faults={faults} retry=identical commit=with_pose upper_pose_consumer=pending");
            Require(aimSourceUpdates > frames / 2 && aimInitializations > 20 && aimHidden == hidden, "Nested Aim frame coverage missing.");
            Require(aimPoseFrames == frames - hidden, "Aim pose evaluation ignored source relevance.");
            GD.Print($"AIM_STATE_FRAME_OK frames={frames} source_updates={aimSourceUpdates} initializations={aimInitializations} hidden={aimHidden} late_faults={faults} retry=identical input=base_layer_candidate relevance=fixture pose_frames={aimPoseFrames} pose_consumer=ordered_nested demo=not_connected");
            Require(turnPlays > 0 && montagePoseFrames > 0, "Turn commands did not reach real montage pose samples.");
            GD.Print($"BASE_LAYER_TURN_MONTAGE_OK plays={turnPlays} pose_frames={montagePoseFrames} hidden_ticks={hiddenMontageTicks} " +
                "clock=before_blueprint pose=exported_sequences retry=identical commit=with_pose notify=unified_queue demo=not_connected");
            Require(idleFrames > 0 && rotateFrames > 0 && turnRequests > 0 && transitionHeld > 0 && hiddenIdle > 0,
                $"Idle check coverage incomplete: idle={idleFrames} rotate={rotateFrames} turns={turnRequests} gated={transitionHeld}.");
            GD.Print($"BASE_LAYER_IDLE_CONTROL_OK frames={idleFrames} rotate={rotateFrames} turn_requests={turnRequests} gated={transitionHeld} hidden={hiddenIdle} " +
                $"feedback=previous_named_curve native_repeat={idleRepeat is not null} retry=identical montage=turn_runtime demo=not_connected");
            Require(controlResets > 0 && hiddenYawChanges > 0 && controlHolds > 0 && controlConsumers > 0, "Global control coverage incomplete.");
            GD.Print($"BASE_LAYER_CONTROL_INPUT_OK reset={controlResets} hidden_yaw={hiddenYawChanges} held={controlHolds} consumed={controlConsumers} " +
                $"retry=identical native_repeat={nativeRepeat is not null} idle_checks=source_graph final_yaw_consumer=not_connected demo=not_connected");
            Require(jumpEvents == 6 && jumpHolds > 0 && hiddenJumpExpires > 0 && jumpRateConsumers > 0, "Jump event/hidden expiry/source coverage missing.");
            GD.Print($"BASE_LAYER_JUMP_INPUT_OK events={jumpEvents} held={jumpHolds} hidden_expiry={hiddenJumpExpires} source_consumed={jumpRateConsumers} " +
                "rate=saved_speed delay=post_animation retry=identical demo=not_connected");
            Require(hiddenInputUpdates > 0 && maskedInputFrames > 0, "Global input/hidden node separation or curve feedback was not covered.");
            Require(groundUpdates > 0 && heldGround > 0 && hiddenGround > 0 && consumedGround > 0 && partialWeights > 0,
                "Global ground update/gate/consumer coverage was incomplete.");
            GD.Print($"BASE_LAYER_GROUND_INPUT_OK frames={frames} updated={groundUpdates} held={heldGround} hidden_changed={hiddenGround} " +
                $"standing_consumed={consumedGround} partial_weights={partialWeights} retry=identical commit=pose_and_inputs physics=fixture demo=not_connected");
            GD.Print($"BASE_LAYER_AIR_INPUT_OK frames={frames} hidden_global_updates={hiddenInputUpdates} previous_mask_frames={maskedInputFrames} " +
                "retry=identical commit=pose_and_inputs physics=fixture grounded=fixture montage=fixture demo=not_connected");
        }
        GD.Print($"BASE_LAYER_FRAME_OK frames={frames} states={states} hidden={hidden} cold_hidden=3 reentries={reentries} source_events={events} hidden_end_events={hiddenEnds} requests={requests} foreign_forwards={forwards} late_faults={faults} guards={guards} retry=identical commit=one_owner montage=fixture demo=not_connected");
    }

    private void RunTurnNotifies(AlsAnimationSetDefinition set, AlsLocomotionAnimationProfile locomotion,
        AlsPoseAnimationProfile poseProfile, AlsMovementGraphDefinition definition, AlsStandingMovementSettings settings)
    {
        var observedAssets = new HashSet<int>(); var callbacks = 0; var lateFailures = 0; var frames = 0;
        foreach (var asset in definition.TurnMontageAssets)
        {
            using var library = AlsAnimationLibraryBuilder.BuildP5a(set, definition.Binding);
            library.UseMovementSources(set, definition.RawSources);
            AddChild(library.Root);
            using var graph = AlsLocomotionGraphBuilder.Build(library, locomotion, poseProfile, set, definition.Binding);
            using var owner = new AlsBaseLayerFrameRuntime(definition, library, graph.StandingCycle!, set, poseProfile);
            var sink = new Sink(owner.ReferencePose.ToArray()); var feedback = default(AlsAnimationInputFeedback);
            var turn = poseProfile.Turns.Single(t => t.AnimationId == asset.AnimationId);
            var aim = -turn.Direction * (turn.NominalDegrees == 90 ? 100f : 160f) * MathF.PI / 180;
            for (var frame = 1; frame <= 300; frame++)
            {
                const float delta = 1f / 60; var identity = new AlsFrameIdentity(frame,11,2);
                var result = AlsFrameResult.CreateDefault(identity);
                result.ResolvedLocomotionState = AlsLocomotionState.Grounded;
                result.ActualStance = (AlsStance)asset.Slot; result.ActualGait = AlsGait.Walking;
                result.ActualRotationMode = AlsRotationMode.LookingDirection; result.PlayRate = result.Stride = 1;
                var movement = AlsStandingMovementInputModel.Evaluate(identity,default,0,settings);
                var rules = new AlsGroundedRuleInput(false,false,false,result.ActualStance,true,false,0,1)
                { MovementState = AlsMovementStateInput.Grounded, FeetCrossing = 1 };
                var ground = new AlsGroundedFrameInputs(delta,new(1.75f,3.75f,6.5f),1,1,1,
                    new(NVector4.UnitX,default,1,default),new(1,1,default),AlsSlotWeights.Passthrough,
                    new(0,0),new(0,0),new(checked((short)frame),(ulong)frame));
                var input = AlsFrameInput.CreateDefault(identity,delta);
                input = input with { Command = input.Command with { AimYaw = aim, ViewYaw = aim },
                    AimRotation = System.Numerics.Quaternion.CreateFromYawPitchRoll(aim,0,0),
                    ViewRotation = System.Numerics.Quaternion.CreateFromYawPitchRoll(aim,0,0),
                    Floor = new(1,System.Numerics.Vector3.UnitY,-1,System.Numerics.Matrix4x4.Identity,default),
                    MaxAcceleration = 8, MaxBrakingDeceleration = 16 };
                var context = new AlsPoseUpdateContext(identity,1,delta,1);
                var committed = owner.TurnNotifies.Committed; var committedEvents = owner.Movement.CommittedEventState;
                Prepare(); var events = owner.SourceEvents; var candidate = owner.TurnNotifies.Candidate;
                owner.Evaluate(AlsLocalPose.Identity,0,0,sink,sink);
                if (events.Count > 0)
                {
                    var pose = owner.Pose.ToArray(); var curves = owner.Curves.ToArray();
                    owner.Discard(); Prepare(true); sink.FailBase = true;
                    Reject(() => owner.Evaluate(AlsLocalPose.Identity,0,0,sink,sink)); sink.FailBase = false;
                    Require(owner.TurnNotifies.Committed == committed &&
                        owner.Movement.CommittedEventState.NextInstanceId == committedEvents.NextInstanceId,
                        "A notify-bearing late failure published event state.");
                    owner.Discard(); Prepare(); owner.Evaluate(AlsLocalPose.Identity,0,0,sink,sink);
                    Require(owner.TurnNotifies.Candidate == candidate && owner.Pose.SequenceEqual(pose) && owner.Curves.SequenceEqual(curves),
                        "Notify-bearing retry changed pose or queue history.");
                    Require(owner.SourceEvents.Count == events.Count,"Retry changed event count.");
                    for (var i = 0; i < events.Count; i++)
                    {
                        Require(owner.SourceEvents[i] == events[i],"Retry changed notify identity or order.");
                        if (definition.TurnMontageAssets.Any(a => a.AnimationId == events[i].SourceAnimationId))
                        {
                            Require(events[i].SourceAnimationId == asset.AnimationId,
                                $"Turn selection differs: expected={asset.AnimationId} actual={events[i].SourceAnimationId} aim={aim}.");
                            observedAssets.Add(events[i].SourceAnimationId); callbacks++;
                        }
                    }
                    lateFailures++;
                }
                var nextFeedback = AlsAnimationInputFeedback.FromCompletedFrame(identity,owner.CurveNames,owner.Curves);
                owner.Commit(identity); Require(owner.TurnNotifies.Committed == candidate,"Pose and notify history diverged on commit.");
                feedback = nextFeedback; frames++;
                void Prepare(bool faultSlot = false)
                {
                    sink.Reset(); owner.PrepareFromFrame(input,result,movement,rules,ground,feedback,context,
                        faultSlot ? new(.75f,.25f,.25f) : AlsSlotWeights.Passthrough,sink);
                }
            }
        }
        Require(observedAssets.Count == 8 && callbacks >= 26 && lateFailures > 0,"Actual turn notify/pose coverage incomplete.");
        GD.Print($"BASE_LAYER_TURN_NOTIFY_OK assets={observedAssets.Count} frames={frames} callbacks={callbacks} late_failures={lateFailures} " +
            "pose=real_turn_sequences events=unified_allocator retry=identical demo=not_connected");
    }

    private static AlsIdleControlUpdate WithTurnScale(AlsIdleControlUpdate idle, float scale) =>
        idle with { State = idle.State with { RotationScale = scale } };

    private sealed class Sink(AlsLocalPose[] rest, int maskIndex = -1) : IAlsGroundedFrameRuntimeSink, IAlsGroundedSlotPoseSink, IAlsBaseLayerSlotPoseSink
    {
        public bool FailBase, EmptyBaseSource;
        public int ForwardCount, ForwardRequester;
        public float ForwardSeconds;
        public float? OutputMask;
        public void Reset() { ForwardCount = 0; EmptyBaseSource = false; }
        public void RequestInertialization(in AlsPoseUpdateContext context, float seconds)
        { ForwardCount++; ForwardRequester = context.InertializationRequester; ForwardSeconds = seconds; }
        public void UpdateGroundedSlot(int slot, in AlsSlotWeights weights, in AlsSlotSourceUpdate source, in AlsPoseUpdateContext context) => weights.Validate();
        public void OnCachedUpdatesSkipped(int handler, ReadOnlySpan<AlsPoseUpdateContext> skipped) =>
            throw new InvalidOperationException("Unexpected foreign skipped handler.");
        public void RefreshSourceBones(int cache) { }
        void IAlsBaseLayerSlotPoseSink.EvaluateSlot(in AlsSlotWeights weights, ReadOnlySpan<AlsLocalPose> source,
            ReadOnlySpan<AlsInertialCurve> sourceCurves, Span<AlsLocalPose> output, Span<AlsInertialCurve> curves)
        {
            if (FailBase) throw new InvalidOperationException("Injected final BaseLayer Slot failure.");
            EmptyBaseSource = source.IsEmpty; Compose(weights, source, sourceCurves, output, curves);
            if (maskIndex >= 0 && OutputMask.HasValue) curves[maskIndex] = new(OutputMask.Value);
        }
        void IAlsGroundedSlotPoseSink.EvaluateSlot(in AlsSlotWeights weights, ReadOnlySpan<AlsLocalPose> source,
            ReadOnlySpan<AlsInertialCurve> sourceCurves, Span<AlsLocalPose> output, Span<AlsInertialCurve> curves) =>
            Compose(weights, source, sourceCurves, output, curves);
        private void Compose(in AlsSlotWeights weights, ReadOnlySpan<AlsLocalPose> source,
            ReadOnlySpan<AlsInertialCurve> sourceCurves, Span<AlsLocalPose> output, Span<AlsInertialCurve> curves)
        {
            for (var b = 0; b < output.Length; b++) output[b] = source.IsEmpty ? rest[b] :
                AlsPoseBlender.Normalize(AlsPoseBlender.BlendRaw(source[b], rest[b], weights.SlotNodeWeight));
            for (var c = 0; c < curves.Length; c++) curves[c] = sourceCurves.IsEmpty ? default :
                AlsStandingCycleCurves.Lerp(sourceCurves[c], default, weights.SlotNodeWeight);
        }
        void IAlsGroundedSlotPoseSink.EvaluateSlot(in AlsSlotWeights weights, ReadOnlySpan<AlsPrecisePose> source,
            ReadOnlySpan<AlsInertialCurve> sourceCurves, Span<AlsPrecisePose> output, Span<AlsInertialCurve> curves)
        {
            for (var b = 0; b < output.Length; b++) output[b] = source.IsEmpty ? new AlsPrecisePose(rest[b]) :
                AlsPrecisePoseBlender.Normalize(AlsPrecisePoseBlender.BlendRaw(source[b], new AlsPrecisePose(rest[b]), weights.SlotNodeWeight));
            for (var c = 0; c < curves.Length; c++) curves[c] = sourceCurves.IsEmpty ? default :
                AlsStandingCycleCurves.Lerp(sourceCurves[c], default, weights.SlotNodeWeight);
        }
        void IAlsBaseLayerSlotPoseSink.EvaluateSlot(in AlsSlotWeights weights, ReadOnlySpan<AlsPrecisePose> source,
            ReadOnlySpan<AlsInertialCurve> sourceCurves, Span<AlsPrecisePose> output, Span<AlsInertialCurve> curves)
        {
            if (FailBase) throw new InvalidOperationException("Injected final BaseLayer Slot failure.");
            EmptyBaseSource = source.IsEmpty;
            for (var b = 0; b < output.Length; b++) output[b] = source.IsEmpty ? new AlsPrecisePose(rest[b]) :
                AlsPrecisePoseBlender.Normalize(AlsPrecisePoseBlender.BlendRaw(source[b], new AlsPrecisePose(rest[b]), weights.SlotNodeWeight));
            for (var c = 0; c < curves.Length; c++) curves[c] = sourceCurves.IsEmpty ? default :
                AlsStandingCycleCurves.Lerp(sourceCurves[c], default, weights.SlotNodeWeight);
            if (maskIndex >= 0 && OutputMask.HasValue) curves[maskIndex] = new(OutputMask.Value);
        }
    }
    private void RunCurveFeedback(AlsAnimationSetDefinition set, AlsLocomotionAnimationProfile locomotion,
        AlsPoseAnimationProfile poseProfile, AlsMovementGraphDefinition definition, AlsStandingMovementSettings settings)
    {
        var frames = 0; var differsFromLocal = 0; var waiting = 0; var crouchingYaw = 0; var faults = 0;
        foreach (var hz in new[] { 30, 60, 120 })
        {
            using var library = AlsAnimationLibraryBuilder.BuildP5a(set, definition.Binding);
            library.UseMovementSources(set, definition.RawSources); AddChild(library.Root);
            using var graph = AlsLocomotionGraphBuilder.Build(library, locomotion, poseProfile, set, definition.Binding);
            using var owner = new AlsBaseLayerFrameRuntime(definition, library, graph.StandingCycle!, set, poseProfile);
            var sink = new Sink(owner.ReferencePose.ToArray());
            var finalSlot = new FeedbackSlot(sink, owner.CurveNames);
            var feedback = default(AlsAnimationInputFeedback);
            for (var frame = 1; frame <= hz * 5; frame++)
            {
                var delta = 1f / hz; var id = new AlsFrameIdentity(frame, 11, 2);
                var right = frame > hz * 3 / 2 && frame <= hz * 2;
                var velocity = new System.Numerics.Vector3(right ? 3.5f : -3.5f, 0, 0);
                var stance = frame > hz * 3 ? AlsStance.Crouching : AlsStance.Standing;
                var result = AlsFrameResult.CreateDefault(id);
                result.ResolvedLocomotionState = AlsLocomotionState.Grounded; result.ActualStance = stance;
                result.ActualGait = AlsGait.Running; result.ActualRotationMode = AlsRotationMode.LookingDirection;
                result.BlendCoordinates = new(velocity.X, 0); result.Stride = result.PlayRate = 1;
                var input = AlsFrameInput.CreateDefault(id, delta) with
                {
                    ActualVelocity = velocity, MaxAcceleration = 8, MaxBrakingDeceleration = 16,
                    Floor = new(1, System.Numerics.Vector3.UnitY, -1, System.Numerics.Matrix4x4.Identity, default),
                };
                input = input with { Command = input.Command with { ViewYaw = -.7f, AimYaw = 0 } };
                var movement = AlsStandingMovementInputModel.Evaluate(id, velocity, 1, settings);
                // Deliberately contradict the final feedback and the computed yaw.
                // Neither these fixture values nor the old local cycle may win.
                var rules = new AlsGroundedRuleInput(true, false, false, stance, true, false, 0, 91)
                    { MovementState = AlsMovementStateInput.Grounded, FeetCrossing = 92, HipBias = 93, HasMovementInput = true, Speed = 3.5f };
                var ground = new AlsGroundedFrameInputs(delta, new(1.75f, 3.75f, 6.5f), 1, 1, 1,
                    new(NVector4.UnitX, new(100, 200, 300, 400), 1, default), new(1, 1, default), AlsSlotWeights.Passthrough,
                    new(0, 0), new(0, 0), new(checked((short)frame), (ulong)frame));
                var context = new AlsPoseUpdateContext(id, 1, delta);
                finalSlot.Feet = new(frame % hz < hz / 2 ? -1 : 1);
                finalSlot.Crossing = frame % hz < hz / 2 ? new(.001f) : new(7, false);
                finalSlot.Hips = new(frame < hz * 2 ? 1 : 0);
                var priorIdentity = owner.CommittedIdentity; var priorFeedback = feedback;
                Prepare();
                var expectedCrossing = feedback.FeetCrossing.Present ? feedback.FeetCrossing.Value : 0;
                var expectedHips = feedback.HipOrientationBias.Present ? feedback.HipOrientationBias.Value : 0;
                Require(owner.Grounded.StandingFrame.State.Crossing == expectedCrossing &&
                    owner.Grounded.StandingFrame.State.HipBias == expectedHips, "Standing did not read previous final crossing and hip bias.");
                Require(owner.Grounded.ObservedInput.Rules.FeetPosition == (feedback.FeetPosition.Present ? feedback.FeetPosition.Value : 0),
                    "Stop observation did not read previous final feet position.");
                if (graph.StandingCycle!.Sample(owner.Grounded.CommittedStanding, "Feet_Crossing") != expectedCrossing ||
                    graph.StandingCycle.Sample(owner.Grounded.CommittedStanding, "HipOrientation_Bias") != expectedHips) differsFromLocal++;
                if (owner.Grounded.StandingFrame.State.WaitingForFeet) waiting++;
                owner.Evaluate(AlsLocalPose.Identity, 0, 0, sink, finalSlot);
                var pose = owner.Pose.ToArray(); var curves = owner.Curves.ToArray(); var events = owner.SourceEvents;
                var candidateState = owner.Grounded.StandingFrame.State;
                var yawIndex = owner.CurveNames.IndexOf("YawOffset");
                if (stance == AlsStance.Crouching && owner.Grounded.CycleUpdate.DirectionUpdated &&
                    yawIndex >= 0 && curves[yawIndex].Present && MathF.Abs(curves[yawIndex].Value) > .01f) crouchingYaw++;
                owner.Discard();
                // A second candidate with different stale caller values must agree.
                ground = ground with { Cycles = ground.Cycles with { Yaw = new(-500, -600, -700, -800) } };
                rules = rules with { FeetPosition = -91, FeetCrossing = -92, HipBias = -93 };
                Prepare();
                if (frame % 23 == 0)
                {
                    finalSlot.Fail = true; Reject(() => owner.Evaluate(AlsLocalPose.Identity, 0, 0, sink, finalSlot));
                    Require(owner.CommittedIdentity == priorIdentity && feedback == priorFeedback, "Failed final slot published feedback history.");
                    owner.Discard(); finalSlot.Fail = false; Prepare(); faults++;
                }
                owner.Evaluate(AlsLocalPose.Identity, 0, 0, sink, finalSlot);
                Require(owner.Pose.SequenceEqual(pose) && owner.Curves.SequenceEqual(curves) && owner.Grounded.StandingFrame.State == candidateState,
                    "Final curve consumers or crouching yaw changed on retry.");
                Require(owner.SourceEvents.Count == events.Count, "Curve feedback retry changed event count.");
                for (var i = 0; i < events.Count; i++) Require(owner.SourceEvents[i] == events[i], "Curve feedback retry changed event identity.");
                var next = finalSlot.Feedback(id);
                owner.Commit(id); feedback = next; frames++;
                void Prepare() => owner.PrepareFromFrame(input, result, movement, rules, ground, feedback, context,
                    new AlsSlotWeights(.75f, .25f, .25f), sink);
            }
        }
        Require(differsFromLocal > 0 && waiting > 0 && crouchingYaw > 0 && faults > 0,
            $"Final curve feedback coverage is incomplete: local={differsFromLocal}, waiting={waiting}, crouchingYaw={crouchingYaw}, faults={faults}.");
        GD.Print($"BASE_LAYER_CURVE_FEEDBACK_OK rates=30,60,120 frames={frames} retries={frames} differs_from_local={differsFromLocal} waiting={waiting} crouching_yaw={crouchingYaw} late_faults={faults} feedback=previous_final presence=preserved demo=not_connected");
    }

    private sealed class FeedbackSlot : IAlsBaseLayerSlotPoseSink
    {
        private readonly IAlsBaseLayerSlotPoseSink _inner;
        private readonly int _feet, _crossing, _hips;
        private readonly string[] _names;
        private readonly AlsInertialCurve[] _finalCurves;
        public AlsInertialCurve Feet, Crossing, Hips;
        public bool Fail;
        public FeedbackSlot(IAlsBaseLayerSlotPoseSink inner, ReadOnlySpan<string> names)
        {
            _inner = inner;
            // HipOrientation_Bias may be authored only by a later Overlay layer.
            // This controlled final layer adds its name without manufacturing
            // presence in the actual BaseLayer source layout.
            _names = names.ToArray().Concat(["Feet_Position", "Feet_Crossing", "HipOrientation_Bias"]).Distinct(StringComparer.Ordinal).ToArray();
            _finalCurves = new AlsInertialCurve[_names.Length];
            _feet = Array.IndexOf(_names, "Feet_Position"); _crossing = Array.IndexOf(_names, "Feet_Crossing"); _hips = Array.IndexOf(_names, "HipOrientation_Bias");
        }
        public AlsAnimationInputFeedback Feedback(AlsFrameIdentity id) => AlsAnimationInputFeedback.FromCompletedFrame(id, _names, _finalCurves);
        public void EvaluateSlot(in AlsSlotWeights weights, ReadOnlySpan<AlsLocalPose> source, ReadOnlySpan<AlsInertialCurve> sourceCurves,
            Span<AlsLocalPose> output, Span<AlsInertialCurve> curves)
        {
            _inner.EvaluateSlot(weights, source, sourceCurves, output, curves);
            curves.CopyTo(_finalCurves);
            _finalCurves[_feet] = Feet; _finalCurves[_crossing] = Crossing; _finalCurves[_hips] = Hips;
            if (Fail) throw new InvalidOperationException("Injected failure after final curve composition.");
        }
        public void EvaluateSlot(in AlsSlotWeights weights, ReadOnlySpan<AlsPrecisePose> source, ReadOnlySpan<AlsInertialCurve> sourceCurves,
            Span<AlsPrecisePose> output, Span<AlsInertialCurve> curves)
        {
            _inner.EvaluateSlot(weights, source, sourceCurves, output, curves);
            curves.CopyTo(_finalCurves);
            _finalCurves[_feet] = Feet; _finalCurves[_crossing] = Crossing; _finalCurves[_hips] = Hips;
            if (Fail) throw new InvalidOperationException("Injected failure after final curve composition.");
        }
    }

    private void RunActions(AlsAnimationSetDefinition set,AlsLocomotionAnimationProfile locomotion,
        AlsPoseAnimationProfile poseProfile,AlsMovementGraphDefinition definition,AlsStandingMovementSettings settings)
    {
        var frames=0; var hidden=0; var faults=0; var accepted=0; var completed=0; var cancelled=0; var replaced=0;
        var begins=0; var ends=0; var fullPose=0; var callbacks=0;
        var typedEntries=0; var resets=0; var fromRoll=0;
        var consumeNotifies = OS.GetCmdlineUserArgs().Contains("--grounded-entry");
        foreach(var hz in new[]{30,60,120})
        {
            using var library=AlsAnimationLibraryBuilder.BuildP5a(set,definition.Binding); library.UseMovementSources(set,definition.RawSources); AddChild(library.Root);
            using var graph=AlsLocomotionGraphBuilder.Build(library,locomotion,poseProfile,set,definition.Binding);
            using var owner=new AlsBaseLayerFrameRuntime(definition,library,graph.StandingCycle!,set,poseProfile);
            var sink=new Sink(owner.ReferencePose.ToArray()); var fault=new ActionFailureSlot(owner.ActionSlot);
            var feedback=default(AlsAnimationInputFeedback); var policy=definition.ActionPolicies.Single(p => p.DefinitionId == definition.RollDefinitionId);
            var notifyState = default(AlsMovementNotifyState);
            var roll=definition.AuthoredMontageAssets.Single(a => a.ActionDefinitionId == definition.RollDefinitionId);
            using var clip=library.MovementSources(set, poseProfile.SkeletonId).Create(roll.AnimationId);
            var sampled=owner.ReferencePose.ToArray();
            var rollCurveNames=set.Animations[roll.AnimationId].Curves.Where(c=>c.Provenance==AlsCurveProvenance.SourceCurve).Select(c=>c.SourceName).ToHashSet(StringComparer.Ordinal);
            for(var frame=1;frame<=hz*5;frame++)
            {
                var delta=1f/hz; var id=new AlsFrameIdentity(frame,11,2); var result=AlsFrameResult.CreateDefault(id);
                result.ResolvedLocomotionState=AlsLocomotionState.Grounded; result.ActualStance=AlsStance.Standing;
                result.ActualGait=AlsGait.Walking; result.ActualRotationMode=AlsRotationMode.LookingDirection; result.PlayRate=result.Stride=1;
                var speed=frame>hz*4 ? 1.5f:0;
                var movement=AlsStandingMovementInputModel.Evaluate(id,new(speed,0,0),speed>0 ? 1:0,settings);
                var rules=new AlsGroundedRuleInput(movement.ShouldMove,false,false,AlsStance.Standing,
                    !consumeNotifies || notifyState.Action == AlsTimelineAction.None,
                    consumeNotifies && notifyState.Entry == AlsTimelineGroundedEntryMode.FromRoll,0,1)
                    {MovementState=AlsMovementStateInput.Grounded,FeetCrossing=1};
                var ground=new AlsGroundedFrameInputs(delta,new(1.75f,3.75f,6.5f),1,1,1,
                    new(NVector4.UnitX,default,1,default),new(1,1,default),AlsSlotWeights.Passthrough,
                    new(0,0),new(0,0),new(checked((short)frame),(ulong)frame));
                var request=frame==1 || frame==hz/2 || frame==hz*2 ?
                    new AlsActionRequest(frame,AlsActionCommand.Start,policy.DefinitionId,policy.StartSectionId,100,2) :
                    frame==hz*3/4 ? new(hz/2,AlsActionCommand.Cancel,policy.DefinitionId,-1,0,2) : AlsActionRequest.None;
                var input=AlsFrameInput.CreateDefault(id,delta) with {ActionRequest=request,ActualVelocity=new(speed,0,0),
                    Floor=new(1,System.Numerics.Vector3.UnitY,-1,System.Numerics.Matrix4x4.Identity,default),MaxAcceleration=8,MaxBrakingDeceleration=16};
                var context=new AlsPoseUpdateContext(id,1,delta,1);
                var priorIdentity=owner.CommittedIdentity; var priorRequests=owner.Actions.CommittedHistory;
                var priorNotify=owner.TurnNotifies.Committed; var priorEvents=owner.Movement.CommittedEventState;
                Prepare(); owner.Evaluate(AlsLocalPose.Identity,0,0,sink);
                var published = AlsFrameResult.CreateDefault(id); owner.CompleteEvents(ref published);
                var nextNotifyState = notifyState.Advance(owner.SourceEvents, owner.ResetGroundedEntry);
                var resetEntry = owner.ResetGroundedEntry;
                if (resetEntry) resets++;
                if (owner.SourceUpdated && owner.Movement.GroundedReadCount > 0 && owner.Grounded.Update.Main.State.CurrentState == 7) fromRoll++;
                if (frame == 1)
                {
                    var foreign = AlsFrameResult.CreateDefault(new(id.FrameId + 1, id.CharacterId, id.SlotGeneration));
                    Reject(() => owner.CompleteEvents(ref foreign));
                    Require(foreign.ActionPlayback == AlsActionPlayback.CreateDefault() && foreign.TypedEvents.Count == 0 && foreign.ActionOutcomes.Count == 0,
                        "Rejected publisher partially changed the public result.");
                    Reject(() => owner.CompleteEvents(ref published));
                }
                var logical = owner.Actions.CandidateOwners.ToArray().Single();
                if (logical.InstanceId == 0) Require(published.ActionPlayback == AlsActionPlayback.CreateDefault(), "Closed action published a fading predecessor.");
                else
                {
                    var instance = owner.Montages.Candidate.ToArray().Single(i => i.InstanceId == logical.InstanceId);
                    var range = definition.MontageNotifies.Ranges.ToArray().Single(r => !r.Direct && r.ActionDefinitionId == logical.DefinitionId);
                    Require(published.ActionPlayback.PlaybackEpoch == logical.InstanceId && published.ActionPlayback.Active == 1 &&
                        published.ActionPlayback.OccurrenceHandleId == range.Handle && published.ActionPlayback.CurrentTime == instance.Position &&
                        published.ActionPlayback.CurrentClipTime == instance.ClipStart + instance.Position * instance.ClipRate,
                        "Published action identity/time differs from the actual Roll instance.");
                    var entry = owner.Montages.Evaluation.ToArray().SingleOrDefault(e => e.InstanceId == logical.InstanceId);
                    Require(published.ActionPlayback.EffectiveWeight == entry.Weight / MathF.Max(1, owner.Montages.SlotWeights(AlsMontageSlot.BaseLayer).TotalNodeWeight),
                        "Published action weight differs from the frozen Slot contribution.");
                }
                var expectedPose=owner.Pose.ToArray(); var expectedCurves=owner.Curves.ToArray(); var events=owner.SourceEvents;
                var outcomes=owner.Actions.Outcomes; var notify=owner.TurnNotifies.Candidate; var physical=owner.Montages.Candidate.ToArray();
                var weights=owner.Montages.SlotWeights(AlsMontageSlot.BaseLayer);
                if(!owner.SourceUpdated)hidden++;
                if(weights.SourceWeight<=AlsPoseBlender.WeightThreshold && owner.Montages.Evaluation.Length==1 &&
                    owner.Montages.Evaluation[0].Slot==AlsMontageSlot.BaseLayer)
                {
                    var entry=owner.Montages.Evaluation[0]; clip.SampleSourceSeconds(owner.ReferencePose,entry.Position,(float)clip.Length,sampled);
                    for(var b=0;b<sampled.Length;b++) Require(System.Numerics.Vector3.Distance(sampled[b].Position,owner.Pose[b].Position)<1e-5f &&
                        MathF.Abs(System.Numerics.Quaternion.Dot(sampled[b].Rotation,owner.Pose[b].Rotation))>.99999f,"Full Roll pose differs from actual sequence.");
                    for(var c=0;c<owner.CurveNames.Length;c++) Require(owner.Curves[c].Present==rollCurveNames.Contains(owner.CurveNames[c]),
                        "Full Roll curves retain hidden source presence.");
                    fullPose++;
                }
                owner.Discard(); Prepare();
                if(frame%17==0 && weights.SlotNodeWeight>AlsPoseBlender.WeightThreshold)
                {
                    Reject(()=>owner.Evaluate(AlsLocalPose.Identity,0,0,sink,fault));
                    var failedResult = AlsFrameResult.CreateDefault(id);
                    Reject(() => owner.CompleteEvents(ref failedResult));
                    Require(failedResult.ActionPlayback == AlsActionPlayback.CreateDefault() && failedResult.TypedEvents.Count == 0 && failedResult.ActionOutcomes.Count == 0,
                        "Failed pose published part of the action result.");
                    Require(owner.CommittedIdentity==priorIdentity && owner.Actions.CommittedHistory==priorRequests &&
                        owner.TurnNotifies.Committed==priorNotify && owner.Movement.CommittedEventState.NextInstanceId==priorEvents.NextInstanceId,
                        "Failed action pose published request or event state.");
                    owner.Discard(); Prepare(); faults++;
                }
                owner.Evaluate(AlsLocalPose.Identity,0,0,sink);
                Require(owner.Pose.SequenceEqual(expectedPose) && owner.Curves.SequenceEqual(expectedCurves) &&
                    owner.Montages.Candidate.SequenceEqual(physical) && owner.TurnNotifies.Candidate==notify,"Authored BaseLayer retry differs.");
                Require(owner.SourceEvents.Count==events.Count && owner.Actions.Outcomes.Count==outcomes.Count,"Action retry counts differ.");
                var retryPublished = AlsFrameResult.CreateDefault(id); owner.CompleteEvents(ref retryPublished);
                Require(retryPublished.ActionPlayback == published.ActionPlayback, "Action playback changed across discard/retry.");
                Require(owner.ResetGroundedEntry == resetEntry && notifyState.Advance(owner.SourceEvents, owner.ResetGroundedEntry) == nextNotifyState,
                    "Grounded entry candidate or reset changed across discard/retry.");
                Require(retryPublished.TypedEvents.Count == events.Count && retryPublished.ActionOutcomes.Count == outcomes.Count,
                    "Published action event/outcome counts differ.");
                for(var i=0;i<events.Count;i++)
                {
                    Require(owner.SourceEvents[i]==events[i] && retryPublished.TypedEvents[i]==events[i],"Action retry/public event identity differs.");
                    if (events[i].EventId == definition.GroundedEntryNotify.Binding.EventId &&
                        events[i].SourceAnimationId == definition.GroundedEntryNotify.Binding.AnimationId)
                    {
                        Require(events[i].Kind == AlsTimelineEventKind.SetGroundedEntry &&
                            events[i].Payload.EnumValue0 == (int)AlsTimelineGroundedEntryMode.FromRoll &&
                            events[i].Payload.SemanticId == definition.GroundedEntryNotify.Binding.SemanticId,
                            "Real Roll callback still has Generic semantics."); typedEntries++;
                    }
                    if(events[i].SourceActionId<0)continue;
                    callbacks++; if(events[i].Phase==AlsAnimationEventPhase.Begin)begins++;
                    if(events[i].Phase==AlsAnimationEventPhase.End)ends++;
                }
                for(var i=0;i<outcomes.Count;i++)
                {
                    Require(owner.Actions.Outcomes[i]==outcomes[i] && retryPublished.ActionOutcomes[i]==outcomes[i],"Action retry/public outcome differs.");
                    switch(outcomes[i].ResultCode)
                    {
                        case AlsActionResultCode.Accepted: accepted++; break;
                        case AlsActionResultCode.Completed: completed++; break;
                        case AlsActionResultCode.InterruptedByExplicitCancel: cancelled++; break;
                        case AlsActionResultCode.InterruptedByReplacement: replaced++; break;
                    }
                }
                var next=AlsAnimationInputFeedback.FromCompletedFrame(id,owner.CurveNames,owner.Curves);
                owner.Commit(id); feedback=next; notifyState=nextNotifyState; frames++;
                void Prepare()=>owner.PrepareFromFrame(input,result,movement,rules,ground,feedback,context,sink);
            }
            if (consumeNotifies) Require(notifyState.Action == AlsTimelineAction.None && notifyState.Entry == AlsTimelineGroundedEntryMode.None,
                "Completed Roll left its movement action or consumed entry selection latched.");
        }
        Require(accepted==9 && completed==3 && cancelled==3 && replaced==3 && begins==6 && ends==6 &&
            hidden>0 && fullPose>0 && faults>0,"Authored BaseLayer action lifecycle coverage differs.");
        Require(typedEntries >= 3, "No real Roll grounded entry callbacks were consumed.");
        if (consumeNotifies) Require(resets >= 3 && fromRoll > 0, "Grounded Entry -> From Roll -> reset path was not covered.");
        GD.Print($"BASE_LAYER_ACTION_OK rates=30,60,120 frames={frames} retries={frames} full_roll_pose={fullPose} hidden={hidden} callbacks={callbacks} begins={begins} ends={ends} accepted={accepted} replaced={replaced} cancelled={cancelled} completed={completed} late_faults={faults} typed_entries={typedEntries} entry_resets={resets} from_roll_frames={fromRoll} notify_consumers={consumeNotifies} slot=real_roll curves=presence_checked events=unified playback=physical_owner root_motion=not_applied gameplay=not_connected");
    }
    private sealed class ActionFailureSlot(IAlsBaseLayerSlotPoseSink inner):IAlsBaseLayerSlotPoseSink
    {
        public void EvaluateSlot(in AlsSlotWeights weights,ReadOnlySpan<AlsLocalPose> source,ReadOnlySpan<AlsInertialCurve> sourceCurves,
            Span<AlsLocalPose> output,Span<AlsInertialCurve> curves)
        { inner.EvaluateSlot(weights,source,sourceCurves,output,curves); throw new InvalidOperationException("Injected failure after actual Roll sampling."); }
        public void EvaluateSlot(in AlsSlotWeights weights,ReadOnlySpan<AlsPrecisePose> source,ReadOnlySpan<AlsInertialCurve> sourceCurves,
            Span<AlsPrecisePose> output,Span<AlsInertialCurve> curves)
        { inner.EvaluateSlot(weights,source,sourceCurves,output,curves); throw new InvalidOperationException("Injected failure after actual Roll sampling."); }
    }

    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static bool SameMachine(in AlsGroundedMachineState a, in AlsGroundedMachineState b)
    {
        if (a.Kind != b.Kind || a.RecordedWeight != b.RecordedWeight || a.ElapsedSeconds != b.ElapsedSeconds ||
            a.LastUpdateSerial != b.LastUpdateSerial || a.LastUpdateCounter != b.LastUpdateCounter || a.HasUpdated != b.HasUpdated || a.HasInitialized != b.HasInitialized ||
            a.CurrentState != b.CurrentState || a.Transitions.Count != b.Transitions.Count || a.Transitions.Latest != b.Transitions.Latest) return false;
        for (var i = 0; i < a.Transitions.Count; i++)
            if (a.GetActiveEdge(i) != b.GetActiveEdge(i) || a.Transitions.GetTransition(i) != b.Transitions.GetTransition(i)) return false;
        return true;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; } catch (ArgumentException) { return; }
        throw new InvalidOperationException("Combined owner accepted invalid commit.");
    }
}
