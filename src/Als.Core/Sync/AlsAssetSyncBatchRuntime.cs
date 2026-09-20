using GodotAls.Core.Contracts;

namespace GodotAls.Core.Sync;

/// <summary>Ranges address the batch's flat history arrays. Player sample offsets are batch-global.</summary>
public readonly record struct AlsAssetSyncBatchGroupHistory(AlsAssetSyncGroupHistory Group,
    int PlayerStart, int PlayerCount, int SampleStart, int SampleCount);

public static partial class AlsSyncRuntime
{
    public const int MaxAssetSyncBatchGroups = 64;
    public const int MaxAssetSyncBatchPlayers = 512;
    public const int MaxAssetSyncBatchSamples = 2048;

    /// <summary>One candidate-wide pass for named groups followed by independent sources.
    /// Delegates time advancement to TryEvaluateAssetSyncGroup. No clock, event queue or physical P5 layout.
    /// Output histories are grouped in groupIds order, with independent sources (playerGroupId -1)
    /// in a trailing range that has no group history. Inputs may interleave groups. Failure publishes nothing.
    /// Independent sources retain arrival order and never synchronize with each other. No teleport evaluators.</summary>
    public static bool TryEvaluateAssetSyncBatch(ReadOnlySpan<int> groupIds, ReadOnlySpan<int> playerGroupIds,
        ReadOnlySpan<AlsAssetSyncPlayer> players, ReadOnlySpan<AlsAssetSyncSample> samples,
        ReadOnlySpan<AlsAssetSyncSequence> sequences, ReadOnlySpan<AlsAssetSyncMarker> markers,
        ReadOnlySpan<AlsAssetSyncBatchGroupHistory> previousGroups,
        ReadOnlySpan<AlsAssetPlayerHistory> previousPlayers, ReadOnlySpan<AlsAssetSampleHistory> previousSamples,
        float delta, Span<AlsAssetSyncBatchGroupHistory> groupOutput, Span<AlsAssetPlayerHistory> playerOutput,
        Span<AlsAssetSampleHistory> sampleOutput, out AlsP5FailureCode failure,
        Span<AlsAssetPlayerTickContext> tickContextOutput = default)
    {
        failure = AlsP5FailureCode.InvalidSyncGroup;
        if (groupIds.Length > MaxAssetSyncBatchGroups || players.Length > MaxAssetSyncBatchPlayers ||
            samples.Length > MaxAssetSyncBatchSamples || previousPlayers.Length > MaxAssetSyncBatchPlayers ||
            previousSamples.Length > MaxAssetSyncBatchSamples || playerGroupIds.Length != players.Length ||
            groupOutput.Length < groupIds.Length || playerOutput.Length < players.Length || sampleOutput.Length < samples.Length ||
            !tickContextOutput.IsEmpty && tickContextOutput.Length < players.Length ||
            !float.IsFinite(delta) || delta < 0 ||
            (previousGroups.IsEmpty ? !groupIds.IsEmpty && (!previousPlayers.IsEmpty || !previousSamples.IsEmpty) : previousGroups.Length != groupIds.Length)) return false;
        for (var i = 0; i < groupIds.Length; i++)
            if (groupIds[i] < 0 || groupIds[..i].Contains(groupIds[i])) return false;
        Span<int> identities = stackalloc int[System.Math.Max(System.Math.Max(players.Length, samples.Length),
            System.Math.Max(previousPlayers.Length, previousSamples.Length))];
        var sampleCursor = 0;
        for (var i = 0; i < players.Length; i++)
        {
            var player = players[i];
            if (playerGroupIds[i] != -1 && !groupIds.Contains(playerGroupIds[i]) || player.SampleStart != sampleCursor ||
                player.SampleCount <= 0 || player.SampleCount > samples.Length - sampleCursor) return false;
            identities[i] = player.PlayerId;
            sampleCursor += player.SampleCount;
        }
        if (sampleCursor != samples.Length || HasDuplicates(identities[..players.Length])) return false;
        for (var i = 0; i < samples.Length; i++) identities[i] = samples[i].SampleId;
        if (HasDuplicates(identities[..samples.Length])) return false;
        var priorPlayerCursor = 0; var priorSampleCursor = 0;
        for (var index = 0; index < previousGroups.Length; index++)
        {
            var group = previousGroups[index];
            if (group.Group.GroupId != groupIds[index] || group.PlayerStart != priorPlayerCursor || group.SampleStart != priorSampleCursor ||
                group.PlayerCount < 0 || group.PlayerCount > MaxAssetSyncPlayers || group.PlayerCount > previousPlayers.Length - priorPlayerCursor ||
                group.SampleCount < 0 || group.SampleCount > MaxAssetSyncSamples || group.SampleCount > previousSamples.Length - priorSampleCursor) return false;
            var end = priorSampleCursor + group.SampleCount;
            for (var n = 0; n < group.PlayerCount; n++)
            {
                var player = previousPlayers[priorPlayerCursor + n];
                if (player.SampleStart != priorSampleCursor || player.SampleCount <= 0 || player.SampleCount > end - priorSampleCursor) return false;
                priorSampleCursor += player.SampleCount;
            }
            if (priorSampleCursor != end) return false;
            priorPlayerCursor += group.PlayerCount;
        }
        var independentPlayerStart = priorPlayerCursor; var independentSampleStart = priorSampleCursor;
        for (; priorPlayerCursor < previousPlayers.Length; priorPlayerCursor++)
        {
            var player = previousPlayers[priorPlayerCursor];
            if (player.SampleStart != priorSampleCursor || player.SampleCount <= 0 || player.SampleCount > previousSamples.Length - priorSampleCursor) return false;
            priorSampleCursor += player.SampleCount;
        }
        if (priorSampleCursor != previousSamples.Length) return false;
        for (var i = 0; i < previousPlayers.Length; i++) identities[i] = previousPlayers[i].PlayerId;
        if (HasDuplicates(identities[..previousPlayers.Length])) return false;
        for (var i = 0; i < previousSamples.Length; i++) identities[i] = previousSamples[i].SampleId;
        if (HasDuplicates(identities[..previousSamples.Length])) return false;

        Span<AlsAssetSyncBatchGroupHistory> stagedGroups = stackalloc AlsAssetSyncBatchGroupHistory[groupIds.Length];
        Span<AlsAssetPlayerHistory> stagedPlayers = stackalloc AlsAssetPlayerHistory[players.Length];
        Span<AlsAssetSampleHistory> stagedSamples = stackalloc AlsAssetSampleHistory[samples.Length];
        Span<AlsAssetPlayerTickContext> stagedTickContexts = stackalloc AlsAssetPlayerTickContext[players.Length];
        Span<AlsAssetSyncPlayer> groupPlayers = stackalloc AlsAssetSyncPlayer[players.Length];
        Span<AlsAssetSyncSample> groupSamples = stackalloc AlsAssetSyncSample[samples.Length];
        Span<AlsAssetPlayerHistory> priorPlayers = stackalloc AlsAssetPlayerHistory[previousPlayers.Length];
        var playerStart = 0; var sampleStart = 0;
        for (var groupIndex = 0; groupIndex <= groupIds.Length; groupIndex++)
        {
            failure = AlsP5FailureCode.InvalidSyncGroup;
            var independent = groupIndex == groupIds.Length;
            var groupId = independent ? -1 : groupIds[groupIndex]; var playerCount = 0; var sampleCount = 0;
            for (var i = 0; i < players.Length; i++)
            {
                if (playerGroupIds[i] != groupId) continue;
                var player = players[i];
                if (!independent && (playerCount == MaxAssetSyncPlayers || player.SampleCount > MaxAssetSyncSamples - sampleCount)) return false;
                groupPlayers[playerCount++] = player with { SampleStart = sampleCount };
                samples.Slice(player.SampleStart, player.SampleCount).CopyTo(groupSamples[sampleCount..]);
                sampleCount += player.SampleCount;
            }
            var previous = independent ? new AlsAssetSyncBatchGroupHistory(default, independentPlayerStart,
                previousPlayers.Length - independentPlayerStart, independentSampleStart, previousSamples.Length - independentSampleStart) :
                previousGroups.IsEmpty ? new AlsAssetSyncBatchGroupHistory(
                new(groupId, false, -1, -1, 0, -1, 0, 0, 0, 0, default, default), 0, 0, 0, 0) : previousGroups[groupIndex];
            if (independent && playerCount == 0 && previous.PlayerCount == 0) continue;
            for (var i = 0; i < previous.PlayerCount; i++)
                priorPlayers[i] = previousPlayers[previous.PlayerStart + i] with
                    { SampleStart = previousPlayers[previous.PlayerStart + i].SampleStart - previous.SampleStart };
            var result = default(AlsAssetSyncGroupHistory);
            if (independent)
            {
                if (!TryEvaluateIndependentAssetPlayers(groupPlayers[..playerCount], groupSamples[..sampleCount], sequences, markers,
                        priorPlayers[..previous.PlayerCount], previousSamples.Slice(previous.SampleStart, previous.SampleCount), delta,
                        stagedPlayers.Slice(playerStart, playerCount), stagedSamples.Slice(sampleStart, sampleCount), out failure)) return false;
                for (var i = 0; i < playerCount; i++) stagedTickContexts[playerStart + i] = new(groupPlayers[i].PlayerId, i, true);
            }
            else if (!TryEvaluateAssetSyncGroup(groupId, previous.Group, groupPlayers[..playerCount], groupSamples[..sampleCount],
                    sequences, markers, priorPlayers[..previous.PlayerCount], previousSamples.Slice(previous.SampleStart, previous.SampleCount),
                    delta, stagedPlayers.Slice(playerStart, playerCount), stagedSamples.Slice(sampleStart, sampleCount), out result, out failure,
                    stagedTickContexts.Slice(playerStart, playerCount))) return false;
            for (var i = playerStart; i < playerStart + playerCount; i++)
            {
                stagedPlayers[i] = stagedPlayers[i] with { SampleStart = stagedPlayers[i].SampleStart + sampleStart };
                stagedTickContexts[i] = stagedTickContexts[i] with { Order = stagedTickContexts[i].Order + playerStart };
            }
            if (!independent) stagedGroups[groupIndex] = new(result, playerStart, playerCount, sampleStart, sampleCount);
            playerStart += playerCount; sampleStart += sampleCount;
        }
        stagedGroups.CopyTo(groupOutput); stagedPlayers.CopyTo(playerOutput); stagedSamples.CopyTo(sampleOutput);
        if (!tickContextOutput.IsEmpty) stagedTickContexts.CopyTo(tickContextOutput);
        failure = AlsP5FailureCode.None;
        return true;

    }

    private static bool HasDuplicates(Span<int> values)
    {
        values.Sort();
        for (var i = 1; i < values.Length; i++) if (values[i] == values[i - 1]) return true;
        return false;
    }
}
