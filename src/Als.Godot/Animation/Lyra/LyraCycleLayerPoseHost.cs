using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraCycleLayerPoseCandidate(LyraCycleLayerSourceCandidate Sources,
    AlsOrientationWarpingInput? Orientation = null, bool ResetOrientation = false, AlsStrideWarpingInput? Stride = null);

// Original Cycle source/provider, HipFire and dual Warp pose. Source clocks
// belong to the character's common Sync; bound inputs follow child callbacks.
internal sealed class LyraCycleLayerPoseHost
{
    private readonly LyraCycleLayerSourceHost _sources;
    private readonly LyraLogicalSourceBank _bank;
    private readonly LyraCycleLayerPosePolicy _policy;
    private readonly LyraLogicalSourceSampler[] _samplers;
    private readonly LyraLogicalSourceDefinition[] _definitions;
    private readonly AlsRawRootMotionIntervalSampler[] _rootSamplers;
    private readonly bool _generateRootMotion;
    private readonly bool _rootOverride;
    private LyraRootMotionAttribute _rootMotion;
    private readonly AlsOrientationWarping? _orientation;
    private readonly AlsCycleWarping? _warping;
    private readonly LyraCycleRuntimeBindings? _bindings;
    private AlsStrideWarpingState _strideState = AlsStrideWarpingState.Initial;
    private AlsCycleWarpingOutput _warpResult;
    public AlsStrideWarpingState StrideState => _strideState;
    private readonly AlsPrecisePose[] _warped = new AlsPrecisePose[81];
    private AlsOrientationWarpingState _orientationState = AlsOrientationWarpingState.Initial;
    private AlsOrientationWarpingState _updatedOrientation;
    private AlsOrientationWarpingOutput _orientationResult;
    public AlsOrientationWarpingState OrientationState => _orientationState;
    private readonly AlsPrecisePose[] _base = new AlsPrecisePose[81], _child = new AlsPrecisePose[81], _pose = new AlsPrecisePose[81];
    private readonly AlsQuaternion[] _scratch = new AlsQuaternion[243];
    private readonly float[] _weights = new float[81];
    private readonly LyraCurveSample[] _baseCurves, _childCurves, _curves;
    private readonly LyraAttributeSample[] _baseAttributes, _childAttributes, _attributes;
    private LyraCycleLayerPoseCandidate? _pending;
    private LyraCycleLayerResolvedSources _evaluatedSources;
    private bool _hasPose;
    public LyraCycleState Cycle => _sources.Cycle;
    internal LyraCycleState PreparedCycle => _sources.PreparedCycle;
    public LyraHipFireSourceState HipFire => _sources.HipFire;
    public bool HasPose => _hasPose;
    public ReadOnlySpan<AlsPrecisePose> Pose => _hasPose ? _pose : throw new InvalidOperationException("Cycle has no current pose.");
    public ReadOnlySpan<LyraCurveSample> Curves => _hasPose ? _curves : throw new InvalidOperationException("Cycle has no current curves.");
    public ReadOnlySpan<LyraAttributeSample> Attributes => _hasPose ? _attributes : throw new InvalidOperationException("Cycle has no current attributes.");
    // A local Cycle output remains distinct from the enclosing Main graph.
    public bool HasGeneratedRootMotion => _hasPose && _rootMotion.Present;
    public LyraRootMotionAttribute RootMotion => _hasPose ? _rootMotion : throw new InvalidOperationException("Cycle has no current root attribute.");

    internal bool InitializeSourceNode(int node)
    {
        if (_pending is not null) throw new InvalidOperationException("Cycle phase initialization needs an idle pose host.");
        return _sources.InitializeSourceNode(node);
    }
    public LyraCycleLayerPoseHost(LyraCycleLayerSourceHost sources, LyraLogicalSourceBank bank,
        LyraCycleLayerPosePolicy policy, IReadOnlyList<string> slotsByAssetId, bool generateRootMotion = false,
        AlsOrientationWarping? orientation = null, AlsStrideWarping? stride = null, LyraCycleRuntimeBindings? bindings = null,
        LyraCompressedRootBank? compressedRoots = null)
    {
        _sources = sources; _bank = bank;
        if (policy.Mask.Length != 81 || policy.CurveSources.Length != bank.Curves.Names.Length ||
            policy.AttributeBones.Length != bank.Curves.Attributes.Layout.Length ||
            policy.AttributeOverrides.Length != policy.AttributeBones.Length ||
            policy.AttributeBones.Any(b => (uint)b >= 81) || policy.Mask.Any(w => !float.IsFinite(w) || w is < 0 or > 1) ||
            policy.CurveSources.Any(i => i is < -1 or > 0)) throw new ArgumentException("Invalid Cycle pose layout.");
        _policy = new(policy.Mask.ToArray(), policy.CurveSources.ToArray(), policy.AttributeBones.ToArray(), policy.AttributeOverrides.ToArray());
        if (slotsByAssetId.Any(s => bank.Get(s).IsAdditive)) throw new NotSupportedException("Cycle requires absolute source poses.");
        _samplers = slotsByAssetId.Select(bank.CreateSampler).ToArray();
        _definitions = slotsByAssetId.Select(bank.Get).ToArray();
        _rootSamplers = _definitions.Select(d => compressedRoots is null
            ? new AlsRawRootMotionIntervalSampler(d.Data, bank.Reference[0], d.NormalizedRootMotionScale)
            : compressedRoots.CreateSampler(d.Slot,bank.Reference[0],d.NormalizedRootMotionScale)).ToArray();
        _generateRootMotion = generateRootMotion;
        _rootOverride = generateRootMotion && LyraRootMotionAttribute.LoadBlendPolicy();
        if (orientation is not null && !generateRootMotion) throw new ArgumentException("Orientation needs generated root attributes.");
        _orientation = orientation;
        if (stride is not null && orientation is null) throw new ArgumentException("Stride requires the original Orientation chain.");
        _warping = stride is null ? null : new(bank.Parents, orientation!, stride);
        if (bindings is not null && _warping is null) throw new ArgumentException("Bound Cycle requires both original Warps.");
        _bindings = bindings;
        var curves = bank.Curves.Names.Length; var attributes = bank.Curves.Attributes.Layout.Length;
        _baseCurves = new LyraCurveSample[curves]; _childCurves = new LyraCurveSample[curves]; _curves = new LyraCurveSample[curves];
        _baseAttributes = new LyraAttributeSample[attributes]; _childAttributes = new LyraAttributeSample[attributes]; _attributes = new LyraAttributeSample[attributes];
    }
    // SkeletalControlBase updates children before executing exposed inputs:
    // use this candidate's fresh callback alpha, never the previous committed one.
    public LyraCycleLayerPoseCandidate PrepareBound(in LyraCycleInput input, float delta, float weight,
        double hipFireWeight, bool active, bool reinitialize, in LyraCycleWarpContext context, int sampleStart = 0)
    {
        if (_pending is not null || _bindings is null) throw new InvalidOperationException("Cycle binding host is unavailable or pending.");
        _hasPose = false;
        try
        {
            var sources = _sources.Prepare(input, delta, weight, hipFireWeight, active, reinitialize, sampleStart);
            var bound = _bindings.Prepare(context, input.DisplacementSpeed, sources.Cycle.State.StrideAlpha, delta, weight, reinitialize);
            _updatedOrientation=reinitialize?_orientationState.Reset():_orientationState;
            if(sources.Cycle.Ticked && bound.Orientation.Alpha > 1e-5f)
                _updatedOrientation=_updatedOrientation.PrepareUpdate(bound.Orientation.UpdateCounter);
            return _pending = new(sources, active ? bound.Orientation : null, reinitialize, active ? bound.Stride : null);
        }
        catch { _sources.Cancel(); throw; }
    }
    public LyraCycleLayerPoseCandidate Prepare(in LyraCycleInput input, float delta, float weight,
        double hipFireWeight, bool active, bool reinitialize, int sampleStart = 0, AlsOrientationWarpingInput? orientation = null,
        AlsStrideWarpingInput? stride = null)
    {
        if (_bindings is not null) throw new InvalidOperationException("Bound Cycle must prepare from its original Main context.");
        if (_pending is not null) throw new InvalidOperationException("Cycle pose candidate is pending.");
        if (_orientation is not null && active && orientation is null || _orientation is null && orientation is not null)
            throw new ArgumentException("Orientation input does not match this Cycle host.");
        if (_warping is not null && active && stride is null || _warping is null && stride is not null)
            throw new ArgumentException("Stride input does not match this Cycle host.");
        _hasPose = false;
        var sources=_sources.Prepare(input, delta, weight, hipFireWeight, active, reinitialize, sampleStart);
        _updatedOrientation=reinitialize?_orientationState.Reset():_orientationState;
        // SkeletalControlBase skips UpdateInternal when ActualAlpha is filtered.
        // Keep the last relevant counter so reactivation detects the native gap.
        if(_orientation is not null && sources.Cycle.Ticked && orientation!.Value.Alpha > 1e-5f)
            _updatedOrientation=_updatedOrientation.PrepareUpdate(orientation.Value.UpdateCounter);
        return _pending = new(sources, orientation, reinitialize, stride);
    }
    public void Evaluate(LyraCycleLayerPoseCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> commonOutputs)
    {
        if (!ReferenceEquals(candidate, _pending) || !candidate.Sources.Cycle.Ticked)
            throw new InvalidOperationException("Rejected stale or hidden Cycle pose evaluation.");
        _hasPose = false;
        var resolved = _sources.Resolve(candidate.Sources, commonOutputs);
        _rootMotion = _generateRootMotion ? LyraRootMotionAttribute.Sample(_definitions[resolved.Cycle.AssetId],
            _rootSamplers[resolved.Cycle.AssetId], resolved.CycleOutput.DeltaPrevious, resolved.CycleOutput.Delta, true) : default;
        _samplers[resolved.Cycle.AssetId].Sample(resolved.Cycle.Time, _base, _baseCurves, _baseAttributes);
        if (!candidate.Sources.HipFireTicked)
        {
            // Native LayeredBoneBlend bypasses without a relevant child. It
            // does not normalize/conjugate the base or apply curve filters.
            _base.CopyTo(_pose, 0); _baseCurves.CopyTo(_curves, 0); _baseAttributes.CopyTo(_attributes, 0);
        }
        else
        {
            _samplers[resolved.HipFire.AssetId].Sample(resolved.HipFire.Time, _child, _childCurves, _childAttributes);
            for (var bone = 0; bone < 81; bone++)
            {
                var raw = _policy.Mask[bone] * candidate.Sources.BlendWeight;
                _weights[bone] = raw > 1e-5f ? raw : 0;
            }
            AlsMeshSpacePoseBlend.Blend(_base, _child, _bank.Parents, _weights, _scratch, _pose, singleRotationAlpha: true);
            for (var id = 0; id < _curves.Length; id++)
                _curves[id] = LyraLayeredDataBlend.OverrideCurve(_baseCurves[id], _childCurves[id], _policy.CurveSources[id]);
            for (var id = 0; id < _attributes.Length; id++)
                _attributes[id] = LyraLayeredDataBlend.BlendInteger(_baseAttributes[id], _childAttributes[id],
                    _weights[_policy.AttributeBones[id]], _policy.AttributeOverrides[id]);
            if (_generateRootMotion)
                _rootMotion = LyraRootMotionAttribute.Blend(_rootMotion, LyraRootMotionAttribute.Sample(_definitions[resolved.HipFire.AssetId],
                    _rootSamplers[resolved.HipFire.AssetId], resolved.HipFire.DeltaPrevious, resolved.HipFire.Delta, true), _weights[0], _rootOverride);
        }
        if (_warping is not null)
        {
            _warpResult = _warping.Evaluate(_updatedOrientation, _strideState, candidate.Orientation!.Value, candidate.Stride!.Value,
                _pose, _rootMotion.Present, _rootMotion.Value, _warped);
            _warped.CopyTo(_pose, 0); _rootMotion = new(_warpResult.RootMotion, _warpResult.RootPresent);
        }
        else if (_orientation is not null)
        {
            _orientationResult = _orientation.Evaluate(_updatedOrientation, candidate.Orientation!.Value,
                _pose, _rootMotion.Present, _rootMotion.Value, _warped);
            _warped.CopyTo(_pose, 0); _rootMotion = new(_orientationResult.RootMotion, _orientationResult.RootPresent);
        }
        _evaluatedSources = resolved; _hasPose = true;
    }
    public void ValidateCommit(LyraCycleLayerPoseCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> commonOutputs,bool allowUpdateOnly=false)
    {
        if (!ReferenceEquals(candidate, _pending)) throw new InvalidOperationException("Rejected stale Cycle pose commit.");
        var resolved = _sources.Resolve(candidate.Sources, commonOutputs);
        if (candidate.Sources.Cycle.Ticked && ((!_hasPose && !allowUpdateOnly) || _hasPose && resolved != _evaluatedSources))
            throw new InvalidOperationException("Cycle pose did not evaluate these common Sync results.");
    }
    public void Commit(LyraCycleLayerPoseCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> commonOutputs,bool allowUpdateOnly=false)
    {
        ValidateCommit(candidate, commonOutputs,allowUpdateOnly);
        _sources.Commit(candidate.Sources, commonOutputs);
        if (_orientation is not null) _orientationState = _hasPose && candidate.Sources.Cycle.Ticked ? (_warping is not null ? _warpResult.Orientation : _orientationResult.State) :
            _updatedOrientation;
        if (_warping is not null) _strideState = _hasPose && candidate.Sources.Cycle.Ticked ? _warpResult.Stride :
            candidate.ResetOrientation ? _strideState.Reinitialize() : _strideState;
        _pending = null;
    }
    public void Cancel() { _sources.Cancel(); _pending = null; _hasPose = false; }

    public LyraCycleLayerPoseCandidate PrepareObserved(LyraMainObservationCandidate observation,
        float delta, float weight, double hipFireWeight, bool active, bool reinitialize,
        AlsPrecisePose component, AlsQuaternion relativeRotation)
        => PrepareBound(observation.CycleInput, delta, weight, hipFireWeight, active, reinitialize,
            new(observation.State.DirectionAngle, component, relativeRotation, observation.Frame));
}
