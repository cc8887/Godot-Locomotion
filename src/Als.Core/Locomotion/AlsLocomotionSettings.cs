using System.Text.Json;

namespace GodotAls.Core.Locomotion;

public sealed class AlsLocomotionSettings
{
    public const string ReferenceCommit = "b754d6f0f2bb03741d301f8fb88077ebfe561e17";
    public const string PatchHash = "3dc561f194045d3dc01bd65c7f7c3bd4acd0a30c0fab31ea0cd16d676d312e5f";

    private static readonly string[] RootProperties =
    [
        "fixedDeltaSeconds",
        "kind",
        "patchHashes",
        "referenceCommit",
        "schemaVersion",
        "sources",
        "values",
    ];

    private static readonly string[] SourceProperties =
    [
        "animation",
        "character",
        "movement",
        "portDefaults",
    ];

    private static readonly string[] ValueProperties =
    [
        "accelerationSmoothingHalfLife",
        "animatedCrouchSpeed",
        "animatedRunSpeed",
        "animatedSprintSpeed",
        "animatedWalkSpeed",
        "crouchedHalfHeight",
        "crouchRunForwardSpeed",
        "crouchWalkForwardSpeed",
        "gravity",
        "initialMaxAcceleration",
        "initialMaxBrakingDeceleration",
        "jumpSpeed",
        "landingRecoveryDuration",
        "leanHalfLife",
        "movingSpeedThreshold",
        "playRateMaximum",
        "playRateMinimum",
        "rotationInterpolationHalfLife",
        "runBackwardSpeed",
        "runForwardSpeed",
        "sprintSpeed",
        "standingHalfHeight",
        "targetYawInterpolationSpeed",
        "velocityAngleInterpolationEnd",
        "velocityAngleInterpolationStart",
        "velocitySmoothingHalfLife",
        "walkBackwardSpeed",
        "walkForwardSpeed",
    ];

    private AlsLocomotionSettings(JsonElement root)
    {
        FixedDeltaSeconds = ReadPositive(root, "fixedDeltaSeconds");

        var values = root.GetProperty("values");
        AccelerationSmoothingHalfLife = ReadNonnegative(values, "accelerationSmoothingHalfLife");
        AnimatedCrouchSpeed = ReadNonnegative(values, "animatedCrouchSpeed");
        AnimatedRunSpeed = ReadNonnegative(values, "animatedRunSpeed");
        AnimatedSprintSpeed = ReadNonnegative(values, "animatedSprintSpeed");
        AnimatedWalkSpeed = ReadNonnegative(values, "animatedWalkSpeed");
        CrouchedHalfHeight = ReadPositive(values, "crouchedHalfHeight");
        CrouchRunForwardSpeed = ReadNonnegative(values, "crouchRunForwardSpeed");
        CrouchWalkForwardSpeed = ReadNonnegative(values, "crouchWalkForwardSpeed");
        Gravity = ReadPositive(values, "gravity");
        InitialMaxAcceleration = ReadPositive(values, "initialMaxAcceleration");
        InitialMaxBrakingDeceleration = ReadNonnegative(values, "initialMaxBrakingDeceleration");
        JumpSpeed = ReadNonnegative(values, "jumpSpeed");
        LandingRecoveryDuration = ReadNonnegative(values, "landingRecoveryDuration");
        LeanHalfLife = ReadNonnegative(values, "leanHalfLife");
        MovingSpeedThreshold = ReadNonnegative(values, "movingSpeedThreshold");
        PlayRateMaximum = ReadNonnegative(values, "playRateMaximum");
        PlayRateMinimum = ReadNonnegative(values, "playRateMinimum");
        RotationInterpolationHalfLife = ReadNonnegative(values, "rotationInterpolationHalfLife");
        RunBackwardSpeed = ReadNonnegative(values, "runBackwardSpeed");
        RunForwardSpeed = ReadNonnegative(values, "runForwardSpeed");
        SprintSpeed = ReadNonnegative(values, "sprintSpeed");
        StandingHalfHeight = ReadPositive(values, "standingHalfHeight");
        TargetYawInterpolationSpeed = ReadNonnegative(values, "targetYawInterpolationSpeed");
        VelocityAngleInterpolationEnd = ReadAngle(values, "velocityAngleInterpolationEnd");
        VelocityAngleInterpolationStart = ReadAngle(values, "velocityAngleInterpolationStart");
        VelocitySmoothingHalfLife = ReadNonnegative(values, "velocitySmoothingHalfLife");
        WalkBackwardSpeed = ReadNonnegative(values, "walkBackwardSpeed");
        WalkForwardSpeed = ReadNonnegative(values, "walkForwardSpeed");

        if (PlayRateMinimum > PlayRateMaximum)
        {
            throw new FormatException("playRateMinimum must not exceed playRateMaximum.");
        }

        if (VelocityAngleInterpolationStart > VelocityAngleInterpolationEnd)
        {
            throw new FormatException("velocityAngleInterpolationStart must not exceed velocityAngleInterpolationEnd.");
        }

        Standing = new AlsStanceSpeeds(
            CreateDirectional(WalkForwardSpeed, WalkBackwardSpeed),
            CreateDirectional(RunForwardSpeed, RunBackwardSpeed),
            CreateDirectional(SprintSpeed, SprintSpeed));
        Crouching = new AlsStanceSpeeds(
            CreateDirectional(CrouchWalkForwardSpeed, CrouchWalkForwardSpeed),
            CreateDirectional(CrouchRunForwardSpeed, CrouchRunForwardSpeed),
            CreateDirectional(CrouchRunForwardSpeed, CrouchRunForwardSpeed));
    }

    public float FixedDeltaSeconds { get; }
    public float AccelerationSmoothingHalfLife { get; }
    public float AnimatedCrouchSpeed { get; }
    public float AnimatedRunSpeed { get; }
    public float AnimatedSprintSpeed { get; }
    public float AnimatedWalkSpeed { get; }
    public float CrouchedHalfHeight { get; }
    public float CrouchRunForwardSpeed { get; }
    public float CrouchWalkForwardSpeed { get; }
    public float Gravity { get; }
    public float InitialMaxAcceleration { get; }
    public float InitialMaxBrakingDeceleration { get; }
    public float JumpSpeed { get; }
    public float LandingRecoveryDuration { get; }
    public float LeanHalfLife { get; }
    public float MovingSpeedThreshold { get; }
    public float PlayRateMaximum { get; }
    public float PlayRateMinimum { get; }
    public float RotationInterpolationHalfLife { get; }
    public float RunBackwardSpeed { get; }
    public float RunForwardSpeed { get; }
    public float SprintSpeed { get; }
    public float StandingHalfHeight { get; }
    public float TargetYawInterpolationSpeed { get; }
    public float VelocityAngleInterpolationEnd { get; }
    public float VelocityAngleInterpolationStart { get; }
    public float VelocitySmoothingHalfLife { get; }
    public float WalkBackwardSpeed { get; }
    public float WalkForwardSpeed { get; }
    public AlsStanceSpeeds Standing { get; }
    public AlsStanceSpeeds Crouching { get; }

    public static AlsLocomotionSettings Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            ValidateObject(root, RootProperties, "root");
            RequireInt32(root, "schemaVersion", 1);
            RequireString(root, "kind", "settings");
            RequireString(root, "referenceCommit", ReferenceCommit);
            ValidatePatchHashes(root.GetProperty("patchHashes"));

            var sources = root.GetProperty("sources");
            ValidateObject(sources, SourceProperties, "sources");
            RequireString(sources, "animation", "/ALS/ALS/Data/AnimationInstance/AIS_Als_Default");
            RequireString(sources, "character", "/ALS/ALS/Data/Character/CS_Als_Default");
            RequireString(sources, "movement", "/ALS/ALS/Data/Character/Movement/MS_Als_Normal");
            RequireString(
                sources,
                "portDefaults",
                "accelerationSmoothingHalfLife,landingRecoveryDuration,playRateMaximum,playRateMinimum,rotationInterpolationHalfLife,targetYawInterpolationSpeed,velocitySmoothingHalfLife");

            ValidateObject(root.GetProperty("values"), ValueProperties, "values");
            return new AlsLocomotionSettings(root);
        }
        catch (FormatException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or OverflowException)
        {
            throw new FormatException("Invalid P3 locomotion settings JSON.", exception);
        }
    }

    private static AlsDirectionalSpeeds CreateDirectional(float forward, float backward) =>
        new(forward, forward, backward);

    private static void ValidateObject(JsonElement element, string[] expectedProperties, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"{path} must be an object.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new FormatException($"Duplicate property '{path}.{property.Name}'.");
            }

            if (Array.IndexOf(expectedProperties, property.Name) < 0)
            {
                throw new FormatException($"Unknown property '{path}.{property.Name}'.");
            }
        }

        foreach (var expectedProperty in expectedProperties)
        {
            if (!seen.Contains(expectedProperty))
            {
                throw new FormatException($"Missing property '{path}.{expectedProperty}'.");
            }
        }
    }

    private static void ValidatePatchHashes(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 1)
        {
            throw new FormatException("patchHashes must contain exactly one entry.");
        }

        var value = element[0];
        if (value.ValueKind != JsonValueKind.String || value.GetString() != PatchHash)
        {
            throw new FormatException("patchHashes does not match the locked compatibility patch.");
        }
    }

    private static void RequireInt32(JsonElement parent, string propertyName, int expected)
    {
        var element = parent.GetProperty(propertyName);
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value) || value != expected)
        {
            throw new FormatException($"{propertyName} must equal {expected}.");
        }
    }

    private static void RequireString(JsonElement parent, string propertyName, string expected)
    {
        var element = parent.GetProperty(propertyName);
        if (element.ValueKind != JsonValueKind.String || element.GetString() != expected)
        {
            throw new FormatException($"{propertyName} does not match the locked value.");
        }
    }

    private static float ReadPositive(JsonElement parent, string propertyName)
    {
        var value = ReadFinite(parent, propertyName);
        if (value <= 0f)
        {
            throw new FormatException($"{propertyName} must be positive.");
        }

        return value;
    }

    private static float ReadNonnegative(JsonElement parent, string propertyName)
    {
        var value = ReadFinite(parent, propertyName);
        if (value < 0f)
        {
            throw new FormatException($"{propertyName} must be nonnegative.");
        }

        return value;
    }

    private static float ReadAngle(JsonElement parent, string propertyName)
    {
        var value = ReadNonnegative(parent, propertyName);
        if (value > MathF.PI)
        {
            throw new FormatException($"{propertyName} must be within [0, pi].");
        }

        return value;
    }

    private static float ReadFinite(JsonElement parent, string propertyName)
    {
        var element = parent.GetProperty(propertyName);
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetSingle(out var value) || !float.IsFinite(value))
        {
            throw new FormatException($"{propertyName} must be a finite number.");
        }

        return value;
    }
}
