using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;

namespace GodotAls.Animation.Lyra;

internal static class LyraPoseLayerContracts
{
    private static readonly Dictionary<string, string> GraphHashes = new(StringComparer.Ordinal)
    {
        ["FullBodyAdditives"] = "2ec9500c1db0116fa503e785b68fbd298b6eece84f947dc12e08693b4db3d869",
        ["LeftHandPose_OverrideState"] = "cd7e6867ac2d710d87660afd3b6760522a372410366954634969f2de664f2525",
        ["FullBody_SkeletalControls"] = "6224d294e6daa91ac8d42ad42e92596dcd4a01757fc2412dfbce496c7f4b545e",
    };
    private static readonly Dictionary<string, string> FunctionHashes = new(StringComparer.Ordinal)
    {
        ["SetLeftHandPoseOverrideWeight"] = "a8b9e161d2783edd8904a4ae91c3ca090ed66904b63cdcdc2b0036fcea4c4e62",
        ["UpdateSkelControlData"] = "b96266e81c9522772d1cc2f417f03c73ceb6bdad20c1ab827b6d8775934b0416",
    };

    public static void Validate()
    {
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(
            "res://assets/generated/lyra_als/pose_layer_contracts.json"));
        var root = document.RootElement;
        var inventoryHash = Convert.ToHexString(SHA256.HashData(Godot.FileAccess.GetFileAsBytes(
            "res://assets/generated/lyra_als/linked_layer_inventory.json"))).ToLowerInvariant();
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("inventorySha256").GetString() != inventoryHash ||
            root.GetProperty("additive").GetProperty("initial").GetString() != "Identity" ||
            root.GetProperty("additive").GetProperty("air").GetString() != "AirIdentity" ||
            root.GetProperty("additive").GetProperty("landingEdgeEnabled").GetBoolean())
            throw new InvalidOperationException("Lyra pose-layer source contract changed.");
        foreach (var (group, hashes) in new[] { ("graphs", GraphHashes), ("functions", FunctionHashes) })
        {
            if (root.GetProperty(group).EnumerateObject().Count() != hashes.Count)
                throw new InvalidOperationException("Incomplete Lyra pose-layer source contract.");
            foreach (var (name, expected) in hashes)
            {
                var row = root.GetProperty(group).GetProperty(name);
                var text = row.GetProperty("text").GetString()!;
                if (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant() != expected ||
                    row.GetProperty("sha256").GetString() != expected)
                    throw new InvalidOperationException("Lyra pose graph hash differs: " + name);
            }
        }
        var left = root.GetProperty("leftHand");
        if (left.GetProperty("curve").GetString() != "DisableLeftHandPoseOverride" ||
            left.GetProperty("mask").GetString() != "LeftFingersMask" ||
            left.GetProperty("meshSpaceRotation").GetBoolean() ||
            left.GetProperty("evaluatorTimeSeconds").GetDouble() != 0)
            throw new InvalidOperationException("Lyra left-hand blend policy differs.");
        var inventory = LyraLinkedLayerInventory.Load();
        if (left.GetProperty("profiles").EnumerateObject().Count() != inventory.Count)
            throw new InvalidOperationException("Incomplete Lyra left-hand profile inventory.");
        foreach (var row in left.GetProperty("profiles").EnumerateObject())
        {
            var profile = inventory.Get(row.Name);
            if (profile.ClassPath != row.Value.GetProperty("class").GetString() ||
                profile.EnableLeftHandPoseOverride != row.Value.GetProperty("enabled").GetBoolean() ||
                profile.Asset("LeftHandPose_Override") != row.Value.GetProperty("source").GetString())
                throw new InvalidOperationException("Lyra left-hand profile differs: " + row.Name);
        }
    }
}
