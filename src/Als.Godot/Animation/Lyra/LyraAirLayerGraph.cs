namespace GodotAls.Animation.Lyra;

internal enum LyraAirLayer { JumpStart, JumpStartLoop, JumpApex, FallLoop, FallLand }
internal sealed record LyraAirLayerGraph(LyraAirLayer Layer, int Root, LyraSourceNode Base, LyraSourceNode Hip,
    bool BaseFirst, float InitialBlendWeight)
{
    public static LyraLayerHook Hook(LyraAirLayer layer) => layer switch
    {
        LyraAirLayer.JumpStart=>LyraLayerHook.FullBody_JumpStartState,
        LyraAirLayer.JumpStartLoop=>LyraLayerHook.FullBody_JumpStartLoopState,
        LyraAirLayer.JumpApex=>LyraLayerHook.FullBody_JumpApexState,
        LyraAirLayer.FallLoop=>LyraLayerHook.FullBody_FallLoopState,
        LyraAirLayer.FallLand=>LyraLayerHook.FullBody_FallLandState,
        _=>throw new ArgumentOutOfRangeException(nameof(layer)),
    };
    public static LyraAirLayerGraph Load(string profile,LyraAirLayer layer,LyraSourceNodeCatalog catalog,
        LyraLocomotionLayerInventory inventory)
    {
        var provider=inventory.Providers[profile];var owner=catalog.ForClass(provider.ClassPath);var g=provider.Layers[Hook(layer)];
        int Follow(int index,string type,string pin)
        {
            var n=g.Nodes[index];if(n.NativeType!=type)throw new NotSupportedException("Changed Air topology.");
            return n.Links.Single(l=>l.Pin==pin).Node;
        }
        var blend=Follow(g.Root,"/Script/Engine.AnimNode_Root","Result");
        var b=Follow(blend,"/Script/AnimGraphRuntime.AnimNode_LayeredBoneBlend","BasePose");
        var h=Follow(blend,"/Script/AnimGraphRuntime.AnimNode_LayeredBoneBlend","BlendPoses[0]");var s=g.Nodes[blend].Settings;
        if(g.Nodes.Count!=4 || s.GetProperty("blendMode").GetString()!="BlendMask" || s.GetProperty("blendMasks").GetArrayLength()!=1 ||
            s.GetProperty("blendMasks")[0].GetString()!="/Game/Characters/Heroes/Mannequin/Meshes/SK_Mannequin.SK_Mannequin:UpperBodyMask" ||
            !s.GetProperty("bMeshSpaceRotationBlend").GetBoolean() || s.GetProperty("bMeshSpaceScaleBlend").GetBoolean() ||
            s.GetProperty("bRootSpaceRotationBlend").GetBoolean() || s.GetProperty("curveBlendOption").GetString()!="Override" ||
            !s.GetProperty("bBlendRootMotionBasedOnRootBone").GetBoolean() || s.GetProperty("lODThreshold").GetInt32()!=-1)
            throw new NotSupportedException("Changed Air blend policy.");
        var source=owner.Nodes[b];var hip=owner.Nodes[h];var fall=layer==LyraAirLayer.FallLand;
        if(hip.Kind!=LyraSourceKind.SequenceEvaluator || hip.Functions!=new LyraSourceFunctions("None","None","UpdateHipFireRaiseWeaponPose") ||
            !hip.IgnoreRelevancy || hip.Method!=LyraSourceSyncMethod.DoNotSync || hip.Group!="None" || !hip.Looping ||
            hip.Role!=LyraSourceGroupRole.CanBeLeader || hip.OverridePositionWhenJoining || source.OverridePositionWhenJoining ||
            source.Kind!=(fall?LyraSourceKind.SequenceEvaluator:LyraSourceKind.SequencePlayer) || source.IgnoreRelevancy ||
            source.Functions!=(fall?new LyraSourceFunctions("None","SetUpFallLandAnim","UpdateFallLandAnim"):new("None","None","None")) ||
            source.Group!=(fall?"Locomotion":"None") || source.Method!=(fall?LyraSourceSyncMethod.SyncGroup:LyraSourceSyncMethod.DoNotSync) ||
            source.Role!=(fall?LyraSourceGroupRole.AlwaysLeader:LyraSourceGroupRole.CanBeLeader) ||
            source.Looping!=(layer is LyraAirLayer.JumpStartLoop or LyraAirLayer.FallLoop))
            throw new NotSupportedException("Changed Air source identity.");
        if(!fall && (source.Settings.GetProperty("playRate").GetSingle()!=1 || source.Settings.GetProperty("playRateBasis").GetSingle()!=1 ||
            source.Settings.GetProperty("startPosition").GetSingle()!=0 || source.Settings.GetProperty("startFromMatchingPose").GetBoolean()))
            throw new NotSupportedException("Unsupported Air player clock policy.");
        if(!fall)
        {
            var clamp=source.Settings.GetProperty("playRateScaleBiasClamp");
            if(clamp.GetProperty("bMapRange").GetBoolean() || clamp.GetProperty("bClampResult").GetBoolean() ||
                clamp.GetProperty("bInterpResult").GetBoolean() || clamp.GetProperty("scale").GetSingle()!=1 || clamp.GetProperty("bias").GetSingle()!=0)
                throw new NotSupportedException("Changed Air rate transform.");
        }
        foreach(var node in fall?new[]{source,hip}:new[]{hip})
        {
            if(node.Settings.GetProperty("explicitTime").GetSingle()!=0 || node.Settings.GetProperty("startPosition").GetSingle()!=0 ||
                node.Settings.GetProperty("reinitialization").GetInt32()!=2 || node.Settings.GetProperty("teleport").GetBoolean()!=(node==hip) ||
                g.Nodes[node.Index].Settings.GetProperty("bUseExplicitFrame").GetBoolean())
                throw new NotSupportedException("Changed Air evaluator policy.");
        }
        return new(layer,g.Root,source,hip,s.GetProperty("bUpdateBasePoseFirst").GetBoolean(),s.GetProperty("blendWeights")[0].GetSingle());
    }
}
