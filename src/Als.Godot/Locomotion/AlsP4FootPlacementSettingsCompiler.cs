using GodotAls.Core.Contracts;
using GodotAls.Import.Compilation;
using CoreFootPlacementSettings = GodotAls.Core.Pose.AlsFootPlacementSettings;

namespace GodotAls.Locomotion;

internal static class AlsP4FootPlacementSettingsCompiler
{
    public static CoreFootPlacementSettings Compile(
        in GodotAls.Import.Compilation.AlsFootPlacementSettings profile,
        in AlsMotorSettings motor,
        AlsStance stance)
    {
        motor.Validate();
        if (profile.CapsuleHalfHeightSource !=
            AlsCapsuleHalfHeightSource.CharacterController)
        {
            throw new ArgumentOutOfRangeException(
                nameof(profile), "Unsupported foot-placement capsule-height source.");
        }
        var capsuleHalfHeight = stance switch
        {
            AlsStance.Standing => motor.StandingHeight * 0.5f,
            AlsStance.Crouching => motor.CrouchingHeight * 0.5f,
            _ => throw new ArgumentOutOfRangeException(nameof(stance)),
        };
        if (capsuleHalfHeight > profile.MaximumLegReachMeters)
        {
            throw new InvalidOperationException(
                "Character-controller capsule half-height exceeds the compiled maximum leg reach.");
        }
        return new CoreFootPlacementSettings(
            1,
            profile.FootHeightMeters,
            profile.MaxPelvisCorrectionMeters,
            profile.PelvisUpHalfLifeSeconds,
            profile.PelvisDownHalfLifeSeconds,
            profile.LockReleaseHalfLifeSeconds,
            profile.MaximumLegReachMeters,
            capsuleHalfHeight,
            profile.MaximumThighAngleRadians,
            profile.MaximumFootAngleRadians,
            profile.PlatformTeleportDistanceMeters,
            profile.PlatformTeleportAngleRadians,
            profile.LockWeightEpsilon);
    }
}
