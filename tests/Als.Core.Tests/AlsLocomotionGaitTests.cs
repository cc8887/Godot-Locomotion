using System.Numerics;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsLocomotionGaitTests
{
    [Theory]
    [InlineData(1.8499f, AlsGait.Walking)]
    [InlineData(1.85f, AlsGait.Running)]
    [InlineData(3.8499f, AlsGait.Running)]
    [InlineData(3.85f, AlsGait.Sprinting)]
    public void ActualGaitUsesExactAlsThresholds(float speed, AlsGait expected)
    {
        var actual = AlsLocomotionModel.CalculateActualGait(
            speed,
            1.75f,
            3.75f,
            AlsGait.Sprinting);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ActualGaitChangesAtTheExactRepresentableThreshold()
    {
        var walkThreshold = 1.75f + 0.1f;
        var runThreshold = 3.75f + 0.1f;

        Assert.Equal(
            AlsGait.Walking,
            AlsLocomotionModel.CalculateActualGait(
                MathF.BitDecrement(walkThreshold),
                1.75f,
                3.75f,
                AlsGait.Sprinting));
        Assert.Equal(
            AlsGait.Running,
            AlsLocomotionModel.CalculateActualGait(
                walkThreshold,
                1.75f,
                3.75f,
                AlsGait.Sprinting));
        Assert.Equal(
            AlsGait.Running,
            AlsLocomotionModel.CalculateActualGait(
                MathF.BitDecrement(runThreshold),
                1.75f,
                3.75f,
                AlsGait.Sprinting));
        Assert.Equal(
            AlsGait.Sprinting,
            AlsLocomotionModel.CalculateActualGait(
                runThreshold,
                1.75f,
                3.75f,
                AlsGait.Sprinting));
    }

    [Theory]
    [InlineData(AlsGait.Running)]
    [InlineData(AlsGait.Walking)]
    public void NonSprintMaximumStillReportsRunningAboveTheWalkThreshold(AlsGait maxAllowedGait)
    {
        var actual = AlsLocomotionModel.CalculateActualGait(
            6.5f,
            1.75f,
            3.75f,
            maxAllowedGait);

        Assert.Equal(AlsGait.Running, actual);
    }

    [Theory]
    [InlineData(float.NaN, 1.75f, 3.75f)]
    [InlineData(float.PositiveInfinity, 1.75f, 3.75f)]
    [InlineData(-0.01f, 1.75f, 3.75f)]
    [InlineData(1f, float.NaN, 3.75f)]
    [InlineData(1f, -1f, 3.75f)]
    [InlineData(1f, 1.75f, float.PositiveInfinity)]
    [InlineData(1f, 1.75f, -1f)]
    public void ActualGaitRejectsInvalidSpeeds(float speed, float maxWalkSpeed, float maxRunSpeed)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AlsLocomotionModel.CalculateActualGait(
                speed,
                maxWalkSpeed,
                maxRunSpeed,
                AlsGait.Sprinting));
    }

    [Fact]
    public void ActualGaitRejectsUndefinedMaximumGait()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AlsLocomotionModel.CalculateActualGait(
                1f,
                1.75f,
                3.75f,
                (AlsGait)byte.MaxValue));
    }

    [Theory]
    [InlineData(6.5f, 0f, AlsGait.Walking)]
    [InlineData(0f, 6.5f, AlsGait.Sprinting)]
    public void EvaluateUsesActualHorizontalVelocityInsteadOfDesiredSpeed(
        float desiredSpeed,
        float actualSpeed,
        AlsGait expected)
    {
        var identity = new AlsFrameIdentity(17, 2, 4);
        var input = AlsFrameInput.CreateDefault(identity, 1f / 60f) with
        {
            ActualVelocity = new Vector3(0f, 25f, -actualSpeed),
            DesiredSpeed = desiredSpeed,
            RequestedGait = AlsGait.Sprinting,
            Stance = AlsStance.Standing,
            RotationMode = AlsRotationMode.VelocityDirection,
        };
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult { ErrorCode = 91 };

        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference);

        Assert.Equal(identity, result.Identity);
        Assert.Equal(expected, state.ActualGait);
        Assert.Equal(expected, result.ActualGait);
        Assert.Equal(AlsStance.Standing, result.ActualStance);
        Assert.Equal(AlsRotationMode.VelocityDirection, result.ActualRotationMode);
        Assert.Equal(AlsDriveMode.MotorDriven, result.RequestedDriveMode);
        Assert.Equal(0, result.ErrorCode);
    }

    [Fact]
    public void EvaluateUsesDirectionDependentThresholds()
    {
        var settings = LoadAsymmetricSettings();
        var identity = new AlsFrameIdentity(3, 0, 1);
        var forwardInput = AlsFrameInput.CreateDefault(identity, 1f / 60f) with
        {
            ActualVelocity = new Vector3(0f, 0f, -2.5f),
            RequestedGait = AlsGait.Sprinting,
        };
        var backwardInput = forwardInput with
        {
            ActualVelocity = new Vector3(0f, 0f, 2.5f),
        };
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();

        AlsLocomotionModel.Evaluate(forwardInput, ref state, ref result, settings);
        Assert.Equal(AlsGait.Sprinting, result.ActualGait);

        AlsLocomotionModel.Evaluate(backwardInput, ref state, ref result, settings);
        Assert.Equal(AlsGait.Walking, result.ActualGait);
    }

    [Theory]
    [InlineData(1.57079632679f, -2.5f, AlsGait.Sprinting)]
    [InlineData(-1.57079632679f, 2.5f, AlsGait.Sprinting)]
    [InlineData(1.57079632679f, 2.5f, AlsGait.Walking)]
    public void EvaluateRecoversLocalDirectionFromRotatedWorldVelocity(
        float characterYaw,
        float worldVelocityX,
        AlsGait expected)
    {
        var settings = LoadAsymmetricSettings();
        var input = AlsFrameInput.CreateDefault(new AlsFrameIdentity(5, 0, 1), 1f / 60f) with
        {
            ActualVelocity = new Vector3(worldVelocityX, 0f, 0f),
            RequestedGait = AlsGait.Sprinting,
            CharacterYaw = characterYaw,
        };
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();

        AlsLocomotionModel.Evaluate(input, ref state, ref result, settings);

        Assert.Equal(expected, result.ActualGait);
    }

    [Fact]
    public void EvaluateRejectsUndefinedActualStance()
    {
        var input = AlsFrameInput.CreateDefault(new AlsFrameIdentity(0, 0, 1), 1f / 60f) with
        {
            Stance = (AlsStance)byte.MaxValue,
        };
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference));

        Assert.Contains("Stance", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateRejectsInvalidFrameValues()
    {
        var identity = new AlsFrameIdentity(0, 0, 1);
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();
        var invalidDelta = AlsFrameInput.CreateDefault(identity, 1f / 60f) with
        {
            DeltaTime = float.NaN,
        };
        var invalidVelocity = AlsFrameInput.CreateDefault(identity, 1f / 60f) with
        {
            ActualVelocity = new Vector3(float.PositiveInfinity, 0f, 0f),
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AlsLocomotionModel.Evaluate(invalidDelta, ref state, ref result, P3TestSettings.Reference));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AlsLocomotionModel.Evaluate(invalidVelocity, ref state, ref result, P3TestSettings.Reference));
        Assert.Throws<ArgumentNullException>(() =>
            AlsLocomotionModel.Evaluate(
                AlsFrameInput.CreateDefault(identity, 1f / 60f),
                ref state,
                ref result,
                null!));
    }

    [Fact]
    public void EvaluateDoesNotAllocateAfterWarmup()
    {
        var input = AlsFrameInput.CreateDefault(new AlsFrameIdentity(0, 0, 1), 1f / 60f) with
        {
            ActualVelocity = new Vector3(2f, -1f, -4f),
            RequestedGait = AlsGait.Sprinting,
            CharacterYaw = 0.25f,
        };
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();
        RunEvaluate(input, ref state, ref result, 100);

        var before = GC.GetAllocatedBytesForCurrentThread();
        RunEvaluate(input, ref state, ref result, 10_000);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static void RunEvaluate(
        AlsFrameInput input,
        ref AlsRuntimeState state,
        ref AlsFrameResult result,
        int iterations)
    {
        for (var index = 0; index < iterations; index++)
        {
            AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference);
        }
    }

    private static AlsLocomotionSettings LoadAsymmetricSettings()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "P3",
            "p3_locomotion_settings.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var values = root["values"]!.AsObject();
        values["walkForwardSpeed"] = 1f;
        values["walkBackwardSpeed"] = 3f;
        values["runForwardSpeed"] = 2f;
        values["runBackwardSpeed"] = 5f;
        return AlsLocomotionSettings.Load(root.ToJsonString());
    }
}
