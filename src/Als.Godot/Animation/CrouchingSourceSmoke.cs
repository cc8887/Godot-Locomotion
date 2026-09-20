using Godot;
using GodotAls.Assets;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

public partial class CrouchingSourceSmoke : Node
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
        var bindings = snapshot.CreateCoreView(); var source = bindings.Sources;
        var sourceText = Read("v4_locomotion_source_graph.json");
        var sourceProfile = AlsLocomotionSourceCompiler.Compile(sourceText, set, locomotion.SkeletonId);
        var directionProfile = AlsCrouchingDirectionPoseCompiler.Compile(sourceText, Read("v4_pose_cache_graph.json"),
            Read("v4_locomotion_curves.json"), sourceProfile, set);
        var directionMachine = AlsGroundedMachineCompiler.CompileGrounded(sourceText).CrouchingDirection!.Runtime;
        var directionGraph = new AlsCrouchingDirectionGraph(directionProfile.UpdateGraph);
        var cacheSink = new CacheSink(); var cacheRetrySink = new CacheSink();
        var cacheFrames = 0; var skippedDirectionFrames = 0; var cacheCalls = 0; var cacheSourceUpdates = 0;
        var strideProfile = AlsCrouchingStrideCompiler.Compile(sourceText, sourceProfile, set);
        var leanProfile = AlsLeanSamplingCompiler.Compile(Read("v4_lean_sampling.json"), sourceProfile, set, AlsLocomotionSourceDomain.Crouching);
        var diagonalProfile = AlsCrouchingDiagonalScaleCompiler.Compile(sourceText, sourceProfile, set);
        var directionRows = directionProfile.Rows.ToArray(); var cachePlayers = directionProfile.PlayerIds.ToArray();
        using var library = AlsAnimationLibraryBuilder.Build(set, locomotion); AddChild(library.Root);
        using var reference = new AlsDetailPoseSampler(library, set,
            AlsLocomotionDetailCompiler.Compile(Read("v4_locomotion_detail_graph.json"), set, locomotion.SkeletonId));
        using var leanSampler = new AlsLeanPoseSampler(library, set, sourceProfile, leanProfile);
        var rest = reference.ReferencePose.ToArray(); var output = new AlsLocalPose[rest.Length]; var retryPose = new AlsLocalPose[rest.Length];
        var players = source.Players.ToArray().Where(p => p.Domain == AlsLocomotionSourceDomain.Crouching).ToArray();
        var timed = players.Where(p => p.Kind != AlsLocomotionSourceKind.TeleportEvaluator).ToArray();
        Require(players.Length == 13 && timed.Length == 9, "Crouching source coverage differs.");
        var clips = new AlsLocalPoseClip?[source.Samples.Length];
        var firstPoses = new AlsLocalPose[source.Samples.Length * rest.Length];
        var moved = new bool[source.Samples.Length]; var initialized = new bool[source.Samples.Length];
        var sourceFrames = 0; var sampled = 0; var emitted = 0;
        var directionCaches = new AlsLocalPose[rest.Length * 6]; var directionScratch = new AlsLocalPose[directionCaches.Length];
        var directionOutput = new AlsLocalPose[rest.Length]; var directionRetry = new AlsLocalPose[rest.Length];
        var walkPose = new AlsLocalPose[rest.Length]; var strideOutput = new AlsLocalPose[rest.Length]; var strideRetry = new AlsLocalPose[rest.Length];
        var diagonalOutput = new AlsLocalPose[rest.Length]; var diagonalRetry = new AlsLocalPose[rest.Length]; var diagonalScratch = new AlsLocalPose[rest.Length];
        var diagonalParents = Enumerable.Range(0, rest.Length).Select(library.Skeleton.GetBoneParent).ToArray();
        var diagonalBoneName = set.Skeletons[diagonalProfile.SkeletonId].PhysicalBones[diagonalProfile.PhysicalBoneId].Name;
        var diagonalBone = Enumerable.Range(0, rest.Length).Single(i => string.Equals(library.Skeleton.GetBoneName(i), diagonalBoneName, StringComparison.OrdinalIgnoreCase));
        var diagonalChanges = 0; var diagonalBypassed = 0;
        float[] diagonalInputs = [-1, 0, .00001f, .25f, .5f, .75f, 1, 2];
        var leanOutput = new AlsLocalPose[rest.Length]; var leanRetry = new AlsLocalPose[rest.Length];
        var leanWeights = new float[5]; var leanOrder = new int[5]; var leanSeconds = new float[5];
        System.Numerics.Vector2[] leanInputs = [new(0, 0), new(0, 1), new(0, -1), new(-1, 0), new(1, 0), new(.3f, -.4f), new(-.8f, .9f), new(1.2f, -1.2f)];
        var leanSamplesUsed = 0; var leanChanges = 0; var leanDirectChecks = 0;
        var cacheBySample = Enumerable.Repeat(-1, source.Samples.Length).ToArray(); var directionMask = new bool[rest.Length];
        var cacheCurves = new AlsCurveSampler[6]; var cacheCurveIds = new Dictionary<string, int>[6]; var curveValues = new float[6];
        var sourcePlayers = source.Players.ToArray(); var sourceSamples = source.Samples.ToArray();
        var walkPosePlayer = sourcePlayers[strideProfile.WalkPosePlayerId];
        var walkPoseAnimation = set.Animations[sourceSamples[walkPosePlayer.SampleStart].AnimationId];
        var walkPoseCurves = new AlsCurveSampler(walkPoseAnimation.Curves);
        var walkPoseCurveIds = walkPoseAnimation.Curves.ToDictionary(c => c.SourceName, c => c.CurveId);
        for (var i = 0; i < cachePlayers.Length; i++)
        {
            var sample = sourceSamples[sourcePlayers[cachePlayers[i]].SampleStart]; cacheBySample[sample.SampleId] = i;
            var animation = set.Animations[sample.AnimationId]; cacheCurves[i] = new(animation.Curves);
            cacheCurveIds[i] = animation.Curves.ToDictionary(c => c.SourceName, c => c.CurveId);
        }
        for (var physical = 0; physical < directionProfile.ProfileBones.Length; physical++)
        {
            var name = set.Skeletons[locomotion.SkeletonId].PhysicalBones[physical].Name;
            var bone = Enumerable.Range(0, rest.Length).Single(i => string.Equals(library.Skeleton.GetBoneName(i), name, StringComparison.OrdinalIgnoreCase));
            directionMask[bone] = directionProfile.ProfileBones[physical];
        }
        var directionStates = 0; var directionTransitions = 0; var directionInterrupts = 0;
        var strideWalkOnly = 0; var strideDirectionOnly = 0; var strideMixed = 0;
        try
        {
            foreach (var player in players)
            for (var index = 0; index < player.SampleCount; index++)
            {
                var sample = source.Samples[player.SampleStart + index];
                clips[sample.SampleId] = new(library.Library.GetAnimation(library.ClipNames[sample.AnimationId]), library.Skeleton, ownsAnimation: false);
                Require(Math.Abs(clips[sample.SampleId]!.Length - sample.DurationSeconds) < .000001,
                    "Crouching library duration differs from the shared source table.");
                clips[sample.SampleId]!.SampleSourceSeconds(rest, 0, sample.DurationSeconds, output);
                clips[sample.SampleId]!.SampleSourceSeconds(rest, sample.DurationSeconds, sample.DurationSeconds, output);
                var rejected = false;
                try { clips[sample.SampleId]!.SampleSourceSeconds(rest, MathF.BitIncrement(sample.DurationSeconds), sample.DurationSeconds, output); }
                catch (ArgumentException) { rejected = true; }
                Require(rejected, "Crouching source accepted a time beyond its float endpoint.");
            }
            foreach (var hz in new[] { 30, 60, 120 })
            {
                var machineState = default(AlsGroundedMachineState);
                var strideState = default(AlsCrouchingStrideState);
                AlsMovementDirection[] movements = [AlsMovementDirection.Forward, AlsMovementDirection.Right, AlsMovementDirection.Backward,
                    AlsMovementDirection.Right, AlsMovementDirection.Left, AlsMovementDirection.Backward, AlsMovementDirection.Left, AlsMovementDirection.Forward];
                var sampleCount = timed.Sum(p => p.SampleCount);
                var updates = new AlsLocomotionSourceUpdate[timed.Length]; var sampleUpdates = new AlsLocomotionSampleUpdate[sampleCount];
                var ticks = new AlsAssetSyncPlayer[timed.Length]; var sampleTicks = new AlsAssetSyncSample[sampleCount]; var groups = new int[timed.Length];
                var previous = new AlsAssetPlayerHistory[timed.Length]; var next = new AlsAssetPlayerHistory[timed.Length];
                var previousSamples = new AlsAssetSampleHistory[sampleCount]; var nextSamples = new AlsAssetSampleHistory[sampleCount];
                var previousGroups = new AlsAssetSyncBatchGroupHistory[source.GroupIds.Length]; var nextGroups = new AlsAssetSyncBatchGroupHistory[source.GroupIds.Length];
                var contexts = new AlsAssetPlayerTickContext[timed.Length]; var notifyTicks = new AlsP5SourceNotifyTick[sampleCount];
                var times = new float[source.Samples.Length]; var eventState = default(AlsP5SourceEventState);
                var playerTimes = sourcePlayers.Select(p => p.StartPosition).ToArray();
                var previousPlayerCount = 0; var previousSampleCount = 0;
                rest.CopyTo(directionOutput, 0); rest.CopyTo(directionRetry, 0);
                for (var frame = 1; frame <= 2 * hz; frame++)
                {
                    var strideInput = ((frame - 1) * 4 / (2 * hz)) switch { 0 => 0f, 1 => 1.5f, 2 => .3f, _ => -.5f };
                    var stride = AlsCrouchingStride.Advance(strideState, strideInput, 1f / hz, strideProfile.Settings);
                    var strideRetried = AlsCrouchingStride.Advance(strideState, strideInput, 1f / hz, strideProfile.Settings);
                    Require(stride == strideRetried, "Crouching stride filter retry differs.");
                    var input = new AlsGroundedRuleInput(true, false, false, AlsStance.Crouching, true, false, 1, 0)
                        { MovementDirection = movements[(frame - 1) * 16 / (2 * hz) % movements.Length], FeetCrossing = 1 };
                    var velocity = new System.Numerics.Vector4(.1f, .2f, .3f, .4f);
                    var direction = default(AlsGroundedMachineUpdate); var directionRetried = default(AlsGroundedMachineUpdate);
                    cacheSink.Reset(); cacheRetrySink.Reset();
                    if (stride.DirectionRelevant)
                    {
                        var context = new AlsPoseUpdateContext(new(frame, 0, 1), stride.DirectionUpdateWeight, 1f / hz);
                        direction = directionGraph.Prepare(machineState, input, velocity, context, cacheSink);
                        directionRetried = directionGraph.Prepare(machineState, input, velocity, context, cacheRetrySink);
                        Require(cacheSink.Count == cacheRetrySink.Count && direction.EventCount == directionRetried.EventCount,
                            "Crouching cached update retry changed ownership/events.");
                        for (var i = 0; i < cacheSink.Count; i++)
                            Require(cacheSink.Players[i] == cacheRetrySink.Players[i] &&
                                cacheSink.Contexts[i].Weight == cacheRetrySink.Contexts[i].Weight &&
                                cacheSink.Contexts[i].GetState(0) == cacheRetrySink.Contexts[i].GetState(0), "Crouching cache winner retry differs.");
                        cacheFrames++; cacheCalls += directionGraph.CachedCallCount; cacheSourceUpdates += directionGraph.SourceUpdateCount;
                    }
                    else skippedDirectionFrames++;
                    var leanInput = leanInputs[(frame - 1) * leanInputs.Length / (2 * hz)];
                    var leanCount = leanProfile.Runtime.Evaluate(leanInput, leanWeights, leanOrder);
                    foreach (var i in leanOrder.AsSpan(0, leanCount)) leanSamplesUsed |= 1 << i;
                    var activePlayers = 0; var activeSamples = 0;
                    // Rotate and Lean remain independently driven component fixtures. Only the six
                    // direction sources below are collected from the native cached update traversal.
                    foreach (var player in timed)
                    {
                        if (cachePlayers.Contains(player.PlayerId)) continue;
                        updates[activePlayers++] = new(player.PlayerId, 1, playerTimes[player.PlayerId], 1, activeSamples, player.SampleCount);
                        for (var n = 0; n < player.SampleCount; n++)
                            sampleUpdates[activeSamples++] = new(player.SampleStart + n, player.PlayerId == leanProfile.PlayerId ? leanWeights[n] : 1, 1);
                    }
                    for (var i = 0; i < cacheSink.Count; i++)
                    {
                        var player = source.Players[cacheSink.Players[i]];
                        updates[activePlayers++] = new(player.PlayerId, 1, playerTimes[player.PlayerId], cacheSink.Contexts[i].Weight, activeSamples, 1);
                        sampleUpdates[activeSamples++] = new(player.SampleStart, 1, 1);
                    }
                    Require(stride.DirectionRelevant || activePlayers == 3, "Inactive direction branch still submitted source ticks.");
                    Require(AlsLocomotionSourceRuntime.TryBuildTicks(source, source.Stamp, updates.AsSpan(0, activePlayers), sampleUpdates.AsSpan(0, activeSamples), 3.7f,
                        ticks, sampleTicks, groups, out _, new(1.2f, true, true), .75f), "Crouching shared tick binding failed.");
                    Require(AlsSyncRuntime.TryEvaluateAssetSyncBatch(source.GroupIds, groups.AsSpan(0, activePlayers), ticks.AsSpan(0, activePlayers),
                        sampleTicks.AsSpan(0, activeSamples), source.Sequences, source.Markers,
                        frame == 1 ? [] : previousGroups, previous.AsSpan(0, previousPlayerCount), previousSamples.AsSpan(0, previousSampleCount),
                        1f / hz, nextGroups, next, nextSamples, out _, contexts), "Crouching shared Sync failed.");
                    Require(AlsP5Runtime.TryBuildSourceNotifyTicks(bindings, ticks.AsSpan(0, activePlayers), sampleTicks.AsSpan(0, activeSamples),
                        next.AsSpan(0, activePlayers), nextSamples.AsSpan(0, activeSamples), contexts.AsSpan(0, activePlayers),
                        notifyTicks, out var notifyCount, out _), "Crouching notify mapping failed.");
                    Require(notifyCount == activePlayers, "Crouching highest-weighted Lean notify ownership differs.");
                    for (var i = 0; i < notifyCount; i++)
                    {
                        var playerId = source.Samples[notifyTicks[i].SampleId].PlayerId;
                        for (var n = 0; n < cacheSink.Count; n++)
                            if (cacheSink.Players[n] == playerId)
                                notifyTicks[i] = notifyTicks[i] with { ActiveContext = cacheSink.Contexts[n].GetState(0).StateIndex == direction.State.CurrentState };
                    }
                    Require(AlsP5Runtime.TryPrepareSourceEvents(bindings, new(frame, 0, 1), 1f / hz, notifyTicks.AsSpan(0, notifyCount), 1,
                        eventState, out var candidate, out var events, out _), "Crouching event candidate failed.");
                    Require(AlsP5Runtime.TryPrepareSourceEvents(bindings, new(frame, 0, 1), 1f / hz, notifyTicks.AsSpan(0, notifyCount), 1,
                        eventState, out var retry, out var retriedEvents, out _) && candidate.RandomSeed == retry.RandomSeed && events.Count == retriedEvents.Count,
                        "Crouching event retry differs.");
                    for (var i = 0; i < events.Count; i++) Require(events[i] == retriedEvents[i], "Crouching event identity differs on retry.");
                    emitted += events.Count; eventState = candidate;
                    foreach (var sample in nextSamples.AsSpan(0, activeSamples)) times[sample.SampleId] = sample.Time;
                    foreach (var player in players)
                    for (var n = 0; n < player.SampleCount; n++)
                    {
                        var id = player.SampleStart + n;
                        var time = player.Kind == AlsLocomotionSourceKind.TeleportEvaluator ? player.StartPosition : times[id];
                        clips[id]!.SampleSourceSeconds(rest, time, source.Samples[id].DurationSeconds, output);
                        if (cacheBySample[id] >= 0) output.CopyTo(directionCaches.AsSpan(cacheBySample[id] * rest.Length, rest.Length));
                        if (id == walkPosePlayer.SampleStart) output.CopyTo(walkPose, 0);
                        clips[id]!.SampleSourceSeconds(rest, time, source.Samples[id].DurationSeconds, retryPose);
                        Require(output.AsSpan().SequenceEqual(retryPose), "Crouching pose sample retry differs.");
                        foreach (var pose in output)
                            Require(float.IsFinite(pose.Position.LengthSquared()) && float.IsFinite(pose.Scale.LengthSquared()) &&
                                float.IsFinite(pose.Rotation.LengthSquared()) && MathF.Abs(pose.Rotation.LengthSquared() - 1) < .0001f,
                                "Crouching sample has invalid transforms.");
                        var first = firstPoses.AsSpan(id * rest.Length, rest.Length);
                        if (!initialized[id]) { output.CopyTo(first); initialized[id] = true; }
                        else moved[id] |= !output.AsSpan().SequenceEqual(first);
                        sampled++;
                    }
                    if (stride.DirectionRelevant)
                    {
                        AlsCrouchingDirectionPose.Compose(directionRows, directionMachine, direction.State, directionCaches, rest,
                            directionMask, velocity, directionScratch, directionOutput);
                        AlsCrouchingDirectionPose.Compose(directionRows, directionMachine, directionRetried.State, directionCaches, rest,
                            directionMask, velocity, directionScratch, directionRetry);
                    }
                    Require(directionOutput.AsSpan().SequenceEqual(directionRetry), "Crouching direction pose candidate retry differs.");
                    foreach (var pose in directionOutput)
                        Require(float.IsFinite(pose.Position.LengthSquared()) && float.IsFinite(pose.Scale.LengthSquared()) &&
                            float.IsFinite(pose.Rotation.LengthSquared()) && MathF.Abs(pose.Rotation.LengthSquared() - 1) < .0001f,
                            "Crouching directional composition has invalid transforms.");
                    for (var i = 0; i < cachePlayers.Length; i++)
                    {
                        curveValues[i] = 0;
                        if (cacheCurveIds[i].TryGetValue("HipOrientation_Bias", out var id))
                            cacheCurves[i].TrySample(id, times[sourcePlayers[cachePlayers[i]].SampleStart], out curveValues[i]);
                    }
                    var yaw = new System.Numerics.Vector4(10, 20, 30, 40);
                    var curve = stride.DirectionRelevant ? AlsCrouchingDirectionPose.Curve(directionRows, direction.State, curveValues, velocity, yaw, false) : 0;
                    var curveRetry = stride.DirectionRelevant ? AlsCrouchingDirectionPose.Curve(directionRows, directionRetried.State, curveValues, velocity, yaw, false) : 0;
                    Require(float.IsFinite(curve) && curve == curveRetry, "Crouching directional curve retry differs.");
                    AlsCrouchingStride.Compose(stride, walkPose, directionOutput, strideOutput);
                    AlsCrouchingStride.Compose(strideRetried, walkPose, directionRetry, strideRetry);
                    Require(strideOutput.AsSpan().SequenceEqual(strideRetry), "Crouching stride pose retry differs.");
                    if (!stride.DirectionRelevant)
                    {
                        Require(strideOutput.AsSpan().SequenceEqual(walkPose), "Crouching stride lost its fixed WalkPose branch."); strideWalkOnly++;
                    }
                    else if (!stride.WalkPoseRelevant)
                    {
                        Require(strideOutput.AsSpan().SequenceEqual(directionOutput), "Crouching stride lost its full direction branch."); strideDirectionOnly++;
                    }
                    else strideMixed++;
                    foreach (var pose in strideOutput)
                        Require(float.IsFinite(pose.Position.LengthSquared()) && float.IsFinite(pose.Scale.LengthSquared()) &&
                            float.IsFinite(pose.Rotation.LengthSquared()) && MathF.Abs(pose.Rotation.LengthSquared() - 1) < .0001f,
                            "Crouching stride has invalid transforms.");
                    var walkCurve = 0f;
                    if (walkPoseCurveIds.TryGetValue("HipOrientation_Bias", out var walkCurveId))
                        walkPoseCurves.TrySample(walkCurveId, walkPosePlayer.StartPosition, out walkCurve);
                    Require(AlsCrouchingStride.Curve(stride, walkCurve, curve) == AlsCrouchingStride.Curve(strideRetried, walkCurve, curveRetry),
                        "Crouching stride curve retry differs.");
                    strideState = stride;
                    var diagonalAlpha = diagonalInputs[(frame - 1) * diagonalInputs.Length / (2 * hz)];
                    AlsDiagonalScalePose.Apply(strideOutput, diagonalParents, diagonalBone, diagonalProfile.Scale, diagonalAlpha, diagonalScratch, diagonalOutput);
                    AlsDiagonalScalePose.Apply(strideOutput, diagonalParents, diagonalBone, diagonalProfile.Scale, diagonalAlpha, diagonalScratch, diagonalRetry);
                    Require(diagonalOutput.AsSpan().SequenceEqual(diagonalRetry), "Crouching diagonal candidate retry differs.");
                    if (diagonalAlpha <= AlsPoseBlender.WeightThreshold)
                    {
                        Require(diagonalOutput.AsSpan().SequenceEqual(strideOutput), "Inactive diagonal control changed the local pose."); diagonalBypassed++;
                    }
                    else
                    {
                        Require(diagonalOutput[diagonalBone].Scale.X > strideOutput[diagonalBone].Scale.X, "Active diagonal did not scale the foot root.");
                        diagonalChanges++;
                    }
                    for (var i = 0; i < 5; i++) leanSeconds[i] = times[leanProfile.SampleStart + i];
                    leanSampler.Compose(leanSeconds, leanInput, diagonalOutput, leanOutput);
                    leanSampler.Compose(leanSeconds, leanInput, diagonalOutput, leanRetry);
                    Require(leanOutput.AsSpan().SequenceEqual(leanRetry), "Lean borrowed-source pose retry differs.");
                    foreach (var pose in leanOutput)
                        Require(float.IsFinite(pose.Position.LengthSquared()) && float.IsFinite(pose.Scale.LengthSquared()) &&
                            float.IsFinite(pose.Rotation.LengthSquared()) && MathF.Abs(pose.Rotation.LengthSquared() - 1) < .0001f,
                            "Lean composition has invalid transforms.");
                    if (!leanOutput.AsSpan().SequenceEqual(diagonalOutput)) leanChanges++;
                    if (leanCount == 1)
                    {
                        var id = leanProfile.SampleStart + leanOrder[0];
                        clips[id]!.SampleSourceSeconds(rest, times[id], source.Samples[id].DurationSeconds, output);
                        for (var bone = 0; bone < rest.Length; bone++)
                        {
                            var additivePose = AlsLocalAdditivePose.Difference(output[bone], leanSampler.AdditiveBasePose[bone]);
                            var expected = AlsLocalAdditivePose.Apply(diagonalOutput[bone], AlsPoseBlender.Normalize(additivePose));
                            Require(expected == leanOutput[bone], "Lean one-sample pose used the wrong additive reference/order.");
                        }
                        leanDirectChecks++;
                    }
                    Require(leanSampler.Curve(leanSeconds, leanInput, "HipOrientation_Bias", curve) ==
                        leanSampler.Curve(leanSeconds, leanInput, "HipOrientation_Bias", curve), "Lean curve retry differs.");
                    if (stride.DirectionRelevant)
                    {
                        var stack = direction.State.Transitions;
                        var expectedYaw = yaw[directionRows[stack.Count == 0 ? stack.CurrentState : stack.GetTransition(0).From].YawAxis];
                        for (var i = 0; i < stack.Count; i++)
                        {
                            var edge = stack.GetTransition(i);
                            expectedYaw = expectedYaw * (1 - edge.Alpha) + yaw[directionRows[edge.To].YawAxis] * edge.Alpha;
                        }
                        Require(expectedYaw == AlsCrouchingDirectionPose.Curve(directionRows, direction.State, curveValues, velocity, yaw, true),
                            "Crouching directional YawOffset used the wrong channel or profile weight.");
                        directionStates |= 1 << direction.State.CurrentState; directionTransitions += direction.TransitionCount;
                        if (direction.State.Transitions.Count > 1) directionInterrupts++;
                        machineState = direction.State;
                    }
                    // Shared Sync returns group-ordered histories, not the submitted player order.
                    for (var i = 0; i < activePlayers; i++) playerTimes[next[i].PlayerId] = next[i].Time;
                    previousPlayerCount = activePlayers; previousSampleCount = activeSamples;
                    (previous, next) = (next, previous); (previousSamples, nextSamples) = (nextSamples, previousSamples);
                    (previousGroups, nextGroups) = (nextGroups, previousGroups); sourceFrames++;
                }
            }
            var walks = players.Where(p => p.PlayRateInput == AlsSourceRateInput.CrouchingPlayRate).ToArray();
            Require(walks.Length == 6 && walks.All(p => moved[p.SampleStart]) && emitted > 0, "Crouching playback did not exercise motion/events.");
            Require(directionStates == 63 && directionTransitions > 0 && directionInterrupts > 0,
                $"Crouching direction coverage incomplete: states={directionStates} transitions={directionTransitions} interrupted={directionInterrupts}.");
            Require(strideWalkOnly > 0 && strideDirectionOnly > 0 && strideMixed > 0, "Crouching stride branch coverage incomplete.");
            Require(leanSamplesUsed == 31 && leanChanges > 0 && leanDirectChecks > 0, "Lean real resource coverage incomplete.");
            Require(diagonalChanges > 0 && diagonalBypassed > 0, "Diagonal control coverage incomplete.");
            Require(cacheFrames > 0 && skippedDirectionFrames > 0 && cacheCalls > cacheSourceUpdates, "Crouching cached update/relevance coverage incomplete.");
            GD.Print($"CROUCHING_CACHE_OK frames={cacheFrames} skipped={skippedDirectionFrames} reads={cacheCalls} source_updates={cacheSourceUpdates} weights=native_max order=compiled sync=shared retry=identical lifecycle=partial demo=not_connected");
            GD.Print($"CROUCHING_DIAGONAL_OK frames={sourceFrames} active={diagonalChanges} bypass={diagonalBypassed} order=stride_scale_lean rates=30,60,120 retry=identical cache_update=connected demo=not_connected");
            GD.Print($"CROUCHING_LEAN_OK frames={sourceFrames} samples=5 changes={leanChanges} direct_checks={leanDirectChecks} rates=30,60,120 sample_weights=native_grid times=shared_sync retry=identical diagonal=component demo=not_connected");
            GD.Print($"CROUCHING_STRIDE_OK frames={sourceFrames} walk_only={strideWalkOnly} direction_only={strideDirectionOnly} mixed={strideMixed} rates=30,60,120 retry=identical inputs=fixtures direction_weights=native_cache diagonal=component lean=component demo=not_connected");
            GD.Print($"CROUCHING_DIRECTION_OK states=6 frames={cacheFrames} transitions={directionTransitions} interrupted={directionInterrupts} rates=30,60,120 sources=shared_sync retry=identical source_weights=native_cache cache_update=connected demo=not_connected");
            GD.Print($"CROUCHING_SOURCES_OK players=13 samples=17 timed=9 frames={sourceFrames} pose_samples={sampled} events={emitted} moving_walks=6 rates=30,60,120 sync=shared retry=identical pose_graph=not_connected");
        }
        finally { foreach (var clip in clips) clip?.Dispose(); }
    }
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class CacheSink : IAlsCrouchingDirectionUpdateSink
    {
        public int[] Players { get; } = new int[6];
        public AlsPoseUpdateContext[] Contexts { get; } = new AlsPoseUpdateContext[6];
        public int Count { get; private set; }
        public void Reset() => Count = 0;
        public void UpdateSource(int direction, int playerId, in AlsPoseUpdateContext context)
        { Players[Count] = playerId; Contexts[Count++] = context; }
        public void OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped) =>
            throw new InvalidOperationException("Fixture does not install a skipped-update handler.");
    }
}
