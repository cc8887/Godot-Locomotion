using GodotAls.Core.Curves;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraStartInput(bool Crouching, bool Ads,
    LyraCardinalDirection Direction, double Displacement);
internal sealed record LyraStartAsset(int Id, AlsAssetSyncSequence Sequence, float DistanceRange,
    Func<float, float> SampleDistance, ulong MarkerMask);
internal readonly record struct LyraStartPolicy(double Offset, double Duration, double ClampMin, double ClampMax);
internal readonly record struct LyraStartState(int AssetId, float Time, float ExplicitTime, double StrideAlpha,
    float CachedWeight, AlsAssetMarkerRecord Marker, float DeltaPrevious, float Delta,
    long Frame, long LastVisited, float LastWeight, bool Initialized, bool ResetPending);
internal sealed record LyraStartCandidate(long Attempt, LyraStartState State,
    LyraEvaluatorSourceCandidate Tick, bool Active, bool BecameRelevant, int BeforeAsset, float Before, float ExplicitBefore,
    float Length);

// An actual source occurrence owned by a linked provider. Relevance follows
// physical visits, not asset identity or Initialize. Commit/Cancel includes
// hidden frames so a rejected character frame cannot reset this source.
internal sealed class LyraStartSourceRuntime
{
    private readonly LyraSourceNode _node;
    private readonly int _playerId;
    private readonly long _epoch;
    private readonly Func<string, LyraCardinalDirection, LyraStartAsset> _resolve;
    private readonly Func<int, LyraStartAsset> _byId;
    private readonly LyraStartPolicy _policy;
    private LyraStartCandidate? _pending;
    private long _attempt;
    public LyraStartState State { get; private set; } = new(-1, 0, 0, 0, 0,
        AlsAssetMarkerRecord.Invalid, 0, 0, 0, -1, 0, false, false);

    public LyraStartSourceRuntime(LyraSourceNode node, int playerId, long epoch,
        Func<string, LyraCardinalDirection, LyraStartAsset> resolve, Func<int, LyraStartAsset> byId, LyraStartPolicy policy)
    {
        if (node.Kind != LyraSourceKind.SequenceEvaluator || node.Functions.BecomeRelevant != "SetUpStartAnim" ||
            node.Functions.Update != "UpdateStartAnim" || node.Method != LyraSourceSyncMethod.SyncGroup ||
            node.Group != "Locomotion" || node.Role != LyraSourceGroupRole.CanBeLeader || node.Looping ||
            node.Settings.GetProperty("reinitialization").GetInt32() != 2 ||
            node.Settings.GetProperty("explicitTime").GetSingle() != 0 ||
            node.Settings.GetProperty("startPosition").GetSingle() != 0 || playerId < 0 || epoch <= 0 ||
            !double.IsFinite(policy.Offset) || !double.IsFinite(policy.Duration) ||
            !double.IsFinite(policy.ClampMin) || !double.IsFinite(policy.ClampMax))
            throw new ArgumentException("Invalid original Lyra Start source binding.");
        _node = node; _playerId = playerId; _epoch = epoch; _resolve = resolve; _byId = byId; _policy = policy;
    }

    internal void InitializeSource()
    {
        if (_pending is not null) throw new InvalidOperationException("Start initialization needs an idle source.");
        State = State with { Initialized = true, ResetPending = true,
            Marker = State.Marker with { PreviousIndex = -2, NextIndex = -2, Initialized = false } };
    }

    public LyraStartCandidate Prepare(in LyraStartInput input, float delta, float weight,
        int sampleStart, bool reinitialize, bool active = true, bool scopeRequestedInertialization = false)
    {
        if (_pending is not null) throw new InvalidOperationException("Start candidate is still pending.");
        if (!float.IsFinite(delta) || delta < 0 || !float.IsFinite(weight) || weight < 0 ||
            !double.IsFinite(input.Displacement) || !float.IsFinite((float)input.Displacement) ||
            !Enum.IsDefined(input.Direction) || sampleStart < 0)
            throw new ArgumentException("Invalid Start update context.");
        var state = State with { Frame = checked(State.Frame + 1) };
        if (!state.Initialized || reinitialize)
            state = state with { Initialized = true, ResetPending = true,
                Marker = state.Marker with { PreviousIndex = -2, NextIndex = -2, Initialized = false } };
        if (!active)
            return _pending = new(++_attempt, state, default, false, false, State.AssetId, State.Time, State.ExplicitTime, 0);
        var previousWeight = State.LastVisited == State.Frame ? State.LastWeight : 0;
        var relevant = previousWeight <= 1e-5f && weight > 1e-5f;
        if (relevant)
        {
            var group = input.Crouching ? "Crouch_Start_Cardinals" : input.Ads ? "ADS_Start_Cardinals" : "Jog_Start_Cardinals";
            var selected = _resolve(group, input.Direction);
            state = state with { AssetId = selected.Id, ExplicitTime = 0, StrideAlpha = 0 };
        }
        if (state.AssetId < 0) throw new InvalidOperationException("Start update has no sequence after original setup.");
        var asset = _byId(state.AssetId);
        if (asset.Id != state.AssetId || asset.Id != asset.Sequence.AnimationId || !float.IsFinite(asset.DistanceRange) || asset.SampleDistance is null)
            throw new InvalidOperationException("Invalid Start distance resource binding.");
        // SequenceEvaluator overrides GetAccumulatedTime to return ExplicitTime.
        // The original Blueprint therefore uses the callback's explicit clock.
        var position = (double)state.ExplicitTime - _policy.Offset;
        var alpha = Math.Abs(_policy.Duration) <= 1e-8 ? (position >= _policy.Duration ? 1d : 0d) :
            Math.Clamp(position / _policy.Duration, 0, 1);
        var lower = _policy.Duration + alpha * (_policy.ClampMin - _policy.Duration);
        var explicitTime = AlsDistanceMatching.AdvanceNonLooping(state.ExplicitTime, (float)input.Displacement,
            delta, asset.Sequence.DurationSeconds, asset.DistanceRange, asset.SampleDistance, lower, _policy.ClampMax);
        var tick = LyraEvaluatorSourceTick.Prepare(_node, _playerId, asset.Id, _epoch, state.Time,
            explicitTime, asset.Sequence, delta, weight, sampleStart, asset.MarkerMask, state.ResetPending,
            scopeRequestedInertialization, markerRecord: state.Marker);
        state = state with { Time = tick.Preparation.Time, ExplicitTime = explicitTime, StrideAlpha = alpha,
            CachedWeight = weight, LastWeight = weight, LastVisited = state.Frame, ResetPending = false };
        return _pending = new(++_attempt, state, tick, true, relevant, State.AssetId, State.Time, State.ExplicitTime,
            asset.Sequence.DurationSeconds);
    }

    internal void ValidateCommit(LyraStartCandidate candidate, in AlsAssetPlayerHistory output)
    {
        if (!ReferenceEquals(candidate, _pending) || !candidate.Active || output.PlayerId != _playerId ||
            output.AssetId != candidate.State.AssetId || output.Epoch != _epoch ||
            output.SampleStart != candidate.Tick.Player.SampleStart || output.SampleCount != 1 ||
            !float.IsFinite(output.Time) || output.Time < 0 || output.Time > candidate.Length ||
            !float.IsFinite(output.DeltaPrevious) || !float.IsFinite(output.Delta) ||
            !float.IsFinite(output.Marker.PreviousDistance) || !float.IsFinite(output.Marker.NextDistance) ||
            output.Marker.Initialized && (output.Marker.PreviousIndex < -1 || output.Marker.NextIndex < -1 ||
                output.Marker.PreviousIndex >= _byId(candidate.State.AssetId).Sequence.MarkerCount ||
                output.Marker.NextIndex >= _byId(candidate.State.AssetId).Sequence.MarkerCount))
            throw new InvalidOperationException("Rejected stale or foreign Start Sync result.");
    }
    public void Commit(LyraStartCandidate candidate, in AlsAssetPlayerHistory output)
    {
        ValidateCommit(candidate, output);
        State = candidate.State with { Time = output.Time, Marker = output.Marker,
            DeltaPrevious = output.DeltaPrevious, Delta = output.Delta };
        _pending = null;
    }
    public void CommitInactive(LyraStartCandidate candidate)
    {
        ValidateInactiveCommit(candidate);
        State = candidate.State; _pending = null;
    }
    internal void ValidateInactiveCommit(LyraStartCandidate candidate)
    {
        if (!ReferenceEquals(candidate, _pending) || candidate.Active)
            throw new InvalidOperationException("Rejected stale or active Start inactive candidate.");
    }
    public void Cancel() => _pending = null;
}
