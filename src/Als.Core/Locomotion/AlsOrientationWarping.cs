using System.Numerics;
using ScalarMath = System.Math;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsOrientationWarpingSettings(float MinSpeed, float AngleThreshold,
    float Distribution, float InterpSpeed, float CounterInterpSpeed, float MaxCorrectionDegrees,
    float MaxCounterDegrees, bool CounterCompensate, bool ScaleGlobalWeight);
public readonly record struct AlsOrientationWarpingState(bool First, float Angle, AlsDoubleVector Direction,
    AlsQuaternion RootRotation, float CounterTarget, long UpdateCounter)
{
    public static AlsOrientationWarpingState Initial => new(true, 0, default, AlsQuaternion.Identity, 0, -1);
    // Native Reset deliberately keeps RootRotation and CounterTarget.
    public AlsOrientationWarpingState Reset() => this with { First = true, Angle = 0, Direction = default };
    // Native UpdateInternal runs even when this frame skips pose evaluation.
    public AlsOrientationWarpingState PrepareUpdate(long counter)
    {
        if(counter<0)throw new ArgumentOutOfRangeException(nameof(counter));
        var state=this;
        if(!state.First && state.UpdateCounter>=0 && state.UpdateCounter!=counter && state.UpdateCounter!=counter-1)state=state.Reset();
        return state with{UpdateCounter=counter};
    }
}
public readonly record struct AlsOrientationWarpingInput(float Delta, float LocomotionAngle,
    AlsDoubleVector LocomotionDirection, AlsPrecisePose Component, AlsQuaternion ComponentRelativeRotation,
    float Alpha, float Weight, long UpdateCounter, bool Reinitialize = false,
    bool HasPrediction = false, AlsDoubleVector Prediction = default);
public readonly record struct AlsOrientationWarpingOutput(AlsOrientationWarpingState State,
    bool RootPresent, AlsPrecisePose RootMotion);

/// <summary>Original Graph/Z/ComponentTransform OrientationWarping evaluation.
/// Caller owns candidate history, source delta, pose and the root attribute.</summary>
public sealed class AlsOrientationWarping
{
    private readonly AlsOrientationWarpingSettings _settings;
    private readonly AlsComponentPose _pose;
    private readonly int[] _spines, _feet;
    private readonly float[] _spineWeights;
    private readonly int _footRoot, _count;
    public ReadOnlySpan<float> SpineWeights => _spineWeights;

    public AlsOrientationWarping(ReadOnlySpan<int> parents, ReadOnlySpan<int> spineBones, int footRoot,
        ReadOnlySpan<int> feet, AlsOrientationWarpingSettings settings)
    {
        var count = parents.Length;
        if (parents.IsEmpty || spineBones.IsEmpty || feet.IsEmpty || (uint)footRoot >= parents.Length ||
            spineBones.ToArray().Distinct().Count() != spineBones.Length || feet.ToArray().Distinct().Count() != feet.Length ||
            spineBones.ToArray().Any(b => (uint)b >= count) || feet.ToArray().Any(b => (uint)b >= count))
            throw new ArgumentException("Invalid OrientationWarping bone layout.");
        for (var bone = 0; bone < parents.Length; bone++) if (parents[bone] < -1 || parents[bone] >= bone)
            throw new ArgumentException("OrientationWarping needs a parent-first hierarchy.");
        if (!float.IsFinite(settings.Distribution) || settings.Distribution is < 0 or > 1 ||
            !float.IsFinite(settings.MinSpeed) || settings.MinSpeed < 0 || !float.IsFinite(settings.AngleThreshold) ||
            !float.IsFinite(settings.InterpSpeed) || !float.IsFinite(settings.CounterInterpSpeed) ||
            !float.IsFinite(settings.MaxCorrectionDegrees) || !float.IsFinite(settings.MaxCounterDegrees))
            throw new ArgumentException("Invalid OrientationWarping settings.");
        _settings = settings; _count = parents.Length; _pose = new(parents); _footRoot = footRoot;
        _spines = spineBones.ToArray(); Array.Sort(_spines); _feet = feet.ToArray(); _spineWeights = new float[_spines.Length];
        for (var index = _spines.Length - 1; index >= 0; index--)
        {
            if (_spineWeights[index] != 0) continue;
            var indices = new List<int> { index }; var existing = 0f;
            for (var ancestor = index - 1; ancestor >= 0; ancestor--)
            {
                var bone = parents[_spines[index]];
                while (bone >= 0 && bone != _spines[ancestor]) bone = parents[bone];
                if (bone < 0) continue;
                if (_spineWeights[ancestor] > 0) existing += _spineWeights[ancestor]; else indices.Add(ancestor);
            }
            var weight = (1f - existing) / indices.Count;
            foreach (var id in indices) _spineWeights[id] = weight;
        }
    }
    public AlsOrientationWarpingOutput Evaluate(in AlsOrientationWarpingState committed, in AlsOrientationWarpingInput input,
        ReadOnlySpan<AlsPrecisePose> pose, bool rootPresent, in AlsPrecisePose rootMotion, Span<AlsPrecisePose> output)
    {
        if (pose.Length != _count || output.Length != _count || pose.Overlaps(output))
            throw new ArgumentException("Invalid OrientationWarping pose layout.");
        _pose.Begin(pose);
        var result = EvaluateInto(committed, input, _pose, rootPresent, rootMotion);
        _pose.Export(output);
        return result;
    }
    internal AlsOrientationWarpingOutput EvaluateInto(in AlsOrientationWarpingState committed,
        in AlsOrientationWarpingInput input, AlsComponentPose component, bool rootPresent, in AlsPrecisePose rootMotion)
    {
        if (!float.IsFinite(input.Delta) || input.Delta < 0 || !float.IsFinite(input.LocomotionAngle) ||
            !float.IsFinite(input.Alpha) || input.Alpha is < 0 or > 1 || !float.IsFinite(input.Weight) || input.Weight < 0 ||
            !input.LocomotionDirection.IsFinite || input.UpdateCounter < 0 || !input.Prediction.IsFinite)
            throw new ArgumentException("Invalid OrientationWarping candidate input.");
        input.Component.Validate();
        var state = input.Reinitialize ? committed.Reset() : committed;
        if (input.Alpha <= 1e-5f) return new(state, rootPresent, rootMotion);
        state = state.PrepareUpdate(input.UpdateCounter);
        if (!rootPresent) return new(state, false, rootMotion);
        rootMotion.Validate();
        var forward = input.LocomotionDirection.LengthSquared > 1e-8f ?
            Normal(InverseVector(input.LocomotionDirection, input.Component)) :
            Normal(new AlsDoubleVector(1, 0, 0).Rotate(ZRotation(ToRadians(NormalizeDegrees(input.LocomotionAngle)))).Rotate(input.ComponentRelativeRotation.Conjugate()));
        forward = Normal(forward with { Z = 0 });
        var translation = rootMotion.Position with { Z = 0 };
        var oldRotation = state.RootRotation; state = state with { RootRotation = rootMotion.Rotation };
        var speed = (float)(ScalarMath.Sqrt(translation.LengthSquared) / input.Delta); float target;
        var changedRoot = rootMotion;
        if (speed < _settings.MinSpeed) target = 0;
        else
        {
            var oldDirection = state.Direction;
            var direction = Normal(translation, oldDirection);
            target = SignedAngle(direction, forward);
            if (_settings.AngleThreshold > 0 && MathF.Abs(ToDegrees(target)) > _settings.AngleThreshold)
            { target = Unwind(target + ToRadians(180)); direction *= -1; }
            state = state with { Direction = direction };
            if (input.HasPrediction && !NearlyZero(input.Prediction with { Z = 0 }, 1e-8f))
            {
                var prediction = SignedAngle(Normal(input.Prediction with { Z = 0 }), forward);
                if (MathF.Abs(prediction) + 1e-4f < MathF.Abs(target)) target = prediction;
            }
            if (_settings.CounterCompensate && !NearlyZero(oldDirection, 1e-8f))
            {
                var correction = SignedAngle(direction, oldDirection.Rotate(oldRotation.Conjugate()));
                if (MathF.Abs(correction) < ToRadians(_settings.MaxCounterDegrees))
                {
                    var counter = state.CounterTarget + correction;
                    var change = Interp(0, counter, input.Delta, _settings.CounterInterpSpeed);
                    state = state with { Angle = Unwind(state.Angle + change), CounterTarget = counter - change };
                }
            }
            changedRoot = rootMotion with { Position = translation.Rotate(ZRotation(target)) };
        }
        var limit = ToRadians(_settings.MaxCorrectionDegrees);
        var angle = _settings.InterpSpeed > 0 && !state.First ?
            ScalarMath.Clamp(Interp(state.Angle, target, input.Delta, _settings.InterpSpeed), state.Angle - limit, state.Angle + limit) : target;
        angle = ScalarMath.Clamp(angle, -limit, limit); angle *= input.Alpha;
        if (_settings.ScaleGlobalWeight) angle *= input.Weight;
        if (!float.IsFinite(angle)) angle = 0;
        var rootOffset = Unwind(angle * _settings.Distribution);
        if (MathF.Abs(rootOffset) > 1e-4f) Rotate(component, 0, rootOffset);
        if (MathF.Abs(_settings.Distribution) > 1e-4f)
            for (var index = 0; index < _spines.Length; index++) Rotate(component, _spines[index], -angle * _settings.Distribution * _spineWeights[index]);
        var footAlpha = 1f - _settings.Distribution;
        if (MathF.Abs(footAlpha) > 1e-4f)
        {
            Rotate(component, _footRoot, angle * footAlpha);
            foreach (var foot in _feet) Rotate(component, foot, -angle * footAlpha);
        }
        return new(state with { Angle = angle, First = false }, true, changedRoot);
    }
    private static void Rotate(AlsComponentPose component, int bone, float angle)
    {
        var pose = component.Component(bone);
        component.SetComponent(bone, pose with { Rotation = (ZRotation(angle) * pose.Rotation).Normalized() });
    }
    private static AlsDoubleVector InverseVector(AlsDoubleVector vector, AlsPrecisePose transform)
    {
        vector = vector.Rotate(transform.Rotation.Conjugate());
        return vector * new AlsDoubleVector(Reciprocal(transform.Scale.X), Reciprocal(transform.Scale.Y), Reciprocal(transform.Scale.Z));
    }
    private static double Reciprocal(double value) => ScalarMath.Abs(value) <= 1e-8f ? 0 : 1d / value;
    private static bool NearlyZero(AlsDoubleVector value, double tolerance) => ScalarMath.Abs(value.X) <= tolerance && ScalarMath.Abs(value.Y) <= tolerance && ScalarMath.Abs(value.Z) <= tolerance;
    private static AlsDoubleVector Normal(AlsDoubleVector value, AlsDoubleVector fallback = default)
    {
        var length = value.LengthSquared;
        return length == 1 ? value : length < 1e-8f ? fallback : value * (1d / ScalarMath.Sqrt(length));
    }
    private static AlsQuaternion ZRotation(float radians) => AlsQuaternion.FromAxisAngle(Vector3.UnitZ, radians);
    private static float SignedAngle(AlsDoubleVector from, AlsDoubleVector to)
    {
        var angle = MathF.Acos(ScalarMath.Clamp((float)AlsDoubleVector.Dot(from, to), -1f, 1f));
        var dot = (float)AlsDoubleVector.Cross(from, to).Z;
        return dot >= 0 ? angle : -angle;
    }
    private static float ToRadians(float degrees) => degrees * (MathF.PI / 180f);
    private static float ToDegrees(float radians) => radians * (180f / MathF.PI);
    private static float NormalizeDegrees(float degrees)
    {
        // Native calls FRotator (double) NormalizeAxis from the float pin, then
        // stores back to float. Keeping the +/-360 arithmetic in float loses
        // fractional negative angles before the controller even evaluates.
        var value = (double)degrees % 360d;
        if (value < 0) value += 360d;
        return (float)(value > 180d ? value - 360d : value);
    }
    private static float Unwind(float angle)
    { while (angle > MathF.PI) angle -= 2 * MathF.PI; while (angle < -MathF.PI) angle += 2 * MathF.PI; return angle; }
    private static float Interp(float current, float target, float delta, float speed)
    {
        if (speed <= 0) return target;
        var distance = target - current;
        if (distance * distance < 1e-8f) return target;
        return current + distance * ScalarMath.Clamp(delta * speed, 0, 1);
    }
}
