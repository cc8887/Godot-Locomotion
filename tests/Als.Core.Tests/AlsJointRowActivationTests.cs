using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsJointRowActivationTests
{
    [Fact]
    public void FasterPhysicsKeepsSubMilliradianErrorsThatSixtyHzIgnores()
    {
        var slow=AlsJointRowActivation.AngleTolerance(1d/30);
        var normal=AlsJointRowActivation.AngleTolerance(1d/60);
        var fast=AlsJointRowActivation.AngleTolerance(1d/120);
        Assert.Equal(slow,normal);Assert.Equal(normal/4,fast);
        foreach(var sign in new[]{-1,1})
        {
            Assert.False(AlsJointRowActivation.SoftLimitActive(sign*.5005,.5,normal));
            Assert.True(AlsJointRowActivation.SoftLimitActive(sign*.5005,.5,fast));
            Assert.False(AlsJointRowActivation.DriveActive(true,sign*.0005,37500,0,normal));
            Assert.True(AlsJointRowActivation.DriveActive(true,sign*.0005,37500,0,fast));
        }
    }

    [Fact]
    public void DampingOnlyDriveRemainsActiveAtTargetButLockedAxisDoesNot()
    {
        var tolerance=AlsJointRowActivation.AngleTolerance(1d/60);
        Assert.True(AlsJointRowActivation.DriveActive(true,0,0,1.5,tolerance));
        Assert.True(AlsJointRowActivation.DriveActive(true,0,75,1.5,tolerance));
        Assert.False(AlsJointRowActivation.DriveActive(false,1,75,1.5,tolerance));
        Assert.False(AlsJointRowActivation.DriveActive(true,1,0,0,tolerance));
        Assert.False(AlsJointRowActivation.DriveActive(true,tolerance,75,0,tolerance));
        Assert.False(AlsJointRowActivation.SoftLimitActive(.25,.5,tolerance));
        Assert.False(AlsJointRowActivation.SoftLimitActive(.5,.5,tolerance));
    }

    [Fact]
    public void NearHalfTurnSwingUsesNativeDriveErrorRatherThanLimitAngle()
    {
        // At pi swing, the native sine approximation is zero despite a large
        // decomposed angle. Jolt's quaternion motor must not choose activation.
        var child=new AlsQuaternion(0,1,0,0);
        var error=AlsJointAngularKinematics.SwingTwistDriveError(AlsQuaternion.Identity,child,AlsQuaternion.Identity);
        var angle=AlsJointAngularKinematics.Evaluate(AlsQuaternion.Identity,child).Angles.Y;
        Assert.True(angle>3);Assert.Equal(0,error.Y);
        Assert.False(AlsJointRowActivation.DriveActive(true,error.Y,75,0,.001));
        Assert.True(AlsJointRowActivation.SoftLimitActive(angle,.5,.001));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidStepIsRejected(double dt)
        => Assert.Throws<ArgumentOutOfRangeException>(()=>AlsJointRowActivation.AngleTolerance(dt));
}
