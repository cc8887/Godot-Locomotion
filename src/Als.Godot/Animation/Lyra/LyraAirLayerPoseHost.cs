using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

// The five Air graphs use the same absolute upper-body layered blend. Their
// individual source occurrences are supplied by the original compiled graph;
// pose evaluation consumes the enclosing character's completed Sync snapshot.
internal sealed class LyraAirLayerPoseHost
{
    private readonly LyraAirLayerSourceHost _sources;
    private readonly LyraAirLayerGraph _graph;
    private readonly LyraLogicalSourceBank _bank;
    private readonly LyraCycleLayerPosePolicy _policy;
    private readonly LyraLogicalSourceSampler[] _samplers;
    private readonly LyraLogicalSourceDefinition[] _definitions;
    private readonly AlsRawRootMotionIntervalSampler[] _roots;
    private readonly bool _rootOverride;
    private readonly AlsPrecisePose[] _base=new AlsPrecisePose[81],_child=new AlsPrecisePose[81],_pose=new AlsPrecisePose[81];
    private readonly AlsQuaternion[] _scratch=new AlsQuaternion[243];
    private readonly float[] _weights=new float[81];
    private readonly LyraCurveSample[] _baseCurves,_childCurves,_curves;
    private readonly LyraAttributeSample[] _baseAttributes,_childAttributes,_attributes;
    private LyraAirSourceCandidate? _pending;
    private LyraAirSourceState? _evaluated;
    private LyraRootMotionAttribute _root;
    public LyraAirSourceState State=>_sources.State;
    internal bool InitializeSourceNode(int node)
    {
        if(_pending is not null)throw new InvalidOperationException("Air source initialization needs an idle pose host.");
        return _sources.InitializeSourceNode(node);
    }
    public bool HasPose {get;private set;}
    public ReadOnlySpan<AlsPrecisePose> Pose=>HasPose?_pose:throw new InvalidOperationException("Air has no pose.");
    public ReadOnlySpan<LyraCurveSample> Curves=>HasPose?_curves:throw new InvalidOperationException("Air has no curves.");
    public ReadOnlySpan<LyraAttributeSample> Attributes=>HasPose?_attributes:throw new InvalidOperationException("Air has no attributes.");
    public LyraRootMotionAttribute RootMotion=>HasPose?_root:throw new InvalidOperationException("Air has no root attribute.");
    public LyraAirLayerPoseHost(LyraAirLayerSourceHost sources,LyraAirLayerGraph graph,LyraLogicalSourceBank bank,
        LyraCycleLayerPosePolicy policy,IReadOnlyList<string> slots,LyraCompressedRootBank roots)
    {
        if(policy.Mask.Length!=81 || policy.CurveSources.Length!=bank.Curves.Names.Length ||
            policy.AttributeBones.Length!=bank.Curves.Attributes.Layout.Length || policy.AttributeOverrides.Length!=policy.AttributeBones.Length)
            throw new ArgumentException("Invalid Air pose layout.");
        _sources=sources;_graph=graph;_bank=bank;
        _policy=new(policy.Mask.ToArray(),policy.CurveSources.ToArray(),policy.AttributeBones.ToArray(),policy.AttributeOverrides.ToArray());
        _definitions=slots.Select(bank.Get).ToArray();if(_definitions.Any(d=>d.IsAdditive))throw new NotSupportedException("Air requires absolute clips.");
        _samplers=slots.Select(bank.CreateSampler).ToArray();
        _roots=_definitions.Select(d=>roots.CreateSampler(d.Slot,bank.Reference[0],d.NormalizedRootMotionScale)).ToArray();
        _rootOverride=LyraRootMotionAttribute.LoadBlendPolicy();
        var n=bank.Curves.Names.Length;_baseCurves=new LyraCurveSample[n];_childCurves=new LyraCurveSample[n];_curves=new LyraCurveSample[n];
        n=bank.Curves.Attributes.Layout.Length;_baseAttributes=new LyraAttributeSample[n];_childAttributes=new LyraAttributeSample[n];_attributes=new LyraAttributeSample[n];
    }
    public LyraAirSourceCandidate Prepare(bool crouching,double groundDistance,double hipWeight,float delta,LyraAirVisit visit,int sampleStart=0)
    {
        if(_pending is not null)throw new InvalidOperationException("Air pose frame is pending.");HasPose=false;_evaluated=null;
        try{return _pending=_sources.Prepare(crouching,groundDistance,hipWeight,delta,visit,sampleStart);}
        catch{_sources.Cancel();throw;}
    }
    public LyraAirSourceState Resolve(LyraAirSourceCandidate c,ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {if(!ReferenceEquals(c,_pending))throw new InvalidOperationException("Stale Air pose.");return _sources.Resolve(c,outputs);}
    public void Evaluate(LyraAirSourceCandidate c,ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {
        if(!c.Visit.Visited)throw new InvalidOperationException("Unvisited Air root cannot evaluate.");HasPose=false;var state=Resolve(c,outputs);
        var b=state.Base;var id=b.AssetId;_samplers[id].Sample(b.Time,_base,_baseCurves,_baseAttributes);
        _root=LyraRootMotionAttribute.Sample(_definitions[id],_roots[id],b.Previous,b.Delta,_graph.Base.Looping,
            retainedEvaluatorClock:_graph.Base.Kind==LyraSourceKind.SequenceEvaluator);
        if(!c.HipTicked)
        {_base.CopyTo(_pose,0);_baseCurves.CopyTo(_curves,0);_baseAttributes.CopyTo(_attributes,0);}
        else
        {
            var h=state.Hip;id=h.AssetId;_samplers[id].Sample(h.Time,_child,_childCurves,_childAttributes);
            for(var bone=0;bone<81;bone++){var weight=_policy.Mask[bone]*state.Blend;_weights[bone]=weight>1e-5f?weight:0;}
            AlsMeshSpacePoseBlend.Blend(_base,_child,_bank.Parents,_weights,_scratch,_pose,singleRotationAlpha:true);
            for(var curve=0;curve<_curves.Length;curve++)_curves[curve]=LyraLayeredDataBlend.OverrideCurve(_baseCurves[curve],_childCurves[curve],_policy.CurveSources[curve]);
            for(var a=0;a<_attributes.Length;a++)_attributes[a]=LyraLayeredDataBlend.BlendInteger(_baseAttributes[a],_childAttributes[a],_weights[_policy.AttributeBones[a]],_policy.AttributeOverrides[a]);
            _root=LyraRootMotionAttribute.Blend(_root,LyraRootMotionAttribute.Sample(_definitions[id],_roots[id],h.Previous,h.Delta,_graph.Hip.Looping),_weights[0],_rootOverride);
        }
        _evaluated=state;HasPose=true;
    }
    public void ValidateCommit(LyraAirSourceCandidate c,ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {
        var state=Resolve(c,outputs);
        // Native Update without Evaluate is supported; if evaluated, it must
        // belong to this candidate and this exact completed Sync result.
        if(HasPose && state!=_evaluated)throw new InvalidOperationException("Air pose belongs to another Sync result.");
    }
    public void Commit(LyraAirSourceCandidate c,ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {ValidateCommit(c,outputs);_sources.Commit(c,outputs);_pending=null;}
    public void Cancel(){_sources.Cancel();_pending=null;_evaluated=null;HasPose=false;}
}
