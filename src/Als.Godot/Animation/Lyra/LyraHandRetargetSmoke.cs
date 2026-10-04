using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace GodotAls.Animation.Lyra;

public partial class LyraHandRetargetSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Hand retarget smoke failed: " + error); GetTree().Quit(1); }
    }

    private void Run()
    {
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(
            "res://assets/generated/lyra_als/hand_retarget_native.json"));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("nativeOperator").GetString() !=
                "FAnimNode_HandIKRetargeting + FCSPose.LocalBlendCSBoneTransforms")
            throw new InvalidOperationException("Native hand-retarget oracle differs.");
        foreach (var entry in root.GetProperty("catalogSha256").EnumerateObject())
        {
            var bytes = Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/" + entry.Name + "_catalog.json");
            if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != entry.Value.GetString())
                throw new InvalidOperationException("Stale hand-retarget catalog oracle.");
        }
        var layout = root.GetProperty("layout");
        var parents = layout.GetProperty("logicalParents").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        var names = layout.GetProperty("logicalBoneNames").EnumerateArray().Select(v => v.GetString()!).ToArray();
        var ids = names.Select((name, id) => (name, id)).ToDictionary(v => v.name, v => v.id, StringComparer.OrdinalIgnoreCase);
        var gun = ids["ik_hand_gun"];
        using var rig = LyraBoundRig.Build(includeAuxiliary: true, includeRemaining: true, includePistol: true, includeRifle: true);
        AddChild(rig.Root);
        var layer = new LyraHandRetargetPoseLayer(rig, LyraLinkedLayerInventory.Load().Get("rifle"));
        var count = 0; var changed = 0; var partial = 0;
        double maxNativePosition = 0, maxNativeRotation = 0;
        float maxGodotPosition = 0;
        foreach (var row in root.GetProperty("rows").EnumerateArray())
        {
            var before = row.GetProperty("before").EnumerateArray().Select(Parse).ToArray();
            var after = row.GetProperty("after").EnumerateArray().Select(Parse).ToArray();
            var component = new AlsPrecisePose[before.Length];
            for (var bone = 0; bone < before.Length; bone++)
                component[bone] = (parents[bone] < 0 ? before[bone] :
                    AlsPrecisePose.Compose(before[bone], component[parents[bone]])).Normalized();
            var solved = AlsHandIkRetargeting.Retarget(component[gun], component[ids["hand_l"]], component[ids["hand_r"]],
                component[ids["ik_hand_l"]], component[ids["ik_hand_r"]], row.GetProperty("handFKWeight").GetSingle());
            var alpha = row.GetProperty("alpha").GetSingle();
            var local = AlsPrecisePose.Relative(solved, component[parents[gun]]);
            var candidate = AlsPrecisePose.BlendTransform(before[gun], local, alpha);
            for (var bone = 0; bone < before.Length; bone++)
            {
                var actual = bone == gun ? candidate : before[bone];
                var expected = after[bone];
                maxNativePosition = Math.Max(maxNativePosition, Math.Sqrt((actual.Position - expected.Position).LengthSquared));
                // Native CS-to-local conversion normalizes visited bones. Compare
                // orientation with unit quaternions; source norm is validated separately.
                var actualRotation = actual.Rotation.Normalized();
                var expectedRotation = expected.Rotation.Normalized();
                var sign = AlsQuaternion.Dot(actualRotation, expectedRotation) < 0 ? -1 : 1;
                var d = actualRotation + expectedRotation * -sign;
                maxNativeRotation = Math.Max(maxNativeRotation, Math.Sqrt(d.LengthSquared));
                if (Math.Sqrt((actual.Scale - expected.Scale).LengthSquared) > 1e-12)
                    throw new InvalidOperationException("Native hand-retarget scale differs.");
            }
            if (row.GetProperty("handFKWeight").GetSingle() == 1)
            {
                var input = before.Take(68).Select(ToGodot).ToArray();
                LyraUnarmedAimOffset.WritePose(rig.Skeleton, input);
                var leftBefore = rig.Skeleton.GetBoneGlobalPose(ids["ik_hand_l"]).Origin;
                var rightBefore = rig.Skeleton.GetBoneGlobalPose(ids["ik_hand_r"]).Origin;
                layer.Apply(1 - alpha);
                var inheritedOffset = rig.Skeleton.GetBoneGlobalPose(parents[gun]).Basis *
                    (rig.Skeleton.GetBonePosePosition(gun) -
                        new Vector3(input[gun].Position.X, input[gun].Position.Y, input[gun].Position.Z));
                if ((rig.Skeleton.GetBoneGlobalPose(ids["ik_hand_l"]).Origin - leftBefore).DistanceTo(inheritedOffset) > 2e-6f ||
                    (rig.Skeleton.GetBoneGlobalPose(ids["ik_hand_r"]).Origin - rightBefore).DistanceTo(inheritedOffset) > 2e-6f)
                    throw new InvalidOperationException("IK targets did not inherit the retargeted gun offset.");
                var actual = new AlsLocalPose[68];
                LyraUnarmedAimOffset.CapturePose(rig.Skeleton, actual);
                for (var bone = 0; bone < actual.Length; bone++)
                {
                    var expected = ToGodot(after[bone]);
                    maxGodotPosition = Math.Max(maxGodotPosition, NVector3.Distance(actual[bone].Position, expected.Position));
                    if (bone != gun && actual[bone] != input[bone])
                        throw new InvalidOperationException("Hand retarget changed an unrelated local bone.");
                }
                layer.RestoreBase();
                var restored = new AlsLocalPose[68];
                LyraUnarmedAimOffset.CapturePose(rig.Skeleton, restored);
                if (!restored.SequenceEqual(input)) throw new InvalidOperationException("Hand retarget did not restore its input.");
            }
            if (row.GetProperty("changedBones").GetInt32() != 0 && alpha > 0) changed++;
            if (alpha is > 0 and < 1) partial++;
            count++;
        }
        if (count != 324 || changed == 0 || partial != 162 || maxNativePosition > 1e-8 ||
            maxNativeRotation > 1e-10 || maxGodotPosition > 2e-6f)
            throw new InvalidOperationException($"Hand-retarget native mismatch: cases={count} changed={changed} " +
                $"partial={partial} position={maxNativePosition} rotation={maxNativeRotation} godot={maxGodotPosition}.");
        GD.Print($"LYRA_HAND_RETARGET_OK cases={count} changed={changed} partial={partial} " +
            $"nativePositionCm={maxNativePosition} nativeQuaternion={maxNativeRotation} godotPositionM={maxGodotPosition} skeleton=68");
    }

    private static AlsPrecisePose Parse(JsonElement row)
    {
        var p = row.GetProperty("position"); var q = row.GetProperty("rotation"); var s = row.GetProperty("scale");
        var result = new AlsPrecisePose(new(p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble()),
            new(q[0].GetDouble(), q[1].GetDouble(), q[2].GetDouble(), q[3].GetDouble()),
            new(s[0].GetDouble(), s[1].GetDouble(), s[2].GetDouble()));
        result.Validate(1e-6);
        return result;
    }

    private static AlsLocalPose ToGodot(AlsPrecisePose pose) => new(
        new NVector3((float)(pose.Position.X * .01), (float)(-pose.Position.Y * .01), (float)(pose.Position.Z * .01)),
        NQuaternion.Normalize(new NQuaternion((float)-pose.Rotation.X, (float)pose.Rotation.Y,
            (float)-pose.Rotation.Z, (float)pose.Rotation.W)), pose.Scale.ToSingle());
}
