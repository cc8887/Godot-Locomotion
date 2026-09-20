using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsJointAngularKinematicsTests
{
    [Fact]
    public void MixedTwistAndSwingCanSatisfyRotationLockWithNonzeroPyramidAngle()
    {
        // A unit rotation in the XZ quaternion plane satisfies the native Y lock.
        // Its decomposed swing still has a Y component when twist and Z coexist.
        var child = new AlsQuaternion(.2, 0, .3, System.Math.Sqrt(.87));
        Assert.True(System.Math.Abs(AlsJointAngularKinematics.Evaluate(AlsQuaternion.Identity, child).Angles.Y) > .1);
        Assert.Equal(0, AlsJointAngularKinematics.RotationLockResidualAngles(AlsQuaternion.Identity, child).Y);
        var world = new AlsQuaternion(.1, -.3, .2, .9).Normalized();
        var moved = AlsJointAngularKinematics.RotationLockResidualAngles(world, -(world * child));
        Assert.InRange(moved.Y, 0, 1e-14);
        Assert.InRange((moved - AlsJointAngularKinematics.RotationLockResidualAngles(AlsQuaternion.Identity, child)).LengthSquared, 0, 1e-28);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RotationLockResidualRetainsSingleAxisAngularTolerance(int axis)
    {
        var direction = axis == 0 ? System.Numerics.Vector3.UnitX : axis == 1 ? System.Numerics.Vector3.UnitY : System.Numerics.Vector3.UnitZ;
        foreach (var angle in new[] { -.4f, .4f, 3f })
        {
            var residual = AlsJointAngularKinematics.RotationLockResidualAngles(AlsQuaternion.Identity, AlsQuaternion.FromAxisAngle(direction, angle));
            for (var i = 0; i < 3; i++) Assert.InRange(System.Math.Abs(residual[i] - (i == axis ? System.Math.Abs((double)angle) : 0)), 0, 1e-14);
        }
    }

    [Fact]
    public void DegenerateHalfTurnRetainsFiniteSwingAndRotationLockAxes()
    {
        var k = AlsJointAngularKinematics.Evaluate(AlsQuaternion.Identity,new(0,1,0,0));
        Assert.Equal(AlsQuaternion.Identity,k.Twist);
        Assert.Equal(new AlsDoubleVector(0,System.Math.PI,0),k.Angles);
        Assert.Equal(new AlsDoubleVector(1e-8f,0,-.5),k.LockedX);
        Assert.Equal(new AlsDoubleVector(0,1e-8f,0),k.LockedY);
        Assert.Equal(new AlsDoubleVector(.5,0,1e-8f),k.LockedZ);
    }

    [Fact]
    public void DriveUsesTargetFrameAndShortestQuaternionArc()
    {
        var parent = new AlsQuaternion(.1,.2,.3,.9).Normalized();
        var target = new AlsQuaternion(.2,-.3,.1,.8).Normalized();
        var child = parent*target;
        Assert.InRange(AlsJointAngularKinematics.SwingTwistDriveError(parent,child,target).LengthSquared,0,1e-28);
        var displaced = child * new AlsQuaternion(.1,.2,-.3,.9).Normalized();
        var a = AlsJointAngularKinematics.SwingTwistDriveError(parent,displaced,target);
        var b = AlsJointAngularKinematics.SwingTwistDriveError(-parent,-displaced,-target);
        Assert.InRange((a-b).LengthSquared,0,1e-28);
    }

    [Fact]
    public void InvalidInputsAreRejectedAndValidHotPathDoesNotAllocate()
    {
        Assert.Throws<ArgumentException>(() => AlsJointAngularKinematics.Evaluate(default,AlsQuaternion.Identity));
        Assert.Throws<ArgumentException>(() => AlsJointAngularKinematics.Evaluate(new(double.NaN,0,0,1),AlsQuaternion.Identity));
        Assert.Throws<ArgumentException>(() => AlsJointAngularKinematics.SwingTwistDriveError(AlsQuaternion.Identity,AlsQuaternion.Identity,default));
        Assert.Throws<ArgumentException>(() => AlsJointAngularKinematics.RotationLockResidualAngles(default,AlsQuaternion.Identity));
        var p = new AlsQuaternion(.1,.2,.3,.9).Normalized();
        var c = new AlsQuaternion(.3,-.1,.2,.8).Normalized();
        double checksum = 0;
        for (var i = 0; i < 1000; i++) checksum += AlsJointAngularKinematics.Evaluate(p,c).Angles.X;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++)
        {
            checksum += AlsJointAngularKinematics.Evaluate(p,c).Angles.Y;
            checksum += AlsJointAngularKinematics.SwingTwistDriveError(p,c,AlsQuaternion.Identity).X;
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread()-before;
        Assert.True(double.IsFinite(checksum)); Assert.Equal(0,bytes);
    }
}
