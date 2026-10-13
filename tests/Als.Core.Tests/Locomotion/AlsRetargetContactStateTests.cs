using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsRetargetContactStateTests
{
    private static readonly int[] Parents = [-1,0,1,2,3];
    private static AlsPrecisePose P(double x,double y=0) => new(new(x,y,0),AlsQuaternion.Identity,AlsDoubleVector.One);
    private static AlsPrecisePose[] Reference(double length) => [P(0),P(0),P(length),P(length),P(2)];
    private static AlsPrecisePose[] Pose()
    {
        var pose=Reference(10);
        pose[2]=pose[2] with { Rotation=AlsQuaternion.FromAxisAngle(new(0,0,1),System.Math.PI/2) };
        return pose;
    }
    private static AlsSkeletonPoseRetargetModel Model() => new(Parents,Reference(10),Reference(8),[],[],[new(1,2,3)],0);
    private static AlsRetargetContactState Frame(AlsSkeletonPoseRetargetModel model,AlsPrecisePose[] source,
        AlsPrecisePose[] output,float weight,AlsRetargetContactState previous,float dt,float duration=.2f)
    {
        model.EvaluateContactFrame(source,output,[weight],previous,dt,duration,out var candidate);
        return candidate;
    }
    private static AlsDoubleVector Wrist(AlsPrecisePose[] local)
    {
        var component=local[0];
        for(var i=1;i<=3;i++)component=AlsPrecisePose.Compose(local[i],component);
        return component.Position;
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void FullReleaseUsesCharacterSecondsAndEndsAtTheStatelessFreePose(int hz)
    {
        var model=Model();var source=Pose();var output=new AlsPrecisePose[source.Length];
        var state=Frame(model,source,output,1,default,1f/hz);
        var initial=state.RealizedCorrection.LengthSquared;var previous=initial;
        for(var i=1;i<=hz/5+1;i++)
        {
            state=Frame(model,source,output,0,state,1f/hz);
            Assert.InRange(state.RealizedCorrection.LengthSquared,0,previous+1e-8);
            if(i==1)Assert.InRange(state.RealizedCorrection.LengthSquared,initial*.5,initial);
            previous=state.RealizedCorrection.LengthSquared;
        }
        Assert.False(state.Releasing);Assert.Equal(AlsDoubleVector.Zero,state.RealizedCorrection);
        var expected=new AlsPrecisePose[source.Length];model.Evaluate(source,expected);
        for(var i=0;i<source.Length;i++)
        {
            Assert.InRange((expected[i].Position-output[i].Position).LengthSquared,0,1e-18);
            Assert.InRange(System.Math.Abs(1-System.Math.Abs(AlsQuaternion.Dot(expected[i].Rotation,output[i].Rotation))),0,1e-12);
        }
    }

    [Fact]
    public void DifferentFrameRatesAgreeAtTheSameElapsedReleaseTime()
    {
        var observations=new List<AlsDoubleVector>();
        foreach(var hz in new[]{30,60,120})
        {
            var model=Model();var source=Pose();var output=new AlsPrecisePose[source.Length];
            var state=Frame(model,source,output,1,default,1f/hz);
            for(var i=0;i<hz/10;i++)state=Frame(model,source,output,0,state,1f/hz);
            observations.Add(state.RealizedCorrection);
        }
        foreach(var value in observations)Assert.InRange((value-observations[0]).LengthSquared,0,1e-10);
    }

    [Fact]
    public void GradualWeightDropDoesNotRestartTheReleaseAndReacquisitionKeepsTheAnchor()
    {
        var model=Model();var source=Pose();var output=new AlsPrecisePose[source.Length];
        var state=Frame(model,source,output,1,default,1f/60);
        var partial=Frame(model,source,output,.5f,state,1f/60);
        Assert.True(partial.Releasing);
        var released=Frame(model,source,output,0,partial,1f/60);
        Assert.True(released.ReleaseElapsedSeconds>partial.ReleaseElapsedSeconds);
        Assert.Equal(partial.ReleaseStartCorrection,released.ReleaseStartCorrection);
        Assert.True(released.RealizedCorrection.LengthSquared>0);
        var acquired=Frame(model,source,output,1,released,1f/60);
        Assert.False(acquired.Releasing);
        Assert.InRange((Wrist(source)-Wrist(output)).LengthSquared,0,1e-15);
        Assert.InRange(model.MaximumConditionalResidual,0,1e-7);
        Assert.InRange(output[2].Position.LengthSquared,63.999999,64.000001);
        Assert.InRange(output[3].Position.LengthSquared,63.999999,64.000001);
    }

    [Fact]
    public void ReleasingOneHandDoesNotSmoothTheRemainingFullSupportAwayFromItsAnchor()
    {
        int[] parents=[-1,0,1,2,0,4,5];
        AlsPrecisePose[] original=[P(0),P(0),P(10),P(10),P(0),P(10),P(10)];
        AlsPrecisePose[] reference=[P(0),P(0),P(8),P(8),P(0),P(8),P(8)];
        var source=original.ToArray();
        source[2]=source[2] with {Rotation=AlsQuaternion.FromAxisAngle(new(0,0,1),System.Math.PI/2)};
        source[5]=source[5] with {Rotation=AlsQuaternion.FromAxisAngle(new(0,0,1),-System.Math.PI/2)};
        var model=new AlsSkeletonPoseRetargetModel(parents,original,reference,[],[],[new(1,2,3),new(4,5,6)],0);
        var output=new AlsPrecisePose[source.Length];
        model.EvaluateContactFrame(source,output,[1,1],default,1f/60,.2f,out var state);
        model.EvaluateContactFrame(source,output,[1,0],state,1f/60,.2f,out var candidate);
        Assert.True(candidate.Releasing);
        Assert.InRange((Wrist(source)-Wrist(output)).LengthSquared,0,1e-15);
        Assert.InRange(model.MaximumConditionalResidual,0,1e-7);
        Assert.Equal(candidate.RealizedCorrection,model.ContactRootCorrection);
        Assert.InRange(candidate.RealizedCorrection.LengthSquared,0,16.000001);
    }

    [Fact]
    public void CancelRetryAndCommittedRestoreUseExactlyTheSameValueState()
    {
        var model=Model();var source=Pose();var output=new AlsPrecisePose[source.Length];
        var committed=Frame(model,source,output,1,default,1f/60);
        var before=committed;var candidate=Frame(model,source,output,0,committed,1f/60);
        var expected=output.ToArray();
        _=Frame(model,source,output,0,candidate,.1f);
        var retried=Frame(model,source,output,0,committed,1f/60);
        Assert.Equal(candidate,retried);Assert.Equal(expected,output);Assert.Equal(before,committed);
        model.EvaluateCommittedContactFrame(source,output,[0],candidate);
        Assert.Equal(expected,output);
        Assert.Equal(candidate.RealizedCorrection,model.ContactRootCorrection);
        model.EvaluateCommittedContactFrame(source,output,[0],candidate);
        Assert.Equal(expected,output);
    }

    [Fact]
    public void ZeroDeltaDoesNotAdvanceReleaseAndDefaultStateExplicitlyResetsIt()
    {
        var model=Model();var source=Pose();var output=new AlsPrecisePose[source.Length];
        var state=Frame(model,source,output,1,default,1f/60);
        state=Frame(model,source,output,0,state,1f/60);
        var paused=Frame(model,source,output,0,state,0);
        Assert.Equal(state.ReleaseElapsedSeconds,paused.ReleaseElapsedSeconds);
        Assert.Equal(state.RealizedCorrection,paused.RealizedCorrection);
        var reset=Frame(model,source,output,0,default,1f/60);
        Assert.False(reset.Releasing);Assert.Equal(AlsDoubleVector.Zero,reset.RealizedCorrection);
    }

    [Fact]
    public void InvalidFrameInputsPreserveBothCallerOutputs()
    {
        var model=Model();var source=Pose();var output=Enumerable.Repeat(P(999),source.Length).ToArray();
        var before=output.ToArray();var sentinel=new AlsRetargetContactState { Initialized=true,ContactWeight=.25 };
        var candidate=sentinel;
        foreach(var dt in new[]{-1,float.NaN,float.PositiveInfinity})
        {
            Assert.Throws<ArgumentException>(()=>model.EvaluateContactFrame(source,output,[0],default,dt,.2f,out candidate));
            Assert.Equal(before,output);Assert.Equal(sentinel,candidate);
        }
        Assert.Throws<ArgumentException>(()=>model.EvaluateContactFrame(source,output,[0],default,1f/60,float.NaN,out candidate));
        Assert.Throws<ArgumentException>(()=>model.EvaluateContactFrame(source,output,[float.NaN],default,1f/60,.2f,out candidate));
        var invalid=sentinel with {RealizedCorrection=new(999,0,0)};
        Assert.Throws<ArgumentException>(()=>model.EvaluateContactFrame(source,output,[0],invalid,1f/60,.2f,out candidate));
        Assert.Throws<ArgumentException>(()=>model.EvaluateCommittedContactFrame(source,output,[0],invalid));
        Assert.Equal(before,output);Assert.Equal(sentinel,candidate);
    }

    [Fact]
    public void ProjectedReleaseStoresActualBudgetLimitedCorrectionAndKeepsUnreachableResidual()
    {
        var model=Model();var source=Pose();var output=new AlsPrecisePose[source.Length];
        var state=Frame(model,source,output,1,default,1f/60);
        var bad=source.ToArray();bad[3]=bad[3] with {Position=new(50,0,0)};
        var candidate=Frame(model,bad,output,.5f,state,1f/60);
        Assert.InRange(candidate.RealizedCorrection.LengthSquared,0,16.000001);
        Assert.Equal(candidate.RealizedCorrection,model.ContactRootCorrection);
        Assert.True(model.MaximumConditionalResidual>1);
        Assert.InRange(output[2].Position.LengthSquared,63.999999,64.000001);
        Assert.InRange(output[3].Position.LengthSquared,63.999999,64.000001);
    }

    [Fact]
    public void ExplicitZeroDurationRetainsImmediateStatelessPolicy()
    {
        var model=Model();var source=Pose();var output=new AlsPrecisePose[source.Length];
        var state=Frame(model,source,output,1,default,1f/60);
        state=Frame(model,source,output,0,state,1f/60,0);
        Assert.False(state.Releasing);Assert.Equal(AlsDoubleVector.Zero,state.RealizedCorrection);
    }

    [Fact]
    public void StatefulEvaluationAndRestoreAllocateNothingAfterWarmup()
    {
        var model=Model();var source=Pose();var output=new AlsPrecisePose[source.Length];float[] full=[1],none=[0];
        model.EvaluateContactFrame(source,output,full,default,1f/60,.2f,out var state);
        for(var i=0;i<100;i++)
        {
            model.EvaluateContactFrame(source,output,none,state,1f/60,.2f,out var candidate);
            model.EvaluateCommittedContactFrame(source,output,none,candidate);
        }
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<100;i++)
        {
            model.EvaluateContactFrame(source,output,none,state,1f/60,.2f,out var candidate);
            model.EvaluateCommittedContactFrame(source,output,none,candidate);
        }
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
    }
}
