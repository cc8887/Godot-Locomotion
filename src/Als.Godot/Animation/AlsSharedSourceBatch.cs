using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation;

internal interface IAlsSharedSourceContributor
{
    float? FlailRate => null;
    void Collect(in AlsFrameIdentity identity, ref AlsCycleSyncFrame candidate,
        Span<AlsLocomotionSourceUpdate> players, Span<AlsLocomotionSampleUpdate> samples, Span<bool> active,
        ref int playerCount, ref int sampleCount);
    void Complete(in AlsFrameIdentity identity, in AlsCycleSyncFrame synchronized);
}

// One transaction-wide tick after every participating graph has collected its sources.
// Initial time/epoch and notify activity are supplied by the graph owners, not inferred here.
internal static class AlsSharedSourceBatch
{
    public static void Validate(in AlsP5RuntimeBindings binding, in AlsCycleSyncFrame candidate)
    {
        var source = binding.Sources;
        if (source.Players.Length > AlsCycleSyncFrame.PlayerCapacity || source.Samples.Length > AlsCycleSyncFrame.SampleCapacity ||
            source.GroupIds.Length > AlsCycleSyncFrame.GroupCapacity || (uint)candidate.PlayerCount > AlsCycleSyncFrame.PlayerCapacity ||
            (uint)candidate.SampleCount > AlsCycleSyncFrame.SampleCapacity || (uint)candidate.GroupCount > AlsCycleSyncFrame.GroupCapacity ||
            candidate.Initialized && (candidate.BindingStamp != source.Stamp || candidate.BindingDigest != binding.Digest ||
                candidate.LayoutDigest != binding.LayoutDigest || candidate.GroupCount != source.GroupIds.Length))
            throw new InvalidOperationException($"Source history belongs to another binding snapshot or exceeds transaction capacity: " +
                $"binding={source.Players.Length}/{source.Samples.Length}/{source.GroupIds.Length} " +
                $"candidate={candidate.PlayerCount}/{candidate.SampleCount}/{candidate.GroupCount} initialized={candidate.Initialized}.");
    }

    public static AlsCycleSyncFrame Evaluate(in AlsP5RuntimeBindings binding, in AlsCycleSyncFrame candidate,
        ReadOnlySpan<AlsLocomotionSourceUpdate> updates, ReadOnlySpan<AlsLocomotionSampleUpdate> samples,
        ReadOnlySpan<bool> active, float standingRate, in AlsSourceRotationInput rotation, float? crouchingRate, float delta, float? jumpRate = null,
        float? flailRate = null)
    {
        Validate(binding, candidate);
        if (active.Length != updates.Length) throw new ArgumentException("Source activity must match the collected player order.");
        var source = binding.Sources;
        foreach (var update in updates)
            if ((uint)update.PlayerId >= source.Players.Length || candidate.Epochs[update.PlayerId] != update.Epoch ||
                candidate.Times[update.PlayerId] != update.Time)
                throw new ArgumentException("Source contribution disagrees with its candidate time or epoch.");
        Span<AlsAssetSyncPlayer> ticks = stackalloc AlsAssetSyncPlayer[AlsCycleSyncFrame.PlayerCapacity];
        Span<AlsAssetSyncSample> sampleTicks = stackalloc AlsAssetSyncSample[AlsCycleSyncFrame.SampleCapacity];
        Span<int> groups = stackalloc int[AlsCycleSyncFrame.PlayerCapacity];
        if (!AlsLocomotionSourceRuntime.TryBuildTicks(source, source.Stamp, updates, samples, standingRate,
            ticks, sampleTicks, groups, out var failure, rotation, crouchingRate, jumpRate, flailRate))
            throw new InvalidOperationException($"Shared source binding failed: {failure}");
        Span<AlsAssetPlayerHistory> players = stackalloc AlsAssetPlayerHistory[AlsCycleSyncFrame.PlayerCapacity];
        Span<AlsAssetSampleHistory> sampleHistory = stackalloc AlsAssetSampleHistory[AlsCycleSyncFrame.SampleCapacity];
        Span<AlsAssetSyncBatchGroupHistory> nextGroups = stackalloc AlsAssetSyncBatchGroupHistory[AlsCycleSyncFrame.GroupCapacity];
        Span<AlsAssetPlayerTickContext> contexts = stackalloc AlsAssetPlayerTickContext[AlsCycleSyncFrame.PlayerCapacity];
        if (!AlsSyncRuntime.TryEvaluateAssetSyncBatch(source.GroupIds, groups[..updates.Length], ticks[..updates.Length],
            sampleTicks[..samples.Length], source.Sequences, source.Markers,
            ((ReadOnlySpan<AlsAssetSyncBatchGroupHistory>)candidate.Groups)[..candidate.GroupCount],
            ((ReadOnlySpan<AlsAssetPlayerHistory>)candidate.Players)[..candidate.PlayerCount],
            ((ReadOnlySpan<AlsAssetSampleHistory>)candidate.Samples)[..candidate.SampleCount], delta,
            nextGroups, players, sampleHistory, out failure, contexts[..updates.Length]))
            throw new InvalidOperationException($"Shared asset sync failed: {failure}");
        var result = candidate;
        Span<AlsP5SourceNotifyTick> notifies = result.NotifyTicks; notifies.Clear();
        if (!AlsP5Runtime.TryBuildSourceNotifyTicks(binding, ticks[..updates.Length], sampleTicks[..samples.Length],
            players[..updates.Length], sampleHistory[..samples.Length], contexts[..updates.Length],
            notifies, out result.NotifyTickCount, out failure))
            throw new InvalidOperationException($"Shared source notify mapping failed: {failure}");
        for (var i = 0; i < result.NotifyTickCount; i++)
        {
            var playerId = source.Samples[notifies[i].SampleId].PlayerId;
            var found = false;
            for (var n = 0; n < updates.Length; n++)
                if (updates[n].PlayerId == playerId)
                { notifies[i] = notifies[i] with { ActiveContext = active[n] }; found = true; break; }
            if (!found) throw new InvalidOperationException("Notify source has no collected activity context.");
        }
        for (var i = 0; i < updates.Length; i++) result.Times[players[i].PlayerId] = players[i].Time;
        Span<AlsAssetPlayerHistory> outputPlayers = result.Players; outputPlayers.Clear();
        Span<AlsAssetSampleHistory> outputSamples = result.Samples; outputSamples.Clear();
        Span<AlsAssetSyncBatchGroupHistory> outputGroups = result.Groups; outputGroups.Clear();
        players[..updates.Length].CopyTo(outputPlayers); sampleHistory[..samples.Length].CopyTo(outputSamples);
        nextGroups[..source.GroupIds.Length].CopyTo(outputGroups);
        // Keep the legacy selected-group view consistent, including empty batches.
        var selectedGroup = source.GroupIds.IndexOf(candidate.Group.GroupId);
        result.Group = selectedGroup >= 0 ? nextGroups[selectedGroup].Group : default;
        result.PlayerCount = updates.Length; result.SampleCount = samples.Length; result.GroupCount = source.GroupIds.Length;
        result.Initialized = true; result.BindingStamp = source.Stamp; result.BindingDigest = binding.Digest;
        result.LayoutDigest = binding.LayoutDigest;
        return result;
    }
}
