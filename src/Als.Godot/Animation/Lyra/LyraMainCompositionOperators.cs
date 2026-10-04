using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// Reusable complete-data buffers at explicit original Main boundaries. They
// contain no players, clocks, feedback publication or independent Sync.
internal sealed class LyraCompositionPoseBuffer
{
    public LyraLogicalSourceBank Layout { get; }
    public AlsPrecisePose[] Pose { get; }
    public LyraCurveSample[] Curves { get; }
    public LyraAttributeSample[] Attributes { get; }
    public LyraRootMotionAttribute RootMotion { get; set; }
    public LyraCompositionPoseBuffer(LyraLogicalSourceBank layout)
    { Layout=layout;Pose=new AlsPrecisePose[81];Curves=new LyraCurveSample[layout.Curves.Names.Length];Attributes=new LyraAttributeSample[layout.Curves.Attributes.Layout.Length]; }
    public LyraLayerPoseInput Input=>new(Layout,Pose,Curves,Attributes,RootMotion);
    public void Copy(in LyraLayerPoseInput input)
    {Validate(input);input.Pose.CopyTo(Pose);input.Curves.CopyTo(Curves);input.Attributes.CopyTo(Attributes);RootMotion=input.RootMotion;}
    public void Validate(in LyraLayerPoseInput input)
    {
        if(!ReferenceEquals(Layout,input.Layout)||input.Pose.Length!=Pose.Length||input.Curves.Length!=Curves.Length||input.Attributes.Length!=Attributes.Length)
            throw new InvalidOperationException("Main composition input has a foreign or incomplete layout.");
        foreach(var p in input.Pose)p.Validate(.001);
        foreach(var c in input.Curves)if(!float.IsFinite(c.Value))throw new InvalidOperationException("Main composition has a nonfinite curve.");
        if(input.RootMotion.Present)input.RootMotion.Value.Validate(.001);
    }
}

// Original Main operators at their actual boundaries. Slot/Aiming/inertia
// outputs must be supplied by the enclosing graph, not replaced here.
internal sealed class LyraMainCompositionOperators
{
    private readonly LyraLogicalSourceBank _bank;
    private readonly float[] _mask,_weights;
    private readonly int[] _curveSources,_attributeBones;
    private readonly bool[] _attributeOverrides;
    private readonly AlsQuaternion[] _scratch=new AlsQuaternion[243];
    private readonly LyraCompositionPoseBuffer _dynamic;
    private readonly bool _rootOverride;
    internal LyraLayerPoseInput DiagnosticDynamic=>_dynamic.Input;
    public LyraMainCompositionOperators(LyraLogicalSourceBank bank,string profile)
    {
        _bank=bank;_dynamic=new(bank);_weights=new float[81];_rootOverride=LyraRootMotionAttribute.LoadBlendPolicy();
        const string root="res://assets/generated/lyra_als/";
        using var document=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"main_composition_v2_policy.json"));
        var policy=document.RootElement;
        if(policy.GetProperty("schemaVersion").GetInt32()!=1||policy.GetProperty("stage").GetString()!="OriginalMainCompositionOperators"||
            !policy.GetProperty("nodes").EnumerateArray().Select(v=>v.GetInt32()).SequenceEqual(new[]{0,3,76,72}))
            throw new InvalidOperationException("Changed Main composition policy.");
        foreach(var d in policy.GetProperty("dependencies").EnumerateObject())
            if(d.Value.GetString()!=LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+d.Name)))throw new InvalidOperationException("Stale Main composition dependency: "+d.Name);
        var row=policy.GetProperty("policies").GetProperty(profile);
        _mask=row.GetProperty("mask").EnumerateArray().Select(v=>v.GetSingle()).ToArray();
        if(_mask.Length!=81||_mask[0]!=0||_mask.Any(v=>!float.IsFinite(v)||v is <0 or >1))throw new InvalidOperationException("Invalid Main split mask.");
        // Verify target identities against the independently captured authored
        // profile. Never reinterpret Manny array indices as ALS bone indices.
        var authored=row.GetProperty("sourceMask").EnumerateArray().ToDictionary(v=>v.GetProperty("bone").GetString()!,v=>v.GetProperty("scale").GetSingle(),StringComparer.OrdinalIgnoreCase);
        using var calibration=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"logical_controls/calibration.json"));
        var names=calibration.RootElement.GetProperty("layout").GetProperty("logicalBoneNames");
        for(var b=0;b<81;b++)
        {
            var name=names[b].GetString()!;var expected=authored.GetValueOrDefault(name);
            if(bank.Bone(name)!=b||_mask[b]!=expected)throw new InvalidOperationException("Main split mask bone mismatch: "+name);
        }
        _curveSources=Enumerable.Repeat(-1,bank.Curves.Names.Length).ToArray();
        foreach(var d in row.GetProperty("curveBindings").EnumerateObject())
        {var i=bank.Curves.Index(d.Name);if(d.Value.GetInt32()!=0)throw new NotSupportedException("Changed Main curve source.");if(i>=0)_curveSources[i]=0;}
        var attributes=LyraCycleLayerPosePolicy.Load(profile,bank);_attributeBones=attributes.AttributeBones;_attributeOverrides=attributes.AttributeOverrides;
        // Keep original topology and node policy as a gate on this operator.
        var graph=LyraMainLayerGraphCatalog.Load().MainGraph.GetProperty("nodes");
        JsonElement Node(int id)=>graph.EnumerateArray().Single(v=>v.GetProperty("index").GetInt32()==id).GetProperty("settings");
        var split=Node(0);var rotate=Node(72);
        if(!split.GetProperty("bMeshSpaceRotationBlend").GetBoolean()||split.GetProperty("bRootSpaceRotationBlend").GetBoolean()||
            split.GetProperty("bMeshSpaceScaleBlend").GetBoolean()||split.GetProperty("bUpdateBasePoseFirst").GetBoolean()||
            !split.GetProperty("bBlendRootMotionBasedOnRootBone").GetBoolean()||split.GetProperty("curveBlendOption").GetString()!="Override"||
            rotate.GetProperty("bRotateRootMotionAttribute").GetBoolean()||Node(76).GetProperty("alpha").GetSingle()!=.65f||
            Node(3).GetProperty("alphaInputType").GetString()!="Float")throw new NotSupportedException("Changed original Main composition nodes.");
    }
    public void Upper(in LyraLayerPoseInput basis,in LyraLayerPoseInput upper,in LyraLayerPoseInput additive,
        float dynamicAlpha,float splitAlpha,LyraCompositionPoseBuffer output)
    {
        output.Validate(basis);output.Validate(upper);output.Validate(additive);
        if(!ReferenceEquals(output.Layout,_bank)||!float.IsFinite(splitAlpha))throw new InvalidOperationException("Invalid Main split output or alpha.");
        Additive(basis,additive,dynamicAlpha,_dynamic);
        if(splitAlpha<=AlsPoseBlender.WeightThreshold){output.Copy(_dynamic.Input);return;}
        for(var b=0;b<81;b++)_weights[b]=_mask[b]*splitAlpha>AlsPoseBlender.WeightThreshold?_mask[b]*splitAlpha:0;
        AlsMeshSpacePoseBlend.Blend(_dynamic.Pose,upper.Pose,_bank.Parents,_weights,_scratch,output.Pose,singleRotationAlpha:true);
        for(var c=0;c<output.Curves.Length;c++)output.Curves[c]=LyraLayeredDataBlend.OverrideCurve(_dynamic.Curves[c],upper.Curves[c],_curveSources[c]);
        for(var a=0;a<output.Attributes.Length;a++)output.Attributes[a]=LyraLayeredDataBlend.BlendInteger(_dynamic.Attributes[a],upper.Attributes[a],_weights[_attributeBones[a]],_attributeOverrides[a]);
        output.RootMotion=LyraRootMotionAttribute.Blend(_dynamic.RootMotion,upper.RootMotion,_weights[0],_rootOverride);
    }
    public static void Additive(in LyraLayerPoseInput basis,in LyraLayerPoseInput additive,float alpha,LyraCompositionPoseBuffer output)
    {
        output.Validate(basis);output.Validate(additive);
        if(!float.IsFinite(alpha))throw new InvalidOperationException("Nonfinite Main additive alpha.");
        if(alpha<=AlsPoseBlender.WeightThreshold){output.Copy(basis);return;}
        // Original Float path calls FInputScaleBias::ApplyTo, which clamps
        // after the explicit double-to-float handler boundary.
        alpha=Math.Clamp(alpha,0,1);
        // AccumulateAdditivePose normalizes the pose, then the original
        // ApplyAdditive node normalizes it again. Both passes affect history
        // used by enclosing inertialization; root attributes use their own path.
        for(var b=0;b<output.Pose.Length;b++)output.Pose[b]=Apply(basis.Pose[b],additive.Pose[b],alpha,
            isPc:alpha>=1f-AlsPoseBlender.WeightThreshold).Normalized();
        for(var c=0;c<output.Curves.Length;c++)
            output.Curves[c]=LyraCurveSample.Additive(basis.Curves[c],additive.Curves[c],alpha);
        for(var a=0;a<output.Attributes.Length;a++)
            output.Attributes[a]=LyraAttributeSample.Accumulate(basis.Attributes[a],additive.Attributes[a],alpha);
        var root=basis.RootMotion;var added=additive.RootMotion;
        output.RootMotion=!added.Present?root:root.Present?new(Apply(root.Value,added.Value,alpha),true):
            new(AlsPrecisePoseBlender.Scale(added.Value,alpha).Normalized(),true);
    }
    private static AlsPrecisePose Apply(in AlsPrecisePose basis,in AlsPrecisePose additive,float alpha,bool isPc=false)
        => AlsPrecisePoseBlender.AccumulateAdditive(basis,additive,alpha,isPc:isPc).Normalized();
    public static void RotateRoot(in LyraLayerPoseInput input,float yaw,LyraCompositionPoseBuffer output)
    {
        output.Copy(input);if(!float.IsFinite(yaw))throw new InvalidOperationException("Nonfinite Main root yaw.");
        if(Math.Abs(yaw)<=.0001f)return;
        var q=LyraRootRotationMath.Quaternion(yaw);
        // RotateRootBone normalizes the skeletal root after writing its product.
        output.Pose[0]=output.Pose[0] with{Rotation=(output.Pose[0].Rotation*q).Normalized()};
        // Original node72 bRotateRootMotionAttribute=false: skeletal root and
        // movement attribute intentionally have different rotation behavior.
    }
}
