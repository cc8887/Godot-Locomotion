using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public sealed record AlsRefactoredFootRigDefinition(AlsRefactoredLegRigDefinition Leg, AlsPelvisRigDefinition Pelvis,
    AlsFootTraceRigDefinition Trace, double EnableThreshold, AlsRigLegBones Left, AlsRigLegBones Right,
    float LegLength, string LeftCurve, string RightCurve, string MovingCurve);

public readonly record struct AlsRigFootTarget(AlsDoubleVector Location, AlsQuaternion Rotation);
public readonly record struct AlsFootContactToeTarget(float Weight, int Bone, AlsPrecisePose LocalPose);
public readonly record struct AlsRefactoredFootRigPose(AlsPrecisePose Left, AlsPrecisePose Right)
{
    public AlsPrecisePose SourceLeft { get; init; }
    public AlsPrecisePose SourceRight { get; init; }
    public AlsRigFootTarget TargetLeft { get; init; }
    public AlsRigFootTarget TargetRight { get; init; }
    public bool LocationEvaluated { get; init; }
    public AlsFootOffsetLocationInput LeftInput { get; init; }
    public AlsFootOffsetLocationInput RightInput { get; init; }
    public AlsFootContactTarget LeftContact { get; init; }
    public AlsFootContactTarget RightContact { get; init; }
}
public readonly record struct AlsRefactoredFootRigInput(AlsFrameIdentity Identity, AlsFrameIdentity PreviousIdentity,
    float DeltaTime, bool ExecuteRig, bool FootTransformsValid, bool Reinitialize, float PelvisAmount,
    AlsRigFootTarget Left, AlsRigFootTarget Right, AlsPrecisePose ToWorld);

// Issued after the current pre-foot pose/curves and locked targets are available.
// Physics runs on its owning thread. The response must echo this exact request,
// including transform and both rays; a frame number alone cannot prevent reuse
// of an observation from a canceled candidate with different targets.
public readonly record struct AlsFootRigQueries(AlsFrameIdentity Identity, ulong RequestSerial, AlsPrecisePose ToWorld,
    bool LeftEnabled, bool RightEnabled, AlsFootTraceSegment Left, AlsFootTraceSegment Right);
public readonly record struct AlsFootRigObservations(AlsFootRigQueries Queries, AlsFootTraceRigHit Left, AlsFootTraceRigHit Right);
public readonly record struct AlsRefactoredFootRigState(AlsFootTraceRigResult LeftOffset, AlsFootTraceRigResult RightOffset,
    AlsRigSpringState PelvisSpring, float PelvisOffset, AlsRefactoredLegRigState Left, AlsRefactoredLegRigState Right)
{
    public bool LeftCalibrated { get; init; }
    public bool RightCalibrated { get; init; }
    public int CalibrationPasses { get; init; }
    public double LeftSupportDistance { get; init; }
    public bool LeftClearanceCorrected { get; init; }
    public bool RightClearanceCorrected { get; init; }
    public double RightSupportDistance { get; init; }
    public bool LeftToePinned { get; init; }
    public bool RightToePinned { get; init; }
    public static AlsRefactoredFootRigState Initial => new(default, default, default, 0,
        AlsRefactoredLegRigState.Initial, AlsRefactoredLegRigState.Initial);
}

// The Refactored foot slice: offsets -> pelvis -> left leg -> right leg.
// Source is native component space AFTER spine controls; hand IK remains a later
// consumer. No engine objects cross the query boundary. An enclosing animation
// transaction validates all consumers before committing this owner.
public sealed class AlsRefactoredFootRigFrame
{
    private enum Phase { Idle, Queries, Evaluated }
    private Phase _phase;
    private readonly AlsRefactoredFootRigDefinition _definition;
    private readonly AlsPrecisePose[] _reference, _scratch;
    private readonly int[] _parents;
    private readonly bool[] _changed;
    private readonly int _leftCurve, _rightCurve, _movingCurve;
    private AlsPrecisePose[] _committedPose, _candidatePose;
    private AlsInertialCurve[] _committedCurves, _candidateCurves;
    private AlsRefactoredFootRigInput _input;
    private AlsRefactoredFootRigInput _committedInput;
    private AlsPrecisePose _sourceLeft, _sourceRight, _committedSourceLeft, _committedSourceRight;
    private double _committedMoving;
    private AlsRefactoredFootRigState _candidate;
    private AlsFootRigQueries _queries;
    private ulong _querySerial;
    private float _leftAmount, _rightAmount, _moving;
    private AlsFootContactTarget _leftContact, _rightContact, _committedLeftContact, _committedRightContact;
    private readonly AlsFootSupportGeometry? _supportGeometry;
    internal bool HasSupportGeometry => _supportGeometry is not null;
    private readonly AlsPrecisePose[] _calibrationSource;

    public AlsFrameIdentity CommittedIdentity { get; private set; }
    public AlsRefactoredFootRigState Committed { get; private set; } = AlsRefactoredFootRigState.Initial;
    public ReadOnlySpan<AlsPrecisePose> CommittedPose => _committedPose;
    public AlsRefactoredFootRigInput CandidateInput => _phase is Phase.Queries or Phase.Evaluated
        ? _input : throw new InvalidOperationException("No active foot rig input.");
    public AlsRefactoredFootRigPose CommittedFeet => new(_committedPose[_definition.Left.Foot], _committedPose[_definition.Right.Foot])
    {
        SourceLeft = _committedSourceLeft, SourceRight = _committedSourceRight,
        TargetLeft = _committedInput.Left, TargetRight = _committedInput.Right,
        LocationEvaluated = _committedInput.ExecuteRig && _committedInput.FootTransformsValid,
        LeftInput = LocationInput(_definition.Left, _committedInput.Left, Committed.LeftOffset),
        RightInput = LocationInput(_definition.Right, _committedInput.Right, Committed.RightOffset),
        LeftContact = _committedLeftContact, RightContact = _committedRightContact,
    };
    private AlsFootOffsetLocationInput LocationInput(AlsRigLegBones bones, AlsRigFootTarget target, AlsFootTraceRigResult offset) =>
        new(_committedInput.DeltaTime, _committedPose[bones.Pelvis].Position.Z, _committedPose[bones.Thigh].Position,
            target.Location, offset.OffsetZ, Committed.PelvisOffset, _definition.LegLength,
            _definition.Leg.MinimumPelvisDistance.Interpolate(_committedMoving), _definition.Leg.MaximumStretch,
            _definition.Leg.OffsetFrequency, _definition.Leg.OffsetDamping, _definition.Leg.OffsetTargetVelocity);
    public ReadOnlySpan<AlsInertialCurve> CommittedCurves => _committedCurves;
    public AlsRefactoredFootRigState Candidate { get { RequireEvaluated(); return _candidate; } }
    public ReadOnlySpan<AlsPrecisePose> Pose { get { RequireEvaluated(); return _candidatePose; } }
    public ReadOnlySpan<AlsInertialCurve> Curves { get { RequireEvaluated(); return _candidateCurves; } }
    public AlsFootRigQueries Queries => _phase == Phase.Queries ? _queries : throw new InvalidOperationException("No pending foot queries.");

    public AlsRefactoredFootRigFrame(AlsRefactoredFootRigDefinition definition, ReadOnlySpan<int> parents,
        ReadOnlySpan<AlsPrecisePose> reference, ReadOnlySpan<string> curves, AlsFootSupportGeometry? supportGeometry = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Left.Pelvis != definition.Right.Pelvis || !double.IsFinite(definition.EnableThreshold) ||
            definition.EnableThreshold <= 0 || !float.IsFinite(definition.LegLength) || definition.LegLength <= 0)
            throw new ArgumentException("Invalid complete foot rig definition.");
        _definition = definition; _parents = parents.ToArray(); _reference = reference.ToArray();
        _supportGeometry = supportGeometry;
        _calibrationSource = supportGeometry is null ? [] : new AlsPrecisePose[reference.Length];
        _committedPose = reference.ToArray(); _candidatePose = new AlsPrecisePose[reference.Length];
        _committedSourceLeft = reference[definition.Left.Foot]; _committedSourceRight = reference[definition.Right.Foot];
        _scratch = new AlsPrecisePose[reference.Length]; _changed = new bool[reference.Length];
        _committedCurves = new AlsInertialCurve[curves.Length]; _candidateCurves = new AlsInertialCurve[curves.Length];
        AlsRigHierarchyWrites.Validate(_candidatePose, parents, _scratch, _changed);
        foreach (var pose in reference) pose.Validate();
        var layout = curves.ToArray();
        _leftCurve = Find(definition.LeftCurve); _rightCurve = Find(definition.RightCurve); _movingCurve = Find(definition.MovingCurve);
        int Find(string name)
        {
            var index = Array.IndexOf(layout, name);
            if (index < 0 || Array.LastIndexOf(layout, name) != index)
                throw new ArgumentException("Missing/duplicate Refactored rig curve: " + name);
            return index;
        }
    }

    public AlsFootRigQueries Prepare(in AlsRefactoredFootRigInput input, ReadOnlySpan<AlsPrecisePose> source,
        ReadOnlySpan<AlsInertialCurve> curves)
    {
        if (_phase != Phase.Idle) throw new InvalidOperationException("Foot rig candidate already active.");
        if (input.Identity.SlotGeneration == 0 || input.PreviousIdentity != CommittedIdentity ||
            CommittedIdentity != default && (input.Identity.CharacterId != CommittedIdentity.CharacterId ||
                input.Identity.SlotGeneration != CommittedIdentity.SlotGeneration || input.Identity.FrameId != CommittedIdentity.FrameId + 1))
            throw new ArgumentException("Foot rig frame/history identity mismatch.");
        if (!float.IsFinite(input.DeltaTime) || input.DeltaTime < 0 || !float.IsFinite(input.PelvisAmount) ||
            source.Length != _reference.Length || curves.Length != _candidateCurves.Length)
            throw new ArgumentException("Invalid foot rig frame dimensions/time.");
        foreach (var pose in source) pose.Validate();
        foreach (var curve in curves) if (!float.IsFinite(curve.Value)) throw new ArgumentException("Nonfinite rig curve.");
        _leftAmount = Value(curves[_leftCurve]); _rightAmount = Value(curves[_rightCurve]); _moving = Value(curves[_movingCurve]);
        var visit = input.ExecuteRig && input.FootTransformsValid;
        var leftEnabled = visit && _leftAmount >= _definition.EnableThreshold;
        var rightEnabled = visit && _rightAmount >= _definition.EnableThreshold;
        // Disabled traces have no observation dependency. Leg controls may still
        // run at zero weight, as authored; they validate their own target inputs.
        var left = leftEnabled ? AlsFootTraceRigModel.Trace(_definition.Trace, input.Left.Location, input.ToWorld) : default;
        var right = rightEnabled ? AlsFootTraceRigModel.Trace(_definition.Trace, input.Right.Location, input.ToWorld) : default;
        // Transport serial is deliberately NOT rolled back with animation state.
        // A canceled request must not satisfy an identical same-frame retry.
        _querySerial = checked(_querySerial + 1);
        _queries = new(input.Identity, _querySerial, input.ToWorld, leftEnabled, rightEnabled, left, right);
        source.CopyTo(_candidatePose); curves.CopyTo(_candidateCurves);
        _sourceLeft = source[_definition.Left.Foot]; _sourceRight = source[_definition.Right.Foot];
        _input = input; _candidate = input.Reinitialize ? AlsRefactoredFootRigState.Initial : Committed;
        _phase = Phase.Queries;
        return _queries;
    }

    public void Evaluate(in AlsFootRigObservations observations,
        AlsFootContactTarget leftContact = default, AlsFootContactTarget rightContact = default,
        bool calibrateLeft = false, bool calibrateRight = false,
        bool clearLeftPenetration = false, bool clearRightPenetration = false,
        AlsFootContactToeTarget leftToe = default, AlsFootContactToeTarget rightToe = default)
    {
        if (_supportGeometry is null || !calibrateLeft && !calibrateRight && !clearLeftPenetration && !clearRightPenetration || !_input.ExecuteRig || !_input.FootTransformsValid)
        { EvaluateOnce(observations, leftContact, rightContact, leftToe, rightToe); return; }
        if (_phase != Phase.Queries) throw new InvalidOperationException("Contact calibration needs issued queries.");
        try
        {
            var initial = _candidate;
            var correctedLeft = false; var correctedRight = false;
            _candidatePose.CopyTo(_calibrationSource, 0);
            for (var pass = 1; pass <= 3; pass++)
            {
                EvaluateOnce(observations, leftContact, rightContact, leftToe, rightToe);
                var left = (calibrateLeft || clearLeftPenetration) && _candidate.LeftOffset.Walkable && _leftAmount >= 1 - AlsPoseBlender.WeightThreshold;
                var right = (calibrateRight || clearRightPenetration) && _candidate.RightOffset.Walkable && _rightAmount >= 1 - AlsPoseBlender.WeightThreshold;
                var leftDistance = left ? _supportGeometry.MinimumDistance(true, _candidatePose, _input.ToWorld, observations.Left) : 0;
                var rightDistance = right ? _supportGeometry.MinimumDistance(false, _candidatePose, _input.ToWorld, observations.Right) : 0;
                _candidate = _candidate with { LeftCalibrated = left && calibrateLeft, RightCalibrated = right && calibrateRight, CalibrationPasses = pass,
                    LeftClearanceCorrected = correctedLeft, RightClearanceCorrected = correctedRight,
                    LeftSupportDistance = leftDistance, RightSupportDistance = rightDistance };
                // Free swing feet only move out of the surface. New planted
                // contacts retain their bidirectional calibration contract.
                var adjustLeft = left && (calibrateLeft ? System.Math.Abs(leftDistance) >= .0001 : leftDistance < -.0001);
                var adjustRight = right && (calibrateRight ? System.Math.Abs(rightDistance) >= .0001 : rightDistance < -.0001);
                if (pass == 3 || !adjustLeft && !adjustRight) break;
                if (left && calibrateLeft || adjustLeft) { leftContact = CorrectContact(_candidate.Left.Location.FootLocation, observations.Left, leftDistance); correctedLeft |= !calibrateLeft; }
                if (right && calibrateRight || adjustRight) { rightContact = CorrectContact(_candidate.Right.Location.FootLocation, observations.Right, rightDistance); correctedRight |= !calibrateRight; }
                // Re-evaluate this SAME candidate from the SAME committed
                // history and original source pose. No clock or spring advances
                // twice; the original issued rays and observations stay intact.
                _calibrationSource.CopyTo(_candidatePose, 0); _candidate = initial; _phase = Phase.Queries;
            }
        }
        catch { Cancel(); throw; }
    }

    private AlsFootContactTarget CorrectContact(AlsDoubleVector location, AlsFootTraceRigHit hit, double distance)
    {
        var world = hit.Normal * (-distance / System.Math.Sqrt(hit.Normal.LengthSquared));
        var local = world.Rotate(_input.ToWorld.Rotation.Conjugate()) *
            new AlsDoubleVector(1 / _input.ToWorld.Scale.X, 1 / _input.ToWorld.Scale.Y, 1 / _input.ToWorld.Scale.Z);
        return new(1, location + local);
    }

    private void EvaluateOnce(in AlsFootRigObservations observations,
        AlsFootContactTarget leftContact, AlsFootContactTarget rightContact,
        AlsFootContactToeTarget leftToe, AlsFootContactToeTarget rightToe)
    {
        if (_phase != Phase.Queries) throw new InvalidOperationException("Foot rig must prepare its queries first.");
        try
        {
            if (observations.Queries != _queries) throw new ArgumentException("Foot query response belongs to another candidate.");
            _leftContact = _rightContact = default;
            if (_input.ExecuteRig)
            {
                var leftOffset = _candidate.LeftOffset; var rightOffset = _candidate.RightOffset;
                if (_input.FootTransformsValid)
                {
                    leftOffset = Offset(_queries.LeftEnabled, observations.Left);
                    rightOffset = Offset(_queries.RightEnabled, observations.Right);
                }
                // This function is outside the validity branches: saved offsets
                // and the pelvis spring continue when feet temporarily become invalid.
                var pelvis = AlsPelvisRigModel.Evaluate(_candidate.PelvisSpring, _definition.Pelvis, _input.DeltaTime,
                    leftOffset.OffsetZ, rightOffset.OffsetZ, _input.PelvisAmount, _candidatePose[_definition.Left.Pelvis].Position);
                _leftContact = leftOffset.Walkable && _leftAmount >= 1 - AlsPoseBlender.WeightThreshold ? leftContact : default;
                _rightContact = rightOffset.Walkable && _rightAmount >= 1 - AlsPoseBlender.WeightThreshold ? rightContact : default;
                var originalShift = pelvis.Location - _candidatePose[_definition.Left.Pelvis].Position;
                var maximum = _definition.LegLength * _definition.Leg.MaximumStretch;
                var lower = System.Math.Min(
                    AlsFootContactAnchoring.PelvisLowering(_candidatePose[_definition.Left.Thigh].Position + originalShift, _leftContact, maximum),
                    AlsFootContactAnchoring.PelvisLowering(_candidatePose[_definition.Right.Thigh].Position + originalShift, _rightContact, maximum));
                if (lower < 0) pelvis = pelvis with { Offset = pelvis.Offset + (float)lower,
                    Location = pelvis.Location + new AlsDoubleVector(0, 0, lower) };
                AlsRigHierarchyWrites.SetGlobal(_candidatePose, _parents, _definition.Left.Pelvis,
                    _candidatePose[_definition.Left.Pelvis] with { Position = pelvis.Location }, _scratch, _changed);
                var left = _candidate.Left; var right = _candidate.Right;
                if (_input.FootTransformsValid)
                {
                    left = Leg(left, _definition.Left, _input.Left, leftOffset, pelvis.Offset, _leftAmount, _leftContact);
                    right = Leg(right, _definition.Right, _input.Right, rightOffset, pelvis.Offset, _rightAmount, _rightContact);
                }
                _candidate = new(leftOffset, rightOffset, pelvis.Spring, pelvis.Offset, left, right)
                {
                    LeftToePinned = PinToe(leftToe, _definition.Left.Foot, _leftContact),
                    RightToePinned = PinToe(rightToe, _definition.Right.Foot, _rightContact)
                };
            }
            _phase = Phase.Evaluated;
        }
        catch { Cancel(); throw; }
    }

    public void ValidateCommit(AlsFrameIdentity identity)
    {
        if (_phase != Phase.Evaluated || identity != _input.Identity)
            throw new InvalidOperationException("Foot rig requires one matching completed candidate.");
    }
    private bool PinToe(AlsFootContactToeTarget target, int foot, AlsFootContactTarget contact)
    {
        if (target.Weight <= AlsPoseBlender.WeightThreshold || contact.Weight <= 0) return false;
        if (!float.IsFinite(target.Weight) || target.Weight > 1 || target.Bone < 0 || target.Bone >= _parents.Length || _parents[target.Bone] != foot)
            throw new ArgumentException("Contact toe must be a direct child of its foot and have a valid weight.");
        var local = AlsPrecisePose.Relative(_candidatePose[target.Bone], _candidatePose[foot]).Normalized();
        var blended = AlsPrecisePose.BlendTransform(local, target.LocalPose, target.Weight);
        AlsRigHierarchyWrites.SetGlobal(_candidatePose, _parents, target.Bone,
            AlsPrecisePose.Compose(blended, _candidatePose[foot]).Normalized(), _scratch, _changed);
        return true;
    }
    public void Commit(AlsFrameIdentity identity)
    {
        ValidateCommit(identity);
        Committed = _candidate; CommittedIdentity = identity;
        _committedInput = _input; _committedMoving = _moving;
        _committedLeftContact = _leftContact; _committedRightContact = _rightContact;
        _committedSourceLeft = _sourceLeft; _committedSourceRight = _sourceRight;
        (_committedPose, _candidatePose) = (_candidatePose, _committedPose);
        (_committedCurves, _candidateCurves) = (_candidateCurves, _committedCurves);
        _phase = Phase.Idle;
    }
    public void Cancel() => _phase = Phase.Idle;
    private void RequireEvaluated()
    { if (_phase != Phase.Evaluated) throw new InvalidOperationException("No completed foot rig candidate."); }
    private static float Value(AlsInertialCurve curve) => curve.Present ? curve.Value : 0;
    private AlsFootTraceRigResult Offset(bool enabled, AlsFootTraceRigHit hit) => enabled ?
        AlsFootTraceRigModel.Evaluate(_definition.Trace, true, hit, _input.ToWorld) : new(false, 0, new(0, 0, 1));
    private AlsRefactoredLegRigState Leg(AlsRefactoredLegRigState previous, AlsRigLegBones bones,
        AlsRigFootTarget target, AlsFootTraceRigResult offset, float pelvisOffset, float amount, AlsFootContactTarget contact) =>
        AlsRefactoredLegRig.Evaluate(previous, _definition.Leg, bones,
            new(_input.DeltaTime, target.Location, target.Rotation, offset.OffsetZ, offset.OffsetNormal,
                pelvisOffset, _definition.LegLength, _moving, amount) { Contact = contact },
            _reference, _candidatePose, _parents, _scratch, _changed);
}
