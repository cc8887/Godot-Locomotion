namespace GodotAls.Core.Locomotion;

public enum AlsRefactoredStopRule { LockRight, LockLeft, PlantRight, PlantLeft }

public static class AlsRefactoredStopRules
{
    public static bool Evaluate(this AlsRefactoredStopRule rule, float footPlantedAmount)
    {
        if (!float.IsFinite(footPlantedAmount)) throw new ArgumentException("Invalid FootPlantedAmount.");
        return rule switch
        {
            AlsRefactoredStopRule.LockRight => footPlantedAmount > .5f,
            AlsRefactoredStopRule.LockLeft => footPlantedAmount <= -.5f,
            AlsRefactoredStopRule.PlantRight => footPlantedAmount > 0,
            AlsRefactoredStopRule.PlantLeft => footPlantedAmount <= 0,
            _ => throw new ArgumentOutOfRangeException(nameof(rule))
        };
    }
}
