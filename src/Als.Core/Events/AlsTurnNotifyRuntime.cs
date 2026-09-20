using GodotAls.Core.Actions;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Events;

public readonly record struct AlsTurnNotifyAsset(AlsDynamicMontageAsset Asset, int Handle, int Offset, int Count);

// Source policy indices remain stable. Imported object/name IDs share a namespace
// with source notifies. Only the eight instant-notify Turn sequences are supported.
public sealed class AlsTurnNotifyBinding : IAlsMontageNotifyBinding
{
    private readonly AlsAssetNotifyPolicy[] _policies;
    private readonly AlsAssetNotifyDefinition[] _definitions;
    private readonly AlsTimelineEventDefinition[] _timeline;
    private readonly AlsTurnNotifyAsset[] _assets;
    public int SourcePolicyCount { get; }
    public ReadOnlySpan<AlsAssetNotifyPolicy> Policies => _policies;
    public ReadOnlySpan<AlsAssetNotifyDefinition> Definitions => _definitions;
    public ReadOnlySpan<AlsTurnNotifyAsset> Assets => _assets;

    public AlsTurnNotifyBinding(ReadOnlySpan<AlsAssetNotifyPolicy> sourcePolicies,
        ReadOnlySpan<AlsAssetNotifyPolicy> turnPolicies, ReadOnlySpan<AlsAssetNotifyDefinition> definitions,
        ReadOnlySpan<AlsTimelineEventDefinition> timeline, ReadOnlySpan<AlsTurnNotifyAsset> assets)
    {
        if (turnPolicies.Length != definitions.Length || timeline.Length != definitions.Length)
            throw new ArgumentException("Turn notify tables differ.");
        SourcePolicyCount = sourcePolicies.Length;
        _policies = new AlsAssetNotifyPolicy[sourcePolicies.Length + turnPolicies.Length];
        sourcePolicies.CopyTo(_policies); turnPolicies.CopyTo(_policies.AsSpan(sourcePolicies.Length));
        _definitions = definitions.ToArray(); _timeline = timeline.ToArray(); _assets = assets.ToArray();
        var end = 0; var handles = new HashSet<int>(); var ids = new HashSet<int>();
        foreach (var asset in _assets)
        {
            if (asset.Asset.AnimationId < 0 || (uint)asset.Asset.Slot > 1 || !float.IsFinite(asset.Asset.Duration) ||
                asset.Asset.Duration <= 0 || asset.Handle < 0 || !handles.Add(asset.Handle) || !ids.Add(asset.Asset.AnimationId) ||
                asset.Offset != end || asset.Count < 0 || asset.Count > timeline.Length - end)
                throw new ArgumentException("Invalid turn notify range.");
            for (var i = end; i < end + asset.Count; i++)
                if (turnPolicies[i].StateObjectId >= 0 || turnPolicies[i].NotifyObjectId < 0 ||
                    turnPolicies[i].TickMode != AlsTimelineTickMode.Queued || timeline[i].DurationSeconds != 0 ||
                    definitions[i].EventId != turnPolicies[i].EventId || timeline[i].EventId != turnPolicies[i].EventId ||
                    timeline[i].SourceAnimationId != asset.Asset.AnimationId || timeline[i].RequiredOccurrenceHandleId != asset.Handle)
                    throw new ArgumentException("Turn requires a queued instant notify with matching provenance.");
            end += asset.Count;
        }
        if (end != timeline.Length) throw new ArgumentException("Unbound turn notify.");
    }

    public bool TryTimeline(in AlsAssetNotifyReference reference, out AlsTimelineEventDefinition definition)
    {
        var index = reference.PolicyIndex - SourcePolicyCount;
        definition = (uint)index < _timeline.Length ? _timeline[index] : default;
        return (uint)index < _timeline.Length && definition.RequiredOccurrenceHandleId == reference.OccurrenceHandleId;
    }
}

public readonly record struct AlsTurnNotifyState(AlsFrameIdentity Identity, uint RandomSeed,
    bool StandingRelevant, bool CrouchingRelevant);

// UE's AnimInstance montage queue has its own RNG, separate from the proxy's
// asset-player queue. Both queues share the final callback allocator, not the RNG.
public sealed class AlsTurnNotifyRuntime
{
    private readonly AlsTurnNotifyBinding _binding;
    private readonly AlsAssetNotifyDispatchInput[] _filtered = new AlsAssetNotifyDispatchInput[AlsP5Runtime.SourceNotifyReferenceCapacity];
    private readonly AlsMontageSlot[] _slots = new AlsMontageSlot[AlsP5Runtime.SourceNotifyReferenceCapacity];
    private readonly AlsAssetNotifyDispatchInput[] _visible = new AlsAssetNotifyDispatchInput[AlsP5Runtime.SourceNotifyReferenceCapacity];
    private readonly AlsMontageSlot[] _slotOrder = new AlsMontageSlot[2];
    private int _count, _visibleCount, _slotCount;
    private bool _prepared, _completed;
    public AlsTurnNotifyState Committed { get; private set; }
    public AlsTurnNotifyState Candidate { get; private set; }
    public int FilteredCount => _count;
    public ReadOnlySpan<AlsAssetNotifyDispatchInput> Notifies
    { get { if (!_completed) throw new InvalidOperationException("Turn slot relevance is not complete."); return _visible.AsSpan(0, _visibleCount); } }

    public AlsTurnNotifyRuntime(AlsTurnNotifyBinding binding) { _binding = binding; }

    public void Begin(AlsFrameIdentity identity, ReadOnlySpan<AlsMontageTraversal> traversal,
        bool dedicatedServer = false, int predictedLod = 0)
    {
        if (_prepared || identity.SlotGeneration == 0 || identity.FrameId < 0 ||
            Committed.Identity != default && (identity.CharacterId != Committed.Identity.CharacterId ||
                identity.SlotGeneration != Committed.Identity.SlotGeneration || identity.FrameId <= Committed.Identity.FrameId))
            throw new ArgumentException("Invalid turn notify frame.");
        _count = _visibleCount = _slotCount = 0; _completed = false;
        var seed = Committed.Identity == default ? AlsTimelineRuntime.InitialAssetNotifyRandomSeed : Committed.RandomSeed;
        Span<AlsAssetNotifyOccurrence> extracted = stackalloc AlsAssetNotifyOccurrence[AlsP5Runtime.SourceNotifyReferenceCapacity];
        Span<AlsAssetNotifyReference> incoming = stackalloc AlsAssetNotifyReference[AlsP5Runtime.SourceNotifyReferenceCapacity];
        Span<AlsAssetNotifyReference> scratch = stackalloc AlsAssetNotifyReference[AlsP5Runtime.SourceNotifyReferenceCapacity];
        Span<AlsAssetNotifyReference> accepted = stackalloc AlsAssetNotifyReference[AlsP5Runtime.SourceNotifyReferenceCapacity];
        foreach (var tick in traversal)
        {
            // Play() later in this frame must not retroactively suppress already gathered events.
            if (tick.Interrupted) continue;
            var found = false; AlsTurnNotifyAsset asset = default;
            foreach (var item in _binding.Assets)
                if (item.Asset.AnimationId == tick.AnimationId && item.Asset.Slot == tick.Slot) { asset = item; found = true; break; }
            if (!found || tick.InstanceId <= 0) throw new ArgumentException("Unbound montage notify traversal.");
            var seenSlot = false;
            for (var i = 0; i < _slotCount; i++) if (_slotOrder[i] == tick.Slot) seenSlot = true;
            if (!seenSlot) _slotOrder[_slotCount++] = tick.Slot;
            if (!AlsTimelineRuntime.TryExtractAssetNotifies(_binding.Definitions.Slice(asset.Offset, asset.Count),
                asset.Asset.Duration, tick.PreviousPosition, tick.CurrentPosition - tick.PreviousPosition, false,
                extracted, out var count, out var failure)) throw new InvalidOperationException($"Turn notify extraction: {failure}.");
            for (var i = 0; i < count; i++) incoming[i] = new(_binding.SourcePolicyCount + asset.Offset + extracted[i].DefinitionIndex,
                asset.Handle, tick.CurrentPosition, true, extracted[i].ReachedEnd);
            if (!AlsTimelineRuntime.TryQueueAssetNotifies(_binding.Policies, [], incoming[..count],
                new(true, dedicatedServer, predictedLod, tick.NotifyWeight), AlsAssetNotifyQueueMode.Filtered, seed,
                scratch, accepted, out var added, out seed, out failure))
                throw new InvalidOperationException($"Turn notify queue: {failure}.");
            if (added > _filtered.Length - _count)
                throw new InvalidOperationException("Turn notify queue: EventBufferOverflow.");
            for (var i = 0; i < added; i++)
            {
                // PlaybackEpoch carries the full monotonic montage identity; SourceInstanceId
                // is only used by the general lifecycle's concurrent-state matching.
                _filtered[_count] = new(accepted[i], AlsAssetNotifySourceKind.Montage, unchecked((uint)tick.InstanceId), false,
                    0, tick.InstanceId, tick.NotifyWeight);
                _slots[_count++] = tick.Slot;
            }
        }
        Candidate = new(identity, seed, false, false); _prepared = true;
    }

    public void Complete(bool standingRelevant, bool crouchingRelevant)
    {
        if (!_prepared || _completed) throw new InvalidOperationException("Turn notify phase differs.");
        Candidate = Candidate with { StandingRelevant = standingRelevant, CrouchingRelevant = crouchingRelevant };
        foreach (var slot in _slotOrder.AsSpan(0, _slotCount))
        {
            var relevant = slot == AlsTurnSlot.Standing ? standingRelevant || Committed.StandingRelevant :
                crouchingRelevant || Committed.CrouchingRelevant;
            if (!relevant) continue;
            for (var i = 0; i < _count; i++) if (_slots[i] == slot) _visible[_visibleCount++] = _filtered[i];
        }
        _completed = true;
    }
    public void ValidateCommit(AlsFrameIdentity identity)
    { if (!_completed || identity != Candidate.Identity) throw new InvalidOperationException("Turn notify commit differs."); }
    public void Commit(AlsFrameIdentity identity) { ValidateCommit(identity); Committed = Candidate; Discard(); }
    public void Discard() { _prepared = _completed = false; _count = _visibleCount = _slotCount = 0; Candidate = default; }
}
