namespace GodotAls.Core.Locomotion;

public readonly record struct AlsRefactoredCrouchingInput(bool MovingSmooth, bool RotatingLeft, bool RotatingRight);
public enum AlsRefactoredCrouchingRule { RotateLeft, RotateRight, Moving, StoppingFullyMoving, Stopping, StopFull, Automatic }

public static class AlsRefactoredCrouchingRules
{
    public static bool Evaluate(this AlsRefactoredCrouchingRule rule, in AlsRefactoredCrouchingInput input, ReadOnlySpan<float> recordedWeights)
    {
        if (recordedWeights.Length != 5 || !Enum.IsDefined(rule)) throw new ArgumentException("Invalid Crouching predicate domain.");
        foreach (var weight in recordedWeights)
            if (!float.IsFinite(weight) || weight is < 0 or > 1) throw new ArgumentException("Invalid Crouching state weight.");
        return rule switch
        {
            AlsRefactoredCrouchingRule.RotateLeft => input.RotatingLeft,
            AlsRefactoredCrouchingRule.RotateRight => input.RotatingRight,
            AlsRefactoredCrouchingRule.Moving => input.MovingSmooth,
            AlsRefactoredCrouchingRule.StoppingFullyMoving => !input.MovingSmooth && recordedWeights[1] >= 1,
            AlsRefactoredCrouchingRule.Stopping => !input.MovingSmooth,
            AlsRefactoredCrouchingRule.StopFull => recordedWeights[4] >= 1,
            _ => throw new ArgumentException("Automatic rules require the original asset-player observation.")
        };
    }
}
