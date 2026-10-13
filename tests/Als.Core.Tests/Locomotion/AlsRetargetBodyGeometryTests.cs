using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsRetargetBodyGeometryTests
{
    private static readonly int[] Parents=[-1,0,1,2];
    private static AlsPrecisePose P(double x,double y=0,double z=0)=>new(new(x,y,z),AlsQuaternion.Identity,AlsDoubleVector.One);
    private static AlsPrecisePose[] Reference(double length)=>[P(0),P(0),P(length),P(length)];
    private static AlsPrecisePose[] Pose()
    {
        var source=Reference(10);source[2]=source[2] with {Rotation=AlsQuaternion.FromAxisAngle(new(0,0,1),System.Math.PI/2)};
        return source;
    }
    private static AlsSkeletonPoseRetargetModel Model(params AlsDoubleVector[] points)
    {
        var geometry=new AlsRetargetBodyGeometry(4,points.Select((_,i)=>new AlsRetargetBodyPoint(i,1)).ToArray(),
            points.Select(p=>new AlsRetargetBodyInfluence(0,p,1)).ToArray());
        return new(Parents,Reference(10),Reference(8),[0],[],[new(1,2,3)],0,bodyGeometry:geometry);
    }
    private static AlsRetargetContactEnvironment Environment()=>new(true,AlsPrecisePose.Identity,.2);
    private static AlsRetargetContactState Frame(AlsSkeletonPoseRetargetModel model,AlsPrecisePose[] source,
        AlsPrecisePose[] output,in AlsRetargetContactEnvironment environment)
    {
        model.EvaluateContactFrame(source,output,[1],default,1f/60,.2f,out var candidate,environment);
        return candidate;
    }
    private static AlsDoubleVector Wrist(AlsPrecisePose[] pose)
    {
        var value=pose[0];for(var i=1;i<pose.Length;i++)value=AlsPrecisePose.Compose(pose[i],value);
        return value.Position;
    }
    private static void ContactAndBudget(AlsSkeletonPoseRetargetModel model,AlsPrecisePose[] source,AlsPrecisePose[] output)
    {
        Assert.InRange(model.MaximumConditionalResidual,0,1e-7);
        Assert.InRange((Wrist(source)-Wrist(output)).LengthSquared,0,1e-15);
        Assert.InRange(model.ContactRootCorrection.LengthSquared,0,16.000001);
        Assert.InRange(output[2].Position.LengthSquared,63.999999,64.000001);
        Assert.InRange(output[3].Position.LengthSquared,63.999999,64.000001);
    }

    [Fact]
    public void HeadLikePointChoosesTheCloserTopEscapeWhileKeepingAuthoredBendSoft()
    {
        var model=Model(new AlsDoubleVector(0,-1,-.5));var source=Pose();var output=new AlsPrecisePose[4];
        var state=Frame(model,source,output,Environment());
        Assert.InRange(model.ContactRootCorrection.Z,.6999999,.7000001);
        Assert.InRange(model.MaximumBodyClearanceResidualCm,0,1e-7);
        Assert.Equal(0,model.MaximumBodyPenetrationCm);Assert.Equal(1,model.BodyGeometryPointCount);
        Assert.InRange(model.MaximumConditionalBendLossDegrees,0,2);
        Assert.Equal(model.ContactRootCorrection,state.RealizedCorrection);
        ContactAndBudget(model,source,output);
    }

    [Fact]
    public void ChestLikePointCanMoveOutsideTheFrontInsteadOfForcingAnUpwardCorrection()
    {
        var model=Model(new AlsDoubleVector(0,-1.8,-2));var source=Pose();var output=new AlsPrecisePose[4];
        Frame(model,source,output,Environment());
        Assert.InRange(model.ContactRootCorrection.Y,1.5999999,1.6000001);
        Assert.InRange(System.Math.Abs(model.ContactRootCorrection.Z),0,1e-7);
        Assert.InRange(model.MaximumBodyClearanceResidualCm,0,1e-7);
        ContactAndBudget(model,source,output);
    }

    [Fact]
    public void AllBodyPointsAreRecheckedAndCanRequireBothDifferentOutsideFaces()
    {
        var model=Model(new AlsDoubleVector(0,-1,-.5),new(0,-1.8,-2));var source=Pose();var output=new AlsPrecisePose[4];
        Frame(model,source,output,Environment());
        Assert.InRange(model.ContactRootCorrection.Y,1.5999999,1.6000001);
        Assert.InRange(model.ContactRootCorrection.Z,.6999999,.7000001);
        Assert.InRange(model.MaximumBodyClearanceResidualCm,0,1e-7);
        Assert.True(model.BodyProjectionCount>2);
        ContactAndBudget(model,source,output);
    }

    [Fact]
    public void LedgeAndSkeletonRigidTransformsKeepTheSameGeometricAnswer()
    {
        var baseline=Model(new AlsDoubleVector(0,-1,-.5),new(0,-1.8,-2));var source=Pose();var output=new AlsPrecisePose[4];
        Frame(baseline,source,output,Environment());var original=baseline.ContactRootCorrection;
        var rotation=AlsQuaternion.FromAxisAngle(new(1,0,0),.7);
        var offset=new AlsDoubleVector(25,-12,70);
        source[0]=source[0] with {Rotation=rotation,Position=offset};
        var model=Model(new AlsDoubleVector(0,-1,-.5),new(0,-1.8,-2));
        Frame(model,source,output,new(true,new(offset,rotation,AlsDoubleVector.One),.2));
        Assert.InRange((model.ContactRootCorrection-original.Rotate(rotation)).LengthSquared,0,1e-12);
        Assert.InRange(model.MaximumBodyClearanceResidualCm,0,1e-7);
        ContactAndBudget(model,source,output);
    }

    [Fact]
    public void ImpossibleBodyClearancePreservesContactBudgetAndReportsTheFailure()
    {
        var model=Model(new AlsDoubleVector(0,100,-100));var source=Pose();var output=new AlsPrecisePose[4];
        Frame(model,source,output,Environment());
        Assert.True(model.MaximumBodyPenetrationCm>90);
        Assert.True(model.MaximumBodyClearanceResidualCm>90);
        Assert.InRange(model.BodyProjectionCount,1,64);
        ContactAndBudget(model,source,output);
    }

    [Fact]
    public void ImportedWeightRoundingIsPreservedInTheExactBodyTranslationEquation()
    {
        AlsRetargetBodyPoint[] points=[new(0,2)];
        AlsRetargetBodyInfluence[] influences=[new(0,new(0,-1,-.5),.25f),new(0,new(0,-1,-.5),.7499f)];
        var geometry=new AlsRetargetBodyGeometry(4,points,influences);
        var model=new AlsSkeletonPoseRetargetModel(Parents,Reference(10),Reference(8),[0],[],[new(1,2,3)],0,bodyGeometry:geometry);
        var source=Pose();var output=new AlsPrecisePose[4];Frame(model,source,output,Environment());
        var sum=(double).25f+.7499f;
        var actual=(new AlsDoubleVector(0,-1,-.5)+model.ContactRootCorrection)*sum;
        Assert.InRange(System.Math.Max(actual.Z,-actual.Y),.1999999,.2000001);
        Assert.InRange(model.MaximumBodyClearanceResidualCm,0,1e-7);
    }

    [Fact]
    public void GeometryRejectsAnyIkAffectedInfluenceEvenWhenItsWeightIsSmall()
    {
        var geometry=new AlsRetargetBodyGeometry(4,[new(0,2)],
            [new(0,new(0,-1,-.5),.999f),new(2,new(0,0,0),.001f)]);
        Assert.Throws<ArgumentException>(()=>new AlsSkeletonPoseRetargetModel(Parents,Reference(10),Reference(8),[],[],
            [new(1,2,3)],0,bodyGeometry:geometry));
        Assert.Throws<ArgumentException>(()=>new AlsRetargetBodyGeometry(4,[new(0,1)],[new(0,new(0,0,0),.5f)]));
    }

    [Fact]
    public void DisabledEnvironmentAndCommittedRestoreDoNotReprojectThePublishedCorrection()
    {
        var model=Model(new AlsDoubleVector(0,-1,-.5));var source=Pose();var output=new AlsPrecisePose[4];
        var committed=Frame(model,source,output,Environment());var expected=output.ToArray();
        Frame(model,source,output,new(true,P(0,0,2),.2));
        model.EvaluateCommittedContactFrame(source,output,[1],committed);
        Assert.Equal(expected,output);Assert.Equal(committed.RealizedCorrection,model.ContactRootCorrection);
        Assert.Equal(0,model.BodyProjectionCount);
        Frame(model,source,output,default);
        Assert.Equal(0,model.BodyProjectionCount);Assert.InRange(System.Math.Abs(model.ContactRootCorrection.Z),0,1e-8);
    }

    [Fact]
    public void InvalidEnvironmentCannotPartlyWritePoseOrCandidateState()
    {
        var model=Model(new AlsDoubleVector(0,-1,-.5));var source=Pose();var output=Enumerable.Repeat(P(999),4).ToArray();
        var before=output.ToArray();var candidate=new AlsRetargetContactState {Initialized=true};var original=candidate;
        Assert.Throws<ArgumentException>(()=>model.EvaluateContactFrame(source,output,[1],default,1f/60,.2f,out candidate,
            new(true,AlsPrecisePose.Identity,double.NaN)));
        Assert.Equal(before,output);Assert.Equal(original,candidate);
    }

    [Fact]
    public void RepeatedFrameAndBodySearchAreDeterministicAndAllocateNoMemory()
    {
        var model=Model(new AlsDoubleVector(0,-1,-.5),new(0,-1.8,-2));var source=Pose();var output=new AlsPrecisePose[4];
        float[] weights=[1];var environment=Environment();
        model.EvaluateContactFrame(source,output,weights,default,1f/60,.2f,out var expectedState,environment);
        var expectedPose=output.ToArray();
        for(var i=0;i<30;i++)model.EvaluateContactFrame(source,output,weights,default,1f/60,.2f,out _,environment);
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<100;i++)model.EvaluateContactFrame(source,output,weights,default,1f/60,.2f,out _,environment);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
        model.EvaluateContactFrame(source,output,weights,default,1f/60,.2f,out var actualState,environment);
        Assert.Equal(expectedState,actualState);Assert.Equal(expectedPose,output);
    }
}
