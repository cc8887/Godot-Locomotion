using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsRetargetAuthoredBendTests
{
    private static readonly int[] Parents = [-1, 0, 1, 2, 3, 4];
    private static AlsPrecisePose P(double x, double y = 0, double z = 0) =>
        new(new(x,y,z), AlsQuaternion.Identity, AlsDoubleVector.One);
    private static AlsPrecisePose[] Reference(double a, double b) => [P(0), P(0,0,10), P(0), P(a), P(b), P(2)];
    private static AlsPrecisePose[] Pose(double bend)
    {
        var result = Reference(10,10);
        result[3] = result[3] with { Rotation = AlsQuaternion.FromAxisAngle(new(0,0,1),bend*System.Math.PI/180) };
        return result;
    }
    private static AlsSkeletonPoseRetargetModel Model(double a = 8, double b = 8) =>
        new(Parents,Reference(10,10),Reference(a,b),[],[],[new(2,3,4)],1);
    private static AlsPrecisePose[] Components(AlsPrecisePose[] local)
    {
        var result = new AlsPrecisePose[local.Length];
        for (var i=0;i<local.Length;i++) result[i] = Parents[i]<0 ? local[i] : AlsPrecisePose.Compose(local[i],result[Parents[i]]);
        return result;
    }
    private static double Bend(AlsPrecisePose[] components)
    {
        var a=components[3].Position-components[2].Position;
        var b=components[4].Position-components[3].Position;
        return System.Math.Acos(System.Math.Clamp(AlsDoubleVector.Dot(a,b)/System.Math.Sqrt(a.LengthSquared*b.LengthSquared),-1,1))*180/System.Math.PI;
    }

    [Theory]
    [InlineData(25,8,8)]
    [InlineData(60,8,8)]
    [InlineData(90,8,8)]
    [InlineData(120,8,8)]
    [InlineData(75,6,10)]
    public void TargetProportionsKeepEachAuthoredBendAndItsPlane(double angle,double upper,double lower)
    {
        var model=Model(upper,lower);var source=Pose(angle);var target=new AlsPrecisePose[source.Length];
        model.Evaluate(source,target,contactWeights:[1]);
        var expected=Components(source);var actual=Components(target);
        Assert.InRange(System.Math.Abs(Bend(actual)-angle),0,1e-7);
        Assert.InRange((actual[4].Position-expected[4].Position).LengthSquared,0,1e-15);
        var sourcePlane=AlsDoubleVector.Cross(expected[3].Position-expected[2].Position,expected[4].Position-expected[3].Position).SafeNormal();
        var targetPlane=AlsDoubleVector.Cross(actual[3].Position-actual[2].Position,actual[4].Position-actual[3].Position).SafeNormal();
        Assert.InRange((targetPlane-sourcePlane).LengthSquared,0,1e-16);
        Assert.InRange(model.ContactRootCorrection.LengthSquared,0,model.MaximumContactRootCorrection*model.MaximumContactRootCorrection+1e-12);
        Assert.InRange(model.MaximumConditionalBendLossDegrees,0,1e-7);
        Assert.InRange(System.Math.Abs(target[3].Position.LengthSquared-upper*upper),0,1e-10);
        Assert.InRange(System.Math.Abs(target[4].Position.LengthSquared-lower*lower),0,1e-10);
        Assert.InRange(System.Math.Abs(target[5].Position.LengthSquared-4),0,1e-10);
    }

    [Fact]
    public void ADeliberatelyStraightSourceIsNotForcedIntoOnePreferredBend()
    {
        var model=Model();var source=Pose(0);var target=new AlsPrecisePose[source.Length];
        model.Evaluate(source,target,contactWeights:[1]);
        Assert.InRange(Bend(Components(target)),0,1e-5);
        Assert.InRange(model.MaximumConditionalResidual,0,1e-8);
        Assert.InRange(model.ContactRootCorrection.LengthSquared,15.999999,16.000001);
    }

    [Fact]
    public void IdenticalProportionsNeedNoBodyTranslationAndReleaseHasNoHistory()
    {
        var model=Model(10,10);var source=Pose(75);var target=new AlsPrecisePose[source.Length];
        model.Evaluate(source,target,contactWeights:[1]);
        Assert.InRange(model.ContactRootCorrection.LengthSquared,0,1e-20);
        Assert.InRange(System.Math.Abs(Bend(Components(target))-75),0,1e-7);
        var free=new AlsPrecisePose[source.Length];model.Evaluate(source,free);
        model.Evaluate(Pose(30),target,contactWeights:[1]);
        model.Evaluate(source,target,contactWeights:[0]);
        Assert.Equal(free,target);
        Assert.Equal(0,model.MaximumConditionalBendLossDegrees);
        Assert.Equal(AlsDoubleVector.Zero,model.ContactRootCorrection);
    }

    [Fact]
    public void OpposedIncompatibleHandTargetsKeepBudgetAndReportTheirTrueResidual()
    {
        int[] parents=[-1,0,1,2,0,4,5];
        AlsPrecisePose[] source=[P(0),P(0),P(10),P(10),P(0),P(-10),P(-10)];
        AlsPrecisePose[] reference=[P(0),P(0),P(8),P(8),P(0),P(-8),P(-8)];
        var model=new AlsSkeletonPoseRetargetModel(parents,source,reference,[],[],[new(1,2,3),new(4,5,6)],0);
        var target=new AlsPrecisePose[source.Length];model.Evaluate(source,target,contactWeights:[1,1]);
        Assert.True(model.MaximumConditionalResidual>=3.999999);
        Assert.InRange(model.ContactRootCorrection.LengthSquared,0,16.000001);
        foreach(var bone in new[]{2,3,5,6}) Assert.InRange(System.Math.Abs(target[bone].Position.LengthSquared-64),0,1e-10);
    }

    [Fact]
    public void ConflictingBendPreferencesRemainSoftWhenBothHandContactsAreReachable()
    {
        int[] parents=[-1,0,1,2,0,4,5];
        AlsPrecisePose[] original=[P(0),P(-10),P(10),P(10),P(10),P(-10),P(-10)];
        AlsPrecisePose[] reference=[P(0),P(-12),P(8),P(8),P(12),P(-8),P(-8)];
        var source=original.ToArray();
        source[2]=source[2] with { Rotation=AlsQuaternion.FromAxisAngle(new(0,0,1),System.Math.PI/2) };
        source[5]=source[5] with { Rotation=AlsQuaternion.FromAxisAngle(new(0,0,1),-System.Math.PI/2) };
        var model=new AlsSkeletonPoseRetargetModel(parents,original,reference,[],[],[new(1,2,3),new(4,5,6)],0);
        var target=new AlsPrecisePose[source.Length];model.Evaluate(source,target,contactWeights:[1,1]);
        Assert.InRange(model.MaximumConditionalResidual,0,1e-7);
        Assert.InRange(model.ContactRootCorrection.LengthSquared,0,model.MaximumContactRootCorrection*model.MaximumContactRootCorrection+1e-10);
        Assert.True(model.MaximumConditionalBendLossDegrees>1);
        foreach(var bone in new[]{2,3,5,6}) Assert.InRange(System.Math.Abs(target[bone].Position.LengthSquared-64),0,1e-10);
    }

    [Fact]
    public void TooCloseUnequalArmTargetDoesNotStretchTheLowerBoneToHideItsResidual()
    {
        var model=Model(4,12);var source=Pose(180);var target=new AlsPrecisePose[source.Length];
        model.Evaluate(source,target,contactWeights:[1]);
        Assert.True(model.MaximumConditionalResidual>7.99);
        Assert.InRange(System.Math.Abs(target[3].Position.LengthSquared-16),0,1e-10);
        Assert.InRange(System.Math.Abs(target[4].Position.LengthSquared-144),0,1e-10);
    }

    [Fact]
    public void PosePreferenceAndMultiContactScratchAllocateNothingAfterWarmup()
    {
        var model=Model();var source=Pose(75);var target=new AlsPrecisePose[source.Length];float[] weights=[1];
        for(var i=0;i<30;i++)model.Evaluate(source,target,contactWeights:weights);
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<100;i++)model.Evaluate(source,target,contactWeights:weights);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
    }
}
