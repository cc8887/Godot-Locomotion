using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Import.Compilation;

/// <summary>Original Sync ranges enter the existing character queue. This
/// immutable adapter owns neither time, RNG nor notification state lifetime.</summary>
public sealed class AlsRefactoredCharacterNotifyBinding : IAlsMontageNotifyBinding
{
    private readonly AlsMontageNotifyBinding _montages;
    private readonly AlsRefactoredNotifyBank _sources;
    private readonly AlsAssetNotifyPolicy[] _policies;
    private readonly AlsTimelineEventDefinition[] _timelines;
    private readonly string?[] _paths;
    private readonly int _eventStart;
    private readonly int _offset, _handleStart, _sampleCapacity;
    public int SourcePolicyCount => _montages.SourcePolicyCount;
    public ReadOnlySpan<AlsAssetNotifyPolicy> Policies => _policies;
    public AlsRefactoredNotifyBank Sources => _sources;

    public AlsRefactoredCharacterNotifyBinding(AlsMontageNotifyBinding montages,
        AlsRefactoredNotifyBank sources, int firstSourceHandle, int sampleCapacity)
    {
        if (firstSourceHandle < 0 || sampleCapacity <= 0 || montages.Ranges.ToArray().Any(r => r.Handle >= firstSourceHandle))
            throw new ArgumentException("Overlapping additional source handle namespace.");
        _montages = montages; _sources = sources; _handleStart = firstSourceHandle;
        _sampleCapacity = sampleCapacity; _offset = montages.Policies.Length;
        _policies = new AlsAssetNotifyPolicy[_offset + sources.Policies.Length];
        montages.Policies.CopyTo(_policies);
        var eventStart = _eventStart = _policies.Take(_offset).Select(p => p.EventId).DefaultIfEmpty(-1).Max() + 1;
        var objectStart = _policies.Take(_offset).SelectMany(p => new[] { p.NotifyObjectId, p.StateObjectId }).DefaultIfEmpty(-1).Max() + 1;
        var nameStart = _policies.Take(_offset).Select(p => p.NameId).DefaultIfEmpty(-1).Max() + 1;
        for (var i = 0; i < sources.Policies.Length; i++)
        {
            var p = sources.Policies[i];
            _policies[_offset + i] = p with { EventId = checked(eventStart + p.EventId),
                NotifyObjectId = p.NotifyObjectId < 0 ? -1 : checked(objectStart + p.NotifyObjectId),
                StateObjectId = p.StateObjectId < 0 ? -1 : checked(objectStart + p.StateObjectId),
                NameId = checked(nameStart + p.NameId) };
        }
        _timelines = new AlsTimelineEventDefinition[sources.Events.Length];
        _paths = new string?[sources.Events.Length];
        foreach (var pair in sources.Assets.Where(p => !p.Value.Montage))
        {
            var asset=pair.Value;
            for (var i = asset.Offset; i < asset.Offset + asset.Count; i++)
            {
                _paths[i]=pair.Key;
                var e = sources.Events[i]; var p = _policies[_offset + i];
                var (kind, payload) = Payload(e.Payload, p.EventId);
                _timelines[i] = new(p.EventId, asset.AssetId, -1, -1,
                    AlsTimelineSourceKind.Animation, p.SourceIndex, p.TrackIndex, p.SourceIndex,
                    e.Time, e.Duration, p.WeightThreshold, kind, AlsTimelineTickMode.Queued, payload);
            }
        }
    }

    // Effect consumers recover the full authored payload (pitch, sound type,
    // placement and curves) using the event identity, without a second clock.
    public bool TryPayload(int eventId, out AlsRefactoredNotifyPayload? payload)
    {
        var index = eventId - _eventStart; payload = null;
        if ((uint)index >= _timelines.Length || _timelines[index].RequiredOccurrenceHandleId != -1) return false;
        payload = _sources.Events[index].Payload; return true;
    }

    // Original catalog IDs are not V4 animation-set IDs. Effect consumers use
    // the shifted event identity to resolve the original asset explicitly.
    public bool TrySourceAssetPath(int eventId, out string? path)
    {
        var index=eventId-_eventStart;path=null;
        if((uint)index>=_paths.Length||_paths[index] is null)return false;
        path=_paths[index];return true;
    }

    public bool TryTimeline(in AlsAssetNotifyReference reference, out AlsTimelineEventDefinition definition)
    {
        if (reference.PolicyIndex < _offset) return _montages.TryTimeline(reference, out definition);
        definition = default;
        var index = reference.PolicyIndex - _offset;
        if ((uint)index >= _sources.Events.Length || reference.OccurrenceHandleId < _handleStart ||
            reference.OccurrenceHandleId - _handleStart >= _sampleCapacity) return false;
        // Unfilled entries belong to physical Montages, never this source owner.
        if (_timelines[index].RequiredOccurrenceHandleId != -1) return false;
        definition = _timelines[index] with { RequiredOccurrenceHandleId = reference.OccurrenceHandleId };
        return true;
    }

    public int Extract(AlsRefactoredSourceSyncScope scope, Span<AlsAdditionalSourceNotify> destination)
    {
        Span<AlsRefactoredSourceNotifyTick> ticks = stackalloc AlsRefactoredSourceNotifyTick[_sampleCapacity];
        Span<AlsAssetNotifyOccurrence> occurrences = stackalloc AlsAssetNotifyOccurrence[AlsP5Runtime.SourceNotifyReferenceCapacity];
        Span<AlsAdditionalSourceNotify> staged = stackalloc AlsAdditionalSourceNotify[AlsP5Runtime.SourceNotifyReferenceCapacity];
        var count = 0;
        foreach (var tick in ticks[..scope.BuildNotifyTicks(ticks)])
        {
            if ((uint)tick.Playback.SampleId >= _sampleCapacity) throw new ArgumentException("Foreign original sample identity.");
            var found = scope.Extract(tick, occurrences);
            for (var i = 0; i < found; i++)
            {
                if (count == staged.Length) throw new InvalidOperationException("Original notify reference capacity exceeded.");
                var index = occurrences[i].DefinitionIndex;
                var reference = new AlsAssetNotifyReference(_offset + index, _handleStart + tick.Playback.SampleId,
                    tick.ContextTime, tick.ActiveContext, occurrences[i].ReachedEnd, ScopeFiltered: tick.ScopeFiltered);
                var policy = _sources.Policies[index];
                staged[count++] = new(new(reference, AlsAssetNotifySourceKind.AssetPlayer,
                    (uint)(_handleStart + tick.Playback.PlayerId), (policy.StateBehaviorFlags & 1) != 0,
                    _sources.Events[index].Duration, tick.Playback.Epoch, tick.Weight), tick.Leader);
            }
        }
        if (count > destination.Length) throw new ArgumentException("Original notify output capacity exceeded.");
        staged[..count].CopyTo(destination); return count;
    }

    private static (AlsTimelineEventKind, AlsCompactEventPayload) Payload(AlsRefactoredNotifyPayload source, int semantic)
    {
        var p = new AlsCompactEventPayload(semantic, 0, 0, 0, 0, 0, AlsActionResultCode.None);
        return source switch
        {
            AlsRefactoredFootstepPayload foot => (AlsTimelineEventKind.Footstep,
                p with { EnumValue0 = (int)foot.Foot, ScalarValue0 = foot.Volume,
                    Flags = (ushort)((foot.SkipInAir ? 1 : 0) | (foot.SpawnSound ? 2 : 0) | (foot.SpawnDecal ? 4 : 0) | (foot.SpawnParticles ? 8 : 0)) }),
            AlsRefactoredGroundedEntryPayload entry when entry.Mode == "Als.GroundedEntryMode.FromRoll" =>
                (AlsTimelineEventKind.SetGroundedEntry, p with { EnumValue0 = (int)AlsTimelineGroundedEntryMode.FromRoll }),
            AlsRefactoredRootScalePayload scale => (AlsTimelineEventKind.RootMotionScale, p with { ScalarValue0 = scale.Scale }),
            // The authored objects retain all effect fields. Physical Montage
            // Action/EarlyBlend branching continues in its existing bank.
            AlsRefactoredCameraShakePayload shake => (AlsTimelineEventKind.Generic, p with { ScalarValue0 = shake.Scale }),
            _ => throw new ArgumentException("Unbound original sequence notify payload: " + source)
        };
    }
}
