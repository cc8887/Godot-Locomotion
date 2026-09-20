using System.Runtime.InteropServices;

namespace GodotAls.Core.Contracts;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsP5SourceOccurrenceMapping(int PlayerId, int SampleId, int CompiledNodeIndex,
    int SourceIndex, int AnimationId, int OccurrenceHandleId);

public readonly ref struct AlsP5SourceOccurrenceView(int version, ulong digest, AlsLocomotionSourceStamp sourceStamp,
    ReadOnlySpan<AlsP5OccurrenceLayoutEntry> entries, ReadOnlySpan<AlsP5SourceOccurrenceMapping> mappings)
{
    public readonly int Version = version;
    public readonly ulong Digest = digest;
    public readonly AlsLocomotionSourceStamp SourceStamp = sourceStamp;
    public readonly ReadOnlySpan<AlsP5OccurrenceLayoutEntry> Entries = entries;
    public readonly ReadOnlySpan<AlsP5SourceOccurrenceMapping> Mappings = mappings;
}

public static class AlsP5SourceOccurrenceContract
{
    /// <summary>Initialization-time exact source ownership check. Authority is per source player,
    /// not per animation or Sync group. Evaluators have identities but no ticking/notify permission.</summary>
    public static void Validate(in AlsP5SourceOccurrenceView layout, in AlsLocomotionSourceView source)
    {
        if (layout.Version != AlsP5OccurrenceLayoutContract.SourceGraphVersion || !source.Stamp.IsValid ||
            layout.SourceStamp != source.Stamp || layout.Mappings.Length != source.Samples.Length)
            throw new ArgumentException("Source occurrence layout does not match its source snapshot.");
        AlsP5OccurrenceLayoutContract.Validate(layout.Version, layout.Digest, layout.Entries);
        var sourceEntries = 0;
        foreach (var entry in layout.Entries)
            if (entry.SourceKind is AlsP5OccurrenceSourceKind.SourceSample or AlsP5OccurrenceSourceKind.SourceEvaluator) sourceEntries++;
        if (sourceEntries != layout.Mappings.Length) throw new ArgumentException("Source occurrence coverage is incomplete.");
        for (var i = 0; i < layout.Mappings.Length; i++)
        {
            var map = layout.Mappings[i]; var sample = source.Samples[i];
            if (map.SampleId != i || sample.SampleId != i || map.PlayerId != sample.PlayerId ||
                (uint)map.PlayerId >= source.Players.Length || (uint)map.OccurrenceHandleId >= layout.Entries.Length)
                throw new ArgumentException("Source occurrence sample ownership is invalid.");
            var player = source.Players[map.PlayerId]; var entry = layout.Entries[map.OccurrenceHandleId];
            var kind = player.Kind == AlsLocomotionSourceKind.TeleportEvaluator ? AlsP5OccurrenceSourceKind.SourceEvaluator : AlsP5OccurrenceSourceKind.SourceSample;
            if (player.PlayerId != map.PlayerId || map.CompiledNodeIndex != player.CompiledNodeIndex ||
                map.SourceIndex != sample.SourceIndex || map.AnimationId != sample.AnimationId ||
                entry.SourceKind != kind || entry.SourceBindingIndex != map.CompiledNodeIndex || entry.GraphSlotIndex != map.SourceIndex)
                throw new ArgumentException("Source occurrence does not resolve to its exact compiled node and sample.");
            for (var j = 0; j < i; j++)
            {
                var prior = layout.Mappings[j]; var priorEntry = layout.Entries[prior.OccurrenceHandleId];
                if (map.OccurrenceHandleId == prior.OccurrenceHandleId ||
                    (map.PlayerId == prior.PlayerId) != (entry.AuthorityGroupId == priorEntry.AuthorityGroupId))
                    throw new ArgumentException("Source players or samples have aliased occurrence authority.");
            }
            foreach (var other in layout.Entries)
                if (other.SourceKind < AlsP5OccurrenceSourceKind.SourceSample && other.AuthorityGroupId == entry.AuthorityGroupId)
                    throw new ArgumentException("Source authority overlaps a legacy or action lane.");
        }
    }
}
