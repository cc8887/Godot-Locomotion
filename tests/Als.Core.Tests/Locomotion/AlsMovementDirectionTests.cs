using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsMovementDirectionTests
{
    [Fact]
    public void QuadrantsMatchCompiledV4FunctionIncludingBoundaryPriority()
    {
        using var fixture = ReadFixture();
        var rows = fixture.RootElement.GetProperty("quadrants");
        Assert.Equal(100, rows.GetArrayLength());
        foreach (var row in rows.EnumerateArray())
        {
            var actual = AlsMovementDirectionModel.CalculateQuadrant(
                (AlsMovementDirection)row.GetProperty("current").GetInt32(), row.GetProperty("angle").GetSingle());
            Assert.Equal((AlsMovementDirection)row.GetProperty("direction").GetInt32(), actual);
        }
    }

    [Fact]
    public void GaitRotationAndAimSpaceMatchCompiledV4Function()
    {
        using var fixture = ReadFixture();
        var rows = fixture.RootElement.GetProperty("movements");
        Assert.Equal(1620, rows.GetArrayLength());
        foreach (var row in rows.EnumerateArray())
        {
            var aimDegrees = row.GetProperty("aimYaw").GetSingle();
            var worldAngle = (row.GetProperty("angle").GetSingle() + aimDegrees) * MathF.PI / 180;
            var local = new Vector2(MathF.Sin(worldAngle), MathF.Cos(worldAngle));
            var actual = AlsMovementDirectionModel.Calculate(
                (AlsMovementDirection)row.GetProperty("current").GetInt32(), local, -aimDegrees * MathF.PI / 180,
                (AlsGait)row.GetProperty("gait").GetInt32(), (AlsRotationMode)row.GetProperty("rotationMode").GetInt32());
            Assert.Equal((AlsMovementDirection)row.GetProperty("direction").GetInt32(), actual);
        }
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void AimRelativeDecisionDoesNotRotateVelocityBlend(int hz)
    {
        var initial = new AlsStandingCycleState { VelocityBlend = Vector4.UnitX };
        var result = AlsStandingCycle.AdvanceDirection(initial, Vector2.UnitY, 1f / hz, 1, 0,
            aimRelativeYaw: MathF.PI / 2);
        Assert.Equal(AlsMovementDirection.Right, result.MovementDirection);
        Assert.Equal(AlsCycleDirection.RightForward, result.Direction);
        Assert.Equal(Vector4.UnitX, result.VelocityBlend);
        Assert.Equal(initial, new AlsStandingCycleState { VelocityBlend = Vector4.UnitX });
    }

    [Theory]
    [InlineData(AlsGait.Walking, AlsRotationMode.VelocityDirection)]
    [InlineData(AlsGait.Running, AlsRotationMode.VelocityDirection)]
    [InlineData(AlsGait.Sprinting, AlsRotationMode.LookingDirection)]
    [InlineData(AlsGait.Sprinting, AlsRotationMode.Aiming)]
    public void ForwardBranchesDoNotForceForwardVelocityWeights(AlsGait gait, AlsRotationMode rotation)
    {
        var result = AlsStandingCycle.AdvanceDirection(default, -Vector2.UnitX, 1f / 60, 1, 0,
            gait: gait, rotationMode: rotation);
        Assert.Equal(AlsMovementDirection.Forward, result.MovementDirection);
        Assert.Equal(AlsCycleDirection.Forward, result.Direction);
        Assert.Equal(new Vector4(0, 0, 1, 0), result.VelocityBlend);
    }

    [Fact]
    public void V4BufferIsNotAReplacementForCrossoverDelay()
    {
        foreach (var current in Enum.GetValues<AlsMovementDirection>())
        {
            Assert.Equal(AlsMovementDirection.Forward, AlsMovementDirectionModel.CalculateQuadrant(current, 75));
            Assert.Equal(AlsMovementDirection.Right, AlsMovementDirectionModel.CalculateQuadrant(current, 75.001f));
        }
        var result = AlsStandingCycle.AdvanceDirection(new AlsStandingCycleState { Direction = AlsCycleDirection.LeftBackward },
            -Vector2.UnitX, 1f / 60, .1f, 0);
        Assert.True(result.WaitingForFeet);
        Assert.Equal(AlsCycleDirection.LeftBackward, result.Direction);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidAimIsRejected(float yaw) => Assert.Throws<ArgumentOutOfRangeException>(() =>
        AlsMovementDirectionModel.Calculate(default, Vector2.UnitY, yaw, AlsGait.Walking, AlsRotationMode.LookingDirection));

    private static JsonDocument ReadFixture() => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "P3", "v4_movement_direction_native.json")));
}
