using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraStartLayerSourceCandidate(LyraStartCandidate Start, LyraHipFireSourceState HipFire,
    AlsAssetSyncPlayer[] Players, int[] Groups, AlsAssetSyncSample[] Samples,
    float BlendWeight, float HipFireWeight, bool HipFireTicked);
internal readonly record struct LyraStartLayerResolvedSources(AlsAssetPlayerHistory StartOutput,
    LyraStartState Start, LyraHipFireSourceState HipFire);

// Original source order and independent HipFire share the enclosing character
// Sync pass. Start keeps its own evaluator and persistent relevance history.
internal sealed class LyraStartLayerSourceHost
{
    private readonly LyraStartLayerGraph _graph;
    private readonly LyraStartSourceRuntime _start;
    private readonly Func<bool, int> _hipAsset;
    private readonly AlsAssetSyncSequence[] _sequences;
    private readonly ulong[] _masks;
    private readonly int _playerBase;
    private readonly long _epoch;
    private LyraStartLayerSourceCandidate? _pending;
    public LyraStartState Start => _start.State;
    internal LyraStartState PreparedStart => _pending?.Start.State ?? Start;
    public LyraHipFireSourceState HipFire { get; private set; } = new(-1, 0, AlsAssetMarkerRecord.Invalid);
    public float BlendWeight { get; private set; }
    public float HipFireWeight { get; private set; }

    public LyraStartLayerSourceHost(LyraStartLayerGraph graph, int playerBase, long epoch,
        Func<string, LyraCardinalDirection, LyraStartAsset> resolve, Func<int, LyraStartAsset> byId, LyraStartPolicy policy,
        Func<bool, int> hipAsset, AlsAssetSyncSequence[] sequences, ulong[] masks)
    {
        if (playerBase < 0 || epoch <= 0 || sequences.Length != masks.Length) throw new ArgumentException("Invalid Start owner.");
        _graph = graph; _playerBase = playerBase; _epoch = epoch; _sequences = sequences; _masks = masks; _hipAsset = hipAsset;
        _start = new(graph.Start, checked(playerBase+graph.Start.Index), epoch, resolve, byId, policy);
        BlendWeight = graph.InitialBlendWeight;
    }
    internal bool InitializeSourceNode(int node)
    {
        if (_pending is not null) throw new InvalidOperationException("Start phase initialization needs an idle layer.");
        if(node==_graph.Start.Index) _start.InitializeSource();
        else if(node==_graph.HipFire.Index)
            HipFire = HipFire with { Marker = HipFire.Marker with { PreviousIndex=-2, NextIndex=-2, Initialized=false }, ResetPending=true };
        else return false;
        return true;
    }
    public LyraStartLayerSourceCandidate Prepare(in LyraStartInput input, float delta, float weight,
        double hipWeight, bool active, bool reset, int sampleStart = 0)
    {
        if (_pending is not null) throw new InvalidOperationException("Start layer candidate is pending.");
        if (!double.IsFinite(hipWeight) || !float.IsFinite((float)hipWeight) || sampleStart < 0)
            throw new ArgumentException("Invalid Start layer context.");
        try
        {
            var blend = active ? (float)hipWeight : BlendWeight;
            var hip = reset ? HipFire with { Marker = HipFire.Marker with { PreviousIndex = -2, NextIndex = -2, Initialized = false }, ResetPending = true } : HipFire;
            var hipTicked = active && blend > 1e-5f;
            var count = active ? hipTicked ? 2 : 1 : 0;
            var players = new AlsAssetSyncPlayer[count]; var groups = new int[count]; var samples = new AlsAssetSyncSample[count];
            var local = hipTicked && !_graph.BaseFirst ? 1 : 0;
            var start = _start.Prepare(input, delta, weight, sampleStart+local, reset, active);
            if (active) { players[local] = start.Tick.Player; groups[local] = 0; samples[local] = new(start.Tick.Player.PlayerId, start.State.AssetId, 1); }
            var cachedWeight = HipFireWeight;
            if (hipTicked)
            {
                var id = _hipAsset(input.Crouching); cachedWeight = weight*blend;
                if ((uint)id >= _sequences.Length) throw new InvalidOperationException("Missing Start HipFire resource.");
                var marker = hip.AssetId == id ? hip.Marker : hip.Marker with { PreviousIndex = -2, NextIndex = -2, Initialized = false };
                local = _graph.BaseFirst ? 1 : 0;
                var tick = LyraEvaluatorSourceTick.Prepare(_graph.HipFire, checked(_playerBase+_graph.HipFire.Index), id, _epoch,
                    hip.Time, _graph.HipFire.Settings.GetProperty("explicitTime").GetSingle(), _sequences[id], delta,
                    cachedWeight, sampleStart+local, _masks[id], reset || hip.ResetPending, markerRecord: marker);
                players[local] = tick.Player; groups[local] = -1; samples[local] = new(tick.Player.PlayerId, id, 1);
                hip = hip with { AssetId = id, Time = tick.Preparation.Time, Marker = marker, ResetPending = false };
            }
            return _pending = new(start, hip, players, groups, samples, blend, cachedWeight, hipTicked);
        }
        catch { _start.Cancel(); throw; }
    }
    internal LyraStartLayerResolvedSources Resolve(LyraStartLayerSourceCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {
        if (!ReferenceEquals(candidate, _pending)) throw new InvalidOperationException("Rejected stale Start layer candidate.");
        AlsAssetPlayerHistory start = default, hip = default; var startFound = false; var hipFound = false; var cursor = 0;
        foreach (var output in outputs)
        {
            if (output.SampleStart != cursor || output.SampleCount <= 0) throw new InvalidOperationException("Invalid common Start Sync ranges.");
            cursor = checked(cursor+output.SampleCount);
            if (output.PlayerId == _playerBase+_graph.Start.Index)
            { if (startFound) throw new InvalidOperationException("Duplicate Start result."); start = output; startFound = true; }
            if (output.PlayerId == _playerBase+_graph.HipFire.Index)
            { if (hipFound) throw new InvalidOperationException("Duplicate Start HipFire result."); hip = output; hipFound = true; }
        }
        if (startFound != candidate.Start.Active || hipFound != candidate.HipFireTicked)
            throw new InvalidOperationException("Incomplete Start layer Sync results.");
        if (startFound) { start = start with { SampleStart = candidate.Start.Tick.Player.SampleStart }; _start.ValidateCommit(candidate.Start, start); }
        else _start.ValidateInactiveCommit(candidate.Start);
        var h = candidate.HipFire;
        if (hipFound)
        {
            var sequence = _sequences[h.AssetId];
            if (hip.AssetId != h.AssetId || hip.Epoch != _epoch || hip.SampleCount != 1 || !float.IsFinite(hip.Time) ||
                hip.Time < 0 || hip.Time > sequence.DurationSeconds || !float.IsFinite(hip.DeltaPrevious) || !float.IsFinite(hip.Delta) ||
                !float.IsFinite(hip.Marker.PreviousDistance) || !float.IsFinite(hip.Marker.NextDistance) ||
                hip.Marker.Initialized && (hip.Marker.PreviousIndex < -1 || hip.Marker.NextIndex < -1 ||
                    hip.Marker.PreviousIndex >= sequence.MarkerCount || hip.Marker.NextIndex >= sequence.MarkerCount))
                throw new InvalidOperationException("Rejected foreign Start HipFire result.");
            h = h with { Time = hip.Time, Marker = hip.Marker, DeltaPrevious = hip.DeltaPrevious, Delta = hip.Delta };
        }
        return new(start, startFound ? candidate.Start.State with { Time = start.Time, Marker = start.Marker,
            DeltaPrevious = start.DeltaPrevious, Delta = start.Delta } : candidate.Start.State, h);
    }
    public void Commit(LyraStartLayerSourceCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {
        var resolved = Resolve(candidate, outputs);
        if (candidate.Start.Active) _start.Commit(candidate.Start, resolved.StartOutput); else _start.CommitInactive(candidate.Start);
        HipFire = resolved.HipFire; BlendWeight = candidate.BlendWeight; HipFireWeight = candidate.HipFireWeight; _pending = null;
    }
    public void Cancel() { _start.Cancel(); _pending = null; }
}
