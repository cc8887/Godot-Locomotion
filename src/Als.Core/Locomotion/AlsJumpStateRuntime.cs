using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public sealed record AlsJumpStateDefinition(AlsGroundedMachineDefinition Machine, int MachineNodeIndex,
    int WalkLeft, int RunLeft, int WalkRight, int RunRight, int Loop, int Flail);
public readonly record struct AlsJumpStateFrame(AlsFrameIdentity Identity, AlsGroundedMachineState Machine, AlsJumpBlendInputs Inputs);
public readonly record struct AlsJumpStateUpdate(AlsJumpStateFrame State, AlsGroundedMachineUpdate Machine);

public interface IAlsJumpStateUpdateSink
{
    void ClearSourceWeights(byte states);
    void InitializeSource(int playerId);
    void UpdateSource(int playerId, in AlsPoseUpdateContext context);
    void RequestInertialization(in AlsPoseUpdateContext context, float seconds);
}

public sealed class AlsJumpStateRuntime
{
    private readonly AlsJumpStateDefinition _definition;
    private bool _updating;

    public AlsJumpStateRuntime(AlsJumpStateDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        int[] ids = [definition.WalkLeft, definition.RunLeft, definition.WalkRight, definition.RunRight, definition.Loop, definition.Flail];
        if (definition.Machine.Kind != AlsGroundedMachineKind.Jump || definition.Machine.States.Length != 5 ||
            definition.MachineNodeIndex < 0 || ids.Any(id => id < 0) || ids.Distinct().Count() != 6)
            throw new ArgumentException("Invalid Jump source/state definition.");
        _definition = definition;
    }

    public AlsJumpStateUpdate Initialize(in AlsJumpStateFrame previous, AlsFrameIdentity identity, IAlsJumpStateUpdateSink sink)
    {
        Validate(previous, identity, sink);
        _updating = true;
        try
        {
            var update = AlsGroundedStateMachine.Initialize(_definition.Machine);
            InitializeSources(update, sink);
            // The state-machine root initializes Entry only. Other node input histories survive.
            return new(new(identity, update.State, previous.Inputs.Capture(update, 0, 0)), update);
        }
        finally { _updating = false; }
    }

    public AlsJumpStateUpdate Update(in AlsJumpStateFrame previous, in AlsGroundedRuleInput rules, float speedMetres,
        in AlsPoseUpdateContext context, IAlsJumpStateUpdateSink sink)
    {
        Validate(previous, context.Identity, sink);
        if (!float.IsFinite(speedMetres) || speedMetres < 0) throw new ArgumentException("Invalid Jump speed.");
        Span<AlsGroundedAutomaticTime> times = stackalloc AlsGroundedAutomaticTime[5]; times.Clear();
        var update = AlsGroundedStateMachine.Update(_definition.Machine, previous.Machine, rules, times,
            context.Weight, context.Delta, context.Identity.FrameId, updateCounter:context.UpdateCounter);
        var inputs = previous.Inputs.Capture(update, speedMetres, context.Delta);
        _updating = true;
        try
        {
            InitializeSources(update, sink);
            if (update.InertializationSeconds >= 0) sink.RequestInertialization(context, update.InertializationSeconds);
            for (var i = 0; i < update.UpdateCount; i++)
            {
                var child = update.GetUpdate(i);
                var path = context.WithWeight(child.Weight).WithState(_definition.MachineNodeIndex, child.State, child.InertializationSync);
                switch (child.State)
                {
                    case 0: break;
                    case 1: Pair(_definition.WalkLeft, _definition.RunLeft, inputs.Left.PoseAlpha, path, sink); break;
                    case 2: Pair(_definition.WalkRight, _definition.RunRight, inputs.Right.PoseAlpha, path, sink); break;
                    case 3: sink.UpdateSource(_definition.Loop, path); break;
                    case 4: sink.UpdateSource(_definition.Flail, path); break;
                    default: throw new InvalidOperationException("Invalid Jump content state.");
                }
            }
            return new(new(context.Identity, update.State, inputs), update);
        }
        finally { _updating = false; }
    }

    private void Validate(in AlsJumpStateFrame previous, AlsFrameIdentity identity, IAlsJumpStateUpdateSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (_updating || identity.SlotGeneration == 0 || previous.Machine.HasInitialized &&
            (previous.Machine.Kind != AlsGroundedMachineKind.Jump || previous.Identity.CharacterId != identity.CharacterId ||
                previous.Identity.SlotGeneration != identity.SlotGeneration || previous.Identity.FrameId > identity.FrameId ||
                previous.Machine.HasUpdated && previous.Identity.FrameId != previous.Machine.LastUpdateSerial))
            throw new ArgumentException("Invalid Jump candidate owner.");
    }
    private void InitializeSources(in AlsGroundedMachineUpdate update, IAlsJumpStateUpdateSink sink)
    {
        sink.ClearSourceWeights(update.ClearCachedWeightStates);
        for (var i = 0; i < update.InitializationCount; i++)
            switch (update.GetInitialization(i))
            {
                case 0: break;
                case 1: sink.InitializeSource(_definition.WalkLeft); sink.InitializeSource(_definition.RunLeft); break;
                case 2: sink.InitializeSource(_definition.WalkRight); sink.InitializeSource(_definition.RunRight); break;
                case 3: sink.InitializeSource(_definition.Loop); break;
                case 4: sink.InitializeSource(_definition.Flail); break;
                default: throw new InvalidOperationException("Invalid Jump initialization.");
            }
    }
    private static void Pair(int walk, int run, float alpha, in AlsPoseUpdateContext context, IAlsJumpStateUpdateSink sink)
    {
        if (alpha == 0) sink.UpdateSource(walk, context);
        else if (alpha == 1) sink.UpdateSource(run, context);
        else
        {
            sink.UpdateSource(walk, context.WithWeight(context.Weight * (1 - alpha)));
            sink.UpdateSource(run, context.WithWeight(context.Weight * alpha));
        }
    }
}
