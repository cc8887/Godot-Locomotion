using System.Text.Json;

namespace GodotAls.Animation.Lyra;

// Configuration exported from the actual native node/skeleton/settings.
// Contains no pose samples or animation clocks.
internal sealed record LyraCycleLayerPosePolicy(float[] Mask, int[] CurveSources, int[] AttributeBones, bool[] AttributeOverrides)
{
    public static LyraCycleLayerPosePolicy Load(string profile, LyraLogicalSourceBank bank)
    {
        const string rootPath = "res://assets/generated/lyra_als/";
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(rootPath + "cycle_layer_pose_policy.json"));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("stage").GetString() != "AuthoredPreWarp" ||
            root.GetProperty("generatedRootMotion").GetBoolean()) throw new InvalidOperationException("Unsupported Cycle pose policy.");
        foreach (var dependency in root.GetProperty("dependencies").EnumerateObject())
            if (dependency.Value.GetString() != LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(rootPath + dependency.Name)))
                throw new InvalidOperationException("Stale Cycle pose dependency: " + dependency.Name);
        var row = root.GetProperty("policies").GetProperty(profile);
        var mask = row.GetProperty("mask").EnumerateArray().Select(v => v.GetSingle()).ToArray();
        if (mask.Length != 81 || mask[0] != 0 || mask.Any(w => !float.IsFinite(w) || w is < 0 or > 1))
            throw new InvalidOperationException("Invalid Cycle logical mask.");
        // Independently check the native name-mapped mask against the immutable
        // Manny metadata and ALS skin identities; ALS-only VBs remain zero.
        using var masks = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(rootPath + "unarmed_layer_masks.json"));
        var authored = masks.RootElement.GetProperty("profiles").GetProperty("UpperBodyMask");
        foreach (var bone in authored.GetProperty("alsBones").EnumerateArray())
            if (bank.Bone(bone.GetProperty("bone").GetString()!) != bone.GetProperty("index").GetInt32() ||
                mask[bone.GetProperty("index").GetInt32()] != bone.GetProperty("scale").GetSingle())
                throw new InvalidOperationException("Changed Cycle skin mask identity.");
        foreach (var name in new[] { "weapon_r", "VB IK_Hand_L_weaponSpace" })
            if (mask[bank.Bone(name)] != authored.GetProperty("sourceBones").EnumerateArray()
                    .Single(b => b.GetProperty("bone").GetString() == name).GetProperty("scale").GetSingle())
                throw new InvalidOperationException("Changed Cycle control mask.");
        for (var bone = 69; bone < 80; bone++) if (mask[bone] != 0) throw new InvalidOperationException("Unexpected ALS-only VB mask.");
        var curveSources = Enumerable.Repeat(-1, bank.Curves.Names.Length).ToArray();
        foreach (var curve in row.GetProperty("curveBindings").EnumerateObject())
        {
            var id = bank.Curves.Index(curve.Name); var source = curve.Value.GetInt32();
            if (source != 0) throw new NotSupportedException("Unsupported Cycle curve source.");
            if (id >= 0) curveSources[id] = source;
        }
        var attributes = row.GetProperty("attributes").EnumerateArray().ToArray();
        var layout = bank.Curves.Attributes.Layout;
        if (attributes.Length != layout.Length) throw new InvalidOperationException("Incomplete Cycle attribute policy.");
        var attributeBones = new int[layout.Length]; var overrides = new bool[layout.Length];
        for (var id = 0; id < layout.Length; id++)
        {
            var identity = layout[id];
            var attribute = attributes.Single(a => a.GetProperty("name").GetString() == identity.Name &&
                a.GetProperty("bone").GetString()!.Equals(identity.Bone, StringComparison.OrdinalIgnoreCase) &&
                a.GetProperty("type").GetString() == identity.Type && a.GetProperty("namespace").GetString() == identity.Namespace);
            attributeBones[id] = bank.Bone(identity.Bone);
            overrides[id] = attribute.GetProperty("blend").GetString() switch
            { "Override" => true, "Blend" => false, _ => throw new NotSupportedException("Unknown native attribute blend mode.") };
        }
        return new(mask, curveSources, attributeBones, overrides);
    }
}
