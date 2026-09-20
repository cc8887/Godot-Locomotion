using GodotAls.Core.Actions;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Events;

public readonly record struct AlsMontageNotifyState(AlsFrameIdentity Identity, uint RandomSeed, byte RelevantSlots);

// One Montage RNG, distinct from the proxy RNG. Direct notifies are independent
// of Slot relevance; the four Slot queues are revealed in first-traversal order.
public sealed class AlsMontageNotifyRuntime
{
    private const int Capacity = AlsP5Runtime.SourceNotifyReferenceCapacity;
    private readonly AlsMontageNotifyBinding _binding;
    private readonly AlsAssetNotifyDispatchInput[] _direct = new AlsAssetNotifyDispatchInput[Capacity];
    private readonly AlsAssetNotifyDispatchInput[] _slots = new AlsAssetNotifyDispatchInput[4 * Capacity];
    private readonly AlsAssetNotifyDispatchInput[] _visible = new AlsAssetNotifyDispatchInput[Capacity];
    private readonly int[] _slotCounts = new int[4], _slotOrder = new int[4];
    private int _directCount, _visibleCount, _slotCount, _total;
    private Phase _phase;
    private enum Phase { Idle, Preparing, Prepared, Complete, Faulted }
    public AlsMontageNotifyState Committed { get; private set; }
    public AlsMontageNotifyState Candidate { get; private set; }
    public int FilteredCount => _total;
    public ReadOnlySpan<AlsAssetNotifyDispatchInput> DirectNotifies { get { RequireComplete(); return _direct.AsSpan(0,_directCount); } }
    public ReadOnlySpan<AlsAssetNotifyDispatchInput> Notifies { get { RequireComplete(); return _visible.AsSpan(0,_visibleCount); } }
    public AlsMontageNotifyRuntime(AlsMontageNotifyBinding binding) => _binding = binding;

    public void Begin(AlsFrameIdentity identity, ReadOnlySpan<AlsMontageTraversal> traversal,
        bool dedicatedServer = false, int predictedLod = 0)
    {
        if (_phase != Phase.Idle || identity.SlotGeneration == 0 || identity.FrameId < 0 ||
            Committed.Identity != default && (identity.CharacterId != Committed.Identity.CharacterId ||
                identity.SlotGeneration != Committed.Identity.SlotGeneration || identity.FrameId <= Committed.Identity.FrameId))
            throw new ArgumentException("Invalid montage notify frame.");
        _phase = Phase.Preparing;
        _directCount = _visibleCount = _slotCount = _total = 0; Array.Clear(_slotCounts);
        var seed = Committed.Identity == default ? AlsTimelineRuntime.InitialAssetNotifyRandomSeed : Committed.RandomSeed;
        Span<AlsAssetNotifyOccurrence> extracted = stackalloc AlsAssetNotifyOccurrence[Capacity];
        Span<AlsAssetNotifyReference> current = stackalloc AlsAssetNotifyReference[Capacity];
        Span<AlsAssetNotifyReference> scratch = stackalloc AlsAssetNotifyReference[Capacity];
        Span<AlsAssetNotifyReference> incoming = stackalloc AlsAssetNotifyReference[1];
        try
        {
            foreach (var tick in traversal)
            {
                if (tick.Interrupted) continue;
                if (tick.InstanceId <= 0 || (uint)tick.Slot.Id > 3 || !float.IsFinite(tick.NotifyWeight) || tick.NotifyWeight < 0)
                    throw new ArgumentException("Invalid montage notify traversal.");
                var found = false;
                // Each instance extracts Montage-owned notifies before its sequence track.
                for (var pass = 0; pass < 2; pass++)
                foreach (var range in _binding.Ranges)
                {
                    if (range.ActionDefinitionId != tick.ActionDefinitionId || range.AnimationId != tick.AnimationId ||
                        range.Slot != tick.Slot || range.Direct != (pass == 0)) continue;
                    found = true;
                    if (!range.Direct)
                    {
                        var seen = false;
                        for (var i=0;i<_slotCount;i++) if (_slotOrder[i]==tick.Slot.Id) seen=true;
                        if (!seen) _slotOrder[_slotCount++]=tick.Slot.Id;
                    }
                    var previous = range.ClipStart + tick.PreviousPosition * range.ClipRate;
                    var currentTime = range.ClipStart + tick.CurrentPosition * range.ClipRate;
                    if (!AlsTimelineRuntime.TryExtractAssetNotifies(_binding.Definitions.Slice(range.Offset,range.Count),
                        range.Duration, previous, currentTime-previous, false, extracted, out var count, out var failure))
                        throw new InvalidOperationException($"Montage notify extraction: {failure}.");
                    var destination = range.Direct ? _direct.AsSpan() : _slots.AsSpan(tick.Slot.Id*Capacity,Capacity);
                    var destinationCount = range.Direct ? _directCount : _slotCounts[tick.Slot.Id];
                    for (var n=0;n<destinationCount;n++) current[n]=destination[n].Reference;
                    for (var n=0;n<count;n++)
                    {
                        var index = _binding.SourcePolicyCount + range.Offset + extracted[n].DefinitionIndex;
                        incoming[0]=new(index,range.Handle,tick.CurrentPosition,true,extracted[n].ReachedEnd);
                        if (!AlsTimelineRuntime.TryQueueAssetNotifies(_binding.Policies,current[..destinationCount],incoming,
                            new(true,dedicatedServer,predictedLod,tick.NotifyWeight),AlsAssetNotifyQueueMode.Filtered,seed,
                            scratch,current,out var added,out seed,out failure))
                            throw new InvalidOperationException($"Montage notify queue: {failure}.");
                        if (added==destinationCount) continue;
                        if (++_total>Capacity) throw new InvalidOperationException("Montage notify queue: EventBufferOverflow.");
                        var policy = _binding.Policies[index];
                        if (!_binding.TryTimeline(incoming[0],out var timeline)) throw new InvalidOperationException("Missing montage notify timeline.");
                        destination[destinationCount]=new(incoming[0],AlsAssetNotifySourceKind.Montage,unchecked((uint)tick.InstanceId),
                            (policy.StateBehaviorFlags & 1)!=0,timeline.DurationSeconds,tick.InstanceId,tick.NotifyWeight);
                        destinationCount=added;
                    }
                    if (range.Direct) _directCount=destinationCount; else _slotCounts[tick.Slot.Id]=destinationCount;
                }
                if (!found) throw new ArgumentException("Unbound montage notify traversal.");
            }
            Candidate = new(identity,seed,0); _phase=Phase.Prepared;
        }
        catch { _phase=Phase.Faulted; throw; }
    }

    public void Complete(byte relevantSlots)
    {
        if (_phase!=Phase.Prepared || relevantSlots>15) throw new InvalidOperationException("Montage notify relevance differs.");
        Candidate=Candidate with {RelevantSlots=relevantSlots};
        foreach(var slot in _slotOrder.AsSpan(0,_slotCount))
        {
            if (((relevantSlots|Committed.RelevantSlots)&(1<<slot))==0) continue;
            _slots.AsSpan(slot*Capacity,_slotCounts[slot]).CopyTo(_visible.AsSpan(_visibleCount));
            _visibleCount+=_slotCounts[slot];
        }
        _phase=Phase.Complete;
    }
    public void ValidateCommit(AlsFrameIdentity identity)
    { RequireComplete(); if(identity!=Candidate.Identity)throw new InvalidOperationException("Montage notify commit differs."); }
    public void Commit(AlsFrameIdentity identity) { ValidateCommit(identity); Committed=Candidate; Discard(); }
    public void Discard() { _phase=Phase.Idle; Candidate=default; _directCount=_visibleCount=_slotCount=_total=0; }
    private void RequireComplete() { if(_phase!=Phase.Complete)throw new InvalidOperationException("Montage notifies are not complete."); }
}
