using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraPoseLayersSmoke : Node
{
    public override void _Ready()
    {
        try
        {
            using var rig = LyraBoundRig.Build(includeAuxiliary: true, includeRemaining: true,
                includePistol: true, includeRifle: true);
            AddChild(rig.Root);
            LyraPoseLayerContracts.Validate();
            var inventory = LyraLinkedLayerInventory.Load();
            var mask = LyraUnarmedLayerMasks.Load(rig.Skeleton).GetWeights("LeftFingersMask").ToArray();
            using var poses = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(
                "res://assets/generated/lyra_als/left_hand_pose_catalog.json"));
            var root = poses.RootElement;
            var tested = 0;
            var changedFingers = 0;
            foreach (var profileName in new[] { "unarmed", "pistol", "rifle", "shotgun", "shotgun_feminine" })
            {
                var profile = inventory.Get(profileName);
                var left = new LyraLeftHandPoseLayer(rig, profile);
                var enabled = profile.EnableLeftHandPoseOverride;
                var reference = new Quaternion[68];
                var positions = new Vector3[68];
                var scales = new Vector3[68];
                var coveredWeights = new HashSet<float>();
                if (enabled)
                {
                    var row = root.GetProperty("clips").EnumerateArray().Single(item =>
                        item.GetProperty("source").GetString() == profile.Asset("LeftHandPose_Override"));
                    for (var logical = 0; logical < 79; logical++)
                    {
                        var bone = root.GetProperty("logicalToPhysical")[logical].GetInt32();
                        if (bone < 0) continue;
                        var atom = row.GetProperty("pose")[logical];
                        var q = atom.GetProperty("rotation");
                        var p = atom.GetProperty("position");
                        var s = atom.GetProperty("scale");
                        reference[bone] = new Quaternion(-q[0].GetSingle(), q[1].GetSingle(),
                            -q[2].GetSingle(), q[3].GetSingle()).Normalized();
                        positions[bone] = new Vector3(p[0].GetSingle(), -p[1].GetSingle(), p[2].GetSingle()) * 0.01f;
                        scales[bone] = new Vector3(s[0].GetSingle(), s[1].GetSingle(), s[2].GetSingle());
                    }
                }
                foreach (var hz in new[] { 30, 60, 120 })
                {
                    rig.Player.Play(rig.QualifiedName("rifle_jog_fwd_cycle"));
                    for (var frame = 0; frame < hz * 2; frame++)
                    {
                        rig.Player.Seek(frame / (double)hz % rig.SourceClip("rifle_jog_fwd_cycle").PlayLength, true);
                        var before = new AlsLocalPose[68];
                        LyraUnarmedAimOffset.CapturePose(rig.Skeleton, before);
                        var disable = (frame % 4) switch { 0 => 0f, 1 => 0.5f, 2 => 1f, _ => 2f };
                        left.Apply(disable);
                        var expectedWeight = Math.Clamp((enabled ? 1f : 0f) - disable, 0, 1);
                        coveredWeights.Add(left.Weight);
                        if (left.Weight != expectedWeight) throw new InvalidOperationException("Left-hand weight differs.");
                        for (var bone = 0; bone < 68; bone++)
                        {
                            var actual = rig.Skeleton.GetBonePoseRotation(bone);
                            var basis = before[bone].Rotation;
                            var baseRotation = new Quaternion(basis.X, basis.Y, basis.Z, basis.W);
                            var basePosition = new Vector3(before[bone].Position.X, before[bone].Position.Y, before[bone].Position.Z);
                            var baseScale = new Vector3(before[bone].Scale.X, before[bone].Scale.Y, before[bone].Scale.Z);
                            if (!actual.IsFinite() || Math.Abs(actual.LengthSquared() - 1) > 1e-3f)
                                throw new InvalidOperationException("Left-hand pose is not normalized.");
                            var weight = mask[bone] * expectedWeight;
                            if (weight == 0)
                            {
                                if (actual != baseRotation || rig.Skeleton.GetBonePosePosition(bone) != basePosition ||
                                    rig.Skeleton.GetBonePoseScale(bone) != baseScale)
                                    throw new InvalidOperationException("Left-hand layer changed an unmasked bone.");
                            }
                            else
                            {
                                var expected = baseRotation.Slerp(reference[bone], weight);
                                if (RotationDistance(actual, expected) > 2e-6f ||
                                    rig.Skeleton.GetBonePosePosition(bone).DistanceTo(basePosition.Lerp(positions[bone], weight)) > 2e-6f ||
                                    rig.Skeleton.GetBonePoseScale(bone).DistanceTo(baseScale.Lerp(scales[bone], weight)) > 2e-6f)
                                    throw new InvalidOperationException("Left-hand local rotation blend differs.");
                                if (RotationDistance(actual, baseRotation) > 0.01f) changedFingers++;
                            }
                        }
                        left.RestoreBase();
                        var restored = new AlsLocalPose[68];
                        LyraUnarmedAimOffset.CapturePose(rig.Skeleton, restored);
                        if (!restored.SequenceEqual(before))
                            throw new InvalidOperationException("Left-hand layer did not restore its incoming pose.");
                        tested++;
                    }
                }
                if (!coveredWeights.SetEquals(enabled ? new[] { 0f, 0.5f, 1f } : new[] { 0f }))
                    throw new InvalidOperationException("Left-hand weight coverage is incomplete.");
            }
            if (changedFingers == 0) throw new InvalidOperationException("Enabled Shotgun left-hand pose did not change fingers.");
            var additives = new LyraFullBodyAdditivesLayer(rig);
            var initial = new AlsLocalPose[68];
            LyraUnarmedAimOffset.CapturePose(rig.Skeleton, initial);
            additives.Apply(true);
            AssertIdentity(rig.Skeleton, initial);
            if (additives.State != LyraAdditiveState.Identity) throw new InvalidOperationException("Initial additive state differs.");
            additives.RestoreBase();
            additives.Apply(false);
            AssertIdentity(rig.Skeleton, initial);
            additives.RestoreBase();
            additives.Apply(true);
            AssertIdentity(rig.Skeleton, initial);
            if (additives.State != LyraAdditiveState.AirIdentity)
                throw new InvalidOperationException("Disabled additive landing edge was traversed.");
            additives.RestoreBase();
            var restoredAdditive = new AlsLocalPose[68];
            LyraUnarmedAimOffset.CapturePose(rig.Skeleton, restoredAdditive);
            if (!restoredAdditive.SequenceEqual(initial))
                throw new InvalidOperationException("Additive layer did not restore its incoming pose.");
            var newLayer = new LyraFullBodyAdditivesLayer(rig);
            if (newLayer.State != LyraAdditiveState.Identity)
                throw new InvalidOperationException("New linked additive owner retained the old state.");
            GD.Print($"LYRA_POSE_LAYERS_OK profiles=5 frames={tested} changedFingers={changedFingers} " +
                "additive=Identity/AirIdentity landing=false skeleton=68");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError("Lyra pose-layer smoke failed: " + error);
            GetTree().Quit(1);
        }
    }

    private static float RotationDistance(Quaternion a, Quaternion b)
    {
        if (a.Dot(b) < 0) b = -b;
        return Math.Max(Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)),
            Math.Max(Math.Abs(a.Z - b.Z), Math.Abs(a.W - b.W)));
    }

    private static void AssertIdentity(Skeleton3D skeleton, AlsLocalPose[] initial)
    {
        var actual = new AlsLocalPose[68];
        LyraUnarmedAimOffset.CapturePose(skeleton, actual);
        for (var bone = 0; bone < actual.Length; bone++)
        {
            var from = initial[bone];
            var to = actual[bone];
            if (from.Position != to.Position || from.Scale != to.Scale ||
                RotationDistance(new(from.Rotation.X, from.Rotation.Y, from.Rotation.Z, from.Rotation.W),
                    new(to.Rotation.X, to.Rotation.Y, to.Rotation.Z, to.Rotation.W)) > 2e-6f)
                throw new InvalidOperationException("Identity additive changed its incoming pose.");
        }
    }
}
