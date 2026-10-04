using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// Original five nodes retain source-weight history across hidden updates.
// They consume one frozen Montage frame and own no clocks or animation data.
internal sealed class LyraMainSlotUpdateOwner:ILyraMontageNotifySlots
{
    private readonly AlsMontageRuntime _runtime;
    private AlsSlotWeights[] _committed = new AlsSlotWeights[5], _candidate = new AlsSlotWeights[5];
    private AlsMontageFrame? _frame; private AlsFrameIdentity _identity, _committedIdentity; private int _visited;
    private ushort _relevant,_candidateRelevant;
    public ushort NotifyRelevantMask {get{Validate();return (ushort)(_relevant|_candidateRelevant);}}
    public ushort CurrentNotifyRelevantMask {get{Validate();return _candidateRelevant;}}
    public LyraMainSlotUpdateOwner(AlsMontageRuntime runtime) { _runtime = runtime; }
    public void Begin(AlsMontageFrame frame, bool initialize = false)
    {
        if (_frame is not null || !ReferenceEquals(frame, _runtime.Frame) || frame.Identity.SlotGeneration == 0 || _committedIdentity != default &&
            (frame.Identity.CharacterId != _committedIdentity.CharacterId || frame.Identity.SlotGeneration != _committedIdentity.SlotGeneration ||
             frame.Identity.FrameId <= _committedIdentity.FrameId)) throw new InvalidOperationException("Invalid Main Slot candidate.");
        _identity = frame.Identity; _frame = frame; _visited = 0;_candidateRelevant=0;
        if (initialize) Array.Clear(_candidate); else _committed.CopyTo(_candidate, 0);
    }
    private void Validate()
    { if (_frame is null || _frame.Identity != _identity || !ReferenceEquals(_frame, _runtime.Frame)) throw new InvalidOperationException("Recycled or absent Main Montage frame."); }
    public AlsSlotSourceUpdate Update(int slot, in AlsPoseUpdateContext context, bool markBlendingOut = true)
    {
        Validate();
        if ((uint)slot >= 5 || context.Identity != _identity || (_visited & 1 << slot) != 0)
            throw new InvalidOperationException("Foreign or duplicate Main Slot update.");
        var weights = _frame!.SlotWeights(new(slot));
        var source = AlsSlotSourceUpdate.Resolve(_candidate[slot].SourceWeight, weights, context, false, markBlendingOut);
        if(weights.SlotNodeWeight>AlsPoseBlender.WeightThreshold)_candidateRelevant|=(ushort)(1<<slot);
        _candidate[slot] = weights; _visited |= 1 << slot; return source;
    }
    public AlsSlotWeights Weights(int slot)
    { Validate(); return (uint)slot < 5 ? _candidate[slot] : throw new ArgumentOutOfRangeException(nameof(slot)); }
    public void ValidateCommit(AlsFrameIdentity identity)
    { Validate(); if (identity != _identity) throw new InvalidOperationException("Foreign Main Slot commit."); }
    public void Commit(AlsFrameIdentity identity)
    { ValidateCommit(identity); (_committed, _candidate) = (_candidate, _committed);_relevant=_candidateRelevant; _committedIdentity = identity; _frame = null; }
    public void Cancel() { _frame = null; _visited = 0; }
}
