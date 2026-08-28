using System.Runtime.InteropServices;

namespace GodotAls.Core.Pose;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsViewPoseSettings(
    float PitchClamp,
    float SpineResidualYawClamp,
    float AimingInHalfLife,
    float AimingOutHalfLife,
    float AimingHeadWeight,
    float NonAimingHeadWeight,
    float AimingSpineWeight,
    float NonAimingSpineWeight,
    float AimingUpperBodyWeight,
    float NonAimingUpperBodyWeight)
{
    public static AlsViewPoseSettings CreateDefault() => new(
        MathF.PI / 2f,
        MathF.PI / 6f,
        0.1f,
        0.7f,
        0f,
        1f,
        1f,
        0f,
        1f,
        0f);

    public bool Validate() =>
        float.IsFinite(PitchClamp) && PitchClamp > 0f && PitchClamp <= MathF.PI / 2f &&
        float.IsFinite(SpineResidualYawClamp) && SpineResidualYawClamp > 0f &&
        SpineResidualYawClamp <= MathF.PI &&
        float.IsFinite(AimingInHalfLife) && AimingInHalfLife > 0f &&
        float.IsFinite(AimingOutHalfLife) && AimingOutHalfLife > 0f &&
        IsWeight(AimingHeadWeight) &&
        IsWeight(NonAimingHeadWeight) &&
        IsWeight(AimingSpineWeight) &&
        IsWeight(NonAimingSpineWeight) &&
        IsWeight(AimingUpperBodyWeight) &&
        IsWeight(NonAimingUpperBodyWeight) &&
        HasRecoverableUpperBodyPhase();

    private bool HasRecoverableUpperBodyPhase()
    {
        if (AimingUpperBodyWeight == NonAimingUpperBodyWeight)
        {
            return true;
        }

        var minimumSpineWeight = MathF.Min(AimingSpineWeight, NonAimingSpineWeight);
        var maximumSpineWeight = MathF.Max(AimingSpineWeight, NonAimingSpineWeight);
        return MathF.BitIncrement(minimumSpineWeight) < maximumSpineWeight;
    }

    private static bool IsWeight(float value) =>
        float.IsFinite(value) && value >= 0f && value <= 1f;
}
