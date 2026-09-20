using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

internal sealed class AlsRagdollSharedSourceCollector
{
    private enum Phase { Idle, Collected, Complete }
    private Phase _phase;
    private readonly AlsRootSharedSourceProfile _profile;
    private readonly AlsRagdollFrameRuntime _runtime;
    private AlsFrameIdentity _identity;
    private bool _updated;
    public AlsRagdollSharedSourceCollector(AlsRootSharedSourceProfile profile, AlsRagdollFrameRuntime runtime)
    { _profile = profile; _runtime = runtime; }

    public void Collect(AlsFrameIdentity identity, ref AlsCycleSyncFrame candidate,
        Span<AlsLocomotionSourceUpdate> players, Span<AlsLocomotionSampleUpdate> samples, Span<bool> active,
        ref int playerCount, ref int sampleCount)
    {
        var request = _runtime.SourceRequest; var player = _profile.RagdollPlayerId; var sample = _profile.RagdollSampleId;
        if (_phase != Phase.Idle || identity != _runtime.Candidate.Traversal.Identity ||
            candidate.Initialized && candidate.BindingStamp != _profile.Sources.RuntimeStamp ||
            checked(candidate.Epochs[player] + request.Initializations) != request.Epoch ||
            request.Initializations == 0 && candidate.Times[player] != request.Time ||
            active.Length != players.Length || playerCount < 0 || sampleCount < 0 ||
            playerCount > players.Length || sampleCount > samples.Length ||
            request.Updated && (playerCount == players.Length || sampleCount == samples.Length))
            throw new ArgumentException("Ragdoll source collection differs from the shared candidate.");
        for (var i = 0; i < playerCount; i++) if (players[i].PlayerId == player) throw new ArgumentException("Repeated Ragdoll source contribution.");
        if (request.Initializations > 0) { candidate.Epochs[player] = request.Epoch; candidate.Times[player] = request.Time; }
        if (request.ClearCachedWeight) candidate.CachedWeights[player] = 0;
        if (request.Updated)
        {
            candidate.CachedWeights[player] = request.Context.Weight;
            players[playerCount] = new(player, request.Epoch, request.Time, request.Context.Weight, sampleCount, 1, request.Context.InertializationSync);
            samples[sampleCount++] = new(sample, 1, 1); active[playerCount++] = request.Context.IsActive;
        }
        _identity = identity; _updated = request.Updated; _phase = Phase.Collected;
    }
    public void Complete(AlsFrameIdentity identity, in AlsCycleSyncFrame synchronized)
    {
        if (_phase != Phase.Collected || identity != _identity || !synchronized.Initialized ||
            synchronized.BindingStamp != _profile.Sources.RuntimeStamp ||
            synchronized.Epochs[_profile.RagdollPlayerId] != _runtime.Candidate.PlayerEpoch)
            throw new ArgumentException("Ragdoll received a foreign shared source frame.");
        if (_updated)
        {
            var found = -1;
            for (var i = 0; i < synchronized.PlayerCount; i++)
                if (synchronized.Players[i].PlayerId == _profile.RagdollPlayerId)
                { if (found >= 0) throw new ArgumentException("Duplicate synchronized Ragdoll source."); found = i; }
            if (found < 0) throw new ArgumentException("Shared batch omitted the Ragdoll player.");
            var player = synchronized.Players[found];
            if (player.SampleCount != 1 || (uint)player.SampleStart >= synchronized.SampleCount ||
                player.Time != synchronized.Times[_profile.RagdollPlayerId]) throw new ArgumentException("Shared Ragdoll time/sample mapping differs.");
            _runtime.CompleteShared(identity, synchronized.BindingStamp, player, synchronized.Samples[player.SampleStart]);
        }
        else _runtime.CompleteShared(identity, synchronized.BindingStamp);
        _phase = Phase.Complete;
    }
    public void Discard() { _phase = Phase.Idle; _updated = false; }
}
