using GodotAls.Core.Contracts;

namespace GodotAls.Core.Actions;

public readonly record struct AlsMontagePlaybackBinding(AlsAuthoredMontageAsset Asset,
    int SectionId, int SegmentId, int OccurrenceHandleId);

// A projection of the shared owner, never another playback clock. The legacy
// scalar summary names a logical owner; physical fades remain queryable by ID.
public sealed class AlsMontageActionPlaybackReader
{
    private readonly Dictionary<int, AlsMontagePlaybackBinding> _bindings = new();

    public AlsMontageActionPlaybackReader(ReadOnlySpan<AlsMontagePlaybackBinding> bindings)
    {
        var handles = new HashSet<int>();
        foreach (var binding in bindings)
            if (binding.Asset.ActionDefinitionId < 0 || binding.SectionId < 0 || binding.SegmentId < 0 ||
                binding.OccurrenceHandleId < 0 || !handles.Add(binding.OccurrenceHandleId) ||
                !_bindings.TryAdd(binding.Asset.ActionDefinitionId, binding))
                throw new ArgumentException("Invalid or ambiguous montage playback binding.");
    }

    public AlsActionPlayback ReadOwned(AlsMontageActionRuntime actions, AlsFrameIdentity identity,
        AlsMontageSlot slot, bool slotVisited = true)
    {
        actions.ValidateCommit(identity);
        long instanceId = 0;
        foreach (var owner in actions.CandidateOwners)
        {
            if (owner.InstanceId <= 0) continue;
            if (!_bindings.TryGetValue(owner.DefinitionId, out var binding) || binding.Asset.GroupId != owner.GroupId)
                throw new InvalidOperationException("Action owner lacks its playback binding.");
            if (binding.Asset.Slot != slot) continue;
            if (instanceId != 0)
                throw new InvalidOperationException("Multiple owned actions cannot be collapsed into one slot summary.");
            instanceId = owner.InstanceId;
        }
        if (instanceId == 0) return AlsActionPlayback.CreateDefault();
        var result = ReadInstance(actions.Montages, identity, instanceId, slotVisited);
        if (result.Active == 0) throw new InvalidOperationException("Logical action lost its physical playback.");
        return result;
    }

    public AlsActionPlayback ReadInstance(AlsMontageRuntime montages, AlsFrameIdentity identity,
        long instanceId, bool slotVisited = true)
    {
        montages.ValidateCommit(identity);
        if (instanceId <= 0) throw new ArgumentOutOfRangeException(nameof(instanceId));
        foreach (var instance in montages.Candidate)
        {
            if (instance.InstanceId != instanceId) continue;
            if (!_bindings.TryGetValue(instance.ActionDefinitionId, out var binding) ||
                !montages.TryGetActionAsset(instance.ActionDefinitionId, out var asset) || asset != binding.Asset)
                throw new InvalidOperationException("Physical action differs from its playback binding.");
            var previous = instance.Position;
            var elapsed = 0f;
            foreach (var tick in montages.Traversal)
                if (tick.InstanceId == instanceId)
                {
                    previous = tick.PreviousPosition;
                    // Traversal reaches the endpoint even when native playback
                    // retains endpoint-epsilon for its last sampled pose.
                    elapsed = instance.PlayRate == 0 ? 0 : MathF.Abs((tick.CurrentPosition - previous) / instance.PlayRate);
                    break;
                }
            var weight = 0f;
            if (slotVisited)
                foreach (var evaluation in montages.Evaluation)
                    if (evaluation.InstanceId == instanceId)
                    {
                        weight = evaluation.Weight / MathF.Max(1, montages.SlotWeights(instance.Slot).TotalNodeWeight);
                        break;
                    }
            return new(binding.OccurrenceHandleId, instance.ActionDefinitionId, instance.AnimationId,
                binding.SectionId, binding.SegmentId, instanceId, previous, instance.Position,
                instance.ClipStart + previous * instance.ClipRate,
                instance.ClipStart + instance.Position * instance.ClipRate, elapsed,
                instance.PlayRate * instance.ClipRate, instance.BlendTime, weight, 1);
        }
        return AlsActionPlayback.CreateDefault();
    }
}
