using M = System.Math;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsRigSpringInput(double DeltaTime, float Target, float Current, float Strength,
    float Force, float Damping, float TargetVelocityAmount, bool UseCurrent, bool InitializeFromTarget);
public readonly record struct AlsRigSpringState(float Result, float Velocity, float PreviousTarget, bool PreviousValid);

// FRigUnit_SpringInterpV2 + UKismetMathLibrary::FloatSpringInterp. This is not
// the initialization wrapper used by UAlsMath::SpringDamperFloat for the feet.
public static class AlsRigSpringModel
{
    public static AlsRigSpringState Evaluate(in AlsRigSpringState previous, in AlsRigSpringInput input)
    {
        if (!double.IsFinite(input.DeltaTime) || !float.IsFinite((float)input.DeltaTime) || input.DeltaTime < 0 ||
            !float.IsFinite(input.Target) || !float.IsFinite(input.Current) || !float.IsFinite(input.Strength) || input.Strength < 0 ||
            !float.IsFinite(input.Force) || !float.IsFinite(input.Damping) || input.Damping < 0 || !float.IsFinite(input.TargetVelocityAmount) ||
            !float.IsFinite(previous.Result) || !float.IsFinite(previous.Velocity) || !float.IsFinite(previous.PreviousTarget))
            throw new ArgumentException("Invalid rig spring input/history.");
        var angularFrequency = input.Strength * 2f * 3.14159265359f;
        var stiffness = angularFrequency * angularFrequency;
        var target = input.Target; var velocity = previous.Velocity;
        if (M.Abs(stiffness) > 1e-8f) target += input.Force / stiffness;
        else velocity = (float)(velocity + input.Force * input.DeltaTime);
        var result = input.UseCurrent ? input.Current : previous.Result;
        if ((!input.UseCurrent || input.InitializeFromTarget) && !previous.PreviousValid) result = target;
        var oldTarget = previous.PreviousTarget; var valid = previous.PreviousValid;
        var delta = (float)input.DeltaTime;
        if (delta > 1e-8f)
        {
            var targetVelocity = valid ? (target - oldTarget) * (input.TargetVelocityAmount / delta) : 0;
            var frequency = MathF.Sqrt(stiffness) / (2f * 3.14159265359f);
            AlsRefactoredSpring.Evaluate(ref result, ref velocity, target, targetVelocity, delta, frequency, input.Damping);
            oldTarget = target; valid = true;
        }
        return new(result, velocity, oldTarget, valid);
    }
}

public sealed record AlsPelvisRigDefinition(double Minimum, double Maximum, float Strength, float Damping);
public readonly record struct AlsPelvisRigResult(AlsRigSpringState Spring, float Offset, AlsDoubleVector Location);

public static class AlsPelvisRigModel
{
    public static AlsPelvisRigResult Evaluate(in AlsRigSpringState previous, AlsPelvisRigDefinition definition,
        double deltaTime, float leftOffset, float rightOffset, float amount, AlsDoubleVector pelvis)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!double.IsFinite(definition.Minimum) || !double.IsFinite(definition.Maximum) || definition.Minimum > definition.Maximum ||
            !float.IsFinite(leftOffset) || !float.IsFinite(rightOffset) || !float.IsFinite(amount) || !pelvis.IsFinite)
            throw new ArgumentException("Invalid pelvis rig input.");
        var target = (float)(M.Clamp((double)M.Min(leftOffset, rightOffset), definition.Minimum, definition.Maximum) * amount);
        var spring = AlsRigSpringModel.Evaluate(previous, new(deltaTime, target, 0, definition.Strength, 0, definition.Damping, 0, false, true));
        // Both double multiply nodes are authored. The second is not a duplicate
        // of the pre-spring weight: changing amount must affect old spring output.
        var offset = (float)((double)spring.Result * amount);
        return new(spring, offset, pelvis + new AlsDoubleVector(0, 0, offset));
    }
}
