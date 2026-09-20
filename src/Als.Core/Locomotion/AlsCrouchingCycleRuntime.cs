using System.Numerics;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public sealed record AlsCrouchingCycleDefinition(AlsGroundedMachineDefinition Machine, int MachineNodeIndex,
    AlsCrouchingDirectionGraphDefinition Direction, AlsCrouchingStrideSettings Stride, int WalkPosePlayerId, int LeanPlayerId);

public readonly record struct AlsCrouchingCycleState(AlsGroundedMachineState Machine,
    AlsGroundedMachineState Direction, AlsCrouchingStrideState Stride, AlsFrameIdentity Identity = default,
    long InitializationEpoch = 0, float DiagonalAlpha = 0);
public readonly record struct AlsCrouchingCycleUpdate(AlsCrouchingCycleState State, bool InitializedSources,
    bool DirectionUpdated, AlsGroundedMachineUpdate DirectionUpdate);

/// <summary>All callbacks belong to the current candidate. Initialization resets source time/epoch
/// in the shared source owner; update only registers contributions, never advances a second clock.</summary>
public interface IAlsCrouchingCycleUpdateSink
{
    void InitializeSource(int playerId);
    void UpdateSource(int playerId, in AlsPoseUpdateContext context);
    void OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped);
}

public sealed class AlsCrouchingCycleRuntime : IAlsCrouchingDirectionUpdateSink
{
    private readonly AlsCrouchingCycleDefinition _definition;
    private readonly AlsCrouchingDirectionGraph _direction;
    private IAlsCrouchingCycleUpdateSink? _sink;

    public AlsCrouchingCycleRuntime(AlsCrouchingCycleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Machine.Kind != AlsGroundedMachineKind.CrouchingCycles || definition.Machine.States.Length != 1 ||
            definition.Machine.Edges.Length != 0 || definition.MachineNodeIndex < 0 || definition.WalkPosePlayerId < 0 ||
            definition.LeanPlayerId < 0 || definition.WalkPosePlayerId == definition.LeanPlayerId ||
            definition.Direction.PlayerIds.Contains(definition.WalkPosePlayerId) || definition.Direction.PlayerIds.Contains(definition.LeanPlayerId) ||
            !float.IsFinite(definition.Stride.IncreasingSpeed) || !float.IsFinite(definition.Stride.DecreasingSpeed))
            throw new ArgumentException("Invalid Crouching Cycles definition.");
        _definition = definition; _direction = new(definition.Direction);
    }

    public AlsCrouchingCycleUpdate Initialize(in AlsCrouchingCycleState previous, AlsFrameIdentity identity,
        IAlsCrouchingCycleUpdateSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ValidateOwner(previous, identity);
        var epoch = NextInitializationEpoch(previous.InitializationEpoch);
        _sink = sink;
        try
        {
            var machine = AlsGroundedStateMachine.Initialize(_definition.Machine);
            // TwoWayBlend initializes both pins, regardless of their later Update relevance.
            var direction = AlsGroundedStateMachine.Initialize(_definition.Direction.Machine);
            InitializeSources(sink);
            return new(new(machine.State, direction.State, default, identity, epoch, previous.DiagonalAlpha), true, false, direction);
        }
        finally { _sink = null; }
    }

    public AlsCrouchingCycleUpdate Prepare(in AlsCrouchingCycleState previous, in AlsGroundedRuleInput rules,
        float strideInput, Vector4 velocity, in AlsPoseUpdateContext context, IAlsCrouchingCycleUpdateSink sink,
        float diagonalAlpha = 1)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ValidateOwner(previous, context.Identity);
        if (!float.IsFinite(velocity.LengthSquared()) || !float.IsFinite(diagonalAlpha) ||
            velocity.X < 0 || velocity.Y < 0 || velocity.Z < 0 || velocity.W < 0)
            throw new ArgumentException("Invalid Crouching Cycles update.");
        Span<AlsGroundedAutomaticTime> automatic = stackalloc AlsGroundedAutomaticTime[1]; automatic.Clear();
        var machine = AlsGroundedStateMachine.Update(_definition.Machine, previous.Machine, rules, automatic,
            context.Weight, context.Delta, context.Identity.FrameId, updateCounter:context.UpdateCounter);
        var reset = machine.Reinitialized;
        var epoch = reset ? NextInitializationEpoch(previous.InitializationEpoch) : previous.InitializationEpoch;
        var stride = AlsCrouchingStride.Advance(reset ? default : previous.Stride, strideInput, context.Delta, _definition.Stride);
        var directionUpdate = reset ? AlsGroundedStateMachine.Initialize(_definition.Direction.Machine) : default;
        var directionState = reset ? directionUpdate.State : previous.Direction;
        var childContext = context.WithState(_definition.MachineNodeIndex, 0);
        _sink = sink;
        try
        {
            if (reset)
                InitializeSources(sink);
            if (stride.WalkPoseRelevant)
                sink.UpdateSource(_definition.WalkPosePlayerId, childContext.WithWeight(context.Weight * stride.WalkPoseUpdateWeight));
            if (stride.DirectionRelevant)
            {
                directionUpdate = _direction.Prepare(directionState, rules, velocity,
                    childContext.WithWeight(context.Weight * stride.DirectionUpdateWeight), this);
                directionState = directionUpdate.State;
            }
            // The self-linked layer drains its saved-pose queue before ApplyAdditive updates Lean.
            sink.UpdateSource(_definition.LeanPlayerId, childContext);
            return new(new(machine.State, directionState, stride, context.Identity, epoch, System.Math.Clamp(diagonalAlpha, 0, 1)),
                reset, stride.DirectionRelevant, directionUpdate);
        }
        finally { _sink = null; }
    }

    private void ValidateOwner(in AlsCrouchingCycleState previous, AlsFrameIdentity identity)
    {
        if (_sink is not null || identity.SlotGeneration == 0 || !float.IsFinite(previous.DiagonalAlpha) ||
            previous.DiagonalAlpha is < 0 or > 1 || previous.InitializationEpoch < 0 || previous.Machine.HasInitialized &&
            (previous.InitializationEpoch == 0 || previous.Machine.Kind != _definition.Machine.Kind ||
                !previous.Direction.HasInitialized || previous.Direction.Kind != _definition.Direction.Machine.Kind ||
                previous.Identity.CharacterId != identity.CharacterId || previous.Identity.SlotGeneration != identity.SlotGeneration ||
                previous.Identity.FrameId > identity.FrameId ||
                previous.Machine.HasUpdated && previous.Identity.FrameId != previous.Machine.LastUpdateSerial))
            throw new ArgumentException("Invalid Crouching Cycles initialization/update owner.");
    }

    private static long NextInitializationEpoch(long previous)
    {
        if (previous == long.MaxValue) throw new ArgumentOutOfRangeException(nameof(previous));
        return previous + 1;
    }

    private void InitializeSources(IAlsCrouchingCycleUpdateSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        sink.InitializeSource(_definition.WalkPosePlayerId);
        // LinkedAnimGraph initializes every outer input, unlike the internal LinkedInputPose links.
        foreach (var player in _definition.Direction.PlayerIds) sink.InitializeSource(player);
        sink.InitializeSource(_definition.LeanPlayerId);
    }

    void IAlsCrouchingDirectionUpdateSink.UpdateSource(int direction, int playerId, in AlsPoseUpdateContext context) =>
        _sink!.UpdateSource(playerId, context);
    void IAlsCrouchingDirectionUpdateSink.OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped) =>
        _sink!.OnCachedUpdatesSkipped(handlerNodeIndex, skipped);
}
