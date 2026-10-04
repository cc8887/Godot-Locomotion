namespace GodotAls.Core.Locomotion;

public readonly record struct AlsFloatSpringState(float Velocity, float PreviousTarget, bool PreviousValid);
public readonly record struct AlsFloatSpringResult(float Value, AlsFloatSpringState State);

// UKismetMathLibrary::FloatSpringInterp, without the optional output clamp.
// Accept stiffness directly: reconstructing it from a rig Strength introduces
// a second float square and changes the authored Kismet spring.
public static class AlsKismetFloatSpring
{
    public static AlsFloatSpringResult Evaluate(float current, float target, in AlsFloatSpringState state,
        float stiffness, float damping, float delta, float mass, float targetVelocityAmount,
        bool initializeFromTarget = false)
    {
        if (!float.IsFinite(current) || !float.IsFinite(target) || !float.IsFinite(state.Velocity) ||
            !float.IsFinite(state.PreviousTarget) || !float.IsFinite(stiffness) || stiffness < 0 ||
            !float.IsFinite(damping) || damping < 0 || !float.IsFinite(delta) || delta < 0 ||
            !float.IsFinite(mass) || mass < 0 || !float.IsFinite(targetVelocityAmount))
            throw new ArgumentException("Invalid Kismet spring input/history.");
        if (initializeFromTarget && !state.PreviousValid) current = target;
        var velocity = state.Velocity; var previous = state.PreviousTarget; var valid = state.PreviousValid;
        if (delta > 1e-8f && MathF.Abs(mass) > 1e-8f)
        {
            var targetVelocity = valid ? (target - previous) * (targetVelocityAmount / delta) : 0f;
            var omega = MathF.Sqrt(stiffness / mass);
            var frequency = omega / (2f * 3.14159265359f);
            AlsRefactoredSpring.Evaluate(ref current, ref velocity, target, targetVelocity, delta, frequency, damping, expandedInvExp: true);
            previous = target; valid = true;
        }
        return new(current, new(velocity, previous, valid));
    }
}
