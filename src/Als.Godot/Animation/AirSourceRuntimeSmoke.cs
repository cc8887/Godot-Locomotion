using Godot;
using GodotAls.Assets;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace GodotAls.Animation;

public partial class AirSourceRuntimeSmoke : Node
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
        var json = Read("v4_main_movement_graph.json");
        var sources = AlsLocomotionSourceCompiler.CompileWithMovement(json, set, locomotion.SkeletonId);
        var profile = AlsAirPoseCompiler.Compile(json, Read("v4_pose_cache_graph.json"), Read("v4_falling_lean_sampling.json"), sources, set);
        var p5 = AlsP5aAnimationRuntimeProfileCompiler.Compile(Read("p5a_animation_runtime.json"), set);
        var inventory = AlsP5SourceInventoryCompiler.Compile(Read("v4_anim_graph_inventory.json"), set, sources);
        var layout = AlsP5OccurrenceLayoutCompiler.CompileSourceAware(locomotion, poseProfile, p5, sources, inventory);
        var snapshot = AlsP5CoreRuntimeBindingCompiler.CompileSourceAware(set, locomotion, poseProfile, p5, layout, sources, inventory);
        using var library = AlsAnimationLibraryBuilder.Build(set, locomotion, poseProfile, sourceProfile: sources); AddChild(library.Root);
        using var graph = new AlsAirPoseGraph(library, set, sources, profile);
        var collector = new AlsAirSourceCollector(sources, profile);
        var rest = new AlsLocalPose[library.Skeleton.GetBoneCount()];
        for (var bone = 0; bone < rest.Length; bone++)
        {
            var t = library.Skeleton.GetBoneRest(bone); var q = t.Basis.GetRotationQuaternion(); var s = t.Basis.Scale;
            rest[bone] = new(new NVector3(t.Origin.X, t.Origin.Y, t.Origin.Z), new NQuaternion(q.X, q.Y, q.Z, q.W), new NVector3(s.X, s.Y, s.Z));
        }
        var pose = new AlsLocalPose[rest.Length * 2]; var retryPose = new AlsLocalPose[pose.Length];
        string[] names = ["BasePose_N", "Weight_InAir", "FootLock_L", "FootLock_R", "Feet_Position", "AbsentAirSourceCurve"];
        var curves = new AlsInertialCurve[names.Length * 2]; var retryCurves = new AlsInertialCurve[curves.Length];
        var sampleTimes = new float[sources.Samples.Length]; var active = new bool[11];
        var weights = new float[5]; var order = new int[5];
        var frames = 0; var eventsCount = 0; var resets = 0; var freezes = 0; var mixed = 0; var failures = 0; var seen = 0;
        CheckGuards();
        foreach (var hz in new[] { 30, 60, 120 }) foreach (var foot in new[] { -1f, 1f })
        {
            var committed = default(AlsCycleSyncFrame); var previous = default(AlsAirRuntimeFrame); var eventState = default(AlsP5SourceEventState);
            committed.Epochs[0] = 19; committed.Times[0] = .25f; committed.CachedWeights[0] = .8f;
            for (var frame = 1; frame <= hz * 8; frame++)
            {
                var identity = new AlsFrameIdentity(frame, 11, 2); var delta = 1f / hz; var rate = foot * (frame % hz < hz / 2 ? .8f : 1.4f);
                var primary = frame < hz * 5 ? 2 : 1; var both = frame >= hz * 5;
                var prediction = frame >= hz * 2 && frame < hz * 4 ? 1f : 0f;
                var reset = frame == 1 || frame == hz * 6;
                var baseContext = new AlsPoseUpdateContext(identity, frame % 31 == 0 ? 0 : .8f, delta, .35f).WithInertialization(999, true);
                if (frame % 17 == 0) baseContext = baseContext.AsInactive();
                if (frame == hz || frame == hz * 7)
                {
                    try { Prepare(frame == hz ? 1 : 2, pose, curves); throw new InvalidOperationException("Fault was not injected."); }
                    catch (InjectedFailure) { failures++; }
                }
                var next = Prepare(0, pose, curves); var firstSync = collector.Frame; var didReset = collector.JumpReinitialized;
                var didUpdate = collector.JumpUpdated;
                var binding = snapshot.CreateCoreView();
                Require(AlsP5Runtime.TryPrepareSourceEvents(binding, identity, delta,
                    ((ReadOnlySpan<AlsP5SourceNotifyTick>)firstSync.NotifyTicks)[..firstSync.NotifyTickCount], 1, eventState,
                    out var nextEvents, out var events, out _), "Air source events failed.");
                if (!didUpdate && !reset)
                {
                    Require(next.NestedJump.Identity == previous.NestedJump.Identity && next.NestedJump.Machine.LastUpdateSerial == previous.NestedJump.Machine.LastUpdateSerial,
                        "Full prediction advanced the hidden nested Jump."); freezes++;
                }
                if (didReset && didUpdate) { Require(next.NestedJump.Machine.CurrentState == (foot < 0 ? 1 : 2), "Jump reentry skipped takeoff."); resets++; }
                if (both && collector.Updates.AsSpan(0, collector.Count).ContainsPlayer(profile.Fall.Lean.PlayerId) &&
                    collector.Updates.AsSpan(0, collector.Count).ContainsPlayer(profile.Jump.Lean.PlayerId)) mixed++;
                for (var i = 0; i < collector.Count; i++)
                {
                    var update = collector.Updates[i]; var context = collector.Contexts[i]; var player = sources.Players[update.PlayerId];
                    Require(player.Kind != AlsLocomotionSourceKind.TeleportEvaluator && context.RootMotionWeight == .35f &&
                        context.InertializationRequester == 999 && context.IsActive == baseContext.IsActive && context.GetState(0).MachineNodeIndex == 245,
                        "Air source lost its parent context or ticked an evaluator.");
                    if (player.Domain == AlsLocomotionSourceDomain.Jump)
                        Require(context.StateCount == 2 && context.GetState(1).MachineNodeIndex == 263, "Nested source lost Jump context.");
                    if (player.Kind == AlsLocomotionSourceKind.BlendSpace)
                    {
                        var lean = update.PlayerId == profile.Fall.Lean.PlayerId ? profile.Fall.Lean : profile.Jump.Lean;
                        lean.Runtime.Evaluate(update.PlayerId == profile.Fall.Lean.PlayerId ? next.Fall.Lean : next.Jump.Lean, weights, order);
                        for (var s = 0; s < 5; s++) Require(collector.Samples[update.SampleStart + s].Weight == weights[s], "Air Lean submitted guessed sample weights.");
                    }
                }
                var retried = Prepare(0, retryPose, retryCurves);
                Require(SameState(next, retried) && SameSources(firstSync, collector.Frame) && pose.AsSpan().SequenceEqual(retryPose) &&
                    curves.AsSpan().SequenceEqual(retryCurves), "Air candidate retry changed history, clocks, pose or curves.");
                var retrySync = collector.Frame;
                Require(AlsP5Runtime.TryPrepareSourceEvents(binding, identity, delta,
                    ((ReadOnlySpan<AlsP5SourceNotifyTick>)retrySync.NotifyTicks)[..retrySync.NotifyTickCount], 1, eventState,
                    out var retryEvents, out var retryEventBatch, out _) && retryEvents.RandomSeed == nextEvents.RandomSeed && retryEventBatch.Count == events.Count,
                    "Air event retry changed state.");
                for (var i = 0; i < events.Count; i++) Require(events[i] == retryEventBatch[i], "Air event identity changed.");
                Require(firstSync.Epochs[0] == 19 && firstSync.Times[0] == .25f && firstSync.CachedWeights[0] == .8f, "Air changed an unrelated source.");
                previous = next; committed = firstSync; eventState = nextEvents; frames++; eventsCount += events.Count;
                seen |= 1 << next.NestedJump.Machine.CurrentState;

                AlsAirRuntimeFrame Prepare(int fault, AlsLocalPose[] output, AlsInertialCurve[] outputCurves)
                {
                    AlsSharedSourceBatch.Validate(snapshot.CreateCoreView(), committed); collector.Begin(identity, committed, previous, rate);
                    if (reset)
                    {
                        collector.ClearWeights(1); collector.ClearWeights(2); collector.Initialize(1); collector.Initialize(2);
                        Require(collector.InitializationOrder.AsSpan(0, collector.InitializationCount).SequenceEqual(new[] { 63, 64, 62, 67, 65, 66, -263, 68, 69, 70 }),
                            "Air Initialize order differs from A/B graph traversal.");
                    }
                    var rules = new AlsGroundedRuleInput { FeetPosition = foot };
                    if (both) collector.Update(1, -8, .2f, new NVector2(.3f, -.4f), 3.5f, rules,
                        baseContext.WithWeight(baseContext.Weight * .4f).WithState(245, 1));
                    collector.Update(2, -8, prediction, new NVector2(-.7f, .2f), 3.5f, rules,
                        baseContext.WithWeight(baseContext.Weight * (both ? .6f : 1)).WithState(245, 2));
                    for (var i = 0; i < collector.Count; i++)
                    {
                        var context = collector.Contexts[i];
                        active[i] = context.IsActive && context.GetState(0).StateIndex == primary &&
                            (context.StateCount == 1 || context.GetState(1).StateIndex == collector.State.NestedJump.Machine.CurrentState);
                    }
                    collector.Frame = AlsSharedSourceBatch.Evaluate(snapshot.CreateCoreView(), collector.Frame,
                        collector.Updates.AsSpan(0, collector.Count), collector.Samples.AsSpan(0, collector.SampleCount),
                        active.AsSpan(0, collector.Count), 1, default, null, delta, rate);
                    if (fault == 1) throw new InjectedFailure();
                    Array.Clear(sampleTimes); var sync = collector.Frame;
                    for (var i = 0; i < sync.SampleCount; i++) sampleTimes[sync.Samples[i].SampleId] = sync.Samples[i].Time;
                    ReadOnlySpan<float> times = ((ReadOnlySpan<float>)sync.Times)[..sources.Players.Length];
                    var next = collector.State;
                    for (var state = 1; state <= 2; state++)
                    {
                        if (state == 1 && !both) { output.AsSpan(0, rest.Length).Clear(); outputCurves.AsSpan(0, names.Length).Clear(); continue; }
                        var input = state == 1 ? next.Fall : next.Jump;
                        graph.Compose(state, input, next.NestedJump.Machine, next.NestedJump.Inputs, times, sampleTimes, rest, output.AsSpan((state - 1) * rest.Length, rest.Length));
                        for (var c = 0; c < names.Length; c++) outputCurves[(state - 1) * names.Length + c] =
                            graph.Curve(state, input, next.NestedJump.Machine, next.NestedJump.Inputs, times, sampleTimes, names[c]);
                    }
                    if (fault == 2) throw new InjectedFailure();
                    return next;
                }
            }
        }
        Require(frames == 3360 && eventsCount > 0 && resets >= 12 && freezes > 0 && mixed > 0 && (seen & 22) == 22 && failures == 12,
            $"Air runtime coverage missing: frames={frames} events={eventsCount} resets={resets} freezes={freezes} mixed={mixed} seen={seen}.");
        GD.Print($"AIR_SOURCE_RUNTIME_OK frames={frames} events={eventsCount} resets={resets} freezes={freezes} mixed={mixed} failures={failures} states={seen} clocks=shared retry=identical demo=not_connected");

        void CheckGuards()
        {
            var identity = new AlsFrameIdentity(1, 11, 2); var initial = default(AlsCycleSyncFrame);
            var context = new AlsPoseUpdateContext(identity, 1, 1f / 60).WithState(245, 1);
            collector.Begin(identity, initial, default, 1); collector.Initialize(1); collector.Initialize(1);
            Require(collector.Frame.Epochs[profile.Fall.Loop] == 2 && collector.Frame.Epochs[profile.Fall.Heavy] == 2 && collector.InitializationCount == 12,
                "Repeated initialization collapsed source epochs or evaluator identity.");
            Reject(() => collector.Update(2, -8, 0, default, 3.5f, default, context));
            collector.Update(1, -8, 0, default, 3.5f, default, context);
            Reject(() => collector.Update(1, -8, 0, default, 3.5f, default, context));
            Reject(() => collector.Initialize(1));
            var foreign = collector.State;
            Reject(() => collector.Begin(new(2, 12, 2), initial, foreign, 1));
            collector.Begin(identity, initial, default, 1);
            Reject(() => collector.Update(1, -8, 0, default, 3.5f, default, context));
            var overflow = initial; overflow.Epochs[profile.Fall.Loop] = long.MaxValue;
            collector.Begin(identity, overflow, default, 1); Reject(() => collector.Initialize(1));
            collector.Begin(identity, initial, default, 1); collector.Initialize(2);
            Require(collector.InitializationOrder.AsSpan(0, collector.InitializationCount).SequenceEqual(new[] { -263, 68, 69, 70 }),
                "Nested Entry initialization eagerly initialized all takeoff sources.");
            GD.Print("AIR_SOURCE_GUARDS_OK rejected=6 repeated_epochs=2 nested_entry=ordered");
        }
        static void Reject(Action action)
        {
            var rejected = false;
            try { action(); } catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Invalid air owner/update was accepted.");
        }
    }
    private static bool SameState(in AlsAirRuntimeFrame a, in AlsAirRuntimeFrame b)
    {
        if (a.Identity != b.Identity || a.Fall != b.Fall || a.Jump != b.Jump || a.NestedJump.Identity != b.NestedJump.Identity ||
            a.NestedJump.Inputs != b.NestedJump.Inputs || a.NestedJump.Machine.CurrentState != b.NestedJump.Machine.CurrentState ||
            a.NestedJump.Machine.Transitions.Latest != b.NestedJump.Machine.Transitions.Latest) return false;
        for (var i = 0; i < 6; i++) if (a.Rates[i] != b.Rates[i]) return false;
        return true;
    }
    private static bool SameSources(in AlsCycleSyncFrame a, in AlsCycleSyncFrame b)
    {
        if (!StandingCycleSmoke.SameSync(a, b) || a.NotifyTickCount != b.NotifyTickCount) return false;
        for (var i = 0; i < AlsCycleSyncFrame.PlayerCapacity; i++) if (a.Times[i] != b.Times[i] || a.Epochs[i] != b.Epochs[i] || a.CachedWeights[i] != b.CachedWeights[i]) return false;
        for (var i = 0; i < a.NotifyTickCount; i++) if (a.NotifyTicks[i] != b.NotifyTicks[i]) return false;
        return true;
    }
    private sealed class InjectedFailure : Exception;
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

internal static class AirSourceSmokeExtensions
{
    public static bool ContainsPlayer(this Span<AlsLocomotionSourceUpdate> updates, int id)
    { foreach (var update in updates) if (update.PlayerId == id) return true; return false; }
}
