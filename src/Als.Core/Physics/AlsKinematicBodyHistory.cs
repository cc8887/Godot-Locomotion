using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// Single owner, one full synchronous physics step per identity. Targets are staged;
// entry never inherits the derivative of an unexecuted target.
// This is kinematic actor velocity as stored by Chaos, not a new COM lever-arm
// reconstruction. The dynamic transition preserves those stored values.
public sealed class AlsKinematicBodyHistory
{
    private readonly AlsIslandBodyState[] _committed, _candidate;
    private AlsFrameIdentity _candidateIdentity;
    private bool _prepared, _teleport;
    public AlsFrameIdentity CommittedIdentity { get; private set; }
    public int BodyCount => _committed.Length;

    public AlsKinematicBodyHistory(AlsFrameIdentity identity, ReadOnlySpan<AlsPrecisePose> initialActors)
    {
        if (identity.FrameId <= 0 || identity.SlotGeneration == 0 || initialActors.IsEmpty)
            throw new ArgumentException("Kinematic history requires a live initial identity and body layout.");
        _committed = new AlsIslandBodyState[initialActors.Length];
        _candidate = new AlsIslandBodyState[initialActors.Length];
        for (var i = 0; i < initialActors.Length; i++)
            _committed[i] = AlsKinematicMotion.PositionTarget(initialActors[i], initialActors[i], 1d / 60);
        CommittedIdentity = identity;
    }

    public void PrepareTargets(AlsFrameIdentity identity, ReadOnlySpan<AlsPrecisePose> actors,
        double dt, bool teleport = false)
    {
        Begin(identity);
        if (actors.Length != BodyCount) throw new ArgumentException("Kinematic body layout differs.");
        for (var i = 0; i < actors.Length; i++)
            _candidate[i] = AlsKinematicMotion.PositionTarget(teleport ? actors[i] : _committed[i].Actor, actors[i], dt);
        _candidateIdentity = identity; _teleport = teleport; _prepared = true;
    }

    // Completing a step with no new target consumes native Reset mode: hold
    // transforms and zero velocity. It is not equivalent to skipping the step.
    public void PrepareNoTarget(AlsFrameIdentity identity)
    {
        Begin(identity);
        for (var i = 0; i < BodyCount; i++) _candidate[i] = _committed[i] with { Velocity = default };
        _candidateIdentity = identity; _teleport = false; _prepared = true;
    }

    public void Commit(AlsFrameIdentity identity)
    {
        if (!_prepared || identity != _candidateIdentity) throw new InvalidOperationException("No matching kinematic candidate.");
        _candidate.AsSpan().CopyTo(_committed); CommittedIdentity = identity; _prepared = false;
    }

    public void Cancel() => _prepared = false;

    public void CopyCommitted(AlsFrameIdentity identity, Span<AlsIslandBodyState> destination)
    {
        if (identity != CommittedIdentity || destination.Length != BodyCount)
            throw new ArgumentException("Read requires the exact completed physics identity and body count.");
        _committed.AsSpan().CopyTo(destination);
    }

    // After an accepted target reaches the game thread but before its physics
    // step, activation uses the new actor pose with the previous stored V/W.
    // Teleport is special: the game-thread setter clears V/W immediately.
    // Returns the completed velocity-source identity; the target identity is
    // deliberately separate. This read neither commits nor consumes the target.
    public AlsFrameIdentity CopyPendingActivation(AlsFrameIdentity targetIdentity, Span<AlsIslandBodyState> destination)
    {
        if (!_prepared || targetIdentity != _candidateIdentity || destination.Length != BodyCount)
            throw new ArgumentException("Activation requires the accepted pending target and exact body layout.");
        for (var i = 0; i < BodyCount; i++)
            destination[i] = new(_candidate[i].Actor, _teleport ? default : _committed[i].Velocity);
        return CommittedIdentity;
    }

    private void Begin(AlsFrameIdentity identity)
    {
        _prepared = false;
        if (identity.CharacterId != CommittedIdentity.CharacterId || identity.SlotGeneration != CommittedIdentity.SlotGeneration ||
            CommittedIdentity.FrameId == long.MaxValue || identity.FrameId != CommittedIdentity.FrameId + 1)
            throw new ArgumentException("Kinematic history requires the next physical step in the same lifetime.");
    }
}
