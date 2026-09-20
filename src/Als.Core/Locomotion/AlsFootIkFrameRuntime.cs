using System.Numerics;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

// One property/curve/pose transaction for the final Foot IK linked layer.
public sealed class AlsFootIkFrameRuntime
{
    private enum Phase { Idle, Global, Graph, Evaluated, Finalized }
    private Phase _phase;
    private readonly AlsFootIkInputModel _model;
    private readonly AlsFootIkRuntime _controls;
    private readonly int _leftEnable, _rightEnable, _leftLock, _rightLock;
    private AlsFootIkPropertyState _candidate;
    private AlsFrameIdentity _identity;
    private AlsFootIkControlInput _controlInput;
    private readonly int _curveCount;
    private readonly float[] _committedCurves = new float[4], _nextCurves = new float[4];
    private bool _visitsControls;
    private readonly AlsFootIkPoseSpace _poseSpace;
    private readonly AlsBasedFootLockFrame? _based;
    public AlsBasedFootLockFrameState CommittedBased => _based?.Committed ?? default;
    public AlsBasedFootLockFrameState CandidateBased => _based?.Candidate ?? default;
    public AlsBasedFootLockFrameTrace? CommittedBasedTrace => _based?.CommittedTrace;
    public AlsFrameIdentity CommittedIdentity { get; private set; }
    public AlsFootIkPropertyState CommittedState { get; private set; }
    public AlsFrameIdentity CommittedPoseIdentity => _controls.CommittedIdentity;
    public AlsFootIkPropertyState CandidateState => _phase != Phase.Idle ? _candidate : throw new InvalidOperationException("No foot frame candidate.");
    public ReadOnlySpan<AlsLocalPose> Pose { get { RequirePose(); return _controls.Pose; } }
    public ReadOnlySpan<AlsInertialCurve> Curves { get { RequirePose(); return _controls.Curves; } }
    public int EvaluatedControls => _visitsControls && _phase is Phase.Evaluated or Phase.Finalized ? _controls.EvaluatedControls : 0;
    public AlsFootIkFrameRuntime(AlsFootIkInputModel model, AlsFootIkDefinition definition,
        ReadOnlySpan<string> bones, ReadOnlySpan<int> parents, ReadOnlySpan<string> curves,
        AlsFootIkPoseSpace poseSpace = AlsFootIkPoseSpace.Godot, ReadOnlySpan<AlsLocalPose> basedReference = default, bool captureBasedTrace = false)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model; CommittedState = model.InitialState;
        _controls = new(definition, bones, parents, curves.Length,poseSpace:poseSpace); _curveCount = curves.Length; _poseSpace=poseSpace;
        if (!basedReference.IsEmpty) _based = new(bones, parents, basedReference, poseSpace, captureBasedTrace);
        var names = curves.ToArray();
        _leftEnable = Find("Enable_FootIK_L"); _rightEnable = Find("Enable_FootIK_R");
        _leftLock = Find("FootLock_L"); _rightLock = Find("FootLock_R");
        int Find(string name)
        {
            var index = Array.IndexOf(names, name);
            return index >= 0 ? index : throw new ArgumentException("Missing Foot IK curve layout: " + name);
        }
    }
    public void Prepare(in AlsFrameInput frame, AlsMovementStateInput state)
    {
        PrepareGlobal(frame, state);
        PrepareGraph();
    }
    // UpdateFootIK is an instance-global property function. It runs even when
    // the final root skips the ordinary pose branch, using last final output.
    public void PrepareGlobal(in AlsFrameInput frame, AlsMovementStateInput state)
    {
        if (_phase != Phase.Idle) throw new InvalidOperationException("Foot frame already prepared.");
        var scene = frame.FootIk;
        if (scene.Captured != 1 || scene.PoseIdentity != CommittedIdentity || frame.Identity.SlotGeneration == 0 ||
            CommittedIdentity != default && (frame.Identity.CharacterId != CommittedIdentity.CharacterId ||
                frame.Identity.SlotGeneration != CommittedIdentity.SlotGeneration || frame.Identity.FrameId != CommittedIdentity.FrameId + 1))
            throw new ArgumentException("Foot scene and committed animation history differ.");
        new AlsPrecisePose(scene.ComponentToWorld).Validate();
        new AlsPrecisePose(scene.LeftComponent).Validate(); new AlsPrecisePose(scene.RightComponent).Validate();
        var actor = Quaternion.CreateFromRotationMatrix(frame.CharacterTransform);
        // Remove FBX's fixed component basis before interpreting mesh rotation
        // in UE world axes. Socket transforms below retain the bone basis.
        var componentWorldRotation = _poseSpace == AlsFootIkPoseSpace.Fbx ?
            scene.ComponentToWorld.Rotation * Quaternion.Conjugate(AlsFootIkCoordinates.FbxToGodotRotation) : scene.ComponentToWorld.Rotation;
        var componentRotation = AlsFootIkCoordinates.Rotation(componentWorldRotation);
        var actorRotation = AlsFootIkCoordinates.Rotation(actor);
        var lastRotation = AlsFootIkCoordinates.Rotation(scene.LastMovementRotation);
        var velocity = AlsFootIkCoordinates.ToNative(scene.MovementVelocity);
        var root = AlsFootIkCoordinates.ToNative(scene.RootWorld);
        var component = new AlsPrecisePose(scene.ComponentToWorld);
        var grounded = frame.Floor.IsGrounded == 1;
        var left = Observation(scene.LeftComponent, frame.LeftFootHit, _committedCurves[0], _committedCurves[2]);
        var right = Observation(scene.RightComponent, frame.RightFootHit, _committedCurves[1], _committedCurves[3]);
        _candidate = _model.Evaluate(CommittedState, new(state, frame.DeltaTime, left.Lock, right.Lock, left.Offset, right.Offset)).State;
        if (_based is not null)
        {
            _based.Prepare(frame, state, _committedCurves);
            _candidate = _candidate with { LeftLock = Property(_based.Candidate.Left), RightLock = Property(_based.Candidate.Right) };
        }
        _controlInput = new(frame.Identity, _candidate, _committedCurves[0], _committedCurves[1], component);
        if (_based is not null) _controlInput = _controlInput with { UseBasedFinal = true,
            BasedLeftFinal = _based.Candidate.Left.FinalComponent, BasedRightFinal = _based.Candidate.Right.FinalComponent };
        _identity = frame.Identity; _phase = Phase.Global; _visitsControls = false;

        static AlsFootLockInputState Property(AlsBasedFootLockState foot)
        {
            var q = foot.ComponentLock.Rotation;
            return new(foot.Amount, foot.ComponentLock.Position,
                AlsFootIkCoordinates.Rotation(new((float)-q.Y, (float)-q.Z, (float)q.X, (float)q.W)));
        }

        (AlsFootLockObservation Lock, AlsFootOffsetObservation Offset) Observation(AlsLocalPose pose, AlsFootHit hit, float enable, float locking)
        {
            var world = AlsPrecisePose.Compose(new(pose), component).Position;
            return (new(enable, locking, AlsFootIkCoordinates.ComponentToNative(pose.Position,_poseSpace), AlsFootIkCoordinates.Rotation(pose.Rotation,_poseSpace),
                velocity, scene.WorldDelta, componentRotation, actorRotation, lastRotation, grounded),
                new(enable, new(-world.Z * 100, world.X * 100, world.Y * 100), root,
                    hit.Valid == 1 && hit.Walkable == 1, AlsFootIkCoordinates.ToNative(hit.Position),
                    new(-hit.Normal.Z, hit.Normal.X, hit.Normal.Y)));
        }
    }
    public void PrepareGraph(bool visitControls = true)
    {
        if (_phase != Phase.Global) throw new InvalidOperationException("Foot graph requires one global property update.");
        try
        {
            // Bone indices are fixed at construction and all authored controller
            // alphas use direct float/curve inputs; no hidden interpolation ticks.
            if (visitControls) _controls.Prepare(_controlInput);
            _visitsControls = visitControls; _phase = Phase.Graph;
        }
        catch { Cancel(); throw; }
    }
    public void Evaluate(ReadOnlySpan<AlsLocalPose> source, ReadOnlySpan<AlsInertialCurve> curves)
    {
        if (_phase != Phase.Graph || !_visitsControls) throw new InvalidOperationException("Foot frame requires one visited graph evaluation.");
        try
        {
            _based?.CaptureTargets(source);
            _controls.Evaluate(source, curves);
            _phase = Phase.Evaluated;
        }
        catch { Cancel(); throw; }
    }
    // The enclosing root supplies its completed curves, including any alternate
    // branch/blend. Evaluating Foot IK alone cannot finalize the frame history.
    public void CompleteFinalOutput(AlsFrameIdentity identity, ReadOnlySpan<AlsInertialCurve> curves,
        ReadOnlySpan<AlsLocalPose> finalPose = default, ReadOnlySpan<AlsLocalPose> ordinaryTargets = default,
        ReadOnlySpan<AlsLocalPose> alternateTargets = default, float ordinaryWeight = 1)
    {
        if (identity != _identity || !(_phase == Phase.Evaluated || _phase == Phase.Graph && !_visitsControls))
            throw new InvalidOperationException("Foot feedback requires a completed graph and matching final root identity.");
        try
        {
            if (!_visitsControls && _based is not null) _based.CaptureTargets(finalPose);
            else if (_based is not null && (!ordinaryTargets.IsEmpty || !alternateTargets.IsEmpty || ordinaryWeight != 1))
                _based.CaptureBlendedTargets(ordinaryTargets, alternateTargets, ordinaryWeight);
            if (curves.Length != _curveCount) throw new ArgumentException("Final foot curve layout differs.");
            foreach (var curve in curves) if (!float.IsFinite(curve.Value)) throw new ArgumentException("Nonfinite final foot curve.");
            _nextCurves[0] = Value(curves[_leftEnable]); _nextCurves[1] = Value(curves[_rightEnable]);
            _nextCurves[2] = Value(curves[_leftLock]); _nextCurves[3] = Value(curves[_rightLock]);
            _phase = Phase.Finalized;
        }
        catch { Cancel(); throw; }
        static float Value(AlsInertialCurve curve) => curve.Present ? curve.Value : 0;
    }
    public void ValidateCommit(AlsFrameIdentity identity)
    {
        if (_phase != Phase.Finalized || identity != _identity) throw new InvalidOperationException("Foot frame requires final root feedback before commit.");
        if (_visitsControls) _controls.ValidateCommit(identity);
    }
    public void Commit(AlsFrameIdentity identity)
    {
        ValidateCommit(identity); if (_visitsControls) _controls.Commit(identity);
        CommittedIdentity = identity; CommittedState = _candidate; _nextCurves.CopyTo(_committedCurves, 0);
        _based?.Commit();
        _phase = Phase.Idle; _visitsControls = false;
    }
    public void Cancel() { _controls.Cancel(); _based?.Cancel(); _phase = Phase.Idle; _visitsControls = false; }
    private void RequirePose()
    {
        if (!_visitsControls || _phase is not (Phase.Evaluated or Phase.Finalized))
            throw new InvalidOperationException("Foot pose is unavailable outside a visited evaluation.");
    }
}
