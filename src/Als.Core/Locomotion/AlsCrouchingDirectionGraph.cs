using System.Numerics;

namespace GodotAls.Core.Locomotion;

public sealed class AlsCrouchingDirectionGraphDefinition
{
    private readonly AlsCrouchingDirectionPoseRow[] _rows;
    private readonly int[] _cacheNodes, _readNodes, _players;
    public AlsGroundedMachineDefinition Machine { get; }
    public int MachineNodeIndex { get; }
    public AlsPoseCacheDefinition Caches { get; }
    public ReadOnlySpan<AlsCrouchingDirectionPoseRow> Rows => _rows;
    public ReadOnlySpan<int> CacheNodes => _cacheNodes;
    public ReadOnlySpan<int> ReadNodes => _readNodes;
    public ReadOnlySpan<int> PlayerIds => _players;

    public AlsCrouchingDirectionGraphDefinition(AlsGroundedMachineDefinition machine, int machineNodeIndex,
        AlsPoseCacheDefinition caches, AlsCrouchingDirectionPoseRow[] rows, int[] cacheNodes, int[] readNodes, int[] players)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(caches);
        if (machine.Kind != AlsGroundedMachineKind.CrouchingDirection || machine.States.Length != 6 ||
            (uint)machineNodeIndex >= caches.NodeCount || rows.Length != 6 || cacheNodes.Length != 6 ||
            readNodes.Length != 24 || players.Length != 6 || players.Any(p => p < 0) || players.Distinct().Count() != 6 ||
            cacheNodes.Distinct().Count() != 6 || caches.UpdateOrder.Length != 6 || caches.Reads.Length != 24 ||
            readNodes.Distinct().Count() != 24 || cacheNodes.Contains(machineNodeIndex) || readNodes.Contains(machineNodeIndex))
            throw new ArgumentException("Invalid Crouching direction update layout.");
        for (var state = 0; state < 6; state++)
        for (var axis = 0; axis < 4; axis++)
        {
            var direction = rows[state].Cache(axis);
            if ((uint)direction >= 6 || !caches.UpdateOrder.Contains(cacheNodes[direction]) ||
                !caches.Reads.Contains(new AlsPoseCacheReadBinding(readNodes[state * 4 + axis], cacheNodes[direction])))
                throw new ArgumentException("Crouching direction read differs from its pose source.");
        }
        Machine = machine; MachineNodeIndex = machineNodeIndex; Caches = caches;
        _rows = (AlsCrouchingDirectionPoseRow[])rows.Clone(); _cacheNodes = (int[])cacheNodes.Clone();
        _readNodes = (int[])readNodes.Clone(); _players = (int[])players.Clone();
    }
}

public interface IAlsCrouchingDirectionUpdateSink
{
    void UpdateSource(int direction, int playerId, in AlsPoseUpdateContext context);
    void OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped);
}

/// <summary>One linked direction-layer update. The caller gates relevance, owns committed machine history,
/// and submits the selected source updates to the shared Sync transaction.</summary>
public sealed class AlsCrouchingDirectionGraph : IAlsPoseCacheUpdateSink
{
    private readonly AlsCrouchingDirectionGraphDefinition _definition;
    private readonly AlsPoseCacheTraversal _caches;
    private IAlsCrouchingDirectionUpdateSink? _sink;
    public int CachedCallCount => _caches.CachedCallCount;
    public int SourceUpdateCount => _caches.SourceUpdateCount;

    public AlsCrouchingDirectionGraph(AlsCrouchingDirectionGraphDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition; _caches = new(definition.Caches, 24);
    }

    public AlsGroundedMachineUpdate Prepare(in AlsGroundedMachineState previous, in AlsGroundedRuleInput input,
        Vector4 velocity, in AlsPoseUpdateContext context, IAlsCrouchingDirectionUpdateSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (_sink is not null || context.Identity.SlotGeneration == 0 ||
            !float.IsFinite(velocity.LengthSquared()) || velocity.X < 0 || velocity.Y < 0 || velocity.Z < 0 || velocity.W < 0)
            throw new ArgumentException("Invalid Crouching direction update context.");
        Span<AlsGroundedAutomaticTime> times = stackalloc AlsGroundedAutomaticTime[6];
        times.Clear();
        var update = AlsGroundedStateMachine.Update(_definition.Machine, previous, input, times,
            context.Weight, context.Delta, context.Identity.FrameId, updateCounter:context.UpdateCounter);
        var total = velocity.X + velocity.Y + velocity.Z + velocity.W;
        velocity = total > AlsPoseBlender.WeightThreshold ? velocity / total : Vector4.Zero;
        _caches.Begin(context.Identity);
        _sink = sink;
        try
        {
            // State-machine update order includes interrupted and zero-weight states; pose blending
            // order alone cannot reconstruct the cached update's winning context.
            for (var i = 0; i < update.UpdateCount; i++)
            {
                var child = update.GetUpdate(i);
                var childContext = context.WithState(_definition.MachineNodeIndex, child.State, child.InertializationSync);
                for (var axis = 0; axis < 4; axis++)
                    if (velocity[axis] > AlsPoseBlender.WeightThreshold)
                        _caches.Use(_definition.ReadNodes[child.State * 4 + axis], childContext.WithWeight(child.Weight * velocity[axis]));
            }
            _caches.Drain(this);
            return update;
        }
        finally { _sink = null; }
    }

    void IAlsPoseCacheUpdateSink.UpdateCachedSource(int cacheNodeIndex, in AlsPoseUpdateContext context)
    {
        var direction = _definition.CacheNodes.IndexOf(cacheNodeIndex);
        _sink!.UpdateSource(direction, _definition.PlayerIds[direction], context);
    }

    void IAlsPoseCacheUpdateSink.OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped) =>
        _sink!.OnCachedUpdatesSkipped(handlerNodeIndex, skipped);
}
