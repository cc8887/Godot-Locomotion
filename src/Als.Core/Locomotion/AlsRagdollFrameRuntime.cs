using GodotAls.Core.Contracts;
using GodotAls.Core.Sync;

namespace GodotAls.Core.Locomotion;

public sealed record AlsRagdollFrameDefinition(int MachineNodeIndex, int PlayerNodeIndex,
    AlsGroundedMachineDefinition Machine, AlsRagdollAnimationInput Input, AlsAssetSyncSequence Sequence,
    double InitialFlailRate);

public readonly record struct AlsRagdollSharedSourceBinding(AlsLocomotionSourceStamp Stamp, int PlayerId, int SampleId);
// Scene-owned observation. Velocity is the root body's native UE-axis cm/s,
// never inferred from the character motor. Snapshot data is immutable.
public readonly record struct AlsRagdollFrameObservation(AlsFrameIdentity Identity,
    AlsDoubleVector RootPhysicsVelocityCm, AlsNamedPoseSnapshot? Snapshot);
public readonly record struct AlsRagdollSourceRequest(long Epoch, int Initializations, bool ClearCachedWeight,
    bool Updated, float Time, float Rate, AlsPoseUpdateContext Context);

public interface IAlsRagdollPoseSource
{
    // Caller-owned sequence seconds. Sampling must not advance another clock.
    void Sample(float seconds, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves);
}

public interface IAlsPreciseRagdollPoseSource : IAlsRagdollPoseSource
{
    void SamplePrecise(float seconds, Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves);
}

// Explicit values avoid ValueType.Equals traversing InlineArray state storage,
// which newer .NET runtimes intentionally reject. The supported machine has no
// active transitions after its zero-duration update.
public readonly record struct AlsRagdollFrameDiagnostics(AlsAnimationGraphFrame Traversal,
    AlsGraphTraversalCounter LastMachineUpdate, long Serial, long PlayerEpoch, float Time, double FlailRate,
    float PlayerCachedWeight, bool PlayerTicked, AlsAssetPlayerHistory Player, AlsAssetSampleHistory Sample,
    AlsGroundedMachineKind MachineKind, int State, bool Initialized, bool Updated, long LastUpdateSerial,
    float Elapsed, float RecordedWeight, int TransitionCount);

public struct AlsRagdollFrameState
{
    public AlsAnimationGraphFrame Traversal { get; internal set; }
    public AlsGraphTraversalCounter LastMachineUpdate { get; internal set; }
    public AlsGroundedMachineState Machine { get; internal set; }
    public long Serial { get; internal set; }
    public long PlayerEpoch { get; internal set; }
    public float Time { get; internal set; }
    public double FlailRate { get; internal set; }
    public float PlayerCachedWeight { get; internal set; }
    public bool PlayerTicked { get; internal set; }
    public AlsAssetPlayerHistory Player { get; internal set; }
    public AlsAssetSampleHistory Sample { get; internal set; }
    public readonly AlsRagdollFrameDiagnostics Diagnostics => new(Traversal, LastMachineUpdate, Serial, PlayerEpoch,
        Time, FlailRate, PlayerCachedWeight, PlayerTicked, Player, Sample, Machine.Kind, Machine.CurrentState,
        Machine.HasInitialized, Machine.HasUpdated, Machine.LastUpdateSerial, Machine.ElapsedSeconds,
        Machine.RecordedWeight, Machine.Transitions.Count);
}

// One character's Ragdoll States, including its DoNotSync SequencePlayer.
// Prepare runs once per animation update, even when the root hides this branch:
// global properties and graph initialization are distinct from branch relevance.
// The enclosing final-pose transaction validates every owner before committing.
public sealed class AlsRagdollFrameRuntime
{
    private readonly AlsRagdollFrameDefinition _definition;
    private readonly AlsNamedPoseSnapshotRuntime _snapshot;
    private readonly uint _character, _generation;
    private readonly AlsRagdollSharedSourceBinding? _shared;
    private readonly AlsLocalPose[] _pose;
    private readonly AlsLocalPose[] _committedFlail;
    private readonly AlsPrecisePose[] _precisePose, _committedPreciseFlail;
    private bool _preciseEvaluated, _preciseCommitted;
    public ReadOnlySpan<AlsPrecisePose> PreciseFlailPose => _evaluated && _preciseEvaluated && _candidate.Machine.CurrentState==0
        ? _precisePose : throw new InvalidOperationException("Precise Flail source is unavailable.");
    public bool TryCopyCommittedPreciseFlail(AlsFrameIdentity identity, Span<AlsPrecisePose> destination)
    {
        if(destination.Length!=_committedPreciseFlail.Length) throw new ArgumentException("Flail skeleton layout differs.");
        if(!_preciseCommitted || identity.FrameId<=0 || identity!=CommittedFlailIdentity) return false;
        _committedPreciseFlail.AsSpan().CopyTo(destination);return true;
    }
    public AlsFrameIdentity CommittedFlailIdentity { get; private set; }
    public bool TryCopyCommittedFlail(AlsFrameIdentity identity, Span<AlsLocalPose> destination)
    {
        if (destination.Length != _committedFlail.Length) throw new ArgumentException("Flail skeleton layout differs.");
        if (identity.FrameId <= 0 || identity != CommittedFlailIdentity) return false;
        _committedFlail.AsSpan().CopyTo(destination);
        return true;
    }
    private readonly AlsInertialCurve[] _curves;
    private AlsRagdollFrameState _committed, _candidate;
    private AlsPoseUpdateContext _sourceContext;
    private bool _pending, _visited, _evaluated;
    private bool _sourceUpdated, _sourceCompleted, _clearWeight;
    private int _initializations;
    public AlsRagdollFrameState Committed => _committed;
    public AlsRagdollFrameState Candidate => _pending ? _candidate : throw new InvalidOperationException("No Ragdoll frame.");
    public AlsPoseUpdateContext SourceContext => _pending && _sourceUpdated ? _sourceContext : throw new InvalidOperationException("Flail was not updated.");
    public AlsRagdollSourceRequest SourceRequest => _pending ? new(_candidate.PlayerEpoch, _initializations, _clearWeight,
        _sourceUpdated, _candidate.Time, (float)_candidate.FlailRate, _sourceContext) : throw new InvalidOperationException("No Ragdoll source candidate.");
    public ReadOnlySpan<AlsLocalPose> Pose => _evaluated ? _pose : throw new InvalidOperationException("Ragdoll pose is not evaluated.");
    public ReadOnlySpan<AlsInertialCurve> Curves => _evaluated ? _curves : throw new InvalidOperationException("Ragdoll curves are not evaluated.");

    public AlsRagdollFrameRuntime(AlsRagdollFrameDefinition definition, AlsNamedPoseSnapshotRuntime snapshot,
        uint character, uint generation, int bones, int curves, AlsRagdollSharedSourceBinding? shared = null)
    {
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(snapshot);
        var m = definition.Machine; var s = definition.Sequence;
        if (generation == 0 || bones <= 0 || curves < 0 || definition.MachineNodeIndex < 0 || definition.PlayerNodeIndex < 0 ||
            m.Kind != AlsGroundedMachineKind.Ragdoll || m.States.Length != 2 || m.InitialState != 0 || m.Edges.Length != 2 ||
            m.Edges[0].Condition != AlsGroundedCondition.MovementNotRagdoll || m.Edges[1].Condition != AlsGroundedCondition.MovementRagdoll ||
            m.Edges[0].Duration != 0 || m.Edges[1].Duration != 0 || s.AnimationId < 0 || s.MarkerStart != 0 || s.MarkerCount != 0 ||
            !float.IsFinite(s.DurationSeconds) || s.DurationSeconds <= 0 || !float.IsFinite(s.RateScale) ||
            !double.IsFinite(definition.InitialFlailRate) || !float.IsFinite((float)definition.InitialFlailRate))
            throw new ArgumentException("Unsupported Ragdoll frame definition.");
        if (shared is { } binding && (!binding.Stamp.IsValid || binding.PlayerId < 0 || binding.SampleId < 0))
            throw new ArgumentException("Invalid shared Ragdoll source binding.");
        _shared = shared;
        _definition = definition; _snapshot = snapshot; _character = character; _generation = generation;
        _pose = new AlsLocalPose[bones]; _curves = new AlsInertialCurve[curves];
        _committedFlail = new AlsLocalPose[bones];
        _precisePose=new AlsPrecisePose[bones];_committedPreciseFlail=new AlsPrecisePose[bones];
    }

    public void Prepare(AlsMovementStateInput movement, AlsDoubleVector rootPhysicsVelocityCm,
        bool visitBranch, in AlsPoseUpdateContext context, in AlsAnimationGraphFrame traversal)
    {
        if (_pending) throw new InvalidOperationException("Commit or cancel the Ragdoll frame first.");
        traversal.Validate(context.Identity);
        if (context.Identity.CharacterId != _character || context.Identity.SlotGeneration != _generation || context.Weight > 1 ||
            _committed.Traversal.Identity != default && context.Identity.FrameId <= _committed.Traversal.Identity.FrameId)
            throw new ArgumentException("Foreign or stale Ragdoll frame.");
        _pending = true; _evaluated = false; _visited = visitBranch;
        _sourceUpdated = _sourceCompleted = _clearWeight = false; _initializations = 0; _sourceContext = default;
        try
        {
            _candidate = _committed; _candidate.Serial = checked(_committed.Serial + 1); _candidate.Traversal = traversal;
            _candidate.PlayerTicked = false;
            if (_committed.Traversal.Identity == default) _candidate.FlailRate = _definition.InitialFlailRate;
            // Blueprint UpdateGraph calls UpdateRagdollValues only from this enum
            // branch. Outside it, the double property retains its previous value.
            if (movement == AlsMovementStateInput.Ragdoll) _candidate.FlailRate = _definition.Input.FlailRate(rootPhysicsVelocityCm);
            var initialize = !_candidate.Machine.HasInitialized || !traversal.Initialization.MatchesCounter(_committed.Traversal.Initialization);
            if (initialize) Initialize();
            if (visitBranch)
            {
                // UE checks traversal counters, not elapsed wall time or FrameId.
                // A skipped global frame alone must not reset an animation instance.
                if (!initialize && _candidate.Machine.HasUpdated &&
                    !_candidate.LastMachineUpdate.MatchesCounter(traversal.Update) &&
                    !_candidate.LastMachineUpdate.Next(traversal.Update.GlobalFrame).MatchesCounter(traversal.Update)) Initialize();
                Span<AlsGroundedAutomaticTime> times = stackalloc AlsGroundedAutomaticTime[2]; times.Clear();
                var update = AlsGroundedStateMachine.Update(_definition.Machine, _candidate.Machine,
                    new() { MovementState = movement }, times, context.Weight, context.Delta,
                    _candidate.Machine.HasUpdated ? checked(_candidate.Machine.LastUpdateSerial + 1) : 0);
                Apply(update); _candidate.LastMachineUpdate = traversal.Update;
                for (var i = 0; i < update.UpdateCount; i++)
                {
                    var state = update.GetUpdate(i);
                    if (state.State != 0) continue;
                    _sourceContext = context.WithWeight(state.Weight).WithState(_definition.MachineNodeIndex, 0, state.InertializationSync);
                    _sourceUpdated = true;
                    if (_shared is null) Tick(_sourceContext);
                }
            }
        }
        catch { Cancel(); throw; }
    }

    private void Initialize() => Apply(AlsGroundedStateMachine.Initialize(_definition.Machine));
    private void Apply(in AlsGroundedMachineUpdate update)
    {
        _candidate.Machine = update.State;
        if ((update.ClearCachedWeightStates & 1) != 0) { _candidate.PlayerCachedWeight = 0; _clearWeight = true; }
        for (var i = 0; i < update.InitializationCount; i++)
        {
            if (update.GetInitialization(i) != 0) continue;
            _candidate.PlayerEpoch = AlsAssetSourceInitialization.NextEpoch(_candidate.PlayerEpoch);
            _initializations++;
            _candidate.Time = AlsAssetSourceInitialization.Time(AlsAssetSyncKind.Sequence, 0,
                _definition.Sequence.DurationSeconds, (float)_candidate.FlailRate, assetRateScale: _definition.Sequence.RateScale);
        }
    }
    private void Tick(in AlsPoseUpdateContext context)
    {
        if (_candidate.PlayerTicked) throw new InvalidOperationException("Flail must tick at most once per candidate.");
        Span<AlsAssetSyncPlayer> players = stackalloc AlsAssetSyncPlayer[1];
        players[0] = new(_definition.PlayerNodeIndex, _definition.Sequence.AnimationId, _candidate.PlayerEpoch,
            AlsAssetSyncKind.Sequence, _candidate.Time, (float)_candidate.FlailRate, context.Weight, 0, 1, 0,
            RequestedInertialization: context.InertializationSync);
        Span<AlsAssetPlayerHistory> output = stackalloc AlsAssetPlayerHistory[1];
        Span<AlsAssetSampleHistory> samples = stackalloc AlsAssetSampleHistory[1];
        // DoNotSync is an independent source in the same native timing backend.
        // Inactive affects graph messages, not the SequencePlayer's time advance.
        if (!AlsSyncRuntime.TryEvaluateAssetSyncBatch([], [-1], players, [new(_definition.PlayerNodeIndex, 0, 1)],
                [_definition.Sequence], [], [], _committed.PlayerTicked ? [_committed.Player] : [],
                _committed.PlayerTicked ? [_committed.Sample] : [], context.Delta, [], output, samples, out var failure))
            throw new InvalidOperationException($"Ragdoll independent source tick failed: {failure}");
        _candidate.Player = output[0]; _candidate.Sample = samples[0]; _candidate.Time = output[0].Time;
        _candidate.PlayerCachedWeight = context.Weight; _candidate.PlayerTicked = true;
    }

    // A shared owner advances every collected source once, then returns this
    // player's identity-checked result. No private tick occurs in this mode.
    public void CompleteShared(AlsFrameIdentity identity, AlsLocomotionSourceStamp stamp,
        AlsAssetPlayerHistory? player = null, AlsAssetSampleHistory? sample = null)
    {
        if (!_pending || _shared is not { } binding || stamp != binding.Stamp || _sourceCompleted ||
            identity != _candidate.Traversal.Identity || _sourceUpdated != player.HasValue || _sourceUpdated != sample.HasValue)
            throw new ArgumentException("Foreign or repeated Ragdoll shared completion.");
        if (player is { } p && sample is { } s)
        {
            if (p.PlayerId != binding.PlayerId || s.SampleId != binding.SampleId || p.AssetId != _definition.Sequence.AnimationId ||
                s.AnimationId != _definition.Sequence.AnimationId || p.Epoch != _candidate.PlayerEpoch || p.SampleCount != 1 || p.SampleStart < 0 ||
                !float.IsFinite(p.Time) || p.Time < 0 || p.Time > _definition.Sequence.DurationSeconds || p.Time != s.Time ||
                !float.IsFinite(p.Delta) || !float.IsFinite(p.DeltaPrevious) || !float.IsFinite(s.Delta) || !float.IsFinite(s.DeltaPrevious))
                throw new ArgumentException("Ragdoll received a foreign shared player or sample.");
            _candidate.Player = p; _candidate.Sample = s; _candidate.Time = p.Time;
            _candidate.PlayerCachedWeight = _sourceContext.Weight; _candidate.PlayerTicked = true;
        }
        _sourceCompleted = true;
    }

    public void Evaluate(IAlsRagdollPoseSource source, AlsNamedPoseSnapshot? snapshot)
    {
        if (!_pending || !_visited || _evaluated || _shared is not null && !_sourceCompleted)
            throw new InvalidOperationException("Ragdoll branch is not ready for evaluation.");
        try
        {
            if (_candidate.Machine.CurrentState == 0)
            {
                if (!_candidate.PlayerTicked) throw new InvalidOperationException("Flail pose has no candidate source tick.");
                if(source is IAlsPreciseRagdollPoseSource precise)
                {
                    precise.SamplePrecise(_candidate.Time,_precisePose,_curves);
                    for(var i=0;i<_pose.Length;i++) { _precisePose[i].Validate();_pose[i]=_precisePose[i].ToSingle(); }
                    _preciseEvaluated=true;
                }
                else source.Sample(_candidate.Time, _pose, _curves);
            }
            else _snapshot.Evaluate(_candidate.Traversal.Identity, snapshot, _pose, _curves);
            foreach (var bone in _pose) new AlsPrecisePose(bone).Validate();
            foreach (var curve in _curves) if (curve.Present && !float.IsFinite(curve.Value)) throw new ArgumentException("Invalid Ragdoll curve.");
            _evaluated = true;
        }
        catch { Cancel(); throw; }
    }
    public void ValidateCommit(AlsFrameIdentity identity)
    {
        if (!_pending || identity != _candidate.Traversal.Identity || _visited && !_evaluated || _shared is not null && !_sourceCompleted)
            throw new InvalidOperationException("Incomplete Ragdoll frame commit.");
    }
    public void Commit(AlsFrameIdentity identity)
    {
        ValidateCommit(identity);
        // Preserve the source before root blending or physical display can
        // replace it. Snapshot/hidden commits invalidate the previous target.
        if (_visited && _evaluated && _candidate.Machine.CurrentState == 0)
        {
            _pose.AsSpan().CopyTo(_committedFlail);
            if(_preciseEvaluated) _precisePose.AsSpan().CopyTo(_committedPreciseFlail);
            _preciseCommitted=_preciseEvaluated;
            CommittedFlailIdentity = identity;
        }
        else { CommittedFlailIdentity = default; _preciseCommitted=false; }
        _committed = _candidate; Cancel();
    }
    public void Cancel() { _pending = _visited = _evaluated = _preciseEvaluated = false; }
}
