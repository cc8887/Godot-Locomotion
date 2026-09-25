namespace GodotAls.Core.Locomotion;

public enum AlsRefactoredDirectionRuleKind { Forward, Backward, Left, Right, HipsLeft, HipsRight, Unlock }
public readonly record struct AlsRefactoredDirectionInput(bool Forward, bool Backward, bool Left, bool Right,
    float HipsLock, float FeetCrossing);

public readonly record struct AlsRefactoredDirectionRule(AlsRefactoredDirectionRuleKind Kind, int RequiredFullState = -1)
{
    public bool Evaluate(in AlsRefactoredDirectionInput input, ReadOnlySpan<float> stateWeights)
    {
        if (!Enum.IsDefined(Kind) || !float.IsFinite(input.HipsLock) || !float.IsFinite(input.FeetCrossing) ||
            stateWeights.Length != 6 || (Kind == AlsRefactoredDirectionRuleKind.Unlock ? RequiredFullState is < 0 or >= 6 : RequiredFullState != -1))
            throw new ArgumentException("Invalid original direction predicate input.");
        foreach (var weight in stateWeights) if (!float.IsFinite(weight) || weight is < 0 or > 1) throw new ArgumentException("Invalid direction state weight.");
        return Kind switch
        {
            AlsRefactoredDirectionRuleKind.Forward => input.Forward,
            AlsRefactoredDirectionRuleKind.Backward => input.Backward,
            AlsRefactoredDirectionRuleKind.Left => input.Left,
            AlsRefactoredDirectionRuleKind.Right => input.Right,
            AlsRefactoredDirectionRuleKind.HipsLeft => input.HipsLock <= -.5 && input.FeetCrossing <= 0,
            AlsRefactoredDirectionRuleKind.HipsRight => input.HipsLock >= .5 && input.FeetCrossing <= 0,
            AlsRefactoredDirectionRuleKind.Unlock => System.Math.Abs((double)input.HipsLock) < .5 && input.FeetCrossing <= 0 && stateWeights[RequiredFullState] >= 1,
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}
