using System.Text.Json;
using Godot;

namespace GodotAls.Animation.Lyra;

public partial class LyraPistolCatalogSmoke : Node
{
    public override void _Ready()
    {
        try
        {
            using var rig = LyraBoundRig.Build(includeAuxiliary: true, includeRemaining: true,
                includePistol: true);
            AddChild(rig.Root);
            var pistol = rig.Pistol ?? throw new InvalidOperationException("Pistol catalog was not bound.");
            var layers = new LyraPistolAnimationLayers(pistol);
            var router = new LyraLinkedLayerRouter(rig.Catalog,
                new LyraUnarmedAnimationLayers(rig.Catalog, rig.Auxiliary, rig.Remaining),
                rig.Auxiliary, rig.Remaining, pistol);
            router.Link(layers);
            foreach (var direction in Enum.GetValues<LyraCardinalDirection>())
            foreach (var crouched in new[] { false, true })
            foreach (var aiming in new[] { false, true })
            {
                var context = new LyraLayerContext(direction, LyraGait.Jog, crouched, aiming);
                foreach (var hook in new[] { LyraLayerHook.FullBody_IdleState,
                             LyraLayerHook.FullBody_StartState, LyraLayerHook.FullBody_CycleState,
                             LyraLayerHook.FullBody_StopState, LyraLayerHook.FullBody_PivotState })
                    _ = rig.QualifiedName(router.Resolve(hook, context));
            }
            foreach (var crouched in new[] { false, true })
            foreach (var right in new[] { false, true })
                _ = rig.QualifiedName(router.ResolveTurnInPlace(right, crouched));
            var timing = LyraUnarmedTiming.Load(rig.Catalog, rig.Auxiliary, rig.Remaining, pistol);
            var notifies = LyraUnarmedNotifies.Load(rig.Catalog, rig.Auxiliary, rig.Remaining, pistol);
            var roots = LyraUnarmedRootMotion.Load(rig.Catalog, rig.Auxiliary, rig.Remaining, pistol);
            var turns = LyraUnarmedTurnInPlace.Load(rig.Remaining!, pistol);
            if (timing.PlaybackCount != 125 || roots.Count != 125 ||
                !turns.IsTurnSlot(router.ResolveTurnInPlace(true, false)))
                throw new InvalidOperationException("Pistol playback, root or turn metadata is incomplete.");

            foreach (var hz in new[] { 30, 60, 120 })
            {
                var sync = timing.CreateCycleSync();
                var time = 0.0;
                foreach (var slot in new[] { "pistol_walk_left_cycle", "pistol_walk_right_cycle",
                             "pistol_walk_left_cycle" })
                {
                    for (var frame = 0; frame < hz; frame++)
                    {
                        var candidate = sync.Evaluate(slot, time, 1, 1.0 / hz);
                        if (!candidate.Group.MarkerEnd.Valid ||
                            candidate.Group.ValidMarkerMask != (slot == "pistol_walk_right_cycle"
                                ? 1UL << 1 : (1UL << 1) | (1UL << 2)) ||
                            !float.IsFinite(candidate.Time))
                            throw new InvalidOperationException($"Pistol marker sync failed: {slot}/{hz}/{frame}.");
                        sync.Commit(candidate);
                        time = candidate.Time;
                    }
                }
                if (sync.ResourceSwitchCount != 2)
                    throw new InvalidOperationException("Pistol single-marker source was not switched twice.");
            }

            var samples = 0;
            foreach (var clip in pistol.Clips.Values)
            {
                rig.Player.Play(rig.QualifiedName(clip.Slot));
                foreach (var time in new[] { 0.0, clip.PlayLength * 0.5, clip.PlayLength })
                {
                    rig.Player.Seek(time, true);
                    for (var bone = 0; bone < rig.Skeleton.GetBoneCount(); bone++)
                    {
                        var position = rig.Skeleton.GetBonePosePosition(bone);
                        var rotation = rig.Skeleton.GetBonePoseRotation(bone);
                        var scale = rig.Skeleton.GetBonePoseScale(bone);
                        if (!position.IsFinite() || !rotation.IsFinite() || !scale.IsFinite() ||
                            position.Length() > 100 || scale.Length() > 100)
                            throw new InvalidOperationException($"Invalid Pistol pose: {clip.Slot}/{bone}/{time}.");
                    }
                    samples++;
                }
            }
            var pistolAim = LyraUnarmedAimOffset.LoadPistol(rig.Skeleton);
            Span<LyraAimWeight> weights = stackalloc LyraAimWeight[3];
            foreach (var yaw in new[] { -180f, 0f, 180f })
            foreach (var pitch in new[] { -90f, 0f, 90f })
            {
                var count = pistolAim.SampleWeights(yaw, pitch, weights);
                if (count != 1 || MathF.Abs(weights[0].Weight - 1) > 1e-6f)
                    throw new InvalidOperationException("Pistol native AimOffset grid lost an authored sample.");
            }
            var aimChecks = 0;
            foreach (var file in new[] { "pistol_aim_native_grid.json",
                         "pistol_aim_native_interp_grid.json" })
            {
                using var oracle = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(
                    "res://assets/generated/lyra_als/" + file));
                foreach (var row in oracle.RootElement.GetProperty("data").GetProperty("cases").EnumerateArray())
                {
                    var input = row.GetProperty("input");
                    var count = pistolAim.SampleWeights(input[0].GetSingle(),
                        input[1].GetSingle(), weights);
                    var expected = row.GetProperty("weights").EnumerateArray().ToDictionary(
                        weight => weight.GetProperty("sample").GetInt32(),
                        weight => weight.GetProperty("weight").GetSingle());
                    if (count != expected.Count || weights[..count].ToArray().Any(weight =>
                            !expected.TryGetValue(weight.Sample, out var value) ||
                            Math.Abs(weight.Weight - value) > 2e-5f))
                        throw new InvalidOperationException("Pistol AimOffset differs from UE: " + file);
                    aimChecks++;
                }
            }
            if (aimChecks != 16)
                throw new InvalidOperationException("Pistol AimOffset native grid is incomplete.");
            rig.Player.Play(rig.QualifiedName("pistol_idle_hipfire"));
            rig.Player.Advance(0);
            var hand = rig.Skeleton.FindBone("hand_r");
            if (hand < 0) throw new InvalidOperationException("ALS Pistol has no right hand bone.");
            var baseHand = rig.Skeleton.GetBonePose(hand);
            var aimingLayer = layers.CreateAimingLayer(rig);
            aimingLayer.Apply(90, 45, 0);
            var relaxedHand = rig.Skeleton.GetBoneGlobalPose(hand).Basis.GetRotationQuaternion();
            aimingLayer.RestoreBase();
            if (rig.Skeleton.GetBonePose(hand) != baseHand)
                throw new InvalidOperationException("Pistol relaxed AimOffset did not restore its base pose.");
            aimingLayer.Apply(90, 45, 1);
            var adsHand = rig.Skeleton.GetBoneGlobalPose(hand).Basis.GetRotationQuaternion();
            aimingLayer.RestoreBase();
            aimingLayer.Apply(90, 45, 0.5f);
            var mixedHand = rig.Skeleton.GetBoneGlobalPose(hand).Basis.GetRotationQuaternion();
            aimingLayer.RestoreBase();
            if (aimingLayer.AppliedFrames != 3 || relaxedHand.AngleTo(adsHand) < 0.001f ||
                relaxedHand.AngleTo(mixedHand) < 0.0002f ||
                mixedHand.AngleTo(adsHand) < 0.0002f ||
                rig.Skeleton.GetBonePose(hand) != baseHand)
                throw new InvalidOperationException("Pistol AimOffsets did not blend on one ALS base pose.");
            var blendWeights = layers.CreateHipFireLayer(rig);
            foreach (var hz in new[] { 30, 60, 120 })
            {
                blendWeights.UpdateWeight(false, true, true, double.MaxValue, 0, 0, true, 1.0 / hz);
                if (blendWeights.Weight != 0 || blendWeights.AimOffsetBlendWeight != 1)
                    throw new InvalidOperationException("Grounded Pistol ADS did not select Idle AimOffset.");
                blendWeights.UpdateWeight(false, false, false, 0, 0, 0, true, 1.0 / hz);
                if (blendWeights.Weight != 1 || blendWeights.AimOffsetBlendWeight != 1)
                    throw new InvalidOperationException("Recent airborne shot did not raise the Pistol layer.");
                for (var frame = 0; frame < hz; frame++)
                    blendWeights.UpdateWeight(false, true, false, double.MaxValue, 0, 0, true,
                        1.0 / hz);
                var expectedHipFire = Math.Pow(1 - 1.0 / hz, hz);
                if (Math.Abs(blendWeights.Weight - expectedHipFire) > 1e-6 ||
                    blendWeights.AimOffsetBlendWeight <= blendWeights.Weight ||
                    blendWeights.AimOffsetBlendWeight >= 0.8f)
                    throw new InvalidOperationException($"Pistol blend weights did not decay at {hz}Hz: " +
                        $"hip={blendWeights.Weight} aim={blendWeights.AimOffsetBlendWeight}.");
                var previousAim = blendWeights.AimOffsetBlendWeight;
                blendWeights.UpdateWeight(false, true, false, double.MaxValue, 0, 20, true,
                    1.0 / hz);
                if (blendWeights.AimOffsetBlendWeight <= previousAim)
                    throw new InvalidOperationException("Root yaw did not restore Pistol Idle AimOffset.");
                blendWeights.UpdateWeight(true, false, false, 0, 1, 0, true, 1.0 / hz);
                if (blendWeights.Weight != 0 || blendWeights.AimOffsetBlendWeight != 1)
                    throw new InvalidOperationException("Pistol crouch did not suppress HipFire override.");
            }
            GD.Print($"LYRA_PISTOL_CATALOG_OK clips={pistol.Clips.Count} poses={samples} " +
                $"playback={timing.PlaybackCount} roots={roots.Count} notifies={notifies.EventCount} " +
                $"markers={timing.MarkerCount} skeleton={rig.Skeleton.GetBoneCount()} aim=15/16/3 native={aimChecks} " +
                "blend=30/60/120");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError("Lyra Pistol catalog smoke failed: " + error);
            GetTree().Quit(1);
        }
    }
}
