using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

// Native EAlsHipsDirection order, not the direction state machine's state order.
public enum AlsRefactoredHipsDirection { Forward, Backward, LeftForward, LeftBackward, RightForward, RightBackward }

public readonly record struct AlsRefactoredMovementParentState(bool PivotActive, AlsRefactoredHipsDirection HipsDirection);

/// <summary>Candidate Parent movement state and original stance callbacks. The
/// host must cancel every participant on frame failure and validate all before
/// committing. Calling order, outer stance traversal and Notify dispatch remain
/// host responsibilities; this is not a complete animation Parent.</summary>
public sealed partial class AlsRefactoredMovementParentRuntime
{
    private readonly HashSet<AlsRefactoredStanceCallback> _bindings;
    private readonly string _digest;
    private AlsRefactoredMovementParentState _candidate;
    private AlsFrameIdentity _identity, _committedIdentity;
    private bool _prepared, _faulted, _hasCommitted;
    public AlsRefactoredMovementParentState Committed { get; private set; }
    public AlsRefactoredMovementParentState Candidate
    {
        get { Check(_identity.FrameId); return _candidate; }
    }

    public AlsRefactoredMovementParentRuntime(AlsRefactoredStanceCallbacks profile, AlsRefactoredMovementSettings? settings = null,
        AlsRefactoredStanceCallbacks? additionalStance = null)
    {
        if (settings is not null && settings.CatalogDigest != profile.CatalogDigest) throw new ArgumentException("Foreign movement settings.");
        if (additionalStance is not null && additionalStance.CatalogDigest != profile.CatalogDigest) throw new ArgumentException("Foreign stance callbacks.");
        _settings = settings;
        _digest = profile.CatalogDigest;
        _bindings = profile.Nodes.ToArray().Concat(additionalStance?.Nodes.ToArray() ?? []).Where(n => n.Function is AlsRefactoredStanceFunction.ResetPivot or
            AlsRefactoredStanceFunction.SetHipsDirection || settings is not null && n.Function is
            (AlsRefactoredStanceFunction.RefreshGroundedMovement or AlsRefactoredStanceFunction.InitializeStandingMovement or
             AlsRefactoredStanceFunction.RefreshStandingMovement or AlsRefactoredStanceFunction.RefreshCrouchingMovement)).ToHashSet();
    }

    public void Prepare(AlsFrameIdentity identity, bool initializeInstance = false)
    {
        if (_prepared || identity.SlotGeneration == 0 || _hasCommitted &&
            (identity.CharacterId != _committedIdentity.CharacterId || identity.SlotGeneration != _committedIdentity.SlotGeneration ||
             identity.FrameId <= _committedIdentity.FrameId)) throw new ArgumentException("Invalid movement Parent frame/owner.");
        _identity = identity; _candidate = initializeInstance ? default : Committed;
        _movementCandidate = initializeInstance ? AlsRefactoredMovementState.Initial : CommittedMovement;
        _airCandidate = initializeInstance ? AlsRefactoredInAirState.Initial : CommittedAir;
        _airPrepared = false;
        _hasInput = false;
        _prepared = true; _faulted = false;
    }

    // Speed and threshold use native cm/s, before mesh scale correction.
    public void ActivatePivot(long frame, float speed, float threshold)
    {
        Check(frame);
        if (!float.IsFinite(speed) || speed < 0 || !float.IsFinite(threshold) || threshold < 0)
        { _faulted = true; throw new ArgumentException("Invalid Pivot speed/threshold."); }
        _candidate = _candidate with { PivotActive = speed < threshold };
    }

    internal void ValidateContext(AlsFrameIdentity identity, string catalogDigest)
    {
        Check(identity.FrameId);
        if (identity != _identity || catalogDigest != _digest)
        { _faulted = true; throw new ArgumentException("Foreign movement Parent context."); }
    }

    public void Apply(AlsFrameIdentity identity, string catalogDigest, AlsRefactoredStanceCallback command)
    {
        ValidateContext(identity, catalogDigest);
        if (!_bindings.Contains(command))
        { _faulted = true; throw new ArgumentException("Unsupported or foreign movement Parent callback."); }
        try
        {
            switch (command.Function)
            {
                case AlsRefactoredStanceFunction.ResetPivot: _candidate = _candidate with { PivotActive = false }; break;
                case AlsRefactoredStanceFunction.SetHipsDirection:
                    _candidate = _candidate with { HipsDirection = Enum.Parse<AlsRefactoredHipsDirection>(command.HipsDirection) }; break;
                case AlsRefactoredStanceFunction.InitializeStandingMovement:
                    _candidate = _candidate with { PivotActive = false };
                    _movementCandidate = _movementCandidate with { SprintTime = 0 }; break;
                default: Refresh(command.Function); break;
            }
        }
        catch { _faulted = true; throw; }
    }

    public void ValidateCommit(long frame) => Check(frame);
    public void Commit(long frame)
    {
        Check(frame); Committed = _candidate; CommittedMovement = _movementCandidate; CommittedAir = _airCandidate;
        _committedIdentity = _identity; _hasCommitted = true; Cancel();
    }
    public void Cancel() { _prepared = _faulted = false; }
    private void Check(long frame)
    {
        if (!_prepared || _faulted || frame != _identity.FrameId) throw new InvalidOperationException("No valid movement Parent candidate.");
    }
}
