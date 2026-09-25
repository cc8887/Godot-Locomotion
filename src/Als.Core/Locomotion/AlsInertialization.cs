using System.Numerics;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsInertialCurve(float Value, bool Present = true);
internal readonly record struct AlsInertialVector(Vector3 Axis, float Magnitude, float Speed);
internal readonly record struct AlsInertialBone(AlsInertialVector Translation, AlsInertialVector Rotation, AlsInertialVector Scale);
internal readonly record struct AlsInertialCurveDiff(float Delta, float Speed, bool Present);

public static class AlsInertialDecay
{
    public static float Evaluate(float offset, float velocity, float elapsed, float duration)
    {
        if (!float.IsFinite(offset) || !float.IsFinite(velocity) || !float.IsFinite(elapsed) ||
            !float.IsFinite(duration) || duration < 0) throw new ArgumentException("Invalid inertial decay input.");
        elapsed = MathF.Max(elapsed, 0);
        if (elapsed >= duration - 1e-7f) return 0;
        var sign = offset < 0 ? -1f : 1f;
        offset *= sign;
        velocity = MathF.Min(velocity * sign, 0);
        if (velocity < -.0001f) duration = MathF.Min(duration, -5 * offset / velocity);
        if (elapsed >= duration - 1e-7f) return 0;
        var t2 = duration * duration;
        var t3 = duration * t2;
        var t4 = duration * t3;
        var t5 = duration * t4;
        var acceleration = MathF.Max(0, (-8 * duration * velocity - 20 * offset) / t2);
        var a = -.5f * (acceleration * t2 + 6 * duration * velocity + 12 * offset) / t5;
        var b = .5f * (3 * acceleration * t2 + 16 * duration * velocity + 30 * offset) / t4;
        var c = -.5f * (3 * acceleration * t2 + 12 * duration * velocity + 20 * offset) / t3;
        return (((((a * elapsed + b) * elapsed + c) * elapsed + .5f * acceleration) * elapsed + velocity) * elapsed + offset) * sign;
    }
}

/// <summary>Full fixed bone/curve layout, default ALS filters/profile, no root-motion attributes.
/// Own one instance per candidate/committed state and use CopyFrom for transactional evaluation.</summary>
public sealed partial class AlsInertialization
{
    private readonly AlsLocalPose[] _current;
    private readonly AlsLocalPose[] _previous;
    private readonly AlsQuaternion[] _preciseCurrent;
    private readonly AlsQuaternion[] _precisePrevious;
    private AlsQuaternion _preciseCurrentComponent = AlsQuaternion.Identity;
    private AlsQuaternion _precisePreviousComponent = AlsQuaternion.Identity;
    private int _rotationMode;
    private readonly AlsInertialBone[] _bones;
    private readonly AlsInertialCurve[] _currentCurves;
    private readonly AlsInertialCurve[] _previousCurves;
    private readonly AlsInertialCurveDiff[] _curveDiffs;
    private readonly float _unitsPerCentimeter;
    private readonly Vector3 _rotationFallbackAxis;
    private AlsLocalPose _currentComponent = AlsLocalPose.Identity;
    private AlsLocalPose _previousComponent = AlsLocalPose.Identity;
    private long _currentParent;
    private long _previousParent;
    private float _historyDelta;
    private float _delta;
    private float _request = -1;
    private int _historyCount;
    public bool IsActive { get; private set; }
    public float ElapsedSeconds { get; private set; }
    public float DurationSeconds { get; private set; }
    public float DeficitSeconds { get; private set; }
    public int HistoryCount => _historyCount;

    public AlsInertialization(int boneCount, int curveCount, float unitsPerCentimeter = 1, Vector3? rotationFallbackAxis = null)
    {
        if (boneCount <= 0 || curveCount < 0 || !float.IsFinite(unitsPerCentimeter) || unitsPerCentimeter <= 0)
            throw new ArgumentOutOfRangeException(nameof(boneCount));
        _unitsPerCentimeter = unitsPerCentimeter;
        _rotationFallbackAxis = rotationFallbackAxis ?? Vector3.UnitX;
        if (!Finite(_rotationFallbackAxis) || MathF.Abs(_rotationFallbackAxis.LengthSquared() - 1) > 1e-6f)
            throw new ArgumentException("The fallback rotation axis must be a unit vector in the pose coordinate system.", nameof(rotationFallbackAxis));
        _current = new AlsLocalPose[boneCount];
        _previous = new AlsLocalPose[boneCount];
        _fullCurrent = new AlsPrecisePose[boneCount]; _fullPrevious = new AlsPrecisePose[boneCount];
        _preciseCurrent = new AlsQuaternion[boneCount];
        _precisePrevious = new AlsQuaternion[boneCount];
        _bones = new AlsInertialBone[boneCount];
        _currentCurves = new AlsInertialCurve[curveCount];
        _previousCurves = new AlsInertialCurve[curveCount];
        _curveDiffs = new AlsInertialCurveDiff[curveCount];
    }

    public void Update(float delta)
    {
        if (!float.IsFinite(delta) || delta < 0 || !float.IsFinite(_delta + delta)) throw new ArgumentOutOfRangeException(nameof(delta));
        _delta += delta;
    }

    public void Request(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        _request = _request < 0 ? seconds : MathF.Min(_request, seconds);
    }

    public void Reset()
    {
        Deactivate();
        _historyCount = 0;
        _rotationMode = 0;
        _historyDelta = _delta = 0;
        _request = -1;
    }

    public void CopyFrom(AlsInertialization source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source._current.Length != _current.Length || source._currentCurves.Length != _currentCurves.Length ||
            source._unitsPerCentimeter != _unitsPerCentimeter || source._rotationFallbackAxis != _rotationFallbackAxis)
            throw new ArgumentException("Inertial layouts differ.");
        source._current.CopyTo(_current, 0); source._previous.CopyTo(_previous, 0); source._bones.CopyTo(_bones, 0);
        if (source._rotationMode == 2)
        {
            source._preciseCurrent.CopyTo(_preciseCurrent, 0); source._precisePrevious.CopyTo(_precisePrevious, 0);
            _preciseCurrentComponent = source._preciseCurrentComponent; _precisePreviousComponent = source._precisePreviousComponent;
        }
        _rotationMode = source._rotationMode;
        if (source._rotationMode == 3)
        {
            source._fullCurrent.CopyTo(_fullCurrent, 0); source._fullPrevious.CopyTo(_fullPrevious, 0);
            _fullCurrentComponent = source._fullCurrentComponent; _fullPreviousComponent = source._fullPreviousComponent;
        }
        source._currentCurves.CopyTo(_currentCurves, 0); source._previousCurves.CopyTo(_previousCurves, 0); source._curveDiffs.CopyTo(_curveDiffs, 0);
        _currentComponent = source._currentComponent; _previousComponent = source._previousComponent;
        _currentParent = source._currentParent; _previousParent = source._previousParent;
        _historyDelta = source._historyDelta; _delta = source._delta; _request = source._request; _historyCount = source._historyCount;
        IsActive = source.IsActive; ElapsedSeconds = source.ElapsedSeconds; DurationSeconds = source.DurationSeconds; DeficitSeconds = source.DeficitSeconds;
    }

    public void Evaluate(ReadOnlySpan<AlsLocalPose> input, ReadOnlySpan<AlsInertialCurve> curves,
        in AlsLocalPose component, long attachParent, float teleportDistance, Span<AlsLocalPose> output, Span<AlsInertialCurve> outputCurves)
        => EvaluateCore(input, curves, component, attachParent, teleportDistance, output, outputCurves,
            default, default, default, false);

    /// <summary>Preserves native double rotations through difference, application and history.
    /// Input pose rotations must be the single projection of preciseInput; callers must supply
    /// the actual upstream double result, not a reconstructed normalized single quaternion.</summary>
    public void EvaluatePrecise(ReadOnlySpan<AlsLocalPose> input, ReadOnlySpan<AlsInertialCurve> curves,
        in AlsLocalPose component, long attachParent, float teleportDistance, Span<AlsLocalPose> output,
        Span<AlsInertialCurve> outputCurves, ReadOnlySpan<AlsQuaternion> preciseInput,
        in AlsQuaternion preciseComponent, Span<AlsQuaternion> preciseOutput)
        => EvaluateCore(input, curves, component, attachParent, teleportDistance, output, outputCurves,
            preciseInput, preciseComponent, preciseOutput, true);

    private void EvaluateCore(ReadOnlySpan<AlsLocalPose> input, ReadOnlySpan<AlsInertialCurve> curves,
        in AlsLocalPose component, long attachParent, float teleportDistance, Span<AlsLocalPose> output,
        Span<AlsInertialCurve> outputCurves, ReadOnlySpan<AlsQuaternion> preciseInput,
        in AlsQuaternion preciseComponent, Span<AlsQuaternion> preciseOutput, bool precise)
    {
        if (_rotationMode != 0 && _rotationMode != (precise ? 2 : 1))
            throw new InvalidOperationException("Reset inertial history before changing rotation precision.");
        if (precise)
        {
            if (preciseInput.Length != input.Length || preciseOutput.Length != input.Length ||
                !Valid(preciseComponent) || preciseComponent.ToSingle() != component.Rotation ||
                (preciseInput.Overlaps(preciseOutput, out var preciseOffset) && preciseOffset != 0))
                throw new ArgumentException("Invalid precise inertial rotation layout or component.");
            for (var i = 0; i < input.Length; i++)
                if (!Valid(preciseInput[i]) || preciseInput[i].ToSingle() != input[i].Rotation)
                    throw new ArgumentException("Precise rotation and pose projection differ.");
        }
        if (input.Length != _current.Length || output.Length != input.Length || curves.Length != _currentCurves.Length ||
            outputCurves.Length != curves.Length || !Valid(component) || !float.IsFinite(teleportDistance) || teleportDistance < 0 ||
            (input.Overlaps(output, out var offset) && offset != 0) || (curves.Overlaps(outputCurves, out var curveOffset) && curveOffset != 0))
            throw new ArgumentException("Invalid inertial evaluation layout or component.");
        foreach (var pose in input) if (!Valid(pose)) throw new ArgumentException("Invalid inertial input pose.");
        foreach (var curve in curves) if (!float.IsFinite(curve.Value)) throw new ArgumentException("Invalid inertial input curve.");
        _rotationMode = precise ? 2 : 1;
        var pending = _request >= 0 && _historyCount > 0;
        if (pending)
        {
            var appliedDeficit = 0f;
            if (IsActive)
            {
                var apply = DeficitSeconds > 0;
                DeficitSeconds = DurationSeconds - ElapsedSeconds;
                if (apply) appliedDeficit = DeficitSeconds;
            }
            ElapsedSeconds = 0;
            DurationSeconds = MathF.Max(_request - appliedDeficit, 0);
        }
        _request = -1;
        if (pending || IsActive)
        {
            ElapsedSeconds += _delta;
            DeficitSeconds = ElapsedSeconds >= DurationSeconds ? 0 : DeficitSeconds - MathF.Min(DeficitSeconds, _delta);
            if (ElapsedSeconds >= DurationSeconds) { Deactivate(); pending = false; }
        }
        if (_historyCount > 0 && teleportDistance > 0 && Vector3.DistanceSquared(WorldPosition(component, input[0].Position),
            WorldPosition(_currentComponent, _current[0].Position)) > teleportDistance * teleportDistance)
        {
            if (pending) { Deactivate(); pending = false; }
            _delta = 0;
        }
        if (pending)
        {
            InitializeDifference(input, curves, component, attachParent);
            if (precise) InitializePreciseRotations(preciseInput, preciseComponent, attachParent);
            IsActive = true;
        }
        input.CopyTo(output);
        curves.CopyTo(outputCurves);
        if (precise) preciseInput.CopyTo(preciseOutput);
        if (IsActive)
        {
            for (var bone = 0; bone < output.Length; bone++)
            {
                var diff = _bones[bone];
                var pose = output[bone];
                if (precise)
                {
                    preciseOutput[bone] = (AlsQuaternion.FromAxisAngle(diff.Rotation.Axis, Decay(diff.Rotation)) * preciseOutput[bone]).Normalized();
                    output[bone] = new(pose.Position + DecayVector(diff.Translation) * _unitsPerCentimeter,
                        preciseOutput[bone].ToSingle(), pose.Scale + DecayVector(diff.Scale));
                }
                else output[bone] = AlsPoseBlender.Normalize(new(pose.Position + DecayVector(diff.Translation) * _unitsPerCentimeter,
                        Quaternion.CreateFromAxisAngle(diff.Rotation.Axis, Decay(diff.Rotation)) * pose.Rotation,
                        pose.Scale + DecayVector(diff.Scale)));
            }
            for (var curve = 0; curve < outputCurves.Length; curve++)
            {
                var diff = _curveDiffs[curve];
                var incoming = outputCurves[curve];
                outputCurves[curve] = new((incoming.Present ? incoming.Value : 0) +
                    AlsInertialDecay.Evaluate(diff.Delta, diff.Speed, ElapsedSeconds, DurationSeconds), incoming.Present || diff.Present);
            }
        }
        if (_historyCount > 0)
        {
            _current.CopyTo(_previous, 0); _currentCurves.CopyTo(_previousCurves, 0);
            _previousComponent = _currentComponent; _previousParent = _currentParent;
            if (precise) { _preciseCurrent.CopyTo(_precisePrevious, 0); _precisePreviousComponent = _preciseCurrentComponent; }
        }
        output.CopyTo(_current); outputCurves.CopyTo(_currentCurves);
        _currentComponent = component; _currentParent = attachParent;
        if (precise) { preciseOutput.CopyTo(_preciseCurrent); _preciseCurrentComponent = preciseComponent; }
        _historyDelta = _delta; _historyCount = System.Math.Min(_historyCount + 1, 2); _delta = 0;
    }

    private void InitializePreciseRotations(ReadOnlySpan<AlsQuaternion> input, in AlsQuaternion component, long parent)
    {
        var previousComponent = _historyCount == 1 ? _preciseCurrentComponent : _precisePreviousComponent;
        var previousParent = _historyCount == 1 ? _currentParent : _previousParent;
        var inverse = component.Conjugate();
        var rebaseRoot = parent != _currentParent || parent != previousParent || parent == 0 &&
            (System.Math.Abs((_preciseCurrentComponent * inverse).W) < .999f || System.Math.Abs((previousComponent * inverse).W) < .999f);
        for (var bone = 0; bone < input.Length; bone++)
        {
            var latest = _preciseCurrent[bone];
            var older = _historyCount == 1 ? latest : _precisePrevious[bone];
            if (bone == 0 && rebaseRoot)
            { latest = inverse * _preciseCurrentComponent * latest; older = inverse * previousComponent * older; }
            var q = latest * input[bone].Conjugate();
            var lengthSquared = q.X * q.X + q.Y * q.Y + q.Z * q.Z;
            var reciprocal = lengthSquared >= 1e-8 ? 1 / System.Math.Sqrt(lengthSquared) : 0;
            var x = reciprocal > 0 ? q.X * reciprocal : _rotationFallbackAxis.X;
            var y = reciprocal > 0 ? q.Y * reciprocal : _rotationFallbackAxis.Y;
            var z = reciprocal > 0 ? q.Z * reciprocal : _rotationFallbackAxis.Z;
            var angle = Unwind((float)(2 * System.Math.Acos(System.Math.Clamp(q.W, -1, 1))));
            if (angle < 0) { angle = -angle; x = -x; y = -y; z = -z; }
            var speed = 0f;
            if (_historyDelta > .0001f && angle > .0001f)
            {
                var old = older * input[bone].Conjugate();
                var oldAngle = 2 * System.Math.Atan2(x * old.X + y * old.Y + z * old.Z, old.W);
                while (oldAngle > System.Math.PI) oldAngle -= 2 * System.Math.PI;
                while (oldAngle < -System.Math.PI) oldAngle += 2 * System.Math.PI;
                speed = Unwind(angle - (float)oldAngle) / _historyDelta;
            }
            _bones[bone] = _bones[bone] with { Rotation = new(new((float)x, (float)y, (float)z), angle, speed) };
        }
    }

    private void InitializeDifference(ReadOnlySpan<AlsLocalPose> input, ReadOnlySpan<AlsInertialCurve> curves, in AlsLocalPose component, long parent)
    {
        var previousComponent = _historyCount == 1 ? _currentComponent : _previousComponent;
        var previousParent = _historyCount == 1 ? _currentParent : _previousParent;
        var inverse = Quaternion.Conjugate(component.Rotation);
        var worldSpace = parent != _currentParent || parent != previousParent;
        var worldRotation = !worldSpace && parent == 0 && (MathF.Abs((_currentComponent.Rotation * inverse).W) < .999f ||
            MathF.Abs((previousComponent.Rotation * inverse).W) < .999f);
        for (var bone = 0; bone < input.Length; bone++)
        {
            var latest = _current[bone];
            var older = _historyCount == 1 ? latest : _previous[bone];
            if (bone == 0 && worldSpace)
            {
                latest = Rebase(latest, _currentComponent, component);
                older = Rebase(older, previousComponent, component);
            }
            else if (bone == 0 && worldRotation)
            {
                latest = latest with { Rotation = inverse * _currentComponent.Rotation * latest.Rotation };
                older = older with { Rotation = inverse * previousComponent.Rotation * older.Rotation };
            }
            var rotation = latest.Rotation * Quaternion.Conjugate(input[bone].Rotation);
            var xyz = new Vector3(rotation.X, rotation.Y, rotation.Z);
            var axis = xyz.LengthSquared() >= 1e-8f ? Vector3.Normalize(xyz) : _rotationFallbackAxis;
            var angle = Unwind(2 * MathF.Acos(System.Math.Clamp(rotation.W, -1, 1)));
            if (angle < 0) { angle = -angle; axis = -axis; }
            var speed = 0f;
            if (_historyDelta > .0001f && angle > .0001f)
            {
                var oldRotation = older.Rotation * Quaternion.Conjugate(input[bone].Rotation);
                var oldAngle = Unwind(2 * MathF.Atan2(Vector3.Dot(axis, new(oldRotation.X, oldRotation.Y, oldRotation.Z)), oldRotation.W));
                speed = Unwind(angle - oldAngle) / _historyDelta;
            }
            _bones[bone] = new(VectorDifference((latest.Position - input[bone].Position) / _unitsPerCentimeter,
                (older.Position - input[bone].Position) / _unitsPerCentimeter), new(axis, angle, speed),
                VectorDifference(latest.Scale - input[bone].Scale, older.Scale - input[bone].Scale));
        }
        for (var curve = 0; curve < curves.Length; curve++)
        {
            var incoming = curves[curve];
            var latest = _currentCurves[curve];
            var older = _historyCount == 1 ? latest : _previousCurves[curve];
            var currentValue = incoming.Present ? incoming.Value : 0;
            var delta = (latest.Present ? latest.Value : 0) - currentValue;
            // Preserve this UE version's CurveDiffs Delta - Value derivative expression.
            var derivative = _historyDelta > .0001f ? (delta - currentValue - (older.Present ? older.Value : 0)) / _historyDelta : 0;
            _curveDiffs[curve] = new(delta, derivative, incoming.Present || latest.Present || older.Present);
        }
    }

    private AlsInertialVector VectorDifference(Vector3 difference, Vector3 previous)
    {
        var magnitude = difference.Length();
        var axis = magnitude > .0001f ? difference / magnitude : Vector3.Zero;
        var speed = magnitude > .0001f && _historyDelta > .0001f ? (magnitude - Vector3.Dot(previous, axis)) / _historyDelta : 0;
        return new(axis, magnitude, speed);
    }
    private float Decay(in AlsInertialVector value) => AlsInertialDecay.Evaluate(value.Magnitude, value.Speed, ElapsedSeconds, DurationSeconds);
    private Vector3 DecayVector(in AlsInertialVector value) => value.Axis * Decay(value);
    private void Deactivate() { IsActive = false; ElapsedSeconds = DurationSeconds = DeficitSeconds = 0; }
    private static float Unwind(float value)
    {
        while (value > MathF.PI) value -= 2 * MathF.PI;
        while (value < -MathF.PI) value += 2 * MathF.PI;
        return value;
    }
    private static Vector3 WorldPosition(in AlsLocalPose component, Vector3 position) =>
        Vector3.Transform(component.Scale * position, component.Rotation) + component.Position;
    private static AlsLocalPose Rebase(in AlsLocalPose pose, in AlsLocalPose oldComponent, in AlsLocalPose component)
    {
        var inverse = Quaternion.Conjugate(component.Rotation);
        var reciprocal = new Vector3(SafeReciprocal(component.Scale.X), SafeReciprocal(component.Scale.Y), SafeReciprocal(component.Scale.Z));
        var rotation = inverse * oldComponent.Rotation;
        var scale = oldComponent.Scale * reciprocal;
        var translation = Vector3.Transform(oldComponent.Position - component.Position, inverse) * reciprocal;
        return new(Vector3.Transform(scale * pose.Position, rotation) + translation, rotation * pose.Rotation, scale * pose.Scale);
    }
    private static float SafeReciprocal(float value) => MathF.Abs(value) <= 1e-8f ? 0 : 1 / value;
    private static bool Valid(in AlsLocalPose pose) => Finite(pose.Position) && Finite(pose.Scale) &&
        float.IsFinite(pose.Rotation.LengthSquared()) && MathF.Abs(pose.Rotation.LengthSquared() - 1) < .001f;
    private static bool Valid(in AlsQuaternion rotation) => double.IsFinite(rotation.LengthSquared) && System.Math.Abs(rotation.LengthSquared - 1) < .001;
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
