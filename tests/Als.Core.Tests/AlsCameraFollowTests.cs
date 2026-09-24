using GodotAls.Core.Camera;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsCameraFollowTests
{
    private static AlsCameraFollowSettings Settings => new(200, 80, 100, 15, new(0, 0, 40), true, .2f);
    private static AlsCameraFollowCurves Curves => new(new(0, 0, 40), new(-300, 60, 0), .1f, .1f, .1f, .1f, 0, 0);
    private static AlsCameraFollowInput Input => new(1f / 60, true, default, default, new(0, 0, 180), new(10, 0, 170),
        new(0, 30, 140), false, default, AlsQuaternion.Identity, 1, 0, "", false, default, AlsQuaternion.Identity, false, 90, 0);
    private static AlsCameraTraceResponse Miss(AlsCameraTraceRequest q) => new(q.Start, q.End);

    [Fact]
    public void InitialSocketsOffsetsAndDetachedRootUseNativeWorldAxes()
    {
        AlsCameraTraceRequest query = default;
        var state = AlsCameraFollow.Step(AlsCameraFollowState.Initial, Input, Settings, Curves,
            q => { query = q; return Miss(q); });
        Assert.Equal(new AlsDoubleVector(0, 0, 90), state.PivotTarget);
        Assert.Equal(new AlsDoubleVector(-300, 60, 130), state.Location);
        Assert.Equal(Input.ShoulderSocket, query.Start); Assert.Equal(15, query.Radius);
        var detached = Input with { DetachedRootPivot = true, CapsuleBottom = new(0, 0, 20),
            FirstPivotSocket = new(999, 999, 999), MeshScale = 2 };
        state = AlsCameraFollow.Step(AlsCameraFollowState.Initial, detached, Settings, Curves with { TraceOverride = 1 },
            q => { query = q; return Miss(q); });
        Assert.Equal(new AlsDoubleVector(0, 0, 100), state.PivotTarget);
        Assert.Equal(new AlsDoubleVector(-600, 120, 180), state.Location);
        Assert.Equal(new AlsDoubleVector(0, 0, 220), query.Start); Assert.Equal(30, query.Radius);
    }

    [Fact]
    public void FullFirstPersonBypassesTraceLagAndThirdPersonFovClamp()
    {
        var input = Input with { View = new(45, 90, 0), OverrideFov = true, FovOverride = 180, FovOffset = 10 };
        var state = AlsCameraFollow.Step(AlsCameraFollowState.Initial, input, Settings, Curves with { FirstPersonOverride = 1 },
            _ => throw new InvalidOperationException("First person must not query."));
        Assert.Equal(input.FirstPersonSocket, state.Location); Assert.Equal(input.View, state.Rotation);
        Assert.Equal(180, state.Fov); Assert.Equal(state.PivotTarget, state.PivotLag);
        var mixed = AlsCameraFollow.Step(AlsCameraFollowState.Initial, Input with { FovOffset = 10 }, Settings,
            Curves with { FirstPersonOverride = .5f }, Miss);
        Assert.Equal(new AlsDoubleVector(-145, 30, 150), mixed.Location); Assert.Equal(100, mixed.Fov);
    }

    [Fact]
    public void MovingBaseTransportsHistoryAndChangingBaseRebasesWithoutJump()
    {
        var input = Input with { BaseId = 1, RelativeBaseRotation = true };
        var previous = AlsCameraFollow.Step(AlsCameraFollowState.Initial, input, Settings, Curves, Miss);
        input = input with { Delta = 0, BaseLocation = new(100, 0, 0), BaseRotation = AlsCameraMath.Quaternion(new(0, 90, 0)),
            View = new(0, 90, 0), FirstPivotSocket = new(100, 0, 0), SecondPivotSocket = new(100, 0, 180) };
        var moved = AlsCameraFollow.Step(previous, input, Settings, Curves, Miss);
        Near(new(100, 0, 90), moved.PivotLag); Near(new(40, -300, 130), moved.Location);
        Assert.Equal(90, moved.Rotation.Yaw, 9);
        var switched = AlsCameraFollow.Step(moved, input with { BaseId = 2, BaseBone = "other", BaseLocation = new(1000, 0, 0) }, Settings, Curves, Miss);
        Near(moved.Location, switched.Location); Near(moved.PivotLag, switched.PivotLag);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void CollisionAndTeleportCandidatesRetryWithoutChangingHistory(int hz)
    {
        var input = Input with { Delta = 1f / hz };
        var previous = AlsCameraFollow.Step(AlsCameraFollowState.Initial, input, Settings, Curves, Miss);
        AlsCameraTraceResponse Hit(AlsCameraTraceRequest q) => new(q.Start, q.Start + (q.End - q.Start) * .25);
        var candidate = AlsCameraFollow.Step(previous, input, Settings, Curves, Hit);
        Assert.Equal(.25f, candidate.TraceRatio);
        Assert.Throws<InvalidOperationException>(() => AlsCameraFollow.Step(previous, input, Settings, Curves,
            _ => throw new InvalidOperationException("Query failed")));
        Assert.Equal(candidate, AlsCameraFollow.Step(previous, input, Settings, Curves, Hit));
        var extending = AlsCameraFollow.Step(candidate, input, Settings, Curves, Miss);
        Assert.InRange(extending.TraceRatio, .25f, .4f);
        var teleported = input with { FirstPivotSocket = new(500, 0, 0), SecondPivotSocket = new(500, 0, 180), View = new(0, 90, 0) };
        var reset = AlsCameraFollow.Step(candidate, teleported, Settings, Curves, Miss);
        Assert.Equal(new AlsDoubleVector(500, 0, 90), reset.PivotLag); Assert.Equal(teleported.View, reset.Rotation); Assert.Equal(1, reset.TraceRatio);
    }

    [Fact]
    public void AdjustedTraceStartControlsDistanceSmoothing()
    {
        var previous = AlsCameraFollow.Step(AlsCameraFollowState.Initial, Input, Settings, Curves, Miss);
        var adjusted = new AlsDoubleVector(20, 40, 150);
        AlsDoubleVector resolved = default;
        var state = AlsCameraFollow.Step(previous, Input, Settings, Curves, q =>
        {
            resolved = adjusted + (q.End - adjusted) * .5;
            return new(adjusted, resolved);
        });
        Assert.Equal(.5f, state.TraceRatio); Near(resolved, state.Location);
    }

    [Fact]
    public void RotatorConversionRetainsOrientationAndRejectsInvalidInputs()
    {
        foreach (var rotation in new[] { new AlsAimingRotation(10, 170, -30), new(-90, 60, 40), new(90, -20, 15) })
        {
            var q = AlsCameraMath.Quaternion(rotation); var recovered = AlsCameraMath.Quaternion(AlsCameraMath.Rotator(q));
            Assert.InRange(System.Math.Abs(AlsQuaternion.Dot(q, recovered)), 1 - 1e-12, 1 + 1e-12);
        }
        Assert.Throws<ArgumentException>(() => AlsCameraFollow.Step(AlsCameraFollowState.Initial, Input with { MeshScale = 0 }, Settings, Curves, Miss));
        Assert.Throws<ArgumentException>(() => AlsCameraFollow.Step(AlsCameraFollowState.Initial, Input, Settings, Curves,
            q => new(q.Start, new(double.NaN, 0, 0))));
    }
    private static void Near(AlsDoubleVector expected, AlsDoubleVector actual)
    { Assert.InRange((expected - actual).LengthSquared, 0, 1e-18); }
}
