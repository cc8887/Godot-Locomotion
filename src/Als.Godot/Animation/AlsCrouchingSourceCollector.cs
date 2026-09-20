using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import.Compilation;
using NVector2 = System.Numerics.Vector2;

namespace GodotAls.Animation;

// Candidate contributions only. The enclosing BaseLayer owner advances the shared Sync batch once.
internal sealed class AlsCrouchingSourceCollector
{
    private readonly AlsLocomotionSourcePlayer[] _players;
    private readonly AlsLocomotionSourceSample[] _samples;
    private readonly AlsCrouchingStateDefinition _states;
    private readonly AlsLeanSamplingProfile _lean;
    private readonly bool[] _known, _updated;
    private readonly float[] _leanWeights = new float[5];
    private readonly int[] _leanOrder = new int[5];
    private float _crouchingRate;
    private AlsSourceRotationInput _rotation;
    private int _leanCount;
    private bool _begun;
    private AlsFrameIdentity _identity;
    public AlsCycleSyncFrame Frame;
    public readonly AlsLocomotionSourceUpdate[] Updates = new AlsLocomotionSourceUpdate[9];
    public readonly AlsLocomotionSampleUpdate[] Samples = new AlsLocomotionSampleUpdate[13];
    public readonly AlsPoseUpdateContext[] Contexts = new AlsPoseUpdateContext[9];
    public int PlayerCount { get; private set; }
    public int SampleCount { get; private set; }
    public int InitializationCount { get; private set; }

    public AlsCrouchingSourceCollector(AlsLocomotionSourceProfile sources, AlsCrouchingStateDefinition states, AlsLeanSamplingProfile lean)
    {
        _players = sources.Players; _samples = sources.Samples; _states = states; _lean = lean;
        _known = new bool[_players.Length]; _updated = new bool[_players.Length];
        var count = 0;
        foreach (var player in _players)
            if (player.Domain == AlsLocomotionSourceDomain.Crouching) { _known[player.PlayerId] = true; count++; }
        if (count != 13 || _players.Length > AlsCycleSyncFrame.PlayerCapacity || _samples.Length > AlsCycleSyncFrame.SampleCapacity ||
            (uint)lean.PlayerId >= _known.Length || !_known[lean.PlayerId] || _players[lean.PlayerId].Kind != AlsLocomotionSourceKind.BlendSpace ||
            _players[lean.PlayerId].SampleStart != lean.SampleStart || _players[lean.PlayerId].SampleCount != 5)
            throw new ArgumentException("Crouching source collector requires the formal source layout.");
    }

    public void Begin(AlsFrameIdentity identity, in AlsCycleSyncFrame committed, float crouchingRate, in AlsSourceRotationInput rotation, NVector2 lean)
    {
        _begun = false;
        if (identity.SlotGeneration == 0 || !float.IsFinite(crouchingRate) || !float.IsFinite(rotation.Rate) || !float.IsFinite(lean.LengthSquared()))
            throw new ArgumentException("Invalid Crouching source inputs.");
        Frame = committed; Frame.NotifyTickCount = 0; PlayerCount = SampleCount = InitializationCount = 0;
        _crouchingRate = crouchingRate; _rotation = rotation;
        _leanCount = _lean.Runtime.Evaluate(lean, _leanWeights, _leanOrder);
        Array.Clear(_updated); _identity = identity; _begun = true;
    }

    public void ClearSourceWeights(byte states)
    {
        if (!_begun || (states & ~31) != 0) throw new ArgumentException("Invalid Crouching source-weight reset.");
        if ((states & 1) != 0) Frame.CachedWeights[_states.IdlePlayerId] = 0;
        if ((states & 4) != 0) Frame.CachedWeights[_states.RotateLeftPlayerId] = 0;
        if ((states & 8) != 0) Frame.CachedWeights[_states.RotateRightPlayerId] = 0;
        if ((states & 16) != 0)
        { Frame.CachedWeights[_states.StopLeftPlayerId] = 0; Frame.CachedWeights[_states.StopRightPlayerId] = 0; }
        // Moving owns only a cache read. Its external Cycles inputs are not baked players of this state.
    }

    public void InitializeSource(int playerId)
    {
        RequireSource(playerId);
        var player = _players[playerId]; var sample = _samples[player.SampleStart];
        var rate = player.PlayRateInput switch
        {
            "CrouchingPlayRate" => _crouchingRate,
            "RotateRate" => _rotation.Rate,
            _ => player.DefaultPlayRate,
        };
        var time = player.Kind == AlsLocomotionSourceKind.TeleportEvaluator ? player.StartPosition :
            AlsAssetSourceInitialization.Time(player.Kind == AlsLocomotionSourceKind.BlendSpace ? AlsAssetSyncKind.BlendSpace : AlsAssetSyncKind.Sequence,
                player.StartPosition, sample.DurationSeconds, rate, player.PlayRateBasis, sample.AssetRateScale);
        Frame.Epochs[playerId] = AlsAssetSourceInitialization.NextEpoch(Frame.Epochs[playerId]);
        Frame.CachedWeights[playerId] = 0; Frame.Times[playerId] = time; InitializationCount++;
    }

    public void UpdateSource(int playerId, in AlsPoseUpdateContext context)
    {
        RequireSource(playerId);
        if (_updated[playerId] || context.Identity != _identity || context.Weight > 1 || Frame.Epochs[playerId] <= 0)
            throw new InvalidOperationException("Duplicate, uninitialized or invalid Crouching source update.");
        var player = _players[playerId]; var blendSpace = player.Kind == AlsLocomotionSourceKind.BlendSpace;
        var count = blendSpace ? _leanCount : 1;
        if (player.Kind != AlsLocomotionSourceKind.TeleportEvaluator &&
            (PlayerCount == Updates.Length || SampleCount + count > Samples.Length))
            throw new InvalidOperationException("Crouching source contribution capacity exceeded.");
        _updated[playerId] = true; Frame.CachedWeights[playerId] = context.Weight;
        if (player.Kind == AlsLocomotionSourceKind.TeleportEvaluator) return;
        Updates[PlayerCount] = new(playerId, Frame.Epochs[playerId], Frame.Times[playerId], context.Weight,
            SampleCount, count, context.InertializationSync);
        Contexts[PlayerCount++] = context;
        for (var i = 0; i < count; i++)
        {
            var sourceIndex = blendSpace ? _leanOrder[i] : 0;
            Samples[SampleCount++] = new(player.SampleStart + sourceIndex, blendSpace ? _leanWeights[sourceIndex] : 1, 1);
        }
    }
    private void RequireSource(int playerId)
    {
        if (!_begun || (uint)playerId >= _known.Length || !_known[playerId])
            throw new ArgumentException("Source is not owned by the Crouching collector.");
    }
}
