using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraStopLayerPoseCandidate(LyraStopLayerSourceCandidate Sources);

// Original four-node Stop provider: source callbacks, common Sync, then
// HipFire layered blend. Publishes no clocks before the character commits.
internal sealed class LyraStopLayerPoseHost
{
    private readonly LyraStopLayerSourceHost _sources;
    private readonly LyraLogicalSourceBank _bank;
    private readonly LyraCycleLayerPosePolicy _policy;
    private readonly LyraLogicalSourceSampler[] _samplers;
    private readonly LyraLogicalSourceDefinition[] _definitions;
    private readonly AlsRawRootMotionIntervalSampler[] _roots;
    private readonly bool _rootOverride;
    private readonly bool _hipFireLooping;
    private readonly AlsPrecisePose[] _base = new AlsPrecisePose[81], _child = new AlsPrecisePose[81],
        _pose = new AlsPrecisePose[81];
    private readonly AlsQuaternion[] _scratch = new AlsQuaternion[243];
    private readonly float[] _weights = new float[81];
    private readonly LyraCurveSample[] _baseCurves, _childCurves, _curves;
    private readonly LyraAttributeSample[] _baseAttributes, _childAttributes, _attributes;
    private LyraStopLayerPoseCandidate? _pending;
    private LyraStopLayerResolvedSources _evaluated;
    private LyraRootMotionAttribute _root;
    public bool HasPose { get; private set; }
    public ReadOnlySpan<AlsPrecisePose> Pose => HasPose ? _pose : throw new InvalidOperationException("Stop has no pose.");
    public ReadOnlySpan<LyraCurveSample> Curves => HasPose ? _curves : throw new InvalidOperationException("Stop has no curves.");
    public ReadOnlySpan<LyraAttributeSample> Attributes => HasPose ? _attributes : throw new InvalidOperationException("Stop has no attributes.");
    public LyraRootMotionAttribute RootMotion => HasPose ? _root : throw new InvalidOperationException("Stop has no root attribute.");
    public LyraStopState Stop => _sources.Stop;
    public LyraHipFireSourceState HipFire => _sources.HipFire;
    public int SyncGroupId => _sources.SyncGroupId;

    internal bool InitializeSourceNode(int node)
    {
        if (_pending is not null) throw new InvalidOperationException("Stop phase initialization needs an idle pose host.");
        return _sources.InitializeSourceNode(node);
    }
    public LyraStopLayerPoseHost(LyraStopLayerSourceHost sources, LyraLogicalSourceBank bank,
        LyraCycleLayerPosePolicy policy, IReadOnlyList<string> slots, bool hipFireLooping, LyraCompressedRootBank roots)
    {
        if (policy.Mask.Length != 81 || policy.CurveSources.Length != bank.Curves.Names.Length ||
            policy.AttributeBones.Length != bank.Curves.Attributes.Layout.Length || policy.AttributeOverrides.Length != policy.AttributeBones.Length ||
            policy.Mask.Any(w => !float.IsFinite(w) || w is < 0 or > 1) || policy.AttributeBones.Any(b => (uint)b >= 81) ||
            policy.CurveSources.Any(i => i is < -1 or > 0)) throw new ArgumentException("Invalid Stop pose policy.");
        _sources = sources; _bank = bank; _hipFireLooping = hipFireLooping;
        _policy = new(policy.Mask.ToArray(), policy.CurveSources.ToArray(), policy.AttributeBones.ToArray(), policy.AttributeOverrides.ToArray());
        _definitions = slots.Select(bank.Get).ToArray();
        if (_definitions.Any(d => d.IsAdditive)) throw new NotSupportedException("Stop requires absolute sources.");
        _samplers = slots.Select(bank.CreateSampler).ToArray();
        _roots = _definitions.Select(d => roots.CreateSampler(d.Slot, bank.Reference[0], d.NormalizedRootMotionScale)).ToArray();
        _rootOverride = LyraRootMotionAttribute.LoadBlendPolicy();
        var n = bank.Curves.Names.Length; _baseCurves = new LyraCurveSample[n]; _childCurves = new LyraCurveSample[n]; _curves = new LyraCurveSample[n];
        n = bank.Curves.Attributes.Layout.Length; _baseAttributes = new LyraAttributeSample[n]; _childAttributes = new LyraAttributeSample[n]; _attributes = new LyraAttributeSample[n];
    }
    public LyraStopLayerPoseCandidate Prepare(in LyraStopInput input, float delta, float weight,
        double hipWeight, bool active, bool reset, int sampleStart = 0)
    {
        if (_pending is not null) throw new InvalidOperationException("Stop pose candidate is pending.");
        HasPose = false;
        try { return _pending = new(_sources.Prepare(input, delta, weight, hipWeight, active, reset, sampleStart)); }
        catch { _sources.Cancel(); throw; }
    }
    public void Evaluate(LyraStopLayerPoseCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {
        if (!ReferenceEquals(candidate, _pending) || !candidate.Sources.Stop.Active)
            throw new InvalidOperationException("Rejected stale or hidden Stop pose.");
        HasPose = false;
        var resolved = _sources.Resolve(candidate.Sources, outputs);
        var id = resolved.Stop.AssetId;
        // Evaluator callback reads ExplicitTime, while native Evaluate samples
        // InternalTimeAccumulator after Sync. Keep these two clocks distinct.
        _samplers[id].Sample(resolved.Stop.Time, _base, _baseCurves, _baseAttributes);
        _root = LyraRootMotionAttribute.Sample(_definitions[id], _roots[id], resolved.StopOutput.DeltaPrevious,
            resolved.StopOutput.Delta, false, retainedEvaluatorClock: true);
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
        _evaluated = resolved; HasPose = true;
    }
    public void ValidateCommit(LyraStopLayerPoseCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> outputs,bool allowUpdateOnly=false)
    {
        if (!ReferenceEquals(candidate, _pending)) throw new InvalidOperationException("Rejected stale Stop pose commit.");
        var resolved = _sources.Resolve(candidate.Sources, outputs);
        if (candidate.Sources.Stop.Active && ((!HasPose && !allowUpdateOnly) || HasPose && resolved != _evaluated))
            throw new InvalidOperationException("Stop did not evaluate this Sync snapshot.");
    }
    public void Commit(LyraStopLayerPoseCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> outputs,bool allowUpdateOnly=false)
    {
        ValidateCommit(candidate, outputs,allowUpdateOnly); _sources.Commit(candidate.Sources, outputs);
        _pending = null;
    }
    public void Cancel() { _sources.Cancel(); _pending = null; HasPose = false; }
}
