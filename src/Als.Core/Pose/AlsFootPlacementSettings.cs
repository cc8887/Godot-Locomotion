using System.Runtime.InteropServices;

namespace GodotAls.Core.Pose;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsFootPlacementSettings(
    byte Enabled,
    float FootHeightMeters,
    float MaximumPelvisCorrectionMeters,
    float PelvisUpHalfLifeSeconds,
    float PelvisDownHalfLifeSeconds,
    float LockReleaseHalfLifeSeconds,
    float MaximumLegReachMeters,
    float CapsuleHalfHeightMeters,
    float MaximumThighAngleRadians,
    float MaximumFootAngleRadians,
    float PlatformTeleportDistanceMeters,
    float PlatformTeleportAngleRadians)
{
    public static AlsFootPlacementSettings CreateReference() => new(
        1,
        0.13f,
        0.4f,
        0.08f,
        0.1f,
        0.12f,
        1.2f,
        0.9f,
        90f * MathF.PI / 180f,
        40f * MathF.PI / 180f,
        1f,
        45f * MathF.PI / 180f);

    internal bool Validate() =>
        Enabled <= 1 &&
        IsNonnegativeFinite(FootHeightMeters) &&
        IsNonnegativeFinite(MaximumPelvisCorrectionMeters) &&
        IsNonnegativeFinite(PelvisUpHalfLifeSeconds) &&
        IsNonnegativeFinite(PelvisDownHalfLifeSeconds) &&
        IsNonnegativeFinite(LockReleaseHalfLifeSeconds) &&
        IsPositiveFinite(MaximumLegReachMeters) &&
        IsNonnegativeFinite(CapsuleHalfHeightMeters) &&
        CapsuleHalfHeightMeters <= MaximumLegReachMeters &&
        IsAngle(MaximumThighAngleRadians) &&
        IsAngle(MaximumFootAngleRadians) &&
        IsPositiveFinite(PlatformTeleportDistanceMeters) &&
        IsAngle(PlatformTeleportAngleRadians);

    private static bool IsAngle(float value) =>
        float.IsFinite(value) && value >= 0f && value <= MathF.PI;

    private static bool IsPositiveFinite(float value) =>
        float.IsFinite(value) && value > 0f;

    private static bool IsNonnegativeFinite(float value) =>
        float.IsFinite(value) && value >= 0f;
}
