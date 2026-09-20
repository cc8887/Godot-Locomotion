using Godot;
using GodotAls.Assets;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace GodotAls.Animation;

public partial class LandingRuntimeSmoke : Node
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
        var json = Read("v4_main_movement_graph.json");
        var sources = AlsLocomotionSourceCompiler.CompileWithMovement(json, set, locomotion.SkeletonId);
        var profile = AlsLandingPoseCompiler.Compile(json, Read("v4_pose_cache_graph.json"), sources, set);
        var machine = AlsGroundedMachineCompiler.CompileMovement(json).Movement!.Runtime;
        var p5 = AlsP5aAnimationRuntimeProfileCompiler.Compile(Read("p5a_animation_runtime.json"), set);
        var inventory = AlsP5SourceInventoryCompiler.Compile(Read("v4_anim_graph_inventory.json"), set, sources);
        var layout = AlsP5OccurrenceLayoutCompiler.CompileSourceAware(locomotion, turns, p5, sources, inventory);
        var snapshot = AlsP5CoreRuntimeBindingCompiler.CompileSourceAware(set, locomotion, turns, p5, layout, sources, inventory);
        var players = sources.Players; var samples = sources.Samples;
        using var library = AlsAnimationLibraryBuilder.Build(set, locomotion, turns, sourceProfile: sources); AddChild(library.Root);
        using var graph = new AlsLandingPoseGraph(library, set, sources, profile);
        var collector = new AlsLandingSourceCollector(sources, profile); var sink = new GroundedFixture(profile.GroundedReadNodeIndex);
        var count = library.Skeleton.GetBoneCount(); var rest = new AlsLocalPose[count]; var basis = new AlsLocalPose[count];
        for (var i = 0; i < count; i++)
        {
            var t = library.Skeleton.GetBoneRest(i); var q = t.Basis.GetRotationQuaternion(); var s = t.Basis.Scale;
            rest[i] = new(new NVector3(t.Origin.X, t.Origin.Y, t.Origin.Z), new NQuaternion(q.X, q.Y, q.Z, q.W), new NVector3(s.X, s.Y, s.Z));
        }
        using var baseClip = new AlsLocalPoseClip(library.Library.GetAnimation(library.ClipNames[profile.AdditiveBaseAnimationId]), library.Skeleton, ownsAnimation: false);
        baseClip.Sample(rest, 0, basis);
        var pose = new AlsLocalPose[count]; var retryPose = new AlsLocalPose[count]; var expected = new AlsLocalPose[count];
        var clips = new AlsLocalPoseClip?[4]; var ids = new[] { profile.Light, profile.Heavy, profile.MovingLight, profile.MovingHeavy };
        var automatic = new AlsGroundedAutomaticTime[8]; var active = new bool[4];
        var frames = 0; var poseFrames = 0; var eventsTotal = 0; var endpoints = 0; var curveChecks = 0; var exits = 0; var retryFailures = 0; var movingChanged = false;
        try
        {
            for (var i = 0; i < 4; i++) clips[i] = new(library.Library.GetAnimation(library.ClipNames[samples[players[ids[i]].SampleStart].AnimationId]), library.Skeleton, ownsAnimation: false);
            graph.Compose(6, default, new float[players.Length], basis, pose);
            Require(pose.AsSpan().SequenceEqual(basis), "Moving landing applied additive before its first Update.");
            var dormant = default(AlsCycleSyncFrame);
            dormant.Epochs[profile.MovingLight] = dormant.Epochs[profile.MovingHeavy] = 1;
            dormant.CachedWeights[profile.MovingLight] = dormant.CachedWeights[profile.MovingHeavy] = 1;
            dormant.Times[profile.MovingLight] = .2f; dormant.Times[profile.MovingHeavy] = .7f;
            collector.Begin(new(1, 5, 3), dormant, default);
            Require(collector.MovingTime().HasAsset && collector.MovingTime().Time == .2f,
                "Inactive cached player lost its automatic time or baked tie order.");
            CheckAllOuterSources();
            foreach (var hz in new[] { 30, 60, 120 }) foreach (var moving in new[] { false, true }) foreach (var speed in new[] { -4f, -10f, -20f })
            {
                var previous = default(AlsGroundedMachineState); var committed = default(AlsCycleSyncFrame);
                var inputs = default(AlsLandingBlendInputs); var eventsBefore = default(AlsP5SourceEventState);
                var target = moving ? 6 : 3; var entered = false; var exited = false;
                for (var frame = 1; frame <= hz * 3; frame++)
                {
                    var identity = new AlsFrameIdentity(frame, 5, 3); var context = new AlsPoseUpdateContext(identity, .8f, 1f / hz);
                    var next = Prepare(); var firstSync = collector.Frame; var firstInputs = collector.Inputs;
                    var binding = snapshot.CreateCoreView();
                    Require(AlsP5Runtime.TryPrepareSourceEvents(binding, identity, context.Delta,
                        ((ReadOnlySpan<AlsP5SourceNotifyTick>)firstSync.NotifyTicks)[..firstSync.NotifyTickCount], 1, eventsBefore,
                        out var nextEvents, out var events, out var failure), $"Landing event preparation failed: {failure}");
                    var retry = Prepare(); var retrySync = collector.Frame;
                    Require(next.State.CurrentState == retry.State.CurrentState && next.State.Transitions.Latest == retry.State.Transitions.Latest &&
                        firstInputs == collector.Inputs && StandingCycleSmoke.SameSync(firstSync, retrySync), "Landing retry changed state or clocks.");
                    Require(AlsP5Runtime.TryPrepareSourceEvents(binding, identity, context.Delta,
                        ((ReadOnlySpan<AlsP5SourceNotifyTick>)retrySync.NotifyTicks)[..retrySync.NotifyTickCount], 1, eventsBefore,
                        out var retriedEvents, out var retried, out failure) && retriedEvents.RandomSeed == nextEvents.RandomSeed &&
                        retried.Count == events.Count, "Landing event retry changed.");
                    for (var i = 0; i < events.Count; i++) Require(events[i] == retried[i], "Landing event identity changed.");
                    for (var i = 0; i < next.UpdateCount; i++)
                    {
                        var state = next.GetUpdate(i).State; if (state is not (3 or 6)) continue;
                        ReadOnlySpan<float> times = ((ReadOnlySpan<float>)firstSync.Times)[..players.Length];
                        graph.Compose(state, firstInputs, times, basis, pose); graph.Compose(state, firstInputs, times, basis, retryPose);
                        Require(pose.AsSpan().SequenceEqual(retryPose), "Landing pose retry differs."); poseFrames++;
                        if (frame == 4)
                        {
                            var invalidTimes = times.ToArray(); invalidTimes[ids[state == 3 ? 0 : 2]] = float.NaN;
                            var rejected = false;
                            try { graph.Compose(state, firstInputs, invalidTimes, basis, retryPose); }
                            catch (ArgumentException) { rejected = true; }
                            Require(rejected && StandingCycleSmoke.SameSync(firstSync, collector.Frame), "Invalid landing pose contaminated clocks.");
                            graph.Compose(state, firstInputs, times, basis, retryPose);
                            Require(pose.AsSpan().SequenceEqual(retryPose), "Landing pose did not recover after failure."); retryFailures++;
                        }
                        var alpha = firstInputs.PoseAlpha(state);
                        if (alpha is 0 or 1)
                        {
                            var slot = (state == 3 ? 0 : 2) + (alpha == 1 ? 1 : 0); var sample = samples[players[ids[slot]].SampleStart];
                            clips[slot]!.SampleSourceSeconds(rest, times[ids[slot]], sample.DurationSeconds, expected);
                            for (var bone = 0; bone < count; bone++) Require(NVector3.Distance(pose[bone].Position, expected[bone].Position) < 1e-5f &&
                                MathF.Abs(NQuaternion.Dot(pose[bone].Rotation, expected[bone].Rotation)) > .99999f, "Landing endpoint did not reconstruct the actual clip.");
                            endpoints++;
                        }
                        foreach (var name in new[] { "Enable_FootIK_L", "Enable_FootIK_R", "BasePose_N", "FootLock_L", "FootLock_R", "AbsentLandingProbe" })
                        {
                            var value = graph.Curve(state, firstInputs, times, name, default);
                            if (name.StartsWith("Enable_", StringComparison.Ordinal) || name == "BasePose_N" || state == 3 && name.StartsWith("FootLock_", StringComparison.Ordinal))
                                Require(value.Present && MathF.Abs(value.Value - 1) < 1e-6f, "Landing authored curve write missing.");
                            if (name == "AbsentLandingProbe") Require(!value.Present, "Landing invented an absent curve.");
                            Require(value == graph.Curve(state, firstInputs, times, name, default), "Landing curve retry differs."); curveChecks++;
                        }
                        movingChanged |= state == 6 && !pose.AsSpan().SequenceEqual(basis);
                    }
                    if (next.State.CurrentState == target) entered = true;
                    if (entered && next.State.CurrentState == 0 && !exited) { exited = true; exits++; }
                    previous = next.State; committed = firstSync; inputs = firstInputs; eventsBefore = nextEvents; frames++; eventsTotal += events.Count;

                    AlsGroundedMachineUpdate Prepare()
                    {
                        collector.Begin(identity, committed, inputs); Array.Clear(automatic); automatic[6] = collector.MovingTime();
                        var rules = new AlsGroundedRuleInput { MovementState = frame == 2 ? AlsMovementStateInput.InAir : AlsMovementStateInput.Grounded,
                            Stance = AlsStance.Standing, HasMovementInput = moving && frame >= 3, RelevantLandTimeRemaining = collector.LandRemaining() };
                        var update = AlsGroundedStateMachine.Update(machine, previous, rules, automatic, context.Weight, context.Delta, frame);
                        foreach (var state in new[] { 3, 6 }) if ((update.ClearCachedWeightStates & (1 << state)) != 0) collector.ClearWeights(state);
                        for (var i = 0; i < update.InitializationCount; i++)
                        { var state = update.GetInitialization(i); if (state is 3 or 6) collector.Initialize(state, sink); }
                        for (var i = 0; i < update.UpdateCount; i++)
                        {
                            var child = update.GetUpdate(i); if (child.State is not (3 or 6)) continue;
                            collector.Update(child.State, speed, context.WithWeight(child.Weight).WithState(245, child.State, child.InertializationSync), sink);
                        }
                        for (var i = 0; i < collector.Count; i++) active[i] = collector.Contexts[i].GetState(0).StateIndex == update.State.CurrentState;
                        collector.Frame = AlsSharedSourceBatch.Evaluate(snapshot.CreateCoreView(), collector.Frame,
                            collector.Updates.AsSpan(0, collector.Count), collector.Samples.AsSpan(0, collector.Count), active.AsSpan(0, collector.Count), 9, default, null, context.Delta);
                        return update;
                    }
                }
                Require(entered && exited, "Landing did not exit using its actual player time.");
            }
            Require(eventsTotal > 0 && endpoints > 0 && movingChanged && exits == 18 && retryFailures == 18 && sink.Initializations > 0 && sink.Updates > 0,
                "Landing coverage incomplete.");
            GD.Print($"LANDING_RUNTIME_OK frames={frames} pose_frames={poseFrames} endpoints={endpoints} curves={curveChecks} events={eventsTotal} exits={exits} failures={retryFailures} sources=75 samples=109 clocks=real grounded=reference_fixture demo=not_connected");
        }
        finally { foreach (var clip in clips) clip?.Dispose(); }

        void CheckAllOuterSources()
        {
            var updates = new List<AlsLocomotionSourceUpdate>(); var sampleUpdates = new List<AlsLocomotionSampleUpdate>(); var candidate = default(AlsCycleSyncFrame);
            foreach (var player in players.Where(p => p.Domain == AlsLocomotionSourceDomain.MainMovement))
            {
                candidate.Epochs[player.PlayerId] = 1; candidate.Times[player.PlayerId] = player.StartPosition;
                if (player.Kind == AlsLocomotionSourceKind.TeleportEvaluator) continue;
                updates.Add(new(player.PlayerId, 1, player.StartPosition, .5f, sampleUpdates.Count, player.SampleCount));
                // Controlled source-weight fixture, not falling Lean's as-yet-unexported native grid.
                for (var i = 0; i < player.SampleCount; i++) sampleUpdates.Add(new(player.SampleStart + i, 1f / player.SampleCount, 1));
            }
            var result = AlsSharedSourceBatch.Evaluate(snapshot.CreateCoreView(), candidate, updates.ToArray(), sampleUpdates.ToArray(),
                Enumerable.Repeat(true, updates.Count).ToArray(), 9, default, null, 1f / 60);
            Require(result.PlayerCount == 9 && result.SampleCount == 17 && result.GroupCount == 8, "Outer source transaction layout differs.");
            foreach (var player in players.Where(p => p.Domain == AlsLocomotionSourceDomain.MainMovement && p.Kind == AlsLocomotionSourceKind.TeleportEvaluator))
            {
                Require(result.Times[player.PlayerId] == 0, "Prediction evaluator advanced its clock.");
                for (var i = 0; i < result.NotifyTickCount; i++) Require(samples[result.NotifyTicks[i].SampleId].PlayerId != player.PlayerId, "Prediction evaluator emitted a notify tick.");
            }
        }
    }
    private sealed class GroundedFixture(int read) : IAlsLandingGroundedUpdateSink
    {
        public int Initializations, Updates;
        public void InitializeGrounded(int node, AlsFrameIdentity identity) { Require(node == read && identity.SlotGeneration == 3, "Wrong Grounded initialization."); Initializations++; }
        public void UpdateGrounded(int node, in AlsPoseUpdateContext context) { Require(node == read && context.GetState(0).StateIndex == 6, "Wrong Grounded update."); Updates++; }
    }
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
