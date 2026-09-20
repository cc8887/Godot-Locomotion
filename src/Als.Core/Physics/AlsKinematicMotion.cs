using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// One complete Position target step, corresponding to Chaos ApplyKinematicTargets
// with StepFraction=1. No substep interpolation or velocity-mode integration.
public static class AlsKinematicMotion
{
    public static AlsIslandBodyState PositionTarget(AlsPrecisePose previous, AlsPrecisePose target, double dt)
    {
        previous.Validate(1e-5); target.Validate(1e-5);
        if (previous.Scale != AlsDoubleVector.One || target.Scale != AlsDoubleVector.One ||
            !double.IsFinite(dt) || dt <= 0 || !float.IsFinite((float)dt)) throw new ArgumentException("Invalid kinematic target step.");
        var r0 = previous.Rotation.ToSingle(); var r1 = target.Rotation.ToSingle();
        var stored = target with { Rotation = new(r1) };
        if (dt <= (double)1e-6f) return new(stored, default);
        var delta = target.Position - previous.Position;
        var v = delta.NearlyZero(1e-8f) ? Vector3.Zero : (delta * (1 / dt)).ToSingle();
        if (Quaternion.Dot(r0, r1) < 0) r1 = -r1;
        var difference = r1 - r0; var w = Vector3.Zero;
        if (MathF.Abs(difference.X) > 1e-8f || MathF.Abs(difference.Y) > 1e-8f ||
            MathF.Abs(difference.Z) > 1e-8f || MathF.Abs(difference.W) > 1e-8f)
        {
            var step = (float)dt;
            var derivative = new Quaternion(difference.X / step, difference.Y / step, difference.Z / step, difference.W / step);
            var angular = (derivative * Quaternion.Conjugate(r0)) * 2;
            w = new(angular.X, angular.Y, angular.Z);
        }
        if (!new AlsDoubleVector(v).IsFinite || !new AlsDoubleVector(w).IsFinite) throw new ArgumentException("Kinematic velocity overflow.");
        return new(stored, new(v, w));
    }
}
