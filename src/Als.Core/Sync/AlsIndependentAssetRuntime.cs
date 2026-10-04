using GodotAls.Core.Contracts;

namespace GodotAls.Core.Sync;

public static partial class AlsSyncRuntime
{
    /// <summary>DoNotSync source ticks in registration order, each with a fresh single-asset
    /// context. No group leader/ratio history or inertialization resync. BlendSpace samples may
    /// still synchronize internally; ungrouped Sequence tick records do not enable marker sync.
    /// Failure publishes nothing. Teleport evaluators and root-motion extraction are not players.</summary>
    public static bool TryEvaluateIndependentAssetPlayers(ReadOnlySpan<AlsAssetSyncPlayer> players,
        ReadOnlySpan<AlsAssetSyncSample> samples, ReadOnlySpan<AlsAssetSyncSequence> sequences,
        ReadOnlySpan<AlsAssetSyncMarker> markers, ReadOnlySpan<AlsAssetPlayerHistory> previousPlayers,
        ReadOnlySpan<AlsAssetSampleHistory> previousSamples, float delta,
        Span<AlsAssetPlayerHistory> playerOutput, Span<AlsAssetSampleHistory> sampleOutput,
        out AlsP5FailureCode failure)
    {
        failure = AlsP5FailureCode.InvalidSyncGroup;
        if (playerOutput.Length < players.Length || sampleOutput.Length < samples.Length ||
            !ValidateAssetSync(0, default, players, samples, sequences, markers, previousPlayers, previousSamples, delta, independent: true) ||
            !ValidateIndependentHistoryRanges(previousPlayers, previousSamples)) return false;
        var cursor = 0;
        foreach (var player in players)
        {
            if (player.SampleStart != cursor) return false;
            cursor += player.SampleCount;
        }
        Span<int> identities = stackalloc int[samples.Length];
        for (var i = 0; i < samples.Length; i++) identities[i] = samples[i].SampleId;
        if (HasDuplicates(identities)) return false;

        Span<AlsAssetPlayerHistory> stagedPlayers = stackalloc AlsAssetPlayerHistory[players.Length];
        Span<AlsAssetSampleHistory> stagedSamples = stackalloc AlsAssetSampleHistory[samples.Length];
        Span<PassedAssetMarker> passed = stackalloc PassedAssetMarker[MaxAssetSyncPassedMarkers];
        for (var i = 0; i < players.Length; i++)
        {
            var player = players[i];
            InitializeAssetTickHistory(player, samples, sequences, previousPlayers, previousSamples, true,
                out stagedPlayers[i], stagedSamples);
            // FAnimSync's ungrouped path never enables FAnimTickRecord.bCanUseMarkerSync.
            // BlendSpace's single-animation branch independently enables its internal sample sync.
            var context = new AssetMarkerContext { ValidMask = player.Kind == AlsAssetSyncKind.BlendSpace ? player.AssetMarkerMask : 0 };
            if (!TickAssetSyncPlayer(player, samples, sequences, markers, delta, true, true, false,
                    ref stagedPlayers[i], stagedSamples, ref context, passed)) return false;
        }
        foreach (var output in stagedPlayers)
            if (!float.IsFinite(output.Time) || !float.IsFinite(output.DeltaPrevious) || !float.IsFinite(output.Delta) || !FiniteAssetMarker(output.Marker))
            { failure = AlsP5FailureCode.NonFiniteOutput; return false; }
        foreach (var output in stagedSamples)
            if (!float.IsFinite(output.Time) || !float.IsFinite(output.PreviousTime) || !float.IsFinite(output.DeltaPrevious) ||
                !float.IsFinite(output.Delta) || !FiniteAssetMarker(output.Marker))
            { failure = AlsP5FailureCode.NonFiniteOutput; return false; }
        stagedPlayers.CopyTo(playerOutput); stagedSamples.CopyTo(sampleOutput);
        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool ValidateIndependentHistoryRanges(ReadOnlySpan<AlsAssetPlayerHistory> players,
        ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        var cursor = 0;
        foreach (var player in players)
        {
            if (player.SampleStart != cursor || player.SampleCount <= 0 || player.SampleCount > samples.Length - cursor) return false;
            cursor += player.SampleCount;
        }
        if (cursor != samples.Length) return false;
        Span<int> identities = stackalloc int[samples.Length];
        for (var i = 0; i < samples.Length; i++) identities[i] = samples[i].SampleId;
        return !HasDuplicates(identities);
    }

    private static void InitializeAssetTickHistory(in AlsAssetSyncPlayer player, ReadOnlySpan<AlsAssetSyncSample> samples,
        ReadOnlySpan<AlsAssetSyncSequence> sequences, ReadOnlySpan<AlsAssetPlayerHistory> previousPlayers,
        ReadOnlySpan<AlsAssetSampleHistory> previousSamples, bool allowSampleMarkers,
        out AlsAssetPlayerHistory history, Span<AlsAssetSampleHistory> sampleHistory)
    {
        var previousIndex = FindAssetHistory(previousPlayers, player);
        // A real source occurrence retains its marker storage even while it
        // is hidden or reinitialized. Native Reset clears indices only.
        var record = player.MarkerRecord ?? (previousIndex >= 0 ? previousPlayers[previousIndex].Marker : AlsAssetMarkerRecord.Invalid);
        history = new(player.PlayerId, player.AssetId, player.Epoch, player.Time, 0, 0, record, player.SampleStart, player.SampleCount,
            player.IsEvaluator && !player.Looping);
        for (var n = player.SampleStart; n < player.SampleStart + player.SampleCount; n++)
        {
            var sample = samples[n]; var sequence = sequences[sample.SequenceIndex];
            sampleHistory[n] = new(sample.SampleId, sequence.AnimationId, 0, 0, AlsAssetMarkerRecord.Invalid, 0, 0);
            if (!allowSampleMarkers || player.Kind != AlsAssetSyncKind.BlendSpace || player.AssetMarkerMask == 0 || previousIndex < 0) continue;
            var prior = previousPlayers[previousIndex];
            for (var j = prior.SampleStart; j < prior.SampleStart + prior.SampleCount; j++)
                if (previousSamples[j].AnimationId == sequence.AnimationId)
                    sampleHistory[n] = previousSamples[j] with { SampleId = sample.SampleId, DeltaPrevious = 0, Delta = 0 };
        }
    }
}
