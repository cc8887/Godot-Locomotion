using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredForwardInput(string Gait, float SprintBlock, float SprintAcceleration);
public readonly record struct AlsRefactoredForwardCacheRead(int ReadPropertyIndex, float Weight, bool Inactive);
public readonly record struct AlsRefactoredForwardWeights(float Block, Vector4 Gait, float Acceleration,
    bool GaitUpdated, bool AccelerationUpdated);

/// <summary>Transactional update of the standing forward cache source. The
/// enclosing cache scheduler must call this once per selected cache context,
/// enqueue BOTH base reads (never sum them), then batch sprint players with all
/// other sources. This owner neither advances player clocks nor consumes poses.</summary>
public sealed class AlsRefactoredForwardSourceRuntime
{
    private struct State
    {
        public AlsOverlayBlendListState Gait;
        public bool BlockInitialized, AccelerationInitialized, SprintRelevant, AccelerationRelevant;
        public bool ResetSprint, ResetAcceleration;
        public float BlockHistory, AccelerationHistory;
    }
    private readonly AlsRefactoredForwardSource _profile;
    private readonly int _first;
    private State _state, _next;
    private readonly AlsRefactoredSourcePlayerInput[] _players = new AlsRefactoredSourcePlayerInput[2];
    private readonly bool[] _inactive = new bool[2];
    private readonly AlsRefactoredForwardCacheRead[] _reads = new AlsRefactoredForwardCacheRead[2];
    private int _playerCount, _readCount;
    private bool _prepared;
    private long _frame, _committed = -1;
    private AlsRefactoredForwardWeights _weights;
    public AlsRefactoredForwardSource Profile => _profile;
    public ReadOnlySpan<AlsRefactoredSourcePlayerInput> SourceInputs => _prepared ? _players.AsSpan(0, _playerCount) : throw new InvalidOperationException("No forward candidate.");
    public ReadOnlySpan<bool> SourceInactive => _prepared ? _inactive.AsSpan(0, _playerCount) : throw new InvalidOperationException("No forward candidate.");
    public ReadOnlySpan<AlsRefactoredForwardCacheRead> BaseReads => _prepared ? _reads.AsSpan(0, _readCount) : throw new InvalidOperationException("No forward candidate.");
    public AlsRefactoredForwardWeights Weights => _prepared ? _weights : throw new InvalidOperationException("No forward candidate.");
    public AlsRefactoredForwardSourceRuntime(AlsRefactoredForwardSource profile, int firstPlayer)
    {
        if (firstPlayer < 0 || firstPlayer > int.MaxValue - profile.Players.Players.Length) throw new ArgumentOutOfRangeException(nameof(firstPlayer));
        _profile = profile; _first = firstPlayer;
    }

    public void Prepare(long frame, AlsRefactoredForwardInput input, AlsRefactoredMovementPlayerInput movement,
        float delta, float weight = 1, bool reinitialize = false, bool inactive = false)
    {
        if (_prepared || frame < 0 || frame <= _committed || input.Gait is null || !float.IsFinite(input.SprintBlock) ||
            !float.IsFinite(input.SprintAcceleration) || !float.IsFinite(delta) || delta < 0 || !float.IsFinite(weight) || weight is < 0 or > 1)
            throw new ArgumentException("Invalid forward update.");
        // Validate source bindings even while sprint is hidden.
        _ = _profile.Players.Input(_first, _profile.SprintPlayer, movement, weight);
        _next = reinitialize || _committed < 0 ? new State { ResetSprint = true, ResetAcceleration = true } : _state;
        _playerCount = _readCount = 0;
        var block = AlsOverlayPoseWeights.Alpha(input.SprintBlock, _profile.BlockPolicy, delta, ref _next.BlockInitialized, ref _next.BlockHistory);
        var a = block < 1 - AlsPoseBlender.WeightThreshold; var b = block > AlsPoseBlender.WeightThreshold;
        var acceleration = 0f; var accelerationUpdated = false; var gaitWeights = Vector4.Zero;
        if (a)
        {
            var gaitWeight = weight * (b ? 1 - block : 1);
            var list = AlsOverlayPoseWeights.BlendList(_next.Gait, input.Gait == "Als.Gait.Sprinting" ? 1 : 0,
                _profile.GaitBlendTimes, AlsTransitionBlend.Cubic, false, false, delta);
            // Preserve the explicit zero-weight old-child update and original
            // child order. Cache selection/active-state messages remain external.
            if (list.ZeroWeightPreviousChild >= 0) Child(list.ZeroWeightPreviousChild, 0, inactive);
            _next.Gait = list.State; gaitWeights = list.State.Weights;
            for (var i = 0; i < 2; i++) if (gaitWeights[i] > AlsPoseBlender.WeightThreshold)
                Child(i, gaitWeight * gaitWeights[i], inactive || i != list.State.ActiveChild);
        }
        if (b) _reads[_readCount++] = new(_profile.BlockBaseRead, weight * (a ? block : 1), inactive);
        _weights = new(block, gaitWeights, acceleration, a, accelerationUpdated); _frame = frame; _prepared = true;

        void Child(int child, float sourceWeight, bool sourceInactive)
        {
            if (child == 0) { _reads[_readCount++] = new(_profile.GaitBaseRead, sourceWeight, sourceInactive); return; }
            acceleration = AlsOverlayPoseWeights.Alpha(input.SprintAcceleration, _profile.AccelerationPolicy, delta,
                ref _next.AccelerationInitialized, ref _next.AccelerationHistory);
            accelerationUpdated = true;
            var sprint = acceleration < 1 - AlsPoseBlender.WeightThreshold; var accel = acceleration > AlsPoseBlender.WeightThreshold;
            if (sprint && !_next.SprintRelevant) _next.ResetSprint = true;
            if (accel && !_next.AccelerationRelevant) _next.ResetAcceleration = true;
            _next.SprintRelevant = sprint; _next.AccelerationRelevant = accel;
            if (sprint)
            {
                Player(_profile.SprintPlayer, sourceWeight * (accel ? 1 - acceleration : 1), _next.ResetSprint, sourceInactive);
                _next.ResetSprint = false;
            }
            if (accel)
            {
                Player(_profile.AccelerationPlayer, sourceWeight * (sprint ? acceleration : 1), _next.ResetAcceleration, sourceInactive);
                _next.ResetAcceleration = false;
            }
        }
        void Player(int local, float w, bool reset, bool sourceInactive)
        {
            _players[_playerCount] = _profile.Players.Input(_first, local, movement, w, reset);
            _inactive[_playerCount++] = sourceInactive;
        }
    }
    public void ValidateCommit(long frame)
    { if (!_prepared || frame != _frame) throw new ArgumentException("Invalid forward commit."); }
    public void Commit(long frame) { ValidateCommit(frame); _state = _next; _committed = frame; Cancel(); }
    public void Cancel() { _prepared = false; _playerCount = _readCount = 0; }
}
