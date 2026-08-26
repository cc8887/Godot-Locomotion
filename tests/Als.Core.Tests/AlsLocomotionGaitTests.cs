using System.Numerics;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
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
            Command = CreateSprintCommand(),
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
            Command = CreateSprintCommand(),
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
            Command = CreateSprintCommand(),
            CharacterYaw = characterYaw,
        };
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();

        AlsLocomotionModel.Evaluate(input, ref state, ref result, settings);

        Assert.Equal(expected, result.ActualGait);
    }

    [Fact]
    public void ConvenienceEvaluateResolvesCrouchingSprintToRunning()
    {
        var input = AlsFrameInput.CreateDefault(new AlsFrameIdentity(8, 0, 1), 1f / 60f) with
        {
            ActualVelocity = new Vector3(0f, 0f, -6.5f),
            Stance = AlsStance.Crouching,
            Command = CreateSprintCommand(),
        };
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();

        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference);

        Assert.Equal(AlsGait.Running, result.ActualGait);
    }

    [Fact]
    public void ConvenienceEvaluateResolvesAimingSprintToRunning()
    {
        var input = AlsFrameInput.CreateDefault(new AlsFrameIdentity(9, 0, 1), 1f / 60f) with
        {
            ActualVelocity = new Vector3(0f, 0f, -6.5f),
            Command = CreateSprintCommand() with
            {
                RequestedRotationMode = AlsRotationMode.Aiming,
            },
        };
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();

        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference);

        Assert.Equal(AlsGait.Running, result.ActualGait);
    }

    [Fact]
    public void ExplicitEvaluateUsesResolvedMaximumInsteadOfLegacyRequestedGait()
    {
        var input = AlsFrameInput.CreateDefault(new AlsFrameIdentity(10, 0, 1), 1f / 60f) with
        {
            ActualVelocity = new Vector3(0f, 0f, -6.5f),
            RequestedGait = AlsGait.Sprinting,
            Command = CreateSprintCommand(),
        };
        var resolved = AlsLocomotionCommandResolver.Resolve(input.Command, input.Stance) with
        {
            MaxAllowedGait = AlsGait.Running,
        };
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();

        AlsLocomotionModel.Evaluate(input, resolved, ref state, ref result, P3TestSettings.Reference);

        Assert.Equal(AlsGait.Running, result.ActualGait);
    }

    [Fact]
    public void InvalidRawCommandPreservesStateAndResult()
    {
        var input = AlsFrameInput.CreateDefault(new AlsFrameIdentity(11, 0, 1), 1f / 60f) with
        {
            Command = CreateSprintCommand() with
            {
                RequestedGait = (AlsGait)byte.MaxValue,
            },
        };
        var state = CreateSentinelState();
        var result = CreateSentinelResult();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference));

        AssertSentinelsPreserved(state, result);
    }

    [Fact]
    public void InvalidExplicitResolvedMaximumPreservesStateAndResult()
    {
        var input = AlsFrameInput.CreateDefault(new AlsFrameIdentity(12, 0, 1), 1f / 60f);
        var resolved = AlsLocomotionCommandResolver.Resolve(input.Command, input.Stance) with
        {
            MaxAllowedGait = (AlsGait)byte.MaxValue,
        };
        var state = CreateSentinelState();
        var result = CreateSentinelResult();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AlsLocomotionModel.Evaluate(input, resolved, ref state, ref result, P3TestSettings.Reference));

        AssertSentinelsPreserved(state, result);
    }

    [Fact]
    public void ExplicitEvaluateRejectsInvalidResolvedContractBeforeWrites()
    {
        var input = AlsFrameInput.CreateDefault(new AlsFrameIdentity(13, 0, 1), 1f / 60f);
        var resolved = AlsLocomotionCommandResolver.Resolve(input.Command, input.Stance);
        var invalidCommands = new[]
        {
            resolved with { RequestedStance = (AlsStance)byte.MaxValue },
            resolved with { RotationMode = (AlsRotationMode)byte.MaxValue },
            resolved with { JumpPressed = 2 },
            resolved with { WorldDirection = new Vector3(float.NaN, 0f, 0f) },
            resolved with { InputAmount = float.PositiveInfinity },
            resolved with { InputAmount = -0.01f },
            resolved with { InputAmount = 1.01f },
            resolved with { WorldDirection = Vector3.Zero, InputAmount = 0.5f },
        };

        foreach (var invalidCommand in invalidCommands)
        {
            var state = CreateSentinelState();
            var result = CreateSentinelResult();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                AlsLocomotionModel.Evaluate(
                    input,
                    invalidCommand,
                    ref state,
                    ref result,
                    P3TestSettings.Reference));
            AssertSentinelsPreserved(state, result);
        }
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
            Command = CreateSprintCommand(),
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

    private static AlsLocomotionCommand CreateSprintCommand() =>
        AlsLocomotionCommand.CreateDefault() with
        {
            MovementAxes = Vector2.UnitY,
            RequestedGait = AlsGait.Sprinting,
        };

    private static AlsRuntimeState CreateSentinelState() => new()
    {
        ActualGait = AlsGait.Sprinting,
        AnimationPhase = 0.375f,
        LandingRecoveryTime = 2.5f,
    };

    private static AlsFrameResult CreateSentinelResult() => new()
    {
        Identity = new AlsFrameIdentity(99, 7, 3),
        ErrorCode = 701,
        ActualGait = AlsGait.Sprinting,
        AnimationPhase = 0.625f,
    };

    private static void AssertSentinelsPreserved(
        in AlsRuntimeState state,
        in AlsFrameResult result)
    {
        Assert.Equal(AlsGait.Sprinting, state.ActualGait);
        Assert.Equal(0.375f, state.AnimationPhase);
        Assert.Equal(2.5f, state.LandingRecoveryTime);
        Assert.Equal(new AlsFrameIdentity(99, 7, 3), result.Identity);
        Assert.Equal(701, result.ErrorCode);
        Assert.Equal(AlsGait.Sprinting, result.ActualGait);
        Assert.Equal(0.625f, result.AnimationPhase);
    }
}
