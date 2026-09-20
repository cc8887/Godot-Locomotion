using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsOverlayTransitionBinding(AlsOverlayMachineKind Machine, int Edge,
    int GeneratedIndex, string NotifyName, int AnimationId, int AdditiveType, float BlendIn, float BlendOut, float PlayRate, float StartTime);

public sealed class AlsOverlayTransitionDefinition
{
    private readonly AlsOverlayTransitionBinding[] _bindings;
    public ReadOnlySpan<AlsOverlayTransitionBinding> Bindings => _bindings;
    public string BindingDigest { get; }
    public AlsMontageSlot Slot => AlsMontageSlot.Grounded;
    public int LoopCount => 1;
    public float BlendOutTriggerTime => 0;
    public AlsOverlayTransitionDefinition(string digest, AlsOverlayTransitionBinding[] bindings)
    {
        if (digest.Length != 64 || bindings.Length != 8 || bindings.Select(b => b.GeneratedIndex).Distinct().Count() != 8 ||
            bindings.Any(b => b.Machine is < AlsOverlayMachineKind.Rifle or > AlsOverlayMachineKind.Bow || b.Edge < 0 ||
                b.GeneratedIndex < 0 || string.IsNullOrEmpty(b.NotifyName) || b.AnimationId < 0 || (uint)b.AdditiveType > 2 ||
                !float.IsFinite(b.BlendIn) || b.BlendIn < 0 || !float.IsFinite(b.BlendOut) || b.BlendOut < 0 ||
                !float.IsFinite(b.PlayRate) || b.PlayRate <= 0 || !float.IsFinite(b.StartTime) || b.StartTime < 0))
            throw new ArgumentException("Incomplete Overlay transition consumers.");
        BindingDigest = digest; _bindings = (AlsOverlayTransitionBinding[])bindings.Clone();
    }
    public AlsOverlayTransitionBinding Resolve(AlsOverlayMachineKind machine, in AlsOverlayTransitionNotify notify)
    {
        foreach (var binding in _bindings)
            if (binding.Machine == machine && binding.Edge == notify.Edge && binding.GeneratedIndex == notify.GeneratedIndex) return binding;
        throw new ArgumentException("Overlay transition notify belongs to another machine/edge/revision.");
    }
}

public readonly record struct AlsOverlayTransitionCommand(AlsFrameIdentity Identity, int QueueOrdinal, AlsOverlayTransitionBinding Binding);

// Generated state-machine notifies bypass asset weight/chance filters and preserve
// duplicates. Resolve CanOverlayTransition at dispatch, after graph evaluation.
// This owns candidate commands only: the enclosing frame must play them through
// its physical Grounded Slot montage owner before committing both transactions.
public sealed class AlsOverlayTransitionRuntime(AlsOverlayTransitionDefinition definition, uint characterId, uint generation)
{
    private enum Phase { Idle, Collecting, Resolved }
    private AlsOverlayTransitionCommand[] _candidate = new AlsOverlayTransitionCommand[16], _committed = new AlsOverlayTransitionCommand[16];
    private int _count, _committedCount, _queued;
    private AlsFrameIdentity _identity;
    private Phase _phase;
    public AlsFrameIdentity CommittedIdentity { get; private set; }
    public ReadOnlySpan<AlsOverlayTransitionCommand> CommittedCommands => _committed.AsSpan(0, _committedCount);
    public int QueuedCount => _queued;
    public ReadOnlySpan<AlsOverlayTransitionCommand> Commands
    {
        get { Require(Phase.Resolved); return _candidate.AsSpan(0, _count); }
    }
    public void Begin(in AlsFrameIdentity identity)
    {
        Require(Phase.Idle);
        if (generation == 0 || identity.SlotGeneration != generation || identity.CharacterId != characterId ||
            identity.FrameId <= 0 || CommittedIdentity != default && identity.FrameId <= CommittedIdentity.FrameId)
            throw new ArgumentException("Invalid Overlay notify frame owner/order.");
        _identity = identity; _count = _queued = 0; _phase = Phase.Collecting;
    }
    public void Queue(AlsOverlayMachineKind machine, in AlsOverlayTransitionNotify notify)
    {
        Require(Phase.Collecting);
        var binding = definition.Resolve(machine, notify);
        if (_count == _candidate.Length) Array.Resize(ref _candidate, checked(_count * 2));
        _candidate[_count++] = new(_identity, _queued++, binding);
    }
    public void Resolve(AlsStance stance, bool shouldMove)
    {
        Require(Phase.Collecting);
        if (stance is not (AlsStance.Standing or AlsStance.Crouching)) throw new ArgumentException("Invalid Overlay dispatch stance.");
        if (stance != AlsStance.Standing || shouldMove) _count = 0;
        _phase = Phase.Resolved;
    }
    public void ValidateCommit(in AlsFrameIdentity identity)
    {
        Require(Phase.Resolved);
        if (identity != _identity) throw new ArgumentException("Overlay notify commit frame differs.");
    }
    public void Commit(in AlsFrameIdentity identity)
    {
        ValidateCommit(identity);
        (_committed, _candidate) = (_candidate, _committed); _committedCount = _count;
        CommittedIdentity = identity; Cancel();
    }
    public void Cancel() { _count = _queued = 0; _phase = Phase.Idle; }
    private void Require(Phase phase)
    {
        if (_phase != phase) throw new InvalidOperationException("Invalid Overlay notify transaction phase.");
    }
}
