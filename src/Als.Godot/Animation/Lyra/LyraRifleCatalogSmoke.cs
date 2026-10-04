using System.Text.Json;
using Godot;

namespace GodotAls.Animation.Lyra;

public partial class LyraRifleCatalogSmoke : Node
{
    public override void _Ready()
    {
        try
        {
            using var rig = LyraBoundRig.Build(includeAuxiliary: true, includeRemaining: true,
                includePistol: true, includeRifle: true);
            AddChild(rig.Root);
            var rifle = rig.Rifle ?? throw new InvalidOperationException("Rifle catalog was not bound.");
            var profile = LyraLinkedLayerInventory.Load().Get("rifle");
            var slots = rifle.Clips.Values.ToDictionary(
                clip => clip.SourceObjectPath, clip => clip.Slot, StringComparer.Ordinal);
            var routes = 0;
            foreach (var direction in Enum.GetValues<LyraCardinalDirection>())
            foreach (var gait in Enum.GetValues<LyraGait>())
            foreach (var crouched in new[] { false, true })
            foreach (var aiming in new[] { false, true })
            foreach (var hook in new[] { LyraLayerHook.FullBody_IdleState,
                         LyraLayerHook.FullBody_StartState, LyraLayerHook.FullBody_CycleState,
                         LyraLayerHook.FullBody_StopState, LyraLayerHook.FullBody_PivotState,
                         LyraLayerHook.FullBody_JumpStartState,
                         LyraLayerHook.FullBody_JumpStartLoopState,
                         LyraLayerHook.FullBody_JumpApexState,
                         LyraLayerHook.FullBody_FallLoopState,
                         LyraLayerHook.FullBody_FallLandState })
            {
                var source = LyraLayerClipResolver.Resolve(profile, hook,
                    new LyraLayerContext(direction, gait, crouched, aiming));
                if (source is null || !slots.TryGetValue(source, out var slot))
                    throw new InvalidOperationException($"Rifle hook has no exported clip: {hook}/{source}.");
                _ = rig.QualifiedName(slot);
                routes++;
            }
            foreach (var crouched in new[] { false, true })
            foreach (var right in new[] { false, true })
            {
                var property = LyraLayerClipResolver.TurnProperty(right, crouched);
                var source = profile.Asset(property);
                if (source is null || !slots.TryGetValue(source, out var slot))
                    throw new InvalidOperationException($"Rifle turn has no exported clip: {property}.");
                _ = rig.QualifiedName(slot);
                routes++;
            }
            var router = new LyraLinkedLayerRouter(rig.Catalog,
                new LyraUnarmedAnimationLayers(rig.Catalog, rig.Auxiliary, rig.Remaining),
                rig.Auxiliary, rig.Remaining, rifle: rifle);
            router.Link(new LyraRifleAnimationLayers(rifle));
            if (router.ActiveName != "Rifle" || router.Revision != 1)
                throw new InvalidOperationException("Rifle linked layer was not selected.");
            var timing = LyraUnarmedTiming.Load(rig.Catalog, rig.Auxiliary, rig.Remaining, rig.Pistol, rifle);
            var rootMotion = LyraUnarmedRootMotion.Load(rig.Catalog, rig.Auxiliary, rig.Remaining, rig.Pistol, rifle);
            var notifies = LyraUnarmedNotifies.Load(rig.Catalog, rig.Auxiliary, rig.Remaining, rig.Pistol, rifle);
            _ = timing.CreateCycleSync();
            if (timing.PlaybackCount != 189 || rootMotion.Count != 189 ||
                notifies.EventCount != 1450 || timing.MarkerCount != 650 || notifies.TransitionStateCount != 36)
                throw new InvalidOperationException("Three-layer playback and event bank is incomplete.");

            var samples = 0;
            foreach (var clip in rifle.Clips.Values)
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
                            position.Length() > 100 || scale.Length() > 100 ||
                            Math.Abs(rotation.LengthSquared() - 1) > 1e-3f)
                            throw new InvalidOperationException(
                                $"Invalid Rifle pose: {clip.Slot}/{bone}/{time}.");
                    }
                    samples++;
                }
            }
            var aim = LyraUnarmedAimOffset.LoadRifle(rig.Skeleton);
            Span<LyraAimWeight> weights = stackalloc LyraAimWeight[3];
            var aimChecks = 0;
            foreach (var file in new[] { "rifle_aim_native_grid.json",
                         "rifle_aim_native_interp_grid.json" })
            {
                using var oracle = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(
                    "res://assets/generated/lyra_als/" + file));
                foreach (var row in oracle.RootElement.GetProperty("data").GetProperty("cases").EnumerateArray())
                {
                    var input = row.GetProperty("input");
                    var count = aim.SampleWeights(input[0].GetSingle(),
                        input[1].GetSingle(), weights);
                    var expected = row.GetProperty("weights").EnumerateArray().ToDictionary(
                        weight => weight.GetProperty("sample").GetInt32(),
                        weight => weight.GetProperty("weight").GetSingle());
                    if (count != expected.Count || weights[..count].ToArray().Any(weight =>
                            !expected.TryGetValue(weight.Sample, out var value) ||
                            Math.Abs(weight.Weight - value) > 2e-5f))
                        throw new InvalidOperationException("Rifle AimOffset differs from UE: " + file);
                    aimChecks++;
                }
            }
            if (aimChecks != 16)
                throw new InvalidOperationException("Rifle AimOffset native grid is incomplete.");
            rig.Player.Play(rig.QualifiedName("rifle_idle_hipfire"));
            rig.Player.Advance(0);
            var hand = rig.Skeleton.FindBone("hand_r");
            if (hand < 0) throw new InvalidOperationException("ALS Rifle has no right hand bone.");
            var baseHand = rig.Skeleton.GetBonePose(hand);
            var aimingLayer = router.CreateAimingLayer(rig);
            aimingLayer.Apply(90, 45, 0);
            var relaxedHand = rig.Skeleton.GetBoneGlobalPose(hand).Basis.GetRotationQuaternion();
            aimingLayer.RestoreBase();
            aimingLayer.Apply(90, 45, 1);
            var rifleHand = rig.Skeleton.GetBoneGlobalPose(hand).Basis.GetRotationQuaternion();
            aimingLayer.RestoreBase();
            aimingLayer.Apply(90, 45, 0.5f);
            var mixedHand = rig.Skeleton.GetBoneGlobalPose(hand).Basis.GetRotationQuaternion();
            aimingLayer.RestoreBase();
            if (aimingLayer.AppliedFrames != 3 || relaxedHand.AngleTo(rifleHand) < 0.001f ||
                relaxedHand.AngleTo(mixedHand) < 0.0002f ||
                mixedHand.AngleTo(rifleHand) < 0.0002f ||
                rig.Skeleton.GetBonePose(hand) != baseHand)
                throw new InvalidOperationException("Rifle AimOffsets did not blend on one ALS base pose.");
            GD.Print($"LYRA_RIFLE_CATALOG_OK clips={rifle.Clips.Count} poses={samples} " +
                $"routes={routes} skeleton={rig.Skeleton.GetBoneCount()} " +
                $"aim=15/16/3 native={aimChecks} layer=rifle playback={timing.PlaybackCount} " +
                $"roots={rootMotion.Count} notifies={notifies.EventCount} markers={timing.MarkerCount}");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError("Lyra Rifle catalog smoke failed: " + error);
            GetTree().Quit(1);
        }
    }
}
