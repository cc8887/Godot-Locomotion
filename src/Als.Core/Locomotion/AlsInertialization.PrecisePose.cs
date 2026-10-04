using System.Numerics;
using ScalarMath = System.Math;

namespace GodotAls.Core.Locomotion;

public sealed partial class AlsInertialization
{
    private readonly AlsPrecisePose[] _fullCurrent, _fullPrevious;
    private AlsPrecisePose _fullCurrentComponent = AlsPrecisePose.Identity, _fullPreviousComponent = AlsPrecisePose.Identity;

    /// <summary>Native FTransform pose/history precision. Difference axes and
    /// magnitudes retain UE's explicit FVector3f/float storage boundaries.</summary>
    public void EvaluatePrecisePose(ReadOnlySpan<AlsPrecisePose> input, ReadOnlySpan<AlsInertialCurve> curves,
        in AlsPrecisePose component, long attachParent, float teleportDistance,
        Span<AlsPrecisePose> output, Span<AlsInertialCurve> outputCurves)
    {
        if (_rotationMode is not (0 or 3)) throw new InvalidOperationException("Reset inertial history before changing pose precision.");
        if (input.Length != _fullCurrent.Length || output.Length != input.Length || curves.Length != _currentCurves.Length ||
            outputCurves.Length != curves.Length || !float.IsFinite(teleportDistance) || teleportDistance < 0 ||
            input.Overlaps(output, out var offset) && offset != 0 || curves.Overlaps(outputCurves, out var curveOffset) && curveOffset != 0)
            throw new ArgumentException("Invalid precise inertial pose layout.");
        component.Validate(.001); foreach (var p in input) p.Validate(.001);
        foreach (var c in curves) if (!float.IsFinite(c.Value)) throw new ArgumentException("Invalid inertial curve.");
        _rotationMode = 3;
        var pending = _request >= 0 && _historyCount > 0;
        if (pending)
        {
            var appliedDeficit = 0f;
            if (IsActive)
            {
                var apply = DeficitSeconds > 0; DeficitSeconds = DurationSeconds - ElapsedSeconds;
                if (apply) appliedDeficit = DeficitSeconds;
            }
            ElapsedSeconds = 0; DurationSeconds = MathF.Max(_request - appliedDeficit, 0);
        }
        _request = -1;
        if (pending || IsActive)
        {
            ElapsedSeconds += _delta;
            DeficitSeconds = ElapsedSeconds >= DurationSeconds ? 0 : DeficitSeconds - MathF.Min(DeficitSeconds, _delta);
            if (ElapsedSeconds >= DurationSeconds) { Deactivate(); pending = false; }
        }
        if (_historyCount > 0 && teleportDistance > 0 &&
            (World(component, input[0].Position) - World(_fullCurrentComponent, _fullCurrent[0].Position)).LengthSquared >
            teleportDistance * teleportDistance)
        {
            if (pending) { Deactivate(); pending = false; }
            _delta = 0;
        }
        if (pending) { InitializeFullDifference(input, curves, component, attachParent); IsActive = true; }
        input.CopyTo(output); curves.CopyTo(outputCurves);
        if (IsActive)
        {
            for (var bone = 0; bone < output.Length; bone++)
            {
                var d = _bones[bone]; var p = output[bone];
                output[bone] = new(p.Position + new AlsDoubleVector(d.Translation.Axis) * ((double)Decay(d.Translation) * _unitsPerCentimeter),
                    (AlsQuaternion.FromAxisAngle(d.Rotation.Axis, Decay(d.Rotation)) * p.Rotation).Normalized(),
                    p.Scale + new AlsDoubleVector(d.Scale.Axis) * Decay(d.Scale));
            }
            for (var c = 0; c < outputCurves.Length; c++)
            {
                var d = _curveDiffs[c]; var incoming = outputCurves[c];
                outputCurves[c] = new((incoming.Present ? incoming.Value : 0) +
                    AlsInertialDecay.Evaluate(d.Delta, d.Speed, ElapsedSeconds, DurationSeconds), incoming.Present || d.Present);
            }
        }
        if (_historyCount > 0)
        {
            _fullCurrent.CopyTo(_fullPrevious, 0); _currentCurves.CopyTo(_previousCurves, 0);
            _fullPreviousComponent = _fullCurrentComponent; _previousParent = _currentParent;
        }
        output.CopyTo(_fullCurrent); outputCurves.CopyTo(_currentCurves);
        _fullCurrentComponent = component; _currentParent = attachParent;
        _historyDelta = _delta; _historyCount = ScalarMath.Min(_historyCount + 1, 2); _delta = 0;
    }

    private void InitializeFullDifference(ReadOnlySpan<AlsPrecisePose> input, ReadOnlySpan<AlsInertialCurve> curves,
        in AlsPrecisePose component, long parent)
    {
        var oldComponent = _historyCount == 1 ? _fullCurrentComponent : _fullPreviousComponent;
        var oldParent = _historyCount == 1 ? _currentParent : _previousParent;
        var inverse = component.Rotation.Conjugate(); var world = parent != _currentParent || parent != oldParent;
        var worldRotation = !world && parent == 0 && (ScalarMath.Abs((_fullCurrentComponent.Rotation * inverse).W) < .999f ||
            ScalarMath.Abs((oldComponent.Rotation * inverse).W) < .999f);
        for (var b = 0; b < input.Length; b++)
        {
            var latest = _fullCurrent[b]; var older = _historyCount == 1 ? latest : _fullPrevious[b];
            if (b == 0 && world)
            {
                latest = AlsPrecisePose.Compose(latest, AlsPrecisePose.Relative(_fullCurrentComponent, component));
                older = AlsPrecisePose.Compose(older, AlsPrecisePose.Relative(oldComponent, component));
            }
            else if (b == 0 && worldRotation)
            {
                latest = latest with { Rotation = inverse * _fullCurrentComponent.Rotation * latest.Rotation };
                older = older with { Rotation = inverse * oldComponent.Rotation * older.Rotation };
            }
            var q = latest.Rotation * input[b].Rotation.Conjugate();
            var length = q.X * q.X + q.Y * q.Y + q.Z * q.Z;
            var reciprocal = length >= 1e-8 ? 1 / ScalarMath.Sqrt(length) : 0;
            var axis = reciprocal > 0 ? new AlsDoubleVector(q.X * reciprocal, q.Y * reciprocal, q.Z * reciprocal) : new(_rotationFallbackAxis);
            var angle = Unwind((float)(2 * ScalarMath.Acos(ScalarMath.Clamp(q.W, -1, 1))));
            if (angle < 0) { angle = -angle; axis *= -1; }
            var speed = 0f;
            if (_historyDelta > .0001f && angle > .0001f)
            {
                var old = older.Rotation * input[b].Rotation.Conjugate();
                var oldAngle = 2 * ScalarMath.Atan2(axis.X * old.X + axis.Y * old.Y + axis.Z * old.Z, old.W);
                // UE GetTwistAngle<double> calls UnwindRadians<double>, whose
                // PI/TWO_PI macros are float constants promoted to double.
                while (oldAngle > MathF.PI) oldAngle -= (double)(2 * MathF.PI);
                while (oldAngle < -MathF.PI) oldAngle += (double)(2 * MathF.PI);
                speed = Unwind(angle - (float)oldAngle) / _historyDelta;
            }
            _bones[b] = new(FullVectorDifference((latest.Position - input[b].Position) * (1d / _unitsPerCentimeter),
                (older.Position - input[b].Position) * (1d / _unitsPerCentimeter)), new(axis.ToSingle(), angle, speed),
                FullVectorDifference(latest.Scale - input[b].Scale, older.Scale - input[b].Scale));
        }
        for (var c = 0; c < curves.Length; c++)
        {
            var incoming = curves[c]; var latest = _currentCurves[c]; var older = _historyCount == 1 ? latest : _previousCurves[c];
            var value = incoming.Present ? incoming.Value : 0;
            var delta = (latest.Present ? latest.Value : 0) - value;
            // Preserve the source engine's Delta - Value derivative expression.
            var speed = _historyDelta > .0001f ? (delta - value - (older.Present ? older.Value : 0)) / _historyDelta : 0;
            _curveDiffs[c] = new(delta, speed, incoming.Present || latest.Present || older.Present);
        }
    }
    private AlsInertialVector FullVectorDifference(AlsDoubleVector difference, AlsDoubleVector previous)
    {
        var magnitude = (float)ScalarMath.Sqrt(difference.LengthSquared);
        var axis = magnitude > .0001f ? difference * (1d / magnitude) : AlsDoubleVector.Zero;
        var oldMagnitude = (float)AlsDoubleVector.Dot(previous, axis);
        var speed = magnitude > .0001f && _historyDelta > .0001f ? (magnitude - oldMagnitude) / _historyDelta : 0;
        return new(axis.ToSingle(), magnitude, speed);
    }
    private static AlsDoubleVector World(in AlsPrecisePose component, AlsDoubleVector p) =>
        (component.Scale * p).Rotate(component.Rotation) + component.Position;
}
