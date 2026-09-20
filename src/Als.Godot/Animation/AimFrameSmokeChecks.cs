using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// Integration fixture only: the eventual outer LayerBlending owner supplies Aim
// relevance independently of the BaseLayer Slot. No pose source is synthesized.
internal sealed class AimFrameSmokeChecks
{
    private readonly AlsAimFrameRuntime _runtime;
    private readonly AlsAimPoseRuntime _poseRuntime;
    private readonly AlsLocalPose[] _pose = new AlsLocalPose[79], _savedPose = new AlsLocalPose[79];
    private readonly AlsInertialCurve[] _curves, _savedCurves;
    private bool _relevant;
    private AlsAimFrameState _expected;
    private AlsAimEvaluatorOperation[] _operations = [];
    public int SourceUpdates { get; private set; }
    public int Initializations { get; private set; }
    public int HiddenFrames { get; private set; }
    public int PoseFrames { get; private set; }

    public AimFrameSmokeChecks(AlsMovementGraphDefinition definition, AlsAnimationSetDefinition set,
        ReadOnlySpan<string> curveNames, uint character, uint generation)
    {
        _runtime = new(definition.AimPose, character, generation);
        _poseRuntime = new(definition.AimPose, new AlsAimAnimationSourceSampler(definition.AimSampling,
            definition.AimRawSources, set, curveNames), curveNames.Length, character, generation);
        _curves = new AlsInertialCurve[curveNames.Length]; _savedCurves = new AlsInertialCurve[curveNames.Length];
    }

    public void Prepare(in AlsAimingInputState input, AlsRotationMode mode, bool hasInput, float delta,
        long serial, bool relevant, float weight)
    {
        _runtime.Cancel();
        _runtime.Prepare(input, mode, hasInput, delta, serial, relevant, weight);
        _relevant = relevant;
        if (relevant) _poseRuntime.Evaluate(_runtime.Candidate, _pose, _curves);
    }
    public void Capture(bool hidden)
    {
        _expected = _runtime.Candidate;
        if (_relevant) { _pose.CopyTo(_savedPose, 0); _curves.CopyTo(_savedCurves, 0); PoseFrames++; }
        _operations = Enumerable.Range(0, _runtime.OperationCount).Select(_runtime.GetOperation).ToArray();
        SourceUpdates += _operations.Count(o => o.Kind == AlsAimEvaluatorOperationKind.Update);
        Initializations += _operations.Count(o => o.Kind == AlsAimEvaluatorOperationKind.Initialize);
        if (hidden)
        {
            HiddenFrames++;
            Require(!_operations.Any(o => o.Kind == AlsAimEvaluatorOperationKind.Update), "Hidden Aim sources advanced.");
            for (var machine = 0; machine < 3; machine++) for (var state = 0; state < (machine == 2 ? 5 : 2); state++)
                Require(_expected.GetRecordedWeight((AlsAimMachineKind)machine, state) == 0, "Hidden Aim proxy weights survived.");
        }
    }
    public void CheckUncommitted(AlsFrameIdentity identity) => Require(_runtime.Committed.Identity == identity, "Late pose failure committed Aim history.");
    public void Compare()
    {
        Same(_expected, _runtime.Candidate);
        if (_relevant) Require(_pose.SequenceEqual(_savedPose) && _curves.SequenceEqual(_savedCurves), "Aim nested pose/curve retry diverged.");
        Require(_operations.Length == _runtime.OperationCount, "Aim retry changed operation count.");
        for (var i = 0; i < _operations.Length; i++) Require(_operations[i] == _runtime.GetOperation(i), "Aim retry changed source operation order.");
    }
    public void ValidateCommit(AlsFrameIdentity identity) => _runtime.ValidateCommit(identity);
    public void Commit(AlsFrameIdentity identity) { _runtime.Commit(identity); Same(_expected, _runtime.Committed); }

    internal static void Same(in AlsAimFrameState expected, in AlsAimFrameState actual)
    {
        Require(expected.Identity == actual.Identity && expected.Serial == actual.Serial && expected.Traversal==actual.Traversal, "Aim frame identity/traversal differs.");
        for (var machine = 0; machine < 3; machine++)
        {
            var kind = (AlsAimMachineKind)machine; var a = expected.GetMachine(kind); var b = actual.GetMachine(kind);
            Require(a.Kind == b.Kind && a.Initialized == b.Initialized && a.Updated == b.Updated && a.LastUpdateSerial == b.LastUpdateSerial && a.LastUpdateCounter==b.LastUpdateCounter &&
                a.CurrentState == b.CurrentState && a.ElapsedSeconds == b.ElapsedSeconds && a.Transitions.Count == b.Transitions.Count &&
                a.Transitions.Latest == b.Transitions.Latest, "Aim machine history differs.");
            for (var state = 0; state < (machine == 2 ? 5 : 2); state++)
                Require(expected.GetRecordedWeight(kind, state) == actual.GetRecordedWeight(kind, state), "Aim recorded weights differ.");
            for (var index = 0; index < a.Transitions.Count; index++) Require(a.GetActiveEdge(index) == b.GetActiveEdge(index) &&
                a.Transitions.GetTransition(index) == b.Transitions.GetTransition(index), "Aim transition stack differs.");
        }
        for (var evaluator = 0; evaluator < 7; evaluator++)
            Require(expected.GetEvaluator(evaluator) == actual.GetEvaluator(evaluator), "Aim independent source history differs.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
