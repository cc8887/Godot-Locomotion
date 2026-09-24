namespace GodotAls.Core.Locomotion;

public readonly record struct AlsRefactoredWeaponRuleInput(string RotationMode, string Gait,
    string LocomotionMode, bool Moving, bool TransitionsAllowed);

public enum AlsRefactoredWeaponRuleKind { Aiming, NotAiming, AllowedAfterDelay, MovingAfterDelay, SprintOrAir }

/// <summary>Refactored rules consume the parent's refreshed state, not V4 curve comparisons.</summary>
public readonly record struct AlsRefactoredWeaponRule(AlsRefactoredWeaponRuleKind Kind, double Delay = 0)
{
    public bool Matches(in AlsRefactoredWeaponRuleInput input, float elapsed)
    {
        if (!float.IsFinite(elapsed) || elapsed < 0 || !double.IsFinite(Delay) || Delay < 0)
            throw new ArgumentException("Invalid weapon state time.");
        return Kind switch
        {
            AlsRefactoredWeaponRuleKind.Aiming => input.RotationMode == "Als.RotationMode.Aiming",
            AlsRefactoredWeaponRuleKind.NotAiming => input.RotationMode != "Als.RotationMode.Aiming",
            // Blueprint promotes the native float state clock to double for GreaterEqual.
            AlsRefactoredWeaponRuleKind.AllowedAfterDelay => input.TransitionsAllowed && elapsed >= Delay,
            AlsRefactoredWeaponRuleKind.MovingAfterDelay => input.Moving && elapsed >= Delay,
            AlsRefactoredWeaponRuleKind.SprintOrAir => input.Gait == "Als.Gait.Sprinting" || input.LocomotionMode == "Als.LocomotionMode.InAir",
            _ => throw new ArgumentOutOfRangeException(nameof(Kind))
        };
    }
}
