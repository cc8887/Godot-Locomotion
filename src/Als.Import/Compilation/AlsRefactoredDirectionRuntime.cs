using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>One stance's six-direction machine. Owns update history only: pose
/// recursion, Parent callbacks and notify consumption remain frame consumers.</summary>
public sealed class AlsRefactoredDirectionRuntime
{
    private readonly AlsGroundedMachineDefinition _definition;
    private AlsGroundedMachineState _committed;
    private AlsGroundedMachineUpdate _candidate;
    private bool _prepared;
    private long _frame, _committedFrame = -1;
    public AlsRefactoredDirectionResources Resources { get; }
    public AlsGroundedMachineState CommittedState => _committed;
    public AlsGroundedMachineUpdate Candidate => _prepared ? _candidate : throw new InvalidOperationException("No direction candidate.");

    public AlsRefactoredDirectionRuntime(AlsRefactoredDirectionResources resources)
    {
        Resources = resources;
        var edges = resources.Edges.ToArray().Select(e => new AlsGroundedEdge(e.From, e.To, AlsGroundedCondition.Never, 0,
            e.Seconds, e.Blend, false, e.StartNotify, -1, -1, AlsGroundedBlendProfile.ChangeDirection)
            { RefactoredDirectionRule = e.Rule }).ToArray();
        var states = resources.States.ToArray().Select(s => new AlsGroundedStateDefinition(false, AlsGroundedCondition.Never,
            s.Exits[0], s.Exits.Length, -1, -1, -1)).ToArray();
        _definition = new(resources.Crouching ? AlsGroundedMachineKind.RefactoredCrouchingDirection : AlsGroundedMachineKind.RefactoredStandingDirection,
            0, 3, true, states, edges);
    }
    public void Prepare(long frame, in AlsRefactoredDirectionInput input, float delta, float weight = 1,
        bool reinitialize = false, AlsGraphTraversalCounter? updateCounter = null)
    {
        if (_prepared || frame < 0 || frame <= _committedFrame) throw new ArgumentException("Invalid direction frame.");
        var next = AlsGroundedStateMachine.UpdateRefactoredDirection(_definition, reinitialize ? default : _committed, input, weight, delta, frame, updateCounter);
        _candidate = next; _frame = frame; _prepared = true;
    }
    public float CandidateBoneWeight(int state, int bone)
    {
        if (!_prepared || (uint)state >= 6 || (uint)bone >= Resources.BlendProfile.BoneNames.Length) throw new ArgumentException("Invalid direction contribution query.");
        var stack = _candidate.State.Transitions;
        if (stack.Count == 0) return stack.CurrentState == state ? 1 : 0;
        var value = 0f;
        for (var i = 0; i < stack.Count; i++)
        {
            var transition = stack.GetTransition(i); var weights = Resources.BlendProfile.Weights(bone, transition.Alpha);
            if (i > 0) value *= weights.Y; else if (transition.From == state) value += weights.Y;
            if (transition.To == state) value += weights.X;
        }
        return Math.Clamp(value, 0, 1);
    }
    public void ValidateCommit(long frame) { if (!_prepared || frame != _frame) throw new ArgumentException("Invalid direction commit."); }
    public void Commit(long frame) { ValidateCommit(frame); _committed = _candidate.State; _committedFrame = frame; Cancel(); }
    public void Cancel() { _prepared = false; }
}
