using Godot;
using GodotAls.Assets;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NVector2 = System.Numerics.Vector2;

namespace GodotAls.Animation;

public partial class SharedSourceBatchSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void Run()
    {
        var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
        var profile = AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"), set);
        var pose = AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"), set, profile);
        var movementSettings = AlsLocomotionInputCompiler.Compile(Read("v4_locomotion_inputs.json")).Movement;
        var text = Read("v4_locomotion_source_graph.json");
        var sources = AlsLocomotionSourceCompiler.Compile(text, set, profile.SkeletonId);
        var mainStates = AlsMainGroundedPoseCompiler.Compile(text, sources);
        var mainMachine = AlsGroundedMachineCompiler.CompileGrounded(text).Main.Runtime;
        var main = new AlsMainGroundedSourceCollector(sources, mainStates);
        var dependencies = AlsGroundedPoseDependencyCompiler.Compile(Read("v4_grounded_dependencies.json"), set.Skeletons[profile.SkeletonId]);
        var detailProfile = AlsLocomotionDetailCompiler.Compile(Read("v4_locomotion_detail_graph.json"), set, profile.SkeletonId);
        var crouching = AlsCrouchingStateCompiler.Compile(text, Read("v4_pose_cache_graph.json"), sources, set);
        var cycles = AlsCrouchingCycleCompiler.Compile(text, Read("v4_pose_cache_graph.json"),
            Read("v4_locomotion_curves.json"), Read("v4_lean_sampling.json"), sources, set);
        var crouch = new AlsCrouchingSourceCollector(sources, crouching.Runtime, cycles.Lean);
        var updates = new AlsLocomotionSourceUpdate[AlsCycleSyncFrame.PlayerCapacity];
        var samples = new AlsLocomotionSampleUpdate[AlsCycleSyncFrame.SampleCapacity];
        var active = new bool[updates.Length]; var automatic = new AlsGroundedAutomaticTime[8];
        var splitFrames = 0; var mixedFrames = 0; var eventsTotal = 0; var rejected = 0; var mainSeen = 0;
        var sourceCount = 0; var mainPoseFrames = 0; var endpoints = 0;
        var leaders = new HashSet<AlsLocomotionSourceDomain>(); long allocated = -1;
        foreach (var hz in new[] { 30, 60, 120 })
        {
            using var library = AlsAnimationLibraryBuilder.Build(set, profile, pose); AddChild(library.Root);
            using var graph = AlsLocomotionGraphBuilder.Build(library, profile, pose, set);
            var standing = graph.StandingCycle!;
            using var reference = new AlsDetailPoseSampler(library, set, detailProfile);
            using var mainPose = new AlsMainGroundedPoseGraph(library, set, mainMachine, mainStates, dependencies, sources);
            var rest = reference.ReferencePose.ToArray(); var crouchPose = new AlsLocalPose[rest.Length];
            var mainOutput = new AlsLocalPose[rest.Length]; var mainRetry = new AlsLocalPose[rest.Length];
            using var crouchIdle = new AlsLocalPoseClip(library.Library.GetAnimation(library.ClipNames[profile.CrouchingIdleAnimationId]),
                library.Skeleton, ownsAnimation: false);
            crouchIdle.Sample(rest, 0, crouchPose);
            var previous = default(AlsStandingCycleFrame);
            for (var frame = 1; frame <= hz * 4; frame++)
            {
                var result = Result(frame, hz); var movement = Movement(result);
                var wrapped = standing.Prepare(previous, result, 1f / hz, new(1.75f, 3.75f, 6.5f), movement, true);
                var split = standing.PrepareUpdate(previous, result, 1f / hz, new(1.75f, 3.75f, 6.5f), movement, true);
                var consumed = standing.ConsumeSharedUpdate(previous, result, 1f / hz, new(1.75f, 3.75f, 6.5f), movement,
                    split.Detail.Standing.Update, split.Detail.Standing.Rotation);
                Require(consumed.State == split.State && consumed.Detail.Standing.Stop.Left == split.Detail.Standing.Stop.Left &&
                    consumed.Detail.Standing.Stop.Right == split.Detail.Standing.Stop.Right &&
                    consumed.Detail.Standing.InertializationSeconds == split.Detail.Standing.InertializationSeconds,
                    "Consuming the completed Standing update changed a derived source or advanced a machine twice.");
                split = consumed;
                var candidate = previous.Sync; var playerCount = 0; var sampleCount = 0;
                standing.CollectSources(split, ref candidate, updates, samples, active, ref playerCount, ref sampleCount);
                Require(candidate.PlayerCount == previous.Sync.PlayerCount && candidate.SampleCount == previous.Sync.SampleCount,
                    "Collection advanced synchronization before all branches participated.");
                var sync = AlsSharedSourceBatch.Evaluate(standing.SourceBindings, candidate, updates.AsSpan(0, playerCount),
                    samples.AsSpan(0, sampleCount), active.AsSpan(0, playerCount), split.State.PlayRate, split.Detail.Standing.Rotation, null, 1f / hz);
                standing.CompleteSources(ref split, previous, sync); standing.EvaluatePose(ref split, previous, 1f / hz);
                Require(StandingCycleSmoke.SameSync(wrapped.Sync, split.Sync) && wrapped.State == split.State,
                    "Split production stages differ from the Controller entry.");
                var wrappedDetail = wrapped.Detail; var splitDetail = split.Detail;
                ReadOnlySpan<AlsLocalPose> a = wrappedDetail.Pose, b = splitDetail.Pose;
                ReadOnlySpan<AlsInertialCurve> ca = wrappedDetail.Curves, cb = splitDetail.Curves;
                Require(a.SequenceEqual(b) && ca.SequenceEqual(cb), "Split stages changed final Standing bones or curves.");
                foreach (var player in sources.Players)
                    if (player.Domain is AlsLocomotionSourceDomain.Crouching or AlsLocomotionSourceDomain.MainGrounded)
                        Require(sync.Epochs[player.PlayerId] == 0 && sync.Times[player.PlayerId] == 0,
                            "Standing initialized an unvisited Main/Crouching source.");
                standing.Apply(graph.Tree, split); standing.CommitPose(); previous = split; splitFrames++;
            }

            // A new character/scenario needs a fresh pose-cache owner as well as a fresh value frame.
            using var mixedLibrary = AlsAnimationLibraryBuilder.Build(set, profile, pose); AddChild(mixedLibrary.Root);
            using var mixedGraph = AlsLocomotionGraphBuilder.Build(mixedLibrary, profile, pose, set);
            standing = mixedGraph.StandingCycle!;
            previous = default;
            var shared = default(AlsCycleSyncFrame); var mainState = default(AlsGroundedMachineState);
            var eventsCommitted = default(AlsP5SourceEventState);
            for (var frame = 1; frame <= hz * 6; frame++)
            {
                var identity = new AlsFrameIdentity(frame + 10000, 3, 2); var delta = 1f / hz;
                var result = Result(identity.FrameId, hz); result.Identity = identity;
                result.BlendCoordinates = frame / (hz * 2) % 2 == 0 ? new(0, 1.75f) : new(.7f, .4f);
                var previousWithShared = previous with { Sync = shared };
                var prepared = standing.PrepareUpdate(previousWithShared, result, delta, new(1.75f, 3.75f, 6.5f), Movement(result), true);
                var candidate = shared; var playerCount = 0; var sampleCount = 0;
                standing.CollectSources(prepared, ref candidate, updates, samples, active, ref playerCount, ref sampleCount);
                // Controlled overlap stresses the transaction; these are not Main's production cache weights.
                var standingWeight = frame / (hz * 2) % 2 == 0 ? .8f : .15f;
                for (var i = 0; i < playerCount; i++) updates[i] = updates[i] with { Weight = updates[i].Weight * standingWeight };
                var rotation = prepared.Detail.Standing.Rotation;
                var rate = hz == 120 ? -.75f : .75f;
                crouch.Begin(identity, candidate, rate, rotation, new NVector2(MathF.Sin(frame * .04f), MathF.Cos(frame * .03f)));
                foreach (var player in sources.Players)
                    if (player.Domain == AlsLocomotionSourceDomain.Crouching)
                    {
                        if (crouch.Frame.Epochs[player.PlayerId] == 0) crouch.InitializeSource(player.PlayerId);
                        crouch.UpdateSource(player.PlayerId, new(identity, 1 - standingWeight, delta));
                    }
                candidate = crouch.Frame;
                for (var i = 0; i < crouch.PlayerCount; i++)
                {
                    updates[playerCount] = crouch.Updates[i] with { SampleStart = crouch.Updates[i].SampleStart + sampleCount };
                    active[playerCount++] = frame % 7 != 0;
                }
                crouch.Samples.AsSpan(0, crouch.SampleCount).CopyTo(samples.AsSpan(sampleCount)); sampleCount += crouch.SampleCount;
                var stance = frame / (hz * 2) % 2 == 0 ? AlsStance.Standing : AlsStance.Crouching;
                var rules = new AlsGroundedRuleInput(false, false, false, stance, true, hz == 120 && frame == 1, frame == 1 ? 0 : 1, 0);
                main.ObserveAutomatic(shared, automatic);
                var mainUpdate = AlsGroundedStateMachine.Update(mainMachine, mainState, rules, automatic, 1, delta, identity.FrameId);
                main.Collect(mainUpdate, ref candidate, updates, samples, active, ref playerCount, ref sampleCount);
                mainSeen |= 1 << mainUpdate.State.CurrentState;
                foreach (var state in mainStates)
                {
                    if (state.Kind != AlsMainGroundedPoseKind.Source) continue;
                    var initializations = 0;
                    for (var i = 0; i < mainUpdate.InitializationCount; i++)
                        if (mainUpdate.GetInitialization(i) == state.StateIndex) initializations++;
                    Require(candidate.Epochs[state.PlayerId] == shared.Epochs[state.PlayerId] + initializations,
                        "Main source initialization reset the wrong owner or lost entry multiplicity.");
                    if (state.StateIndex == 7 && initializations > 0)
                        Require(candidate.Times[state.PlayerId] == sources.Players[state.PlayerId].StartPosition,
                            "From Roll did not retain its explicit evaluation time.");
                }
                for (var i = 0; i < mainUpdate.UpdateCount; i++)
                {
                    var child = mainUpdate.GetUpdate(i); var id = mainStates[child.State].PlayerId;
                    if (id >= 0) Require(candidate.CachedWeights[id] == child.Weight, "Main source lost its actual state update weight.");
                }
                var first = Tick(); var retry = Tick();
                Require(StandingCycleSmoke.SameSync(first, retry), "Shared batch retry changed a source or notification.");
                for (var i = 0; i < first.PlayerCount; i++)
                {
                    var history = first.Players[i]; var source = sources.Players[history.PlayerId];
                    if (source.Domain != AlsLocomotionSourceDomain.MainGrounded) continue;
                    var sample = sources.Samples[source.SampleStart];
                    var expected = Math.Clamp(candidate.Times[source.PlayerId] + delta * source.DefaultPlayRate / source.PlayRateBasis * sample.AssetRateScale,
                        0, sample.DurationSeconds);
                    Require(MathF.Abs(expected - history.Time) < .000001f, "Main DoNotSync source did not use its own constant play rate.");
                }
                for (var i = 0; i < first.GroupCount; i++)
                    if (first.Groups[i].Group.HasLeader) leaders.Add(sources.Players[first.Groups[i].Group.LeaderPlayerId].Domain);
                for (var i = 0; i < first.NotifyTickCount; i++)
                {
                    var tick = first.NotifyTicks[i]; var id = sources.Samples[tick.SampleId].PlayerId;
                    var contribution = Array.FindIndex(updates, 0, playerCount, p => p.PlayerId == id);
                    Require(contribution >= 0 && tick.ActiveContext == active[contribution], "Group reordering lost source activity identity.");
                    Require(sources.Players[id].Kind != AlsLocomotionSourceKind.TeleportEvaluator, "Teleport evaluator emitted a timeline notification.");
                }
                var binding = standing.SourceBindings;
                Require(AlsP5Runtime.TryPrepareSourceEvents(binding, identity, delta,
                    ((ReadOnlySpan<AlsP5SourceNotifyTick>)first.NotifyTicks)[..first.NotifyTickCount], 1, eventsCommitted,
                    out var nextEvents, out var events, out _), "Mixed source event candidate failed.");
                Require(AlsP5Runtime.TryPrepareSourceEvents(binding, identity, delta,
                    ((ReadOnlySpan<AlsP5SourceNotifyTick>)retry.NotifyTicks)[..retry.NotifyTickCount], 1, eventsCommitted,
                    out var retryEvents, out var retried, out _) && events.Count == retried.Count && nextEvents.RandomSeed == retryEvents.RandomSeed,
                    "Mixed source event retry changed.");
                for (var i = 0; i < events.Count; i++) Require(events[i] == retried[i], "Mixed source event identity changed.");
                if (frame == hz)
                {
                    for (var i = 0; i < 100; i++) Tick();
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    for (var i = 0; i < 500; i++) Tick();
                    allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                    Require(allocated == 0, $"Shared source tick allocated {allocated} bytes.");
                    for (var fault = 0; fault < 9; fault++)
                    {
                        var broken = candidate; var savedUpdate = updates[0]; var savedSample = samples[0];
                        if (fault == 0) { broken.Initialized = true; broken.BindingDigest ^= 1; }
                        if (fault == 1) broken.Epochs[updates[0].PlayerId]++;
                        if (fault == 2) broken.Times[updates[0].PlayerId] = float.NaN;
                        if (fault == 3) samples[0] = samples[0] with { SampleId = sources.Samples.Length };
                        if (fault == 4) broken.PlayerCount = -1;
                        if (fault == 5) updates[0] = updates[0] with { Weight = float.PositiveInfinity };
                        var refused = false;
                        try
                        {
                            AlsSharedSourceBatch.Evaluate(standing.SourceBindings, broken, updates.AsSpan(0, playerCount), samples.AsSpan(0, sampleCount),
                                active.AsSpan(0, fault == 6 ? playerCount - 1 : playerCount), prepared.State.PlayRate, rotation,
                                fault == 7 ? null : rate, fault == 8 ? float.NaN : delta);
                        }
                        catch (ArgumentException) { refused = true; }
                        catch (InvalidOperationException) { refused = true; }
                        finally { updates[0] = savedUpdate; samples[0] = savedSample; }
                        Require(refused && StandingCycleSmoke.SameSync(first, Tick()), "Invalid mixed batch was accepted or contaminated retry."); rejected++;
                    }
                }
                standing.CompleteSources(ref prepared, previousWithShared, first);
                standing.EvaluatePose(ref prepared, previousWithShared, delta);
                var detailFrame = prepared.Detail; ReadOnlySpan<AlsLocalPose> standingPose = detailFrame.Pose;
                var sourceCountForPose = standing.SourceBindings.Sources.Players.Length;
                mainPose.Compose(mainUpdate.State, ((ReadOnlySpan<float>)first.Times)[..sourceCountForPose], standingPose[..rest.Length], crouchPose, rest, mainOutput);
                mainPose.Compose(mainUpdate.State, ((ReadOnlySpan<float>)retry.Times)[..sourceCountForPose], standingPose[..rest.Length], crouchPose, rest, mainRetry);
                Require(mainOutput.AsSpan().SequenceEqual(mainRetry), "Main pose changed when retrying the shared source clock.");
                foreach (var bone in mainOutput)
                    Require(float.IsFinite(bone.Position.LengthSquared()) && MathF.Abs(bone.Rotation.LengthSquared() - 1) < .0001f,
                        "Main shared-source pose is invalid.");
                mainPoseFrames++;
                standing.Apply(mixedGraph.Tree, prepared); standing.CommitPose();
                previous = prepared; shared = first; mainState = mainUpdate.State; eventsCommitted = nextEvents;
                eventsTotal += events.Count; sourceCount += playerCount; mixedFrames++;
                AlsCycleSyncFrame Tick() => AlsSharedSourceBatch.Evaluate(standing.SourceBindings, candidate,
                    updates.AsSpan(0, playerCount), samples.AsSpan(0, sampleCount), active.AsSpan(0, playerCount),
                    prepared.State.PlayRate, rotation, rate, delta);
            }
            var expired = AlsSharedSourceBatch.Evaluate(standing.SourceBindings, shared, [], [], [], 1, default, 1, 1f / hz);
            Require(expired.PlayerCount == 0 && expired.SampleCount == 0 && expired.NotifyTickCount == 0,
                "Empty shared batch retained active source histories.");
            Require(!expired.Group.HasLeader, "Empty batch retained the selected group's old leader.");
            for (var i = 0; i < expired.GroupCount; i++) Require(!expired.Groups[i].Group.HasLeader, "Empty batch retained a group leader.");
            var idle = new AlsGroundedRuleInput(false, false, false, AlsStance.Standing, true, false, 0, 0);
            Array.Clear(automatic);
            var standState = AlsGroundedStateMachine.Update(mainMachine, default, idle, automatic, 1, .01f, 1).State;
            var crouchRules = idle with { Stance = AlsStance.Crouching, BasePoseClf = 1 };
            var crouchState = AlsGroundedStateMachine.Update(mainMachine, default, crouchRules, automatic, 1, .01f, 1).State;
            foreach (var stateId in new[] { 3, 4 })
            {
                var sourceState = AlsGroundedStateMachine.Update(mainMachine, stateId == 3 ? standState : crouchState,
                    stateId == 3 ? crouchRules : idle with { BasePoseClf = 1 }, automatic, 1, 1, 2).State;
                Require(sourceState.CurrentState == stateId && sourceState.Transitions.Count == 0, "Main endpoint fixture is not a single source state.");
                var player = sources.Players[mainStates[stateId].PlayerId]; var sample = sources.Samples[player.SampleStart];
                var times = shared.Times; times[player.PlayerId] = sample.DurationSeconds;
                mainPose.Compose(sourceState, ((ReadOnlySpan<float>)times)[..standing.SourceBindings.Sources.Players.Length], rest, crouchPose, rest, mainOutput);
                using var clip = new AlsLocalPoseClip(library.Library.GetAnimation(library.ClipNames[sample.AnimationId]), library.Skeleton, ownsAnimation: false);
                clip.Sample(rest, clip.Length, mainRetry);
                Require(mainOutput.AsSpan().SequenceEqual(mainRetry), "Main float endpoint differs from the actual last frame.");
                times[player.PlayerId] = MathF.BitIncrement(sample.DurationSeconds);
                var refused = false;
                try { mainPose.Compose(sourceState, ((ReadOnlySpan<float>)times)[..standing.SourceBindings.Sources.Players.Length], rest, crouchPose, rest, mainOutput); }
                catch (ArgumentException) { refused = true; }
                Require(refused, "Main accepted a time beyond the source duration."); endpoints++;
            }
        }
        Require(splitFrames == 840 && mixedFrames == 1260 && eventsTotal > 0 && rejected == 27 && allocated == 0 &&
            (mainSeen & ((1 << 3) | (1 << 4) | (1 << 7))) == ((1 << 3) | (1 << 4) | (1 << 7)) &&
            leaders.Contains(AlsLocomotionSourceDomain.Cycle) && leaders.Contains(AlsLocomotionSourceDomain.Crouching),
            $"Shared source coverage incomplete: split={splitFrames} mixed={mixedFrames} events={eventsTotal} rejected={rejected} main={mainSeen} leaders={string.Join(',', leaders)}.");
        Require(mainPoseFrames == 1260 && endpoints == 6, "Main shared-source pose/endpoints were not covered.");
        GD.Print($"SHARED_SOURCE_BATCH_OK split_frames={splitFrames} mixed_frames={mixedFrames} contributions={sourceCount} events={eventsTotal} rejected={rejected} allocated={allocated} main_states={mainSeen} main_pose_frames={mainPoseFrames} endpoints={endpoints} rates=30,60,120 standing=production main_crouching=controlled_overlap clocks=single_batch retry=identical");

        AlsStandingMovementInput Movement(in AlsFrameResult result) => AlsStandingMovementInputModel.Evaluate(result.Identity,
            new(result.BlendCoordinates.Length(), 0, 0), result.BlendCoordinates.LengthSquared() > 0 ? 1 : 0, movementSettings);
    }

    private static AlsFrameResult Result(long frame, int hz)
    {
        var result = AlsFrameResult.CreateDefault(new(frame, 0, 1));
        result.AnimationState = AlsAnimationState.Grounded; result.ResolvedLocomotionState = AlsLocomotionState.Grounded;
        result.ActualStance = AlsStance.Standing; result.ActualRotationMode = AlsRotationMode.LookingDirection;
        result.ActualGait = AlsGait.Walking;
        result.BlendCoordinates = (frame / hz % 4) switch { 0 => new(0, 1.75f), 1 => new(.7f, .7f), 2 => default, _ => new(-1.75f, 0) };
        return result;
    }
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
