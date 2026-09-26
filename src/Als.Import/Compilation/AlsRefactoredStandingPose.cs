using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Node65 output before outer node118. Collects this frame's evaluated
/// state outputs and applies the original standard transition stack once.</summary>
public sealed class AlsRefactoredStandingPose
{
    public AlsRefactoredStandingResources Resources { get; }
    public AlsRefactoredStandingRestPose Rest { get; }
    public AlsRefactoredMovementDetailsPose Movement { get; }
    public AlsRefactoredStopPose Stop { get; }
    private readonly string[] _bones, _curves;
    private readonly int[][] _maps;
    public ReadOnlySpan<string> BoneNames => _bones;
    public ReadOnlySpan<string> CurveNames => _curves;

    public AlsRefactoredStandingPose(AlsRefactoredStandingResources resources, AlsRefactoredStandingRestPose rest,
        AlsRefactoredMovementDetailsPose movement, AlsRefactoredStopPose stop)
    {
        if (resources.CatalogDigest != rest.Graph.CatalogDigest || resources.CatalogDigest != movement.Graph.Resources.CatalogDigest ||
            resources.CatalogDigest != stop.Graph.Resources.CatalogDigest || !rest.BoneNames.SequenceEqual(movement.BoneNames) ||
            !rest.BoneNames.SequenceEqual(stop.BoneNames) || !rest.Parents.SequenceEqual(movement.Movement.Lean.Parents) ||
            !rest.Parents.SequenceEqual(stop.Graph.Evaluators.Parents)) throw new ArgumentException("Foreign Standing pose resources/layout.");
        Resources = resources; Rest = rest; Movement = movement; Stop = stop; _bones = rest.BoneNames.ToArray();
        string[][] layouts = [rest.CurveNames.ToArray(),movement.CurveNames.ToArray(),stop.CurveNames.ToArray(),rest.CurveNames.ToArray(),rest.CurveNames.ToArray()];
        _curves = layouts.SelectMany(c => c).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _maps = layouts.Select(c => c.Select(n => Array.FindIndex(_curves,s => s.Equals(n,StringComparison.OrdinalIgnoreCase))).ToArray()).ToArray();
    }
    public Runtime CreateRuntime(IAlsRefactoredSourcePlayers rotatePlayers, int firstRotatePlayer) => new(this,rotatePlayers,firstRotatePlayer);

    public sealed class Runtime
    {
        public AlsRefactoredStandingPose Profile { get; }
        private readonly AlsRefactoredStandingRestPose.RotateSampler _rotate;
        private readonly AlsRefactoredStopPose.Sampler _stop;
        private readonly AlsPrecisePose[][] _states;
        private readonly AlsInertialCurve[][] _stateCurves;
        private readonly bool[] _needed = new bool[5], _ready = new bool[5];
        private readonly AlsPrecisePose[] _pose;
        private readonly AlsInertialCurve[] _curves;
        private AlsRefactoredStandingRuntime? _machineOwner;
        private AlsRefactoredMovementInertialization? _movementOwner;
        private AlsGroundedMachineState _state;
        private AlsFrameIdentity _identity, _committedIdentity;
        private bool _prepared, _evaluated, _hasCommitted;
        public int StateEvaluations { get; private set; }
        public ReadOnlySpan<AlsPrecisePose> Pose => _prepared && _evaluated ? _pose : throw new InvalidOperationException("Standing pose unavailable.");
        public ReadOnlySpan<AlsInertialCurve> Curves => _prepared && _evaluated ? _curves : throw new InvalidOperationException("Standing curves unavailable.");
        internal Runtime(AlsRefactoredStandingPose profile, IAlsRefactoredSourcePlayers rotatePlayers, int first)
        {
            Profile = profile; _rotate = profile.Rest.BindRotate(rotatePlayers,first); _stop = profile.Stop.CreateSampler();
            _pose = new AlsPrecisePose[profile._bones.Length]; _curves = new AlsInertialCurve[profile._curves.Length];
            _states = Enumerable.Range(0,5).Select(_ => new AlsPrecisePose[_pose.Length]).ToArray();
            _stateCurves = profile._maps.Select(m => new AlsInertialCurve[m.Length]).ToArray();
        }
        public void Begin(in AlsFrameIdentity identity, AlsRefactoredStandingRuntime machine)
        {
            if (_prepared || identity.FrameId < 0 || !ReferenceEquals(machine.Resources,Profile.Resources) ||
                _machineOwner is not null && !ReferenceEquals(_machineOwner,machine) ||
                _hasCommitted && (identity.FrameId <= _committedIdentity.FrameId || identity.CharacterId != _committedIdentity.CharacterId || identity.SlotGeneration != _committedIdentity.SlotGeneration))
                throw new ArgumentException("Foreign Standing pose frame/owner.");
            machine.ValidateCommit(identity.FrameId);
            var state = machine.Candidate.State; var stack = state.Transitions;
            Array.Clear(_needed); Array.Clear(_ready);
            _needed[stack.Count == 0 ? state.CurrentState : stack.GetTransition(0).From] = true;
            for (var i = 0; i < stack.Count; i++)
            {
                if (Profile.Resources.Edges[state.GetActiveEdge(i)].Inertialization)
                    throw new InvalidOperationException("Live inertial transition must not be blended a second time.");
                _needed[stack.GetTransition(i).To] = true;
            }
            _identity = identity; _state = state; _machineOwner ??= machine;
            _prepared = true; _evaluated = false; StateEvaluations = 0;
        }
        public bool NeedsState(int state)
        {
            if (!_prepared || (uint)state >= 5) throw new ArgumentException("Invalid Standing state query.");
            return _needed[state];
        }
        public void CaptureIdleSlot(ReadOnlySpan<AlsPrecisePose> slotPose, ReadOnlySpan<AlsInertialCurve> slotCurves, float turnRate)
        {
            Capture(0); Profile.Rest.FinishIdleSlot(slotPose,slotCurves,turnRate,_states[0],_stateCurves[0]); Ready(0);
        }
        public void CaptureMovement(AlsRefactoredMovementInertialization movement)
        {
            Capture(1); ValidateMovement(movement);
            movement.Pose.CopyTo(_states[1]); movement.Curves.CopyTo(_stateCurves[1]); _movementOwner ??= movement; Ready(1);
        }
        public void CaptureStop(AlsRefactoredStopRuntime machine, AlsRefactoredStopSourceRuntime source, AlsRefactoredMovementInertialization movement)
        {
            Capture(2); ValidateMovement(movement);
            if (source.Identity != _identity) throw new ArgumentException("Foreign Standing Stop identity.");
            _stop.Sample(_identity.FrameId,machine,source,movement,_states[2],_stateCurves[2]); _movementOwner ??= movement; Ready(2);
        }
        public void CaptureRotate(bool left, float rotateRate)
        {
            var state = left ? 3 : 4; Capture(state);
            _rotate.Sample(_identity.FrameId,left,rotateRate,_states[state],_stateCurves[state]); Ready(state);
        }
        private void ValidateMovement(AlsRefactoredMovementInertialization movement)
        {
            movement.ValidateContext(_identity);
            if (_movementOwner is not null && !ReferenceEquals(_movementOwner,movement) || !movement.BoneNames.SequenceEqual(Profile._bones) ||
                !movement.CurveNames.SequenceEqual(Profile.Movement.CurveNames)) throw new ArgumentException("Foreign Standing Movement owner/layout.");
        }
        private void Capture(int state)
        {
            if (!_prepared || _evaluated || !_needed[state] || _ready[state]) throw new ArgumentException("Inactive or duplicate Standing pose source.");
            ValidateMachine();
        }
        private void Ready(int state) { _ready[state] = true; StateEvaluations++; }
        private void ValidateMachine()
        {
            _machineOwner!.ValidateCommit(_identity.FrameId);
            if (!_machineOwner.Candidate.State.Matches(_state)) throw new ArgumentException("Standing candidate changed after pose collection began.");
        }
        public void Evaluate(long frame)
        {
            if (!_prepared || frame != _identity.FrameId) throw new ArgumentException("Foreign Standing pose evaluation.");
            _evaluated = false; ValidateMachine();
            for (var s = 0; s < 5; s++) if (_needed[s] && !_ready[s]) throw new InvalidOperationException("Standing state pose has not been evaluated: " + s);
            var stack = _state.Transitions; var first = stack.Count == 0 ? _state.CurrentState : stack.GetTransition(0).From;
            _states[first].CopyTo(_pose,0); Array.Clear(_curves);
            for (var c = 0; c < _stateCurves[first].Length; c++) _curves[Profile._maps[first][c]] = _stateCurves[first][c];
            for (var i = 0; i < stack.Count; i++)
            {
                var edge = stack.GetTransition(i);
                for (var b = 0; b < _pose.Length; b++) _pose[b] = AlsPrecisePoseBlender.BlendRaw(_pose[b],_states[edge.To][b],edge.Alpha);
                for (var c = 0; c < _curves.Length; c++) _curves[c] = AlsStandingCycleCurves.Scale(_curves[c],1-edge.Alpha);
                for (var c = 0; c < _stateCurves[edge.To].Length; c++)
                {
                    var index = Profile._maps[edge.To][c];
                    _curves[index] = AlsStandingCycleCurves.Accumulate(_curves[index],_stateCurves[edge.To][c],edge.Alpha);
                }
            }
            if (stack.Count > 0) for (var b = 0; b < _pose.Length; b++) _pose[b] = _pose[b].Normalized();
            _evaluated = true;
        }
        public void ValidateCommit(long frame)
        {
            if (!_prepared || !_evaluated || frame != _identity.FrameId) throw new ArgumentException("Standing pose is not ready to commit.");
            ValidateMachine();
        }
        internal void ValidateContext(in AlsFrameIdentity identity)
        { ValidateCommit(identity.FrameId);if(_identity!=identity)throw new ArgumentException("Foreign Standing pose identity."); }
        public void Commit(long frame) { ValidateCommit(frame); _committedIdentity = _identity; _hasCommitted = true; Cancel(); }
        public void Cancel() { _prepared = _evaluated = false; }
    }
}
