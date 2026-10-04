using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

// Observation of the actual enclosing machine; its previous weight is distinct
// from the current graph traversal weight. Full transition selection is upstream.
internal readonly record struct LyraStopStateContext(int CurrentState, float PreviousStopWeight);
internal sealed record LyraMainStopCandidate(LyraMainUpdateCandidate Main, LyraStopLayerPoseCandidate Stop,
    LyraStopStateContext Context, int GraphRootYawMode, float[] InertiaDurations);

// Original StateResult18(UpdateStopState) -> Linked19 -> Stop provider. A single
// Main candidate and source transaction retain post-graph side effects together.
internal sealed class LyraMainStopHost
{
    private readonly LyraMainUpdateHost _main = new();
    private readonly LyraStopLayerPoseHost _stop;
    private LyraMainStopCandidate? _pending;
    private float _pendingBlendIn;
    public LyraMainObservationState Main => _main.State;
    public LyraMainTailState Tail => _main.Tail;
    public LyraStopState Stop => _stop.Stop;
    public LyraHipFireSourceState HipFire => _stop.HipFire;
    public bool HasPose => _stop.HasPose;
    public ReadOnlySpan<AlsPrecisePose> Pose => _stop.Pose;
    public ReadOnlySpan<LyraCurveSample> Curves => _stop.Curves;
    public ReadOnlySpan<LyraAttributeSample> Attributes => _stop.Attributes;
    public LyraRootMotionAttribute RootMotion => _stop.RootMotion;

    public LyraMainStopHost(LyraStopLayerPoseHost stop, LyraLayerSignature signature)
    {
        if (signature.Hook != LyraLayerHook.FullBody_StopState || signature.Group != "ItemAnimLayers" ||
            signature.BlendInProfile != "" || signature.BlendOutProfile != "" ||
            !float.IsFinite(signature.BlendInTime)) throw new NotSupportedException("Changed linked Stop contract.");
        _stop = stop; _pendingBlendIn = signature.BlendInTime;
    }
    public LyraMainStopCandidate Prepare(in LyraMainUpdateInput input, in AlsStopMovementSnapshot movement,
        float delta, float weight, double hipFireWeight, bool active, bool initialize, in LyraStopStateContext context)
    {
        if (_pending is not null) throw new InvalidOperationException("Main Stop frame is pending.");
        if (context.CurrentState is not (2 or 3) || !float.IsFinite(context.PreviousStopWeight) ||
            context.PreviousStopWeight is < 0 or > 1) throw new ArgumentException("Invalid native Stop state context.");
        try
        {
            var main = _main.Prepare(input, delta); var state = main.State;
            var direction = state.Direction switch { 0 => LyraCardinalDirection.Forward, 1 => LyraCardinalDirection.Backward,
                2 => LyraCardinalDirection.Left, 3 => LyraCardinalDirection.Right,
                _ => throw new InvalidOperationException("Invalid native cardinal direction.") };
            // StateResult OnUpdate precedes its child traversal, after the full
            // Main update cleared Mode. Native IsStateBlendingOut reads last weight.
            var mode = active && !(context.PreviousStopWeight > 0 && context.CurrentState != 3) ? 2 : main.Tail.Mode;
            var stop = _stop.Prepare(new(state.Crouching, state.Ads, direction, state.HasVelocity,
                state.HasAcceleration, movement), delta, weight, hipFireWeight, active, initialize);
            return _pending = new(main, stop, context, mode,
                active && _pendingBlendIn >= 0 ? [_pendingBlendIn] : []);
        }
        catch { Cancel(); throw; }
    }
    public void Evaluate(LyraMainStopCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    { Validate(candidate); _stop.Evaluate(candidate.Stop, outputs); }
    public void ValidateCommit(LyraMainStopCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {
        Validate(candidate); _main.ValidateCommit(candidate.Main, candidate.GraphRootYawMode);
        _stop.ValidateCommit(candidate.Stop, outputs);
    }
    public void Commit(LyraMainStopCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {
        ValidateCommit(candidate, outputs);
        _stop.Commit(candidate.Stop, outputs); _main.Commit(candidate.Main, candidate.GraphRootYawMode);
        if (candidate.Stop.Sources.Stop.Active) _pendingBlendIn = -1;
        _pending = null;
    }
    public void Cancel() { _stop.Cancel(); _main.Cancel(); _pending = null; }
    private void Validate(LyraMainStopCandidate candidate)
    { if (!ReferenceEquals(candidate, _pending)) throw new InvalidOperationException("Stale Main Stop candidate."); }
}
