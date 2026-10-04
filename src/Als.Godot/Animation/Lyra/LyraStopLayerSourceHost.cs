using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraStopLayerSourceCandidate(LyraStopCandidate Stop, LyraHipFireSourceState HipFire,
    AlsAssetSyncPlayer[] Players, int[] Groups, AlsAssetSyncSample[] Samples,
    float BlendWeight, float HipFireWeight, bool HipFireTicked);
internal readonly record struct LyraStopLayerResolvedSources(AlsAssetPlayerHistory StopOutput,
    LyraStopState Stop, LyraHipFireSourceState HipFire);

// Original source order and independent HipFire share the enclosing character
// Sync pass. Stop keeps its own evaluator and persistent relevance history.
internal sealed class LyraStopLayerSourceHost
{
    private readonly LyraStopLayerGraph _graph;
    private readonly LyraStopSourceRuntime _stop;
    private readonly Func<bool, int> _hipAsset;
    private readonly AlsAssetSyncSequence[] _sequences;
    private readonly ulong[] _masks;
    private readonly int _playerBase;
    private readonly long _epoch;
    private readonly int _groupId;
    private LyraStopLayerSourceCandidate? _pending;
    public LyraStopState Stop => _stop.State;
    public LyraHipFireSourceState HipFire { get; private set; } = new(-1, 0, AlsAssetMarkerRecord.Invalid);
    public float BlendWeight { get; private set; }
    public float HipFireWeight { get; private set; }
    public int SyncGroupId => _groupId;

    public LyraStopLayerSourceHost(LyraStopLayerGraph graph, int playerBase, long epoch,
        Func<string, LyraCardinalDirection, LyraStopAsset> resolve, Func<int, LyraStopAsset> byId,
        Func<bool, int> hipAsset, AlsAssetSyncSequence[] sequences, ulong[] masks, int groupId = 0)
    {
        if (playerBase < 0 || epoch <= 0 || groupId < 0 || sequences.Length != masks.Length) throw new ArgumentException("Invalid Stop owner.");
        _graph = graph; _playerBase = playerBase; _epoch = epoch; _sequences = sequences; _masks = masks; _hipAsset = hipAsset;
        _stop = new(graph.Stop, checked(playerBase+graph.Stop.Index), epoch, resolve, byId); _groupId = groupId;
        BlendWeight = graph.InitialBlendWeight;
    }
    internal bool InitializeSourceNode(int node)
    {
        if (_pending is not null) throw new InvalidOperationException("Stop phase initialization needs an idle layer.");
        if(node==_graph.Stop.Index) _stop.InitializeSource();
        else if(node==_graph.HipFire.Index)
            HipFire = HipFire with { Marker = HipFire.Marker with { PreviousIndex=-2, NextIndex=-2, Initialized=false }, ResetPending=true };
        else return false;
        return true;
    }
    public LyraStopLayerSourceCandidate Prepare(in LyraStopInput input, float delta, float weight,
        double hipWeight, bool active, bool reset, int sampleStart = 0)
    {
        if (_pending is not null) throw new InvalidOperationException("Stop layer candidate is pending.");
        if (!double.IsFinite(hipWeight) || !float.IsFinite((float)hipWeight) || sampleStart < 0)
            throw new ArgumentException("Invalid Stop layer context.");
        try
        {
            var blend = active ? (float)hipWeight : BlendWeight;
            var hip = reset ? HipFire with { Marker = HipFire.Marker with { PreviousIndex = -2, NextIndex = -2, Initialized = false }, ResetPending = true } : HipFire;
            var hipTicked = active && blend > 1e-5f;
            var count = active ? hipTicked ? 2 : 1 : 0;
            var players = new AlsAssetSyncPlayer[count]; var groups = new int[count]; var samples = new AlsAssetSyncSample[count];
            var local = hipTicked && !_graph.BaseFirst ? 1 : 0;
            var stop = _stop.Prepare(input, delta, weight, sampleStart+local, reset, active);
            if (active) { players[local] = stop.Tick.Player; groups[local] = _groupId; samples[local] = new(stop.Tick.Player.PlayerId, stop.State.AssetId, 1); }
            var cachedWeight = HipFireWeight;
            if (hipTicked)
            {
                var id = _hipAsset(input.Crouching); cachedWeight = weight*blend;
                if ((uint)id >= _sequences.Length) throw new InvalidOperationException("Missing Stop HipFire resource.");
                var marker = hip.AssetId == id ? hip.Marker : hip.Marker with { PreviousIndex = -2, NextIndex = -2, Initialized = false };
                local = _graph.BaseFirst ? 1 : 0;
                var tick = LyraEvaluatorSourceTick.Prepare(_graph.HipFire, checked(_playerBase+_graph.HipFire.Index), id, _epoch,
                    hip.Time, _graph.HipFire.Settings.GetProperty("explicitTime").GetSingle(), _sequences[id], delta,
                    cachedWeight, sampleStart+local, _masks[id], reset || hip.ResetPending, markerRecord: marker);
                players[local] = tick.Player; groups[local] = -1; samples[local] = new(tick.Player.PlayerId, id, 1);
                hip = hip with { AssetId = id, Time = tick.Preparation.Time, Marker = marker, ResetPending = false };
            }
            return _pending = new(stop, hip, players, groups, samples, blend, cachedWeight, hipTicked);
        }
        catch { _stop.Cancel(); throw; }
    }
    internal LyraStopLayerResolvedSources Resolve(LyraStopLayerSourceCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {
        if (!ReferenceEquals(candidate, _pending)) throw new InvalidOperationException("Rejected stale Stop layer candidate.");
        AlsAssetPlayerHistory stop = default, hip = default; var startFound = false; var hipFound = false; var cursor = 0;
        foreach (var output in outputs)
        {
            if (output.SampleStart != cursor || output.SampleCount <= 0) throw new InvalidOperationException("Invalid common Stop Sync ranges.");
            cursor = checked(cursor+output.SampleCount);
            if (output.PlayerId == _playerBase+_graph.Stop.Index)
            { if (startFound) throw new InvalidOperationException("Duplicate Stop result."); stop = output; startFound = true; }
            if (output.PlayerId == _playerBase+_graph.HipFire.Index)
            { if (hipFound) throw new InvalidOperationException("Duplicate Stop HipFire result."); hip = output; hipFound = true; }
        }
        if (startFound != candidate.Stop.Active || hipFound != candidate.HipFireTicked)
            throw new InvalidOperationException("Incomplete Stop layer Sync results.");
        if (startFound) { stop = stop with { SampleStart = candidate.Stop.Tick.Player.SampleStart }; _stop.ValidateCommit(candidate.Stop, stop); }
        else _stop.ValidateInactiveCommit(candidate.Stop);
        var h = candidate.HipFire;
        if (hipFound)
        {
            var sequence = _sequences[h.AssetId];
            if (hip.AssetId != h.AssetId || hip.Epoch != _epoch || hip.SampleCount != 1 || !float.IsFinite(hip.Time) ||
                hip.Time < 0 || hip.Time > sequence.DurationSeconds || !float.IsFinite(hip.DeltaPrevious) || !float.IsFinite(hip.Delta) ||
                !float.IsFinite(hip.Marker.PreviousDistance) || !float.IsFinite(hip.Marker.NextDistance) ||
                hip.Marker.Initialized && (hip.Marker.PreviousIndex < -1 || hip.Marker.NextIndex < -1 ||
                    hip.Marker.PreviousIndex >= sequence.MarkerCount || hip.Marker.NextIndex >= sequence.MarkerCount))
                throw new InvalidOperationException("Rejected foreign Stop HipFire result.");
            h = h with { Time = hip.Time, Marker = hip.Marker, DeltaPrevious = hip.DeltaPrevious, Delta = hip.Delta };
        }
        return new(stop, startFound ? candidate.Stop.State with { Time = stop.Time, Marker = stop.Marker,
            DeltaPrevious = stop.DeltaPrevious, Delta = stop.Delta } : candidate.Stop.State, h);
    }
    public void Commit(LyraStopLayerSourceCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {
        var resolved = Resolve(candidate, outputs);
        if (candidate.Stop.Active) _stop.Commit(candidate.Stop, resolved.StopOutput); else _stop.CommitInactive(candidate.Stop);
        HipFire = resolved.HipFire; BlendWeight = candidate.BlendWeight; HipFireWeight = candidate.HipFireWeight; _pending = null;
    }
    public void Cancel() { _stop.Cancel(); _pending = null; }
}
