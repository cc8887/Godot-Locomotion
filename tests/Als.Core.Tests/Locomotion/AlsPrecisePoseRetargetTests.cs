using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsPrecisePoseRetargetTests
{
    private static AlsPrecisePose P(double x,double y=0)=>new(new(x,y,0),AlsQuaternion.Identity,AlsDoubleVector.One);
    [Theory]
    [InlineData(1d)][InlineData(100d)]
    public void OrientationScaleAndUnanimatedShortcutUseNativeCentimeterThresholds(double units)
    {
        var source=P(10/units);var target=P(0,20/units);
        var model=new AlsPrecisePoseRetargetModel([0],[4],[true],[target],[source],units);
        var poses=new[]{P(10.0005/units)};model.Apply(poses,true);Assert.Equal(target.Position,poses[0].Position);
        poses[0]=P(12/units);model.Apply(poses,true);
        Assert.True((poses[0].Position-new AlsDoubleVector(0,24/units,0)).LengthSquared<1e-20);
        var unchanged=new AlsPrecisePoseRetargetModel([0],[4],[true],[P(10.0005/units)],[source],units);
        poses[0]=P(12/units);unchanged.Apply(poses,true);Assert.Equal(P(12/units),poses[0]);
    }
    [Fact]
    public void RelativeCorrectsAllChannelsAndSafeReciprocalHandlesZeroScale()
    {
        var q=new AlsQuaternion(0,0,System.Math.Sin(.3),System.Math.Cos(.3));
        var source=P(10) with {Rotation=q,Scale=new(2,0,4)};
        var target=P(20) with {Scale=new(4,2,8)};
        var model=new AlsPrecisePoseRetargetModel([0],[3],[true],[target],[source]);
        var pose=new[]{P(13) with {Rotation=q,Scale=new(1,3,2)}};model.Apply(pose,true);
        Assert.Equal(new AlsDoubleVector(23,0,0),pose[0].Position);
        Assert.Equal(new AlsDoubleVector(2,0,4),pose[0].Scale);
        Assert.True(System.Math.Abs(pose[0].Rotation.W-1)<1e-12);
    }
    [Fact]
    public void OppositeDirectionsZeroLengthsMissingTracksAndVirtualBonesAreHandled()
    {
        var model=new AlsPrecisePoseRetargetModel([0,1,2,-1],[4,4,3],[true,true,false,true],
            [P(-10),P(20),P(30),P(40)],[P(10),P(0),P(0)]);
        var poses=new[]{P(12),P(3),P(4),P(5)};model.Apply(poses,true);
        Assert.Equal(P(-12),poses[0]);Assert.Equal(P(3),poses[1]);Assert.Equal(P(4),poses[2]);Assert.Equal(P(5),poses[3]);
    }
    [Fact]
    public void InvalidTailIsRejectedBeforeAnyMutationAndInputsAreCopied()
    {
        var target=new[]{P(20),P(30)};var model=new AlsPrecisePoseRetargetModel([0,1],[2,1],[true,true],target,[P(10),P(10)]);
        target[0]=P(999);var pose=new[]{P(2),P(double.NaN)};
        Assert.Throws<ArgumentException>(()=>model.Apply(pose,true));Assert.Equal(P(2),pose[0]);
        pose[1]=P(3);model.Apply(pose,true);Assert.Equal(P(4),pose[0]);Assert.Equal(P(30),pose[1]);
        var before=pose.ToArray();model.Apply(pose,false);Assert.Equal(before,pose);
    }
}
