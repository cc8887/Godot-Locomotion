using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraMainCycleLeanCandidate(LyraObservedCycleCandidate Joint,
    LyraMainLeanCompositionCandidate Lean, LyraMainLeanSyncInputs LeanInputs,
    AlsAssetSyncPlayer[] Players, AlsAssetSyncSample[] Samples, int[] Groups, float[] InertiaDurations);

// Original Main ApplyAdditive17 -> Linked15/Lean16. The enclosing Main graph
// still supplies relevance/weight. This owner collects both branches for one
// Sync and publishes their histories together. Its basis is the real Cycle.
internal sealed class LyraMainCycleLeanHost
{
    public LyraObservedCycleHost Joint { get; }
    private readonly LyraCycleLayerPoseHost _cycle;
    private readonly LyraMainLeanCompositionHost _lean;
    private LyraMainCycleLeanCandidate? _pending;
    private LyraMainLeanCompositionResolved? _resolved;
    private AlsAssetPlayerHistory[]? _players;
    private AlsAssetSampleHistory[]? _samples;
    private readonly AlsPrecisePose[] _pose = new AlsPrecisePose[81];
    private readonly LyraCurveSample[] _curves;
    private readonly LyraAttributeSample[] _attributes;
    private LyraRootMotionAttribute _root;
    private float _pendingBlendIn;
    public bool HasPose { get; private set; }
    public ReadOnlySpan<AlsPrecisePose> Pose => HasPose ? _pose : throw new InvalidOperationException("Main Cycle has no pose.");
    public ReadOnlySpan<LyraCurveSample> Curves => HasPose ? _curves : throw new InvalidOperationException("Main Cycle has no curves.");
    public ReadOnlySpan<LyraAttributeSample> Attributes => HasPose ? _attributes : throw new InvalidOperationException("Main Cycle has no attributes.");
    public LyraRootMotionAttribute RootMotion => HasPose ? _root : throw new InvalidOperationException("Main Cycle has no root attribute.");
    public LyraMainLeanNodeState LeanState => _lean.LeanStates[1];
    public LyraMainCycleLeanHost(LyraCycleLayerPoseHost cycle, LyraLogicalSourceBank bank,
        AlsAssetSyncSequence[] sequences, int sequenceBase, LyraLayerSignature signature,
        int playerBase = 1000, int assetId = 9000, long epoch = 1)
    {
        if (signature.Hook != LyraLayerHook.FullBody_CycleState || signature.Group != "ItemAnimLayers" ||
            signature.BlendInProfile != "" || signature.BlendOutProfile != "")
            throw new NotSupportedException("Changed linked Cycle blend contract.");
        _pendingBlendIn = signature.BlendInTime;
        _cycle = cycle; Joint = new(cycle, completeMain: true);
        _lean = new(bank, sequences, playerBase, assetId, sequenceBase, epoch);
        _curves = new LyraCurveSample[bank.Curves.Names.Length];
        _attributes = new LyraAttributeSample[bank.Curves.Attributes.Layout.Length];
    }
    public LyraMainCycleLeanCandidate Prepare(in LyraMainUpdateInput input, float delta, float weight,
        double hipFireWeight, bool active, bool initialize, AlsPrecisePose component, AlsQuaternion relativeRotation)
    {
        if (_pending is not null) throw new InvalidOperationException("Main Cycle frame is pending.");
        HasPose = false;
        try
        {
            var joint = Joint.PrepareFull(input, delta, weight, hipFireWeight, active, initialize, component, relativeRotation);
            var lean = _lean.PrepareObserved(joint.Main!, delta, [0, weight, 0], [false, active, false],
                [false, initialize, false], [0, 1, 2]);
            var sources = joint.Cycle.Sources;
            var collected = _lean.CollectAtCommonSync(lean, sources.Samples.Length);
            var requests = new List<float>();
            if (sources.Cycle.InertiaDuration > 0) requests.Add(sources.Cycle.InertiaDuration);
            // LinkedAnimGraph consumes pending graph blending after its child
            // update. Initialize/cache and hidden frames do not consume it.
            if (active && _pendingBlendIn >= 0) requests.Add(_pendingBlendIn);
            // ApplyAdditive traverses Base first, then Additive. Preserve that
            // order even when HipFire has tiny weight or the root is hidden.
            return _pending = new(joint, lean, collected, [.. sources.Players, .. collected.Players],
                [.. sources.Samples, .. collected.Samples], [.. sources.Groups, .. collected.Players.Select(_ => -1)], requests.ToArray());
        }
        catch { Cancel(); throw; }
    }
    public void Resolve(LyraMainCycleLeanCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> players,
        ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        Validate(candidate);
        if (_resolved is not null) throw new InvalidOperationException("Main Cycle was already synchronized.");
        _resolved = _lean.Resolve(candidate.Lean, candidate.LeanInputs, players, samples);
        _players = players.ToArray(); _samples = samples.ToArray();
    }
    public LyraMainLeanNodeState PreparedLean(LyraMainCycleLeanCandidate candidate)
    { Validate(candidate); return (_resolved ?? throw new InvalidOperationException("Main Cycle needs Sync.")).Lean.States[1]; }
    public void Evaluate(LyraMainCycleLeanCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> players,
        ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        ValidateSync(candidate, players, samples);
        Joint.Evaluate(candidate.Joint, players);
        _root = _lean.Evaluate(_resolved!, 1, _cycle.Pose, _cycle.Curves, _cycle.Attributes,
            _cycle.RootMotion, _pose, _curves, _attributes);
        HasPose = true;
    }
    public void ValidateCommit(LyraMainCycleLeanCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> players,
        ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        ValidateSync(candidate, players, samples);
        if (candidate.Joint.Cycle.Sources.Cycle.Ticked && !HasPose) throw new InvalidOperationException("Main Cycle commit needs evaluation.");
        _lean.ValidateCommit(_resolved!); Joint.ValidateCommit(candidate.Joint, players);
    }
    public void Commit(LyraMainCycleLeanCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> players,
        ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        ValidateCommit(candidate, players, samples);
        Joint.Commit(candidate.Joint, players); _lean.Commit(_resolved!);
        if (candidate.Joint.Cycle.Sources.Cycle.Ticked) _pendingBlendIn = -1;
        _pending = null; _resolved = null; _players = null; _samples = null;
    }
    public void Cancel()
    { Joint.Cancel(); _lean.Cancel(); _pending = null; _resolved = null; _players = null; _samples = null; HasPose = false; }
    private void Validate(LyraMainCycleLeanCandidate candidate)
    { if (!ReferenceEquals(candidate, _pending)) throw new InvalidOperationException("Stale Main Cycle candidate."); }
    private void ValidateSync(LyraMainCycleLeanCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> players,
        ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        Validate(candidate);
        if (_resolved is null || _players is null || _samples is null || !players.SequenceEqual(_players) || !samples.SequenceEqual(_samples))
            throw new InvalidOperationException("Different Main Cycle Sync snapshot.");
    }
}
