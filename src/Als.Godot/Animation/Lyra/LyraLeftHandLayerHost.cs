using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal readonly ref struct LyraLayerPoseInput
{
    public readonly LyraLogicalSourceBank Layout;
    public readonly ReadOnlySpan<AlsPrecisePose> Pose;
    public readonly ReadOnlySpan<LyraCurveSample> Curves;
    public readonly ReadOnlySpan<LyraAttributeSample> Attributes;
    public readonly LyraRootMotionAttribute RootMotion;
    public LyraLayerPoseInput(LyraLogicalSourceBank layout,ReadOnlySpan<AlsPrecisePose> pose,
        ReadOnlySpan<LyraCurveSample> curves,ReadOnlySpan<LyraAttributeSample> attributes,LyraRootMotionAttribute rootMotion)
    {Layout=layout;Pose=pose;Curves=curves;Attributes=attributes;RootMotion=rootMotion;}
}
internal sealed record LyraLeftHandLayerCandidate(bool Visited,bool Initialize,float ContextWeight,double Weight,float BlendWeight,
    LyraIdleOccurrence Source);
internal readonly struct LyraLeftHandPoseView
{
    private readonly LyraLeftHandLayerHost _owner;
    private readonly LyraLeftHandLayerCandidate _candidate;
    internal LyraLeftHandPoseView(LyraLeftHandLayerHost owner,LyraLeftHandLayerCandidate candidate){_owner=owner;_candidate=candidate;}
    public ReadOnlySpan<AlsPrecisePose> Pose=>_owner.Pose(_candidate);
    public ReadOnlySpan<LyraCurveSample> Curves=>_owner.Curves(_candidate);
    public ReadOnlySpan<LyraAttributeSample> Attributes=>_owner.Attributes(_candidate);
    public LyraRootMotionAttribute RootMotion=>_owner.RootMotion(_candidate);
}

// Original null SequenceEvaluator in the three current providers. The node
// callback updates a double field; its exposed blend pin converts to float.
// There is no independent source tick or curve feedback from this input pose.
internal sealed class LyraLeftHandLayerHost
{
    private readonly LyraLogicalSourceBank _bank;
    private readonly float[] _mask;
    private readonly int[] _curveSources,_attributeBones;
    private readonly bool[] _attributeOverrides;
    private readonly bool _enabled,_rootOverride;
    private readonly AlsPrecisePose[] _pose=new AlsPrecisePose[81];
    private readonly LyraCurveSample[] _curves;
    private readonly LyraAttributeSample[] _attributes;
    private LyraRootMotionAttribute _root;
    private LyraLeftHandLayerCandidate? _pending;
    private bool _evaluated,_failed;
    public double Weight {get;private set;}
    // The original null evaluator is still an occurrence. Initialization keeps
    // its clock and delta history; no tick is registered without an asset.
    internal LyraIdleOccurrence Source {get;private set;}=new(-1,0,0,0,AlsAssetMarkerRecord.Invalid,0,0,false,-1,0,false);
    internal void InitializeSource()
    {
        if(_pending is not null)throw new InvalidOperationException("Left-hand source initialization needs an idle host.");
        Source=Source with{Marker=Source.Marker with{PreviousIndex=-2,NextIndex=-2,Initialized=false},ResetPending=true};
    }
    public LyraLeftHandLayerHost(LyraLogicalSourceBank bank,LyraMainLayerGraphCatalog graphs,string profile)
    {
        _bank=bank;
        var graph=graphs.Graph(profile,LyraLayerHook.LeftHandPose_OverrideState);
        var nodes=graph.GetProperty("nodes").EnumerateArray().ToDictionary(n=>n.GetProperty("index").GetInt32());
        var blend=nodes[115];var settings=blend.GetProperty("settings");var evaluator=nodes[114].GetProperty("settings");
        if(graph.GetProperty("root").GetInt32()!=117 || nodes.Count!=4 ||
            settings.GetProperty("blendMode").GetString()!="BlendMask" || settings.GetProperty("curveBlendOption").GetString()!="Override" ||
            settings.GetProperty("bMeshSpaceRotationBlend").GetBoolean() || settings.GetProperty("bRootSpaceRotationBlend").GetBoolean() ||
            settings.GetProperty("bMeshSpaceScaleBlend").GetBoolean() || settings.GetProperty("bUpdateBasePoseFirst").GetBoolean() ||
            !settings.GetProperty("bBlendRootMotionBasedOnRootBone").GetBoolean() ||
            blend.GetProperty("functions").GetProperty("update").GetString()!="SetLeftHandPoseOverrideWeight" ||
            evaluator.GetProperty("method").GetString()!="DoNotSync" || evaluator.GetProperty("groupName").GetString()!="None" ||
            !evaluator.GetProperty("bTeleportToExplicitTime").GetBoolean() || evaluator.GetProperty("explicitTime").GetDouble()!=0)
            throw new NotSupportedException("Changed original LeftHand layer graph.");
        var defaults=graphs.Defaults(profile);_enabled=defaults.GetProperty("EnableLeftHandPoseOverride").GetProperty("value").GetBoolean();
        if(defaults.GetProperty("LeftHandPose_Override").GetProperty("value").GetString()!="")
            throw new NotSupportedException("Non-null left-hand evaluator needs a bound shared source occurrence.");
        using var document=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/left_hand_layer_v4_policy.json"));
        var data=document.RootElement;
        if(data.GetProperty("schemaVersion").GetInt32()!=1 || !data.GetProperty("nullSequence").GetBoolean() ||
            data.GetProperty("stage").GetString()!="OriginalLeftHandLayer" || data.GetProperty("skeleton").GetString()!="ALS81")
            throw new InvalidOperationException("Unsupported left-hand layer policy.");
        foreach(var dep in data.GetProperty("dependencies").EnumerateObject())
            if(dep.Value.GetString()!=LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/"+dep.Name)))
                throw new InvalidOperationException("Stale left-hand layer policy.");
        var policy=data.GetProperty("policies").GetProperty(profile);
        _mask=policy.GetProperty("mask").EnumerateArray().Select(v=>v.GetSingle()).ToArray();
        if(_mask.Length!=81 || _mask[0]!=0 || _mask.Any(v=>!float.IsFinite(v) || v is <0 or >1))throw new InvalidOperationException("Invalid left-finger mask.");
        using var masks=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/unarmed_layer_masks.json"));
        var authored=masks.RootElement.GetProperty("profiles").GetProperty("LeftFingersMask");
        foreach(var b in authored.GetProperty("alsBones").EnumerateArray())
            if(bank.Bone(b.GetProperty("bone").GetString()!)!=b.GetProperty("index").GetInt32() ||
                _mask[b.GetProperty("index").GetInt32()]!=b.GetProperty("scale").GetSingle())throw new InvalidOperationException("Changed left mask skin identity.");
        foreach(var name in new[]{"weapon_r","VB IK_Hand_L_weaponSpace"})
            if(_mask[bank.Bone(name)]!=authored.GetProperty("sourceBones").EnumerateArray().Single(v=>v.GetProperty("bone").GetString()==name).GetProperty("scale").GetSingle())
                throw new InvalidOperationException("Changed left mask control identity.");
        if(_mask.Skip(69).Take(11).Any(v=>v!=0))throw new InvalidOperationException("Unexpected ALS-only VB mask.");
        _curveSources=Enumerable.Repeat(-1,bank.Curves.Names.Length).ToArray();
        foreach(var binding in policy.GetProperty("curveBindings").EnumerateObject())
        {var id=bank.Curves.Index(binding.Name);if(id<0 || binding.Value.GetInt32()!=0)throw new NotSupportedException("Unknown left-hand curve binding.");_curveSources[id]=0;}
        var attributes=LyraCycleLayerPosePolicy.Load(profile,bank);_attributeBones=attributes.AttributeBones;_attributeOverrides=attributes.AttributeOverrides;
        _rootOverride=LyraRootMotionAttribute.LoadBlendPolicy();_curves=new LyraCurveSample[_curveSources.Length];_attributes=new LyraAttributeSample[_attributeBones.Length];
    }
    public LyraLeftHandLayerCandidate Prepare(bool visited,bool initialize,float contextWeight,float committedDisableCurve,bool? enabled=null)
    {
        if(_pending is not null)throw new InvalidOperationException("Left-hand layer frame is pending.");
        if(!float.IsFinite(contextWeight) || contextWeight<0 || !float.IsFinite(committedDisableCurve))throw new ArgumentException("Invalid left-hand update input.");
        _evaluated=_failed=false;
        var weight=visited?Math.Clamp(((enabled??_enabled)?1d:0d)-(double)committedDisableCurve,0d,1d):Weight;
        var source=initialize?Source with{Marker=Source.Marker with{PreviousIndex=-2,NextIndex=-2,Initialized=false},ResetPending=true}:Source;
        if(visited&&(float)weight>1e-5f)
            source=source with{Weight=contextWeight*(float)weight,ResetPending=false};
        return _pending=new(visited,initialize,contextWeight,weight,(float)weight,source);
    }
    private void Validate(LyraLeftHandLayerCandidate candidate)
    {if(!ReferenceEquals(candidate,_pending) || _failed)throw new InvalidOperationException("Stale or failed left-hand layer candidate.");}
    public LyraLeftHandPoseView Evaluate(LyraLeftHandLayerCandidate candidate,in LyraLayerPoseInput input)
    {
        Validate(candidate);_evaluated=false;
        try
        {
            if(!candidate.Visited || !ReferenceEquals(input.Layout,_bank) || input.Curves.Length!=_curves.Length || input.Attributes.Length!=_attributes.Length ||
                input.Curves.Overlaps(_curves) || input.Attributes.Overlaps(_attributes))throw new ArgumentException("Foreign or aliased left-hand input pose.");
            LyraPoseBuffers.Validate(input.Pose,_pose);
            var alpha=candidate.BlendWeight;
            if(alpha<=1e-5f)
            {input.Pose.CopyTo(_pose);input.Curves.CopyTo(_curves);input.Attributes.CopyTo(_attributes);_root=input.RootMotion;}
            else
            {
                for(var bone=0;bone<81;bone++)
                {_pose[bone]=AlsPrecisePose.BlendWith(input.Pose[bone],_bank.Reference[bone],_mask[bone]*alpha);}
                for(var i=0;i<_curves.Length;i++)_curves[i]=LyraLayeredDataBlend.OverrideCurve(input.Curves[i],default,_curveSources[i]);
                for(var i=0;i<_attributes.Length;i++)_attributes[i]=LyraLayeredDataBlend.BlendInteger(input.Attributes[i],default,
                    _mask[_attributeBones[i]]*alpha,_attributeOverrides[i]);
                _root=LyraRootMotionAttribute.Blend(input.RootMotion,default,_mask[0]*alpha,_rootOverride);
            }
            _evaluated=true;return new(this,candidate);
        }
        catch{_failed=true;throw;}
    }
    public void ValidateCommit(LyraLeftHandLayerCandidate candidate,bool updateOnly)
    {Validate(candidate);if(!updateOnly && candidate.Visited && !_evaluated)throw new InvalidOperationException("Left-hand layer needs its current pose.");}
    public void Commit(LyraLeftHandLayerCandidate candidate,bool updateOnly)
    {ValidateCommit(candidate,updateOnly);Weight=candidate.Weight;Source=candidate.Source;_pending=null;_evaluated=false;}
    public void Cancel(){_pending=null;_evaluated=_failed=false;}
    private void Output(LyraLeftHandLayerCandidate c){Validate(c);if(!_evaluated)throw new InvalidOperationException("Left-hand layer has no current output.");}
    internal ReadOnlySpan<AlsPrecisePose> Pose(LyraLeftHandLayerCandidate c){Output(c);return _pose;}
    internal ReadOnlySpan<LyraCurveSample> Curves(LyraLeftHandLayerCandidate c){Output(c);return _curves;}
    internal ReadOnlySpan<LyraAttributeSample> Attributes(LyraLeftHandLayerCandidate c){Output(c);return _attributes;}
    internal LyraRootMotionAttribute RootMotion(LyraLeftHandLayerCandidate c){Output(c);return _root;}
}
