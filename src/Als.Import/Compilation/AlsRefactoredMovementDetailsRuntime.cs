using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Snapshot of the node before this graph update, not a new tick. Order
/// must match Resources.TimingPlayers. Hidden nodes retain cached observations
/// until the enclosing graph initializes or explicitly clears their weights.</summary>
public readonly record struct AlsRefactoredMovementDetailsObservation(int PropertyIndex, float CachedWeight,
    float Time, bool PreviousValid = false, float PreviousTime = 0, float Delta = 0);

/// <summary>Transactional original Movement Details update. Owns only state
/// history; players, Parent callbacks, pose evaluation and request consumption
/// belong to the shared graph frame. No private animation clock is advanced.</summary>
public sealed class AlsRefactoredMovementDetailsRuntime
{
    private readonly AlsGroundedMachineDefinition _definition;
    private readonly int[] _selectedPlayers = new int[6];
    private AlsGroundedMachineState _committed;
    private AlsGroundedMachineUpdate _candidate;
    private bool _prepared;
    private long _frame, _committedFrame = -1;
    public AlsRefactoredMovementDetailsResources Resources { get; }
    public AlsGroundedMachineState CommittedState => _committed;
    public AlsGroundedMachineUpdate Candidate => _prepared ? _candidate : throw new InvalidOperationException("No movement details candidate.");
    public AlsOverlayInertialRequest? InertializationRequest => !_prepared ? throw new InvalidOperationException("No movement details candidate.") :
        _candidate.InertializationSeconds < 0 ? null : new(Resources.MachinePropertyIndex, _candidate.InertializationSeconds, true, AlsTransitionBlend.HermiteCubic);
    public ReadOnlySpan<int> SelectedPlayerProperties => _prepared ? _selectedPlayers : throw new InvalidOperationException("No movement details candidate.");

    public AlsRefactoredMovementDetailsRuntime(AlsRefactoredMovementDetailsResources resources)
    {
        Resources = resources;
        var edges = resources.Edges.ToArray().Select(e => new AlsGroundedEdge(e.From, e.To,
            e.Rule == AlsRefactoredMovementDetailsRule.AutomaticRemainingTime ? AlsGroundedCondition.Automatic : AlsGroundedCondition.Never,
            e.AutomaticTriggerTime, e.Seconds, AlsTransitionBlend.HermiteCubic, e.Inertialization, -1, -1, -1)
            { RefactoredMovementDetailsRule = e.Rule }).ToArray();
        var states = resources.States.ToArray().Select(s => new AlsGroundedStateDefinition(false, AlsGroundedCondition.Never,
            s.Exits[0], s.Exits.Length, -1, -1, -1)).ToArray();
        _definition = new(AlsGroundedMachineKind.RefactoredMovementDetails, 0, resources.MaxTransitionsPerFrame,
            resources.SkipFirstUpdateTransition, states, edges);
    }

    public void Prepare(long frame, in AlsRefactoredMovementDetailsInput input,
        ReadOnlySpan<AlsRefactoredMovementDetailsObservation> observations, float delta, float weight = 1,
        bool reinitialize = false, AlsGraphTraversalCounter? updateCounter = null)
    {
        if (_prepared || frame < 0 || frame <= _committedFrame || observations.Length != Resources.TimingPlayers.Length)
            throw new ArgumentException("Invalid movement details frame or observation inventory.");
        Span<AlsGroundedAutomaticTime> times = stackalloc AlsGroundedAutomaticTime[6]; times.Clear();
        Span<int> selected = stackalloc int[6]; selected.Fill(-1);
        var cursor = 0;
        for (var state = 0; state < Resources.States.Length; state++)
        {
            var maxWeight = 0f;
            foreach (var property in Resources.States[state].PlayerPropertyIndices)
            {
                var observation = observations[cursor]; var player = Resources.TimingPlayers[cursor++];
                if (observation.PropertyIndex != property || player.PropertyIndex != property ||
                    !float.IsFinite(observation.CachedWeight) || observation.CachedWeight is < 0 or > 1 ||
                    !float.IsFinite(observation.Time) || observation.Time < 0 || observation.Time > player.Length ||
                    !float.IsFinite(observation.PreviousTime) || !float.IsFinite(observation.Delta) ||
                    observation.PreviousValid && (observation.PreviousTime < 0 || observation.PreviousTime > player.Length))
                    throw new ArgumentException("Invalid movement details player observation.");
                // Native GetRelevantAssetPlayerInterfaceFromState uses strict >,
                // including sub-threshold positive weights. First tie wins.
                if (observation.CachedWeight <= maxWeight) continue;
                maxWeight = observation.CachedWeight; selected[state] = property;
                times[state] = new(true, player.Length, observation.Time, player.Loop,
                    observation.PreviousValid, observation.PreviousTime, observation.Delta);
            }
        }
        var next = AlsGroundedStateMachine.UpdateRefactoredMovementDetails(_definition, reinitialize ? default : _committed,
            input, times, weight, delta, frame, updateCounter);
        // Expose the observations actually visible to the state-machine query:
        // initialized/reentered states have their cached player weights cleared.
        for (var s = 0; s < selected.Length; s++)
            if ((next.ClearCachedWeightStates & (1 << s)) != 0) selected[s] = -1;
        selected.CopyTo(_selectedPlayers); _candidate = next; _frame = frame; _prepared = true;
    }
    public void ValidateCommit(long frame) { if (!_prepared || frame != _frame) throw new ArgumentException("Invalid movement details commit."); }
    public void Commit(long frame) { ValidateCommit(frame); _committed = _candidate.State; _committedFrame = frame; Cancel(); }
    public void Cancel() { _prepared = false; }
}
