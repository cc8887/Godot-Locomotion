using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// Only the three asset-owning Main states. Cache initialization and Slot remain with BaseLayer.
internal sealed class AlsMainGroundedSourceCollector
{
    private readonly AlsLocomotionSourceProfile _sources;
    private readonly int[] _players = Enumerable.Repeat(-1, 8).ToArray();

    public AlsMainGroundedSourceCollector(AlsLocomotionSourceProfile sources, IReadOnlyList<AlsMainGroundedPoseState> states)
    {
        if (states.Count != 8) throw new ArgumentException("Main source state layout differs.");
        _sources = sources;
        var bindings = sources.CreateCoreView();
        for (var state = 0; state < states.Count; state++)
        {
            if (states[state].StateIndex != state) throw new ArgumentException("Main states are not indexed by native identity.");
            if (states[state].Kind != AlsMainGroundedPoseKind.Source) continue;
            var id = states[state].PlayerId;
            if (state is not (3 or 4 or 7) || (uint)id >= bindings.Players.Length)
                throw new ArgumentException("Unknown Main source owner.");
            var player = bindings.Players[id];
            if (player.Domain != AlsLocomotionSourceDomain.MainGrounded || player.SampleCount != 1 ||
                player.Kind != (state == 7 ? AlsLocomotionSourceKind.TeleportEvaluator : AlsLocomotionSourceKind.Sequence))
                throw new ArgumentException("Main source kind differs.");
            _players[state] = id;
        }
        if (_players.Count(p => p >= 0) != 3 || _players.Where(p => p >= 0).Distinct().Count() != 3)
            throw new ArgumentException("Main requires three independent sources.");
    }

    public void ObserveAutomatic(in AlsCycleSyncFrame previous, Span<AlsGroundedAutomaticTime> output)
    {
        if (output.Length != 8) throw new ArgumentException("Main automatic observation layout differs.");
        var bindings = _sources.CreateCoreView();
        output.Clear();
        for (var state = 0; state < _players.Length; state++)
        {
            var id = _players[state];
            if (id < 0 || previous.CachedWeights[id] <= 0 || previous.Epochs[id] <= 0) continue;
            var player = bindings.Players[id]; var sample = bindings.Samples[player.SampleStart];
            output[state] = new(true, sample.DurationSeconds, previous.Times[id], player.Loop, false, 0, 0);
            if (player.Kind == AlsLocomotionSourceKind.TeleportEvaluator) continue;
            for (var i = 0; i < previous.PlayerCount; i++)
                if (previous.Players[i].PlayerId == id && previous.Players[i].Epoch == previous.Epochs[id])
                {
                    var history = previous.Players[i];
                    output[state] = output[state] with { PreviousValid = true, PreviousTime = history.DeltaPrevious, Delta = history.Delta };
                    break;
                }
        }
    }

    public void Collect(in AlsGroundedMachineUpdate update, ref AlsCycleSyncFrame candidate,
        Span<AlsLocomotionSourceUpdate> players, Span<AlsLocomotionSampleUpdate> samples, Span<bool> active,
        ref int playerCount, ref int sampleCount, bool sourceActive = true, bool inertializationSync = false)
    {
        if (update.State.Kind != AlsGroundedMachineKind.Main || !update.State.HasUpdated ||
            (uint)playerCount > players.Length || (uint)sampleCount > samples.Length || active.Length != players.Length)
            throw new ArgumentException("Invalid Main source candidate.");
        var bindings = _sources.CreateCoreView();
        for (var state = 0; state < _players.Length; state++)
            if (_players[state] >= 0 && (update.ClearCachedWeightStates & (1 << state)) != 0)
                candidate.CachedWeights[_players[state]] = 0;
        for (var i = 0; i < update.InitializationCount; i++)
        {
            var id = _players[update.GetInitialization(i)];
            if (id < 0) continue;
            var player = bindings.Players[id]; var sample = bindings.Samples[player.SampleStart];
            candidate.Epochs[id] = AlsAssetSourceInitialization.NextEpoch(candidate.Epochs[id]);
            candidate.CachedWeights[id] = 0;
            candidate.Times[id] = player.Kind == AlsLocomotionSourceKind.TeleportEvaluator ? player.StartPosition :
                AlsAssetSourceInitialization.Time(AlsAssetSyncKind.Sequence, player.StartPosition, sample.DurationSeconds,
                    player.DefaultPlayRate, player.PlayRateBasis, sample.AssetRateScale);
        }
        for (var i = 0; i < update.UpdateCount; i++)
        {
            var child = update.GetUpdate(i); var id = _players[child.State];
            if (id < 0) continue;
            var player = bindings.Players[id];
            if (candidate.Epochs[id] <= 0) throw new InvalidOperationException("Main source was not initialized.");
            candidate.CachedWeights[id] = child.Weight;
            if (player.Kind == AlsLocomotionSourceKind.TeleportEvaluator) continue;
            if (playerCount == players.Length || sampleCount == samples.Length) throw new InvalidOperationException("Main source capacity exceeded.");
            samples[sampleCount] = new(player.SampleStart, 1, 1);
            players[playerCount] = new(id, candidate.Epochs[id], candidate.Times[id], child.Weight, sampleCount++, 1,
                child.InertializationSync || inertializationSync);
            active[playerCount++] = sourceActive && child.State == update.State.CurrentState;
        }
    }
}
