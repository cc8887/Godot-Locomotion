using System.Text.Json;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraStartLayerGraph(int Root, LyraSourceNode Start, LyraSourceNode HipFire,
    bool BaseFirst, float InitialBlendWeight)
{
    public static LyraStartLayerGraph Load(string profile, LyraSourceNodeCatalog catalog)
    {
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(
            "res://assets/generated/lyra_als/start_layer_graph.json"));
        var data = document.RootElement;
        if (data.GetProperty("schemaVersion").GetInt32() != 1 || data.GetProperty("sourceNodesSha256").GetString() != catalog.Sha256)
            throw new InvalidOperationException("Stale original Start closure.");
        var owner = catalog.ForClass(LyraLinkedLayerInventory.Load().Get(profile).ClassPath);
        var graph = data.GetProperty("graphs").GetProperty(profile);
        var nodes = graph.GetProperty("nodes").EnumerateArray().ToDictionary(n => n.GetProperty("index").GetInt32());
        var root = graph.GetProperty("root").GetInt32();
        if (nodes.Count != 8 || graph.GetProperty("rootPropertyIndex").GetInt32() != owner.NodeCount-1-root)
            throw new InvalidOperationException("Invalid Start root/property mapping.");
        int Follow(int index, string type, string pin)
        {
            var node = nodes[index];
            if (node.GetProperty("type").GetString() != type) throw new NotSupportedException("Changed Start topology: " + type);
            var links = node.GetProperty("links").EnumerateArray().ToArray();
            if (links.Any(l => l.GetProperty("propertyIndex").GetInt32() != owner.NodeCount-1-l.GetProperty("index").GetInt32()))
                throw new InvalidOperationException("Invalid Start pose link.");
            return links.Single(l => l.GetProperty("pin").GetString() == pin).GetProperty("index").GetInt32();
        }
        var index = Follow(root, "/Script/Engine.AnimNode_Root", "Result");
        index = Follow(index, "/Script/Engine.AnimNode_ConvertComponentToLocalSpace", "ComponentPose");
        index = Follow(index, "/Script/AnimationWarpingRuntime.AnimNode_StrideWarping", "ComponentPose");
        index = Follow(index, "/Script/AnimationWarpingRuntime.AnimNode_OrientationWarping", "ComponentPose");
        index = Follow(index, "/Script/Engine.AnimNode_ConvertLocalToComponentSpace", "LocalPose");
        var start = Follow(index, "/Script/AnimGraphRuntime.AnimNode_LayeredBoneBlend", "BasePose");
        var hip = Follow(index, "/Script/AnimGraphRuntime.AnimNode_LayeredBoneBlend", "BlendPoses[0]");
        var settings = nodes[index].GetProperty("settings");
        if (settings.GetProperty("blendMode").GetString() != "BlendMask" || settings.GetProperty("blendPoses").GetArrayLength() != 1 ||
            settings.GetProperty("blendMasks")[0].GetString() != "/Game/Characters/Heroes/Mannequin/Meshes/SK_Mannequin.SK_Mannequin:UpperBodyMask" ||
            !settings.GetProperty("bMeshSpaceRotationBlend").GetBoolean() || settings.GetProperty("bMeshSpaceScaleBlend").GetBoolean() ||
            settings.GetProperty("bRootSpaceRotationBlend").GetBoolean() || settings.GetProperty("curveBlendOption").GetString() != "Override" ||
            !settings.GetProperty("bBlendRootMotionBasedOnRootBone").GetBoolean() || settings.GetProperty("lODThreshold").GetInt32() != -1 ||
            settings.GetProperty("perBoneBlendWeights")[0].GetProperty("blendWeight").GetSingle() != 0 ||
            owner.Nodes[start].Functions.BecomeRelevant != "SetUpStartAnim" || owner.Nodes[start].Functions.Update != "UpdateStartAnim" ||
            owner.Nodes[hip].Functions.Update != "UpdateHipFireRaiseWeaponPose" || !owner.Nodes[hip].IgnoreRelevancy ||
            owner.Nodes[hip].Method != LyraSourceSyncMethod.DoNotSync)
            throw new NotSupportedException("Changed Start blend/source policy.");
        return new(root, owner.Nodes[start], owner.Nodes[hip], settings.GetProperty("bUpdateBasePoseFirst").GetBoolean(),
            settings.GetProperty("blendWeights")[0].GetSingle());
    }
}
