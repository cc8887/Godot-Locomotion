using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// Real Manny logical atoms validate the operator and resource dependency chain.
// This scene does not pretend the missing ALS control channels are in production.
public partial class LyraWeaponSpaceSmoke : Node
{
    private const string Root = "res://assets/generated/lyra_als/";

    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Weapon-space native smoke failed: " + error); GetTree().Quit(1); }
    }

    private static void Run()
    {
        var sourceBytes = Godot.FileAccess.GetFileAsBytes(Root + "weapon_space_sources.json");
        using var sources = JsonDocument.Parse(sourceBytes);
        var sourceRoot = sources.RootElement;
        if (sourceRoot.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidOperationException("Unsupported source control schema.");
        foreach (var dependency in sourceRoot.GetProperty("dependencySha256").EnumerateObject())
            if (Sha(Godot.FileAccess.GetFileAsBytes(Root + dependency.Name)) != dependency.Value.GetString())
                throw new InvalidOperationException("Stale source control dependency: " + dependency.Name);
        var ordinary = 0; var additive = 0; var sampled = 0;
        foreach (var clip in sourceRoot.GetProperty("clips").EnumerateArray())
        {
            if (clip.GetProperty("additive").GetBoolean()) additive++; else ordinary++;
            if (!clip.GetProperty("names").EnumerateArray().Select(v => v.GetString()).SequenceEqual(
                new[] { "hand_r", "hand_l", "weapon_r", "VB IK_Hand_L_weaponSpace" }))
                throw new InvalidOperationException("Control channel order changed.");
            var samples = clip.GetProperty("samples");
            if (samples.GetArrayLength() != 2 * clip.GetProperty("keyCount").GetInt32() - 1)
                throw new InvalidOperationException("Missing keys or interval midpoints.");
            double previous = -1;
            foreach (var sample in samples.EnumerateArray())
            {
                var seconds = sample.GetProperty("seconds").GetDouble();
                if (!double.IsFinite(seconds) || seconds <= previous)
                    throw new InvalidOperationException("Control times are not strictly ordered.");
                previous = seconds;
                if (sample.GetProperty("local").GetArrayLength() != 4)
                    throw new InvalidOperationException("Missing control atom.");
                foreach (var atom in sample.GetProperty("local").EnumerateArray()) Parse(atom);
                sampled++;
            }
        }
        using var native = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root + "weapon_space_native.json"));
        using var scaled = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root + "weapon_space_native_scaled.json"));
        var root = native.RootElement;
        if (root.GetProperty("sourceDataSha256").GetString() != Sha(sourceBytes) ||
            root.GetProperty("nativeOperator").GetString() !=
            "FAnimNode_CopyBone ComponentSpace translation+rotation + FCSPose.LocalBlendCSBoneTransforms")
            throw new InvalidOperationException("Stale or changed native CopyBone oracle.");
        var layout = root.GetProperty("layout");
        var parents = layout.GetProperty("logicalParents").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        var names = layout.GetProperty("logicalBoneNames").EnumerateArray().Select(v => v.GetString()).ToArray();
        var source = Array.IndexOf(names, "VB IK_Hand_L_weaponSpace");
        var target = Array.IndexOf(names, "ik_hand_l");
        var controller = new AlsComponentCopyBoneController(parents, source, target, true, true, false);
        var output = new AlsPrecisePose[parents.Length];
        var cases = 0; var scaledCases = 0; double maxPosition = 0, maxQuaternion = 0, maxScale = 0;
        foreach (var oracle in new[] { root, scaled.RootElement })
        {
            if (oracle.GetProperty("schemaVersion").GetInt32() != 1 ||
                oracle.GetProperty("sourceDataSha256").GetString() != Sha(sourceBytes) ||
                oracle.GetProperty("nativeOperator").GetString() != root.GetProperty("nativeOperator").GetString() ||
                oracle.GetProperty("layout").GetRawText() != layout.GetRawText())
                throw new InvalidOperationException("Scaled CopyBone oracle is stale or changed.");
            foreach (var row in oracle.GetProperty("rows").EnumerateArray())
            {
                var input = row.GetProperty("before").EnumerateArray().Select(Parse).ToArray();
                var frozen = (AlsPrecisePose[])input.Clone();
                var expected = row.GetProperty("after").EnumerateArray().Select(Parse).ToArray();
                controller.Evaluate(input, row.GetProperty("alpha").GetSingle(), output);
                if (!input.SequenceEqual(frozen)) throw new InvalidOperationException("CopyBone modified its input.");
                if (Math.Sqrt((output[target].Scale - input[target].Scale).LengthSquared) > 1e-12)
                    throw new InvalidOperationException("CopyBone copied source scale despite copyScale=false.");
                for (var bone = 0; bone < parents.Length; bone++)
                {
                    maxPosition = Math.Max(maxPosition, Math.Sqrt((output[bone].Position - expected[bone].Position).LengthSquared));
                    var a = output[bone].Rotation.Normalized(); var b = expected[bone].Rotation.Normalized();
                    var sign = AlsQuaternion.Dot(a, b) < 0 ? -1 : 1;
                    maxQuaternion = Math.Max(maxQuaternion, Math.Sqrt((a + b * -sign).LengthSquared));
                    maxScale = Math.Max(maxScale, Math.Sqrt((output[bone].Scale - expected[bone].Scale).LengthSquared));
                }
                cases++;
                if (oracle.TryGetProperty("fixture", out _)) scaledCases++;
            }
        }
        if (ordinary != 189 || additive != 45 || cases != 432 || scaledCases != 48 || parents.Length != 164 ||
            maxPosition > 1e-8 || maxQuaternion > 1e-10 || maxScale > 1e-12)
            throw new InvalidOperationException($"Weapon-space mismatch ordinary={ordinary} additive={additive} " +
                $"cases={cases} p={maxPosition} q={maxQuaternion} s={maxScale}.");
        GD.Print($"LYRA_WEAPON_SPACE_NATIVE_OK ordinary={ordinary} additive={additive} samples={sampled} " +
            $"cases={cases} scaled={scaledCases} logical=164 positionCm={maxPosition} quaternion={maxQuaternion} scale={maxScale} " +
            "operator=CopyBoneComponentSpace targetScale=preserved productionLeftIk=pending");
    }

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static AlsPrecisePose Parse(JsonElement atom)
    {
        var p = atom.GetProperty("position"); var q = atom.GetProperty("rotation"); var s = atom.GetProperty("scale");
        var pose = new AlsPrecisePose(new(p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble()),
            new(q[0].GetDouble(), q[1].GetDouble(), q[2].GetDouble(), q[3].GetDouble()),
            new(s[0].GetDouble(), s[1].GetDouble(), s[2].GetDouble()));
        pose.Validate(1e-5); return pose;
    }
}
