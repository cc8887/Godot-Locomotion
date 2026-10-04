using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraHipFireSourceState(int AssetId, float Time, AlsAssetMarkerRecord Marker, float DeltaPrevious = 0, float Delta = 0, bool ResetPending = false);
internal sealed record LyraCycleLayerSourceCandidate(LyraCycleCandidate Cycle, LyraHipFireSourceState HipFire,
    AlsAssetSyncPlayer[] Players, int[] Groups, AlsAssetSyncSample[] Samples,
    float BlendWeight, float CycleWeight, float HipFireWeight, bool HipFireTicked);
internal readonly record struct LyraCycleLayerResolvedSources(AlsAssetPlayerHistory CycleOutput,
    LyraCycleState Cycle, LyraHipFireSourceState HipFire);

// The original Cycle source collection closure. The character owns the one
// common Sync pass; this host submits both grouped and independent records.
// Orientation/Stride pose evaluation and the enclosing Main graph are separate.
internal sealed class LyraCycleLayerSourceHost
{
    private readonly LyraCycleLayerGraph _graph;
    private readonly LyraCycleSourceRuntime _cycle;
    private readonly Func<bool, int> _hipFireAsset;
    private readonly Func<string, LyraCardinalDirection, LyraCycleAsset> _cycleAsset;
    private readonly AlsAssetSyncSequence[] _sequences;
    private readonly ulong[] _masks;
    private readonly int _playerBase;
    private readonly long _epoch;
    private LyraCycleLayerSourceCandidate? _pending;
    public LyraCycleState Cycle => _cycle.State;
    internal LyraCycleState PreparedCycle => _pending?.Cycle.State ?? Cycle;
    public LyraHipFireSourceState HipFire { get; private set; } = new(-1, 0, AlsAssetMarkerRecord.Invalid);
    public float BlendWeight { get; private set; }
    public float CycleWeight { get; private set; }
    public float HipFireWeight { get; private set; }

    public LyraCycleLayerSourceHost(LyraCycleLayerGraph graph, int playerBase, long epoch,
        Func<string, LyraCardinalDirection, LyraCycleAsset> cycleAsset, Func<bool, int> hipFireAsset,
        AlsAssetSyncSequence[] sequences, ulong[] masks, double clampMin, double clampMax)
    {
        if (playerBase < 0 || epoch <= 0 || masks.Length != sequences.Length)
            throw new ArgumentException("Invalid linked Cycle source owner.");
        _graph = graph; _playerBase = playerBase; _epoch = epoch; _sequences = sequences; _masks = masks;
        _hipFireAsset = hipFireAsset; _cycleAsset = cycleAsset;
        _cycle = new(graph.Cycle, checked(playerBase + graph.Cycle.Index), epoch, cycleAsset, clampMin, clampMax);
        BlendWeight = graph.InitialBlendWeight;
    }

    internal bool InitializeSourceNode(int node)
    {
        if (_pending is not null) throw new InvalidOperationException("Cycle phase initialization needs an idle layer.");
        if(node==_graph.Cycle.Index) _cycle.InitializeSource();
        else if(node==_graph.HipFire.Index)
            HipFire = HipFire with { Marker = HipFire.Marker with { PreviousIndex=-2, NextIndex=-2, Initialized=false }, ResetPending=true };
        else return false;
        return true;
    }
    public LyraCycleLayerSourceCandidate Prepare(in LyraCycleInput input, float delta, float weight,
        double hipFireWeight, bool active, bool reinitialize, int sampleStart = 0)
    {
        if (_pending is not null) throw new InvalidOperationException("Cycle layer candidate is still pending.");
        if (!float.IsFinite(delta) || delta < 0 || !float.IsFinite(weight) || weight < 0 ||
            !double.IsFinite(hipFireWeight) || !float.IsFinite((float)hipFireWeight) || sampleStart < 0)
            throw new ArgumentException("Invalid Cycle layer traversal context.");
        try
        {
            var blend = active ? (float)hipFireWeight : BlendWeight;
            // AssetPlayer.Initialize resets markers/full-weight history,
            // while its cached blend weight survives until the next Update.
            var cycleWeight = CycleWeight;
            var cachedHipFireWeight = HipFireWeight;
            var hipFire = reinitialize ? HipFire with { Marker = HipFire.Marker with
                { PreviousIndex = -2, NextIndex = -2, Initialized = false }, ResetPending = true } : HipFire;
            var hipFireTicked = active && blend > 1e-5f;
            var count = active ? (hipFireTicked ? 2 : 1) : 0;
            var players = new AlsAssetSyncPlayer[count]; var groups = new int[count]; var samples = new AlsAssetSyncSample[count];
            var cycleSample = sampleStart + (hipFireTicked && !_graph.BaseFirst ? 1 : 0);
            var selected = active ? _cycleAsset(input.Crouching ? "Crouch_Walk_Cardinals" : input.Ads ? "Walk_Cardinals" : "Jog_Cardinals", input.Direction) : default;
            var cycle = active ? _cycle.Prepare(input, delta, weight, cycleSample, _masks[selected.Id], reinitialize) : _cycle.PrepareInactive(reinitialize);
            if (active)
            {
                cycleWeight = weight;
                var player = cycle.Player;
                var local = cycleSample - sampleStart; players[local] = player; groups[local] = 0;
                samples[local] = new(player.PlayerId, cycle.State.AssetId, 1);
            }
            if (hipFireTicked)
            {
                var asset = _hipFireAsset(input.Crouching); cachedHipFireWeight = weight * blend;
                if ((uint)asset >= _sequences.Length) throw new InvalidOperationException("Missing HipFire source.");
                var marker = hipFire.AssetId == asset ? hipFire.Marker : hipFire.Marker with
                    { PreviousIndex = -2, NextIndex = -2, Initialized = false };
                var local = _graph.BaseFirst ? 1 : 0;
                var prepared = LyraEvaluatorSourceTick.Prepare(_graph.HipFire, checked(_playerBase + _graph.HipFire.Index), asset,
                    _epoch, hipFire.Time, _graph.HipFire.Settings.GetProperty("explicitTime").GetSingle(), _sequences[asset],
                    delta, cachedHipFireWeight, sampleStart + local, _masks[asset], reinitialize || hipFire.ResetPending, markerRecord: marker);
                players[local] = prepared.Player; groups[local] = -1; samples[local] = new(prepared.Player.PlayerId, asset, 1);
                hipFire = hipFire with { AssetId = asset, Time = prepared.Preparation.Time, Marker = marker, ResetPending = false };
            }
            return _pending = new(cycle, hipFire, players, groups, samples, blend, cycleWeight, cachedHipFireWeight, hipFireTicked);
        }
        catch { _cycle.Cancel(); throw; }
    }

    public void Commit(LyraCycleLayerSourceCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {
        var resolved = Resolve(candidate, outputs);
        if (candidate.Cycle.Ticked) _cycle.Commit(candidate.Cycle, resolved.CycleOutput); else _cycle.CommitInactive(candidate.Cycle);
        HipFire = resolved.HipFire; BlendWeight = candidate.BlendWeight; CycleWeight = candidate.CycleWeight; HipFireWeight = candidate.HipFireWeight;
        _pending = null;
    }

    // Resolving common Sync records validates the whole dependency set without
    // publishing clocks. Pose evaluation must happen inside the same candidate.
    internal LyraCycleLayerResolvedSources Resolve(LyraCycleLayerSourceCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {
        if (!ReferenceEquals(candidate, _pending)) throw new InvalidOperationException("Rejected stale Cycle layer candidate.");
        var cycleOutput = default(AlsAssetPlayerHistory); var hipFireOutput = default(AlsAssetPlayerHistory);
        var cycleFound = false; var hipFireFound = false;
        var cursor = 0;
        foreach (var output in outputs)
        {
            if (output.SampleStart != cursor || output.SampleCount <= 0)
                throw new InvalidOperationException("Invalid common Sync output ranges.");
            cursor = checked(cursor + output.SampleCount);
            if (output.PlayerId == _playerBase + _graph.Cycle.Index)
            { if (cycleFound) throw new InvalidOperationException("Duplicate Cycle result."); cycleOutput = output; cycleFound = true; }
            if (output.PlayerId == _playerBase + _graph.HipFire.Index)
            { if (hipFireFound) throw new InvalidOperationException("Duplicate HipFire result."); hipFireOutput = output; hipFireFound = true; }
        }
        if (cycleFound != candidate.Cycle.Ticked || hipFireFound != candidate.HipFireTicked)
            throw new InvalidOperationException("Incomplete Cycle source results.");
        if (cycleFound)
        {
            // Batch groups reorder global output ranges. Rebase only the
            // sample offset before the occurrence validates its own record.
            cycleOutput = cycleOutput with { SampleStart = candidate.Cycle.Player.SampleStart };
            _cycle.ValidateCommit(candidate.Cycle, cycleOutput);
        }
        else _cycle.ValidateInactiveCommit(candidate.Cycle);
        var hipFire = candidate.HipFire;
        if (hipFireFound)
        {
            if (hipFireOutput.AssetId != hipFire.AssetId || hipFireOutput.Epoch != _epoch || hipFireOutput.SampleCount != 1 ||
                !float.IsFinite(hipFireOutput.Time) || hipFireOutput.Time < 0 || hipFireOutput.Time > _sequences[hipFire.AssetId].DurationSeconds ||
                !float.IsFinite(hipFireOutput.DeltaPrevious) || !float.IsFinite(hipFireOutput.Delta) ||
                !float.IsFinite(hipFireOutput.Marker.PreviousDistance) || !float.IsFinite(hipFireOutput.Marker.NextDistance))
                throw new InvalidOperationException("Rejected foreign HipFire result.");
            hipFire = hipFire with { Time = hipFireOutput.Time, Marker = hipFireOutput.Marker,
                DeltaPrevious = hipFireOutput.DeltaPrevious, Delta = hipFireOutput.Delta };
        }
        var cycle = cycleFound ? candidate.Cycle.State with { Time = cycleOutput.Time, Marker = cycleOutput.Marker,
            DeltaPrevious = cycleOutput.DeltaPrevious, Delta = cycleOutput.Delta } : candidate.Cycle.State;
        return new(cycleOutput, cycle, hipFire);
    }
    public void Cancel() { _cycle.Cancel(); _pending = null; }
}
