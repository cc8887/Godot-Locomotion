using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsRetargetContactPreparationTests
{
    private static readonly int[] Parents=[-1,0,1,2];
    private static AlsPrecisePose P(double x,double y=0,double z=0)=>new(new(x,y,z),AlsQuaternion.Identity,AlsDoubleVector.One);
    private static AlsPrecisePose[] Reference(double length)=>[P(0),P(0),P(length),P(length)];
    private static AlsPrecisePose[] Pose(double angle=90)
    {
        var pose=Reference(10);pose[2]=pose[2] with {Rotation=AlsQuaternion.FromAxisAngle(new(0,0,1),angle*System.Math.PI/180)};
        return pose;
    }
    private static AlsPrecisePose End(AlsPrecisePose[] pose)
    {var end=pose[0];for(var i=1;i<pose.Length;i++)end=AlsPrecisePose.Compose(pose[i],end);return end;}
    private static AlsSkeletonPoseRetargetModel Model(AlsRetargetBodyGeometry? geometry=null)=>
        new(Parents,Reference(10),Reference(8),[0],[],[new(1,2,3)],0,bodyGeometry:geometry);
    private static float Envelope(double elapsed)
    {var t=System.Math.Clamp(elapsed/.2,0,1);return (float)(t*t*t*(10+t*(-15+6*t)));}

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void PreparationUsesCallerSampleWithoutAnotherClockAndAcquisitionDoesNotJump(int hz)
    {
        var model=Model();var source=Pose();var output=new AlsPrecisePose[4];var anchor=End(source);
        var state=default(AlsRetargetContactState);var last=AlsDoubleVector.Zero;
        for(var step=0;step<=24;step++)
        {
            // Same sample time with different caller dt must give the same
            // stateless anticipation; only release uses the existing dt.
            var elapsed=step/120d;var alpha=Envelope(elapsed);
            model.EvaluateContactFrame(source,output,[step==24?1:0],state,hz==0?0:1f/hz,.2f,out state,
                preparations:[new(true,anchor,alpha)]);
            Assert.Equal(alpha,model.PreparationWeight);
            Assert.InRange(state.RealizedCorrection.LengthSquared,0,16.000001);
            if(step==0)Assert.Equal(AlsDoubleVector.Zero,state.RealizedCorrection);
            if(step==24)
            {
                Assert.InRange((state.RealizedCorrection-last).LengthSquared,0,.00001);
                Assert.InRange(model.MaximumConditionalResidual,0,1e-7);
                Assert.InRange((End(output).Position-anchor.Position).LengthSquared,0,1e-15);
            }
            last=state.RealizedCorrection;
        }
        Assert.InRange(model.MaximumConditionalBendLossDegrees,0,.02);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(90)]
    [InlineData(120)]
    public void FullSupportStillUsesTheCurrentAuthoredBend(double angle)
    {
        var model=Model();var source=Pose(angle);var output=new AlsPrecisePose[4];
        model.EvaluateContactFrame(source,output,[1],default,1f/60,.2f,out var state,
            preparations:[new(true,End(source),1)]);
        Assert.InRange(model.MaximumConditionalBendLossDegrees,0,.02);
        Assert.InRange(model.MaximumConditionalResidual,0,1e-7);
        Assert.InRange(output[2].Position.LengthSquared,63.999999,64.000001);
        Assert.InRange(output[3].Position.LengthSquared,63.999999,64.000001);
        Assert.InRange(state.RealizedCorrection.LengthSquared,0,16.000001);
    }

    [Fact]
    public void ZeroAndVanishingPreparationDoNotPrematurelyLockTheHand()
    {
        var model=Model();var source=Pose();var output=new AlsPrecisePose[4];
        model.EvaluateContactFrame(source,output,[0],default,0,.2f,out var baseline);
        var expected=output.ToArray();
        model.EvaluateContactFrame(source,output,[0],default,0,.2f,out var zero,preparations:[new(true,End(source),0)]);
        Assert.Equal(baseline,zero);Assert.Equal(expected,output);
        model.EvaluateContactFrame(source,output,[0],default,0,.2f,out var nearZero,preparations:[new(true,End(source),1e-7f)]);
        Assert.InRange(nearZero.RealizedCorrection.LengthSquared,0,1e-12);
        Assert.True((End(output).Position-End(source).Position).LengthSquared>7.9);
        Assert.Equal(source[2].Rotation,output[2].Rotation);
    }

    [Fact]
    public void SourceAnchorSaturationIsReportedAndBudgetRemainsHard()
    {
        var model=Model();var source=Pose();var output=new AlsPrecisePose[4];
        model.EvaluateContactFrame(source,output,[0],default,0,.2f,out var state,preparations:[new(true,P(100),1)]);
        Assert.InRange(model.MaximumPreparationSourceReachExcessCm,79.999,80.001);
        Assert.InRange(state.RealizedCorrection.LengthSquared,0,16.000001);
        Assert.True(model.MaximumPreparationStyleResidualCm>79);
    }

    [Fact]
    public void GeometryOverridesPreparedStyleAndImpossibleGeometryRemainsVisible()
    {
        var source=Pose();var output=new AlsPrecisePose[4];
        foreach(var depth in new[]{.5,100d})
        {
            var geometry=new AlsRetargetBodyGeometry(4,[new(0,1)],[new(0,new(0,depth<1?1:100,-depth),1)]);
            var model=Model(geometry);
            model.EvaluateContactFrame(source,output,[1],default,1f/60,.2f,out var state,
                new(true,AlsPrecisePose.Identity,.2),[new(true,End(source),1)]);
            Assert.InRange(state.RealizedCorrection.LengthSquared,0,16.000001);
            Assert.InRange(model.MaximumConditionalResidual,0,1e-6);
            if(depth<1)Assert.InRange(model.MaximumBodyClearanceResidualCm,0,1e-7);
            else Assert.True(model.MaximumBodyClearanceResidualCm>95);
        }
    }

    [Fact]
    public void ASecondHandEntersContinuouslyAndConflictingFullContactsKeepTheirResidual()
    {
        int[] parents=[-1,0,1,2,0,4,5];
        AlsPrecisePose[] source=[P(0),P(0),P(10),P(10),P(0),P(-10),P(-10)];
        AlsPrecisePose[] target=[P(0),P(0),P(8),P(8),P(0),P(-8),P(-8)];
        var model=new AlsSkeletonPoseRetargetModel(parents,source,target,[],[],[new(1,2,3),new(4,5,6)],0);
        var output=new AlsPrecisePose[7];
        model.EvaluateContactFrame(source,output,[0,0],default,0,.2f,out var first,
            preparations:[new(true,P(20),1),new(true,P(-20),0)]);
        model.EvaluateContactFrame(source,output,[0,0],default,0,.2f,out var next,
            preparations:[new(true,P(20),1),new(true,P(-20),1e-8f)]);
        Assert.InRange((next.RealizedCorrection-first.RealizedCorrection).LengthSquared,0,1e-10);
        model.EvaluateContactFrame(source,output,[1,1],default,0,.2f,out var impossible,
            preparations:[new(true,P(20),1),new(true,P(-20),1)]);
        Assert.True(model.MaximumConditionalResidual>=3.999);
        Assert.InRange(impossible.RealizedCorrection.LengthSquared,0,16.000001);
        foreach(var i in new[]{2,3,5,6})Assert.InRange(output[i].Position.LengthSquared,63.999999,64.000001);
    }

    [Fact]
    public void CancelRetryCommittedRestoreAndInvalidCandidateRemainAtomic()
    {
        var model=Model();var source=Pose();var output=new AlsPrecisePose[4];
        AlsRetargetContactPreparation[] preparation=[new(true,End(source),.6f)];
        model.EvaluateContactFrame(source,output,[0],default,1f/60,.2f,out var committed,preparations:preparation);
        var expected=output.ToArray();
        model.EvaluateContactFrame(source,output,[0],committed,1f/60,.2f,out var candidate,preparations:preparation);
        model.EvaluateContactFrame(source,output,[0],committed,1f/60,.2f,out var retry,preparations:preparation);
        Assert.Equal(candidate,retry);
        model.EvaluateCommittedContactFrame(source,output,[0],committed);Assert.Equal(expected,output);
        var saved=output.ToArray();var rejected=committed;
        Assert.Throws<ArgumentException>(()=>model.EvaluateContactFrame(source,output,[0],committed,0,.2f,out rejected,
            preparations:[new(true,End(source),float.NaN)]));
        Assert.Equal(committed,rejected);Assert.Equal(saved,output);
    }

    [Fact]
    public void ReleaseThenReacquisitionRetainsHardContactAndNoAllocations()
    {
        var model=Model();var source=Pose();var output=new AlsPrecisePose[4];
        float[] actual=[1];AlsRetargetContactPreparation[] preparation=[new(true,End(source),1)];
        model.EvaluateContactFrame(source,output,actual,default,1f/60,.2f,out var state,preparations:preparation);
        actual[0]=0;model.EvaluateContactFrame(source,output,actual,state,1f/60,.2f,out var release);
        Assert.True(release.Releasing);
        actual[0]=1;model.EvaluateContactFrame(source,output,actual,release,1f/60,.2f,out var reacquired,preparations:preparation);
        Assert.False(reacquired.Releasing);Assert.InRange(model.MaximumConditionalResidual,0,1e-7);
        for(var i=0;i<30;i++)model.EvaluateContactFrame(source,output,actual,reacquired,1f/60,.2f,out _,preparations:preparation);
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<100;i++)model.EvaluateContactFrame(source,output,actual,reacquired,1f/60,.2f,out _,preparations:preparation);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
    }
}
