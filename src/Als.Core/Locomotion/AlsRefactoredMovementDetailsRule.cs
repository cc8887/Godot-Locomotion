namespace GodotAls.Core.Locomotion;

public enum AlsRefactoredMovementDetailsRule
{
    StartWhileEntering, StartFromWalk, RunWhileNotGrounded, Pivot, Walk, AutomaticRemainingTime
}

public readonly record struct AlsRefactoredMovementDetailsInput(
    string Gait, float GroundedAmount, float UnweightedGaitRunningAmount,
    float StandingMachineWeight, bool PivotActive);

public static class AlsRefactoredMovementDetailsRules
{
    // The automatic rules require the machine's most relevant asset player;
    // they cannot be replaced by elapsed state time or a boolean predicate.
    public static bool Evaluate(this AlsRefactoredMovementDetailsRule rule, in AlsRefactoredMovementDetailsInput input)
    {
        if (!float.IsFinite(input.GroundedAmount) || !float.IsFinite(input.UnweightedGaitRunningAmount) || !float.IsFinite(input.StandingMachineWeight))
            throw new ArgumentException("Movement details input must be finite.", nameof(input));
        var running = input.Gait is "Als.Gait.Running" or "Als.Gait.Sprinting";
        return rule switch
        {
            AlsRefactoredMovementDetailsRule.StartWhileEntering => running && input.GroundedAmount >= 1 && input.StandingMachineWeight < 1,
            AlsRefactoredMovementDetailsRule.StartFromWalk => running && input.GroundedAmount >= 1 && input.StandingMachineWeight >= 1,
            AlsRefactoredMovementDetailsRule.RunWhileNotGrounded => running && input.GroundedAmount < 1,
            AlsRefactoredMovementDetailsRule.Pivot => input.PivotActive,
            AlsRefactoredMovementDetailsRule.Walk => (string.IsNullOrEmpty(input.Gait) || input.Gait == "Als.Gait.Walking") && input.UnweightedGaitRunningAmount < .2d,
            _ => throw new ArgumentException("Automatic transition requires relevant-player timing.", nameof(rule))
        };
    }
}
