using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public sealed record AlsCrouchingStateDefinition(AlsGroundedMachineDefinition Machine, int MachineNodeIndex,
    AlsPoseCacheDefinition Caches, int CycleCacheNodeIndex, int CycleMachineNodeIndex, int MovingReadNodeIndex,
    int StopReadNodeIndex, int SlotNodeIndex, int IdlePlayerId, int RotateLeftPlayerId, int RotateRightPlayerId,
    int StopLeftPlayerId, int StopRightPlayerId);

public readonly record struct AlsCrouchingStateFrame(AlsFrameIdentity Identity, AlsGroundedMachineState Machine);
public readonly record struct AlsCrouchingStateUpdate(AlsCrouchingStateFrame State, AlsGroundedMachineUpdate Machine);

/// <summary>Callbacks operate on the owner's candidate only. Slot lifecycle/weights, cache counters,
/// source epochs, Cycles and synchronization remain owned by that same transaction.</summary>
public interface IAlsCrouchingStateUpdateSink
{
    void ObserveStateInitialization(in AlsCrouchingStateUpdate initialization) { }
    void ObserveStateUpdate(in AlsCrouchingStateUpdate update) { }
    void ClearSourceWeights(byte states);
    void InitializeSource(int playerId);
    void InitializeCycleCache(int readNodeIndex);
    void InitializeSlot(int slotNodeIndex, int sourcePlayerId);
    void UpdateSource(int playerId, in AlsPoseUpdateContext context);
    void UpdateSlot(int slotNodeIndex, int sourcePlayerId, in AlsPoseUpdateContext context);
    void UseCycleCache(int readNodeIndex, in AlsPoseUpdateContext context);
    void RequestInertialization(in AlsPoseUpdateContext context, float seconds);
}

public sealed class AlsCrouchingStateRuntime
{
    private readonly AlsCrouchingStateDefinition _definition;
    private IAlsCrouchingStateUpdateSink? _sink;

    public AlsCrouchingStateRuntime(AlsCrouchingStateDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Machine.Kind != AlsGroundedMachineKind.Crouching || definition.Machine.States.Length != 5 ||
            definition.MachineNodeIndex < 0 || definition.CycleMachineNodeIndex < 0 || definition.SlotNodeIndex < 0 ||
            definition.Caches.UpdateOrder.Length != 1 || definition.Caches.UpdateOrder[0] != definition.CycleCacheNodeIndex ||
            definition.Caches.Reads.Length != 2 || definition.MovingReadNodeIndex == definition.StopReadNodeIndex)
            throw new ArgumentException("Invalid Crouching state graph definition.");
        foreach (var read in definition.Caches.Reads)
            if (read.CacheNodeIndex != definition.CycleCacheNodeIndex ||
                read.ReadNodeIndex != definition.MovingReadNodeIndex && read.ReadNodeIndex != definition.StopReadNodeIndex)
                throw new ArgumentException("Crouching state cache binding differs.");
        int[] players = [definition.IdlePlayerId, definition.RotateLeftPlayerId, definition.RotateRightPlayerId,
            definition.StopLeftPlayerId, definition.StopRightPlayerId];
        if (players.Any(p => p < 0) || players.Distinct().Count() != players.Length)
            throw new ArgumentException("Crouching states require independent source identities.");
        _definition = definition;
    }

    public AlsCrouchingStateUpdate Initialize(AlsFrameIdentity identity, IAlsCrouchingStateUpdateSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (_sink is not null || identity.SlotGeneration == 0)
            throw new ArgumentException("Invalid Crouching initialization owner.");
        _sink = sink;
        try
        {
            var machine = AlsGroundedStateMachine.Initialize(_definition.Machine);
            var initialization = new AlsCrouchingStateUpdate(new(identity, machine.State), machine);
            sink.ClearSourceWeights(machine.ClearCachedWeightStates);
            for (var i = 0; i < machine.InitializationCount; i++) Initialize(machine.GetInitialization(i), sink);
            sink.ObserveStateInitialization(initialization);
            return initialization;
        }
        finally { _sink = null; }
    }

    public AlsCrouchingStateUpdate Prepare(in AlsCrouchingStateFrame previous, in AlsGroundedRuleInput rules,
        ReadOnlySpan<AlsGroundedAutomaticTime> automaticTimes, in AlsPoseUpdateContext context, IAlsCrouchingStateUpdateSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (_sink is not null || context.Identity.SlotGeneration == 0 || previous.Machine.HasInitialized &&
            (previous.Identity.CharacterId != context.Identity.CharacterId || previous.Identity.SlotGeneration != context.Identity.SlotGeneration ||
                previous.Identity.FrameId > context.Identity.FrameId ||
                previous.Machine.HasUpdated && previous.Identity.FrameId != previous.Machine.LastUpdateSerial))
            throw new ArgumentException("Invalid Crouching state candidate owner.");
        var update = AlsGroundedStateMachine.Update(_definition.Machine, previous.Machine, rules, automaticTimes,
            context.Weight, context.Delta, context.Identity.FrameId, updateCounter:context.UpdateCounter);
        _sink = sink;
        try
        {
            sink.ClearSourceWeights(update.ClearCachedWeightStates);
            // Preserve repeated entries and baked transition order; the bitmask alone is insufficient.
            for (var i = 0; i < update.InitializationCount; i++) Initialize(update.GetInitialization(i), sink);
            sink.ObserveStateUpdate(new(new(context.Identity, update.State), update));
            if (update.InertializationSeconds >= 0) sink.RequestInertialization(context, update.InertializationSeconds);
            for (var i = 0; i < update.UpdateCount; i++)
            {
                var child = update.GetUpdate(i);
                var path = context.WithWeight(child.Weight).WithState(_definition.MachineNodeIndex, child.State, child.InertializationSync);
                switch (child.State)
                {
                    case 0: sink.UpdateSlot(_definition.SlotNodeIndex, _definition.IdlePlayerId, path); break;
                    case 1: sink.UseCycleCache(_definition.MovingReadNodeIndex, path); break;
                    case 2: sink.UpdateSource(_definition.RotateLeftPlayerId, path); break;
                    case 3: sink.UpdateSource(_definition.RotateRightPlayerId, path); break;
                    case 4:
                        // Both leg masks exclude root. Updates precede BasePose in this native node.
                        var leg = path.WithWeight(path.Weight, 0);
                        sink.UpdateSource(_definition.StopLeftPlayerId, leg);
                        sink.UpdateSource(_definition.StopRightPlayerId, leg);
                        sink.UseCycleCache(_definition.StopReadNodeIndex, path);
                        break;
                    default: throw new InvalidOperationException("Invalid Crouching state update.");
                }
            }
            // BaseLayer owns the deferred queue. Draining here would reorder Cycles relative to Standing caches.
            return new(new(context.Identity, update.State), update);
        }
        finally { _sink = null; }
    }

    private void Initialize(int state, IAlsCrouchingStateUpdateSink sink)
    {
        switch (state)
        {
            case 0: sink.InitializeSlot(_definition.SlotNodeIndex, _definition.IdlePlayerId); break;
            case 1: sink.InitializeCycleCache(_definition.MovingReadNodeIndex); break;
            case 2: sink.InitializeSource(_definition.RotateLeftPlayerId); break;
            case 3: sink.InitializeSource(_definition.RotateRightPlayerId); break;
            case 4:
                // Initialize and CacheBones visit BasePose before the leg inputs, unlike Update.
                sink.InitializeCycleCache(_definition.StopReadNodeIndex);
                sink.InitializeSource(_definition.StopLeftPlayerId); sink.InitializeSource(_definition.StopRightPlayerId);
                break;
            default: throw new InvalidOperationException("Invalid Crouching state initialization.");
        }
    }
}
