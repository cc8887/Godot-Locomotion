using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Events;

public readonly record struct AlsMontageNotifyRange(int ActionDefinitionId, int AnimationId, AlsMontageSlot Slot,
    int Handle, int Offset, int Count, float Duration, float ClipStart, float ClipRate, bool Direct);

// Definitions are indexed without the source-policy prefix; runtime references include it.
public sealed class AlsMontageNotifyBinding : IAlsMontageNotifyBinding
{
    private readonly AlsAssetNotifyPolicy[] _policies;
    private readonly AlsAssetNotifyDefinition[] _definitions;
    private readonly AlsTimelineEventDefinition[] _timeline;
    private readonly AlsMontageNotifyRange[] _ranges;
    public int SourcePolicyCount { get; }
    public ReadOnlySpan<AlsAssetNotifyPolicy> Policies => _policies;
    public ReadOnlySpan<AlsAssetNotifyDefinition> Definitions => _definitions;
    public ReadOnlySpan<AlsMontageNotifyRange> Ranges => _ranges;

    public AlsMontageNotifyBinding(ReadOnlySpan<AlsAssetNotifyPolicy> sourcePolicies,
        ReadOnlySpan<AlsAssetNotifyPolicy> policies, ReadOnlySpan<AlsAssetNotifyDefinition> definitions,
        ReadOnlySpan<AlsTimelineEventDefinition> timeline, ReadOnlySpan<AlsMontageNotifyRange> ranges)
    {
        if (policies.Length != definitions.Length || timeline.Length != definitions.Length)
            throw new ArgumentException("Montage notify tables differ.");
        SourcePolicyCount = sourcePolicies.Length;
        _policies = new AlsAssetNotifyPolicy[sourcePolicies.Length + policies.Length];
        sourcePolicies.CopyTo(_policies); policies.CopyTo(_policies.AsSpan(sourcePolicies.Length));
        _definitions = definitions.ToArray(); _timeline = timeline.ToArray(); _ranges = ranges.ToArray();
        var end = 0; var handles = new HashSet<int>(); var keys = new HashSet<(int,int,AlsMontageSlot,bool)>();
        foreach (var range in _ranges)
        {
            if (range.ActionDefinitionId < -1 || range.AnimationId < 0 || (uint)range.Slot.Id > 4 ||
                range.Handle < 0 || !handles.Add(range.Handle) || !keys.Add((range.ActionDefinitionId,range.AnimationId,range.Slot,range.Direct)) ||
                range.Offset != end || range.Count < 0 || range.Count > timeline.Length - end ||
                !float.IsFinite(range.Duration) || range.Duration <= 0 || !float.IsFinite(range.ClipStart) ||
                range.ClipStart < 0 || range.ClipStart >= range.Duration || !float.IsFinite(range.ClipRate) || range.ClipRate <= 0 ||
                range.Direct && (range.ActionDefinitionId < 0 || range.ClipStart != 0 || range.ClipRate != 1))
                throw new ArgumentException("Invalid montage notify range.");
            for (var i = end; i < end + range.Count; i++)
            {
                var p = policies[i]; var t = timeline[i]; var d = definitions[i];
                if (p.TickMode != AlsTimelineTickMode.Queued || t.TickMode != AlsTimelineTickMode.Queued ||
                    (p.NotifyObjectId >= 0) == (p.StateObjectId >= 0) ||
                    p.EventId != d.EventId || p.EventId != t.EventId || p.SourceIndex != t.SourceIndex ||
                    t.RequiredOccurrenceHandleId != range.Handle || t.SourceActionId != range.ActionDefinitionId ||
                    t.SourceKind != (range.Direct ? AlsTimelineSourceKind.Montage : AlsTimelineSourceKind.MontageSegmentAnimation) ||
                    !range.Direct && t.SourceAnimationId != range.AnimationId ||
                    !float.IsFinite(t.DurationSeconds) || t.DurationSeconds < 0 || p.StateObjectId < 0 && t.DurationSeconds != 0 ||
                    !float.IsFinite(d.TriggerTimeSeconds) || !float.IsFinite(d.EndTriggerTimeSeconds) || d.EndTriggerTimeSeconds < d.TriggerTimeSeconds)
                    throw new ArgumentException("Montage notify provenance or queue mode differs.");
            }
            end += range.Count;
        }
        if (end != timeline.Length) throw new ArgumentException("Unbound montage notify.");
    }

    public bool TryTimeline(in AlsAssetNotifyReference reference, out AlsTimelineEventDefinition definition)
    {
        var index = reference.PolicyIndex - SourcePolicyCount;
        definition = (uint)index < _timeline.Length ? _timeline[index] : default;
        return (uint)index < _timeline.Length && definition.RequiredOccurrenceHandleId == reference.OccurrenceHandleId;
    }
}
