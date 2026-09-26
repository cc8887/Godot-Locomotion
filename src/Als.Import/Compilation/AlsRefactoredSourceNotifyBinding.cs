using GodotAls.Core.Events;
using GodotAls.Core.Sync;

namespace GodotAls.Import.Compilation;

/// <summary>The character allocates distinct owner scopes for its subgraphs. Local player/sample
/// IDs and epochs must never be used alone as character-wide playback identities.</summary>
public readonly record struct AlsRefactoredNotifyPlayback(uint OwnerScope, int PlayerId, int SampleId, long Epoch);
public readonly record struct AlsRefactoredNotifyPlayerContext(int PlayerId, bool Active, bool ScopeFiltered = false);
public readonly record struct AlsRefactoredSourceNotifyTick(AlsRefactoredNotifyPlayback Playback, long Frame,
    int SequenceIndex, float PreviousTime, float Delta, float ContextTime, float Weight, bool Leader,
    bool Looping, bool ActiveContext, bool ScopeFiltered);

/// <summary>Read-only bridge from a prepared original Sync batch to notification extraction.
/// It owns no cursor, random seed or state lifecycle. The character's single queue owns those.
/// Extraction does not dispatch effects and must be discarded when the source frame is canceled.</summary>
public sealed class AlsRefactoredSourceNotifyBinding
{
    private readonly AlsRefactoredSourcePlayerRuntime _players;
    private readonly AlsRefactoredNotifyBank _bank;
    public uint OwnerScope { get; }
    public AlsRefactoredSourceNotifyBinding(uint ownerScope, AlsRefactoredSourcePlayerRuntime players, AlsRefactoredNotifyBank bank)
    {
        if (ownerScope == 0 || players.CatalogDigest != bank.CatalogDigest) throw new ArgumentException("Foreign source notify owner.");
        OwnerScope = ownerScope; _players = players; _bank = bank;
    }

    public int BuildTicks(long frame, ReadOnlySpan<AlsRefactoredNotifyPlayerContext> contexts, Span<AlsRefactoredSourceNotifyTick> output)
    {
        _players.ValidateCommit(frame);
        var histories = _players.Players; var ticks = _players.Ticks; var samples = _players.ResolvedSamples;
        var completed = _players.Samples; var orders = _players.TickContexts;
        if (contexts.Length != ticks.Length) throw new ArgumentException("Missing graph notify contexts.");
        for (var i = 0; i < contexts.Length; i++)
        {
            var found = false;
            foreach (var tick in ticks) if (tick.PlayerId == contexts[i].PlayerId) found = true;
            if (!found) throw new ArgumentException("Foreign graph notify context.");
            for (var j = 0; j < i; j++) if (contexts[j].PlayerId == contexts[i].PlayerId) throw new ArgumentException("Duplicate graph notify context.");
        }
        Span<AlsRefactoredSourceNotifyTick> staged = stackalloc AlsRefactoredSourceNotifyTick[samples.Length];
        var count = 0;
        for (var order = 0; order < orders.Length; order++)
        {
            var index = -1;
            for (var i = 0; i < orders.Length; i++) if (orders[i].Order == order) index = i;
            if (index < 0) throw new InvalidOperationException("Incomplete source tick order.");
            var history = histories[index]; var context = orders[index]; var tickIndex = -1;
            for (var i = 0; i < ticks.Length; i++) if (ticks[i].PlayerId == history.PlayerId) tickIndex = i;
            if (tickIndex < 0 || context.PlayerId != history.PlayerId) throw new InvalidOperationException("Foreign completed source tick.");
            var tick = ticks[tickIndex]; var graphContext = default(AlsRefactoredNotifyPlayerContext);
            foreach (var c in contexts) if (c.PlayerId == tick.PlayerId) graphContext = c;
            var mode = _players.NotifyMode(tick.PlayerId);
            var highest = 0; var highestWeight = -1f;
            for (var i = 0; i < tick.SampleCount; i++)
            {
                var weight = System.Math.Clamp(samples[tick.SampleStart + i].Weight, 0, 1);
                if (weight > highestWeight) { highest = i; highestWeight = weight; }
            }
            for (var i = 0; i < tick.SampleCount; i++)
            {
                var sample = samples[tick.SampleStart + i]; var mapped = completed[history.SampleStart + i];
                if (sample.SampleId != mapped.SampleId || _bank.Sequences[sample.SequenceIndex].AssetId != mapped.AnimationId)
                    throw new InvalidOperationException("Foreign notification sample.");
                if (sample.Weight <= .00001f || mode == AlsBlendSpaceNotifyMode.None ||
                    mode == AlsBlendSpaceNotifyMode.HighestWeightedAnimation && i != highest) continue;
                staged[count++] = new(new(OwnerScope, tick.PlayerId, sample.SampleId, tick.Epoch), frame, sample.SequenceIndex,
                    mapped.DeltaPrevious, mapped.Delta, history.Time, tick.Weight, context.Leader, tick.Looping,
                    graphContext.Active, graphContext.ScopeFiltered);
            }
        }
        if (output.Length < count) throw new ArgumentException("Source notify tick capacity exceeded.");
        staged[..count].CopyTo(output); return count;
    }

    /// <summary>Returns bank-global definition indices, preserving loop/definition order.
    /// The source frame must still be prepared. Queue filtering follows at character scope.</summary>
    public int Extract(in AlsRefactoredSourceNotifyTick tick, Span<AlsAssetNotifyOccurrence> output)
    {
        _players.ValidateCommit(tick.Frame);
        if (tick.Playback.OwnerScope != OwnerScope || (uint)tick.SequenceIndex >= (uint)_bank.Sequences.Length)
            throw new ArgumentException("Foreign notify playback scope.");
        // Reject stale epochs and mismatched ranges, including ticks held across cancel/retry.
        var valid = false;
        foreach (var player in _players.Players)
        {
            if (player.PlayerId != tick.Playback.PlayerId || player.Epoch != tick.Playback.Epoch) continue;
            var matchesInput = false;
            foreach (var input in _players.Ticks)
                if (input.PlayerId == player.PlayerId && input.Weight == tick.Weight && input.Looping == tick.Looping) matchesInput = true;
            foreach (var context in _players.TickContexts)
                if (context.PlayerId == player.PlayerId && context.Leader != tick.Leader) matchesInput = false;
            if (!matchesInput) continue;
            foreach (var sample in _players.Samples.Slice(player.SampleStart, player.SampleCount))
                if (sample.SampleId == tick.Playback.SampleId && sample.AnimationId == _bank.Sequences[tick.SequenceIndex].AssetId &&
                    sample.DeltaPrevious == tick.PreviousTime && sample.Delta == tick.Delta && player.Time == tick.ContextTime) valid = true;
        }
        if (!valid) throw new ArgumentException("Stale or foreign notify tick.");
        var asset = _bank.Sequences[tick.SequenceIndex];
        if (!AlsTimelineRuntime.TryExtractAssetNotifies(_bank.Definitions.Slice(asset.Offset, asset.Count), asset.Length,
            tick.PreviousTime, tick.Delta, tick.Looping, output, out var count, out var failure))
            throw new InvalidOperationException("Source notify extraction failed: " + failure);
        for (var i = 0; i < count; i++) output[i] = output[i] with { DefinitionIndex = output[i].DefinitionIndex + asset.Offset };
        return count;
    }
}
