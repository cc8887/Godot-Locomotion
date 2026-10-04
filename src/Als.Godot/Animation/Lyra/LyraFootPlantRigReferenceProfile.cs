using System.Text.Json;

namespace GodotAls.Animation.Lyra;

internal enum LyraFootPlantRigReference { AuthoredRig, AlsCompactReference }

// Immutable target binding, separate from the authored program and oracle.
// The native opt-in copies InitialLocal by imported bone name. It retains
// the Rig topology and unmatched reference bones; Construction owns offsets.
internal sealed class LyraFootPlantRigReferenceProfile
{
    private readonly (string Name, int Target)[] _mapping;
    private readonly LyraLogicalSourceBank _bank;
    public string Name { get; }
    private LyraFootPlantRigReferenceProfile(LyraLogicalSourceBank bank, JsonElement policy)
    {
        if (policy.GetProperty("schemaVersion").GetInt32() != 1 ||
            policy.GetProperty("binding").GetString() != "SetBoneInitialTransformsFromCompactPose")
            throw new NotSupportedException("Changed Rig target-reference binding.");
        const string root = "res://assets/generated/lyra_als/";
        foreach (var dependency in policy.GetProperty("dependencies").EnumerateObject())
            if (dependency.Value.GetString() != LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root + dependency.Name)))
                throw new InvalidOperationException("Stale Rig reference dependency: " + dependency.Name);
        if (bank.CalibrationSha256 != policy.GetProperty("dependencies").GetProperty("logical_controls/calibration.json").GetString())
            throw new InvalidOperationException("Foreign Rig reference source bank.");
        using var calibration = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root + "logical_controls/calibration.json"));
        var names = calibration.RootElement.GetProperty("layout").GetProperty("logicalBoneNames");
        _bank = bank; Name = policy.GetProperty("name").GetString()!;
        var rows = policy.GetProperty("mapping").EnumerateArray().ToArray();
        if (rows.Length != 91 ||
            !rows.Where(r => !r.GetProperty("imported").GetBoolean()).Select(r => r.GetProperty("name").GetString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(new[] { "ik_ball_l", "ik_ball_r" }) ||
            rows.Any(r => !r.GetProperty("imported").GetBoolean() && r.GetProperty("targetIndex").GetInt32() != -1) ||
            rows.Select(r => r.GetProperty("name").GetString()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 91)
            throw new NotSupportedException("Changed native imported Rig bones.");
        _mapping = rows.Select(row => (Name: row.GetProperty("name").GetString()!, Target: row.GetProperty("targetIndex").GetInt32())).ToArray();
        foreach (var (name, target) in _mapping)
        {
            int expected = names.EnumerateArray().Select((n, i) => (Name: n.GetString(), Index: i))
                .Where(n => StringComparer.OrdinalIgnoreCase.Equals(n.Name, name)).Select(n => n.Index).DefaultIfEmpty(-1).Single();
            if (target != expected || target >= 0 && bank.Bone(name) != target)
                throw new NotSupportedException("Foreign Rig reference target bone: " + name);
        }
        if (_mapping.Count(m => m.Target >= 0) != 69 || bank.Reference.Length != 81)
            throw new NotSupportedException("Incomplete ALS Rig reference mapping.");
    }
    public static LyraFootPlantRigReferenceProfile Load(LyraLogicalSourceBank bank)
    {
        using var policy = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(
            "res://assets/generated/lyra_als/rig_reference_v1_policy.json"));
        return new(bank, policy.RootElement);
    }
    public void Apply(LyraFootPlantRigHierarchy hierarchy)
    {
        // Validate the complete target before changing any candidate storage.
        foreach (var (name, target) in _mapping)
        {
            _ = hierarchy.Get(name, local: true, initial: true);
            if (target >= 0) _bank.Reference[target].Validate();
        }
        foreach (var (name, target) in _mapping)
            if (target >= 0) hierarchy.Set(name, _bank.Reference[target], local: true, initial: true);
    }
}
