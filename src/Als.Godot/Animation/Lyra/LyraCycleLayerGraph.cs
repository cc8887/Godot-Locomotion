using System.Text.Json;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraCycleLayerGraph(int Root, LyraSourceNode Cycle, LyraSourceNode HipFire,
    bool BaseFirst, float InitialBlendWeight)
{
    public static LyraCycleLayerGraph Load(string profileName, LyraSourceNodeCatalog catalog)
    {
        var bytes = Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/cycle_layer_graph.json");
        using var document = JsonDocument.Parse(bytes); var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("sourceNodesSha256").GetString() != catalog.Sha256)
            throw new InvalidOperationException("Stale native Cycle closure.");
        var owner = catalog.ForClass(LyraLinkedLayerInventory.Load().Get(profileName).ClassPath);
        var graph = root.GetProperty("graphs").GetProperty(profileName);
        var nodes = graph.GetProperty("nodes").EnumerateArray().ToDictionary(n => n.GetProperty("index").GetInt32());
        if (nodes.Count != 8 || graph.GetProperty("rootPropertyIndex").GetInt32() != owner.NodeCount - 1 - graph.GetProperty("root").GetInt32())
            throw new InvalidOperationException("Invalid Cycle native/property index translation.");
        int Follow(int index, string type, string pin)
        {
            var node = nodes[index];
            if (node.GetProperty("type").GetString() != type) throw new InvalidOperationException("Changed Cycle topology: " + type);
            var links = node.GetProperty("links").EnumerateArray().ToArray();
            if (links.Any(l => l.GetProperty("propertyIndex").GetInt32() != owner.NodeCount - 1 - l.GetProperty("index").GetInt32()))
                throw new InvalidOperationException("Invalid native pose link index.");
            return links.Single(l => l.GetProperty("pin").GetString() == pin).GetProperty("index").GetInt32();
        }
        var rootIndex = graph.GetProperty("root").GetInt32();
        var index = Follow(rootIndex, "/Script/Engine.AnimNode_Root", "Result");
        index = Follow(index, "/Script/Engine.AnimNode_ConvertComponentToLocalSpace", "ComponentPose");
        index = Follow(index, "/Script/AnimationWarpingRuntime.AnimNode_StrideWarping", "ComponentPose");
        index = Follow(index, "/Script/AnimationWarpingRuntime.AnimNode_OrientationWarping", "ComponentPose");
        index = Follow(index, "/Script/Engine.AnimNode_ConvertLocalToComponentSpace", "LocalPose");
        var cycleIndex = Follow(index, "/Script/AnimGraphRuntime.AnimNode_LayeredBoneBlend", "BasePose");
        var hipFireIndex = Follow(index, "/Script/AnimGraphRuntime.AnimNode_LayeredBoneBlend", "BlendPoses[0]");
        var settings = nodes[index].GetProperty("settings");
        if (settings.GetProperty("blendPoses").GetArrayLength() != 1 || settings.GetProperty("blendMode").GetString() != "BlendMask" ||
            settings.GetProperty("blendMasks")[0].GetString() != "/Game/Characters/Heroes/Mannequin/Meshes/SK_Mannequin.SK_Mannequin:UpperBodyMask" ||
            !settings.GetProperty("bMeshSpaceRotationBlend").GetBoolean() || settings.GetProperty("lODThreshold").GetInt32() != -1 ||
            settings.GetProperty("bMeshSpaceScaleBlend").GetBoolean() || settings.GetProperty("bRootSpaceRotationBlend").GetBoolean() ||
            settings.GetProperty("curveBlendOption").GetString() != "Override" || !settings.GetProperty("bBlendRootMotionBasedOnRootBone").GetBoolean() ||
            settings.GetProperty("perBoneBlendWeights")[0].GetProperty("blendWeight").GetSingle() != 0 ||
            owner.Nodes[cycleIndex].Functions.Update != "UpdateCycleAnim" ||
            owner.Nodes[hipFireIndex].Functions.Update != "UpdateHipFireRaiseWeaponPose" ||
            owner.Nodes[hipFireIndex].Method != LyraSourceSyncMethod.DoNotSync || !owner.Nodes[hipFireIndex].IgnoreRelevancy)
            throw new NotSupportedException("Changed Cycle blend/source policy.");
        return new(rootIndex, owner.Nodes[cycleIndex], owner.Nodes[hipFireIndex],
            settings.GetProperty("bUpdateBasePoseFirst").GetBoolean(), settings.GetProperty("blendWeights")[0].GetSingle());
    }
}
