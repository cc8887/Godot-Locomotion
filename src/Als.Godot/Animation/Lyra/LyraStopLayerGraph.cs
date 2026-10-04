using System.Text.Json;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraStopLayerGraph(int Root, LyraSourceNode Stop, LyraSourceNode HipFire,
    bool BaseFirst, float InitialBlendWeight)
{
    public static LyraStopLayerGraph Load(string profile, LyraSourceNodeCatalog catalog)
    {
        using var document=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/stop_layer_graph.json"));
        var data=document.RootElement;
        if (data.GetProperty("schemaVersion").GetInt32()!=1 || data.GetProperty("sourceNodesSha256").GetString()!=catalog.Sha256)
            throw new InvalidOperationException("Stale original Stop closure.");
        var owner=catalog.ForClass(LyraLinkedLayerInventory.Load().Get(profile).ClassPath);
        var graph=data.GetProperty("graphs").GetProperty(profile);
        var nodes=graph.GetProperty("nodes").EnumerateArray().ToDictionary(n=>n.GetProperty("index").GetInt32());
        var root=graph.GetProperty("root").GetInt32();
        if (nodes.Count!=4 || graph.GetProperty("rootPropertyIndex").GetInt32()!=owner.NodeCount-1-root)
            throw new InvalidOperationException("Invalid Stop root/property mapping.");
        int Follow(int index,string type,string pin)
        {
            var node=nodes[index];
            if (node.GetProperty("type").GetString()!=type) throw new NotSupportedException("Changed Stop topology: "+type);
            var links=node.GetProperty("links").EnumerateArray().ToArray();
            if (links.Any(l=>l.GetProperty("propertyIndex").GetInt32()!=owner.NodeCount-1-l.GetProperty("index").GetInt32()))
                throw new InvalidOperationException("Invalid Stop pose link.");
            return links.Single(l=>l.GetProperty("pin").GetString()==pin).GetProperty("index").GetInt32();
        }
        var blend=Follow(root,"/Script/Engine.AnimNode_Root","Result");
        var stop=Follow(blend,"/Script/AnimGraphRuntime.AnimNode_LayeredBoneBlend","BasePose");
        var hip=Follow(blend,"/Script/AnimGraphRuntime.AnimNode_LayeredBoneBlend","BlendPoses[0]");
        var s=nodes[blend].GetProperty("settings");
        if (s.GetProperty("blendMode").GetString()!="BlendMask" || s.GetProperty("blendPoses").GetArrayLength()!=1 ||
            s.GetProperty("blendMasks")[0].GetString()!="/Game/Characters/Heroes/Mannequin/Meshes/SK_Mannequin.SK_Mannequin:UpperBodyMask" ||
            !s.GetProperty("bMeshSpaceRotationBlend").GetBoolean() || s.GetProperty("bMeshSpaceScaleBlend").GetBoolean() ||
            s.GetProperty("bRootSpaceRotationBlend").GetBoolean() || s.GetProperty("curveBlendOption").GetString()!="Override" ||
            !s.GetProperty("bBlendRootMotionBasedOnRootBone").GetBoolean() || s.GetProperty("lODThreshold").GetInt32()!=-1 ||
            s.GetProperty("perBoneBlendWeights")[0].GetProperty("blendWeight").GetSingle()!=0 ||
            owner.Nodes[stop].Functions.BecomeRelevant!="SetUpStopAnim" || owner.Nodes[stop].Functions.Update!="UpdateStopAnim" ||
            owner.Nodes[hip].Functions.Update!="UpdateHipFireRaiseWeaponPose" || !owner.Nodes[hip].IgnoreRelevancy ||
            owner.Nodes[hip].Method!=LyraSourceSyncMethod.DoNotSync)
            throw new NotSupportedException("Changed Stop blend/source policy.");
        return new(root,owner.Nodes[stop],owner.Nodes[hip],s.GetProperty("bUpdateBasePoseFirst").GetBoolean(),s.GetProperty("blendWeights")[0].GetSingle());
    }
}
