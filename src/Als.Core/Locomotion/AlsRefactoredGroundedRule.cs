namespace GodotAls.Core.Locomotion;

// Keep invalid and unrecognized valid tags distinct: the original Stand rule
// accepts an empty tag, but does not accept an arbitrary other valid stance.
public enum AlsRefactoredGroundedStance { Invalid, Standing, Crouching, Other }
public readonly record struct AlsRefactoredGroundedInput(AlsRefactoredGroundedStance Stance,
    bool MovingSmooth, bool RotatingLeft, bool RotatingRight, bool FromRoll,
    float StandingAmount, float CrouchingAmount);

public enum AlsRefactoredGroundedPredicate
{
    False, True, Moving, RotateLeft, RotateRight, Standing, Crouching, ValidStance,
    FromRoll, StandingAmountAtLeast, CrouchingAmountAtLeast, And, Or, Not,
}

/// <summary>Immutable, typed expression compiled from the original Grounded
/// predicate graph. No UObject, dynamic evaluation or allocation during updates.</summary>
public sealed class AlsRefactoredGroundedRule
{
    public AlsRefactoredGroundedPredicate Kind { get; }
    public AlsRefactoredGroundedRule? A { get; }
    public AlsRefactoredGroundedRule? B { get; }
    public float Threshold { get; }
    public string Signature { get; }
    public AlsRefactoredGroundedRule(AlsRefactoredGroundedPredicate kind,
        AlsRefactoredGroundedRule? a = null, AlsRefactoredGroundedRule? b = null, float threshold = 0)
    {
        var binary = kind is AlsRefactoredGroundedPredicate.And or AlsRefactoredGroundedPredicate.Or;
        if (!Enum.IsDefined(kind) || (a is not null) != (binary || kind == AlsRefactoredGroundedPredicate.Not) ||
            (b is not null) != binary || !float.IsFinite(threshold) ||
            kind is not (AlsRefactoredGroundedPredicate.StandingAmountAtLeast or AlsRefactoredGroundedPredicate.CrouchingAmountAtLeast) && threshold != 0)
            throw new ArgumentException("Invalid Grounded predicate expression.");
        Kind = kind; A = a; B = b; Threshold = threshold;
        Signature = binary ? $"{kind}({a!.Signature},{b!.Signature})" : a is not null ? $"{kind}({a.Signature})" :
            kind is AlsRefactoredGroundedPredicate.StandingAmountAtLeast or AlsRefactoredGroundedPredicate.CrouchingAmountAtLeast
                ? kind + "(" + threshold.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ")" : kind.ToString();
    }
    public bool Evaluate(in AlsRefactoredGroundedInput input) => Kind switch
    {
        AlsRefactoredGroundedPredicate.False => false,
        AlsRefactoredGroundedPredicate.True => true,
        AlsRefactoredGroundedPredicate.Moving => input.MovingSmooth,
        AlsRefactoredGroundedPredicate.RotateLeft => input.RotatingLeft,
        AlsRefactoredGroundedPredicate.RotateRight => input.RotatingRight,
        AlsRefactoredGroundedPredicate.Standing => input.Stance == AlsRefactoredGroundedStance.Standing,
        AlsRefactoredGroundedPredicate.Crouching => input.Stance == AlsRefactoredGroundedStance.Crouching,
        AlsRefactoredGroundedPredicate.ValidStance => input.Stance != AlsRefactoredGroundedStance.Invalid,
        AlsRefactoredGroundedPredicate.FromRoll => input.FromRoll,
        AlsRefactoredGroundedPredicate.StandingAmountAtLeast => input.StandingAmount >= Threshold,
        AlsRefactoredGroundedPredicate.CrouchingAmountAtLeast => input.CrouchingAmount >= Threshold,
        AlsRefactoredGroundedPredicate.And => A!.Evaluate(input) && B!.Evaluate(input),
        AlsRefactoredGroundedPredicate.Or => A!.Evaluate(input) || B!.Evaluate(input),
        AlsRefactoredGroundedPredicate.Not => !A!.Evaluate(input),
        _ => throw new InvalidOperationException("Unknown Grounded predicate.")
    };
}
