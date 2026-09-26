using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

/// <summary>Actual Character CDO and animation settings bindings. Distinct
/// Moving and MovingSmooth thresholds must not inherit V4 graph rules.</summary>
public sealed class AlsRefactoredLocomotionSettings
{
    public float MovingThreshold { get; }
    public float MovingSmoothThreshold { get; }
    public float TeleportDistance { get; }
    public bool IgnoreBaseRotation { get; }
    public bool InheritBaseYawInVelocityMode { get; }
    public AlsRefactoredLocomotionSettings(string characterJson, string animationJson)
    {
        using var character = JsonDocument.Parse(characterJson);
        using var animation = JsonDocument.Parse(animationJson);
        var c = character.RootElement; var a = animation.RootElement;
        Expect(c, new { schemaVersion = 1, characterClass = "/ALS/ALS/Character/B_Als_Character.B_Als_Character_C",
            source = "/ALS/ALS/Data/Character/CS_Als_Default.CS_Als_Default" });
        Expect(a, new { schemaVersion = 1, source = AlsRefactoredMovementSettings.Source,
            animationClass = "/ALS/ALS/Character/AB_Als.AB_Als_C" });
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(animationJson)));
        if (!string.Equals(c.GetProperty("animationSettingsSha256").GetString(), digest, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Foreign animation settings for locomotion observation.");
        MovingThreshold = Number(c, "movingSpeedThreshold");
        TeleportDistance = Number(c, "teleportDistanceThreshold");
        IgnoreBaseRotation = c.GetProperty("ignoreBaseRotation").GetBoolean();
        InheritBaseYawInVelocityMode = c.GetProperty("inheritBaseYawInVelocityMode").GetBoolean();
        MovingSmoothThreshold = Number(a.GetProperty("general"), "movingSmoothSpeedThreshold");
    }
    private static float Number(JsonElement owner, string key)
    {
        var value = owner.GetProperty(key).GetSingle();
        return float.IsFinite(value) && value >= 0 ? value : throw new ArgumentException("Invalid locomotion setting.");
    }
}
