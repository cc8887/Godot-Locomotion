namespace GodotAls.Core.Locomotion;

public enum AlsRefactoredLocomotionMode { Invalid, Grounded, InAir, Other }
public readonly record struct AlsRefactoredLocomotionInput(AlsRefactoredLocomotionMode Mode,
    AlsRefactoredGroundedStance Stance, bool Jumped, bool HasInput, bool RotatingLeft,
    bool RotatingRight, bool HasRelativeLocation, float Speed, float FootPlanted);

public enum AlsRefactoredLocomotionPredicate
{
    False, True, Grounded, InAir, Standing, Jumped, HasInput, RotateLeft, RotateRight,
    RelativeLocation, SpeedAtLeast, FootPositive, FootNonPositive, And, Or, Not,
}

/// <summary>Original Locomotion/Jump rule domain, independent of V4 main movement.
/// Values are frozen Parent inputs; evaluating a predicate has no side effects.</summary>
public sealed class AlsRefactoredLocomotionRule
{
    public AlsRefactoredLocomotionPredicate Kind { get; }
    public AlsRefactoredLocomotionRule? A { get; }
    public AlsRefactoredLocomotionRule? B { get; }
    public float Threshold { get; }
    public string Signature { get; }
    public AlsRefactoredLocomotionRule(AlsRefactoredLocomotionPredicate kind,
        AlsRefactoredLocomotionRule? a = null, AlsRefactoredLocomotionRule? b = null, float threshold = 0)
    {
        var binary = kind is AlsRefactoredLocomotionPredicate.And or AlsRefactoredLocomotionPredicate.Or;
        if (!Enum.IsDefined(kind) || (a is not null) != (binary || kind == AlsRefactoredLocomotionPredicate.Not) ||
            (b is not null) != binary || !float.IsFinite(threshold) ||
            kind != AlsRefactoredLocomotionPredicate.SpeedAtLeast && threshold != 0)
            throw new ArgumentException("Invalid Locomotion predicate.");
        Kind = kind; A = a; B = b; Threshold = threshold;
        Signature = binary ? $"{kind}({a!.Signature},{b!.Signature})" : a is not null ? $"{kind}({a.Signature})" :
            kind == AlsRefactoredLocomotionPredicate.SpeedAtLeast
                ? kind + "(" + threshold.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ")" : kind.ToString();
    }
    public bool Evaluate(in AlsRefactoredLocomotionInput input) => Kind switch
    {
        AlsRefactoredLocomotionPredicate.False => false,
        AlsRefactoredLocomotionPredicate.True => true,
        AlsRefactoredLocomotionPredicate.Grounded => input.Mode == AlsRefactoredLocomotionMode.Grounded,
        AlsRefactoredLocomotionPredicate.InAir => input.Mode == AlsRefactoredLocomotionMode.InAir,
        AlsRefactoredLocomotionPredicate.Standing => input.Stance == AlsRefactoredGroundedStance.Standing,
        AlsRefactoredLocomotionPredicate.Jumped => input.Jumped,
        AlsRefactoredLocomotionPredicate.HasInput => input.HasInput,
        AlsRefactoredLocomotionPredicate.RotateLeft => input.RotatingLeft,
        AlsRefactoredLocomotionPredicate.RotateRight => input.RotatingRight,
        AlsRefactoredLocomotionPredicate.RelativeLocation => input.HasRelativeLocation,
        AlsRefactoredLocomotionPredicate.SpeedAtLeast => input.Speed >= Threshold,
        AlsRefactoredLocomotionPredicate.FootPositive => input.FootPlanted > 0,
        AlsRefactoredLocomotionPredicate.FootNonPositive => input.FootPlanted <= 0,
        AlsRefactoredLocomotionPredicate.And => A!.Evaluate(input) && B!.Evaluate(input),
        AlsRefactoredLocomotionPredicate.Or => A!.Evaluate(input) || B!.Evaluate(input),
        AlsRefactoredLocomotionPredicate.Not => !A!.Evaluate(input),
        _ => throw new InvalidOperationException("Unknown Locomotion predicate.")
    };
}
