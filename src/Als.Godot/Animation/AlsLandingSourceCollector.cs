using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

internal interface IAlsLandingGroundedUpdateSink
{
    void InitializeGrounded(int readNode, AlsFrameIdentity identity);
    void UpdateGrounded(int readNode, in AlsPoseUpdateContext context);
}

internal sealed class AlsLandingSourceCollector
{
    private readonly AlsLandingPoseProfile _profile;
    private readonly AlsLocomotionSourcePlayer[] _players;
    private readonly AlsLocomotionSourceSample[] _samples;
    private readonly AlsLocomotionSourceStamp _stamp;
    private readonly bool[] _updated = new bool[4];
    private readonly int[] _ids;
    private AlsFrameIdentity _identity;
    private bool _begun;
    public AlsCycleSyncFrame Frame;
    public AlsLandingBlendInputs Inputs;
    public readonly AlsLocomotionSourceUpdate[] Updates = new AlsLocomotionSourceUpdate[4];
    public readonly AlsLocomotionSampleUpdate[] Samples = new AlsLocomotionSampleUpdate[4];
    public readonly AlsPoseUpdateContext[] Contexts = new AlsPoseUpdateContext[4];
    public int Count { get; private set; }

    public AlsLandingSourceCollector(AlsLocomotionSourceProfile sources, AlsLandingPoseProfile profile)
    {
        _profile = profile; _players = sources.Players; _samples = sources.Samples; _stamp = sources.RuntimeStamp;
        _ids = [profile.Light, profile.Heavy, profile.MovingLight, profile.MovingHeavy];
        if (_players.Length > AlsCycleSyncFrame.PlayerCapacity || _samples.Length > AlsCycleSyncFrame.SampleCapacity ||
            _ids.Distinct().Count() != 4 || _ids.Any(id => (uint)id >= _players.Length || _players[id].Kind != AlsLocomotionSourceKind.Sequence ||
                _players[id].Domain != AlsLocomotionSourceDomain.MainMovement)) throw new ArgumentException("Invalid landing source layout.");
    }
    public void Begin(AlsFrameIdentity identity, in AlsCycleSyncFrame committed, in AlsLandingBlendInputs inputs)
    {
        _begun = false;
        if (identity.SlotGeneration == 0 || committed.Initialized && committed.BindingStamp != _stamp)
            throw new ArgumentException("Invalid landing source owner.");
        _ = inputs.PoseAlpha(3); _ = inputs.PoseAlpha(6);
        Frame = committed; Frame.NotifyTickCount = 0; Inputs = inputs; Count = 0; _identity = identity;
        Array.Clear(_updated); _begun = true;
    }
    public void ClearWeights(int state)
    { var slot = Slot(state); Frame.CachedWeights[_ids[slot]] = Frame.CachedWeights[_ids[slot + 1]] = 0; }
    public void Initialize(int state, IAlsLandingGroundedUpdateSink? grounded = null)
    {
        var slot = Slot(state);
        if (state == 6) (grounded ?? throw new ArgumentNullException(nameof(grounded))).InitializeGrounded(_profile.GroundedReadNodeIndex, _identity);
        for (var i = slot; i < slot + 2; i++)
        {
            var id = _ids[i]; var player = _players[id]; var sample = _samples[player.SampleStart];
            Frame.Times[id] = AlsAssetSourceInitialization.Time(AlsAssetSyncKind.Sequence, player.StartPosition, sample.DurationSeconds,
                player.DefaultPlayRate, player.PlayRateBasis, sample.AssetRateScale);
            Frame.Epochs[id] = AlsAssetSourceInitialization.NextEpoch(Frame.Epochs[id]); Frame.CachedWeights[id] = 0;
        }
    }
    public void Update(int state, float fallSpeedMetres, in AlsPoseUpdateContext context, IAlsLandingGroundedUpdateSink? grounded = null)
    {
        var slot = Slot(state);
        if (context.Identity != _identity) throw new ArgumentException("Foreign landing source context.");
        // ApplyMeshSpaceAdditive updates its base before exposed inputs/additive children.
        if (state == 6) (grounded ?? throw new ArgumentNullException(nameof(grounded))).UpdateGrounded(_profile.GroundedReadNodeIndex, context);
        Inputs = Inputs.Update(state, fallSpeedMetres); var alpha = Inputs.PoseAlpha(state);
        if (alpha < 1) Source(slot, context.WithWeight(context.Weight * (1 - alpha)));
        if (alpha > 0) Source(slot + 1, context.WithWeight(context.Weight * alpha));
    }
    public float LandRemaining()
    {
        Slot(3);
        // Native relevant-time order is Heavy then Light, unlike Initialize/Update's A then B.
        var id = Frame.CachedWeights[_profile.Heavy] >= Frame.CachedWeights[_profile.Light] ? _profile.Heavy : _profile.Light;
        return Frame.CachedWeights[id] > 0 && Frame.Epochs[id] > 0 ? _samples[_players[id].SampleStart].DurationSeconds - Frame.Times[id] : float.MaxValue;
    }
    public AlsGroundedAutomaticTime MovingTime()
    {
        Slot(6);
        var id = Frame.CachedWeights[_profile.MovingLight] >= Frame.CachedWeights[_profile.MovingHeavy] ? _profile.MovingLight : _profile.MovingHeavy;
        if (Frame.CachedWeights[id] <= 0 || Frame.Epochs[id] <= 0) return default;
        for (var i = 0; i < Frame.PlayerCount; i++)
        {
            var history = Frame.Players[i]; if (history.PlayerId != id || history.Epoch != Frame.Epochs[id]) continue;
            return new(true, _samples[_players[id].SampleStart].DurationSeconds, Frame.Times[id], false, true, history.DeltaPrevious, history.Delta);
        }
        // An inactive player retains cached weight/time in UE even after it leaves the active tick batch.
        // These landing sequences never loop, so the automatic rule needs no previous delta to use that time.
        return new(true, _samples[_players[id].SampleStart].DurationSeconds, Frame.Times[id], false, false, 0, 0);
    }
    private void Source(int slot, in AlsPoseUpdateContext context)
    {
        var id = _ids[slot]; var player = _players[id];
        if (_updated[slot] || Frame.Epochs[id] <= 0 || context.Weight > 1) throw new InvalidOperationException("Duplicate or uninitialized landing source.");
        _updated[slot] = true; Frame.CachedWeights[id] = context.Weight;
        Updates[Count] = new(id, Frame.Epochs[id], Frame.Times[id], context.Weight, Count, 1, context.InertializationSync);
        Samples[Count] = new(player.SampleStart, 1, 1); Contexts[Count++] = context;
    }
    private int Slot(int state)
    {
        if (!_begun || state is not (3 or 6)) throw new ArgumentException("Not a landing source state.");
        return state == 3 ? 0 : 2;
    }
}
