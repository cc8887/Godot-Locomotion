using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import.Compilation;
using NVector2 = System.Numerics.Vector2;

namespace GodotAls.Animation;

internal readonly record struct AlsAirRuntimeFrame(AlsAirPoseInputs Fall, AlsAirPoseInputs Jump,
    AlsJumpStateFrame NestedJump, AlsJumpPlayerRates Rates, AlsFrameIdentity Identity = default);

// Collect once per candidate; the enclosing Main Movement transaction ticks and publishes the shared batch.
internal sealed class AlsAirSourceCollector : IAlsJumpStateUpdateSink
{
    private readonly AlsAirPoseProfile _profile;
    private readonly AlsLocomotionSourcePlayer[] _players;
    private readonly AlsLocomotionSourceSample[] _samples;
    private readonly AlsLocomotionSourceStamp _stamp;
    private readonly AlsJumpStateRuntime _runtime;
    private readonly AlsJumpSourceCollector _jump;
    private readonly int[][] _outerIds;
    private readonly bool[] _updated;
    private readonly bool[] _statesUpdated = new bool[2];
    private readonly float[] _weights = new float[5];
    private readonly int[] _order = new int[5];
    private AlsFrameIdentity _identity;
    private bool _begun;
    public AlsCycleSyncFrame Frame;
    public AlsAirRuntimeFrame State;
    public readonly AlsLocomotionSourceUpdate[] Updates = new AlsLocomotionSourceUpdate[11];
    public readonly AlsLocomotionSampleUpdate[] Samples = new AlsLocomotionSampleUpdate[19];
    public readonly AlsPoseUpdateContext[] Contexts = new AlsPoseUpdateContext[11];
    public readonly int[] InitializationOrder = new int[64];
    public int Count { get; private set; }
    public int SampleCount { get; private set; }
    public int InitializationCount { get; private set; }
    public bool JumpUpdated { get; private set; }
    public bool JumpReinitialized { get; private set; }
    public AlsGroundedMachineUpdate JumpUpdate { get; private set; }
    public float InertializationSeconds => _jump.InertializationSeconds;
    public AlsPoseUpdateContext InertializationContext => _jump.InertializationContext;

    public AlsAirSourceCollector(AlsLocomotionSourceProfile sources, AlsAirPoseProfile profile)
    {
        _profile = profile; _players = sources.Players; _samples = sources.Samples; _stamp = sources.RuntimeStamp;
        _runtime = new(profile.NestedJump.Runtime); _jump = new(sources, profile.NestedJump.Runtime);
        _outerIds = [[profile.Fall.Loop, profile.Fall.Fast, profile.Fall.Flail, profile.Fall.Lean.PlayerId, profile.Fall.Heavy, profile.Fall.Light],
            [profile.Jump.Lean.PlayerId, profile.Jump.Heavy, profile.Jump.Light]];
        if (sources.SkeletonId != profile.SkeletonId || _outerIds.SelectMany(ids => ids).Distinct().Count() != 9 ||
            _outerIds.SelectMany(ids => ids).Any(id => (uint)id >= _players.Length || _players[id].Domain != AlsLocomotionSourceDomain.MainMovement))
            throw new ArgumentException("Invalid air source ownership.");
        _updated = new bool[_players.Length];
    }

    public void Begin(AlsFrameIdentity identity, in AlsCycleSyncFrame committed, in AlsAirRuntimeFrame state, float jumpRate)
    {
        _begun = false;
        if (identity.SlotGeneration == 0 || committed.Initialized && committed.BindingStamp != _stamp ||
            state.Identity.SlotGeneration != 0 && (state.Identity.CharacterId != identity.CharacterId ||
                state.Identity.SlotGeneration != identity.SlotGeneration || state.Identity.FrameId > identity.FrameId))
            throw new ArgumentException("Invalid air source owner.");
        _jump.Begin(identity, committed, state.Rates, jumpRate);
        Frame = committed; Frame.NotifyTickCount = 0; State = state with { Identity = identity }; _identity = identity;
        Count = SampleCount = InitializationCount = 0; JumpUpdated = JumpReinitialized = false; JumpUpdate = default;
        Array.Clear(_updated); Array.Clear(_statesUpdated); _begun = true;
    }

    public void ClearWeights(int state)
    {
        CheckState(state);
        foreach (var id in _outerIds[state - 1]) Frame.CachedWeights[id] = 0;
        if (state == 2) ((IAlsJumpStateUpdateSink)this).ClearSourceWeights(31);
    }
    public void Initialize(int state)
    {
        CheckState(state);
        if (_statesUpdated[state - 1]) throw new InvalidOperationException("Cannot initialize an air state after collecting its update.");
        if (state == 2)
        {
            // Negative journal entry distinguishes the nested machine from asset-player identities.
            RecordInitialization(-_profile.NestedJump.Runtime.MachineNodeIndex);
            var initial = _runtime.Initialize(State.NestedJump, _identity, this);
            JumpUpdate = initial.Machine;
            State = State with { NestedJump = initial.State, Jump = State.Jump.Initialize(), Rates = _jump.Rates };
            JumpReinitialized = true;
        }
        else State = State with { Fall = State.Fall.Initialize() };
        foreach (var id in _outerIds[state - 1])
        {
            var player = _players[id]; var sample = _samples[player.SampleStart];
            Frame.Times[id] = player.Kind == AlsLocomotionSourceKind.TeleportEvaluator ? player.StartPosition :
                AlsAssetSourceInitialization.Time(player.Kind == AlsLocomotionSourceKind.BlendSpace ? AlsAssetSyncKind.BlendSpace : AlsAssetSyncKind.Sequence,
                    player.StartPosition, sample.DurationSeconds, player.DefaultPlayRate, player.PlayRateBasis, sample.AssetRateScale);
            Frame.Epochs[id] = AlsAssetSourceInitialization.NextEpoch(Frame.Epochs[id]); Frame.CachedWeights[id] = 0;
            RecordInitialization(id);
        }
    }

    public void Update(int state, float fallSpeed, float prediction, NVector2 lean, float speed,
        in AlsGroundedRuleInput rules, in AlsPoseUpdateContext context)
    {
        CheckState(state);
        if (context.Identity != _identity || _statesUpdated[state - 1] || context.Weight > 1 ||
            context.HasSharedContext && (context.StateCount == 0 ||
                context.GetState(context.StateCount - 1) != new AlsActiveAnimationState(_profile.NestedJump.ParentMachineNodeIndex, state)))
            throw new ArgumentException("Duplicate or foreign air state update.");
        var input = (state == 1 ? State.Fall : State.Jump).Update(state == 2, fallSpeed, prediction, lean, context.Delta);
        _statesUpdated[state - 1] = true;
        if (input.Prediction.PoseAlpha < 1)
        {
            var basis = context.WithWeight(context.Weight * (1 - input.Prediction.PoseAlpha));
            if (state == 1)
            {
                var flail = input.Flail.PoseAlpha;
                if (flail < 1)
                {
                    var fall = basis.WithWeight(basis.Weight * (1 - flail));
                    if (input.Fast.PoseAlpha < 1) Source(_profile.Fall.Loop, fall.WithWeight(fall.Weight * (1 - input.Fast.PoseAlpha)));
                    if (input.Fast.PoseAlpha > 0) Source(_profile.Fall.Fast, fall.WithWeight(fall.Weight * input.Fast.PoseAlpha));
                }
                if (flail > 0) Source(_profile.Fall.Flail, basis.WithWeight(basis.Weight * flail));
            }
            else
            {
                _jump.Frame = Frame;
                var actualRules = rules with { RelevantJumpLeftTimeRemaining = _jump.ObserveRemaining(1), RelevantJumpRightTimeRemaining = _jump.ObserveRemaining(2) };
                var update = _runtime.Update(State.NestedJump, actualRules, speed, basis, this);
                JumpUpdate = update.Machine;
                State = State with { NestedJump = update.State, Rates = _jump.Rates };
                JumpUpdated = true; JumpReinitialized |= update.Machine.Reinitialized;
            }
            // ApplyAdditive visits Base before Lean. Both receive the parent's full branch weight.
            var profile = state == 1 ? _profile.Fall.Lean : _profile.Jump.Lean;
            profile.Runtime.Evaluate(input.Lean, _weights, _order);
            Source(profile.PlayerId, basis, _weights);
        }
        if (input.Prediction.PoseAlpha > 0)
        {
            var profile = state == 1 ? _profile.Fall : _profile.Jump;
            var weight = context.Weight * input.Prediction.PoseAlpha;
            if (input.PredictionLightPoseAlpha < 1) Evaluator(profile.Heavy, weight * (1 - input.PredictionLightPoseAlpha));
            if (input.PredictionLightPoseAlpha > 0) Evaluator(profile.Light, weight * input.PredictionLightPoseAlpha);
        }
        State = state == 1 ? State with { Fall = input } : State with { Jump = input };
    }

    private void Source(int id, in AlsPoseUpdateContext context, ReadOnlySpan<float> weights = default)
    {
        var player = _players[id];
        if (_updated[id] || Frame.Epochs[id] <= 0 || player.Kind == AlsLocomotionSourceKind.TeleportEvaluator ||
            player.SampleCount != (weights.IsEmpty ? 1 : weights.Length)) throw new InvalidOperationException("Invalid or duplicate air source.");
        _updated[id] = true; Frame.CachedWeights[id] = context.Weight;
        Updates[Count] = new(id, Frame.Epochs[id], Frame.Times[id], context.Weight, SampleCount, player.SampleCount, context.InertializationSync);
        for (var sample = 0; sample < player.SampleCount; sample++) Samples[SampleCount++] = new(player.SampleStart + sample, weights.IsEmpty ? 1 : weights[sample], 1);
        Contexts[Count++] = context;
    }
    private void Evaluator(int id, float weight)
    {
        if (Frame.Epochs[id] <= 0) throw new InvalidOperationException("Uninitialized air evaluator.");
        Frame.CachedWeights[id] = weight;
    }
    void IAlsJumpStateUpdateSink.ClearSourceWeights(byte states)
    { _jump.Frame = Frame; _jump.ClearSourceWeights(states); Frame = _jump.Frame; }
    void IAlsJumpStateUpdateSink.InitializeSource(int id)
    { _jump.Frame = Frame; _jump.InitializeSource(id); Frame = _jump.Frame; RecordInitialization(id); }
    void IAlsJumpStateUpdateSink.UpdateSource(int id, in AlsPoseUpdateContext context)
    {
        _jump.Frame = Frame; _jump.UpdateSource(id, context); Frame = _jump.Frame;
        Source(id, context);
    }
    void IAlsJumpStateUpdateSink.RequestInertialization(in AlsPoseUpdateContext context, float seconds) => _jump.RequestInertialization(context, seconds);
    private void RecordInitialization(int id)
    {
        if (InitializationCount == InitializationOrder.Length) throw new InvalidOperationException("Air initialization journal overflow.");
        InitializationOrder[InitializationCount++] = id;
    }
    private void CheckState(int state)
    { if (!_begun || state is not (1 or 2)) throw new ArgumentException("Not an air source state."); }
}
