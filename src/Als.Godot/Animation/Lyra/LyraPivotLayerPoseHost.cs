using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraPivotLayerPoseCandidate(LyraPivotLayerSourceCandidate Sources,
    AlsOrientationWarpingInput Orientation, AlsStrideWarpingInput Stride);

// Original Pivot topology: each state owns Orientation -> Stride history;
// PivotSM output is then blended with the independent outer HipFire source.
// Transition inertia is emitted for the enclosing graph, not invented here.
internal sealed class LyraPivotLayerPoseHost
{
    private readonly LyraPivotLayerSourceHost _sources;
    private readonly LyraLogicalSourceBank _bank;
    private readonly LyraCycleLayerPosePolicy _policy;
    private readonly LyraLogicalSourceSampler[] _samplers;
    private readonly LyraLogicalSourceDefinition[] _definitions;
    private readonly AlsRawRootMotionIntervalSampler[] _roots;
    private readonly AlsCycleWarping[] _warps;
    private readonly bool _rootOverride;
    private readonly bool _hipFireLooping;
    private readonly AlsPrecisePose[] _base=new AlsPrecisePose[81], _child=new AlsPrecisePose[81],
        _machinePose=new AlsPrecisePose[81], _pose=new AlsPrecisePose[81];
    private readonly AlsQuaternion[] _scratch=new AlsQuaternion[243];
    private readonly float[] _weights=new float[81];
    private readonly LyraCurveSample[] _baseCurves, _childCurves, _curves;
    private readonly LyraAttributeSample[] _baseAttributes, _childAttributes, _attributes;
    private readonly AlsOrientationWarpingState[] _orientation=[AlsOrientationWarpingState.Initial,AlsOrientationWarpingState.Initial];
    private readonly AlsOrientationWarpingState[] _updatedOrientation=new AlsOrientationWarpingState[2];
    private readonly AlsStrideWarpingState[] _stride=[AlsStrideWarpingState.Initial,AlsStrideWarpingState.Initial];
    private LyraPivotLayerPoseCandidate? _pending;
    private LyraPivotLayerResolvedSources? _evaluated;
    private AlsCycleWarpingOutput _warpResult;
    private LyraRootMotionAttribute _root, _machineRoot;
    public AlsOrientationWarpingState OrientationState(int state) => _orientation[state];
    public AlsStrideWarpingState StrideState(int state) => _stride[state];
    public LyraPivotMachineRuntime Machine => _sources.Machine;
    public LyraHipFireSourceState HipFire => _sources.HipFire;
    public bool HasPose { get; private set; }
    public ReadOnlySpan<AlsPrecisePose> Pose => HasPose ? _pose : throw new InvalidOperationException("Pivot has no pose.");
    public ReadOnlySpan<LyraCurveSample> Curves => HasPose ? _curves : throw new InvalidOperationException("Pivot has no curves.");
    public ReadOnlySpan<LyraAttributeSample> Attributes => HasPose ? _attributes : throw new InvalidOperationException("Pivot has no attributes.");
    public LyraRootMotionAttribute RootMotion => HasPose ? _root : throw new InvalidOperationException("Pivot has no root attribute.");
    public ReadOnlySpan<AlsPrecisePose> MachinePose => HasPose ? _machinePose : throw new InvalidOperationException("Pivot machine has no pose.");
    public ReadOnlySpan<LyraCurveSample> MachineCurves => HasPose ? _baseCurves : throw new InvalidOperationException("Pivot machine has no curves.");
    public ReadOnlySpan<LyraAttributeSample> MachineAttributes => HasPose ? _baseAttributes : throw new InvalidOperationException("Pivot machine has no attributes.");
    public LyraRootMotionAttribute MachineRootMotion => HasPose ? _machineRoot : throw new InvalidOperationException("Pivot machine has no root attribute.");

    internal bool InitializeSourceNode(int node)
    {
        if (_pending is not null) throw new InvalidOperationException("Pivot phase initialization needs an idle pose host.");
        return _sources.InitializeSourceNode(node);
    }
    public LyraPivotLayerPoseHost(LyraPivotLayerSourceHost sources, LyraLogicalSourceBank bank,
        LyraCycleLayerPosePolicy policy, IReadOnlyList<string> slots, IReadOnlyList<AlsOrientationWarping> orientation,
        IReadOnlyList<AlsStrideWarping> stride, bool hipFireLooping, LyraCompressedRootBank roots)
    {
        if (orientation.Count!=2 || stride.Count!=2 || ReferenceEquals(orientation[0],orientation[1]) ||
            ReferenceEquals(stride[0],stride[1]) || policy.Mask.Length!=81 || policy.CurveSources.Length!=bank.Curves.Names.Length ||
            policy.AttributeBones.Length!=bank.Curves.Attributes.Layout.Length || policy.AttributeOverrides.Length!=policy.AttributeBones.Length ||
            policy.Mask.Any(w=>!float.IsFinite(w) || w is < 0 or > 1) || policy.AttributeBones.Any(b=>(uint)b>=81) ||
            policy.CurveSources.Any(i=>i is < -1 or > 0)) throw new ArgumentException("Invalid independent Pivot pose operators.");
        _sources=sources; _bank=bank; _hipFireLooping=hipFireLooping;
        _policy=new(policy.Mask.ToArray(),policy.CurveSources.ToArray(),policy.AttributeBones.ToArray(),policy.AttributeOverrides.ToArray());
        _definitions=slots.Select(bank.Get).ToArray();
        if (_definitions.Any(d=>d.IsAdditive)) throw new NotSupportedException("Pivot requires absolute sources.");
        _samplers=slots.Select(bank.CreateSampler).ToArray();
        _roots=_definitions.Select(d=>roots.CreateSampler(d.Slot,bank.Reference[0],d.NormalizedRootMotionScale)).ToArray();
        _warps=[new(bank.Parents,orientation[0],stride[0]),new(bank.Parents,orientation[1],stride[1])];
        _rootOverride=LyraRootMotionAttribute.LoadBlendPolicy();
        var n=bank.Curves.Names.Length; _baseCurves=new LyraCurveSample[n]; _childCurves=new LyraCurveSample[n]; _curves=new LyraCurveSample[n];
        n=bank.Curves.Attributes.Layout.Length; _baseAttributes=new LyraAttributeSample[n]; _childAttributes=new LyraAttributeSample[n]; _attributes=new LyraAttributeSample[n];
    }

    public LyraPivotLayerPoseCandidate Prepare(in LyraPivotInput input,float delta,float weight,double hipWeight,
        bool active,bool reset,double displacementSpeed,in LyraCycleWarpContext context,int sampleStart=0)
    {
        if (_pending is not null) throw new InvalidOperationException("Pivot pose candidate is pending.");
        if (!double.IsFinite(displacementSpeed) || !float.IsFinite((float)displacementSpeed) ||
            !double.IsFinite(context.DirectionAngle) || !float.IsFinite((float)context.DirectionAngle) || context.UpdateCounter<0)
            throw new ArgumentException("Invalid Pivot bound pins.");
        context.Component.Validate(); new AlsPrecisePose(default,context.RelativeRotation,AlsDoubleVector.One).Validate();
        HasPose=false; _evaluated=null;
        try
        {
            var sources=_sources.Prepare(input,delta,weight,hipWeight,active,reset,sampleStart);
            for(var state=0;state<2;state++)
            {
                _updatedOrientation[state]=sources.Machine.Initializations[state]>0?_orientation[state].Reset():_orientation[state];
                if(sources.Machine.Active && sources.Machine.State.Current==state)_updatedOrientation[state]=_updatedOrientation[state].PrepareUpdate(context.UpdateCounter);
            }
            var initialized=sources.Machine.Initializations[sources.Machine.State.Current]>0;
            // Alpha is read after the selected child's original callback.
            return _pending=new(sources,new(delta,(float)context.DirectionAngle,default,context.Component,
                context.RelativeRotation,1,weight,context.UpdateCounter,initialized),
                new(delta,(float)displacementSpeed,Math.Clamp((float)sources.Machine.Sources.Shared.StrideAlpha,0,1),context.Component,initialized));
        }
        catch { _sources.Cancel(); throw; }
    }

    public void Evaluate(LyraPivotLayerPoseCandidate candidate,ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {
        if (!ReferenceEquals(candidate,_pending) || !candidate.Sources.Machine.Active)
            throw new InvalidOperationException("Rejected stale or hidden Pivot pose.");
        HasPose=false; _evaluated=null;
        var resolved=_sources.Resolve(candidate.Sources,outputs); var current=candidate.Sources.Machine.State.Current;
        var id=resolved.Pivot.AssetId;
        if (id>=0)
        {
            _samplers[id].Sample(resolved.Pivot.Time,_base,_baseCurves,_baseAttributes);
            _machineRoot=LyraRootMotionAttribute.Sample(_definitions[id],_roots[id],resolved.PivotOutput.DeltaPrevious,
                resolved.PivotOutput.Delta,false);
        }
        else
        {
            _bank.Reference.CopyTo(_base); Array.Clear(_baseCurves); Array.Clear(_baseAttributes);
            _machineRoot=new(AlsPrecisePose.Identity,false);
        }
        _warpResult=_warps[current].Evaluate(_updatedOrientation[current],_stride[current],candidate.Orientation,candidate.Stride,
            _base,_machineRoot.Present,_machineRoot.Value,_machinePose);
        _machineRoot=new(_warpResult.RootMotion,_warpResult.RootPresent); _root=_machineRoot;
        if (!candidate.Sources.HipFireTicked)
        { _machinePose.CopyTo(_pose,0); _baseCurves.CopyTo(_curves,0); _baseAttributes.CopyTo(_attributes,0); }
        else
        {
            id=resolved.HipFire.AssetId;
            _samplers[id].Sample(resolved.HipFire.Time,_child,_childCurves,_childAttributes);
            for (var bone=0;bone<81;bone++)
            { var w=_policy.Mask[bone]*candidate.Sources.BlendWeight; _weights[bone]=w>1e-5f ? w : 0; }
            AlsMeshSpacePoseBlend.Blend(_machinePose,_child,_bank.Parents,_weights,_scratch,_pose,singleRotationAlpha:true);
            for (var i=0;i<_curves.Length;i++) _curves[i]=LyraLayeredDataBlend.OverrideCurve(_baseCurves[i],_childCurves[i],_policy.CurveSources[i]);
            for (var i=0;i<_attributes.Length;i++) _attributes[i]=LyraLayeredDataBlend.BlendInteger(_baseAttributes[i],_childAttributes[i],
                _weights[_policy.AttributeBones[i]],_policy.AttributeOverrides[i]);
            _root=LyraRootMotionAttribute.Blend(_root,LyraRootMotionAttribute.Sample(_definitions[id],_roots[id],
                resolved.HipFire.DeltaPrevious,resolved.HipFire.Delta,_hipFireLooping),_weights[0],_rootOverride);
        }
        _evaluated=resolved; HasPose=true;
    }

    public void ValidateCommit(LyraPivotLayerPoseCandidate candidate,ReadOnlySpan<AlsAssetPlayerHistory> outputs,bool allowUpdateOnly=false)
    {
        if (!ReferenceEquals(candidate,_pending)) throw new InvalidOperationException("Rejected stale Pivot pose commit.");
        var resolved=_sources.Resolve(candidate.Sources,outputs);
        if (candidate.Sources.Machine.Active && ((!HasPose && !allowUpdateOnly) || HasPose && (_evaluated is null || resolved.PivotOutput!=_evaluated.PivotOutput ||
            resolved.Pivot!=_evaluated.Pivot || resolved.HipFire!=_evaluated.HipFire || !resolved.MachineOutputs.SequenceEqual(_evaluated.MachineOutputs))))
            throw new InvalidOperationException("Pivot did not evaluate this Sync snapshot.");
    }
    public void Commit(LyraPivotLayerPoseCandidate candidate,ReadOnlySpan<AlsAssetPlayerHistory> outputs,bool allowUpdateOnly=false)
    {
        ValidateCommit(candidate,outputs,allowUpdateOnly); _sources.Commit(candidate.Sources,outputs);
        for (var state=0;state<2;state++)
        {
            if (HasPose && candidate.Sources.Machine.Active && candidate.Sources.Machine.State.Current==state)
            { _orientation[state]=_warpResult.Orientation; _stride[state]=_warpResult.Stride; }
            else
            { _orientation[state]=_updatedOrientation[state];if(candidate.Sources.Machine.Initializations[state]>0)_stride[state]=_stride[state].Reinitialize(); }
        }
        _pending=null;
    }
    public void Cancel() { _sources.Cancel(); _pending=null; _evaluated=null; HasPose=false; }
}
