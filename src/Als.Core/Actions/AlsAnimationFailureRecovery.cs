using GodotAls.Core.Contracts;

namespace GodotAls.Core.Actions;

// Outside committed animation banks. Process-stage barriers serialize access.
// Failed attempts retain both this latch and the exact already-gathered input.
public sealed class AlsAnimationFailureRecovery
{
    public const int MaximumAutomaticRetries = 3;
    public AlsFrameIdentity PendingIdentity { get; private set; }
    public AlsFrameIdentity CommittedBeforeFailure { get; private set; }
    public int FailedAttempts { get; private set; }
    public bool Pending => FailedAttempts != 0;
    public bool CanRetry => Pending && FailedAttempts <= MaximumAutomaticRetries;

    public void Block() { if (Pending) FailedAttempts = MaximumAutomaticRetries + 1; }

    public void RecordRolledBackFailure(AlsFrameIdentity identity, AlsFrameIdentity committed)
    {
        if (identity.SlotGeneration == 0 || identity.CharacterId != committed.CharacterId ||
            identity.SlotGeneration != committed.SlotGeneration || committed.FrameId < 0 ||
            committed.FrameId == long.MaxValue || identity.FrameId != committed.FrameId + 1 ||
            Pending && (identity != PendingIdentity || committed != CommittedBeforeFailure))
            throw new InvalidOperationException("Recovery requires the next uncommitted frame of the same owner.");
        PendingIdentity = identity; CommittedBeforeFailure = committed;
        FailedAttempts = System.Math.Min(FailedAttempts + 1, MaximumAutomaticRetries + 1);
    }

    public bool RequiresCancellation(AlsFrameIdentity identity)
    {
        if (!Pending) return false;
        if (identity != PendingIdentity) throw new InvalidOperationException("Recovery cannot skip its gathered frame.");
        return true;
    }

    public void Commit(AlsFrameIdentity identity)
    {
        if (!Pending) return;
        if (identity != PendingIdentity || identity.FrameId <= CommittedBeforeFailure.FrameId)
            throw new InvalidOperationException("Only a strictly newer successful animation commit can consume recovery.");
        PendingIdentity = CommittedBeforeFailure = default; FailedAttempts = 0;
    }
}
