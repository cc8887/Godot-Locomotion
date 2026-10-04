using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace GodotAls.Animation.Lyra;

public partial class LyraTwoBoneHandSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("TwoBoneIK native smoke failed: " + error); GetTree().Quit(1); }
    }

    private void Run()
    {
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(
            "res://assets/generated/lyra_als/two_bone_hand_native.json"));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("nativeOperator").GetString() != "FAnimNode_TwoBoneIK + FCSPose.LocalBlendCSBoneTransforms")
            throw new InvalidOperationException("TwoBoneIK native oracle differs.");
        foreach (var dependency in root.GetProperty("dependencySha256").EnumerateObject())
            if (Convert.ToHexString(SHA256.HashData(Godot.FileAccess.GetFileAsBytes(
                "res://assets/generated/lyra_als/" + dependency.Name))).ToLowerInvariant() != dependency.Value.GetString())
                throw new InvalidOperationException("Stale TwoBoneIK oracle dependency: " + dependency.Name);
        var layout = root.GetProperty("layout");
        var parents = layout.GetProperty("logicalParents").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        var names = layout.GetProperty("logicalBoneNames").EnumerateArray().Select(v => v.GetString()!).ToArray();
        var ids = names.Select((name, id) => (name, id)).ToDictionary(v => v.name, v => v.id, StringComparer.OrdinalIgnoreCase);
        var solvers = new Dictionary<string, AlsTwoBoneIkController>();
        foreach (var side in new[] { "left", "right" })
        {
            var suffix = side == "right" ? "r" : "l";
            solvers[side] = new(parents, ids["hand_" + suffix], ids["ik_hand_" + suffix],
                ids["lowerarm_" + suffix], new AlsDoubleVector(0, side == "right" ? 50 : -50, 0), side == "left");
        }
        using var rig = LyraBoundRig.Build(includeAuxiliary: true, includeRemaining: true, includePistol: true, includeRifle: true);
        AddChild(rig.Root);
        var inventory = LyraLinkedLayerInventory.Load();
        var rightLayer = new LyraRightHandIkPoseLayer(rig, inventory.Get("pistol"));
        var unarmedLayer = new LyraRightHandIkPoseLayer(rig, inventory.Get("unarmed"));
        var output = new AlsPrecisePose[79];
        var godotOutput = new AlsLocalPose[68];
        var visible = new AlsLocalPose[68];
        var cases = 0; var physicalCases = 0; var partial = 0; var unreachable = 0;
        double maxPosition = 0, maxQuaternion = 0;
        float maxGodotPosition = 0, maxGodotQuaternion = 0;
        foreach (var row in root.GetProperty("rows").EnumerateArray())
        {
            var input = row.GetProperty("before").EnumerateArray().Select(Parse).ToArray();
            var expected = row.GetProperty("after").EnumerateArray().Select(Parse).ToArray();
            var offsetRow = row.GetProperty("effectorOffset");
            var offset = new AlsDoubleVector(offsetRow[0].GetDouble(), offsetRow[1].GetDouble(), offsetRow[2].GetDouble());
            var alpha = row.GetProperty("alpha").GetSingle();
            var side = row.GetProperty("side").GetString()!;
            solvers[side].Evaluate(input, offset, alpha, output);
            for (var bone = 0; bone < output.Length; bone++)
            {
                maxPosition = Math.Max(maxPosition, Math.Sqrt((output[bone].Position - expected[bone].Position).LengthSquared));
                maxQuaternion = Math.Max(maxQuaternion, QuaternionDistance(output[bone].Rotation, expected[bone].Rotation));
                if (Math.Sqrt((output[bone].Scale - expected[bone].Scale).LengthSquared) > 1e-12)
                    throw new InvalidOperationException("TwoBoneIK changed scale.");
            }
            if (alpha is > 0.00001f and < 0.99999f) partial++;
            if (offset.X == 150) unreachable++;
            if (side == "right" && offset.LengthSquared == 0)
            {
                var physical = input.Take(68).Select(ToGodot).ToArray();
                LyraUnarmedAimOffset.WritePose(rig.Skeleton, physical);
                rightLayer.EvaluatePose(physical, 1 - alpha, godotOutput);
                LyraUnarmedAimOffset.CapturePose(rig.Skeleton, visible);
                if (!visible.SequenceEqual(physical)) throw new InvalidOperationException("Right IK wrote the visible skeleton.");
                for (var bone = 0; bone < physical.Length; bone++)
                {
                    var target = ToGodot(expected[bone]);
                    maxGodotPosition = Math.Max(maxGodotPosition, NVector3.Distance(godotOutput[bone].Position, target.Position));
                    maxGodotQuaternion = Math.Max(maxGodotQuaternion,
                        (float)QuaternionDistance(new AlsQuaternion(godotOutput[bone].Rotation), new AlsQuaternion(target.Rotation)));
                    if (bone != ids["upperarm_r"] && bone != ids["lowerarm_r"] && bone != ids["hand_r"] &&
                        godotOutput[bone] != physical[bone])
                        throw new InvalidOperationException("Right IK modified an unrelated local bone.");
                }
                unarmedLayer.EvaluatePose(physical, 0, godotOutput);
                if (unarmedLayer.Alpha != 0 || !godotOutput.SequenceEqual(physical))
                    throw new InvalidOperationException("Unarmed CDO did not disable right-hand IK.");
                physicalCases++;
            }
            cases++;
        }
        if (cases != 648 || physicalCases != 108 || partial != 216 || unreachable != 216 ||
            maxPosition > 1e-8 || maxQuaternion > 1e-10 || maxGodotPosition > 2e-6f || maxGodotQuaternion > 2e-6f)
            throw new InvalidOperationException($"TwoBoneIK mismatch: cases={cases} physical={physicalCases} " +
                $"partial={partial} far={unreachable} p={maxPosition} q={maxQuaternion} " +
                $"godotP={maxGodotPosition} godotQ={maxGodotQuaternion}.");
        GD.Print($"LYRA_TWO_BONE_HAND_OK cases={cases} physical={physicalCases} partial={partial} far={unreachable} " +
            $"nativePositionCm={maxPosition} nativeQuaternion={maxQuaternion} godotPositionM={maxGodotPosition} " +
            $"godotQuaternion={maxGodotQuaternion} unarmed=disabled right=keepRotation left=takeRotation skeleton=68");
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

    private static double QuaternionDistance(AlsQuaternion a, AlsQuaternion b)
    {
        a = a.Normalized(); b = b.Normalized();
        var sign = AlsQuaternion.Dot(a, b) < 0 ? -1 : 1;
        return Math.Sqrt((a + b * -sign).LengthSquared);
    }
}
