using GodotAls.Core.Contracts;

namespace GodotAls.Import.Compilation;

// Native EAlsHipsDirection order, not the direction state machine's state order.
public enum AlsRefactoredHipsDirection { Forward, Backward, LeftForward, LeftBackward, RightForward, RightBackward }

public readonly record struct AlsRefactoredMovementParentState(bool PivotActive, AlsRefactoredHipsDirection HipsDirection);

/// <summary>Candidate Parent latches for the original ResetPivot/SetHipsDirection
/// callbacks. The host must cancel every participant on frame failure and validate
/// every participant before committing. Movement refresh and Notify dispatch are
/// separate responsibilities; this is not a complete animation Parent.</summary>
public sealed class AlsRefactoredMovementParentRuntime
{
    private readonly Dictionary<int, AlsRefactoredStanceCallback> _bindings;
    private readonly string _digest;
    private AlsRefactoredMovementParentState _candidate;
    private AlsFrameIdentity _identity, _committedIdentity;
    private bool _prepared, _faulted, _hasCommitted;
    public AlsRefactoredMovementParentState Committed { get; private set; }
    public AlsRefactoredMovementParentState Candidate
    {
        get { Check(_identity.FrameId); return _candidate; }
    }

    public AlsRefactoredMovementParentRuntime(AlsRefactoredStanceCallbacks profile)
    {
        _digest = profile.CatalogDigest;
        _bindings = profile.Nodes.ToArray().Where(n => n.Function is AlsRefactoredStanceFunction.ResetPivot or
            AlsRefactoredStanceFunction.SetHipsDirection).ToDictionary(n => n.PropertyIndex);
    }

    public void Prepare(AlsFrameIdentity identity, bool initializeInstance = false)
    {
        if (_prepared || identity.SlotGeneration == 0 || _hasCommitted &&
            (identity.CharacterId != _committedIdentity.CharacterId || identity.SlotGeneration != _committedIdentity.SlotGeneration ||
             identity.FrameId <= _committedIdentity.FrameId)) throw new ArgumentException("Invalid movement Parent frame/owner.");
        _identity = identity; _candidate = initializeInstance ? default : Committed;
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
        if (!_bindings.TryGetValue(command.PropertyIndex, out var binding) || binding != command)
        { _faulted = true; throw new ArgumentException("Unsupported or foreign movement Parent callback."); }
        _candidate = command.Function == AlsRefactoredStanceFunction.ResetPivot
            ? _candidate with { PivotActive = false }
            : _candidate with { HipsDirection = Enum.Parse<AlsRefactoredHipsDirection>(command.HipsDirection) };
    }

    public void ValidateCommit(long frame) => Check(frame);
    public void Commit(long frame)
    {
        Check(frame); Committed = _candidate; _committedIdentity = _identity; _hasCommitted = true; Cancel();
    }
    public void Cancel() { _prepared = _faulted = false; }
    private void Check(long frame)
    {
        if (!_prepared || _faulted || frame != _identity.FrameId) throw new InvalidOperationException("No valid movement Parent candidate.");
    }
}
