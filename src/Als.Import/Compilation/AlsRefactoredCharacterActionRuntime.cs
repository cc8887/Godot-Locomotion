using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>One physical Grounded bank/queue and atomic frame coordinator.
/// Caller traverses the optional original Grounded host (or isolated stances)
/// and supplies its pose to Transition. Locomotion remains an outer consumer.</summary>
public sealed class AlsRefactoredCharacterActionRuntime
{
    internal readonly AlsMontageRuntime Bank;
    internal readonly AlsTransitionQueueRuntime Queue;
    internal readonly AlsRefactoredMovementParentRuntime MovementParent;
    internal readonly AlsRefactoredRestParentRuntime RestParent;
    private bool _parentsPrepared;
    private AlsRefactoredStandingHostInput _parentInput;
    private readonly AlsRefactoredCharacterActionProfile _profile;
    private readonly uint _character, _generation;
    private AlsFrameIdentity _identity;
    private float _delta;
    private bool _prepared, _postUpdated, _globalFrame;
    public AlsRefactoredStandingHost Standing { get; }
    public AlsRefactoredCrouchingHost? Crouching { get; }
    public AlsRefactoredGroundedHost? Grounded { get; }
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
        MovementParent=new(profile.Standing.Callbacks,profile.Standing.MovementSettings,profile.Crouching?.Callbacks);
        RestParent=new(profile.Standing.Montages.Settings,profile.Standing.Montages,Bank,Queue,
            profile.Crouching is null?[profile.Standing.Callbacks]:[profile.Standing.Callbacks,profile.Crouching.Callbacks]);
        Standing = new(profile.Standing, character, generation, this); Transition = new(profile, this);
        if(profile.Crouching is not null)Crouching=new(profile.Crouching,character,generation,this);
        if(profile.Grounded is not null)Grounded=new(profile,this);
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
    /// <summary>AnimInstance update also runs when the pose graph is hidden. The
    /// caller may subsequently visit either stance and the outer Transition Slot.</summary>
    public void BeginGlobal(in AlsPoseUpdateContext context, in AlsRefactoredStandingHostInput input, bool initialize = false)
    {
        Begin(context.Identity, context.Delta);
        try { PrepareParents(context, input, initialize); _globalFrame = true; }
        catch { Discard(); throw; }
    }
    public void StopTransitions()
    { Check(); if (_postUpdated) throw new InvalidOperationException("Late transition stop."); Queue.QueueStop(); }
    internal void PrepareParents(in AlsPoseUpdateContext context,in AlsRefactoredStandingHostInput input,bool initialize)
    {
        ValidateUpdate(context);
        if(_parentsPrepared)
        {if(input.Movement!=_parentInput.Movement||input.Rest!=_parentInput.Rest)throw new ArgumentException("Stance children received different Parent inputs.");return;}
        MovementParent.Prepare(context.Identity,input.Movement,initialize);
        // With the original outer graph, Grounded refresh belongs to node5 and
        // only runs when that graph is traversed, after node44 initialization.
        if(Grounded is null)MovementParent.RefreshGrounded(context.Identity.FrameId);
        if(input.ActivatePivot)MovementParent.ActivatePivot(context.Identity.FrameId);
        RestParent.Prepare(context.Identity,input.Rest,initialize);_parentInput=input;_parentsPrepared=true;
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
        try
        {
            if(!_parentsPrepared)throw new InvalidOperationException("No character Parent update.");
            _profile.Standing.Montages.PostUpdate(Bank,RestParent,Queue,_identity);
            if(Standing.Prepared)Standing.PostUpdateSharedActions();
            if(Crouching?.Prepared==true)Crouching.PostUpdateSharedActions();
            _postUpdated = true;
        }
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
        if(!_globalFrame&&!Standing.Prepared&&Crouching?.Prepared!=true)throw new ArgumentException("No stance updated.");
        if(Standing.Prepared)Standing.ValidateCommit(identity);if(Crouching?.Prepared==true)Crouching.ValidateCommit(identity);
        if(Grounded?.Prepared==true)Grounded.ValidateCommit(identity);
        MovementParent.ValidateCommit(identity.FrameId);RestParent.ValidateCommit(identity.FrameId);
        if (!_globalFrame || Transition.Prepared) Transition.ValidateCommit(identity);
        Queue.ValidateCommit(identity); Bank.ValidateCommit(identity);
    }
    public void Commit(in AlsFrameIdentity identity)
    {
        ValidateCommit(identity);
        if(Standing.Prepared)Standing.CommitShared(identity);if(Crouching?.Prepared==true)Crouching.CommitShared(identity);
        if(Grounded?.Prepared==true)Grounded.CommitShared(identity);
        MovementParent.Commit(identity.FrameId);RestParent.Commit(identity.FrameId);
        if (Transition.Prepared) Transition.Commit(identity);
        Queue.Commit(identity); Bank.Commit(identity);
        CommittedIdentity = identity; _prepared = _postUpdated = _parentsPrepared = _globalFrame = false;
    }
    public void Discard()
    { Grounded?.CancelGraph();Standing.CancelGraph();Crouching?.CancelGraph();MovementParent.Cancel();RestParent.Cancel(); Transition.Cancel(); Queue.Discard(); Bank.Discard(); _prepared = _postUpdated = _parentsPrepared = _globalFrame = false; }
    private void Check() { if (!_prepared) throw new InvalidOperationException("No character action candidate."); }
}
