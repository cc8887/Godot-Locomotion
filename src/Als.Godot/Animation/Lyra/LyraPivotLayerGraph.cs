using System.Text.Json;
using System.Collections.Immutable;

namespace GodotAls.Animation.Lyra;

// Bind the pair through the original compiled PivotA/B state links. The
// machine factory derives visits, weights and inertia from its original states.
internal readonly record struct LyraPivotWarpGraph(int Orientation, int Stride);
internal sealed record LyraPivotLayerGraph(LyraPoseClosure Closure, LyraSourceNode PivotA,
    LyraSourceNode PivotB, LyraSourceNode HipFire, int MachineNode, int RuleNode, JsonElement Machine,
    ImmutableArray<LyraPivotWarpGraph> Warps, float InitialBlendWeight)
{
    public static LyraPivotLayerGraph Load(string profile,LyraSourceNodeCatalog catalog,
        LyraLocomotionLayerInventory inventory)
    {
        var provider=inventory.Providers[profile]; var owner=catalog.ForClass(provider.ClassPath);
        var closure=provider.Layers[LyraLayerHook.FullBody_PivotState];
        if (closure.Nodes.Count!=16) throw new NotSupportedException("Changed Pivot pose closure.");
        int Follow(int index,string type,string pin)
        {
            var node=closure.Nodes[index];
            if (node.NativeType!=type) throw new NotSupportedException("Changed Pivot node: "+type);
            return node.Links.Single(l=>l.Pin==pin).Node;
        }
        var blend=Follow(closure.Root,"/Script/Engine.AnimNode_Root","Result");
        var machine=Follow(blend,"/Script/AnimGraphRuntime.AnimNode_LayeredBoneBlend","BasePose");
        var hip=Follow(blend,"/Script/AnimGraphRuntime.AnimNode_LayeredBoneBlend","BlendPoses[0]");
        var blendSettings=closure.Nodes[blend].Settings;
        if (blendSettings.GetProperty("bUpdateBasePoseFirst").GetBoolean() ||
            !blendSettings.GetProperty("bMeshSpaceRotationBlend").GetBoolean() ||
            blendSettings.GetProperty("bMeshSpaceScaleBlend").GetBoolean() ||
            blendSettings.GetProperty("bRootSpaceRotationBlend").GetBoolean() ||
            blendSettings.GetProperty("blendMode").GetString()!="BlendMask" ||
            blendSettings.GetProperty("curveBlendOption").GetString()!="Override" ||
            blendSettings.GetProperty("blendMasks").GetArrayLength()!=1 ||
            blendSettings.GetProperty("blendMasks")[0].GetString()!=
                "/Game/Characters/Heroes/Mannequin/Meshes/SK_Mannequin.SK_Mannequin:UpperBodyMask" ||
            !blendSettings.GetProperty("bBlendRootMotionBasedOnRootBone").GetBoolean())
            throw new NotSupportedException("Changed Pivot outer blend policy.");
        var settings=closure.Nodes[machine].Settings;
        if (closure.Nodes[machine].NativeType!="/Script/Engine.AnimNode_StateMachine" ||
            settings.GetProperty("maxTransitionsPerFrame").GetInt32()!=1 ||
            !settings.GetProperty("bSkipFirstUpdateTransition").GetBoolean() ||
            !settings.GetProperty("bReinitializeOnBecomingRelevant").GetBoolean())
            throw new NotSupportedException("Changed Pivot machine update policy.");
        var warps=new LyraPivotWarpGraph[2];
        int Source(string state)
        {
            var root=closure.Nodes[machine].Links.Single(l=>l.Pin=="State["+state+"]" && l.CompiledState).Node;
            var index=Follow(root,"/Script/Engine.AnimNode_StateResult","Result");
            index=Follow(index,"/Script/Engine.AnimNode_ConvertComponentToLocalSpace","ComponentPose");
            var stride=index; index=Follow(index,"/Script/AnimationWarpingRuntime.AnimNode_StrideWarping","ComponentPose");
            var orientation=index; index=Follow(index,"/Script/AnimationWarpingRuntime.AnimNode_OrientationWarping","ComponentPose");
            warps[state=="PivotA" ? 0 : 1]=new(orientation,stride);
            return Follow(index,"/Script/Engine.AnimNode_ConvertLocalToComponentSpace","LocalPose");
        }
        var a=Source("PivotA"); var b=Source("PivotB");
        using var document=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/runtime_graph.json"));
        var definition=document.RootElement.GetProperty("classes").GetProperty(profile).GetProperty("machines")
            .EnumerateArray().Single(m=>m.GetProperty("machineIndex").GetInt32()==settings.GetProperty("stateMachineIndexInClass").GetInt32());
        if (definition.GetProperty("machineName").GetString()!="PivotSM" || definition.GetProperty("initialState").GetInt32()!=0 ||
            definition.GetProperty("states").GetArrayLength()!=2 || definition.GetProperty("transitions").GetArrayLength()!=2)
            throw new NotSupportedException("Changed original Pivot machine.");
        var rule=-1;
        for (var i=0;i<2;i++)
        {
            var state=definition.GetProperty("states")[i]; var edge=state.GetProperty("transitions")[0];
            var player=i==0 ? a : b;
            if (state.GetProperty("playerNodeIndices").GetArrayLength()!=1 || state.GetProperty("playerNodeIndices")[0].GetInt32()!=player ||
                state.GetProperty("transitions").GetArrayLength()!=1 || state.GetProperty("bAlwaysResetOnEntry").GetBoolean() ||
                state.GetProperty("bIsAConduit").GetBoolean() || edge.GetProperty("transitionIndex").GetInt32()!=i ||
                edge.GetProperty("bAutomaticRemainingTimeRule").GetBoolean() || !edge.GetProperty("bDesiredTransitionReturnValue").GetBoolean())
                throw new NotSupportedException("Changed Pivot state/player binding.");
            var currentRule=edge.GetProperty("canTakeDelegateIndex").GetInt32();
            if (rule>=0 && rule!=currentRule) throw new NotSupportedException("Pivot states no longer share their original rule.");
            rule=currentRule;
            var transition=definition.GetProperty("transitions")[i];
            if (transition.GetProperty("previousState").GetInt32()!=i || transition.GetProperty("nextState").GetInt32()!=1-i ||
                transition.GetProperty("crossfadeDuration").GetSingle()!=.4f || transition.GetProperty("logicType").GetString()!="TLT_Inertialization" ||
                transition.GetProperty("blendProfile").GetString()!=
                    "/Game/Characters/Heroes/Mannequin/Meshes/SK_Mannequin.SK_Mannequin:FastFeet")
                throw new NotSupportedException("Changed Pivot transition/inertia policy.");
        }
        if (owner.Nodes[hip].Functions.Update!="UpdateHipFireRaiseWeaponPose" ||
            !closure.Sources.SequenceEqual(new[]{hip,a,b}.Order()))
            throw new NotSupportedException("Changed Pivot source inventory.");
        return new(closure,owner.Nodes[a],owner.Nodes[b],owner.Nodes[hip],machine,rule,definition.Clone(),
            warps.ToImmutableArray(),blendSettings.GetProperty("blendWeights")[0].GetSingle());
    }

    public LyraPivotSourcePair CreateSources(int playerBase,long epoch,LyraPivotDistanceBank bank,string profile,
        Func<string,LyraCardinalDirection,LyraPivotAsset> resolve) =>
        new(new(PivotA,checked(playerBase+PivotA.Index),epoch,resolve,bank.Asset,bank.Policy(profile)),
            new(PivotB,checked(playerBase+PivotB.Index),epoch,resolve,bank.Asset,bank.Policy(profile)));

    public LyraPivotMachineRuntime CreateMachine(int playerBase,long epoch,LyraPivotDistanceBank bank,string profile,
        Func<string,LyraCardinalDirection,LyraPivotAsset> resolve) =>
        new(this,CreateSources(playerBase,epoch,bank,profile,resolve));
}
