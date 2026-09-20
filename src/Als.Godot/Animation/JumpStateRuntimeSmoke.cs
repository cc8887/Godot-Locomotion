using Godot;
using GodotAls.Assets;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace GodotAls.Animation;

public partial class JumpStateRuntimeSmoke : Node
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
        var sources = AlsLocomotionSourceCompiler.CompileWithJump(Read("v4_main_movement_graph.json"), set, locomotion.SkeletonId);
        var profile = AlsJumpStateCompiler.Compile(Read("v4_main_movement_graph.json"), Read("v4_pose_cache_graph.json"), sources, set);
        var p5 = AlsP5aAnimationRuntimeProfileCompiler.Compile(Read("p5a_animation_runtime.json"), set);
        var inventory = AlsP5SourceInventoryCompiler.Compile(Read("v4_anim_graph_inventory.json"), set, sources);
        var layout = AlsP5OccurrenceLayoutCompiler.CompileSourceAware(locomotion, turns, p5, sources, inventory);
        var snapshot = AlsP5CoreRuntimeBindingCompiler.CompileSourceAware(set, locomotion, turns, p5, layout, sources, inventory);
        using var library = AlsAnimationLibraryBuilder.Build(set, locomotion, turns, sourceProfile: sources); AddChild(library.Root);
        using var poseGraph = new AlsJumpPoseGraph(library, set, sources, profile.Pose);
        var runtime = new AlsJumpStateRuntime(profile.Runtime); var collector = new AlsJumpSourceCollector(sources, profile.Runtime);
        var rest = new AlsLocalPose[library.Skeleton.GetBoneCount()];
        for (var i = 0; i < rest.Length; i++)
        {
            var t = library.Skeleton.GetBoneRest(i); var q = t.Basis.GetRotationQuaternion(); var s = t.Basis.Scale;
            rest[i] = new(new NVector3(t.Origin.X, t.Origin.Y, t.Origin.Z), new NQuaternion(q.X, q.Y, q.Z, q.W), new NVector3(s.X, s.Y, s.Z));
        }
        var pose = new AlsLocalPose[rest.Length]; var retryPose = new AlsLocalPose[rest.Length];
        var curveNames = sources.Players.Where(p => p.Domain == AlsLocomotionSourceDomain.Jump)
            .SelectMany(p => set.Animations[sources.Samples[p.SampleStart].AnimationId].Curves.Select(c => c.SourceName))
            .Append("AbsentJumpProbe").Distinct().ToArray();
        var curves = new AlsInertialCurve[curveNames.Length]; var retryCurves = new AlsInertialCurve[curveNames.Length];
        var sourceCount = sources.RuntimePlayers.Length;
        var active = new bool[6]; var frames = 0; var eventCount = 0; var resets = 0; var failures = 0; var seen = 0; var nonRest = false;
        CheckRelevantTime();
        foreach (var hz in new[] { 30, 60, 120 }) foreach (var foot in new[] { -1f, 1f }) foreach (var direction in new[] { -1f, 1f })
        {
            var previous = default(AlsJumpStateFrame); var committed = default(AlsCycleSyncFrame);
            var rates = default(AlsJumpPlayerRates); var eventState = default(AlsP5SourceEventState);
            committed.Epochs[0] = 19; committed.Times[0] = .25f; committed.CachedWeights[0] = .8f;
            for (var frame = 1; frame <= hz * 8; frame++)
            {
                if (frame >= hz * 5 && frame < hz * 5 + 3) continue;
                var identity = new AlsFrameIdentity(frame, 7, 2); var delta = 1f / hz;
                var parentReset = frame == hz * 3 || frame == hz * 6;
                var speed = frame < hz * 3 ? 3.5f : frame < hz * 6 ? 0 : 7;
                var rate = direction * (frame % hz < hz / 2 ? .8f : 1.4f);
                var context = new AlsPoseUpdateContext(identity, .8f, delta, .35f)
                    .WithState(profile.ParentMachineNodeIndex, 2).WithInertialization(999, true);
                if (frame % 11 == 0) context = context.AsInactive();
                var before = committed;
                // Inject late failures before publication. The same identity must remain retryable.
                if (frame == hz || frame == hz * 2)
                {
                    try { Prepare(frame == hz ? 1 : 2, pose, curves); throw new InvalidOperationException("Fault was not raised."); }
                    catch (InjectedFailure) { failures++; }
                    Require(SameSources(before, committed), "Failed candidate published source history.");
                }
                var first = Prepare(0, pose, curves); var firstSync = collector.Frame; var firstRates = collector.Rates;
                var firstInitializations = collector.InitializationCount;
                var binding = snapshot.CreateCoreView();
                Require(AlsP5Runtime.TryPrepareSourceEvents(binding, identity, delta,
                    ((ReadOnlySpan<AlsP5SourceNotifyTick>)firstSync.NotifyTicks)[..firstSync.NotifyTickCount], 1, eventState,
                    out var nextEvents, out var events, out var failure), $"Jump event preparation failed: {failure}");
                var retry = Prepare(0, retryPose, retryCurves);
                Require(first.State.Inputs == retry.State.Inputs && first.State.Machine.CurrentState == retry.State.Machine.CurrentState &&
                    first.State.Machine.Transitions.Latest == retry.State.Machine.Transitions.Latest &&
                    SameSources(firstSync, collector.Frame) && SameRates(firstRates, collector.Rates) &&
                    pose.AsSpan().SequenceEqual(retryPose) && curves.AsSpan().SequenceEqual(retryCurves), "Jump retry changed state, clocks, rates, pose or curves.");
                var retrySync = collector.Frame;
                Require(AlsP5Runtime.TryPrepareSourceEvents(binding, identity, delta,
                    ((ReadOnlySpan<AlsP5SourceNotifyTick>)retrySync.NotifyTicks)[..retrySync.NotifyTickCount], 1, eventState,
                    out var retryEvents, out var retried, out failure) && retryEvents.RandomSeed == nextEvents.RandomSeed &&
                    events.Count == retried.Count, "Jump event retry changed state.");
                for (var i = 0; i < events.Count; i++) Require(events[i] == retried[i], "Jump event identity changed on retry.");
                Require(committed.Epochs[0] == firstSync.Epochs[0] && committed.Times[0] == firstSync.Times[0] &&
                    committed.CachedWeights[0] == firstSync.CachedWeights[0], "Jump changed an unrelated source.");
                for (var i = 0; i < collector.Count; i++)
                {
                    var path = collector.Contexts[i];
                    Require(path.StateCount == 2 && path.GetState(0) == context.GetState(0) &&
                        path.GetState(1).MachineNodeIndex == profile.Runtime.MachineNodeIndex && path.RootMotionWeight == .35f &&
                        path.IsActive == context.IsActive && path.InertializationRequester == 999, "Jump lost its ancestor update context.");
                }
                if (parentReset || first.Machine.Reinitialized)
                {
                    Require(first.State.Machine.CurrentState == (foot < 0 ? 1 : 2) && firstInitializations == 2,
                        "Jump reentry skipped takeoff or duplicated initialization."); resets++;
                }
                foreach (var bone in pose) Require(float.IsFinite(bone.Position.LengthSquared()) &&
                    MathF.Abs(bone.Rotation.LengthSquared() - 1) < .0001f, "Invalid Jump bone.");
                nonRest |= !pose.AsSpan().SequenceEqual(rest); seen |= 1 << first.State.Machine.CurrentState;
                previous = first.State; committed = firstSync; rates = firstRates; eventState = nextEvents;
                frames++; eventCount += events.Count;

                AlsJumpStateUpdate Prepare(int fault, AlsLocalPose[] output, AlsInertialCurve[] outputCurves)
                {
                    AlsSharedSourceBatch.Validate(snapshot.CreateCoreView(), committed);
                    collector.Begin(identity, committed, rates, rate);
                    var rules = new AlsGroundedRuleInput { FeetPosition = foot,
                        RelevantJumpLeftTimeRemaining = collector.ObserveRemaining(1), RelevantJumpRightTimeRemaining = collector.ObserveRemaining(2) };
                    var start = parentReset ? runtime.Initialize(previous, identity, collector).State : previous;
                    var update = runtime.Update(start, rules, speed, context, collector);
                    for (var i = 0; i < collector.Count; i++)
                    {
                        var path = collector.Contexts[i];
                        active[i] = path.IsActive && path.GetState(1).StateIndex == update.State.Machine.CurrentState;
                    }
                    collector.Frame = AlsSharedSourceBatch.Evaluate(snapshot.CreateCoreView(), collector.Frame,
                        collector.Updates.AsSpan(0, collector.Count), collector.Samples.AsSpan(0, collector.Count),
                        active.AsSpan(0, collector.Count), 9, default, null, delta, rate);
                    if (fault == 1) throw new InjectedFailure();
                    ReadOnlySpan<float> times = ((ReadOnlySpan<float>)collector.Frame.Times)[..sourceCount];
                    poseGraph.Compose(update.State.Machine, update.State.Inputs, times, rest, output);
                    for (var i = 0; i < curveNames.Length; i++) outputCurves[i] = poseGraph.Curve(update.State.Machine, update.State.Inputs, times, curveNames[i]);
                    if (fault == 2) throw new InjectedFailure();
                    return update;
                }
            }
        }
        Require((seen & 22) == 22 && nonRest && resets > 0 && failures == 24, "Jump integration coverage missing.");
        GD.Print($"JUMP_STATE_RUNTIME_OK frames={frames} events={eventCount} resets={resets} failures={failures} states={seen} retry=identical clocks=real demo=not_connected");

        void CheckRelevantTime()
        {
            var frame = default(AlsCycleSyncFrame); var cached = default(AlsJumpPlayerRates);
            var id = profile.Runtime.WalkLeft; var run = profile.Runtime.RunLeft;
            var length = sources.Samples[sources.Players[id].SampleStart].DurationSeconds;
            frame.Epochs[id] = frame.Epochs[run] = 1; frame.CachedWeights[id] = frame.CachedWeights[run] = .4f;
            frame.Times[id] = .1f; frame.Times[run] = .2f; cached[0] = -2; cached[1] = 2;
            collector.Begin(new(1, 7, 2), frame, cached, 3);
            Require(MathF.Abs(collector.ObserveRemaining(1) - .1f) < 1e-6f,
                "Relevant time used current input, divided by rate, or lost baked tie order.");
            cached[0] = 2; collector.Begin(new(1, 7, 2), frame, cached, -3);
            Require(collector.ObserveRemaining(1) == length - .1f, "Positive cached rate did not preserve asset-time remaining.");
            collector.ClearSourceWeights(2);
            Require(collector.ObserveRemaining(1) == float.MaxValue, "Cleared weights exposed stale remaining time.");
        }
    }

    private sealed class InjectedFailure : Exception;
    private static bool SameRates(in AlsJumpPlayerRates a, in AlsJumpPlayerRates b)
    { for (var i = 0; i < 6; i++) if (a[i] != b[i]) return false; return true; }
    private static bool SameSources(in AlsCycleSyncFrame a, in AlsCycleSyncFrame b)
    {
        if (!StandingCycleSmoke.SameSync(a, b) || a.NotifyTickCount != b.NotifyTickCount) return false;
        for (var i = 0; i < AlsCycleSyncFrame.PlayerCapacity; i++)
            if (a.Times[i] != b.Times[i] || a.Epochs[i] != b.Epochs[i] || a.CachedWeights[i] != b.CachedWeights[i]) return false;
        for (var i = 0; i < a.NotifyTickCount; i++) if (a.NotifyTicks[i] != b.NotifyTicks[i]) return false;
        return true;
    }
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
