using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsLocomotionCommandResolverTests
{
    [Theory]
    [InlineData(0f, 2f, 0f, 0f, 0f, -1f, 1f)]
    [InlineData(2f, 0f, 1.57079632679f, 0f, 0f, -1f, 1f)]
    [InlineData(0f, 0.65f, 0f, 0f, 0f, -1f, 0.65f)]
    public void ResolveClampsAxesAndRotatesUnitDirection(
        float x,
        float y,
        float yaw,
        float expectedX,
        float expectedY,
        float expectedZ,
        float expectedAmount)
    {
        var command = CreateCommand(new Vector2(x, y), viewYaw: yaw);

        var resolved = AlsLocomotionCommandResolver.Resolve(command, AlsStance.Standing);

        AssertVectorNear(new Vector3(expectedX, expectedY, expectedZ), resolved.WorldDirection);
        Assert.Equal(expectedAmount, resolved.InputAmount, 5);
        Assert.Equal(expectedAmount, resolved.WorldDirection.Length() * resolved.InputAmount, 5);
    }

    [Fact]
    public void ResolvePreservesZeroInputWithoutNormalizing()
    {
        var resolved = AlsLocomotionCommandResolver.Resolve(
            CreateCommand(Vector2.Zero, gait: AlsGait.Sprinting),
            AlsStance.Standing);

        Assert.Equal(Vector3.Zero, resolved.WorldDirection);
        Assert.Equal(0f, resolved.InputAmount);
        Assert.Equal(AlsGait.Running, resolved.MaxAllowedGait);
    }

    [Theory]
    [InlineData(AlsStance.Crouching, AlsRotationMode.VelocityDirection)]
    [InlineData(AlsStance.Standing, AlsRotationMode.Aiming)]
    public void ResolveRejectsSprintForCrouchingOrAiming(
        AlsStance actualStance,
        AlsRotationMode rotationMode)
    {
        var resolved = AlsLocomotionCommandResolver.Resolve(
            CreateCommand(Vector2.UnitY, rotationMode: rotationMode, gait: AlsGait.Sprinting),
            actualStance);

        Assert.Equal(AlsGait.Running, resolved.MaxAllowedGait);
    }

    [Fact]
    public void VelocityDirectionAllowsSprintForAnyNonzeroStandingInput()
    {
        var resolved = AlsLocomotionCommandResolver.Resolve(
            CreateCommand(-Vector2.UnitY, rotationMode: AlsRotationMode.VelocityDirection, gait: AlsGait.Sprinting),
            AlsStance.Standing);

        Assert.Equal(AlsGait.Sprinting, resolved.MaxAllowedGait);
    }

    [Theory]
    [InlineData(0f, 1f, AlsGait.Sprinting)]
    [InlineData(0f, -1f, AlsGait.Running)]
    [InlineData(1f, 0f, AlsGait.Running)]
    public void LookingDirectionSprintDependsOnCameraRelativeInput(
        float x,
        float y,
        AlsGait expected)
    {
        var resolved = AlsLocomotionCommandResolver.Resolve(
            CreateCommand(new Vector2(x, y), viewYaw: 2.4f, gait: AlsGait.Sprinting),
            AlsStance.Standing);

        Assert.Equal(expected, resolved.MaxAllowedGait);
    }

    [Theory]
    [InlineData(49.999f, AlsGait.Sprinting)]
    [InlineData(50f, AlsGait.Running)]
    public void LookingDirectionSprintUsesStrictFiftyDegreeBoundary(float degrees, AlsGait expected)
    {
        var radians = degrees * MathF.PI / 180f;
        var axes = new Vector2(MathF.Sin(radians), MathF.Cos(radians));

        var resolved = AlsLocomotionCommandResolver.Resolve(
            CreateCommand(axes, gait: AlsGait.Sprinting),
            AlsStance.Standing);

        Assert.Equal(expected, resolved.MaxAllowedGait);
    }

    [Theory]
    [InlineData(AlsGait.Walking)]
    [InlineData(AlsGait.Running)]
    public void NonSprintGaitsPassThrough(AlsGait gait)
    {
        var command = CreateCommand(Vector2.UnitY, gait: gait) with
        {
            RequestedStance = AlsStance.Crouching,
            JumpPressed = 1,
        };

        var resolved = AlsLocomotionCommandResolver.Resolve(command, AlsStance.Standing);

        Assert.Equal(gait, resolved.MaxAllowedGait);
        Assert.Equal(AlsStance.Crouching, resolved.RequestedStance);
        Assert.Equal(AlsRotationMode.LookingDirection, resolved.RotationMode);
        Assert.Equal((byte)1, resolved.JumpPressed);
    }

    [Theory]
    [InlineData(float.NaN, 0f, 0f)]
    [InlineData(0f, float.PositiveInfinity, 0f)]
    [InlineData(0f, 0f, float.NegativeInfinity)]
    public void ResolveRejectsNonfiniteInputs(float x, float y, float yaw)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AlsLocomotionCommandResolver.Resolve(
                CreateCommand(new Vector2(x, y), viewYaw: yaw),
                AlsStance.Standing));
    }

    [Fact]
    public void ResolverValueContractsContainOnlyUnmanagedData()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsDirectionalSpeeds>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsStanceSpeeds>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsResolvedLocomotionCommand>());
    }

    [Fact]
    public void ResolveDoesNotAllocateAfterWarmup()
    {
        var command = CreateCommand(new Vector2(0.25f, 0.75f), viewYaw: 0.75f, gait: AlsGait.Sprinting);
        Run(command, 100);

        var before = GC.GetAllocatedBytesForCurrentThread();
        Run(command, 10_000);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static void Run(AlsLocomotionCommand command, int iterations)
    {
        for (var index = 0; index < iterations; index++)
        {
            _ = AlsLocomotionCommandResolver.Resolve(command, AlsStance.Standing);
        }
    }

    private static AlsLocomotionCommand CreateCommand(
        Vector2 movementAxes,
        float viewYaw = 0f,
        AlsRotationMode rotationMode = AlsRotationMode.LookingDirection,
        AlsGait gait = AlsGait.Running) => new(
            movementAxes,
            viewYaw,
            0f,
            gait,
            AlsStance.Standing,
            rotationMode,
            0);

    private static void AssertVectorNear(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, 5);
        Assert.Equal(expected.Y, actual.Y, 5);
        Assert.Equal(expected.Z, actual.Z, 5);
    }
}

public sealed class AlsLocomotionSettingsTests
{
    [Fact]
    public void LoadsPinnedGeneratedSettingsIntoImmutableValues()
    {
        var settings = P3TestSettings.Reference;

        Assert.Equal(1f / 60f, settings.FixedDeltaSeconds, 6);
        Assert.Equal(20f, settings.InitialMaxAcceleration);
        Assert.Equal(15f, settings.InitialMaxBrakingDeceleration);
        Assert.Equal(0.5f, settings.MovingSpeedThreshold);
        Assert.Equal(0.9f, settings.StandingHalfHeight);
        Assert.Equal(0.56f, settings.CrouchedHalfHeight);
        Assert.Equal(9.8f, settings.Gravity);
        Assert.Equal(4.2f, settings.JumpSpeed);
        Assert.Equal(0.1f, settings.VelocitySmoothingHalfLife);
        Assert.Equal(0.1f, settings.AccelerationSmoothingHalfLife);
        Assert.Equal(0.2f, settings.LeanHalfLife);
        Assert.Equal(0.1f, settings.RotationInterpolationHalfLife);
        Assert.Equal(12f, settings.TargetYawInterpolationSpeed);
        Assert.Equal(0.2f, settings.LandingRecoveryDuration);
        Assert.Equal(0f, settings.PlayRateMinimum);
        Assert.Equal(3f, settings.PlayRateMaximum);
        Assert.Equal(1.5f, settings.AnimatedWalkSpeed);
        Assert.Equal(3.5f, settings.AnimatedRunSpeed);
        Assert.Equal(6f, settings.AnimatedSprintSpeed);
        Assert.Equal(1.5f, settings.AnimatedCrouchSpeed);
        Assert.Equal(1.7453293f, settings.VelocityAngleInterpolationStart, 5);
        Assert.Equal(2.1816616f, settings.VelocityAngleInterpolationEnd, 5);

        Assert.Equal(new AlsDirectionalSpeeds(1.75f, 1.75f, 1.75f), settings.Standing.Walking);
        Assert.Equal(new AlsDirectionalSpeeds(3.75f, 3.75f, 3.75f), settings.Standing.Running);
        Assert.Equal(new AlsDirectionalSpeeds(6.5f, 6.5f, 6.5f), settings.Standing.Sprinting);
        Assert.Equal(new AlsDirectionalSpeeds(1.5f, 1.5f, 1.5f), settings.Crouching.Walking);
        Assert.Equal(new AlsDirectionalSpeeds(2f, 2f, 2f), settings.Crouching.Running);
        Assert.Equal(settings.Crouching.Running, settings.Crouching.Sprinting);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("missing")]
    [InlineData("commit")]
    [InlineData("nonfinite")]
    [InlineData("duplicate")]
    [InlineData("wrongDelta")]
    [InlineData("zeroDelta")]
    public void RejectsInvalidSettingsDocuments(string mutation)
    {
        var original = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "P3",
            "p3_locomotion_settings.json"));
        var root = JsonNode.Parse(original)!.AsObject();

        var json = mutation switch
        {
            "unknown" => AddUnknown(root),
            "missing" => RemoveRequired(root),
            "commit" => ChangeCommit(root),
            "nonfinite" => original.Replace(
                "\"gravity\": 9.8000000000000007",
                "\"gravity\": 1e400",
                StringComparison.Ordinal),
            "duplicate" => original.Replace(
                "\"kind\": \"settings\"",
                "\"kind\": \"settings\", \"kind\": \"settings\"",
                StringComparison.Ordinal),
            "wrongDelta" => original.Replace(
                "\"fixedDeltaSeconds\": 0.016666666666666666",
                "\"fixedDeltaSeconds\": 0.02",
                StringComparison.Ordinal),
            "zeroDelta" => original.Replace(
                "\"fixedDeltaSeconds\": 0.016666666666666666",
                "\"fixedDeltaSeconds\": 0",
                StringComparison.Ordinal),
            _ => throw new InvalidOperationException(),
        };

        Assert.Throws<FormatException>(() => AlsLocomotionSettings.Load(json));
    }

    private static string AddUnknown(JsonObject root)
    {
        root["unexpected"] = true;
        return root.ToJsonString();
    }

    private static string RemoveRequired(JsonObject root)
    {
        root.Remove("fixedDeltaSeconds");
        return root.ToJsonString();
    }

    private static string ChangeCommit(JsonObject root)
    {
        root["referenceCommit"] = new string('0', 40);
        return root.ToJsonString();
    }
}
