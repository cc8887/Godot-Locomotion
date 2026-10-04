using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraStartLayerPoseCandidate(LyraStartLayerSourceCandidate Sources,
    AlsOrientationWarpingInput Orientation, AlsStrideWarpingInput Stride, bool Reset);

// Actual Start provider closure: source callbacks -> HipFire -> component-space
// Orientation/Stride -> local pose. No independent clock or Skeleton3D writes.
internal sealed class LyraStartLayerPoseHost
{
    private readonly LyraStartLayerSourceHost _sources;
    private readonly LyraLogicalSourceBank _bank;
    private readonly LyraCycleLayerPosePolicy _policy;
    private readonly LyraLogicalSourceSampler[] _samplers;
    private readonly LyraLogicalSourceDefinition[] _definitions;
    private readonly AlsRawRootMotionIntervalSampler[] _roots;
    private readonly AlsCycleWarping _warps;
    private readonly bool _rootOverride;
    private readonly bool _hipFireLooping;
    private readonly AlsPrecisePose[] _base = new AlsPrecisePose[81], _child = new AlsPrecisePose[81],
        _pose = new AlsPrecisePose[81], _warped = new AlsPrecisePose[81];
    private readonly AlsQuaternion[] _scratch = new AlsQuaternion[243];
    private readonly float[] _weights = new float[81];
    private readonly LyraCurveSample[] _baseCurves, _childCurves, _curves;
    private readonly LyraAttributeSample[] _baseAttributes, _childAttributes, _attributes;
    private LyraStartLayerPoseCandidate? _pending;
    private LyraStartLayerResolvedSources _evaluated;
    private AlsCycleWarpingOutput _warpResult;
    private LyraRootMotionAttribute _root;
    public AlsOrientationWarpingState OrientationState { get; private set; } = AlsOrientationWarpingState.Initial;
    private AlsOrientationWarpingState _updatedOrientation;
    public AlsStrideWarpingState StrideState { get; private set; } = AlsStrideWarpingState.Initial;
    public bool HasPose { get; private set; }
    public ReadOnlySpan<AlsPrecisePose> Pose => HasPose ? _pose : throw new InvalidOperationException("Start has no pose.");
    public ReadOnlySpan<LyraCurveSample> Curves => HasPose ? _curves : throw new InvalidOperationException("Start has no curves.");
    public ReadOnlySpan<LyraAttributeSample> Attributes => HasPose ? _attributes : throw new InvalidOperationException("Start has no attributes.");
    public LyraRootMotionAttribute RootMotion => HasPose ? _root : throw new InvalidOperationException("Start has no root attribute.");
    public LyraStartState Start => _sources.Start;
    internal LyraStartState PreparedStart => _sources.PreparedStart;
    public LyraHipFireSourceState HipFire => _sources.HipFire;

    internal bool InitializeSourceNode(int node)
    {
        if (_pending is not null) throw new InvalidOperationException("Start phase initialization needs an idle pose host.");
        return _sources.InitializeSourceNode(node);
    }
    public LyraStartLayerPoseHost(LyraStartLayerSourceHost sources, LyraLogicalSourceBank bank,
        LyraCycleLayerPosePolicy policy, IReadOnlyList<string> slots, AlsOrientationWarping orientation, AlsStrideWarping stride,
        bool hipFireLooping, LyraCompressedRootBank roots)
    {
        if (policy.Mask.Length != 81 || policy.CurveSources.Length != bank.Curves.Names.Length ||
            policy.AttributeBones.Length != bank.Curves.Attributes.Layout.Length || policy.AttributeOverrides.Length != policy.AttributeBones.Length ||
            policy.Mask.Any(w => !float.IsFinite(w) || w is < 0 or > 1) || policy.AttributeBones.Any(b => (uint)b >= 81) ||
            policy.CurveSources.Any(i => i is < -1 or > 0)) throw new ArgumentException("Invalid Start pose policy.");
        _sources = sources; _bank = bank; _hipFireLooping = hipFireLooping;
        _policy = new(policy.Mask.ToArray(), policy.CurveSources.ToArray(), policy.AttributeBones.ToArray(), policy.AttributeOverrides.ToArray());
        _definitions = slots.Select(bank.Get).ToArray();
        if (_definitions.Any(d => d.IsAdditive)) throw new NotSupportedException("Start requires absolute sources.");
        _samplers = slots.Select(bank.CreateSampler).ToArray();
        _roots = _definitions.Select(d => roots.CreateSampler(d.Slot, bank.Reference[0], d.NormalizedRootMotionScale)).ToArray();
        _warps = new(bank.Parents, orientation, stride); _rootOverride = LyraRootMotionAttribute.LoadBlendPolicy();
        var n = bank.Curves.Names.Length; _baseCurves = new LyraCurveSample[n]; _childCurves = new LyraCurveSample[n]; _curves = new LyraCurveSample[n];
        n = bank.Curves.Attributes.Layout.Length; _baseAttributes = new LyraAttributeSample[n]; _childAttributes = new LyraAttributeSample[n]; _attributes = new LyraAttributeSample[n];
    }
    public LyraStartLayerPoseCandidate Prepare(in LyraStartInput input, float delta, float weight, double hipWeight,
        bool active, bool reset, double displacementSpeed, in LyraCycleWarpContext context, int sampleStart = 0)
    {
        if (_pending is not null) throw new InvalidOperationException("Start pose candidate is pending.");
        if (!double.IsFinite(displacementSpeed) || !float.IsFinite((float)displacementSpeed) ||
            !double.IsFinite(context.DirectionAngle) || !float.IsFinite((float)context.DirectionAngle) || context.UpdateCounter < 0)
            throw new ArgumentException("Invalid Start bound pins.");
        context.Component.Validate(); new AlsPrecisePose(default, context.RelativeRotation, AlsDoubleVector.One).Validate();
        HasPose = false;
        try
        {
            var sources = _sources.Prepare(input, delta, weight, hipWeight, active, reset, sampleStart);
            _updatedOrientation=reset?OrientationState.Reset():OrientationState;
            if(sources.Start.Active)_updatedOrientation=_updatedOrientation.PrepareUpdate(context.UpdateCounter);
            // Exposed inputs execute after the child Start callback. Alpha is
            // the fresh candidate value; angle is Main's WITH-offset direction.
            return _pending = new(sources, new(delta, (float)context.DirectionAngle, default, context.Component,
                context.RelativeRotation, 1, weight, context.UpdateCounter, reset),
                new(delta, (float)displacementSpeed, Math.Clamp((float)sources.Start.State.StrideAlpha, 0, 1), context.Component, reset), reset);
        }
        catch { _sources.Cancel(); throw; }
    }
    public void Evaluate(LyraStartLayerPoseCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {
        if (!ReferenceEquals(candidate, _pending) || !candidate.Sources.Start.Active)
            throw new InvalidOperationException("Rejected stale or hidden Start pose.");
        HasPose = false;
        var resolved = _sources.Resolve(candidate.Sources, outputs);
        var id = resolved.Start.AssetId;
        // Evaluator callback reads ExplicitTime, while native Evaluate samples
        // InternalTimeAccumulator after Sync. Keep these two clocks distinct.
        _samplers[id].Sample(resolved.Start.Time, _base, _baseCurves, _baseAttributes);
        _root = LyraRootMotionAttribute.Sample(_definitions[id], _roots[id], resolved.StartOutput.DeltaPrevious,
            resolved.StartOutput.Delta, false);
        if (!candidate.Sources.HipFireTicked)
        { _base.CopyTo(_pose, 0); _baseCurves.CopyTo(_curves, 0); _baseAttributes.CopyTo(_attributes, 0); }
        else
        {
            id = resolved.HipFire.AssetId;
            _samplers[id].Sample(resolved.HipFire.Time, _child, _childCurves, _childAttributes);
            for (var bone = 0; bone < 81; bone++)
            { var w = _policy.Mask[bone]*candidate.Sources.BlendWeight; _weights[bone] = w > 1e-5f ? w : 0; }
            AlsMeshSpacePoseBlend.Blend(_base, _child, _bank.Parents, _weights, _scratch, _pose, singleRotationAlpha: true);
            for (var i = 0; i < _curves.Length; i++) _curves[i] = LyraLayeredDataBlend.OverrideCurve(_baseCurves[i], _childCurves[i], _policy.CurveSources[i]);
            for (var i = 0; i < _attributes.Length; i++) _attributes[i] = LyraLayeredDataBlend.BlendInteger(_baseAttributes[i], _childAttributes[i],
                _weights[_policy.AttributeBones[i]], _policy.AttributeOverrides[i]);
            _root = LyraRootMotionAttribute.Blend(_root, LyraRootMotionAttribute.Sample(_definitions[id], _roots[id],
                resolved.HipFire.DeltaPrevious, resolved.HipFire.Delta, _hipFireLooping), _weights[0], _rootOverride);
        }
        _warpResult = _warps.Evaluate(_updatedOrientation, StrideState, candidate.Orientation, candidate.Stride,
            _pose, _root.Present, _root.Value, _warped);
        _warped.CopyTo(_pose, 0); _root = new(_warpResult.RootMotion, _warpResult.RootPresent);
        _evaluated = resolved; HasPose = true;
    }
    public void ValidateCommit(LyraStartLayerPoseCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> outputs,bool allowUpdateOnly=false)
    {
        if (!ReferenceEquals(candidate, _pending)) throw new InvalidOperationException("Rejected stale Start pose commit.");
        var resolved = _sources.Resolve(candidate.Sources, outputs);
        if (candidate.Sources.Start.Active && ((!HasPose && !allowUpdateOnly) || HasPose && resolved != _evaluated))
            throw new InvalidOperationException("Start did not evaluate this Sync snapshot.");
    }
    public void Commit(LyraStartLayerPoseCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> outputs,bool allowUpdateOnly=false)
    {
        ValidateCommit(candidate, outputs,allowUpdateOnly); _sources.Commit(candidate.Sources, outputs);
        OrientationState = HasPose && candidate.Sources.Start.Active ? _warpResult.Orientation : _updatedOrientation;
        StrideState = HasPose && candidate.Sources.Start.Active ? _warpResult.Stride : candidate.Reset ? StrideState.Reinitialize() : StrideState;
        _pending = null;
    }
    public void Cancel() { _sources.Cancel(); _pending = null; HasPose = false; }
}
