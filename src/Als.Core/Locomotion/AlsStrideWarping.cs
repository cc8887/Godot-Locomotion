using ScalarMath = System.Math;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsStrideFoot(int Ik, int Fk, int Thigh);
public readonly record struct AlsStrideWarpingSettings(float MinSpeed, float ClampMin, float ClampMax,
    float InterpIncreasing, float InterpDecreasing, float SpringStiffness, float SpringDamping,
    float SpringRate, int SpringIterations, double PelvisAlpha, double PelvisMaxDistance,
    double PelvisTolerance, int PelvisIterations);
public readonly record struct AlsStrideSpringState(bool Initialized, bool Motion, float Remaining,
    AlsDoubleVector Position, AlsDoubleVector Velocity, AlsDoubleVector LastPosition);
public readonly record struct AlsStrideWarpingState(bool ModifierInitialized, float ModifierResult,
    AlsDoubleVector Direction, float Scale, AlsStrideSpringState Spring)
{
    public static AlsStrideWarpingState Initial => new(false, 0, new(1, 0, 0), 1, default);
    // Native Initialize resets the scale modifier and direction, not the pelvis spring.
    public AlsStrideWarpingState Reinitialize() => this with { ModifierInitialized = false, Direction = new(1, 0, 0), Scale = 1 };
}
public readonly record struct AlsStrideWarpingInput(float Delta, float Speed, float Alpha,
    AlsPrecisePose Component, bool Reinitialize = false);
public readonly record struct AlsStrideWarpingOutput(AlsStrideWarpingState State, bool RootPresent, AlsPrecisePose RootMotion);

/// <summary>Original Graph stride controller with world up/down and the original
/// clamp/interpolation, pelvis solver, FK thigh compensation and IK limits.</summary>
public sealed class AlsStrideWarping
{
    private readonly AlsStrideWarpingSettings _settings;
    private readonly AlsStrideFoot[] _feet;
    private readonly int _pelvis, _footRoot, _count;
    private readonly AlsComponentPose _pose;
    public AlsStrideWarping(ReadOnlySpan<int> parents, int pelvis, int footRoot,
        ReadOnlySpan<AlsStrideFoot> feet, AlsStrideWarpingSettings settings)
    {
        var count = parents.Length;
        if (count == 0 || (uint)pelvis >= count || (uint)footRoot >= count || feet.IsEmpty ||
            feet.ToArray().Any(f => (uint)f.Ik >= count || (uint)f.Fk >= count || (uint)f.Thigh >= count) ||
            settings.ClampMin > settings.ClampMax || settings.PelvisIterations < 0 || settings.SpringIterations < 0 ||
            !float.IsFinite(settings.SpringRate) || settings.SpringRate < 1)
            throw new ArgumentException("Invalid StrideWarping configuration.");
        for (var i = 0; i < count; i++) if (parents[i] < -1 || parents[i] >= i)
            throw new ArgumentException("StrideWarping requires parent-first bones.");
        _feet = feet.ToArray(); _settings = settings; _count = count; _pelvis = pelvis; _footRoot = footRoot; _pose = new(parents);
    }
    public AlsStrideWarpingOutput Evaluate(in AlsStrideWarpingState committed, in AlsStrideWarpingInput input,
        ReadOnlySpan<AlsPrecisePose> pose, bool rootPresent, in AlsPrecisePose rootMotion, Span<AlsPrecisePose> output)
    {
        if (pose.Length != _count || output.Length != _count || pose.Overlaps(output)) throw new ArgumentException("Wrong Stride pose layout.");
        _pose.Begin(pose); var result = EvaluateInto(committed, input, _pose, rootPresent, rootMotion); _pose.Export(output); return result;
    }
    internal AlsStrideWarpingOutput EvaluateInto(in AlsStrideWarpingState committed, in AlsStrideWarpingInput input,
        AlsComponentPose pose, bool rootPresent, in AlsPrecisePose rootMotion)
    {
        if (!float.IsFinite(input.Delta) || input.Delta < 0 || !float.IsFinite(input.Speed) || input.Speed < 0 ||
            !float.IsFinite(input.Alpha) || input.Alpha is < 0 or > 1) throw new ArgumentException("Invalid Stride inputs.");
        input.Component.Validate();
        var state = input.Reinitialize ? committed.Reinitialize() : committed;
        if (input.Alpha <= 1e-5f) return new(state, rootPresent, rootMotion);
        var previousDirection = state.Direction;
        state = state with { Direction = new(1, 0, 0), Scale = 1 };
        if (!rootPresent) return new(state, false, rootMotion);
        rootMotion.Validate();
        var direction = Normal(rootMotion.Position, previousDirection);
        _ = pose.Component(_footRoot);
        var floor = Normal(new AlsDoubleVector(0, 0, 1).Rotate(input.Component.Rotation.Conjugate()));
        var gravity = Normal(new AlsDoubleVector(0, 0, -1).Rotate(input.Component.Rotation.Conjugate()));
        direction = AlsDoubleVector.Cross(AlsDoubleVector.Cross(floor, direction), floor);
        var feet = new AlsPrecisePose[_feet.Length];
        for (var i = 0; i < _feet.Length; i++) feet[i] = pose.Component(_feet[i].Ik);
        var speed = MathF.Abs(input.Delta) > 1e-8f ? (float)(ScalarMath.Sqrt(rootMotion.Position.LengthSquared) / input.Delta) : 0;
        var scale = speed <= _settings.MinSpeed || MathF.Abs(speed) <= 1e-8f ? 1 : input.Speed / speed;
        scale = ScalarMath.Clamp(scale, _settings.ClampMin, _settings.ClampMax);
        if (state.ModifierInitialized)
        {
            var rate = scale >= state.ModifierResult ? _settings.InterpIncreasing : _settings.InterpDecreasing;
            scale = Interp(state.ModifierResult, scale, input.Delta, rate);
        }
        state = state with { Direction = direction, Scale = scale, ModifierResult = scale, ModifierInitialized = true };
        var changedRoot = rootMotion with { Position = rootMotion.Position * scale };
        for (var i = 0; i < _feet.Length; i++)
        {
            var location = feet[i].Position; var thigh = pose.Component(_feet[i].Thigh).Position;
            var denominator = AlsDoubleVector.Dot(gravity, floor);
            var origin = ScalarMath.Abs(denominator) > 1e-5f ? thigh + gravity * (AlsDoubleVector.Dot(location - thigh, floor) / denominator) : location;
            var scaleOrigin = location - direction * AlsDoubleVector.Dot(location - origin, direction);
            feet[i] = feet[i] with { Position = scaleOrigin + (location - scaleOrigin) * scale };
        }
        var pelvis = pose.Component(_pelvis); var initial = pelvis.Position;
        var lengths = new float[_feet.Length]; var targets = new AlsDoubleVector[_feet.Length];
        for (var i = 0; i < _feet.Length; i++)
        { lengths[i] = (float)ScalarMath.Sqrt((pose.Component(_feet[i].Fk).Position - initial).LengthSquared); targets[i] = feet[i].Position; }
        var adjusted = initial; var adjustment = AlsDoubleVector.Zero;
        for (var iteration = 0; iteration < _settings.PelvisIterations; iteration++)
        {
            var previous = adjusted; adjusted = default;
            for (var i = 0; i < _feet.Length; i++) adjusted += (targets[i] + Normal(previous - targets[i]) * lengths[i]) * (1d / _feet.Length);
            var old = adjustment; adjustment = adjusted - initial;
            if (ScalarMath.Sqrt((old - adjustment).LengthSquared) <= _settings.PelvisTolerance) break;
        }
        var spring = Spring(state.Spring, adjustment, input.Delta);
        adjusted = initial + spring.Position * _settings.PelvisAlpha;
        if ((adjusted - initial).LengthSquared >= _settings.PelvisMaxDistance * _settings.PelvisMaxDistance)
            adjusted = initial + Normal(adjusted - initial) * _settings.PelvisMaxDistance;
        pelvis = pelvis with { Position = adjusted }; var offset = adjusted - initial;
        var changes = new SortedDictionary<int, AlsPrecisePose> { [_pelvis] = pelvis };
        for (var i = 0; i < _feet.Length; i++)
        {
            var thigh = pose.Component(_feet[i].Thigh); var fk = pose.Component(_feet[i].Fk);
            var newThigh = thigh with { Position = thigh.Position + offset };
            var from = Normal(fk.Position - thigh.Position); var to = Normal(feet[i].Position - newThigh.Position);
            newThigh = newThigh with { Rotation = Between(from, to) * thigh.Rotation };
            changes.Add(_feet[i].Thigh, newThigh);
            var fkLength = (float)ScalarMath.Sqrt((fk.Position - thigh.Position).LengthSquared);
            var ikLength = (float)ScalarMath.Sqrt((feet[i].Position - newThigh.Position).LengthSquared);
            if (ikLength > fkLength) feet[i] = feet[i] with { Position = newThigh.Position + to * fkLength };
            changes.Add(_feet[i].Ik, feet[i]);
        }
        pose.Apply(changes.Keys.ToArray(), changes.Values.ToArray(), input.Alpha);
        return new(state with { Spring = spring }, true, changedRoot);
    }
    private AlsStrideSpringState Spring(AlsStrideSpringState state, AlsDoubleVector target, float delta)
    {
        if (delta <= 0) return state;
        state = state with { LastPosition = state.Position, Motion = state.Motion || !(state.Position - target).NearlyZero(.001f) };
        if (!state.Motion) return state;
        state = state with { Initialized = true, Remaining = MathF.Min(state.Remaining + delta, .1f) };
        var step = 1f / _settings.SpringRate; var iterations = ScalarMath.Min((int)(state.Remaining / step), _settings.SpringIterations);
        state = state with { Remaining = state.Remaining - iterations * step };
        for (var i = 0; i < iterations; i++) state = Integrate(state, target, step);
        if (state.Remaining > 0 && state.Remaining < step)
        { state = Integrate(state, target, state.Remaining); state = state with { Remaining = 0 }; }
        if (((state.Position - target).NearlyZero(.001f) && state.Velocity.NearlyZero(.01f)) ||
            !Valid(state.Position) || !Valid(state.Velocity))
            state = state with { Position = target, Velocity = default, Motion = false, Remaining = 0 };
        return state;
    }
    private AlsStrideSpringState Integrate(AlsStrideSpringState state, AlsDoubleVector target, float delta)
    {
        var damping = _settings.SpringDamping * 2f * MathF.Sqrt(_settings.SpringStiffness);
        (AlsDoubleVector V, AlsDoubleVector A) Step(AlsDoubleVector v, AlsDoubleVector a, float dt)
        {
            var p = state.Position + v * dt; var velocity = state.Velocity + a * dt;
            return (velocity, (target - p) * _settings.SpringStiffness - velocity * damping);
        }
        var a = Step(default, default, 0); var b = Step(a.V, a.A, delta * .5f);
        var c = Step(b.V, b.A, delta * .5f); var d = Step(c.V, c.A, delta);
        var velocity = (a.V + (b.V + c.V) * 2f + d.V) * (1f / 6f);
        var acceleration = (a.A + (b.A + c.A) * 2f + d.A) * (1f / 6f);
        return state with { Position = state.Position + velocity * delta, Velocity = state.Velocity + acceleration * delta };
    }
    private static bool Valid(AlsDoubleVector v) => ScalarMath.Max(ScalarMath.Max(ScalarMath.Abs(v.X), ScalarMath.Abs(v.Y)), ScalarMath.Abs(v.Z)) < 1e16f;
    private static AlsDoubleVector Normal(AlsDoubleVector v, AlsDoubleVector fallback = default)
        => v.LengthSquared == 1 ? v : v.LengthSquared < 1e-8f ? fallback : v * (1d / ScalarMath.Sqrt(v.LengthSquared));
    private static AlsQuaternion Between(AlsDoubleVector from, AlsDoubleVector to)
    {
        var w = 1 + AlsDoubleVector.Dot(from, to); var xyz = w >= 1e-6f ? AlsDoubleVector.Cross(from, to) :
            AlsDoubleVector.Cross(from, ScalarMath.Abs(from.X) > ScalarMath.Abs(from.Y) && ScalarMath.Abs(from.X) > ScalarMath.Abs(from.Z) ? new(0, 1, 0) : new(-1, 0, 0));
        return new AlsQuaternion(xyz.X, xyz.Y, xyz.Z, w >= 1e-6f ? w : 0).Normalized();
    }
    private static float Interp(float current, float target, float delta, float speed)
    {
        if (speed <= 0) return target; var distance = target - current;
        return distance * distance < 1e-8f ? target : current + distance * ScalarMath.Clamp(delta * speed, 0, 1);
    }
}

public readonly record struct AlsCycleWarpingOutput(AlsOrientationWarpingState Orientation,
    AlsStrideWarpingState Stride, bool RootPresent, AlsPrecisePose RootMotion);

/// <summary>One FCSPose across the original Orientation -> Stride chain.</summary>
public sealed class AlsCycleWarping(ReadOnlySpan<int> parents, AlsOrientationWarping orientation, AlsStrideWarping stride)
{
    private readonly AlsComponentPose _pose = new(parents);
    private readonly int _count = parents.Length;
    public AlsCycleWarpingOutput Evaluate(in AlsOrientationWarpingState orientationState, in AlsStrideWarpingState strideState,
        in AlsOrientationWarpingInput orientationInput, in AlsStrideWarpingInput strideInput,
        ReadOnlySpan<AlsPrecisePose> pose, bool rootPresent, in AlsPrecisePose rootMotion, Span<AlsPrecisePose> output)
    {
        if (pose.Length != _count || output.Length != _count || pose.Overlaps(output)) throw new ArgumentException("Wrong Warp chain layout.");
        if (orientationInput.Delta != strideInput.Delta || orientationInput.Component != strideInput.Component ||
            orientationInput.Reinitialize != strideInput.Reinitialize) throw new ArgumentException("Warp nodes must share one update context.");
        _pose.Begin(pose);
        var one = orientation.EvaluateInto(orientationState, orientationInput, _pose, rootPresent, rootMotion);
        var two = stride.EvaluateInto(strideState, strideInput, _pose, one.RootPresent, one.RootMotion);
        _pose.Export(output); return new(one.State, two.State, two.RootPresent, two.RootMotion);
    }
}
