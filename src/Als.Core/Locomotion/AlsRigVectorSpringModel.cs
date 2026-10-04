namespace GodotAls.Core.Locomotion;

public readonly record struct AlsRigVectorSpringState(AlsDoubleVector Result, AlsDoubleVector Velocity,
    AlsDoubleVector PreviousTarget, bool PreviousValid);

// FRigUnit_SpringInterpVectorV2's authored FootPlant path: current input,
// zero force, critical damping. FVector values stay double; coefficients are
// the float UKismetMathLibrary::VectorSpringInterp instantiation.
public static class AlsRigVectorSpringModel
{
    public static AlsRigVectorSpringState Evaluate(in AlsRigVectorSpringState previous,
        AlsDoubleVector current, AlsDoubleVector target, double deltaTime, float strength,
        float targetVelocityAmount, bool initializeFromTarget = false)
    {
        if (!current.IsFinite || !target.IsFinite || !previous.Result.IsFinite || !previous.Velocity.IsFinite ||
            !previous.PreviousTarget.IsFinite || !double.IsFinite(deltaTime) || deltaTime < 0 ||
            !float.IsFinite((float)deltaTime) || !float.IsFinite(strength) || strength <= 0 || !float.IsFinite(targetVelocityAmount))
            throw new ArgumentException("Invalid FootPlant vector spring input/history.");
        var velocity = previous.Velocity; var oldTarget = previous.PreviousTarget; var valid = previous.PreviousValid;
        if (initializeFromTarget && !valid) current = target;
        float delta = (float)deltaTime;
        if (delta > 1e-8f)
        {
            float angular = strength * 2f * MathF.PI, stiffness = angular * angular;
            float frequency = MathF.Sqrt(stiffness) / (2f * MathF.PI), w = frequency * 6.28318530718f;
            if (w < 1e-8f)
                return new(current + velocity * delta, velocity, target, true);
            var targetVelocity = valid ? (target - oldTarget) * (targetVelocityAmount / delta) : default;
            var adjusted = target + targetVelocity * (2f / w);
            var error = current - adjusted; var c2 = velocity + error * w;
            float x = w * delta;
            float e = 1f / (1f + 1.00746054f * x + .45053901f * x * x + .25724632f * x * x * x);
            current = adjusted + (error + c2 * delta) * e;
            velocity = (c2 - error * w - c2 * (w * delta)) * e;
            oldTarget = target; valid = true;
        }
        return new(current, velocity, oldTarget, valid);
    }
}
