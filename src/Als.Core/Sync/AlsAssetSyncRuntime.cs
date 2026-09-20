using GodotAls.Core.Contracts;

namespace GodotAls.Core.Sync;

public static partial class AlsSyncRuntime
{
    public const int MaxAssetSyncPlayers = 128;
    public const int MaxAssetSyncSamples = 512;
    public const int MaxAssetSyncPassedMarkers = 256;

    /// <summary>Candidate-only mixed Sequence/BlendSpace CanBeLeader/AlwaysFollower group. Call on inactive
    /// frames too. Sample weights must be resolved by the source BlendSpace before this call.
    /// Marker tracks currently require looping and the same complete symbol set within a group.
    /// No evaluator, mirror, phase-matching, notify queue, or root-motion side effects.</summary>
    public static bool TryEvaluateAssetSyncGroup(int groupId, in AlsAssetSyncGroupHistory previousGroup,
        ReadOnlySpan<AlsAssetSyncPlayer> players, ReadOnlySpan<AlsAssetSyncSample> samples,
        ReadOnlySpan<AlsAssetSyncSequence> sequences, ReadOnlySpan<AlsAssetSyncMarker> markers,
        ReadOnlySpan<AlsAssetPlayerHistory> previousPlayers, ReadOnlySpan<AlsAssetSampleHistory> previousSamples,
        float frameDelta, Span<AlsAssetPlayerHistory> playerOutput, Span<AlsAssetSampleHistory> sampleOutput,
        out AlsAssetSyncGroupHistory candidate, out AlsP5FailureCode failure,
        Span<AlsAssetPlayerTickContext> tickContextOutput = default)
    {
        candidate = previousGroup; failure = AlsP5FailureCode.InvalidSyncGroup;
        if (!ValidateAssetSync(groupId, previousGroup, players, samples, sequences, markers,
                previousPlayers, previousSamples, frameDelta) || playerOutput.Length < players.Length || sampleOutput.Length < samples.Length ||
            !tickContextOutput.IsEmpty && tickContextOutput.Length < players.Length) return false;
        if (players.IsEmpty)
        {
            candidate = new(groupId, false, -1, -1, 0, -1, 0, 0, 0, 0, default, default);
            failure = AlsP5FailureCode.None; return true;
        }
        Span<int> order = stackalloc int[players.Length];
        Span<AlsLengthSyncPlayback> scores = stackalloc AlsLengthSyncPlayback[players.Length];
        for (var i = 0; i < players.Length; i++) { order[i] = i; scores[i] = new() { Weight = AssetLeaderScore(players[i]) }; }
        // Share the existing native score-only IntroSort, including its unstable tie ordering.
        SortLengthTicks(scores, order, (int)(MathF.Log(order.Length) * 2));
        var validMask = players[order[0]].AssetMarkerMask;
        for (var i = 1; i < order.Length; i++)
            if (players[order[i]].AssetMarkerMask != 0) validMask &= players[order[i]].AssetMarkerMask;

        Span<AlsAssetPlayerHistory> stagedPlayers = stackalloc AlsAssetPlayerHistory[players.Length];
        Span<AlsAssetSampleHistory> stagedSamples = stackalloc AlsAssetSampleHistory[samples.Length];
        Span<AlsAssetPlayerTickContext> stagedTickContexts = stackalloc AlsAssetPlayerTickContext[players.Length];
        for (var i = 0; i < players.Length; i++)
        {
            var player = players[i];
            InitializeAssetTickHistory(player, samples, sequences, previousPlayers, previousSamples,
                players.Length == 1 || validMask != 0, out stagedPlayers[i], stagedSamples);
            var record = stagedPlayers[i].Marker;
            if (validMask == 0 || !previousGroup.HasLeader || validMask != previousGroup.ValidMarkerMask) record = AlsAssetMarkerRecord.Invalid;
            stagedPlayers[i] = stagedPlayers[i] with { Marker = record };
        }
        var context = new AssetMarkerContext { ValidMask = validMask, Ratio = previousGroup.HasLeader ? previousGroup.Ratio : 0 };
        context.PreviousRatio = context.Ratio;
        if (previousGroup.HasLeader && previousGroup.MarkerEnd.Valid &&
            ContainsMarker(validMask, previousGroup.MarkerEnd.PreviousSymbol) && ContainsMarker(validMask, previousGroup.MarkerEnd.NextSymbol))
            context.Start = previousGroup.MarkerEnd;
        Span<PassedAssetMarker> passed = stackalloc PassedAssetMarker[MaxAssetSyncPassedMarkers];
        var leaderIndex = 0;
        var groupStart = default(AlsAssetMarkerPosition); var groupEnd = default(AlsAssetMarkerPosition);
        for (; leaderIndex < order.Length; leaderIndex++)
        {
            var index = order[leaderIndex]; var player = players[index];
            var resync = false;
            if (previousGroup.HasLeader)
            {
                if (player.OverridePositionWhenJoining)
                {
                    if (validMask != 0 && previousGroup.ValidMarkerMask != 0 &&
                        (previousGroup.LeaderPlayerId != player.PlayerId || previousGroup.LeaderAssetId != player.AssetId || previousGroup.LeaderEpoch != player.Epoch))
                        context.Start = default;
                }
                else if (player.RequestedInertialization && (validMask == 0 || previousGroup.ValidMarkerMask == 0))
                    resync = previousGroup.LeaderScore >= AssetLeaderScore(player);
            }
            context.PassedCount = 0;
            if (!TickAssetSyncPlayer(player, samples, sequences, markers, frameDelta, true, players.Length == 1,
                    resync, ref stagedPlayers[index], stagedSamples, ref context, passed)) return false;
            stagedTickContexts[index] = new(player.PlayerId, leaderIndex, true);
            if (validMask == 0) break;
            if (context.End.Valid) { groupStart = context.Start; groupEnd = context.End; break; }
        }
        leaderIndex = System.Math.Min(leaderIndex, order.Length - 1);
        for (var n = leaderIndex + 1; n < order.Length; n++)
        {
            var index = order[n]; var player = players[index];
            // UE compares the sorted leader index here, not the identity of the winning player.
            if (!previousGroup.HasLeader || previousGroup.SortedLeaderIndex != leaderIndex)
                stagedPlayers[index] = stagedPlayers[index] with { Marker = AlsAssetMarkerRecord.Invalid };
            if (!TickAssetSyncPlayer(player, samples, sequences, markers, frameDelta, false, false,
                    player.RequestedInertialization, ref stagedPlayers[index], stagedSamples, ref context, passed)) return false;
            stagedTickContexts[index] = new(player.PlayerId, n, false);
        }
        var leader = players[order[leaderIndex]];
        var result = new AlsAssetSyncGroupHistory(groupId, true, leader.PlayerId, leader.AssetId, leader.Epoch,
            leaderIndex, AssetLeaderScore(leader), validMask, context.PreviousRatio, context.Ratio, groupStart, groupEnd);
        foreach (var output in stagedPlayers)
            if (!float.IsFinite(output.Time) || !float.IsFinite(output.DeltaPrevious) || !float.IsFinite(output.Delta) || !FiniteAssetMarker(output.Marker))
            { failure = AlsP5FailureCode.NonFiniteOutput; return false; }
        foreach (var output in stagedSamples)
            if (!float.IsFinite(output.Time) || !float.IsFinite(output.PreviousTime) || !float.IsFinite(output.DeltaPrevious) || !float.IsFinite(output.Delta) || !FiniteAssetMarker(output.Marker))
            { failure = AlsP5FailureCode.NonFiniteOutput; return false; }
        if (!float.IsFinite(result.PreviousRatio) || !float.IsFinite(result.Ratio) || !FiniteAssetPosition(result.MarkerStart) || !FiniteAssetPosition(result.MarkerEnd))
        { failure = AlsP5FailureCode.NonFiniteOutput; return false; }
        stagedPlayers.CopyTo(playerOutput); stagedSamples.CopyTo(sampleOutput);
        if (!tickContextOutput.IsEmpty) stagedTickContexts.CopyTo(tickContextOutput);
        candidate = result; failure = AlsP5FailureCode.None; return true;
    }

    // FAnimGroupInstance::TestTickRecordForLeadership: followers still compete by
    // weight when no leader is available, but sort behind every possible leader.
    private static float AssetLeaderScore(in AlsAssetSyncPlayer player) =>
        player.Role == AlsAssetSyncRole.AlwaysFollower ? -2f + player.Weight : player.Weight;

    private static bool TickAssetSyncPlayer(in AlsAssetSyncPlayer player, ReadOnlySpan<AlsAssetSyncSample> allSamples,
        ReadOnlySpan<AlsAssetSyncSequence> sequences, ReadOnlySpan<AlsAssetSyncMarker> markers, float frameDelta,
        bool leader, bool single, bool resync, ref AlsAssetPlayerHistory history, Span<AlsAssetSampleHistory> allHistory,
        ref AssetMarkerContext context, Span<PassedAssetMarker> passed)
    {
        var samples = allSamples.Slice(player.SampleStart, player.SampleCount);
        var cache = allHistory.Slice(player.SampleStart, player.SampleCount);
        var groupMarker = player.AssetMarkerMask != 0 && context.ValidMask != 0;
        if (player.Kind == AlsAssetSyncKind.Sequence)
        {
            var sequence = sequences[samples[0].SequenceIndex];
            var track = markers.Slice(sequence.MarkerStart, sequence.MarkerCount);
            var rate = player.PlayRate * sequence.RateScale;
            var time = resync ? context.Ratio * sequence.DurationSeconds : history.Time;
            var previousTime = time; var delta = 0f; var record = history.Marker;
            if (leader)
            {
                delta = rate * frameDelta; context.LeaderDelta = delta; context.PreviousRatio = previousTime / sequence.DurationSeconds;
                if (delta != 0)
                {
                    if (groupMarker)
                    {
                        if (!TickMarkerLeader(track, sequence.DurationSeconds, delta, ref time, out previousTime, ref record, ref context, passed)) return false;
                    }
                    else if (!AdvanceAssetTime(time, sequence.DurationSeconds, delta, player.Looping, out time)) return false;
                }
                else if (groupMarker && !record.Initialized) record = MarkersAtTime(track, sequence.DurationSeconds, time);
                context.Ratio = time / sequence.DurationSeconds;
            }
            else
            {
                if (groupMarker)
                {
                    if (context.Start.Valid)
                    {
                        if (!TickMarkerFollower(track, sequence.DurationSeconds, context.LeaderDelta, ref time, out previousTime, ref record, context, passed)) return false;
                    }
                    else
                    {
                        delta = rate * frameDelta;
                        if (!AdvanceAssetTime(time, sequence.DurationSeconds, delta, player.Looping, out time)) return false;
                    }
                }
                else { previousTime = context.PreviousRatio * sequence.DurationSeconds; time = context.Ratio * sequence.DurationSeconds; }
                if (time != previousTime)
                {
                    delta = time - previousTime;
                    if (delta * rate < 0) delta += MathF.Sign(rate) * sequence.DurationSeconds;
                }
            }
            history = history with { Time = time, DeltaPrevious = previousTime, Delta = delta, Marker = record };
            cache[0] = new(samples[0].SampleId, sequence.AnimationId, time, previousTime, record, previousTime, delta);
            return true;
        }

        Span<AlsBlendSpaceTimingSample> timing = stackalloc AlsBlendSpaceTimingSample[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = samples[i]; var sequence = sequences[sample.SequenceIndex];
            timing[i] = new(sample.SampleId, sequence.AnimationId, sample.Weight, sequence.DurationSeconds,
                sequence.RateScale, sample.RateScale, sample.CachedPlayRate, sequence.MarkerCount > 0);
        }
        if (!TryDescribeBlendSpaceTiming(timing, player.LegacyLength, out var description, out _)) return false;
        var current = resync ? context.Ratio : history.Time;
        var previous = current; var tickDelta = player.PlayRate * frameDelta;
        if (!float.IsFinite(tickDelta)) return false;
        var canMarker = player.AssetMarkerMask != 0 && (single || groupMarker);
        var playerRecord = history.Marker;
        if (leader)
        {
            context.PreviousRatio = current;
            var selected = canMarker ? description.HighestMarkerWeightIndex : -1;
            canMarker = selected >= 0;
            if (canMarker)
            {
                var entry = cache[selected]; var sample = samples[selected]; var sequence = sequences[sample.SequenceIndex];
                var track = markers.Slice(sequence.MarkerStart, sequence.MarkerCount);
                var record = entry.Marker; var sampleTime = entry.Time; var samplePrevious = entry.PreviousTime;
                var resetFollowers = !playerRecord.Initialized;
                if (resetFollowers) { record = AlsAssetMarkerRecord.Invalid; sampleTime = current * sequence.DurationSeconds; }
                else if (!record.Initialized && context.Start.Valid && !MarkersAtPosition(track, sequence.DurationSeconds, context.Start, ref sampleTime, out record)) return false;
                // Preserve the native multiplication order; RunPose has a small asset multiplier.
                context.LeaderDelta = tickDelta * sample.RateScale * sequence.RateScale;
                var advance = MathF.Abs(context.LeaderDelta) > 1e-8f;
                if (advance)
                {
                    if (!TickMarkerLeader(track, sequence.DurationSeconds, context.LeaderDelta, ref sampleTime,
                            out samplePrevious, ref record, ref context, passed)) return false;
                }
                else if (resetFollowers)
                {
                    record = MarkersAtTime(track, sequence.DurationSeconds, sampleTime);
                    context.Start = context.End = PositionFromMarkers(track, sequence.DurationSeconds, sampleTime, record);
                }
                cache[selected] = entry with { Time = sampleTime, PreviousTime = samplePrevious, Marker = record };
                if ((advance || resetFollowers) && !TickAssetSampleFollowers(samples, sequences, markers, cache, selected,
                        resetFollowers, context, passed)) return false;
                current = sampleTime / sequence.DurationSeconds; playerRecord = record;
            }
            else if (!TryAdvanceBlendSpaceLength(current, description.EffectiveLengthSeconds, tickDelta, player.Looping, out current, out _)) return false;
            context.Ratio = current;
        }
        else
        {
            canMarker &= context.Start.Valid;
            if (canMarker)
            {
                var selected = description.HighestWeightIndex;
                var sequence = sequences[samples[selected].SequenceIndex];
                if (frameDelta != 0)
                {
                    if (!playerRecord.Initialized) cache[selected] = cache[selected] with { Time = current * sequence.DurationSeconds };
                    if (!TickAssetSampleFollowers(samples, sequences, markers, cache, -1, false, context, passed)) return false;
                }
                playerRecord = cache[selected].Marker; current = cache[selected].Time / sequence.DurationSeconds;
            }
            else { previous = context.PreviousRatio; current = context.Ratio; }
        }
        for (var i = 0; i < samples.Length; i++)
            timing[i] = timing[i] with { MarkerPreviousTime = cache[i].PreviousTime, MarkerTime = cache[i].Time };
        Span<AlsBlendSpaceSampleTime> finalized = stackalloc AlsBlendSpaceSampleTime[samples.Length];
        if (!TryFinalizeBlendSpaceTimes(timing, previous, current, tickDelta, canMarker, AlsBlendSpaceNotifyMode.None, finalized, out var count, out _)) return false;
        var sourceIndex = 0;
        for (var i = 0; i < count; i++)
        {
            while (samples[sourceIndex].SampleId != finalized[i].SampleId) sourceIndex++;
            cache[sourceIndex] = cache[sourceIndex] with { Time = finalized[i].TimeSeconds,
                DeltaPrevious = finalized[i].PreviousTimeSeconds, Delta = finalized[i].AdvanceSeconds };
        }
        history = history with { Time = current, DeltaPrevious = player.Time, Delta = tickDelta, Marker = playerRecord };
        return true;
    }

    private static bool TickAssetSampleFollowers(ReadOnlySpan<AlsAssetSyncSample> samples,
        ReadOnlySpan<AlsAssetSyncSequence> sequences, ReadOnlySpan<AlsAssetSyncMarker> markers,
        Span<AlsAssetSampleHistory> cache, int leaderIndex, bool reset, in AssetMarkerContext context, ReadOnlySpan<PassedAssetMarker> passed)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            if (i == leaderIndex) continue;
            var sequence = sequences[samples[i].SequenceIndex]; var entry = cache[i];
            var record = reset ? AlsAssetMarkerRecord.Invalid : entry.Marker;
            if (sequence.MarkerCount > 0)
            {
                var time = entry.Time;
                if (!TickMarkerFollower(markers.Slice(sequence.MarkerStart, sequence.MarkerCount), sequence.DurationSeconds,
                        context.LeaderDelta, ref time, out var previous, ref record, context, passed)) return false;
                entry = entry with { Time = time, PreviousTime = previous };
            }
            cache[i] = entry with { Marker = record };
        }
        return true;
    }

    private static bool AdvanceAssetTime(float time, float duration, float delta, bool looping, out float result)
    {
        result = time + delta;
        if (!float.IsFinite(result)) return false;
        if (result < 0 || result > duration)
        {
            if (looping) { result %= duration; if (result < 0) result += duration; }
            else result = System.Math.Clamp(result, 0, duration);
        }
        return true;
    }

    private static int FindAssetHistory(ReadOnlySpan<AlsAssetPlayerHistory> histories, in AlsAssetSyncPlayer player)
    {
        for (var i = 0; i < histories.Length; i++)
            if (histories[i].PlayerId == player.PlayerId && histories[i].AssetId == player.AssetId && histories[i].Epoch == player.Epoch) return i;
        return -1;
    }

    private static ulong AssetMarkerMask(ReadOnlySpan<AlsAssetSyncMarker> track)
    {
        ulong result = 0;
        foreach (var marker in track) result |= 1UL << marker.Symbol;
        return result;
    }

    private static bool FiniteAssetMarker(in AlsAssetMarkerRecord record) => !record.Initialized ||
        record.PreviousIndex >= 0 && record.NextIndex >= 0 && float.IsFinite(record.PreviousDistance) && float.IsFinite(record.NextDistance);

    private static bool FiniteAssetPosition(in AlsAssetMarkerPosition position) =>
        position.PreviousSymbol is >= 0 and < 64 && position.NextSymbol is >= 0 and < 64 && float.IsFinite(position.Alpha);

    private static bool ValidateAssetSync(int groupId, in AlsAssetSyncGroupHistory previousGroup,
        ReadOnlySpan<AlsAssetSyncPlayer> players, ReadOnlySpan<AlsAssetSyncSample> samples,
        ReadOnlySpan<AlsAssetSyncSequence> sequences, ReadOnlySpan<AlsAssetSyncMarker> markers,
        ReadOnlySpan<AlsAssetPlayerHistory> previousPlayers, ReadOnlySpan<AlsAssetSampleHistory> previousSamples, float frameDelta,
        bool independent = false)
    {
        var maxPlayers = independent ? MaxAssetSyncBatchPlayers : MaxAssetSyncPlayers;
        var maxSamples = independent ? MaxAssetSyncBatchSamples : MaxAssetSyncSamples;
        if (groupId < 0 || players.Length > maxPlayers || samples.Length > maxSamples ||
            previousPlayers.Length > maxPlayers || previousSamples.Length > maxSamples ||
            !float.IsFinite(frameDelta) || frameDelta < 0 || previousGroup.HasLeader &&
            (previousGroup.GroupId != groupId || previousGroup.LeaderPlayerId < 0 || previousGroup.LeaderAssetId < 0 || previousGroup.LeaderEpoch <= 0 ||
             previousGroup.SortedLeaderIndex < 0 || previousGroup.SortedLeaderIndex >= previousPlayers.Length ||
             !float.IsFinite(previousGroup.LeaderScore) ||
             previousGroup.LeaderScore is not (>= -2 and <= -1 or >= 0 and <= 1) ||
             !float.IsFinite(previousGroup.PreviousRatio) || previousGroup.PreviousRatio is < 0 or > 1 ||
             !float.IsFinite(previousGroup.Ratio) || previousGroup.Ratio is < 0 or > 1 ||
             !FiniteAssetPosition(previousGroup.MarkerStart) || !FiniteAssetPosition(previousGroup.MarkerEnd))) return false;
        foreach (var sequence in sequences)
        {
            if (sequence.AnimationId < 0 || !float.IsFinite(sequence.DurationSeconds) || sequence.DurationSeconds <= 0 ||
                !float.IsFinite(sequence.RateScale) || sequence.MarkerStart < 0 || sequence.MarkerCount < 0 ||
                sequence.MarkerStart > markers.Length - sequence.MarkerCount) return false;
            var last = -1f;
            foreach (var marker in markers.Slice(sequence.MarkerStart, sequence.MarkerCount))
            {
                if (marker.Symbol is <= 0 or >= 64 || !float.IsFinite(marker.TimeSeconds) || marker.TimeSeconds < 0 ||
                    marker.TimeSeconds > sequence.DurationSeconds || marker.TimeSeconds < last) return false;
                last = marker.TimeSeconds;
            }
        }
        Span<bool> owned = stackalloc bool[samples.Length]; owned.Clear();
        for (var i = 0; i < players.Length; i++)
        {
            var p = players[i];
            if (p.PlayerId < 0 || p.AssetId < 0 || p.Epoch <= 0 || p.Kind > AlsAssetSyncKind.BlendSpace || p.Role > AlsAssetSyncRole.AlwaysFollower ||
                !float.IsFinite(p.Time) || p.Time < 0 || !float.IsFinite(p.PlayRate) || !float.IsFinite(p.PlayRate * frameDelta) ||
                !float.IsFinite(p.Weight) || p.Weight < 0 ||
                p.SampleStart < 0 || p.SampleCount < 1 || p.SampleCount > MaxBlendSpaceTimingSamples || p.SampleStart > samples.Length - p.SampleCount ||
                p.MatchSyncPhases || p.AssetMarkerMask != 0 && !p.Looping && (!independent || p.Kind == AlsAssetSyncKind.BlendSpace) || (p.AssetMarkerMask & 1UL) != 0 ||
                p.Kind == AlsAssetSyncKind.Sequence && p.SampleCount != 1 || p.Kind == AlsAssetSyncKind.BlendSpace && p.Time > 1) return false;
            for (var j = 0; j < i; j++) if (players[j].PlayerId == p.PlayerId) return false;
            for (var j = p.SampleStart; j < p.SampleStart + p.SampleCount; j++)
            {
                var sample = samples[j];
                if (owned[j] || sample.SampleId < 0 || (uint)sample.SequenceIndex >= sequences.Length || !float.IsFinite(sample.Weight) ||
                    !float.IsFinite(sample.RateScale) || !float.IsFinite(sample.CachedPlayRate)) return false;
                owned[j] = true;
                var sequence = sequences[sample.SequenceIndex];
                if (!float.IsFinite(p.PlayRate * sequence.RateScale) || !float.IsFinite(p.PlayRate * sequence.RateScale * frameDelta) ||
                    !float.IsFinite(p.PlayRate * frameDelta * sample.RateScale * sequence.RateScale)) return false;
                var mask = AssetMarkerMask(markers.Slice(sequence.MarkerStart, sequence.MarkerCount));
                if (mask != 0 && p.AssetMarkerMask != 0 && mask != p.AssetMarkerMask) return false;
                if (p.Kind == AlsAssetSyncKind.Sequence && (p.Time > sequence.DurationSeconds || mask != p.AssetMarkerMask)) return false;
                for (var k = p.SampleStart; k < j; k++)
                    if (samples[k].SampleId == sample.SampleId || samples[k].SequenceIndex == sample.SequenceIndex) return false;
            }
            if (!independent && p.AssetMarkerMask != 0)
                for (var j = 0; j < i; j++)
                    if (players[j].AssetMarkerMask != 0 && players[j].AssetMarkerMask != p.AssetMarkerMask) return false;
        }
        foreach (var item in owned) if (!item) return false;
        for (var index = 0; index < previousPlayers.Length; index++)
        {
            var p = previousPlayers[index];
            if (p.PlayerId < 0 || p.AssetId < 0 || p.Epoch <= 0 || p.SampleStart < 0 || p.SampleCount < 0 ||
                p.SampleStart > previousSamples.Length - p.SampleCount || !float.IsFinite(p.Time) || p.Time < 0 ||
                !float.IsFinite(p.DeltaPrevious) || !float.IsFinite(p.Delta) || !FiniteAssetMarker(p.Marker)) return false;
            for (var j = 0; j < index; j++) if (previousPlayers[j].PlayerId == p.PlayerId) return false;
            foreach (var current in players)
            {
                if (current.PlayerId != p.PlayerId || current.AssetId != p.AssetId || current.Epoch != p.Epoch || current.Kind != AlsAssetSyncKind.Sequence) continue;
                var sequence = sequences[samples[current.SampleStart].SequenceIndex];
                if (p.Marker.Initialized && (p.Marker.PreviousIndex >= sequence.MarkerCount || p.Marker.NextIndex >= sequence.MarkerCount)) return false;
            }
        }
        foreach (var s in previousSamples)
        {
            if (s.SampleId < 0 || !float.IsFinite(s.Time) || s.Time < 0 || !float.IsFinite(s.PreviousTime) || s.PreviousTime < 0 ||
                !float.IsFinite(s.DeltaPrevious) || !float.IsFinite(s.Delta) || !FiniteAssetMarker(s.Marker)) return false;
            var found = false;
            foreach (var sequence in sequences)
            {
                if (s.AnimationId != sequence.AnimationId) continue;
                found = true;
                if (s.Time > sequence.DurationSeconds || s.PreviousTime > sequence.DurationSeconds || s.Marker.Initialized &&
                    (s.Marker.PreviousIndex >= sequence.MarkerCount || s.Marker.NextIndex >= sequence.MarkerCount)) return false;
                break;
            }
            if (!found) return false;
        }
        return true;
    }
}
