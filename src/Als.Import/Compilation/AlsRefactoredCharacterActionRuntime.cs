using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>One physical Grounded bank/queue and atomic frame coordinator.
/// Caller traverses Standing and supplies the Grounded pose to Transition;
/// this object does not stand in for the missing Grounded/Locomotion graph.</summary>
public sealed class AlsRefactoredCharacterActionRuntime
{
    internal readonly AlsMontageRuntime Bank;
    internal readonly AlsTransitionQueueRuntime Queue;
    private readonly AlsRefactoredCharacterActionProfile _profile;
    private readonly uint _character, _generation;
    private AlsFrameIdentity _identity;
    private float _delta;
    private bool _prepared, _postUpdated;
    public AlsRefactoredStandingHost Standing { get; }
    public AlsRefactoredTransitionSlot Transition { get; }
    public AlsFrameIdentity CommittedIdentity { get; private set; }
    public AlsMontageFrame Frame { get { Check(); return Bank.Frame; } }
    public ReadOnlySpan<AlsMontageInstance> Committed => Bank.Committed;
    public ReadOnlySpan<AlsMontageInstance> Candidate { get { Check(); return Bank.Candidate; } }
    public AlsTransitionQueueState QueueState { get { Check(); return Queue.Candidate; } }

    internal AlsRefactoredCharacterActionRuntime(AlsRefactoredCharacterActionProfile profile, uint character, uint generation)
    {
        ArgumentOutOfRangeException.ThrowIfZero(generation);
        _profile = profile; _character = character; _generation = generation;
        Bank = new([], sequences: profile.Assets); Queue = new(Bank, character, generation);
        Standing = new(profile.Standing, character, generation, this); Transition = new(profile, this);
    }
    public void Begin(in AlsFrameIdentity identity, float delta)
    {
        if (_prepared || identity.CharacterId != _character || identity.SlotGeneration != _generation ||
            !float.IsFinite(delta) || delta < 0) throw new ArgumentException("Invalid character action frame.");
        try { Queue.Begin(identity); Bank.Begin(identity, delta); _identity = identity; _delta = delta; _prepared = true; }
        catch { Discard(); throw; }
    }
    internal void ValidateUpdate(in AlsPoseUpdateContext context)
    {
        Check();
        if (_postUpdated || context.Identity != _identity || context.Delta != _delta)
            throw new ArgumentException("Foreign, stale or post-updated character action context.");
    }
    public void QueueWeapon(in AlsRefactoredWeaponNotifyBinding binding, string stance, bool moving)
    {
        Check(); if (_postUpdated) throw new InvalidOperationException("Worker requests must precede PostUpdate.");
        try { Queue.QueuePlay(_profile.Weapons.Command(binding), stance, moving, true); }
        catch { Discard(); throw; }
    }
    public void PostUpdateActions()
    {
        Check(); if (_postUpdated) throw new InvalidOperationException("Character actions already consumed.");
        try { Standing.PostUpdateSharedActions(); _postUpdated = true; }
        catch { Discard(); throw; }
    }
    public void PlayWeaponNotify(in AlsRefactoredWeaponNotifyBinding binding, string stance, bool moving)
    {
        Check(); if (!_postUpdated) throw new InvalidOperationException("Main-thread notifies follow PostUpdate.");
        try { Queue.PlayImmediate(_profile.Weapons.Command(binding), stance, moving, true); }
        catch { Discard(); throw; }
    }
    public void ValidateCommit(in AlsFrameIdentity identity)
    {
        Check(); if (identity != _identity || !_postUpdated) throw new ArgumentException("Incomplete character action frame.");
        Standing.ValidateCommit(identity); Transition.ValidateCommit(identity); Queue.ValidateCommit(identity); Bank.ValidateCommit(identity);
    }
    public void Commit(in AlsFrameIdentity identity)
    {
        ValidateCommit(identity);
        Standing.CommitShared(identity); Transition.Commit(identity); Queue.Commit(identity); Bank.Commit(identity);
        CommittedIdentity = identity; _prepared = _postUpdated = false;
    }
    public void Discard()
    { Standing.CancelGraph(); Transition.Cancel(); Queue.Discard(); Bank.Discard(); _prepared = _postUpdated = false; }
    private void Check() { if (!_prepared) throw new InvalidOperationException("No character action candidate."); }
}
