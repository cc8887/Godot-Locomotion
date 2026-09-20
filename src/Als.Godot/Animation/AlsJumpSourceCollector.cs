using System.Runtime.CompilerServices;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

[InlineArray(6)] internal struct AlsJumpPlayerRates { private float _element; }

// Candidate source state only: the enclosing transaction owns sync, events and final commit.
internal sealed class AlsJumpSourceCollector : IAlsJumpStateUpdateSink
{
    private readonly AlsLocomotionSourcePlayer[] _players;
    private readonly AlsLocomotionSourceSample[] _samples;
    private readonly int[] _ids;
    private readonly AlsLocomotionSourceStamp _stamp;
    private readonly bool[] _updated = new bool[6];
    private AlsFrameIdentity _identity;
    private float _rate;
    private bool _begun;
    public AlsCycleSyncFrame Frame;
    public AlsJumpPlayerRates Rates;
    public readonly AlsLocomotionSourceUpdate[] Updates = new AlsLocomotionSourceUpdate[6];
    public readonly AlsLocomotionSampleUpdate[] Samples = new AlsLocomotionSampleUpdate[6];
    public readonly AlsPoseUpdateContext[] Contexts = new AlsPoseUpdateContext[6];
    public int Count { get; private set; }
    public int InitializationCount { get; private set; }
    public float InertializationSeconds { get; private set; }
    public AlsPoseUpdateContext InertializationContext { get; private set; }

    public AlsJumpSourceCollector(AlsLocomotionSourceProfile sources, AlsJumpStateDefinition definition)
    {
        _players = sources.Players; _samples = sources.Samples; _stamp = sources.RuntimeStamp;
        _ids = [definition.WalkLeft, definition.RunLeft, definition.WalkRight, definition.RunRight, definition.Loop, definition.Flail];
        if (_players.Length > AlsCycleSyncFrame.PlayerCapacity || _samples.Length > AlsCycleSyncFrame.SampleCapacity ||
            _ids.Distinct().Count() != 6 || _ids.Any(id => (uint)id >= _players.Length ||
                _players[id].Domain != AlsLocomotionSourceDomain.Jump || _players[id].Kind != AlsLocomotionSourceKind.Sequence ||
                _players[id].SampleCount != 1)) throw new ArgumentException("Invalid Jump source ownership.");
    }

    public void Begin(AlsFrameIdentity identity, in AlsCycleSyncFrame committed, in AlsJumpPlayerRates rates, float rate)
    {
        _begun = false;
        if (identity.SlotGeneration == 0 || !float.IsFinite(rate) || committed.Initialized && committed.BindingStamp != _stamp)
            throw new ArgumentException("Invalid Jump source inputs or binding history.");
        for (var i = 0; i < 6; i++)
            if (committed.Epochs[_ids[i]] > 0 && !float.IsFinite(rates[i])) throw new ArgumentException("Invalid cached Jump rate.");
        Frame = committed; Frame.NotifyTickCount = 0; Rates = rates; _identity = identity; _rate = rate;
        Count = InitializationCount = 0; InertializationSeconds = -1; InertializationContext = default;
        Array.Clear(_updated); _begun = true;
    }

    public float ObserveRemaining(int state)
    {
        if (!_begun || state is not (1 or 2)) throw new ArgumentException("Invalid Jump relevant-time state.");
        var best = 0f; var remaining = float.MaxValue;
        // Native baked order and strict greater-than preserve the first player on equal weights.
        for (var slot = (state - 1) * 2; slot < state * 2; slot++)
        {
            var id = _ids[slot]; var weight = Frame.CachedWeights[id];
            if (Frame.Epochs[id] <= 0 || weight <= best) continue;
            best = weight; var player = _players[id]; var sample = _samples[player.SampleStart];
            var effectiveRate = (MathF.Abs(player.PlayRateBasis) <= 1e-8f ? 0 : Rates[slot] / player.PlayRateBasis) * sample.AssetRateScale;
            var adjustedTime = effectiveRate < 0 ? sample.DurationSeconds - Frame.Times[id] : Frame.Times[id];
            remaining = sample.DurationSeconds - adjustedTime;
        }
        return remaining;
    }

    public void ClearSourceWeights(byte states)
    {
        if (!_begun || (states & ~31) != 0) throw new ArgumentException("Invalid Jump weight reset.");
        for (var slot = 0; slot < 6; slot++)
        {
            var state = slot < 4 ? slot / 2 + 1 : slot - 1;
            if ((states & (1 << state)) != 0) Frame.CachedWeights[_ids[slot]] = 0;
        }
    }

    public void InitializeSource(int playerId)
    {
        var slot = Slot(playerId); var player = _players[playerId]; var sample = _samples[player.SampleStart];
        var rate = Rate(player);
        Frame.Times[playerId] = AlsAssetSourceInitialization.Time(AlsAssetSyncKind.Sequence, player.StartPosition,
            sample.DurationSeconds, rate, player.PlayRateBasis, sample.AssetRateScale);
        Frame.Epochs[playerId] = AlsAssetSourceInitialization.NextEpoch(Frame.Epochs[playerId]);
        Frame.CachedWeights[playerId] = 0; Rates[slot] = rate; InitializationCount++;
    }

    public void UpdateSource(int playerId, in AlsPoseUpdateContext context)
    {
        var slot = Slot(playerId);
        if (_updated[slot] || context.Identity != _identity || context.Weight > 1 || Frame.Epochs[playerId] <= 0)
            throw new InvalidOperationException("Duplicate, uninitialized or foreign Jump update.");
        var player = _players[playerId]; _updated[slot] = true;
        Rates[slot] = Rate(player); Frame.CachedWeights[playerId] = context.Weight;
        Updates[Count] = new(playerId, Frame.Epochs[playerId], Frame.Times[playerId], context.Weight, Count, 1, context.InertializationSync);
        Samples[Count] = new(player.SampleStart, 1, 1); Contexts[Count++] = context;
    }

    public void RequestInertialization(in AlsPoseUpdateContext context, float seconds)
    {
        if (!_begun || context.Identity != _identity || !float.IsFinite(seconds) || seconds < 0)
            throw new ArgumentException("Invalid Jump inertialization request.");
        InertializationSeconds = seconds; InertializationContext = context;
    }
    private float Rate(AlsLocomotionSourcePlayer player) => player.PlayRateInput == "JumpPlayRate" ? _rate : player.DefaultPlayRate;
    private int Slot(int id)
    {
        var slot = Array.IndexOf(_ids, id);
        if (!_begun || slot < 0) throw new ArgumentException("Source is not owned by Jump.");
        return slot;
    }
}
