using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Actions;

public readonly record struct AlsTransitionQueueState(AlsSequenceMontageCommand? Play, bool Stop, float StopDuration)
{
    public static AlsTransitionQueueState Empty => new(null, false, -1);
}

/// <summary>Exclusive per-character ALS transition queue. Worker calls only Begin/Queue;
/// after joining the worker, the host consumes PlayQueued, queued Turn, then StopQueued.
/// Queue and physical bank candidates must both validate before either is committed.</summary>
public sealed class AlsTransitionQueueRuntime
{
    private readonly AlsMontageRuntime _owner;
    private readonly uint _character, _generation;
    private AlsTransitionQueueState _candidate;
    private AlsFrameIdentity _identity;
    private bool _prepared;
    public AlsTransitionQueueState Committed { get; private set; } = AlsTransitionQueueState.Empty;
    public AlsFrameIdentity CommittedIdentity { get; private set; }
    public AlsTransitionQueueState Candidate { get { RequirePrepared(); return _candidate; } }

    public AlsTransitionQueueRuntime(AlsMontageRuntime owner, uint character, uint generation)
    {
        ArgumentNullException.ThrowIfNull(owner); ArgumentOutOfRangeException.ThrowIfZero(generation);
        _owner = owner; _character = character; _generation = generation;
    }
    public void Begin(AlsFrameIdentity identity)
    {
        if (_prepared || identity.CharacterId != _character || identity.SlotGeneration != _generation ||
            CommittedIdentity != default && identity.FrameId <= CommittedIdentity.FrameId)
            throw new ArgumentException("Foreign transition queue frame or owner.");
        _identity = identity; _candidate = Committed; _prepared = true;
    }
    // A null sequence is an explicit invalid/empty native sequence. It overwrites
    // an earlier request, whereas a rejected standing-idle guard leaves it intact.
    public bool QueuePlay(AlsSequenceMontageCommand? command, string stance, bool moving, bool standingIdleOnly = false)
    {
        RequirePrepared();
        if (standingIdleOnly && (moving || stance != "Als.Stance.Standing")) return false;
        if (command is { } value && (value.AnimationId < 0 || value.Slot != AlsMontageSlot.Transition || value.LoopCount != 1 ||
            value.BlendOutTriggerTime != 0 || !float.IsFinite(value.PlayRate) || !float.IsFinite(value.StartTime) ||
            !float.IsFinite(value.BlendInTime) || !float.IsFinite(value.BlendOutTime) || value.BlendInTime < 0 || value.BlendOutTime < 0))
            throw new ArgumentException("Invalid ALS transition request.");
        _candidate = _candidate with { Play = command }; return true;
    }
    public void QueueStop(float duration = -1)
    {
        RequirePrepared();
        if (!float.IsFinite(duration)) throw new ArgumentException("Invalid transition stop duration.");
        _candidate = _candidate with { Stop = true, StopDuration = duration };
    }
    public bool PlayQueued()
    {
        ValidateOwner();
        if (_candidate.Stop || _candidate.Play is not { } command) return false;
        if (!_owner.PlaySequence(command)) throw new InvalidOperationException("Transition resource is missing from the physical bank.");
        _candidate = _candidate with { Play = null }; return true;
    }
    public void StopQueued()
    {
        ValidateOwner();
        if (!_candidate.Stop) return;
        _owner.StopSlots([AlsMontageSlot.Transition, AlsTurnSlot.Standing, AlsTurnSlot.Crouching], _candidate.StopDuration);
        // UE deliberately retains the play request blocked above. It can play in
        // a subsequent post-update unless another request overwrites it first.
        _candidate = _candidate with { Stop = false, StopDuration = -1 };
    }
    public bool PlayImmediate(AlsSequenceMontageCommand? command, string stance, bool moving, bool standingIdleOnly = false)
    {
        ValidateOwner();
        return QueuePlay(command, stance, moving, standingIdleOnly) && PlayQueued();
    }
    public void StopImmediate(float duration = -1) { ValidateOwner(); QueueStop(duration); StopQueued(); }
    public void ValidateCommit(AlsFrameIdentity identity)
    {
        if (!_prepared || identity != _identity) throw new ArgumentException("Wrong transition queue commit.");
    }
    public void ValidateBank(AlsMontageRuntime owner,AlsFrameIdentity identity)
    {ValidateCommit(identity);if(!ReferenceEquals(owner,_owner))throw new ArgumentException("Foreign transition queue bank.");}
    public void Commit(AlsFrameIdentity identity)
    {
        ValidateCommit(identity); Committed = _candidate; CommittedIdentity = identity; Discard();
    }
    public void Discard() { _prepared = false; _candidate = default; }
    private void ValidateOwner() { RequirePrepared(); _owner.ValidateCommit(_identity); }
    private void RequirePrepared() { if (!_prepared) throw new InvalidOperationException("No transition queue candidate."); }
}
