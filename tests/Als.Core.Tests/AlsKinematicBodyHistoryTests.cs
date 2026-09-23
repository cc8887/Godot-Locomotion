using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsKinematicBodyHistoryTests
{
    private static AlsFrameIdentity Id(long frame) => new(frame, 7, 3);
    private static AlsPrecisePose Pose(double x) => AlsPrecisePose.Identity with { Position = new(x, 0, 0) };

    [Fact]
    public void PendingActivationUsesNewPoseButOldVelocityAndTeleportClearsImmediately()
    {
        var history = new AlsKinematicBodyHistory(Id(1), [Pose(0)]);
        history.PrepareTargets(Id(2), [Pose(3)], .02); history.Commit(Id(2));
        var output = new AlsIslandBodyState[1];
        history.PrepareTargets(Id(3), [Pose(20)], .02);
        Assert.Equal(Id(2), history.CopyPendingActivation(Id(3), output));
        Assert.Equal(Pose(20), output[0].Actor);
        Assert.Equal(150, output[0].Velocity.Linear.X); // Not the pending 850 cm/s.
        history.CopyCommitted(Id(2), output); Assert.Equal(Pose(3), output[0].Actor);
        history.Cancel();
        Assert.Throws<ArgumentException>(() => history.CopyPendingActivation(Id(3), output));
        history.PrepareTargets(Id(3), [Pose(10000)], .02, teleport: true);
        history.CopyPendingActivation(Id(3), output);
        Assert.Equal(Pose(10000), output[0].Actor); Assert.Equal(default, output[0].Velocity);
        history.CopyCommitted(Id(2), output); Assert.Equal(150, output[0].Velocity.Linear.X);
        var saved = output.ToArray();
        Assert.Throws<ArgumentException>(() => history.CopyPendingActivation(new(3, 7, 4), output));
        Assert.Equal(saved, output);
        history.Commit(Id(3));
        Assert.Throws<ArgumentException>(() => history.CopyPendingActivation(Id(3), output));
    }

    [Fact]
    public void QueuedTargetDoesNotPublishUntilPhysicalStepCommits()
    {
        var history = new AlsKinematicBodyHistory(Id(1), [Pose(0), Pose(10)]);
        var output = new AlsIslandBodyState[2];
        history.PrepareTargets(Id(2), [Pose(3), Pose(16)], .02);
        history.CopyCommitted(Id(1), output);
        Assert.Equal(Vector3.Zero, output[0].Velocity.Linear);
        Assert.Throws<ArgumentException>(() => history.CopyCommitted(Id(2), output));
        history.Commit(Id(2)); history.CopyCommitted(Id(2), output);
        Assert.Equal(new Vector3(150, 0, 0), output[0].Velocity.Linear);
        Assert.Equal(new Vector3(300, 0, 0), output[1].Velocity.Linear);
        Assert.Throws<ArgumentException>(() => history.CopyCommitted(Id(1), output));
    }

    [Fact]
    public void CancelAndLateInvalidTargetPreserveHistoryForRetry()
    {
        var history = new AlsKinematicBodyHistory(Id(1), [Pose(0), Pose(10)]);
        var output = new AlsIslandBodyState[2];
        history.PrepareTargets(Id(2), [Pose(3), Pose(16)], .02); history.Cancel();
        Assert.Throws<InvalidOperationException>(() => history.Commit(Id(2)));
        Assert.Throws<ArgumentException>(() => history.PrepareTargets(Id(2), [Pose(3), Pose(double.NaN)], .02));
        Assert.Throws<InvalidOperationException>(() => history.Commit(Id(2)));
        history.CopyCommitted(Id(1), output); Assert.Equal(Pose(10), output[1].Actor);
        history.PrepareTargets(Id(2), [Pose(3), Pose(16)], .02); history.Commit(Id(2));
        history.CopyCommitted(Id(2), output); Assert.Equal(300, output[1].Velocity.Linear.X);
    }

    [Fact]
    public void MissingTargetStepAndTeleportZeroVelocityWithoutInventingMotion()
    {
        var history = new AlsKinematicBodyHistory(Id(1), [Pose(0)]);
        var output = new AlsIslandBodyState[1];
        history.PrepareTargets(Id(2), [Pose(3)], .02); history.Commit(Id(2));
        history.PrepareNoTarget(Id(3)); history.CopyCommitted(Id(2), output);
        Assert.Equal(150, output[0].Velocity.Linear.X);
        history.Commit(Id(3)); history.CopyCommitted(Id(3), output);
        Assert.Equal(Pose(3), output[0].Actor); Assert.Equal(default, output[0].Velocity);
        var rotated = Pose(6) with { Rotation = AlsQuaternion.FromAxisAngle(Vector3.UnitY, .1f) };
        history.PrepareTargets(Id(4), [rotated], .02); history.Commit(Id(4));
        history.CopyCommitted(Id(4), output);
        Assert.NotEqual(Vector3.Zero, output[0].Velocity.Linear);
        Assert.NotEqual(Vector3.Zero, output[0].Velocity.Angular);
        history.PrepareTargets(Id(5), [Pose(10000)], .02, teleport: true); history.Commit(Id(5));
        history.CopyCommitted(Id(5), output); Assert.Equal(Pose(10000), output[0].Actor);
        Assert.Equal(default, output[0].Velocity);
        history.PrepareTargets(Id(6), [Pose(10003)], .02); history.Commit(Id(6));
        history.CopyCommitted(Id(6), output); Assert.Equal(150, output[0].Velocity.Linear.X);
    }

    [Fact]
    public void WrongLifetimeAndSkippedPhysicsStepsAreRejected()
    {
        var history = new AlsKinematicBodyHistory(Id(1), [Pose(0)]);
        Assert.Throws<ArgumentException>(() => history.PrepareNoTarget(new(2, 7, 4)));
        Assert.Throws<ArgumentException>(() => history.PrepareNoTarget(new(2, 8, 3)));
        Assert.Throws<ArgumentException>(() => history.PrepareNoTarget(Id(3)));
        Assert.Equal(Id(1), history.CommittedIdentity);
    }
}
