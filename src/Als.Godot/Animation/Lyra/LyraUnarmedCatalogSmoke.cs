using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace GodotAls.Animation.Lyra;

public partial class LyraUnarmedCatalogSmoke : Node
{
    public override void _Ready()
    {
        try
        {
            using var rig = LyraBoundRig.Build(includeAuxiliary: true, includeRemaining: true);
            AddChild(rig.Root);
            var inventory = LyraLinkedLayerInventory.Load();
            if (inventory.Count != 9 || !inventory.Get("unarmed").DisableHandIk ||
                inventory.Get("pistol").DisableHandIk ||
                !inventory.Get("rifle").RaiseWeaponAfterFiringWhenCrouched ||
                !inventory.Get("shotgun").EnableLeftHandPoseOverride ||
                inventory.Get("base").AllAssetPaths.Any())
                throw new InvalidOperationException("Lyra linked-layer inventory is incomplete.");
            var router = new LyraLinkedLayerRouter(rig.Catalog,
                new LyraUnarmedAnimationLayers(rig.Catalog, rig.Auxiliary, rig.Remaining),
                rig.Auxiliary, rig.Remaining);
            var timing = LyraUnarmedTiming.Load(rig.Catalog);
            var notifies = LyraUnarmedNotifies.Load(rig.Catalog);
            var rootMotion = LyraUnarmedRootMotion.Load(rig.Catalog);
            var defaults = LyraUnarmedLayerDefaults.Load();
            var masks = LyraUnarmedLayerMasks.Load(rig.Skeleton);
            var upper = masks.GetWeights("UpperBodyMask");
            var fingers = masks.GetWeights("LeftFingersMask");
            int Bone(string name) => Enumerable.Range(0, rig.Skeleton.GetBoneCount())
                .Single(index => rig.Skeleton.GetBoneName(index).ToString()
                    .Equals(name, StringComparison.OrdinalIgnoreCase));
            if (upper.Length != 68 || fingers.Length != 68 ||
                upper.ToArray().Count(value => value == 1) != 38 ||
                fingers.ToArray().Count(value => value == 1) != 17 ||
                upper[Bone("spine_01")] < 0.09f ||
                fingers[Bone("Hand_L")] != 1 ||
                fingers[Bone("hand_r")] != 0)
                throw new InvalidOperationException("Lyra ALS blend mask mapping differs from UE.");
            var blendWeights = new float[68];
            masks.ScaleWeights("UpperBodyMask", 0.5f, blendWeights);
            var parents = Enumerable.Range(0, 68).Select(rig.Skeleton.GetBoneParent).ToArray();
            var basis = Enumerable.Range(0, 68).Select(_ => new AlsLocalPose(
                NVector3.Zero, NQuaternion.Identity, NVector3.One)).ToArray();
            var layer = basis.ToArray();
            var leftHand = Bone("Hand_L");
            var rightHand = Bone("hand_r");
            layer[leftHand] = layer[leftHand] with { Position = new NVector3(10, 0, 0) };
            layer[rightHand] = layer[rightHand] with { Position = new NVector3(10, 0, 0) };
            var blended = new AlsLocalPose[68];
            AlsMeshSpacePoseBlend.Blend(basis, layer, parents, blendWeights,
                new NQuaternion[68 * 3], blended);
            if (Math.Abs(blended[leftHand].Position.X - 5) > 1e-5 ||
                Math.Abs(blended[rightHand].Position.X - 5) > 1e-5 ||
                blended[Bone("root")].Position != NVector3.Zero)
                throw new InvalidOperationException("Lyra upper-body mesh blend mask failed.");
            if (timing.DistanceCurveCount != 12 || timing.MarkerCount != 84 ||
                timing.PlaybackCount != 21)
                throw new InvalidOperationException("Lyra timing inventory is incomplete.");
            if (notifies.EventCount != 192 || notifies.TransitionStateCount != 4)
                throw new InvalidOperationException("Lyra notify inventory is incomplete.");
            var notifyChecks = 0;
            var stateEnds = 0;
            var scopeClosedStates = 0;
            var notifyBuffer = new LyraQueuedNotify[64];
            foreach (var clip in rig.Catalog.Clips.Values)
            foreach (var entry in notifies.Events(clip.Slot))
            {
                var start = Math.Max(0, entry.TriggerTime - 0.005);
                var end = Math.Min(clip.PlayLength, entry.TriggerTime + 0.005);
                var seed = GodotAls.Core.Events.AlsTimelineRuntime.InitialAssetNotifyRandomSeed;
                var count = notifies.Collect(clip.Slot, start, end - start,
                    ref seed, notifyBuffer);
                if (!notifyBuffer.AsSpan(0, count).Contains(new LyraQueuedNotify(clip.Slot,
                        entry, false)) && !notifyBuffer.AsSpan(0, count).Contains(
                        new LyraQueuedNotify(clip.Slot, entry, true)))
                    throw new InvalidOperationException($"Lyra notify window failed: {clip.Slot}/{entry.Index}.");
                notifyChecks++;
                if (entry.Kind != LyraNotifyKind.TransitionToLocomotion) continue;
                seed = GodotAls.Core.Events.AlsTimelineRuntime.InitialAssetNotifyRandomSeed;
                count = notifies.Collect(clip.Slot, entry.EndTriggerTime - 0.005,
                    0.005, ref seed, notifyBuffer);
                var reachedEnd = notifyBuffer.AsSpan(0, count).Contains(
                    new LyraQueuedNotify(clip.Slot, entry, true));
                if (entry.EndTriggerTime > clip.PlayLength)
                {
                    if (reachedEnd)
                        throw new InvalidOperationException($"Lyra state exceeded clip end: {clip.Slot}.");
                    scopeClosedStates++;
                }
                else
                {
                    if (!reachedEnd)
                        throw new InvalidOperationException($"Lyra notify state end failed: {clip.Slot}.");
                    stateEnds++;
                }
            }
            var reversePoint = notifies.Events("jog_fwd_start")
                .First(entry => entry.Kind == LyraNotifyKind.FootPlantLeft);
            var reverseSeed = GodotAls.Core.Events.AlsTimelineRuntime.InitialAssetNotifyRandomSeed;
            var reverseCount = notifies.Collect("jog_fwd_start",
                reversePoint.TriggerTime + 0.005, -0.01, ref reverseSeed, notifyBuffer);
            if (!notifyBuffer.AsSpan(0, reverseCount).Contains(new LyraQueuedNotify(
                    "jog_fwd_start", reversePoint, true)) ||
                notifyChecks != 192 || stateEnds != 3 || scopeClosedStates != 1)
                throw new InvalidOperationException("Lyra nonlooping notify coverage is incomplete.");
            if (rootMotion.Count != 21 || rig.Catalog.Clips.Values
                    .Where(clip => clip.Slot.EndsWith("_cycle", StringComparison.Ordinal))
                    .Any(clip => rootMotion[clip.Slot].Target.AveragePlanarSpeedCmPerSecond < 200))
                throw new InvalidOperationException("Lyra cycle root motion is incomplete.");
            var allTiming = LyraUnarmedTiming.Load(rig.Catalog, rig.Auxiliary);
            var allNotifies = LyraUnarmedNotifies.Load(rig.Catalog, rig.Auxiliary);
            var allRootMotion = LyraUnarmedRootMotion.Load(rig.Catalog, rig.Auxiliary);
            if (allTiming.DistanceCurveCount != 24 || allTiming.MarkerCount != 159 ||
                allTiming.PlaybackCount != 45 || allNotifies.EventCount != 332 ||
                allNotifies.TransitionStateCount != 8 || allRootMotion.Count != 45)
                throw new InvalidOperationException("Lyra auxiliary metadata is incomplete.");
            var auxiliaryNotifyChecks = 0;
            foreach (var clip in rig.Auxiliary!.Clips.Values)
            foreach (var entry in allNotifies.Events(clip.Slot))
            {
                var seed = GodotAls.Core.Events.AlsTimelineRuntime.InitialAssetNotifyRandomSeed;
                var start = Math.Max(0, entry.TriggerTime - 0.005);
                var end = Math.Min(clip.PlayLength, entry.TriggerTime + 0.005);
                var count = allNotifies.Collect(clip.Slot, start, end - start,
                    ref seed, notifyBuffer);
                if (!notifyBuffer.AsSpan(0, count).Contains(new LyraQueuedNotify(clip.Slot,
                        entry, false)) && !notifyBuffer.AsSpan(0, count).Contains(
                        new LyraQueuedNotify(clip.Slot, entry, true)))
                    throw new InvalidOperationException($"Lyra auxiliary notify failed: {clip.Slot}/{entry.Index}.");
                auxiliaryNotifyChecks++;
            }
            if (auxiliaryNotifyChecks != 140 ||
                rig.Auxiliary.Clips.Values.Where(clip => clip.Slot.StartsWith("crouch_walk_",
                        StringComparison.Ordinal))
                    .Any(clip => allRootMotion[clip.Slot].Target.AveragePlanarSpeedCmPerSecond < 250))
                throw new InvalidOperationException("Lyra auxiliary cycle metadata is incomplete.");
            var expandedTiming = LyraUnarmedTiming.Load(rig.Catalog, rig.Auxiliary, rig.Remaining);
            var expandedNotifies = LyraUnarmedNotifies.Load(rig.Catalog, rig.Auxiliary, rig.Remaining);
            var expandedRootMotion = LyraUnarmedRootMotion.Load(rig.Catalog, rig.Auxiliary, rig.Remaining);
            if (expandedTiming.DistanceCurveCount != 36 || expandedTiming.MarkerCount != 227 ||
                expandedTiming.PlaybackCount != 62 || expandedNotifies.EventCount != 430 ||
                expandedNotifies.TransitionStateCount != 12 || expandedRootMotion.Count != 62)
                throw new InvalidOperationException("Lyra remaining metadata is incomplete.");
            var remainingNotifyChecks = 0;
            foreach (var clip in rig.Remaining!.Clips.Values)
            foreach (var entry in expandedNotifies.Events(clip.Slot))
            {
                var seed = GodotAls.Core.Events.AlsTimelineRuntime.InitialAssetNotifyRandomSeed;
                var start = Math.Max(0, entry.TriggerTime - 0.005);
                var end = Math.Min(clip.PlayLength, entry.TriggerTime + 0.005);
                var count = expandedNotifies.Collect(clip.Slot, start, end - start,
                    ref seed, notifyBuffer);
                if (!notifyBuffer.AsSpan(0, count).Contains(new LyraQueuedNotify(clip.Slot,
                        entry, false)) && !notifyBuffer.AsSpan(0, count).Contains(
                        new LyraQueuedNotify(clip.Slot, entry, true)))
                    throw new InvalidOperationException($"Lyra remaining notify failed: {clip.Slot}/{entry.Index}.");
                remainingNotifyChecks++;
            }
            if (remainingNotifyChecks != 98)
                throw new InvalidOperationException("Lyra remaining notify inventory is incomplete.");
            var crouchSync = allTiming.CreateCycleSync();
            var crouchFirst = crouchSync.Evaluate("crouch_walk_fwd", 0.4, 1, 1.0 / 60);
            crouchSync.Commit(crouchFirst);
            var crouchSwitch = crouchSync.Evaluate("crouch_walk_right", crouchFirst.Time, 1, 1.0 / 60);
            if (!crouchSwitch.MarkerStart.Valid || !crouchSwitch.Group.MarkerEnd.Valid ||
                crouchSwitch != crouchSync.Evaluate("crouch_walk_right", crouchFirst.Time, 1, 1.0 / 60))
                throw new InvalidOperationException("Lyra crouch marker switch failed.");
            crouchSync.Commit(crouchSwitch);
            var standSwitch = crouchSync.Evaluate("walk_fwd_cycle", crouchSwitch.Time, 1, 1.0 / 60);
            if (!standSwitch.MarkerStart.Valid || !standSwitch.Group.MarkerEnd.Valid)
                throw new InvalidOperationException("Lyra standing/crouch marker switch failed.");
            crouchSync.Commit(standSwitch);
            if (crouchSync.ResourceSwitchCount != 2)
                throw new InvalidOperationException("Lyra crouch Cycle lost shared player identity.");
            var cycleSync = timing.CreateCycleSync();
            var cycleTime = 0d;
            for (var frame = 0; frame < 35; frame++)
            {
                var tick = cycleSync.Evaluate("jog_fwd_cycle", cycleTime, 1, 1.0 / 60);
                cycleSync.Commit(tick);
                cycleTime = tick.Time;
            }
            var previousPlayerMarker = cycleSync.Evaluate("jog_fwd_cycle", cycleTime, 1, 0).Player.Marker;
            var frozenSwitch = cycleSync.Evaluate("walk_fwd_cycle", cycleTime, 1, 0);
            var switched = cycleSync.Evaluate("walk_fwd_cycle", cycleTime, 1, 1.0 / 60);
            if (switched != cycleSync.Evaluate("walk_fwd_cycle", cycleTime, 1, 1.0 / 60) ||
                !frozenSwitch.Player.Marker.Initialized ||
                frozenSwitch.Player.Marker == previousPlayerMarker ||
                !switched.MarkerStart.Valid || !switched.Group.MarkerEnd.Valid ||
                switched.PreviousTime <= 0 || switched.Time <= 0)
                throw new InvalidOperationException("Lyra Cycle marker history/retry failed.");
            cycleSync.Commit(switched);
            if (cycleSync.ResourceSwitchCount != 1)
                throw new InvalidOperationException("Lyra Cycle switch did not retain Sync identity.");
            try
            {
                cycleSync.Commit(switched);
                throw new InvalidOperationException("Lyra Cycle accepted a stale candidate.");
            }
            catch (InvalidOperationException exception) when (exception.Message == "Stale Lyra Cycle Sync candidate.")
            {
            }
            cycleSync.Reset();
            var restarted = cycleSync.Evaluate("jog_fwd_cycle", 0, 1, 1.0 / 60);
            if (restarted.PreviousTime != 0 || cycleSync.ResourceSwitchCount != 1)
                throw new InvalidOperationException("Lyra Cycle Sync was not reset on phase exit.");
            var cycleSlots = rig.Catalog.Clips.Values
                .Where(clip => clip.Slot.EndsWith("_cycle", StringComparison.Ordinal)).ToArray();
            var markerSwitches = 0;
            foreach (var from in cycleSlots)
            foreach (var to in cycleSlots)
            {
                if (from.Slot == to.Slot) continue;
                foreach (var fraction in new[] { 0.0, 0.25, 0.5, 0.75, 0.99 })
                {
                    var pairSync = timing.CreateCycleSync();
                    var prior = pairSync.Evaluate(from.Slot, from.PlayLength * fraction,
                        1, 1.0 / 60);
                    pairSync.Commit(prior);
                    var next = pairSync.Evaluate(to.Slot, prior.Time, 1, 1.0 / 60);
                    if (!next.MarkerStart.Valid || !next.Group.MarkerEnd.Valid ||
                        next.PreviousTime < 0 || next.PreviousTime > to.PlayLength ||
                        next.Time < 0 || next.Time > to.PlayLength)
                        throw new InvalidOperationException($"Lyra Marker switch mismatch: {from.Slot}->{to.Slot}.");
                    markerSwitches++;
                }
            }
            if (Math.Abs(defaults.StartPivotMinimumRate - 0.6) > 1e-5 ||
                Math.Abs(defaults.StartPivotMaximumRate - 5.0) > 1e-5)
                throw new InvalidOperationException("Lyra Unarmed rate defaults differ from source.");
            var syntheticRoot = new LyraRootMotionRange(1,
                new System.Numerics.Vector3(100, 0, 0), System.Numerics.Quaternion.Identity, 100);
            if (Math.Abs(LyraUnarmedRootMotion.MatchCycleRate(100, syntheticRoot, 1, defaults) - 1) > 1e-6 ||
                Math.Abs(LyraUnarmedRootMotion.MatchCycleRate(1, syntheticRoot, 1, defaults) - 0.8) > 1e-6 ||
                Math.Abs(LyraUnarmedRootMotion.MatchCycleRate(1000, syntheticRoot, 1, defaults) - 1.2) > 1e-6 ||
                LyraUnarmedRootMotion.MatchCycleRate(100, syntheticRoot with { PlanarDistanceCm = 0 }, 1, defaults) != 1)
                throw new InvalidOperationException("Lyra Cycle play-rate rule differs from source.");
            foreach (var pivot in rig.Catalog.Clips.Values.Where(clip => clip.Slot.EndsWith("_pivot", StringComparison.Ordinal)))
            {
                var state = notifies.Events(pivot.Slot).Single(entry => entry.NotifyStateClass.Length > 0);
                if (notifies.TransitionToLocomotionActive(pivot.Slot, state.TriggerTime - 1e-4) ||
                    !notifies.TransitionToLocomotionActive(pivot.Slot, state.TriggerTime) ||
                    notifies.TransitionToLocomotionActive(pivot.Slot, state.EndTriggerTime))
                    throw new InvalidOperationException($"Invalid Lyra pivot transition window: {pivot.Slot}.");
            }
            foreach (var clip in rig.Catalog.Clips.Values)
            {
                if (!timing.TryGetDistance(clip.Slot, out var curve)) continue;
                var time = clip.PlayLength * 0.4;
                var distance = curve.DistanceAtTime(time);
                if (Math.Abs(curve.DistanceAtTime(curve.TimeAtDistance(distance)) - distance) > 1e-3)
                    throw new InvalidOperationException($"Lyra Distance inverse failed: {clip.Slot}.");
                if (clip.Slot.EndsWith("_start", StringComparison.Ordinal))
                {
                    var advanced = curve.AdvanceByDistance(0.2, 20, 1.0 / 60.0, 0.6, 5.0);
                    if (advanced < 0.2 + 0.6 / 60.0 - 1e-6 ||
                        advanced > 0.2 + 5.0 / 60.0 + 1e-6 ||
                        curve.AdvanceByDistance(0.2, 0, 1.0 / 60.0, 0.6, 5.0) != 0.2)
                        throw new InvalidOperationException($"Lyra distance advance failed: {clip.Slot}.");
                }
            }
            var moving = 0;
            foreach (var clip in rig.Catalog.Clips.Values)
            {
                rig.Player.Play(rig.QualifiedName(clip.Slot));
                using var animation = rig.Player.GetAnimation(rig.QualifiedName(clip.Slot));
                var expectedLoop = clip.Slot is "idle" || clip.Slot.EndsWith("_cycle", StringComparison.Ordinal);
                if ((animation.LoopMode == Godot.Animation.LoopModeEnum.Linear) != expectedLoop)
                    throw new InvalidOperationException($"Invalid Lyra loop mode: {clip.Slot}.");
                rig.Player.Advance(0);
                var initial = Enumerable.Range(0, rig.Skeleton.GetBoneCount())
                    .Select(rig.Skeleton.GetBonePose).ToArray();
                rig.Player.Advance(Math.Min(0.3, clip.PlayLength * 0.5));
                var changed = Enumerable.Range(0, initial.Length)
                    .Count(bone => rig.Skeleton.GetBonePose(bone).Origin.DistanceTo(initial[bone].Origin) > 1e-4f ||
                        rig.Skeleton.GetBonePose(bone).Basis.GetRotationQuaternion()
                            .AngleTo(initial[bone].Basis.GetRotationQuaternion()) > 1e-4f);
                if (changed < 3) throw new InvalidOperationException($"Lyra clip did not drive ALS bones: {clip.Slot} ({changed}).");
                moving++;
            }
            var auxiliaryMoving = 0;
            foreach (var clip in rig.Auxiliary!.Clips.Values)
            {
                rig.Player.Play(rig.QualifiedName(clip.Slot));
                using var animation = rig.Player.GetAnimation(rig.QualifiedName(clip.Slot));
                var expectedLoop = clip.Slot is "crouch_idle" or "crouch_entry" or "crouch_exit" or
                    "jump_start_loop" or "jump_fall_loop" ||
                    clip.Slot.StartsWith("crouch_walk_", StringComparison.Ordinal);
                if ((animation.LoopMode == Godot.Animation.LoopModeEnum.Linear) != expectedLoop)
                    throw new InvalidOperationException($"Invalid Lyra auxiliary loop mode: {clip.Slot}.");
                rig.Player.Advance(0);
                var initial = Enumerable.Range(0, rig.Skeleton.GetBoneCount())
                    .Select(rig.Skeleton.GetBonePose).ToArray();
                rig.Player.Advance(Math.Min(0.3, clip.PlayLength * 0.5));
                var changed = Enumerable.Range(0, initial.Length)
                    .Count(bone => rig.Skeleton.GetBonePose(bone).Origin.DistanceTo(initial[bone].Origin) > 1e-4f ||
                        rig.Skeleton.GetBonePose(bone).Basis.GetRotationQuaternion()
                            .AngleTo(initial[bone].Basis.GetRotationQuaternion()) > 1e-4f);
                if (changed < 3)
                    throw new InvalidOperationException($"Lyra auxiliary clip did not drive ALS bones: {clip.Slot} ({changed}).");
                auxiliaryMoving++;
            }
            foreach (var direction in Enum.GetValues<LyraCardinalDirection>())
            {
                var slot = router.Resolve(LyraLayerHook.FullBody_CycleState,
                    new LyraLayerContext(direction, LyraGait.Jog));
                if (!slot.EndsWith("_cycle", StringComparison.Ordinal))
                    throw new InvalidOperationException("Invalid Lyra cycle layer selection.");
                foreach (var hook in new[] { LyraLayerHook.FullBody_StartState,
                             LyraLayerHook.FullBody_CycleState, LyraLayerHook.FullBody_StopState,
                             LyraLayerHook.FullBody_PivotState })
                {
                    var ads = router.Resolve(hook, new(direction, LyraGait.Jog, false, true));
                    if (!ads.StartsWith("walk_", StringComparison.Ordinal))
                        throw new InvalidOperationException($"Invalid Lyra ADS layer selection: {hook}.");
                }
                foreach (var (hook, prefix) in new[]
                         {
                             (LyraLayerHook.FullBody_StartState, "crouch_start_"),
                             (LyraLayerHook.FullBody_CycleState, "crouch_walk_"),
                             (LyraLayerHook.FullBody_StopState, "crouch_stop_"),
                             (LyraLayerHook.FullBody_PivotState, "crouch_pivot_"),
                         })
                {
                    var crouch = router.Resolve(hook, new(direction, LyraGait.Walk, true));
                    if (!crouch.StartsWith(prefix, StringComparison.Ordinal))
                        throw new InvalidOperationException($"Invalid Lyra crouch layer selection: {hook}.");
                }
            }
            foreach (var hook in new[] { LyraLayerHook.FullBody_JumpStartState,
                         LyraLayerHook.FullBody_JumpStartLoopState, LyraLayerHook.FullBody_JumpApexState,
                         LyraLayerHook.FullBody_FallLoopState, LyraLayerHook.FullBody_FallLandState })
                if (!router.Resolve(hook, new(LyraCardinalDirection.Forward, LyraGait.Jog))
                        .StartsWith("jump_", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Invalid Lyra air layer selection: {hook}.");
            if (auxiliaryMoving != 24)
                throw new InvalidOperationException("Lyra crouch/air auxiliary catalog is incomplete.");
            var remainingMoving = 0;
            foreach (var clip in rig.Remaining.Clips.Values)
            {
                rig.Player.Play(rig.QualifiedName(clip.Slot));
                using var animation = rig.Player.GetAnimation(rig.QualifiedName(clip.Slot));
                if ((animation.LoopMode == Godot.Animation.LoopModeEnum.Linear) !=
                    (clip.Slot == "hipfire_crouch"))
                    throw new InvalidOperationException($"Invalid Lyra remaining loop mode: {clip.Slot}.");
                rig.Player.Advance(0);
                var initial = Enumerable.Range(0, rig.Skeleton.GetBoneCount())
                    .Select(rig.Skeleton.GetBonePose).ToArray();
                rig.Player.Advance(Math.Min(0.3, clip.PlayLength * 0.5));
                var changed = Enumerable.Range(0, initial.Length)
                    .Count(bone => rig.Skeleton.GetBonePose(bone).Origin.DistanceTo(initial[bone].Origin) > 1e-4f ||
                        rig.Skeleton.GetBonePose(bone).Basis.GetRotationQuaternion()
                            .AngleTo(initial[bone].Basis.GetRotationQuaternion()) > 1e-4f);
                if (changed < 3)
                    throw new InvalidOperationException($"Lyra remaining clip did not drive ALS bones: {clip.Slot} ({changed}).");
                remainingMoving++;
            }
            var hipFire = new LyraHipFirePoseLayer(rig);
            rig.Player.Play(rig.QualifiedName("jog_fwd_cycle"));
            rig.Player.Advance(0);
            rig.Player.Seek(0.2, true);
            var baseRoot = rig.Skeleton.GetBonePose(Bone("root"));
            var baseHand = rig.Skeleton.GetBonePose(leftHand);
            hipFire.UpdateWeight(false, true, false, double.MaxValue, 0, 0, false, 1.0 / 60);
            hipFire.Apply(false);
            if (hipFire.Weight != 0 || hipFire.AppliedFrames != 0 ||
                rig.Skeleton.GetBonePose(leftHand) != baseHand)
                throw new InvalidOperationException("Grounded Unarmed HipFire must retain base pose.");
            hipFire.UpdateWeight(false, false, false, 0, 1, 0, false, 1.0 / 60);
            hipFire.Apply(false);
            var layeredHand = rig.Skeleton.GetBonePose(leftHand);
            var layeredGlobalHand = rig.Skeleton.GetBoneGlobalPose(leftHand);
            if (hipFire.Weight != 1 || hipFire.AppliedFrames != 1 ||
                rig.Skeleton.GetBonePose(Bone("root")) != baseRoot ||
                layeredHand == baseHand)
                throw new InvalidOperationException("Airborne HipFire did not blend the ALS upper body.");
            rig.Player.Play(rig.QualifiedName("idle"));
            rig.Player.Advance(0);
            var targetHand = rig.Skeleton.GetBonePose(leftHand);
            var targetGlobalHand = rig.Skeleton.GetBoneGlobalPose(leftHand);
            if (layeredHand.Origin.DistanceTo(targetHand.Origin) > 1e-4f ||
                layeredGlobalHand.Basis.GetRotationQuaternion()
                    .AngleTo(targetGlobalHand.Basis.GetRotationQuaternion()) > 1e-4f)
                throw new InvalidOperationException("HipFire hand differs from the source evaluator at time zero: " +
                    $"layered={layeredGlobalHand} target={targetGlobalHand} base={baseHand} " +
                    $"position={layeredHand.Origin.DistanceTo(targetHand.Origin)} " +
                    $"rotation={layeredGlobalHand.Basis.GetRotationQuaternion().AngleTo(targetGlobalHand.Basis.GetRotationQuaternion())}.");
            hipFire.RestoreBase();
            if (rig.Skeleton.GetBonePose(leftHand) != baseHand ||
                rig.Skeleton.GetBonePose(Bone("root")) != baseRoot)
                throw new InvalidOperationException("HipFire did not restore its incoming base pose.");
            hipFire.UpdateWeight(true, false, false, 0, 1, 0, false, 1.0 / 60);
            if (hipFire.Weight != 0)
                throw new InvalidOperationException("Unarmed crouch must suppress HipFire override.");
            var aim = LyraUnarmedAimOffset.Load(rig.Skeleton);
            var aimChecks = 0;
            Span<LyraAimWeight> actualAimWeights = stackalloc LyraAimWeight[3];
            foreach (var file in new[] { "unarmed_aim_native_grid.json",
                         "unarmed_aim_native_interp_grid.json" })
            {
                using var oracle = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(
                    "res://assets/generated/lyra_als/" + file));
                foreach (var row in oracle.RootElement.GetProperty("data").GetProperty("cases").EnumerateArray())
                {
                    var input = row.GetProperty("input");
                    var count = aim.SampleWeights(input[0].GetSingle(), input[1].GetSingle(), actualAimWeights);
                    var expected = row.GetProperty("weights").EnumerateArray().ToDictionary(
                        weight => weight.GetProperty("sample").GetInt32(),
                        weight => weight.GetProperty("weight").GetSingle());
                    if (count != expected.Count || actualAimWeights[..count].ToArray().Any(weight =>
                            !expected.TryGetValue(weight.Sample, out var value) ||
                            Math.Abs(weight.Weight - value) > 2e-5f))
                        throw new InvalidOperationException($"Lyra AimOffset weights differ from UE: {file}/" +
                            $"{input[0].GetSingle()},{input[1].GetSingle()}.");
                    aimChecks++;
                }
            }
            var aimCatalogBytes = Godot.FileAccess.GetFileAsBytes(
                "res://assets/generated/lyra_als/unarmed_aim_samples_catalog.json");
            using var aimCatalog = JsonDocument.Parse(aimCatalogBytes);
            var aimMapping = aimCatalog.RootElement.GetProperty("logicalToPhysical");
            var aimCatalogHash = Convert.ToHexString(SHA256.HashData(aimCatalogBytes)).ToLowerInvariant();
            var sampledAdditive = new AlsLocalPose[68];
            var aimPoseChecks = 0;
            var maxAimPosition = 0f;
            var maxAimRotation = 0f;
            var maxAimScale = 0f;
            static float RotationError(NQuaternion left, NQuaternion right)
            {
                var relative = NQuaternion.Normalize(left) *
                    NQuaternion.Conjugate(NQuaternion.Normalize(right));
                return 2 * MathF.Atan2(new NVector3(relative.X, relative.Y, relative.Z).Length(),
                    Math.Abs(relative.W));
            }
            foreach (var file in new[] { "unarmed_aim_als_native_grid_poses.json",
                         "unarmed_aim_als_native_interp_poses.json" })
            {
                using var oracle = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(
                    "res://assets/generated/lyra_als/" + file));
                if (oracle.RootElement.GetProperty("catalogSha256").GetString() != aimCatalogHash ||
                    oracle.RootElement.GetProperty("targetSkeleton").GetString() !=
                    aimCatalog.RootElement.GetProperty("targetSkeleton").GetString())
                    throw new InvalidOperationException("ALS AimOffset oracle does not match the target catalog.");
                foreach (var row in oracle.RootElement.GetProperty("data").EnumerateArray())
                {
                    aim.SampleAdditive(row.GetProperty("pitch").GetSingle(),
                        row.GetProperty("y").GetSingle(), sampledAdditive);
                    var pose = row.GetProperty("pose");
                    if (pose.GetArrayLength() != aimMapping.GetArrayLength())
                        throw new InvalidOperationException("ALS AimOffset oracle bone count changed.");
                    for (var logical = 0; logical < pose.GetArrayLength(); logical++)
                    {
                        var physical = aimMapping[logical].GetInt32();
                        if (physical < 0) continue;
                        var actual = sampledAdditive[physical];
                        var atom = pose[logical];
                        var p = atom.GetProperty("position");
                        var q = atom.GetProperty("rotation");
                        var s = atom.GetProperty("scale");
                        var expectedPosition = new NVector3(p[0].GetSingle(), -p[1].GetSingle(),
                            p[2].GetSingle()) * 0.01f;
                        var expectedRotation = NQuaternion.Normalize(new NQuaternion(
                            -q[1].GetSingle(), -q[2].GetSingle(), q[0].GetSingle(), q[3].GetSingle()));
                        var expectedScale = new NVector3(s[0].GetSingle(), s[1].GetSingle(),
                            s[2].GetSingle());
                        maxAimPosition = Math.Max(maxAimPosition,
                            NVector3.Distance(actual.Position, expectedPosition));
                        maxAimRotation = Math.Max(maxAimRotation,
                            RotationError(actual.Rotation, expectedRotation));
                        maxAimScale = Math.Max(maxAimScale,
                            NVector3.Distance(actual.Scale, expectedScale));
                    }
                    aimPoseChecks++;
                }
            }
            if (aimPoseChecks != 16 || maxAimPosition > 1e-6f ||
                maxAimRotation > 1e-5f || maxAimScale > 1e-5f)
                throw new InvalidOperationException($"ALS AimOffset differs from UE: " +
                    $"cases={aimPoseChecks} position={maxAimPosition} " +
                    $"rotation={maxAimRotation} scale={maxAimScale}.");
            static AlsLocalPose UnrealAtom(JsonElement atom)
            {
                var p = atom.GetProperty("position");
                var q = atom.GetProperty("rotation");
                var s = atom.GetProperty("scale");
                return new AlsLocalPose(new NVector3(p[0].GetSingle(), p[1].GetSingle(),
                    p[2].GetSingle()), new NQuaternion(q[0].GetSingle(), q[1].GetSingle(),
                    q[2].GetSingle(), q[3].GetSingle()), new NVector3(s[0].GetSingle(),
                    s[1].GetSingle(), s[2].GetSingle()));
            }
            var baseCatalogBytes = Godot.FileAccess.GetFileAsBytes(
                "res://assets/generated/lyra_als/unarmed_catalog.json");
            var baseCatalogHash = Convert.ToHexString(SHA256.HashData(baseCatalogBytes)).ToLowerInvariant();
            var nativeBase = new AlsLocalPose[68];
            var nativeAdditive = new AlsLocalPose[68];
            var applied = new AlsLocalPose[68];
            var appliedScratch = new NQuaternion[136];
            var appliedChecks = 0;
            var maxAppliedPosition = 0f;
            var maxAppliedRotation = 0f;
            var maxAppliedScale = 0f;
            foreach (var file in new[] { "unarmed_aim_als_native_apply-grid_poses.json",
                         "unarmed_aim_als_native_apply-interp_poses.json" })
            {
                using var oracle = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(
                    "res://assets/generated/lyra_als/" + file));
                if (oracle.RootElement.GetProperty("catalogSha256").GetString() != aimCatalogHash ||
                    oracle.RootElement.GetProperty("baseCatalogSha256").GetString() != baseCatalogHash)
                    throw new InvalidOperationException("ALS applied AimOffset oracle has stale asset catalogs.");
                foreach (var row in oracle.RootElement.GetProperty("data").EnumerateArray())
                {
                    var bases = row.GetProperty("basePose");
                    var additives = row.GetProperty("pose");
                    var expected = row.GetProperty("appliedPose");
                    if (bases.GetArrayLength() != 79 || additives.GetArrayLength() != 79 ||
                        expected.GetArrayLength() != 79)
                        throw new InvalidOperationException("ALS applied AimOffset pose layout changed.");
                    for (var logical = 0; logical < 79; logical++)
                    {
                        var physical = aimMapping[logical].GetInt32();
                        if (physical < 0) continue;
                        nativeBase[physical] = UnrealAtom(bases[logical]);
                        nativeAdditive[physical] = UnrealAtom(additives[logical]);
                    }
                    AlsMeshSpaceAdditivePose.Apply(nativeBase, nativeAdditive, parents,
                        appliedScratch, applied);
                    for (var logical = 0; logical < 79; logical++)
                    {
                        var physical = aimMapping[logical].GetInt32();
                        if (physical < 0) continue;
                        var target = UnrealAtom(expected[logical]);
                        var actual = applied[physical];
                        maxAppliedPosition = Math.Max(maxAppliedPosition,
                            NVector3.Distance(actual.Position, target.Position));
                        maxAppliedRotation = Math.Max(maxAppliedRotation,
                            RotationError(actual.Rotation, target.Rotation));
                        maxAppliedScale = Math.Max(maxAppliedScale,
                            NVector3.Distance(actual.Scale, target.Scale));
                    }
                    appliedChecks++;
                }
            }
            if (appliedChecks != 16 || maxAppliedPosition > 1e-4f ||
                maxAppliedRotation > 1e-5f || maxAppliedScale > 1e-5f)
                throw new InvalidOperationException($"ALS AimOffset application differs from UE: " +
                    $"cases={appliedChecks} position={maxAppliedPosition} " +
                    $"rotation={maxAppliedRotation} scale={maxAppliedScale}.");
            rig.Player.Play(rig.QualifiedName("idle"));
            rig.Player.Advance(0);
            var aimBaseRoot = rig.Skeleton.GetBonePose(Bone("root"));
            var aimBaseHand = rig.Skeleton.GetBonePose(leftHand);
            var aimBaseGlobalHand = rig.Skeleton.GetBoneGlobalPose(leftHand);
            aim.Apply(90, 90, 1);
            var aimHand = rig.Skeleton.GetBoneGlobalPose(leftHand);
            if (rig.Skeleton.GetBonePose(Bone("root")).Origin.DistanceTo(aimBaseRoot.Origin) > 1e-5f ||
                aimHand.Basis.GetRotationQuaternion()
                    .AngleTo(aimBaseGlobalHand.Basis.GetRotationQuaternion()) < 0.01f)
                throw new InvalidOperationException("Lyra AimOffset did not rotate the ALS upper body.");
            aim.RestoreBase();
            if (rig.Skeleton.GetBonePose(Bone("root")) != aimBaseRoot ||
                rig.Skeleton.GetBonePose(leftHand) != aimBaseHand)
                throw new InvalidOperationException("Lyra AimOffset did not restore its incoming pose.");
            aim.Apply(0, 0, 0);
            if (rig.Skeleton.GetBoneGlobalPose(leftHand).Basis.GetRotationQuaternion()
                    .AngleTo(aimBaseGlobalHand.Basis.GetRotationQuaternion()) > 1e-3f)
                throw new InvalidOperationException("Lyra AimOffset center is not its additive base.");
            aim.RestoreBase();
            var motion = new LyraUnarmedMotion(rig, router, expandedTiming, expandedNotifies,
                expandedRootMotion, defaults);
            motion.Advance(new(Vector2.Zero, Vector2.Zero, LyraGait.Jog, 0, 0, 0, 0,
                AimPitchDegrees: 45));
            if (motion.Phase != LyraMotionPhase.Idle || motion.AimOffsetFrames != 1 ||
                rig.Skeleton.GetBoneGlobalPose(leftHand).Basis.GetRotationQuaternion()
                    .AngleTo(aimBaseGlobalHand.Basis.GetRotationQuaternion()) < 0.01f)
                throw new InvalidOperationException("Unarmed Idle did not evaluate FullBody_Aiming without ADS.");
            motion.Advance(new(Vector2.Zero, Vector2.Zero, LyraGait.Jog, 0, 0, 0, 0,
                IsOnGround: false, VerticalVelocity: 1, ApplyHipfireOverridePose: 1));
            if (motion.Phase != LyraMotionPhase.JumpStart ||
                motion.HipFireBlendWeight != 1 || motion.HipFireBlendFrames != 1 ||
                motion.AimOffsetFrames != 2)
                throw new InvalidOperationException("Lyra linked layer did not apply during the air phase.");
            GD.Print($"LYRA_UNARMED_CATALOG_OK clips={moving} auxiliary={auxiliaryMoving} remaining={remainingMoving} bones={rig.Skeleton.GetBoneCount()} " +
                $"distance={timing.DistanceCurveCount} markers={timing.MarkerCount} playback={timing.PlaybackCount} " +
                $"notifies={notifies.EventCount} notifyChecks={notifyChecks}/{stateEnds}/{scopeClosedStates}/1 transitions={notifies.TransitionStateCount} root={rootMotion.Count} switches={cycleSync.ResourceSwitchCount}/{markerSwitches} " +
                $"rate={defaults.StartPivotMinimumRate:0.0}-{defaults.StartPivotMaximumRate:0.0} masks=2/68 profiles={inventory.Count} layer={router.ActiveName} " +
                $"auxMetadata={allTiming.DistanceCurveCount}/{allTiming.MarkerCount}/{allTiming.PlaybackCount}/{allNotifies.EventCount}/{allRootMotion.Count} auxNotifyChecks={auxiliaryNotifyChecks} auxSwitches={crouchSync.ResourceSwitchCount} " +
                $"allMetadata={expandedTiming.DistanceCurveCount}/{expandedTiming.MarkerCount}/{expandedTiming.PlaybackCount}/{expandedNotifies.EventCount}/{expandedRootMotion.Count} remainingNotifyChecks={remainingNotifyChecks} hipfire={hipFire.AppliedFrames}/{motion.HipFireBlendFrames} aim={aimChecks}/{aim.AppliedFrames}/15 aimOracle={aimPoseChecks}/{maxAimPosition}/{maxAimRotation}/{maxAimScale} aimApplied={appliedChecks}/{maxAppliedPosition}/{maxAppliedRotation}/{maxAppliedScale}");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }
}
