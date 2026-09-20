using Godot;
using GodotAls.Assets;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace GodotAls.Animation;

public partial class CrouchingCycleSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void Run()
    {
        var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
        var locomotion = AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"), set);
        var snapshot = AlsLocomotionGraphBuilder.CompileSourceBindings(set, locomotion);
        var sourceProfile = AlsLocomotionSourceCompiler.Compile(Read("v4_locomotion_source_graph.json"), set, locomotion.SkeletonId);
        var profile = AlsCrouchingCycleCompiler.Compile(Read("v4_locomotion_source_graph.json"), Read("v4_pose_cache_graph.json"),
            Read("v4_locomotion_curves.json"), Read("v4_lean_sampling.json"), sourceProfile, set);
        using var library = AlsAnimationLibraryBuilder.Build(set, locomotion); AddChild(library.Root);
        var runtime = new AlsCrouchingCycleRuntime(profile.Runtime);
        using var directLean = new AlsLeanPoseSampler(library, set, sourceProfile, profile.Lean);
        var directClips = new AlsLocalPoseClip?[7]; var directCurves = new AlsCurveSampler[7];
        var directCurveIds = new Dictionary<string, int>[7];
        var players = sourceProfile.Players; var samples = sourceProfile.Samples;
        var bones = library.Skeleton.GetBoneCount(); var output = new AlsLocalPose[bones]; var retryOutput = new AlsLocalPose[bones];
        var directionCaches = new AlsLocalPose[bones * 6]; var directionScratch = new AlsLocalPose[bones * 6];
        var directionOutput = new AlsLocalPose[bones]; var walk = new AlsLocalPose[bones]; var stride = new AlsLocalPose[bones];
        var diagonal = new AlsLocalPose[bones]; var scratch = new AlsLocalPose[bones]; var expected = new AlsLocalPose[bones];
        var mask = new bool[bones]; var parents = Enumerable.Range(0, bones).Select(library.Skeleton.GetBoneParent).ToArray();
        var physical = set.Skeletons[profile.SkeletonId].PhysicalBones;
        for (var i = 0; i < bones; i++) mask[i] = profile.Direction.ProfileBones[Array.FindIndex(physical,
            b => string.Equals(b.Name, library.Skeleton.GetBoneName(i), StringComparison.OrdinalIgnoreCase))];
        var diagonalBone = Enumerable.Range(0, bones).Single(i => string.Equals(library.Skeleton.GetBoneName(i),
            physical[profile.Diagonal.PhysicalBoneId].Name, StringComparison.OrdinalIgnoreCase));
        var sink = new SourceSink(sourceProfile, profile.Lean); var retrySink = new SourceSink(sourceProfile, profile.Lean);
        var ticks = new AlsAssetSyncPlayer[8]; var sampleTicks = new AlsAssetSyncSample[11]; var groupIds = new int[8];
        var contexts = new AlsAssetPlayerTickContext[8]; var sourceSeconds = new float[samples.Length]; var leanSeconds = new float[5];
        var cacheCurveValues = new float[6]; var frames = 0; var resets = 0; var directionSkips = 0; var cachedEvaluations = 0;
        var curveChecks = 0; var eventCount = 0; var rejectedPoses = 0; long poseAllocated = -1;
        try
        {
            for (var i = 0; i < 7; i++)
            {
                var player = players[i == 6 ? profile.Runtime.WalkPosePlayerId : profile.Direction.PlayerIds[i]];
                var animation = set.Animations[samples[player.SampleStart].AnimationId];
                directClips[i] = new(library.Library.GetAnimation(library.ClipNames[animation.Id]), library.Skeleton, ownsAnimation: false);
                directCurves[i] = new(animation.Curves); directCurveIds[i] = animation.Curves.ToDictionary(c => c.SourceName, c => c.CurveId);
            }
            VerifyInitialization(library, set, sourceProfile, profile, directClips[6]!, directCurves[6], directCurveIds[6], parents, diagonalBone);
            foreach (var hz in new[] { 30, 60, 120 })
            {
                using var poseGraph = new AlsCrouchingCyclePoseGraph(library, set, sourceProfile, profile);
                var rest = poseGraph.ReferencePose.ToArray(); var previous = default(AlsCrouchingCycleState);
                var committed = default(AlsCycleSyncFrame); var eventsCommitted = default(AlsP5SourceEventState);
                var curveNames = poseGraph.CurveNames.ToArray(); var firstCurves = new float[curveNames.Length];
                var evaluation = default(AlsGraphTraversalCounter); var initialization = new AlsGraphTraversalCounter(0, 0);
                var boneCounter = new AlsGraphTraversalCounter(0, 0);
                for (var frame = 1; frame <= hz * 6; frame++)
                {
                    if (frame >= hz * 2 && frame < hz * 2 + 4) continue;
                    var identity = new AlsFrameIdentity(frame, 0, 1); var delta = 1f / hz;
                    evaluation = evaluation.Next((ulong)frame);
                    if (frame == hz * 4) boneCounter = boneCounter.Next((ulong)frame);
                    var context = new AlsPoseUpdateContext(identity, .65f, delta).WithState(900, 1);
                    var quarter = (frame - 1) * 4 / hz;
                    var strideInput = (quarter % 8) switch { 0 or 1 => 0f, 2 or 3 => 1.4f, 4 or 5 => .4f, _ => -.5f };
                    var rule = new AlsGroundedRuleInput(true, false, false, AlsStance.Crouching, true, false, 1, 0)
                        { MovementDirection = (AlsMovementDirection)(quarter % 4), FeetCrossing = 1 };
                    var velocity = (frame - 1) / hz % 2 == 0 ? NVector4.UnitX : new NVector4(.1f, .2f, .3f, .4f);
                    var input = new AlsCrouchingCyclePoseInputs(velocity, new(10, 20, 30, 40),
                        quarter % 4 / 3f, new NVector2(MathF.Sin(frame * .04f), MathF.Cos(frame * .05f)));
                    var rate = (frame < hz * 3 ? .75f : -.75f) * (hz == 120 ? -1 : 1);
                    sink.Begin(committed, rate, input.Lean);
                    var update = runtime.Prepare(previous, rule, strideInput, input.Velocity, context, sink, input.DiagonalAlpha);
                    Tick(sink);
                    ReadSeconds(sink.Frame);
                    var bindings = snapshot.CreateCoreView();
                    Require(AlsP5Runtime.TryPrepareSourceEvents(bindings, identity, delta, ((ReadOnlySpan<AlsP5SourceNotifyTick>)sink.Frame.NotifyTicks)[..sink.Frame.NotifyTickCount],
                        1, eventsCommitted, out var eventsCandidate, out var events, out _), "Cycles event candidate failed.");
                    poseGraph.Prepare(identity, update, input, sourceSeconds, initialization, boneCounter, evaluation, output);
                    Require(poseGraph.CachedSourceEvaluations <= 6 && (update.DirectionUpdated || poseGraph.CachedSourceEvaluations == 0), "Cycles cache reevaluated a source.");
                    cachedEvaluations += poseGraph.CachedSourceEvaluations;
                    for (var i = 0; i < curveNames.Length; i++) firstCurves[i] = poseGraph.Curve(curveNames[i]);
                    Direct(update, input, rest);
                    Require(output.AsSpan().SequenceEqual(expected), "Whole Cycles cached pose differs from direct source composition.");
                    foreach (var pose in output)
                        Require(float.IsFinite(pose.Position.LengthSquared()) && float.IsFinite(pose.Scale.LengthSquared()) &&
                            MathF.Abs(pose.Rotation.LengthSquared() - 1) < .0001f, "Cycles output has invalid transforms.");
                    for (var i = 0; i < curveNames.Length; i++)
                    {
                        var name = curveNames[i];
                        for (var d = 0; d < 6; d++)
                        {
                            cacheCurveValues[d] = 0;
                            if (directCurveIds[d].TryGetValue(name, out var id))
                                directCurves[d].TrySample(id, sourceSeconds[players[profile.Direction.PlayerIds[d]].SampleStart], out cacheCurveValues[d]);
                        }
                        var direction = update.DirectionUpdated ? AlsCrouchingDirectionPose.Curve(profile.Direction.Rows, update.State.Direction,
                            cacheCurveValues, input.Velocity, input.Yaw, name == "YawOffset") : 0;
                        var walkCurve = 0f;
                        if (directCurveIds[6].TryGetValue(name, out var walkId)) directCurves[6].TrySample(walkId, players[profile.Runtime.WalkPosePlayerId].StartPosition, out walkCurve);
                        var value = directLean.Curve(leanSeconds, input.Lean, name, AlsCrouchingStride.Curve(update.State.Stride, walkCurve, direction));
                        Require(firstCurves[i] == value, "Whole Cycles curve order differs from direct composition."); curveChecks++;
                    }
                    retrySink.Begin(committed, rate, input.Lean);
                    var retry = runtime.Prepare(previous, rule, strideInput, input.Velocity, context, retrySink, input.DiagonalAlpha);
                    Tick(retrySink); Require(SameSources(sink.Frame, retrySink.Frame), "Cycles source initialization/sync retry differs.");
                    ReadSeconds(retrySink.Frame);
                    Require(AlsP5Runtime.TryPrepareSourceEvents(bindings, identity, delta, ((ReadOnlySpan<AlsP5SourceNotifyTick>)retrySink.Frame.NotifyTicks)[..retrySink.Frame.NotifyTickCount],
                        1, eventsCommitted, out var eventsRetry, out var retriedEvents, out _) && eventsCandidate.RandomSeed == eventsRetry.RandomSeed && events.Count == retriedEvents.Count,
                        "Cycles source event retry differs.");
                    for (var i = 0; i < events.Count; i++) Require(events[i] == retriedEvents[i], "Cycles event identity changed on retry.");
                    poseGraph.Prepare(identity, retry, input, sourceSeconds, initialization, boneCounter, evaluation, retryOutput);
                    Require(output.AsSpan().SequenceEqual(retryOutput), "Cycles pose retry differs.");
                    for (var i = 0; i < curveNames.Length; i++) Require(firstCurves[i] == poseGraph.Curve(curveNames[i]), "Cycles curve retry differs.");
                    if (frame == hz + 2)
                    {
                        var sample = samples[players[profile.Direction.PlayerIds[0]].SampleStart];
                        var originalTime = sourceSeconds[sample.SampleId];
                        sourceSeconds[sample.SampleId] = MathF.BitIncrement(sample.DurationSeconds);
                        var failed = false; var refusedCommit = false;
                        try { poseGraph.Prepare(identity, retry, input, sourceSeconds, initialization, boneCounter, evaluation, retryOutput); }
                        catch (ArgumentException) { failed = true; }
                        try { poseGraph.Commit(identity); }
                        catch (InvalidOperationException) { refusedCommit = true; }
                        Require(failed && refusedCommit, "Faulted Cycles cache was accepted for commit.");
                        sourceSeconds[sample.SampleId] = originalTime;
                        poseGraph.Prepare(identity, retry, input, sourceSeconds, initialization, boneCounter, evaluation, retryOutput);
                        Require(output.AsSpan().SequenceEqual(retryOutput), "Faulted Cycles cache contaminated the next candidate.");
                        for (var i = 0; i < curveNames.Length; i++) Require(firstCurves[i] == poseGraph.Curve(curveNames[i]), "Faulted cache contaminated curves.");
                        rejectedPoses++;
                    }
                    if (frame == hz + 1)
                    {
                        for (var i = 0; i < 50; i++) poseGraph.Prepare(identity, retry, input, sourceSeconds, initialization, boneCounter, evaluation, retryOutput);
                        var before = GC.GetAllocatedBytesForCurrentThread();
                        for (var i = 0; i < 100; i++)
                        {
                            poseGraph.Prepare(identity, retry, input, sourceSeconds, initialization, boneCounter, evaluation, retryOutput);
                            foreach (var name in curveNames) poseGraph.Curve(name);
                        }
                        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                        Require(allocated == 0, $"Cycles active pose/curve evaluation allocated {allocated} bytes."); poseAllocated = allocated;
                    }
                    Require(update.InitializedSources == (frame == 1 || frame == hz * 2 + 4), "Cycles source reset occurred at the wrong relevance boundary.");
                    if (update.InitializedSources) resets++;
                    if (!update.DirectionUpdated) directionSkips++;
                    var epoch = frame < hz * 2 ? 1 : 2;
                    foreach (var id in profile.Direction.PlayerIds) Require(sink.Frame.Epochs[id] == epoch, "Direction reentry reset an outer source.");
                    poseGraph.Commit(identity); previous = update.State; committed = sink.Frame; eventsCommitted = eventsCandidate;
                    eventCount += events.Count; frames++;

                    void Tick(SourceSink collector)
                    {
                        var binding = snapshot.CreateCoreView(); var source = binding.Sources;
                        Require(AlsLocomotionSourceRuntime.TryBuildTicks(source, source.Stamp, collector.Updates.AsSpan(0, collector.PlayerCount),
                            collector.Samples.AsSpan(0, collector.SampleCount), 1, ticks, sampleTicks, groupIds, out _, crouchingPlayRate: rate), "Cycles source tick binding failed.");
                        Span<AlsAssetPlayerHistory> next = collector.Frame.Players; Span<AlsAssetSampleHistory> nextSamples = collector.Frame.Samples;
                        Span<AlsAssetSyncBatchGroupHistory> nextGroups = collector.Frame.Groups;
                        Require(AlsSyncRuntime.TryEvaluateAssetSyncBatch(source.GroupIds, groupIds.AsSpan(0, collector.PlayerCount), ticks.AsSpan(0, collector.PlayerCount),
                            sampleTicks.AsSpan(0, collector.SampleCount), source.Sequences, source.Markers,
                            ((ReadOnlySpan<AlsAssetSyncBatchGroupHistory>)committed.Groups)[..committed.GroupCount],
                            ((ReadOnlySpan<AlsAssetPlayerHistory>)committed.Players)[..committed.PlayerCount],
                            ((ReadOnlySpan<AlsAssetSampleHistory>)committed.Samples)[..committed.SampleCount], delta, nextGroups, next, nextSamples, out _, contexts), "Cycles shared sync failed.");
                        collector.Frame.PlayerCount = collector.PlayerCount; collector.Frame.SampleCount = collector.SampleCount;
                        collector.Frame.GroupCount = source.GroupIds.Length;
                        for (var i = 0; i < collector.PlayerCount; i++) collector.Frame.Times[next[i].PlayerId] = next[i].Time;
                        Require(AlsP5Runtime.TryBuildSourceNotifyTicks(binding, ticks.AsSpan(0, collector.PlayerCount), sampleTicks.AsSpan(0, collector.SampleCount),
                            next[..collector.PlayerCount], nextSamples[..collector.SampleCount], contexts.AsSpan(0, collector.PlayerCount),
                            collector.Frame.NotifyTicks, out collector.Frame.NotifyTickCount, out _), "Cycles source notify mapping failed.");
                        for (var i = 0; i < collector.Frame.NotifyTickCount; i++)
                        {
                            var tick = collector.Frame.NotifyTicks[i]; var player = samples[tick.SampleId].PlayerId;
                            for (var n = 0; n < collector.PlayerCount; n++)
                                if (collector.Updates[n].PlayerId == player)
                                    collector.Frame.NotifyTicks[i] = tick with { ActiveContext = collector.Contexts[n].StateCount < 3 ||
                                        collector.Contexts[n].GetState(2).StateIndex == update.State.Direction.CurrentState };
                        }
                    }
                }

                void ReadSeconds(in AlsCycleSyncFrame frame)
                {
                    foreach (var player in players.Where(p => p.Domain == AlsLocomotionSourceDomain.Crouching))
                        for (var i = 0; i < player.SampleCount; i++)
                            sourceSeconds[player.SampleStart + i] = player.Kind == AlsLocomotionSourceKind.BlendSpace
                                ? frame.Times[player.PlayerId] * samples[player.SampleStart + i].DurationSeconds : frame.Times[player.PlayerId];
                    for (var i = 0; i < frame.SampleCount; i++) sourceSeconds[frame.Samples[i].SampleId] = frame.Samples[i].Time;
                    for (var i = 0; i < 5; i++) leanSeconds[i] = sourceSeconds[profile.Lean.SampleStart + i];
                }
            }
            Require(frames == 1248 && resets == 6 && directionSkips > 0 && cachedEvaluations > 0 && eventCount > 0 && poseAllocated == 0 && rejectedPoses == 3,
                $"Whole Cycles coverage incomplete: frames={frames} resets={resets} skipped={directionSkips} cached={cachedEvaluations} events={eventCount} allocated={poseAllocated}.");
            GD.Print($"CROUCHING_CYCLES_OK frames={frames} root_initializations={resets} direction_skips={directionSkips} cached_evaluations={cachedEvaluations} curve_checks={curveChecks} events={eventCount} rejected_poses={rejectedPoses} allocated={poseAllocated} rates=30,60,120 source_epoch=owned reverse_initialization=covered sync=shared pose_cache=scoped retry=identical demo=not_connected");
        }
        finally { foreach (var clip in directClips) clip?.Dispose(); }

        void Direct(in AlsCrouchingCycleUpdate update, in AlsCrouchingCyclePoseInputs input, AlsLocalPose[] rest)
        {
            for (var i = 0; i < 6; i++)
            {
                var player = players[profile.Direction.PlayerIds[i]]; var sample = samples[player.SampleStart];
                directClips[i]!.SampleSourceSeconds(rest, sourceSeconds[sample.SampleId], sample.DurationSeconds, directionCaches.AsSpan(i * bones, bones));
            }
            if (update.DirectionUpdated) AlsCrouchingDirectionPose.Compose(profile.Direction.Rows, profile.Runtime.Direction.Machine, update.State.Direction,
                directionCaches, rest, mask, input.Velocity, directionScratch, directionOutput);
            var walkPlayer = players[profile.Runtime.WalkPosePlayerId];
            directClips[6]!.SampleSourceSeconds(rest, walkPlayer.StartPosition, samples[walkPlayer.SampleStart].DurationSeconds, walk);
            AlsCrouchingStride.Compose(update.State.Stride, walk, directionOutput, stride);
            AlsDiagonalScalePose.Apply(stride, parents, diagonalBone, profile.Diagonal.Scale, input.DiagonalAlpha, scratch, diagonal);
            directLean.Compose(leanSeconds, input.Lean, diagonal, expected);
        }
    }

    private static void VerifyInitialization(AlsAnimationLibraryBuildResult library, AlsAnimationSetDefinition set,
        AlsLocomotionSourceProfile sources, AlsCrouchingCycleProfile profile, AlsLocalPoseClip walkClip,
        AlsCurveSampler walkCurves, Dictionary<string, int> walkCurveIds, int[] parents, int diagonalBone)
    {
        var frames = 0; var rejected = 0; var delayedUpdates = 0;
        foreach (var hz in new[] { 30, 60, 120 })
        {
            using var graph = new AlsCrouchingCyclePoseGraph(library, set, sources, profile);
            var runtime = new AlsCrouchingCycleRuntime(profile.Runtime); var sink = new InitializationSink();
            var initial = runtime.Initialize(default, new(1, 0, 1), sink);
            var counter = new AlsGraphTraversalCounter(0, 0); var evaluation = counter;
            var rest = graph.ReferencePose.ToArray(); var output = new AlsLocalPose[rest.Length];
            var expected = new AlsLocalPose[rest.Length]; var walk = new AlsLocalPose[rest.Length]; var scratch = new AlsLocalPose[rest.Length];
            var times = new float[sources.Samples.Length]; var player = sources.Players[profile.Runtime.WalkPosePlayerId];
            walkClip.SampleSourceSeconds(rest, player.StartPosition, sources.Samples[player.SampleStart].DurationSeconds, walk);
            var inputs = new AlsCrouchingCyclePoseInputs(NVector4.One, new(10, 20, 30, 40), 1, new(1, -1));
            // A newly initialized additive BlendSpace must not read the prior cycle's samples.
            Array.Fill(times, float.NaN);
            for (var frame = 1; frame <= hz; frame++)
            {
                var identity = new AlsFrameIdentity(frame, 0, 1); evaluation = evaluation.Next((ulong)frame);
                graph.Prepare(identity, initial, inputs, times, counter, counter, evaluation, output);
                Require(output.AsSpan().SequenceEqual(walk), "Cold Cycles pose must be WalkPose without control or Lean.");
                foreach (var name in graph.CurveNames)
                {
                    var value = 0f; if (walkCurveIds.TryGetValue(name, out var id)) walkCurves.TrySample(id, player.StartPosition, out value);
                    Require(graph.Curve(name) == value, "Unupdated Cycle used stale additive curves.");
                }
                graph.Commit(identity); frames++;
            }
            var rules = new AlsGroundedRuleInput(true, false, false, AlsStance.Crouching, true, false, 1, 0) { FeetCrossing = 1 };
            var updated = runtime.Prepare(initial.State, rules, 1, NVector4.One, new(new(hz + 1, 0, 1), 1, 1f / hz), sink, .6f);
            Require(!updated.InitializedSources && sink.Count == 8, "Delayed first Update initialized sources twice.");
            // A separate consumer has never evaluated the Initialize result. It must still initialize
            // its inner caches from the persistent epoch, although this Update has no source flag.
            using var delayed = new AlsCrouchingCyclePoseGraph(library, set, sources, profile);
            Array.Clear(times); var updatedInputs = inputs with { DiagonalAlpha = .6f };
            evaluation = evaluation.Next((ulong)(hz + 1));
            delayed.Prepare(updated.State.Identity, updated, updatedInputs, times, counter, counter, evaluation, output);
            Require(delayed.CacheInitializations > 0, "Deferred first pose missed inner cache initialization.");
            var initializedCaches = delayed.CacheInitializations;
            var invalid = updatedInputs with { DiagonalAlpha = .2f }; var failed = false; var refusedCommit = false;
            try { delayed.Prepare(updated.State.Identity, updated, invalid, times, counter, counter, evaluation, expected); }
            catch (ArgumentException) { failed = true; }
            try { delayed.Commit(updated.State.Identity); } catch (InvalidOperationException) { refusedCommit = true; }
            Require(failed && refusedCommit, "Failed initial candidate committed its epoch.");
            delayed.Prepare(updated.State.Identity, updated, updatedInputs, times, counter, counter, evaluation, expected);
            Require(delayed.CacheInitializations == initializedCaches && output.AsSpan().SequenceEqual(expected), "Initial pose retry lost epoch or pose.");
            delayed.Commit(updated.State.Identity); rejected++; delayedUpdates++;

            var reset = runtime.Initialize(updated.State, new(hz + 2, 0, 1), sink);
            Array.Fill(times, float.NaN); evaluation = evaluation.Next((ulong)(hz + 2));
            delayed.Prepare(reset.State.Identity, reset, inputs, times, counter, counter, evaluation, output);
            AlsDiagonalScalePose.Apply(walk, parents, diagonalBone, profile.Diagonal.Scale, .6f, scratch, expected);
            Require(output.AsSpan().SequenceEqual(expected), "Reinitialized Cycle lost cached control alpha or sampled stale Lean.");
            delayed.Commit(reset.State.Identity);
        }
        Require(frames == 210 && delayedUpdates == 3 && rejected == 3, "Cycles initialization coverage incomplete.");
        GD.Print($"CROUCHING_CYCLES_INITIALIZATION_OK initial_frames={frames} delayed_updates={delayedUpdates} failed_retries={rejected} cold_alpha=0 retained_alpha=0.6 stale_lean=ignored");
    }

    private sealed class InitializationSink : IAlsCrouchingCycleUpdateSink
    {
        public int Count;
        public void InitializeSource(int playerId) => Count++;
        public void UpdateSource(int playerId, in AlsPoseUpdateContext context) { }
        public void OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped) { }
    }

    private static bool SameSources(in AlsCycleSyncFrame a, in AlsCycleSyncFrame b)
    {
        if (a.PlayerCount != b.PlayerCount || a.SampleCount != b.SampleCount || a.GroupCount != b.GroupCount) return false;
        for (var i = 0; i < AlsCycleSyncFrame.PlayerCapacity; i++) if (a.Times[i] != b.Times[i] || a.Epochs[i] != b.Epochs[i]) return false;
        for (var i = 0; i < a.PlayerCount; i++) if (a.Players[i] != b.Players[i]) return false;
        for (var i = 0; i < a.SampleCount; i++) if (a.Samples[i] != b.Samples[i]) return false;
        for (var i = 0; i < a.GroupCount; i++) if (a.Groups[i] != b.Groups[i]) return false;
        return true;
    }

    private sealed class SourceSink(AlsLocomotionSourceProfile sources, AlsLeanSamplingProfile lean) : IAlsCrouchingCycleUpdateSink
    {
        public AlsCycleSyncFrame Frame;
        public readonly AlsLocomotionSourceUpdate[] Updates = new AlsLocomotionSourceUpdate[8];
        public readonly AlsLocomotionSampleUpdate[] Samples = new AlsLocomotionSampleUpdate[11];
        public readonly AlsPoseUpdateContext[] Contexts = new AlsPoseUpdateContext[8];
        private readonly float[] _weights = new float[5]; private readonly int[] _order = new int[5];
        private float _rate; private int _leanCount;
        public int PlayerCount, SampleCount;
        public void Begin(in AlsCycleSyncFrame committed, float rate, NVector2 leanInput)
        { Frame = committed; PlayerCount = SampleCount = 0; _rate = rate; _leanCount = lean.Runtime.Evaluate(leanInput, _weights, _order); }
        public void InitializeSource(int playerId)
        {
            var player = sources.Players[playerId]; var sample = sources.Samples[player.SampleStart];
            Frame.Epochs[playerId] = AlsAssetSourceInitialization.NextEpoch(Frame.Epochs[playerId]); Frame.CachedWeights[playerId] = 0;
            Frame.Times[playerId] = player.Kind == AlsLocomotionSourceKind.TeleportEvaluator ? player.StartPosition :
                AlsAssetSourceInitialization.Time(player.Kind == AlsLocomotionSourceKind.BlendSpace ? AlsAssetSyncKind.BlendSpace : AlsAssetSyncKind.Sequence,
                    player.StartPosition, sample.DurationSeconds, player.Kind == AlsLocomotionSourceKind.BlendSpace ? player.DefaultPlayRate : _rate,
                    player.PlayRateBasis, sample.AssetRateScale);
        }
        public void UpdateSource(int playerId, in AlsPoseUpdateContext context)
        {
            var player = sources.Players[playerId]; Frame.CachedWeights[playerId] = context.Weight;
            if (player.Kind == AlsLocomotionSourceKind.TeleportEvaluator) return;
            var count = player.Kind == AlsLocomotionSourceKind.BlendSpace ? _leanCount : 1;
            Updates[PlayerCount] = new(playerId, Frame.Epochs[playerId], Frame.Times[playerId], context.Weight, SampleCount, count, context.InertializationSync);
            Contexts[PlayerCount++] = context;
            for (var i = 0; i < count; i++)
            {
                var index = player.Kind == AlsLocomotionSourceKind.BlendSpace ? _order[i] : 0;
                Samples[SampleCount++] = new(player.SampleStart + index, player.Kind == AlsLocomotionSourceKind.BlendSpace ? _weights[index] : 1, 1);
            }
        }
        public void OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped) =>
            throw new InvalidOperationException("Cycles fixture has no skipped-update handler.");
    }
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
