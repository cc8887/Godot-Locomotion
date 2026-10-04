using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// One character's complete Slot evaluator. The physical Montage bank owns all
// clocks; sampling and blending only consume its frozen frame. No oracle data.
internal sealed class LyraMontageSlotPose
{
    private sealed class Entry(LyraLogicalSourceBank bank)
    {
        public readonly LyraCompositionPoseBuffer Data=new(bank);
        public readonly float[] Bones=new float[81];
        public float Weight;
        public int AdditiveType;
    }
    private readonly LyraLogicalSourceBank _bank;
    private readonly LyraMontageCatalog _catalog;
    private readonly LyraMontageTrackSampler _sampler;
    private readonly bool[] _attributeOverrides;
    private readonly bool _rootOverride;
    private readonly List<Entry> _scratch=[];
    private readonly List<Entry> _absolute=[],_additive=[],_blend=[];
    private readonly LyraCompositionPoseBuffer _result;
    private readonly float[] _totals=new float[81];
    private readonly AlsQuaternion[] _mesh=new AlsQuaternion[81];
    private int _busy;
    public LyraMontageSlotPose(LyraLogicalSourceBank bank,LyraMontageCatalog catalog,LyraMontageTrackSampler sampler)
    {
        _bank=bank;_catalog=catalog;_sampler=sampler;_result=new(bank);
        if(catalog.BlendProfiles is null)throw new InvalidOperationException("Slot blending requires target Montage profiles.");
        _attributeOverrides=LyraCycleLayerPosePolicy.Load("unarmed",bank).AttributeOverrides;
        _rootOverride=LyraRootMotionAttribute.LoadBlendPolicy();
    }
    public void Evaluate(AlsMontageFrame frame,in AlsFrameIdentity identity,AlsMontageSlot slot,
        in LyraLayerPoseInput source,LyraCompositionPoseBuffer output,bool extractRootMotion=true)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if(identity.SlotGeneration==0||identity!=frame.Identity||!slot.IsValid||slot.Id>=5||!ReferenceEquals(output.Layout,_bank))
            throw new ArgumentException("Stale or foreign Lyra Slot frame/layout.");
        var weights=frame.SlotWeights(slot);var hasSource=weights.SourceWeight>AlsPoseBlender.WeightThreshold;
        if(hasSource)output.Validate(source);
        if(Interlocked.CompareExchange(ref _busy,1,0)!=0)throw new InvalidOperationException("Character Slot evaluator is already in use.");
        try
        {
            if(weights.SlotNodeWeight<=AlsPoseBlender.WeightThreshold){output.Copy(source);return;}
            _absolute.Clear();_additive.Clear();_blend.Clear();Array.Clear(_totals);
            var profiles=false;var count=0;var nonAdditiveTotal=0f;
            foreach(var e in frame.Evaluations)
            {
                if(e.Slot!=slot)continue;
                if((uint)e.AdditiveType>2||!float.IsFinite(e.Weight)||e.Weight is <0 or >1||
                    e.BlendSnapshot.ProfileId < -1||e.BlendSnapshot.ProfileId>=_catalog.BlendProfiles!.Profiles.Length)
                    throw new ArgumentException("Invalid physical Montage Slot evaluation.");
                var p=At(count++);p.Weight=e.Weight;p.AdditiveType=e.AdditiveType;
                _sampler.Sample(e,extractRootMotion&&_catalog.Metadata[e.ActionDefinitionId].GetProperty("rootMotion").GetBoolean(),p.Data);
                var profile=e.BlendSnapshot.ProfileId;
                for(var b=0;b<81;b++)p.Bones[b]=profile<0?e.Weight:AlsMontageBlendProfile.BoneWeight(
                    _catalog.BlendProfiles.Profiles[profile].Factors[b],_catalog.BlendProfiles.Profiles[profile].Mode,e.BlendSnapshot,e.Weight);
                profiles|=profile>=0;
                if(e.AdditiveType==0)
                { _absolute.Add(p);nonAdditiveTotal+=e.Weight;for(var b=0;b<81;b++)_totals[b]+=p.Bones[b]; }
                else _additive.Add(p);
            }
            if(count==0||count>=255)throw new InvalidOperationException("Invalid native Slot pose count.");
            const float threshold=AlsPoseBlender.WeightThreshold;
            foreach(var p in _absolute)
            {
                if(profiles)
                {
                    if(nonAdditiveTotal>1f+threshold)p.Weight/=nonAdditiveTotal;
                    for(var b=0;b<81;b++)
                        if(_totals[b]>(hasSource?1f+threshold:threshold))p.Bones[b]/=_totals[b];
                        else if(!hasSource)p.Bones[b]=1f;
                }
                else if(weights.TotalNodeWeight>1f+threshold)p.Weight/=weights.TotalNodeWeight;
                _blend.Add(p);
            }
            if(!profiles&&weights.TotalNodeWeight>1f+threshold)
                foreach(var p in _additive)p.Weight/=weights.TotalNodeWeight;
            if(hasSource)
            {
                var p=At(count);p.Data.Copy(source);p.Weight=profiles&&nonAdditiveTotal>1f+threshold?
                    weights.SourceWeight/nonAdditiveTotal:weights.SourceWeight;
                for(var b=0;b<81;b++)
                {
                    var sum=0f;foreach(var other in _absolute)sum+=other.Bones[b];
                    p.Bones[b]=profiles?1f-sum:weights.SourceWeight;
                }
                _blend.Add(p);
            }
            if(_absolute.Count==0)
            {
                if(hasSource)_result.Copy(source);
                else { _bank.Reference.CopyTo(_result.Pose);Array.Clear(_result.Curves);Array.Clear(_result.Attributes);_result.RootMotion=default; }
            }
            else
            {
                for(var b=0;b<81;b++)
                {
                    var pose=AlsPrecisePoseBlender.Scale(_blend[0].Data.Pose[b],profiles?_blend[0].Bones[b]:_blend[0].Weight);
                    for(var i=1;i<_blend.Count;i++)pose=AlsPrecisePoseBlender.Accumulate(pose,_blend[i].Data.Pose[b],profiles?_blend[i].Bones[b]:_blend[i].Weight);
                    _result.Pose[b]=profiles||_blend.Count>1?pose.Normalized():pose;
                }
            }
            if(_blend.Count>0)BlendData();
            foreach(var p in _additive)
            {
                // Profile slots always run BlendFromIdentity per bone; the
                // regular path skips irrelevant bone accumulation only.
                if(profiles||p.Weight>threshold)
                {
                    if(p.AdditiveType==2)
                        for(var b=0;b<81;b++)_mesh[b]=_bank.Parents[b]<0?_result.Pose[b].Rotation:_mesh[_bank.Parents[b]]*_result.Pose[b].Rotation;
                    for(var b=0;b<81;b++)
                    {
                        var basis=_result.Pose[b];if(p.AdditiveType==2)basis=basis with{Rotation=_mesh[b]};
                        var applied=Apply(basis,p.Data.Pose[b],profiles?p.Bones[b]:p.Weight,profiles,
                            isPc:!profiles&&p.Weight>=1f-threshold);
                        _result.Pose[b]=applied;if(p.AdditiveType==2)_mesh[b]=applied.Rotation;
                    }
                    if(p.AdditiveType==2)
                        for(var b=80;b>=0;b--)if(_bank.Parents[b]>=0)_result.Pose[b]=_result.Pose[b] with{Rotation=AlsQuaternion.MultiplyIsPc(_mesh[_bank.Parents[b]].Conjugate(),_mesh[b])};
                }
                for(var b=0;b<81;b++)_result.Pose[b]=_result.Pose[b].Normalized();
                for(var c=0;c<_result.Curves.Length;c++)
                {
                    var a=_result.Curves[c];var d=p.Data.Curves[c];
                    _result.Curves[c]=LyraCurveSample.Additive(a,d,p.Weight);
                }
                for(var a=0;a<_result.Attributes.Length;a++)
                {
                    var basis=_result.Attributes[a];var d=p.Data.Attributes[a];
                    _result.Attributes[a]=LyraAttributeSample.Accumulate(basis,d,p.Weight);
                }
                var root=_result.RootMotion;var added=p.Data.RootMotion;
                if(added.Present)_result.RootMotion=root.Present?
                    new(Apply(root.Value,added.Value,p.Weight,false).Normalized(),true):new(AlsPrecisePoseBlender.Scale(added.Value,p.Weight).Normalized(),true);
            }
            output.Copy(_result.Input);
        }
        finally{Volatile.Write(ref _busy,0);}
    }
    private Entry At(int index)
    {while(_scratch.Count<=index)_scratch.Add(new(_bank));return _scratch[index];}
    private void BlendData()
    {
        for(var c=0;c<_result.Curves.Length;c++)
        {
            var value=default(LyraCurveSample);
            foreach(var p in _blend)
            {
                var v=p.Data.Curves[c];if(!v.Present)continue;
                value=LyraCurveSample.AccumulateContribution(value,v,p.Weight);
            }
            _result.Curves[c]=value;
        }
        for(var a=0;a<_result.Attributes.Length;a++)
        {
            var present=false;var value=0;var highest=float.NegativeInfinity;
            foreach(var p in _blend)
            {
                var v=p.Data.Attributes[a];if(!v.Present)continue;
                if(_attributeOverrides[a]){if(p.Weight>highest){value=v.Value;highest=p.Weight;}}
                else value=LyraAttributeSample.Accumulate(new(value,present),v,p.Weight).Value;present=true;
            }
            _result.Attributes[a]=new(value,present);
        }
        var count=0;Entry? unique=null;var highestWeight=float.NegativeInfinity;var result=default(AlsPrecisePose);
        foreach(var p in _blend)
        {
            var r=p.Data.RootMotion;if(!r.Present)continue;unique=p;
            if(_rootOverride){if(p.Weight>highestWeight){highestWeight=p.Weight;result=r.Value;}}
            else result=count==0?AlsPrecisePoseBlender.Scale(r.Value,p.Weight):AlsPrecisePoseBlender.Accumulate(result,r.Value,p.Weight);
            count++;
        }
        _result.RootMotion=count==0?default:count==1&&!_rootOverride?
            LyraRootMotionAttribute.Blend(default,unique!.Data.RootMotion,unique.Weight):new(result.Normalized(),true);
    }
    private static AlsPrecisePose Apply(in AlsPrecisePose basis,in AlsPrecisePose additive,float weight,bool forceBlend,bool isPc=false)
        => AlsPrecisePoseBlender.AccumulateAdditive(basis,additive,weight,forceBlend,isPc);
}
