using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraCycleInput(bool Crouching, bool Ads,
    LyraCardinalDirection Direction, float DisplacementSpeed, bool RunningIntoWall);
internal readonly record struct LyraCycleAsset(int Id, float Length, float RootDistance);
internal readonly record struct LyraCycleState(int AssetId, float Time, float PlayRate,
    double StrideAlpha, AlsAssetMarkerRecord Marker, float DeltaPrevious = 0, float Delta = 0);
internal sealed record LyraCycleCandidate(long Attempt, float Before, int BeforeAsset,
    LyraCycleState State, AlsAssetSyncPlayer Player, float Length, float InertiaDuration, bool Ticked = true);

// One compiled source occurrence in one linked instance. The caller submits
// Player to the character's common Sync and commits only its accepted result.
// This component does not traverse the enclosing Cycle/layer/main graphs.
internal sealed class LyraCycleSourceRuntime
{
    private readonly LyraSourceNode _node;
    private readonly int _playerId;
    private readonly long _epoch;
    private readonly Func<string, LyraCardinalDirection, LyraCycleAsset> _asset;
    private readonly double _clampMin, _clampMax;
    private long _attempt;
    private LyraCycleCandidate? _pending;
    public LyraCycleState State { get; private set; } = new(-1, 0, 1, 0, AlsAssetMarkerRecord.Invalid);

    public LyraCycleSourceRuntime(LyraSourceNode node, int playerId, long epoch,
        Func<string, LyraCardinalDirection, LyraCycleAsset> asset, double clampMin, double clampMax)
    {
        if (node.Kind != LyraSourceKind.SequencePlayer || node.Functions.Update != "UpdateCycleAnim" ||
            node.Method != LyraSourceSyncMethod.SyncGroup || node.Group != "Locomotion" ||
            node.Role != LyraSourceGroupRole.AlwaysFollower || !node.Looping || playerId < 0 || epoch <= 0 ||
            !double.IsFinite(clampMin) || !double.IsFinite(clampMax))
            throw new ArgumentException("Invalid compiled Lyra Cycle source binding.");
        var settings = node.Settings;
        var scale = settings.GetProperty("playRateScaleBiasClamp");
        if (settings.GetProperty("playRateBasis").GetSingle() != 1 ||
            settings.GetProperty("startPosition").GetSingle() != 0 ||
            settings.GetProperty("startFromMatchingPose").GetBoolean() ||
            scale.GetProperty("bMapRange").GetBoolean() || scale.GetProperty("bClampResult").GetBoolean() ||
            scale.GetProperty("bInterpResult").GetBoolean() || scale.GetProperty("scale").GetSingle() != 1 ||
            scale.GetProperty("bias").GetSingle() != 0)
            throw new NotSupportedException("Cycle source requires its original identity play-rate settings.");
        _node = node; _playerId = playerId; _epoch = epoch; _asset = asset;
        _clampMin = clampMin; _clampMax = clampMax;
    }

    internal void InitializeSource()
    {
        if (_pending is not null) throw new InvalidOperationException("Cycle initialization needs an idle source.");
        State = State with { Time = 0,
            Marker = State.Marker with { PreviousIndex = -2, NextIndex = -2, Initialized = false } };
    }

    public LyraCycleCandidate Prepare(in LyraCycleInput input, float delta, float weight,
        int sampleStart, ulong markerMask, bool reinitialize, bool scopeRequestedInertialization = false)
    {
        if (_pending is not null) throw new InvalidOperationException("Cycle candidate is still pending.");
        if (!float.IsFinite(delta) || delta < 0 || !float.IsFinite(weight) || weight < 0 ||
            !float.IsFinite(input.DisplacementSpeed) || !Enum.IsDefined(input.Direction) || sampleStart < 0)
            throw new ArgumentException("Invalid Cycle update context.");
        var group = input.Crouching ? "Crouch_Walk_Cardinals" : input.Ads ? "Walk_Cardinals" : "Jog_Cardinals";
        var asset = _asset(group, input.Direction);
        if (asset.Id < 0 || !float.IsFinite(asset.Length) || asset.Length <= 0 ||
            !float.IsFinite(asset.RootDistance) || asset.RootDistance < 0)
            throw new InvalidOperationException("Invalid Cycle sequence speed definition.");
        var before = reinitialize ? 0 : State.Time;
        var changed = asset.Id != State.AssetId;
        var rate = State.PlayRate;
        // AnimDistanceMatchingLibrary uses float length, distance, speed and
        // division, followed by a double FVector2D clamp and float assignment.
        if (MathF.Abs(asset.Length) > 1e-8f && MathF.Abs(asset.RootDistance) > 1e-8f)
        {
            var speed = asset.RootDistance / asset.Length;
            var desired = input.DisplacementSpeed / speed;
            rate = _clampMin >= 0 && _clampMin < _clampMax
                ? (float)Math.Clamp((double)desired, _clampMin, _clampMax) : desired;
        }
        var target = input.RunningIntoWall ? .5 : 1;
        var distance = target - State.StrideAlpha;
        var alpha = distance * distance < (double)1e-8f ? target :
            State.StrideAlpha + distance * Math.Clamp((double)delta * 10, 0, 1);
        var marker = State.Marker;
        if (changed || reinitialize)
            marker = marker with { PreviousIndex = -2, NextIndex = -2, Initialized = false };
        // SetSequenceWithInertialBlending preserves time. SequencePlayer's
        // subsequent Update clamps it against the newly selected clip length.
        var state = State with { AssetId = asset.Id, Time = Math.Clamp(before, 0, asset.Length), PlayRate = rate, StrideAlpha = alpha, Marker = marker };
        if (!float.IsFinite(rate) || !double.IsFinite(alpha))
            throw new InvalidOperationException("Nonfinite Cycle callback result.");
        var player = new AlsAssetSyncPlayer(_playerId, asset.Id, _epoch, AlsAssetSyncKind.Sequence,
            state.Time, rate, weight, sampleStart, 1, markerMask, Looping: _node.Looping,
            Role: AlsAssetSyncRole.AlwaysFollower, OverridePositionWhenJoining: _node.OverridePositionWhenJoining,
            RequestedInertialization: scopeRequestedInertialization, MarkerRecord: marker);
        return _pending = new(++_attempt, before, State.AssetId, state, player, asset.Length, changed ? .2f : 0);
    }

    public void Commit(LyraCycleCandidate candidate, in AlsAssetPlayerHistory synced)
    {
        ValidateCommit(candidate, synced);
        State = candidate.State with { Time = synced.Time, Marker = synced.Marker, DeltaPrevious = synced.DeltaPrevious, Delta = synced.Delta };
        _pending = null;
    }
    internal void ValidateCommit(LyraCycleCandidate candidate, in AlsAssetPlayerHistory synced)
    {
        if (!ReferenceEquals(candidate, _pending) || !candidate.Ticked || synced.PlayerId != _playerId || synced.Epoch != _epoch ||
            synced.AssetId != candidate.State.AssetId || synced.SampleStart != candidate.Player.SampleStart ||
            synced.SampleCount != 1 || !float.IsFinite(synced.Time) || synced.Time < 0 || synced.Time > candidate.Length ||
            !float.IsFinite(synced.DeltaPrevious) || !float.IsFinite(synced.Delta) ||
            !float.IsFinite(synced.Marker.PreviousDistance) || !float.IsFinite(synced.Marker.NextDistance))
            throw new InvalidOperationException("Rejected stale or foreign Cycle Sync result.");
    }
    internal LyraCycleCandidate PrepareInactive(bool reinitialize)
    {
        if (_pending is not null) throw new InvalidOperationException("Cycle candidate is still pending.");
        var state = reinitialize ? State with { Time = 0, Marker = State.Marker with
            { PreviousIndex = -2, NextIndex = -2, Initialized = false } } : State;
        return _pending = new(++_attempt, state.Time, State.AssetId, state, default, 0, 0, false);
    }
    internal void ValidateInactiveCommit(LyraCycleCandidate candidate)
    {
        if (!ReferenceEquals(candidate, _pending) || candidate.Ticked)
            throw new InvalidOperationException("Rejected stale or active Cycle candidate.");
    }
    internal void CommitInactive(LyraCycleCandidate candidate)
    {
        ValidateInactiveCommit(candidate); State = candidate.State;
        _pending = null;
    }
    public void Cancel() => _pending = null;
}
