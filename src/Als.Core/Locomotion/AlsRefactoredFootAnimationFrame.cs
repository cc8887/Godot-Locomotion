using System.Numerics;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

// The animation owner around the Refactored rig: previous final sockets/curves
// -> global lock update -> current spine pose -> queries -> rig -> final feedback.
// All buffers are private. No engine object or blocking query callback enters it.
public sealed class AlsRefactoredFootAnimationFrame
{
    private enum Phase { Idle, Global, Queries, Evaluated, Finalized }
    private Phase _phase;
    private readonly AlsRefactoredFootRigFrame _rig;
    private readonly AlsBasedFootLockFrame _locks;
    private readonly int[] _parents, _indices;
    private readonly AlsPrecisePose[] _components, _reference;
    private readonly AlsLocalPose[] _pose;
    private readonly AlsInertialCurve[] _previousCurves;
    private readonly float[] _lockCurves = new float[4];
    private AlsFrameInput _frame;
    private AlsRefactoredMotionObservation? _locomotion;
    public AlsRefactoredMotionObservation? CommittedLocomotion { get; private set; }
    private AlsRefactoredPoseCurveHistory _cachedPose;
    private float _prediction;
    private bool _valid, _visited;
    private bool _committedValid, _finalPelvisIdentity, _nextFinalPelvisIdentity;
    private readonly bool _pinContacts;
    private readonly bool _correctUnplantedPenetration;
    private AlsFootContactAnchor _leftContact, _rightContact, _nextLeftContact, _nextRightContact;
    private bool _leftContactEnabled, _rightContactEnabled;
    private readonly int _leftToe = -1, _rightToe = -1;
    private AlsPrecisePose? _leftContactToe, _rightContactToe, _nextLeftContactToe, _nextRightContactToe;
    public AlsFrameIdentity CommittedIdentity { get; private set; }
    public AlsFrameIdentity CommittedPoseIdentity { get; private set; }
    public AlsRefactoredFootRigState CommittedRig => _rig.Committed;
    public ReadOnlySpan<AlsPrecisePose> CommittedRigPose => _rig.CommittedPose;
    public AlsRefactoredFootRigInput CandidateRigInput => _rig.CandidateInput;
    public AlsRefactoredFootRigPose CommittedRigFeet => _rig.CommittedFeet;
    public AlsRefactoredFootRigState CandidateRig => _rig.Candidate;
    public AlsBasedFootLockFrameState CommittedLocks => _locks.Committed;
    public AlsBasedFootLockFrameTrace? CommittedLockTrace => _locks.CommittedTrace;
    public AlsBasedFootLockFrameState CandidateLocks => _locks.Candidate;
    public AlsFootTransitionFeedback TransitionFeedback
    {
        get
        {
            Require(Phase.Global);
            var component = ToNativeWorld(_frame.FootIk.ComponentToWorld);
            var locks = _locks.Candidate;
            return new(_frame.Identity, CommittedIdentity, MathF.Abs(_frame.FootIk.ComponentToWorld.Scale.Y),
                locks.Left.Amount, locks.Right.Amount,
                AlsPrecisePose.Compose(_locks.Committed.LeftTarget, component).Position, locks.Left.WorldLock.Position,
                AlsPrecisePose.Compose(_locks.Committed.RightTarget, component).Position, locks.Right.WorldLock.Position);
        }
    }
    public ReadOnlySpan<AlsLocalPose> Pose { get { Require(Phase.Evaluated); if (!_visited) throw new InvalidOperationException("Unvisited foot pose."); return _pose; } }
    public ReadOnlySpan<AlsInertialCurve> Curves { get { Require(Phase.Evaluated); return _rig.Curves; } }

    public AlsRefactoredFootAnimationFrame(AlsRefactoredFootRigFrame rig, ReadOnlySpan<string> bones,
        ReadOnlySpan<int> parents, ReadOnlySpan<AlsPrecisePose> localFbxReference, ReadOnlySpan<string> curves, bool captureLockTrace = false,
        AlsBasedFootLockSettings? lockSettings = null)
    {
        ArgumentNullException.ThrowIfNull(rig);
        if ((lockSettings?.CorrectUnplantedPenetration == true || lockSettings?.PinContactToes == true) && !rig.HasSupportGeometry)
            throw new ArgumentException("Geometry contact correction requires actual support geometry.");
        if (rig.CommittedIdentity != default || localFbxReference.Length != bones.Length || parents.Length != bones.Length)
            throw new ArgumentException("Foot animation requires a fresh rig and matching skeleton.");
        _rig = rig; _parents = parents.ToArray();
        _components = new AlsPrecisePose[bones.Length]; _reference = new AlsPrecisePose[bones.Length];
        _pose = new AlsLocalPose[bones.Length]; _previousCurves = new AlsInertialCurve[curves.Length];
        ToNativeComponents(localFbxReference, parents, _reference);
        for (var i = 0; i < _pose.Length; i++) _pose[i] = localFbxReference[i].ToSingle();
        _locks = new(bones, parents, _pose, AlsFootIkPoseSpace.Fbx, captureLockTrace, lockSettings);
        _pinContacts = lockSettings?.PinFinalContact ?? false;
        _correctUnplantedPenetration = lockSettings?.CorrectUnplantedPenetration ?? false;
        if (lockSettings?.PinContactToes == true)
        {
            var boneNames = bones.ToArray();
            int Toe(string toeName, string footName)
            {
                var toe = Array.FindIndex(boneNames, n => n.Equals(toeName, StringComparison.OrdinalIgnoreCase));
                if (toe < 0 || _parents[toe] < 0 || !boneNames[_parents[toe]].Equals(footName, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Final toe contact requires a direct foot/toe binding: " + toeName);
                return toe;
            }
            _leftToe = Toe("ball_l", "foot_l"); _rightToe = Toe("ball_r", "foot_r");
        }
        var names = curves.ToArray();
        _indices = new[] { "FootLeftIk", "FootRightIk", "FootLeftLock", "FootRightLock" }.Select(Find).ToArray();
        int Find(string name)
        {
            var index = Array.IndexOf(names, name);
            return index >= 0 && Array.LastIndexOf(names, name) == index ? index :
                throw new ArgumentException("Missing/duplicate Refactored foot feedback: " + name);
        }
    }

    public void PrepareGlobal(in AlsFrameInput frame, AlsMovementStateInput movement,
        in AlsRefactoredPoseCurveHistory cachedPose, float prediction, AlsRefactoredMotionObservation? locomotion = null)
    {
        Require(Phase.Idle);
        if (frame.FootIk.Captured != 1 || frame.FootIk.PoseIdentity != CommittedIdentity ||
            cachedPose.Identity != CommittedIdentity || frame.Identity.SlotGeneration == 0 ||
            CommittedIdentity != default && (frame.Identity.CharacterId != CommittedIdentity.CharacterId ||
                frame.Identity.SlotGeneration != CommittedIdentity.SlotGeneration || frame.Identity.FrameId != CommittedIdentity.FrameId + 1))
            throw new ArgumentException("Refactored foot scene/final history identity differs.");
        _ = cachedPose.PelvisAmount(prediction);
        _ = ToNativeWorld(frame.FootIk.ComponentToWorld);
        for (var i = 0; i < 4; i++) _lockCurves[i] = _previousCurves[_indices[i]].Present ? _previousCurves[_indices[i]].Value : 0;
        // A newly constructed animation instance is pending for its first update.
        // RefreshFeetOnGameThread waits for a nonidentity pelvis before the
        // first valid sample; after becoming valid it does not repeat that test.
        _valid = CommittedIdentity != default && (_committedValid || !_finalPelvisIdentity);
        _locks.Prepare(frame, movement, _lockCurves, _valid, locomotion);
        _locomotion = locomotion;
        _frame = frame; _cachedPose = cachedPose; _prediction = prediction; _visited = false; _phase = Phase.Global;
    }

    public AlsFootRigQueries PrepareQueries(ReadOnlySpan<AlsLocalPose> preFootPose,
        ReadOnlySpan<AlsInertialCurve> curves, bool executeRig)
    {
        Require(Phase.Global);
        try
        {
            if (executeRig)
            {
                if (preFootPose.Length != _components.Length) throw new ArgumentException("Pre-foot pose is incomplete.");
                for (var bone = 0; bone < _components.Length; bone++)
                {
                    var local = ToNative(new(preFootPose[bone]));
                    _components[bone] = _parents[bone] < 0 ? local : AlsPrecisePose.Compose(local, _components[_parents[bone]]).Normalized();
                }
            }
            else _reference.CopyTo(_components, 0);
            var locks = _locks.Candidate;
            var input = new AlsRefactoredFootRigInput(_frame.Identity, CommittedIdentity, _frame.DeltaTime,
                executeRig, _valid, CommittedIdentity == default, _cachedPose.PelvisAmount(_prediction),
                new(locks.Left.FinalComponent.Position, locks.Left.FinalComponent.Rotation),
                new(locks.Right.FinalComponent.Position, locks.Right.FinalComponent.Rotation), ToNativeWorld(_frame.FootIk.ComponentToWorld));
            var queries = _rig.Prepare(input, _components, curves);
            _leftContactEnabled = executeRig && _valid && curves[_indices[0]].Present && curves[_indices[0]].Value >= 1 - AlsPoseBlender.WeightThreshold;
            _rightContactEnabled = executeRig && _valid && curves[_indices[1]].Present && curves[_indices[1]].Value >= 1 - AlsPoseBlender.WeightThreshold;
            _visited = executeRig; _phase = Phase.Queries; return queries;
        }
        catch { Cancel(); throw; }
    }

    public void Evaluate(in AlsFootRigObservations observations)
    {
        Require(Phase.Queries);
        try
        {
            _nextLeftContact = _nextRightContact = default;
            _nextLeftContactToe = _nextRightContactToe = null;
            if (_pinContacts)
            {
                var locks = _locks.Candidate;
                var basis = ContactBase(); var component = _rig.CandidateInput.ToWorld;
                var leftGround = MatchingContactGround(_leftContactEnabled, locks.Left.BaseIdentity, observations.Left);
                var rightGround = MatchingContactGround(_rightContactEnabled, locks.Right.BaseIdentity, observations.Right);
                var left = AlsFootContactAnchoring.Target(_leftContact, locks.Left, locks.TeleportSequence, leftGround, basis, component,
                    observations.Left.ColliderIdentity);
                var right = AlsFootContactAnchoring.Target(_rightContact, locks.Right, locks.TeleportSequence, rightGround, basis, component,
                    observations.Right.ColliderIdentity);
                _rig.Evaluate(observations, left, right,
                    leftGround && left.Weight == 0 && locks.Left.Amount >= 1 - AlsPoseBlender.WeightThreshold,
                    rightGround && right.Weight == 0 && locks.Right.Amount >= 1 - AlsPoseBlender.WeightThreshold,
                    CanCorrectUnplanted(_leftContactEnabled, locks.Left.Amount, observations.Left),
                    CanCorrectUnplanted(_rightContactEnabled, locks.Right.Amount, observations.Right),
                    ToeTarget(left, _leftContactToe, _leftToe), ToeTarget(right, _rightContactToe, _rightToe));
                var rig = _rig.Candidate;
                _nextLeftContact = AlsFootContactAnchoring.Complete(_leftContact, locks.Left, locks.TeleportSequence,
                    leftGround && rig.LeftOffset.Walkable && (!rig.LeftCalibrated || System.Math.Abs(rig.LeftSupportDistance) <= .05), basis, component,
                    rig.Left.Location.FootLocation, left.Weight > 0, observations.Left.ColliderIdentity);
                _nextRightContact = AlsFootContactAnchoring.Complete(_rightContact, locks.Right, locks.TeleportSequence,
                    rightGround && rig.RightOffset.Walkable && (!rig.RightCalibrated || System.Math.Abs(rig.RightSupportDistance) <= .05), basis, component,
                    rig.Right.Location.FootLocation, right.Weight > 0, observations.Right.ColliderIdentity);
                _nextLeftContactToe = CaptureContactToe(_nextLeftContact, _leftContactToe, _leftToe, left.Weight > 0);
                _nextRightContactToe = CaptureContactToe(_nextRightContact, _rightContactToe, _rightToe, right.Weight > 0);
            }
            else _rig.Evaluate(observations);
            if (_visited)
                for (var bone = 0; bone < _pose.Length; bone++)
                {
                    var local = _parents[bone] < 0 ? _rig.Pose[bone] :
                        AlsPrecisePose.Relative(_rig.Pose[bone], _rig.Pose[_parents[bone]]).Normalized();
                    _pose[bone] = FromNative(local).ToSingle();
                }
            _phase = Phase.Evaluated;
        }
        catch { Cancel(); throw; }
    }

    public void CompleteFinalOutput(AlsFrameIdentity identity, ReadOnlySpan<AlsLocalPose> pose, ReadOnlySpan<AlsInertialCurve> curves)
    {
        Require(Phase.Evaluated);
        try
        {
            if (identity != _frame.Identity || curves.Length != _previousCurves.Length || pose.Length != _pose.Length)
                throw new ArgumentException("Incomplete or foreign final foot feedback.");
            foreach (var curve in curves) if (!float.IsFinite(curve.Value)) throw new ArgumentException("Nonfinite final foot curve.");
            // Refactored leg controls do not overwrite IK target bones. Capture
            // sockets and pelvis from the actual final root, after hands and any
            // alternate-branch blend, as RefreshFeetOnGameThread does next frame.
            _locks.CaptureTargets(pose);
            var pelvis = _locks.CapturedPelvisComponent; var q = pelvis.Rotation; var p = pelvis.Position;
            const double tolerance = (double)1e-4f; // TTransform::EqualsNoScale default, UE_KINDA_SMALL_NUMBER
            _nextFinalPelvisIdentity = System.Math.Abs(p.X) <= tolerance && System.Math.Abs(p.Y) <= tolerance && System.Math.Abs(p.Z) <= tolerance &&
                System.Math.Abs(q.X) <= tolerance && System.Math.Abs(q.Y) <= tolerance && System.Math.Abs(q.Z) <= tolerance && System.Math.Abs(System.Math.Abs(q.W) - 1) <= tolerance;
            for (var i = 0; i < 4; i++) _lockCurves[i] = curves[_indices[i]].Present ? curves[_indices[i]].Value : 0;
            _phase = Phase.Finalized;
        }
        catch { Cancel(); throw; }
    }
    public void ValidateCommit(AlsFrameIdentity identity)
    {
        Require(Phase.Finalized);
        if (identity != _frame.Identity) throw new ArgumentException("Foreign foot animation commit.");
        _rig.ValidateCommit(identity);
    }
    public void Commit(AlsFrameIdentity identity)
    {
        ValidateCommit(identity); _rig.Commit(identity); _locks.Commit(); CommittedLocomotion = _locomotion;
        _leftContact = _nextLeftContact; _rightContact = _nextRightContact;
        _leftContactToe = _nextLeftContactToe; _rightContactToe = _nextRightContactToe;
        for (var i = 0; i < 4; i++) _previousCurves[_indices[i]] = new(_lockCurves[i]);
        _committedValid = _valid; _finalPelvisIdentity = _nextFinalPelvisIdentity;
        CommittedIdentity = identity; if (_visited) CommittedPoseIdentity = identity; _phase = Phase.Idle;
    }
    public void Cancel() { _rig.Cancel(); _locks.Cancel(); _phase = Phase.Idle; }

    private static AlsFootContactToeTarget ToeTarget(AlsFootContactTarget target, AlsPrecisePose? toe, int bone) =>
        bone >= 0 && toe.HasValue && target.Weight > AlsPoseBlender.WeightThreshold ? new(target.Weight, bone, toe.Value) : default;
    private AlsPrecisePose? CaptureContactToe(AlsFootContactAnchor anchor, AlsPrecisePose? old, int bone, bool reused) =>
        !anchor.Active || bone < 0 ? null : reused ? old :
            AlsPrecisePose.Relative(_rig.Pose[bone], _rig.Pose[_parents[bone]]).Normalized();

    private bool MatchingContactGround(bool enabled, ulong baseIdentity, AlsFootTraceRigHit hit) =>
        enabled && _frame.Floor.IsGrounded == 1 && hit.Blocking && hit.ColliderIdentity != 0 &&
        (baseIdentity != 0 ? hit.ColliderIdentity == baseIdentity :
            _frame.Floor.ColliderId > 0 && hit.ColliderIdentity == (ulong)_frame.Floor.ColliderId);

    private bool CanCorrectUnplanted(bool enabled, float lockAmount, AlsFootTraceRigHit hit) =>
        // A releasing contact keeps its original weighted horizontal target.
        // Only its final penetration is corrected; full locks retain the anchor.
        _correctUnplantedPenetration && enabled && lockAmount < 1 - AlsPoseBlender.WeightThreshold &&
        _frame.Floor.IsGrounded == 1 && _frame.Floor.ColliderId > 0 && hit.Blocking &&
        hit.ColliderIdentity == (ulong)_frame.Floor.ColliderId;

    private AlsPrecisePose ContactBase()
    {
        if (_locks.Candidate.Left.BaseIdentity == 0 && _locks.Candidate.Right.BaseIdentity == 0)
            return AlsPrecisePose.Identity;
        if (!System.Numerics.Matrix4x4.Decompose(_frame.Floor.PlatformTransform, out _, out var q, out var p))
            throw new ArgumentException("Invalid contact platform transform.");
        return new(new(-p.Z * 100d, p.X * 100d, p.Y * 100d), new(q.Z, -q.X, -q.Y, q.W), AlsDoubleVector.One);
    }

    public static void ToNativeComponents(ReadOnlySpan<AlsPrecisePose> localFbx, ReadOnlySpan<int> parents, Span<AlsPrecisePose> output)
    {
        if (localFbx.Length != parents.Length || output.Length != parents.Length) throw new ArgumentException("Rig reference layout differs.");
        for (var i = 0; i < output.Length; i++)
        {
            if (parents[i] < -1 || parents[i] >= i) throw new ArgumentException("Rig requires a parent-first skeleton.");
            var local = ToNative(localFbx[i]);
            output[i] = parents[i] < 0 ? local : AlsPrecisePose.Compose(local, output[parents[i]]).Normalized();
        }
    }
    private static AlsPrecisePose ToNative(AlsPrecisePose fbx)
    {
        fbx.Validate(.001); var p = fbx.Position; var q = fbx.Rotation;
        return new(new(p.X * 100, -p.Y * 100, p.Z * 100), new(-q.X, q.Y, -q.Z, q.W), fbx.Scale);
    }
    private static AlsPrecisePose FromNative(AlsPrecisePose native)
    {
        var p = native.Position; var q = native.Rotation;
        return new(new(p.X * .01, -p.Y * .01, p.Z * .01), new(-q.X, q.Y, -q.Z, q.W), native.Scale);
    }
    private static AlsPrecisePose ToNativeWorld(AlsLocalPose fbxWorld)
        => AlsFootIkCoordinates.FbxComponentToNativeWorld(fbxWorld);
    private void Require(Phase phase)
    { if (_phase != phase) throw new InvalidOperationException($"Refactored foot animation expected {phase}, got {_phase}."); }
}
