using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace GodotAls.Animation;

public partial class MainMovementRuntimeSmoke : Node
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
        var turns = AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"), set, locomotion);
        var json = Read("v4_main_movement_graph.json"); var cache = Read("v4_pose_cache_graph.json");
        var sources = AlsLocomotionSourceCompiler.CompileWithMovement(json, set, locomotion.SkeletonId);
        var binding = AlsLocomotionGraphBuilder.CompileSourceBindings(set, locomotion, turns, sources);
        var machines = AlsGroundedMachineCompiler.CompileMovement(json);
        var main = AlsMainGroundedCachedGraphCompiler.Compile(json, cache, Read("v4_locomotion_detail_graph.json"), sources, set);
        var cycles = AlsCrouchingCycleCompiler.Compile(json, cache, Read("v4_locomotion_curves.json"), Read("v4_lean_sampling.json"), sources, set);
        var dependencies = AlsGroundedPoseDependencyCompiler.Compile(Read("v4_grounded_dependencies.json"), set.Skeletons[locomotion.SkeletonId]);
        var air = AlsAirPoseCompiler.Compile(json, cache, Read("v4_falling_lean_sampling.json"), sources, set);
        var landing = AlsLandingPoseCompiler.Compile(json, cache, sources, set);
        var baseProfile = AlsBaseLayerCompiler.Compile(json, cache);
        var withBaseLayer = OS.GetCmdlineUserArgs().Contains("--base-layer");
        var withMovementCurves = OS.GetCmdlineUserArgs().Contains("--refactored-movement-curves");
        var withStateCurves = OS.GetCmdlineUserArgs().Contains("--refactored-state-curves");
        Require(!withStateCurves || withMovementCurves, "State curve test requires the movement cache producers.");
        var stateDefinitions = withStateCurves ? AlsRefactoredPoseCurveCompiler.Compile(Read("refactored_pose_curve_inputs.json")) : null;
        var stateCurveFrames = 0; var stateCurveBlends = 0; var stateInputGuards = 0; var preciseStateFrames = 0;
        var movingCurveFrames = 0; var crouchingCurveFrames = 0; var fractionalCurveFrames = 0; var stoppedCurveFrames = 0; var absentAirCurveFrames = 0;
        if (withBaseLayer) BaseLayerPoseChecks.Run(baseProfile);
        var movementSettings = AlsLocomotionInputCompiler.Compile(Read("v4_locomotion_inputs.json")).Movement;
        var frames = 0; var events = 0; var machineEvents = 0; var requests = 0; var mixed = 0; var movingPoses = 0;
        var exits = 0; var faults = 0; var guards = 0; var seen = 0; var frozen = 0; var lingering = 0;
        var inertialFrames = 0; var finalSlotFrames = 0; var finalFaults = 0;
        foreach (var hz in new[] { 30, 60, 120 }) foreach (var foot in new[] { -1f, 1f })
        {
            using var library = AlsAnimationLibraryBuilder.Build(set, locomotion, turns, sourceProfile: sources); AddChild(library.Root);
            using var standing = AlsLocomotionGraphBuilder.Build(library, locomotion, turns, set, binding);
            using var ground = new AlsGroundedFrameRuntime(library, standing.StandingCycle!, set, sources, main, cycles, dependencies, turns);
            using var runtime = new AlsMainMovementFrameRuntime(library, set, sources, binding, machines.Movement!, ground, air, landing, dependencies,
                refactoredCurves: stateDefinitions);
            using var landingProbe = new AlsLandingPoseGraph(library, set, sources, landing);
            var pose = new AlsLocalPose[runtime.ReferencePose.Length]; var retry = new AlsLocalPose[pose.Length]; var probe = new AlsLocalPose[pose.Length];
            var curves = new AlsInertialCurve[runtime.CurveNames.Length]; var retryCurves = new AlsInertialCurve[curves.Length];
            var preciseProbe = new AlsPrecisePose[pose.Length];
            var sink = new Sink(runtime.ReferencePose.ToArray());
            var tail = withBaseLayer ? new AlsBaseLayerPoseRuntime(baseProfile, pose.Length, curves.Length) : null;
            var finalPose = new AlsLocalPose[pose.Length]; var finalRetry = new AlsLocalPose[pose.Length];
            var finalCurves = new AlsInertialCurve[curves.Length]; var finalRetryCurves = new AlsInertialCurve[curves.Length];
            sink.Tail = tail; sink.Requester = tail == null ? 999 : baseProfile.InertializationNodeIndex;
            var priorState = 0; var baseClf = 0f; var localSeen = 0; var landExit = false; var moveExit = false;
            var curveClf = runtime.CurveNames.IndexOf("BasePose_CLF"); var inAir = runtime.CurveNames.IndexOf("Weight_InAir");
            var leftIk = runtime.CurveNames.IndexOf("Enable_FootIK_L"); var rightIk = runtime.CurveNames.IndexOf("Enable_FootIK_R");
            var poseMoving = runtime.CurveNames.IndexOf("PoseMoving");
            Require(withMovementCurves == (poseMoving >= 0), "Movement curve feature/layout differ.");
            for (var frame = 1; frame <= hz * 8; frame++)
            {
                var seconds = frame / (float)hz;
                var coldAir = foot < 0;
                var jumping = (coldAir || frame > hz) && seconds < 2.4f; var falling = seconds >= 4 && seconds < 5.3f;
                var airborne = jumping || falling; var idleLanding = seconds >= 2.4f && seconds < 4;
                var speed = idleLanding || withMovementCurves && seconds is >= 6 and < 6.6f ? 0f : 3.5f; var delta = 1f / hz;
                var result = AlsFrameResult.CreateDefault(new(frame, 11, 2));
                result.ResolvedLocomotionState = airborne ? AlsLocomotionState.InAir : AlsLocomotionState.Grounded;
                result.ActualStance = seconds > 6.8f ? AlsStance.Crouching : AlsStance.Standing;
                result.ActualGait = AlsGait.Running; result.ActualRotationMode = AlsRotationMode.LookingDirection;
                result.BlendCoordinates = new(0, speed); result.PlayRate = 1; result.Stride = 1;
                var move = AlsStandingMovementInputModel.Evaluate(result.Identity, new(speed, 0, 0), speed > 0 ? 1 : 0, movementSettings);
                var rules = new AlsGroundedRuleInput(move.ShouldMove, false, false, result.ActualStance, true, false, baseClf, foot)
                {
                    MovementState = airborne ? AlsMovementStateInput.InAir : AlsMovementStateInput.Grounded,
                    Jumped = frame == (coldAir ? 1 : hz + 1), HasMovementInput = speed > 0, Speed = speed,
                    MovementDirection = AlsMovementDirection.Forward, FeetCrossing = 1,
                };
                var slot = seconds >= 7 && seconds < 7.5f ? new AlsSlotWeights(.7f, .3f, .3f) : AlsSlotWeights.Passthrough;
                var finalSlot = seconds >= 6.8f && seconds < 7.2f ? new AlsSlotWeights(.75f, .25f, .25f) : AlsSlotWeights.Passthrough;
                var groundInputs = new AlsGroundedFrameInputs(delta, new(1.75f, 3.75f, 6.5f), .75f, 1, 1,
                    new(NVector4.UnitX, default, 1, new NVector2(.2f, -.3f)), new(1, 1, default), slot,
                    new(0, 0), new(0, 0), new(checked((short)frame), (ulong)frame));
                var fallSpeed = jumping ? 3 - (seconds - 1) * 8 : foot < 0 ? -4 : -12;
                var prediction = falling && seconds > 5.1f ? 1 : 0;
                var inputs = new AlsMainMovementInputs(groundInputs, fallSpeed, prediction, new(.3f, -.2f), foot < 0 ? .8f : 1.4f, speed);
                var refactoredPrediction = .2f + .6f * ((frame % 7) / 6f);
                if (withStateCurves) inputs = inputs with { RefactoredCurves = new(result.Identity, refactoredPrediction) };
                var context = new AlsPoseUpdateContext(result.Identity, frame % 31 == 0 ? 0 : .8f, delta, .4f).WithInertialization(999, true);
                if (frame % 19 == 0) context = context.AsInactive();
                var committed = runtime.CommittedSources;
                if (withStateCurves && frame == 1)
                {
                    AlsRefactoredPoseCurveInputs?[] invalid = [null,
                        new(new(result.Identity.FrameId, result.Identity.CharacterId, 99), refactoredPrediction), new(result.Identity, float.NaN)];
                    foreach (var invalidInput in invalid)
                    {
                        try { runtime.Prepare(result, move, rules, inputs with { RefactoredCurves = invalidInput }, context, sink);
                            throw new InvalidOperationException("Missing/foreign Refactored prediction was accepted."); }
                        catch (ArgumentException) { stateInputGuards++; }
                    }
                    Require(StandingCycleSmoke.SameSync(committed, runtime.CommittedSources), "Rejected curve input changed source history.");
                }
                if (frame == hz * 7 + hz / 4)
                {
                    var rejected = false;
                    try { Prepare(); sink.FailSlot = true; runtime.EvaluateRaw(retry, retryCurves, sink); }
                    catch (InvalidOperationException failure) when (failure.Message == "Injected Main Movement Slot failure.")
                    { rejected = runtime.IsFaulted; }
                    finally { sink.FailSlot = false; runtime.Discard(); tail?.Discard(); }
                    Require(rejected && StandingCycleSmoke.SameSync(committed, runtime.CommittedSources), "Late Main Movement pose failure leaked source state."); faults++;
                }
                Prepare();
                if (frame == 1)
                {
                    var rejected = false;
                    try { runtime.Commit(result.Identity); } catch (InvalidOperationException) { rejected = true; }
                    Require(rejected, "Main Movement committed before pose evaluation."); guards++;
                    if (coldAir)
                    {
                        Require(runtime.GroundedUpdate.State.Main.HasInitialized && !runtime.GroundedUpdate.State.Main.HasUpdated &&
                            runtime.Update.State.CurrentState == 2, "Cold air start skipped Grounded Entry initialization or updated the hidden Main.");
                        for (var p = 0; p < 56; p++) Require(runtime.Sources.Epochs[p] == 0, "Cold air initialized a hidden Grounded asset.");
                    }
                }
                runtime.EvaluateRaw(pose, curves, sink);
                if (withStateCurves)
                {
                    CheckRefactoredStateCurves(runtime, curves, refactoredPrediction);
                    stateCurveFrames++; if (runtime.Update.State.Transitions.Count > 0) stateCurveBlends++;
                }
                if (tail != null)
                {
                    tail.Evaluate(pose, curves, AlsLocalPose.Identity, 0, 0, finalPose, finalCurves, sink);
                    if (!pose.AsSpan().SequenceEqual(finalPose) && finalSlot.SlotNodeWeight == 0) inertialFrames++;
                    if (finalSlot.SlotNodeWeight > 0) finalSlotFrames++;
                }
                var first = runtime.Sources; var firstEvents = runtime.SourceEvents;
                var firstState = runtime.Update.State.CurrentState; var firstRequests = sink.Count;
                if (withMovementCurves)
                {
                    var movingCurve = curves[poseMoving];
                    Require(!movingCurve.Present || float.IsFinite(movingCurve.Value) && movingCurve.Value is >= 0 and <= 1.000001f,
                        "Movement cache curve escaped its state/Slot blend.");
                    if (movingCurve.Present && movingCurve.Value > 0)
                    {
                        movingCurveFrames++;
                        if (seconds > 7.5f) crouchingCurveFrames++;
                        if (movingCurve.Value < .9999f) fractionalCurveFrames++;
                        if (speed == 0 && !airborne) stoppedCurveFrames++;
                    }
                    if (firstState is 1 or 2 && runtime.Update.State.Transitions.Count == 0)
                    {
                        Require(!movingCurve.Present, "Air branch inherited an unvisited movement cache curve.");
                        absentAirCurveFrames++;
                    }
                }
                if (firstState == 0 && runtime.Update.State.Transitions.Count == 0)
                    Require(curves[leftIk] == new AlsInertialCurve(1) && curves[rightIk] == new AlsInertialCurve(1),
                        "Grounded state omitted its exposed Enable_FootIK overrides outside the shared cache.");
                sink.Save();
                localSeen |= 1 << firstState; seen |= 1 << firstState;
                if (runtime.GroundedReadCount == 2)
                {
                    Require(runtime.GroundedEvaluations is >= 1 and <= 6, "Grounded aliases recomputed cached sources."); mixed++;
                }
                if (priorState == 3 && firstState == 0) { landExit = true; exits++; }
                if (priorState == 6 && firstState == 0) { moveExit = true; exits++; }
                if (firstState is 1 or 2)
                {
                    Require(curves[inAir] == new AlsInertialCurve(1), "Air state lost its graph curve write.");
                    if (runtime.GroundedReadCount == 0)
                    {
                        for (var p = 0; p < 56; p++) Require(first.Times[p] == committed.Times[p] && first.Epochs[p] == committed.Epochs[p],
                            $"Air advanced an unvisited Grounded source: hz={hz} foot={foot} frame={frame} player={p}.");
                        frozen++;
                    }
                    else foreach (var read in runtime.GroundedReads)
                    {
                        // UE updates older unfinished transitions before removing a newly completed
                        // inertial edge. A zero-duration edge has query alpha zero, not one.
                        var sourceState = read.ReadNodeIndex == landing.GroundedReadNodeIndex ? 6 : 0;
                        var found = false;
                        for (var u = 0; u < runtime.Update.UpdateCount; u++)
                            if (runtime.Update.GetUpdate(u).State == sourceState)
                            { found = true; Require(read.Context.Weight == runtime.Update.GetUpdate(u).Weight, "Grounded lost the old transition's update weight."); }
                        Require(found && !read.Context.IsActive, "A lingering Grounded update became the active state."); lingering++;
                    }
                }
                if (firstState == 6 && runtime.Update.State.Transitions.Count == 0)
                {
                    var basis = runtime.RawGroundedPose;
                    Require(!basis.SequenceEqual(runtime.ReferencePose), "Moving landing used a reference-pose Grounded fixture.");
                    // The shared frame reserves capacity beyond this graph's
                    // player layout. The standalone probe consumes that layout.
                    ReadOnlySpan<float> times = ((ReadOnlySpan<float>)first.Times)[..sources.Players.Length];
                    landingProbe.Compose(6, runtime.LandingInputs, times, basis, probe);
                    Require(pose.AsSpan().SequenceEqual(probe), "Moving landing did not consume the real Grounded cache.");
                    landingProbe.Compose(6, runtime.LandingInputs, times, runtime.ReferencePose, probe);
                    Require(!pose.AsSpan().SequenceEqual(probe), "Moving landing ignored its Grounded basis.");
                    Require(curves[leftIk] == new AlsInertialCurve(1) && curves[rightIk] == new AlsInertialCurve(1), "Landing lost Foot IK writes.");
                    movingPoses++;
                }
                foreach (var bone in pose)
                    Require(float.IsFinite(bone.Position.LengthSquared()) && MathF.Abs(bone.Rotation.LengthSquared() - 1) < .0001f,
                        "Main Movement produced an invalid raw bone.");
                for (var i = 0; i < runtime.Update.EventCount; i++)
                    Require(machines.NotifyNames.ContainsKey(runtime.Update.GetEvent(i).NotifyIndex), "Unbound native state notify.");
                machineEvents += runtime.Update.EventCount; events += firstEvents.Count; requests += firstRequests;
                runtime.Discard(); tail?.Discard();
                Require(StandingCycleSmoke.SameSync(committed, runtime.CommittedSources), "Discard changed committed source clocks.");
                if (withStateCurves)
                {
                    Prepare(); runtime.EvaluateRaw(preciseProbe, retryCurves, sink);
                    Require(curves.AsSpan().SequenceEqual(retryCurves), "Float/precise state curve placements differ.");
                    CheckRefactoredStateCurves(runtime, retryCurves, refactoredPrediction); preciseStateFrames++;
                    runtime.Discard(); tail?.Discard();
                }
                Prepare(); runtime.EvaluateRaw(retry, retryCurves, sink);
                if (tail != null)
                {
                    tail.Evaluate(retry, retryCurves, AlsLocalPose.Identity, 0, 0, finalRetry, finalRetryCurves, sink);
                    Require(finalPose.AsSpan().SequenceEqual(finalRetry) && finalCurves.AsSpan().SequenceEqual(finalRetryCurves),
                        "BaseLayer retry changed the final pose or inertia curve history.");
                }
                Require(firstState == runtime.Update.State.CurrentState && StandingCycleSmoke.SameSync(first, runtime.Sources) &&
                    pose.AsSpan().SequenceEqual(retry) && curves.AsSpan().SequenceEqual(retryCurves) && sink.MatchesSaved() &&
                    firstEvents.Count == runtime.SourceEvents.Count, "Main Movement retry changed pose, clocks, requests or events.");
                for (var e = 0; e < firstEvents.Count; e++) Require(firstEvents[e] == runtime.SourceEvents[e], "Retry changed a source event identity.");
                if (tail != null && frame == hz * 7)
                {
                    runtime.Discard(); tail.Discard(); Prepare(); runtime.EvaluateRaw(retry, retryCurves, sink);
                    var priorTailIdentity = tail.CommittedIdentity; var priorHistory = tail.CommittedHistoryCount;
                    var rejected = false; sink.FailSlot = true;
                    try { tail.Evaluate(retry, retryCurves, AlsLocalPose.Identity, 0, 0, finalRetry, finalRetryCurves, sink); }
                    catch (InvalidOperationException failure) when (failure.Message == "Injected Main Movement Slot failure.")
                    { rejected = tail.IsFaulted; }
                    finally { sink.FailSlot = false; runtime.Discard(); tail.Discard(); }
                    Require(rejected && tail.CommittedIdentity == priorTailIdentity && tail.CommittedHistoryCount == priorHistory &&
                        StandingCycleSmoke.SameSync(committed, runtime.CommittedSources), "Final Slot failure leaked candidate state.");
                    Prepare(); runtime.EvaluateRaw(retry, retryCurves, sink);
                    tail.Evaluate(retry, retryCurves, AlsLocalPose.Identity, 0, 0, finalRetry, finalRetryCurves, sink);
                    Require(finalPose.AsSpan().SequenceEqual(finalRetry) && finalCurves.AsSpan().SequenceEqual(finalRetryCurves) &&
                        StandingCycleSmoke.SameSync(first, runtime.Sources), "Final Slot failure retry diverged.");
                    finalFaults++;
                }
                runtime.Commit(result.Identity); tail?.Commit(result.Identity);
                // This fixture intentionally keeps raw feedback, preserving the raw-graph baseline.
                baseClf = curves[curveClf].Value; priorState = firstState; frames++;
                void Prepare()
                {
                    sink.Reset(); var sourceContext = tail == null ? context : tail.Begin(context, finalSlot).Context;
                    runtime.Prepare(result, move, rules, inputs, sourceContext, sink);
                }
            }
            Require(localSeen == 79 && landExit && moveExit, $"Main Movement missed states or real landing exits: hz={hz} foot={foot} states={localSeen}.");
        }
        Require(mixed > 0 && movingPoses > 0 && events > 0 && requests > 0 && faults == 6 && guards == 6 && lingering > 0,
            "Main Movement integration coverage is incomplete.");
        GD.Print($"MAIN_MOVEMENT_RUNTIME_OK frames={frames} states={seen} source_events={events} state_events={machineEvents} inertia_requests={requests} ground_aliases={mixed} real_moving_land={movingPoses} actual_exits={exits} frozen_ground={frozen} lingering_ground={lingering} cold_air=3 slot_faults={faults} guards={guards} sync=one_batch retry=identical pose=raw demo=not_connected");
        if (withMovementCurves)
        {
            Require(movingCurveFrames > 0 && crouchingCurveFrames > 0 && fractionalCurveFrames > 0 && stoppedCurveFrames > 0 && absentAirCurveFrames > 0,
                "Movement cache curve integration coverage missing.");
            GD.Print($"REFACTORED_MOVEMENT_CURVES_OK present={movingCurveFrames} crouching={crouchingCurveFrames} blended={fractionalCurveFrames} " +
                $"stopped_but_present={stoppedCurveFrames} absent_air={absentAirCurveFrames} placement=cache_before_state retry=identical prediction=not_substituted");
        }
        if (withBaseLayer)
        {
            Require(inertialFrames > 0 && finalSlotFrames > 0 && finalFaults == 6, "BaseLayer tail coverage missing.");
            GD.Print($"BASE_LAYER_MOVEMENT_OK frames={frames} changed_by_inertia={inertialFrames} slot_frames={finalSlotFrames} late_faults={finalFaults} retry=identical order=movement_inertia_slot montage=fixture demo=not_connected");
        }
        if (withStateCurves)
        {
            Require(stateCurveFrames == frames && preciseStateFrames == frames && stateCurveBlends > 0 && stateInputGuards == 18,
                "Missing Refactored state/crossfade coverage.");
            GD.Print($"REFACTORED_STATE_CURVES_OK frames={stateCurveFrames} precise={preciseStateFrames} blends={stateCurveBlends} input_guards={stateInputGuards} sites=6 prediction=independent retry=identical demo=not_connected");
        }
    }

    private static void CheckRefactoredStateCurves(AlsMainMovementFrameRuntime runtime, ReadOnlySpan<AlsInertialCurve> curves, float prediction)
    {
        string[] names = ["PoseGrounded", "PoseInAir", "PoseStanding", "FootLeftIk", "FootRightIk", "FootLeftLock", "FootRightLock"];
        var stack = runtime.Update.State.Transitions;
        foreach (var name in names)
        {
            if (name is "FootLeftLock" or "FootRightLock")
            {
                // These channels also contain V4 state/Plant ModifyCurve writes,
                // before state blending and the Grounded slot. They are not zero
                // everywhere outside Land. Compare against the native V4 channel
                // that has already traversed the same authored graph and slot.
                var original = curves[runtime.CurveNames.IndexOf(AlsRefactoredV4SourceCurves.V4ProducerName(name))];
                var adapted = curves[runtime.CurveNames.IndexOf(name)];
                Require(adapted == original,
                    $"Graph lock producer lost presence/value through state blending: {name} expected={original}, actual={adapted}.");
                continue;
            }
            float StateValue(int state) => name switch
            {
                "PoseGrounded" => state is 0 or 3 or 6 ? 1 : 0,
                "PoseInAir" => state is 1 or 2 ? 1 : 0,
                "PoseStanding" => state is 1 or 2 or 3 ? 1 : 0,
                "FootLeftIk" or "FootRightIk" => state is 1 or 2 ? prediction : 1,
                _ => state == 3 ? 1 : 0,
            };
            var expected = 0f;
            foreach (var state in new[] { 0, 1, 2, 3, 6 }) expected += AlsTransitionStack.Weight(stack, state) * StateValue(state);
            var actual = curves[runtime.CurveNames.IndexOf(name)];
            Require(MathF.Abs((actual.Present ? actual.Value : 0) - expected) <= .000002f,
                $"Refactored curve misplaced around state/cache blend: {name} expected={expected:R}, actual={actual}.");
            if (stack.Count == 0 && (name is "FootLeftIk" or "FootRightIk" || expected > 0))
                Require(actual.Present, "Required Refactored producer lost curve presence.");
        }
    }

    private sealed class Sink(AlsLocalPose[] rest) : IAlsGroundedFrameRuntimeSink, IAlsGroundedSlotPoseSink, IAlsBaseLayerSlotPoseSink
    {
        private readonly AlsPoseUpdateContext[] _contexts = new AlsPoseUpdateContext[32], _savedContexts = new AlsPoseUpdateContext[32];
        private readonly float[] _seconds = new float[32], _savedSeconds = new float[32];
        private int _savedCount;
        public int Count { get; private set; }
        public bool FailSlot;
        public AlsBaseLayerPoseRuntime? Tail;
        public int Requester = 999;
        public void Reset() => Count = 0;
        public void Save() { _savedCount = Count; _contexts.CopyTo(_savedContexts, 0); _seconds.CopyTo(_savedSeconds, 0); }
        public bool MatchesSaved()
        {
            if (Count != _savedCount) return false;
            for (var i = 0; i < Count; i++)
            {
                var a = _contexts[i]; var b = _savedContexts[i];
                if (_seconds[i] != _savedSeconds[i] || a.Identity != b.Identity || a.Weight != b.Weight || a.Delta != b.Delta ||
                    a.RootMotionWeight != b.RootMotionWeight || a.IsActive != b.IsActive || a.InertializationSync != b.InertializationSync ||
                    a.InertializationRequester != b.InertializationRequester || a.StateCount != b.StateCount) return false;
                for (var s = 0; s < a.StateCount; s++) if (a.GetState(s) != b.GetState(s)) return false;
            }
            return true;
        }
        public void RequestInertialization(in AlsPoseUpdateContext context, float seconds)
        {
            Require(Count < _seconds.Length && float.IsFinite(seconds) && seconds >= 0 && context.InertializationRequester == Requester,
                "Invalid or lost inertialization request.");
            _contexts[Count] = context; _seconds[Count++] = seconds;
            Tail?.RequestInertialization(context, seconds);
        }
        public void UpdateGroundedSlot(int slot, in AlsSlotWeights weights, in AlsSlotSourceUpdate source, in AlsPoseUpdateContext context) => weights.Validate();
        public void OnCachedUpdatesSkipped(int handler, ReadOnlySpan<AlsPoseUpdateContext> skipped) { }
        public void RefreshSourceBones(int cache) { }
        public void EvaluateSlot(in AlsSlotWeights weights, ReadOnlySpan<AlsLocalPose> source,
            ReadOnlySpan<AlsInertialCurve> sourceCurves, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves)
        {
            if (FailSlot) throw new InvalidOperationException("Injected Main Movement Slot failure.");
            for (var bone = 0; bone < bones.Length; bone++) bones[bone] = source.IsEmpty ? rest[bone] :
                AlsPoseBlender.Normalize(AlsPoseBlender.BlendRaw(source[bone], rest[bone], weights.SlotNodeWeight));
            for (var i = 0; i < curves.Length; i++) curves[i] = sourceCurves.IsEmpty ? default :
                AlsStandingCycleCurves.Lerp(sourceCurves[i], default, weights.SlotNodeWeight);
        }
        public void EvaluateSlot(in AlsSlotWeights weights, ReadOnlySpan<AlsPrecisePose> source,
            ReadOnlySpan<AlsInertialCurve> sourceCurves, Span<AlsPrecisePose> bones, Span<AlsInertialCurve> curves)
        {
            if (FailSlot) throw new InvalidOperationException("Injected Main Movement Slot failure.");
            for (var bone = 0; bone < bones.Length; bone++) bones[bone] = source.IsEmpty ? new AlsPrecisePose(rest[bone]) :
                AlsPrecisePoseBlender.Normalize(AlsPrecisePoseBlender.BlendRaw(source[bone], new AlsPrecisePose(rest[bone]), weights.SlotNodeWeight));
            for (var i = 0; i < curves.Length; i++) curves[i] = sourceCurves.IsEmpty ? default :
                AlsStandingCycleCurves.Lerp(sourceCurves[i], default, weights.SlotNodeWeight);
        }
    }
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
