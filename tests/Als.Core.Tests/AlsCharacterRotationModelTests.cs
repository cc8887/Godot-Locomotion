using GodotAls.Core.Contracts;
using GodotAls.Core.Curves;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsCharacterRotationModelTests
{
    private static AlsCharacterRotationModel Model() => new(new(.01f, 1.5f, 800, 500, 1000, 20, 2, 5, 15, -100, 100, 20, 30, .001f, 0, 300, 1, 3),
        Enumerable.Range(0, 6).Select(_ => new AlsCharacterRotationMovement(1.75f, 3.75f, 6.5f,
            new AlsMovementInputCurve(new AlsCurveKey[] { new(0, 5, 0, 0, AlsCurveInterpolationMode.Linear), new(3, 5, 0, 0, AlsCurveInterpolationMode.Linear) }))).ToArray());
    private static AlsCharacterRotationInput Input() => new(1, 0, 30, 90, -90, 3.75f, true, false,
        AlsMovementStateInput.Grounded, AlsRotationMode.LookingDirection, AlsStance.Standing, AlsGait.Running,
        AlsTimelineAction.None, false, 0, 20, 0);

    [Theory]
    [InlineData(AlsRotationMode.LookingDirection, AlsGait.Running, 50)]
    [InlineData(AlsRotationMode.LookingDirection, AlsGait.Walking, 50)]
    [InlineData(AlsRotationMode.LookingDirection, AlsGait.Sprinting, 90)]
    [InlineData(AlsRotationMode.VelocityDirection, AlsGait.Running, 90)]
    [InlineData(AlsRotationMode.Aiming, AlsGait.Running, 30)]
    public void ChoosesActualCurveControlOrVelocityTarget(AlsRotationMode mode, AlsGait gait, double expected)
    {
        var original = AlsCharacterRotationModel.Initialize(0);
        var input = Input() with { RotationMode = mode, Gait = gait };
        var model = Model(); var result = model.Evaluate(input, original);
        Assert.Equal(expected, result.ActorYaw);
        Assert.Equal(expected, result.History.TargetYaw);
        Assert.Equal(result, model.Evaluate(input, original)); // discarded candidate does not advance target history
        Assert.Equal(0, original.TargetYaw);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void TargetConstantAndActorSmoothAreDistinctStages(int hz)
    {
        var input = Input() with { Delta = 1f / hz, ControlYaw = 170, YawOffset = 0 };
        var result = Model().Evaluate(input, AlsCharacterRotationModel.Initialize(0));
        var target = 500f / hz;
        Assert.InRange(System.Math.Abs(result.History.TargetYaw - target), 0, .00001);
        Assert.InRange(System.Math.Abs(result.ActorYaw - target * (5f / hz)), 0, .00001);
    }

    [Theory]
    [InlineData(AlsRotationMode.LookingDirection, false, 0)]
    [InlineData(AlsRotationMode.LookingDirection, true, 20)]
    [InlineData(AlsRotationMode.Aiming, false, 20)]
    [InlineData(AlsRotationMode.VelocityDirection, true, 20)]
    public void LimitsStationaryAimingAndFirstPersonOnly(AlsRotationMode mode, bool firstPerson, double expected)
    {
        var result = Model().Evaluate(Input() with { Speed = 0, HasMovementInput = false, ControlYaw = 120,
            RotationMode = mode, FirstPerson = firstPerson }, AlsCharacterRotationModel.Initialize(0));
        Assert.Equal(expected, result.ActorYaw); Assert.Equal(expected != 0, result.Limited);
    }

    [Theory]
    [InlineData(-120)] [InlineData(120)]
    public void AppliesCurveAfterLimitAndStoresTheFinalActorRotation(double control)
    {
        var result = Model().Evaluate(Input() with { Delta = 1f / 60, Speed = 0, HasMovementInput = false,
            ControlYaw = control, RotationMode = AlsRotationMode.Aiming, RotationAmount = .5f }, AlsCharacterRotationModel.Initialize(0));
        var expected = System.Math.CopySign(20d / 3, control) + .25;
        Assert.InRange(System.Math.Abs(result.ActorYaw - expected), 0, .00001);
        Assert.Equal(result.ActorYaw, result.History.TargetYaw); Assert.True(result.Limited && result.CurveRotated);
    }

    [Fact]
    public void AirCapturesEntryThenHoldsUntilAimingUpdatesItsOwnHistory()
    {
        var model = Model(); var original = AlsCharacterRotationModel.Initialize(40);
        var input = Input() with { ActorYaw = 50, ControlYaw = 90, MovementState = AlsMovementStateInput.InAir };
        var entry = model.Evaluate(input, original); Assert.Equal(50, entry.ActorYaw);
        var held = model.Evaluate(input with { ActorYaw = 50, ControlYaw = 160, VelocityYaw = -170 }, entry.History);
        Assert.Equal(50, held.ActorYaw);
        var aimed = model.Evaluate(input with { RotationMode = AlsRotationMode.Aiming }, held.History);
        Assert.Equal(90, aimed.ActorYaw); Assert.Equal(90, aimed.History.InAirYaw);
        var released = model.Evaluate(input with { ActorYaw = 90, ControlYaw = -90 }, aimed.History);
        Assert.Equal(90, released.ActorYaw);
    }

    [Fact]
    public void RootMotionGatesMovingRotationAndRollingRequiresActualInput()
    {
        var model = Model(); var original = AlsCharacterRotationModel.Initialize(0);
        Assert.Equal(0, model.Evaluate(Input() with { HasRootMotion = true }, original).ActorYaw);
        var roll = model.Evaluate(Input() with { Action = AlsTimelineAction.Rolling }, original);
        Assert.Equal(-90, roll.ActorYaw); Assert.Equal(AlsCharacterRotationBranch.Rolling, roll.Branch);
        Assert.Equal(0, model.Evaluate(Input() with { Action = AlsTimelineAction.Rolling, HasMovementInput = false }, original).ActorYaw);
        Assert.Equal(0, model.Evaluate(Input() with { Action = AlsTimelineAction.Mantling }, original).ActorYaw);
    }

    [Fact]
    public void RootMotionStillAllowsIdleLimitCurveAndRollingBranches()
    {
        var model = Model(); var history = AlsCharacterRotationModel.Initialize(0);
        var result = model.Evaluate(Input() with { HasRootMotion = true, RotationMode = AlsRotationMode.Aiming,
            ControlYaw = 120, RotationAmount = 1 }, history);
        Assert.Equal(AlsCharacterRotationBranch.Idle, result.Branch);
        Assert.True(result.Limited && result.CurveRotated); Assert.Equal(50, result.ActorYaw);
        var roll = model.Evaluate(Input() with { HasRootMotion = true, Action = AlsTimelineAction.Rolling }, history);
        Assert.Equal(AlsCharacterRotationBranch.Rolling, roll.Branch); Assert.Equal(-90, roll.ActorYaw);
    }

    [Theory]
    [InlineData(.01f, true, false)] [InlineData(.010001f, true, true)]
    [InlineData(1.5f, false, false)] [InlineData(1.50001f, false, true)]
    public void MovingGateUsesStrictThresholdsAndAllowsHighSpeedWithoutInput(float speed, bool input, bool moving)
    {
        var result = Model().Evaluate(Input() with { Speed = speed, HasMovementInput = input, Delta = 1f / 60 },
            AlsCharacterRotationModel.Initialize(0));
        Assert.Equal(moving ? AlsCharacterRotationBranch.MovingLooking : AlsCharacterRotationBranch.Idle, result.Branch);
    }

    [Fact]
    public void LastDirectionHistoryHoldsWithoutMovementAndAirActionDoesNotCapture()
    {
        var model = Model(); var history = AlsCharacterRotationModel.Initialize(35);
        var stopped = model.Evaluate(Input() with { Speed = .01f, HasMovementInput = false, ActorYaw = 35 }, history);
        Assert.Equal(35, stopped.History.LastVelocityYaw); Assert.Equal(35, stopped.History.LastInputYaw);
        var coast = model.Evaluate(Input() with { Speed = 2, HasMovementInput = false, VelocityYaw = 75 }, stopped.History);
        Assert.Equal(75, coast.History.LastVelocityYaw); Assert.Equal(35, coast.History.LastInputYaw);
        var air = model.Evaluate(Input() with { ActorYaw = 70, MovementState = AlsMovementStateInput.InAir,
            Action = AlsTimelineAction.Rolling, Delta = 1f / 60 }, coast.History);
        Assert.Equal(0, air.History.InAirYaw);
        var cleared = model.Evaluate(Input() with { ActorYaw = air.ActorYaw, MovementState = AlsMovementStateInput.InAir,
            Action = AlsTimelineAction.None, Delta = 1f / 60 }, air.History);
        Assert.Equal(0, cleared.History.InAirYaw); // action ending is not another movement-state entry
    }

    [Theory]
    [InlineData(100, .001f)] [InlineData(-100, -.001f)]
    public void InclusiveLimitAndStrictCurveDeadZoneDoNotRotateAtBoundary(double control, float amount)
    {
        var result = Model().Evaluate(Input() with { Speed = 0, HasMovementInput = false, ControlYaw = control,
            RotationMode = AlsRotationMode.Aiming, RotationAmount = amount }, AlsCharacterRotationModel.Initialize(0));
        Assert.Equal(0, result.ActorYaw); Assert.False(result.Limited || result.CurveRotated);
    }

    [Fact]
    public void NativeRotatorTiesAndZeroDeltaPreserveSourceSemantics()
    {
        Assert.Equal(180, AlsCharacterRotationMath.Normalize(-180));
        Assert.Equal(1, AlsCharacterRotationMath.Constant(0, -180, 1, 1));
        Assert.Equal(700, AlsCharacterRotationMath.Constant(0, 700, 1, 0));
        Assert.Equal(0, AlsCharacterRotationMath.Smooth(0, 700, 0, 0));
        Assert.Equal(0, AlsCharacterRotationMath.Constant(0, 700, 0, 0));
        Assert.Equal(0, AlsCharacterRotationMath.MapClamped(5e-10, 0, 1e-9, 0, 10));
        Assert.Equal(10, AlsCharacterRotationMath.MapClamped(1e-9, 0, 1e-9, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsCharacterRotationMath.Smooth(0, double.NaN, 1, 5));
    }
}
