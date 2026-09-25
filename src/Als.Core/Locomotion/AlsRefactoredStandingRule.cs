namespace GodotAls.Core.Locomotion;

public readonly record struct AlsRefactoredStandingInput(bool MovingSmooth, bool RotatingLeft, bool RotatingRight);
public enum AlsRefactoredStandingRule { RotateLeft, RotateRight, Moving, StoppingFullyMoving, Stopping, StopFull, Automatic }

public static class AlsRefactoredStandingRules
{
    public static bool Evaluate(this AlsRefactoredStandingRule rule, in AlsRefactoredStandingInput input, ReadOnlySpan<float> recordedWeights)
    {
        if (recordedWeights.Length != 5 || !Enum.IsDefined(rule)) throw new ArgumentException("Invalid Standing predicate domain.");
        foreach (var weight in recordedWeights) if (!float.IsFinite(weight) || weight is < 0 or > 1) throw new ArgumentException("Invalid Standing state weight.");
        return rule switch
        {
            AlsRefactoredStandingRule.RotateLeft => input.RotatingLeft,
            AlsRefactoredStandingRule.RotateRight => input.RotatingRight,
            AlsRefactoredStandingRule.Moving => input.MovingSmooth,
            AlsRefactoredStandingRule.StoppingFullyMoving => !input.MovingSmooth && recordedWeights[1] >= 1,
            AlsRefactoredStandingRule.Stopping => !input.MovingSmooth,
            AlsRefactoredStandingRule.StopFull => recordedWeights[2] >= 1,
            _ => throw new ArgumentException("Automatic rules require the original asset-player observation.")
        };
    }
}
