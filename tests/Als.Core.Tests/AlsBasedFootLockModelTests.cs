using System.Numerics;
using System.Runtime.CompilerServices;
using GodotAls.Core.Locomotion;
using M = System.Math;

namespace GodotAls.Core.Tests;

public sealed class AlsBasedFootLockModelTests
{
    private static readonly AlsBasedFootLockModel Model = new();
    private static AlsPrecisePose Pose(double x = 0, double y = 0, double z = 0, double yaw = 0) =>
        new(new(x, y, z), new(0, 0, M.Sin(yaw * M.PI / 360), M.Cos(yaw * M.PI / 360)), AlsDoubleVector.One);
    private static AlsBasedFootLockInput Input => new(1f / 60, 1, 1, true, false, true, false, 1,
        0, Pose(), Pose(), Pose(30, 10, -90), AlsQuaternion.Identity, new(1, 0, 0));
    private static void Near(AlsDoubleVector expected, AlsDoubleVector actual, double tolerance = .0001) =>
        Assert.True((expected - actual).LengthSquared < tolerance * tolerance, $"Expected {expected}, actual {actual}");
    private static void NearRotation(AlsQuaternion expected, AlsQuaternion actual, double tolerance = 1e-5) =>
        Assert.True(1 - M.Abs(AlsQuaternion.Dot(expected.Normalized(), actual.Normalized())) < tolerance);

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void GravityTwistPreservesContactOrientationAfterGroundAlignment(int hz)
    {
        var model = new AlsBasedFootLockModel(new(true, 180, 180)
            { BaseRotationMode = AlsFootLockBaseRotationMode.GravityTwist });
        var original = new AlsBasedFootLockModel(new(true, 180, 180));
        var swing0 = AlsQuaternion.FromAxisAngle(Vector3.UnitY, .07f);
        var base0 = Pose() with { Rotation = swing0 * Pose(yaw: 20).Rotation };
        var input = Input with { BaseIdentity = 1, BaseTransform = base0, DeltaTime = 1f / hz };
        var state = model.Evaluate(AlsBasedFootLockState.Empty, input);
        var native = original.Evaluate(AlsBasedFootLockState.Empty, input);
        // A material contact frame is rigidly attached to the platform. This
        // invariant includes the downstream normal alignment, not just locking.
        var contact0 = swing0 * state.WorldLock.Rotation;
        var anchor = state.BaseLock.Position;
        var positiveControlDifference = 0d;
        for (var frame = 1; frame <= hz * 2; frame++)
        {
            var t = (double)frame / hz;
            var axis = Vector3.Normalize(new Vector3((float)M.Sin(t), (float)M.Cos(t), 0));
            var swing = AlsQuaternion.FromAxisAngle(axis, (float)(.07 + .1 * M.Sin(t)));
            var basis = Pose(t * 2, -t, t * 3) with { Rotation = swing * Pose(yaw: 20 + t * 8).Rotation };
            input = input with { BaseTransform = basis };
            var next = model.Evaluate(state, input);
            var expected = basis.Rotation * base0.Rotation.Conjugate() * contact0;
            NearRotation(expected, swing * next.WorldLock.Rotation, 1e-10);
            Near(anchor.Rotate(basis.Rotation) + basis.Position, next.WorldLock.Position);
            Assert.False(next.ThighConstrained); Assert.False(next.FootConstrained);
            Assert.Equal(next, model.Evaluate(state, input));
            native = original.Evaluate(native, input);
            positiveControlDifference = M.Max(positiveControlDifference,
                1 - M.Abs(AlsQuaternion.Dot(expected.Normalized(), (swing * native.WorldLock.Rotation).Normalized())));
            state = next;
        }
        Assert.True(positiveControlDifference > .001, "Original full rotation must expose the extra tilt.");
    }

    [Fact]
    public void GravityTwistLeavesPureYawHistoryExactlyEqualToOriginal()
    {
        var model = new AlsBasedFootLockModel(AlsBasedFootLockSettings.Default with
            { BaseRotationMode = AlsFootLockBaseRotationMode.GravityTwist });
        var state = AlsBasedFootLockState.Empty;
        var native = state;
        for (var frame = 0; frame < 360; frame++)
        {
            var input = Input with { BaseIdentity = frame < 180 ? 1ul : 2ul,
                BaseTransform = Pose(frame * .2, 0, frame * .01, frame * .5),
                SecondsSinceTeleport = frame is >= 120 and < 130 ? .1 : 1,
                LockAmount = frame is >= 60 and < 80 ? .7f : 1 };
            state = model.Evaluate(state, input); native = Model.Evaluate(native, input);
            Assert.Equal(native, state);
        }
    }

    [Fact]
    public void GravityTwistRebasesTiltedBaseSwitchAndTeleportWithoutJumping()
    {
        var model = new AlsBasedFootLockModel(new(true, 180, 180)
            { BaseRotationMode = AlsFootLockBaseRotationMode.GravityTwist });
        var input = Input with { BaseIdentity = 1,
            BaseTransform = Pose(yaw: 30) with { Rotation = AlsQuaternion.FromAxisAngle(Vector3.UnitX, .15f) * Pose(yaw: 30).Rotation } };
        var state = model.Evaluate(AlsBasedFootLockState.Empty, input);
        input = input with { BaseIdentity = 2, BaseTransform = Pose(100, -50, 20) with
            { Rotation = AlsQuaternion.FromAxisAngle(Vector3.UnitY, -.2f) * Pose(yaw: -40).Rotation } };
        var switched = model.Evaluate(state, input);
        Near(state.WorldLock.Position, switched.WorldLock.Position);
        NearRotation(state.WorldLock.Rotation, switched.WorldLock.Rotation, 1e-10);
        input = input with { SecondsSinceTeleport = .1, ComponentTransform = Pose(500, 0, 0, 10) };
        var moved = model.Evaluate(switched, input);
        var expected = AlsPrecisePose.Compose(switched.ComponentLock, input.ComponentTransform);
        Near(expected.Position, moved.WorldLock.Position);
        NearRotation(expected.Rotation, moved.WorldLock.Rotation, 1e-10);
        var singular = model.Evaluate(AlsBasedFootLockState.Empty, input with
            { BaseTransform = Pose() with { Rotation = new(1, 0, 0, 0) } });
        singular.WorldLock.Validate();
        Assert.Throws<ArgumentException>(() => new AlsBasedFootLockModel(AlsBasedFootLockSettings.Default with
            { BaseRotationMode = (AlsFootLockBaseRotationMode)99 }));
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void RotatingAndTranslatingBaseKeepsItsAnchorAcrossAnimatedTargets(int hz)
    {
        var state = AlsBasedFootLockState.Empty;
        var model = new AlsBasedFootLockModel(new(true, 180, 180));
        var input = Input with { BaseIdentity = ulong.MaxValue, DeltaTime = 1f / hz };
        state = model.Evaluate(state, input);
        var anchor = state.BaseLock;
        for (var frame = 1; frame <= hz * 6; frame++)
        {
            var t = (double)frame / hz;
            var basis = Pose(25 * t, -12 * t, 3 * t, 20 * t);
            // Component follows only the platform translation. The target
            // animation deliberately varies; recapturing it would break this.
            input = input with { BaseTransform = basis, ComponentTransform = Pose(25 * t, -12 * t, 3 * t),
                TargetWorld = Pose(30 + 25 * t + 4 * M.Sin(t * 10), 10 - 12 * t, -90 + 3 * t) };
            var next = model.Evaluate(state, input);
            var angle = 20 * t * M.PI / 180;
            Near(new(25 * t + 30 * M.Cos(angle) - 10 * M.Sin(angle),
                -12 * t + 30 * M.Sin(angle) + 10 * M.Cos(angle), -90 + 3 * t), next.WorldLock.Position);
            Assert.Equal(anchor, next.BaseLock);
            Assert.Equal(1, next.Amount);
            Assert.Equal(next, model.Evaluate(state, input)); // discarded candidate / same-frame retry
            state = next;
        }
    }

    [Fact]
    public void RisingPartialCurveCannotBlendIntoLockAndFullCurveUsesPreviousFinalPose()
    {
        var state = Model.Evaluate(AlsBasedFootLockState.Empty, Input with { LockAmount = 0 });
        state = Model.Evaluate(state, Input with { LockAmount = .8f, TargetWorld = Pose(35, 10, -90) });
        Assert.Equal(0, state.Amount);
        state = Model.Evaluate(state, Input with { TargetWorld = Pose(50, 10, -90) });
        Near(new(35, 10, -90), state.WorldLock.Position);
        state = Model.Evaluate(state, Input with { LockAmount = .7f });
        Assert.Equal(.7f, state.Amount);
        state = Model.Evaluate(state, Input with { LockAmount = .85f });
        Assert.Equal(.7f, state.Amount);
    }

    [Fact]
    public void AlmostFullRelockKeepsWorldAnchorOnStaticGround()
    {
        var state = Model.Evaluate(AlsBasedFootLockState.Empty, Input);
        var anchor = state.WorldLock;
        state = Model.Evaluate(state, Input with { LockAmount = .95f, TargetWorld = Pose(90, 10, -90) });
        state = Model.Evaluate(state, Input with { TargetWorld = Pose(95, 10, -90) });
        Assert.Equal(anchor, state.WorldLock);
        Assert.Equal(1, state.Amount);
    }

    [Theory]
    [InlineData(30, true)] [InlineData(60, true)] [InlineData(120, true)]
    [InlineData(30, false)] [InlineData(60, false)] [InlineData(120, false)]
    public void MovementAndAirReleaseWithoutDependingOnAnAnimationCurve(int hz, bool moving)
    {
        var state = Model.Evaluate(AlsBasedFootLockState.Empty, Input);
        var input = Input with { DeltaTime = 1f / hz, MovingSmooth = moving, Grounded = moving };
        var first = Model.Evaluate(state, input);
        Assert.Equal(1f - input.DeltaTime * (moving ? 5f : .6f), first.Amount);
        var frames = (int)M.Ceiling(hz / (moving ? 5.0 : .6)) + 2;
        for (var frame = 0; frame < frames; frame++) state = Model.Evaluate(state, input);
        Assert.Equal(0, state.Amount);
        Assert.Equal(AlsPrecisePose.Identity, state.WorldLock);
        Near(Input.TargetWorld.Position, state.FinalComponent.Position);
        var cold = Model.Evaluate(AlsBasedFootLockState.Empty, input);
        Assert.Equal(0, cold.Amount);
    }

    [Fact]
    public void BaseSwitchPreservesWorldFootAndRemovalDoesNotApplyAnUnrelatedTransform()
    {
        var input = Input with { BaseIdentity = 1 };
        var state = Model.Evaluate(AlsBasedFootLockState.Empty, input);
        input = input with { BaseIdentity = 2, BaseTransform = Pose(1000, -200, 5, 70) };
        state = Model.Evaluate(state, input);
        Near(Input.TargetWorld.Position, state.WorldLock.Position);
        input = input with { BaseTransform = input.BaseTransform with { Position = new(1003, -200, 5) } };
        state = Model.Evaluate(state, input);
        Near(new(33, 10, -90), state.WorldLock.Position);
        state = Model.Evaluate(state, input with { BaseIdentity = 0, BaseTransform = Pose(-9999, 5, 800, 180) });
        Near(new(33, 10, -90), state.WorldLock.Position);
        Assert.Equal(AlsPrecisePose.Identity, state.BaseLock);
    }

    [Theory]
    [InlineData(0)] [InlineData(.1)] [InlineData(.2)]
    public void ExplicitTeleportWindowPreservesComponentLockAndRebases(double elapsed)
    {
        var input = Input with { BaseIdentity = 7 };
        var state = Model.Evaluate(AlsBasedFootLockState.Empty, input);
        input = input with { SecondsSinceTeleport = elapsed, ComponentTransform = Pose(500, 0, 0), BaseTransform = Pose(500, 0, 0) };
        state = Model.Evaluate(state, input);
        Near(new(530, 10, -90), state.WorldLock.Position);
        Near(new(30, 10, -90), state.BaseLock.Position);
        // The smoothing period may span several movement updates.
        state = Model.Evaluate(state, input with { ComponentTransform = Pose(502, 0, 0), BaseTransform = Pose(502, 0, 0) });
        Near(new(532, 10, -90), state.WorldLock.Position);
    }

    [Fact]
    public void TeleportWindowExpiryAndOrdinaryMovementKeepWorldAnchor()
    {
        var state = Model.Evaluate(AlsBasedFootLockState.Empty, Input);
        var model = new AlsBasedFootLockModel(new(true, 180, 180));
        state = model.Evaluate(state, Input with { SecondsSinceTeleport = .201, ComponentTransform = Pose(100, 0, 0) });
        Near(new(30, 10, -90), state.WorldLock.Position);
    }

    [Fact]
    public void ThighLimitRebasesPositionAndFootLimitOnlyChangesWorldOutputRotation()
    {
        var input = Input with { BaseIdentity = 1, TargetWorld = Pose(10, 0, -90) };
        var state = Model.Evaluate(AlsBasedFootLockState.Empty, input);
        input = input with { BaseTransform = Pose(yaw: 120) };
        state = Model.Evaluate(state, input);
        Near(new(0, 10, -90), state.WorldLock.Position);
        NearRotation(Pose(yaw: 40).Rotation, state.WorldLock.Rotation);
        NearRotation(Pose(yaw: 90).Rotation, state.ComponentLock.Rotation);
        NearRotation(Pose(yaw: -30).Rotation, state.BaseLock.Rotation);
        var again = Model.Evaluate(state, input);
        Near(state.WorldLock.Position, again.WorldLock.Position);
        NearRotation(state.WorldLock.Rotation, again.WorldLock.Rotation);
    }

    [Fact]
    public void PelvisRotationDeterminesThighLimitAndNegativeFootTwistIsClamped()
    {
        var input = Input with { BaseIdentity = 1, TargetWorld = Pose(10, 0, -90) };
        var state = Model.Evaluate(AlsBasedFootLockState.Empty, input);
        state = Model.Evaluate(state, input with { BaseTransform = Pose(yaw: -120), PelvisRotation = Pose(yaw: -120).Rotation });
        Near(new(-5, -M.Sqrt(75), -90), state.WorldLock.Position);
        NearRotation(Pose(yaw: -40).Rotation, state.WorldLock.Rotation);
    }

    [Fact]
    public void DegenerateHorizontalFootVectorDoesNotInventAThighDirection()
    {
        var model = new AlsBasedFootLockModel(new(true, 0, 40));
        var input = Input with { TargetWorld = Pose(1e-7, -1e-7, -90) };
        var state = model.Evaluate(AlsBasedFootLockState.Empty, input);
        Near(input.TargetWorld.Position, state.WorldLock.Position, 1e-10);
    }

    [Fact]
    public void ComponentScaleIsAppliedButBaseScaleIsNot()
    {
        var input = Input with { BaseIdentity = 1, ComponentTransform = Pose() with { Scale = new(2, 2, 2) },
            BaseTransform = Pose() with { Scale = new(4, 4, 4) } };
        var state = Model.Evaluate(AlsBasedFootLockState.Empty, input);
        Near(new(15, 5, -45), state.ComponentLock.Position);
        Near(Input.TargetWorld.Position, state.BaseLock.Position);
    }

    [Fact]
    public void InvalidPoseSkipsUpdateAndBecomingValidUsesNewTarget()
    {
        var state = Model.Evaluate(AlsBasedFootLockState.Empty, Input);
        Assert.Equal(state, Model.Evaluate(state, default));
        state = Model.Evaluate(state, Input with { BecameValid = true, TargetWorld = Pose(45, 10, -90) });
        Near(new(45, 10, -90), state.WorldLock.Position);
    }

    [Fact]
    public void DisabledIkOrSettingClearsAnchorsAndNonfiniteInputIsRejected()
    {
        var state = Model.Evaluate(AlsBasedFootLockState.Empty, Input with { BaseIdentity = 1 });
        Assert.Equal(0, Model.Evaluate(state, Input with { IkAmount = 0 }).Amount);
        var disabled = new AlsBasedFootLockModel(AlsBasedFootLockSettings.Default with { AllowLock = false });
        var cleared = disabled.Evaluate(state, Input);
        Assert.Equal(0, cleared.Amount);
        Assert.Equal(AlsPrecisePose.Identity, cleared.WorldLock);
        Assert.Equal(AlsPrecisePose.Identity, cleared.BaseLock);
        Assert.Throws<ArgumentException>(() => Model.Evaluate(state, Input with { DeltaTime = float.NaN }));
        Assert.Throws<ArgumentException>(() => Model.Evaluate(state, Input with { TargetWorld = Pose(double.NaN) }));
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsBasedFootLockInput>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsBasedFootLockState>());
    }
}
