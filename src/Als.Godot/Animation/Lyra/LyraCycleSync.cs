using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraCycleSyncSequence(string Slot, float Duration, float RateScale,
    AlsAssetSyncMarker[] Markers);

internal readonly record struct LyraCycleSyncCandidate(int AssetIndex, long Epoch, long Revision,
    AlsAssetSyncGroupHistory Group, AlsAssetPlayerHistory Player,
    AlsAssetSampleHistory Sample)
{
    public float Time => Player.Time;
    public float PreviousTime => Player.DeltaPrevious;
    public float Delta => Player.Delta;
    public AlsAssetMarkerPosition MarkerStart => Group.MarkerStart;
}

internal sealed class LyraCycleSync
{
    private const int LocomotionPlayerId = 0;
    private readonly Dictionary<string, int> _indices = new(StringComparer.Ordinal);
    private readonly AlsAssetSyncSequence[] _sequences;
    private readonly AlsAssetSyncMarker[] _markers;
    private readonly AlsAssetPlayerHistory[] _previousPlayers = new AlsAssetPlayerHistory[1];
    private readonly AlsAssetSampleHistory[] _previousSamples = new AlsAssetSampleHistory[1];
    private AlsAssetSyncGroupHistory _group;
    private long _epoch = 1;
    private long _revision;
    private int _lastAsset = -1;
    private bool _active;

    public LyraCycleSync(IReadOnlyList<LyraCycleSyncSequence> definitions)
    {
        var expectedSlots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var gait in new[] { "jog", "walk" })
        foreach (var direction in new[] { "fwd", "bwd", "left", "right" })
            expectedSlots.Add(gait + "_" + direction + "_cycle");
        if (definitions.Any(entry => entry.Slot.StartsWith("crouch_walk_", StringComparison.Ordinal)))
        foreach (var direction in new[] { "fwd", "bwd", "left", "right" })
            expectedSlots.Add("crouch_walk_" + direction);
        foreach (var item in new[] { "pistol", "rifle" })
        {
            if (!definitions.Any(entry => entry.Slot.StartsWith(item + "_", StringComparison.Ordinal))) continue;
            foreach (var gait in new[] { "jog", "walk", "crouch_walk" })
            foreach (var direction in new[] { "fwd", "bwd", "left", "right" })
                expectedSlots.Add(item + "_" + gait + "_" + direction + "_cycle");
        }
        if (definitions.Count != expectedSlots.Count ||
            !expectedSlots.SetEquals(definitions.Select(entry => entry.Slot)))
            throw new InvalidOperationException("Lyra Cycle requires a complete direction set for each bound layer.");
        _sequences = new AlsAssetSyncSequence[definitions.Count];
        var markers = new List<AlsAssetSyncMarker>();
        for (var index = 0; index < definitions.Count; index++)
        {
            var entry = definitions[index];
            if (!LyraUnarmedTiming.IsCycleSlot(entry.Slot) ||
                !float.IsFinite(entry.Duration) || entry.Duration <= 0 ||
                !float.IsFinite(entry.RateScale) || entry.RateScale <= 0 ||
                (entry.Markers.Length < 2 &&
                    (entry.Slot != "pistol_walk_right_cycle" ||
                     entry.Markers.Length != 1 || entry.Markers[0].Symbol != 1)) ||
                entry.Markers.Any(marker => marker.Symbol is not (1 or 2) ||
                    !float.IsFinite(marker.TimeSeconds) || marker.TimeSeconds < 0 ||
                    marker.TimeSeconds > entry.Duration) ||
                !entry.Markers.Any(marker => marker.Symbol == 1) ||
                (entry.Slot != "pistol_walk_right_cycle" &&
                    !entry.Markers.Any(marker => marker.Symbol == 2)) ||
                !_indices.TryAdd(entry.Slot, index))
                throw new InvalidOperationException($"Invalid Lyra Cycle marker track: {entry.Slot}.");
            _sequences[index] = new(index, entry.Duration, entry.RateScale, markers.Count,
                entry.Markers.Length);
            markers.AddRange(entry.Markers);
        }
        _markers = markers.ToArray();
    }

    public int ResourceSwitchCount { get; private set; }
    public AlsAssetMarkerPosition MarkerEnd => _group.MarkerEnd;

    public LyraCycleSyncCandidate Evaluate(string slot, double time, float rate, double delta)
    {
        if (!_indices.TryGetValue(slot, out var index) || !double.IsFinite(time) ||
            !float.IsFinite(rate) || rate < 0 || !double.IsFinite(delta) ||
            delta < 0 || delta > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(slot), "Invalid Lyra Cycle Sync input.");
        var sequence = _sequences[index];
        var markerMask = 0UL;
        foreach (var marker in _markers.AsSpan(sequence.MarkerStart, sequence.MarkerCount))
            markerMask |= 1UL << marker.Symbol;
        Span<AlsAssetSyncPlayer> players = stackalloc AlsAssetSyncPlayer[1];
        players[0] = new(LocomotionPlayerId, LocomotionPlayerId, _epoch, AlsAssetSyncKind.Sequence,
            (float)Math.Clamp(time, 0, sequence.DurationSeconds), rate, 1,
            0, 1, markerMask, RequestedInertialization: _active && _lastAsset != index,
            Role: AlsAssetSyncRole.AlwaysFollower);
        Span<AlsAssetSyncSample> samples = stackalloc AlsAssetSyncSample[1];
        samples[0] = new(0, index, 1);
        Span<AlsAssetPlayerHistory> previousPlayers = stackalloc AlsAssetPlayerHistory[1];
        if (_active)
        {
            previousPlayers[0] = _previousPlayers[0];
            // FAnimGroupInstance::Prepare resets the cached record when the source asset changes.
            if (_lastAsset != index)
                previousPlayers[0] = previousPlayers[0] with { Marker = AlsAssetMarkerRecord.Invalid };
        }
        Span<AlsAssetPlayerHistory> nextPlayers = stackalloc AlsAssetPlayerHistory[1];
        Span<AlsAssetSampleHistory> nextSamples = stackalloc AlsAssetSampleHistory[1];
        if (!AlsSyncRuntime.TryEvaluateAssetSyncGroup(0, _active ? _group : default,
                players, samples, _sequences, _markers,
                _active ? previousPlayers : [], _active ? _previousSamples : [],
                (float)delta, nextPlayers, nextSamples, out var group, out var failure))
            throw new InvalidOperationException($"Lyra Cycle Sync failed: {slot}/{failure}.");
        return new(index, _epoch, _revision, group, nextPlayers[0], nextSamples[0]);
    }

    public void Commit(in LyraCycleSyncCandidate candidate)
    {
        if (candidate.Epoch != _epoch || candidate.Revision != _revision ||
            candidate.AssetIndex < 0 ||
            candidate.AssetIndex >= _sequences.Length ||
            candidate.Player.PlayerId != LocomotionPlayerId ||
            candidate.Player.AssetId != LocomotionPlayerId ||
            candidate.Sample.AnimationId != candidate.AssetIndex)
            throw new InvalidOperationException("Stale Lyra Cycle Sync candidate.");
        if (_active && _lastAsset != candidate.AssetIndex) ResourceSwitchCount++;
        _group = candidate.Group;
        _previousPlayers[0] = candidate.Player;
        _previousSamples[0] = candidate.Sample;
        _lastAsset = candidate.AssetIndex;
        _active = true;
        _revision++;
    }

    public void Reset()
    {
        _active = false;
        _group = default;
        _lastAsset = -1;
        _epoch++;
        _revision++;
    }
}
