using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraMainStartLeanCandidate(LyraMainUpdateCandidate Main, LyraStartLayerPoseCandidate Start,
    LyraMainLeanCompositionCandidate Lean, LyraMainLeanSyncInputs LeanInputs,
    AlsAssetSyncPlayer[] Players, AlsAssetSyncSample[] Samples, int[] Groups, float[] InertiaDurations);

// Original ApplyAdditive13 -> Linked11/Lean12. Main computes the bound pins once,
// then traverses both branches; all histories publish only after common Sync.
internal sealed class LyraMainStartLeanHost
{
    private readonly LyraMainUpdateHost _main = new();
    private readonly LyraStartLayerPoseHost _start;
    private readonly LyraMainLeanCompositionHost _lean;
    private LyraMainStartLeanCandidate? _pending;
    private LyraMainLeanCompositionResolved? _resolved;
    private AlsAssetPlayerHistory[]? _players;
    private AlsAssetSampleHistory[]? _samples;
    private readonly AlsPrecisePose[] _pose = new AlsPrecisePose[81];
    private readonly LyraCurveSample[] _curves;
    private readonly LyraAttributeSample[] _attributes;
    private LyraRootMotionAttribute _root;
    private float _pendingBlendIn;
    public bool HasPose { get; private set; }
    public LyraMainObservationState Main => _main.State;
    public LyraMainTailState Tail => _main.Tail;
    public LyraMainLeanNodeState LeanState => _lean.LeanStates[2];
    public ReadOnlySpan<AlsPrecisePose> Pose => HasPose ? _pose : throw new InvalidOperationException("Main Start has no pose.");
    public ReadOnlySpan<LyraCurveSample> Curves => HasPose ? _curves : throw new InvalidOperationException("Main Start has no curves.");
    public ReadOnlySpan<LyraAttributeSample> Attributes => HasPose ? _attributes : throw new InvalidOperationException("Main Start has no attributes.");
    public LyraRootMotionAttribute RootMotion => HasPose ? _root : throw new InvalidOperationException("Main Start has no root attribute.");

    public LyraMainStartLeanHost(LyraStartLayerPoseHost start, LyraLogicalSourceBank bank,
        AlsAssetSyncSequence[] sequences, int sequenceBase, LyraLayerSignature signature,
        int playerBase = 1000, int assetId = 9000, long epoch = 1)
    {
        if (signature.Hook != LyraLayerHook.FullBody_StartState || signature.Group != "ItemAnimLayers" ||
            signature.BlendInProfile != "" || signature.BlendOutProfile != "")
            throw new NotSupportedException("Changed linked Start blend contract.");
        _pendingBlendIn = signature.BlendInTime; _start = start;
        _lean = new(bank,sequences,playerBase,assetId,sequenceBase,epoch);
        _curves = new LyraCurveSample[bank.Curves.Names.Length];
        _attributes = new LyraAttributeSample[bank.Curves.Attributes.Layout.Length];
    }
    public LyraMainStartLeanCandidate Prepare(in LyraMainUpdateInput input, float delta, float weight,
        double hipFireWeight, bool active, bool initialize, AlsPrecisePose component, AlsQuaternion relativeRotation)
    {
        if (_pending is not null) throw new InvalidOperationException("Main Start frame is pending.");
        HasPose = false;
        try
        {
            var main = _main.Prepare(input,delta); var state = main.State;
            var direction = state.Direction switch { 0 => LyraCardinalDirection.Forward, 1 => LyraCardinalDirection.Backward,
                2 => LyraCardinalDirection.Left, 3 => LyraCardinalDirection.Right, _ => throw new InvalidOperationException("Invalid native cardinal direction.") };
            var start = _start.Prepare(new(state.Crouching,state.Ads,direction,state.Displacement),delta,weight,hipFireWeight,
                active,initialize,state.DisplacementSpeed,new(state.DirectionAngleWithOffset,component,relativeRotation,main.Observation.Frame));
            var lean = _lean.PrepareObserved(main,delta,[0,0,weight],[false,false,active],
                [false,false,initialize],[0,1,2]);
            var sources = start.Sources; var collected = _lean.CollectAtCommonSync(lean,sources.Samples.Length);
            // Linked consumes its pending request after the Base child update.
            var requests = active && _pendingBlendIn >= 0 ? new[] { _pendingBlendIn } : [];
            return _pending = new(main,start,lean,collected,[..sources.Players,..collected.Players],
                [..sources.Samples,..collected.Samples],[..sources.Groups,..collected.Players.Select(_=>-1)],requests);
        }
        catch { Cancel(); throw; }
    }
    public void Resolve(LyraMainStartLeanCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> players,
        ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        Validate(candidate);
        if (_resolved is not null) throw new InvalidOperationException("Main Start was already synchronized.");
        _resolved = _lean.Resolve(candidate.Lean,candidate.LeanInputs,players,samples);
        _players = players.ToArray(); _samples = samples.ToArray();
    }
    public LyraMainLeanNodeState PreparedLean(LyraMainStartLeanCandidate candidate)
    { Validate(candidate); return (_resolved ?? throw new InvalidOperationException("Main Start needs Sync.")).Lean.States[2]; }
    public void Evaluate(LyraMainStartLeanCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> players,
        ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        ValidateSync(candidate,players,samples); _start.Evaluate(candidate.Start,players);
        _root = _lean.Evaluate(_resolved!,2,_start.Pose,_start.Curves,_start.Attributes,_start.RootMotion,_pose,_curves,_attributes);
        HasPose = true;
    }
    public void ValidateCommit(LyraMainStartLeanCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> players,
        ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        ValidateSync(candidate,players,samples);
        if (candidate.Start.Sources.Start.Active && !HasPose) throw new InvalidOperationException("Main Start commit needs evaluation.");
        _main.ValidateCommit(candidate.Main); _start.ValidateCommit(candidate.Start,players); _lean.ValidateCommit(_resolved!);
    }
    public void Commit(LyraMainStartLeanCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> players,
        ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        ValidateCommit(candidate,players,samples);
        _start.Commit(candidate.Start,players); _main.Commit(candidate.Main); _lean.Commit(_resolved!);
        if (candidate.Start.Sources.Start.Active) _pendingBlendIn = -1;
        _pending = null; _resolved = null; _players = null; _samples = null;
    }
    public void Cancel()
    { _start.Cancel(); _main.Cancel(); _lean.Cancel(); _pending = null; _resolved = null; _players = null; _samples = null; HasPose = false; }
    private void Validate(LyraMainStartLeanCandidate candidate)
    { if (!ReferenceEquals(candidate,_pending)) throw new InvalidOperationException("Stale Main Start candidate."); }
    private void ValidateSync(LyraMainStartLeanCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> players,
        ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        Validate(candidate);
        if (_resolved is null || _players is null || _samples is null || !players.SequenceEqual(_players) || !samples.SequenceEqual(_samples))
            throw new InvalidOperationException("Different Main Start Sync snapshot.");
    }
}
