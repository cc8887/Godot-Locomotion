using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsMantlingContactTests
{
    [Fact]
    public void SupportingArmSolveDoesNotAllocateAfterWarmup()
    {
        var solver=new AlsMantlingHandSolver(Names,Parents);var source=Pose();var output=new AlsLocalPose[source.Length];
        var contact=new AlsMantlingHandContact(Anchor,1);var free=new AlsMantlingHandContact(AlsPrecisePose.Identity,0);
        for(var i=0;i<100;i++)solver.Evaluate(source,output,World,AlsPrecisePose.Identity,contact,free,1);
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<1000;i++)solver.Evaluate(source,output,World,AlsPrecisePose.Identity,contact,free,1);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
    }

    private static readonly string[] Names=["root","upperarm_l","lowerarm_l","hand_l","upperarm_r","lowerarm_r","hand_r"];
    private static readonly int[] Parents=[-1,0,1,2,0,4,5];
    private static AlsLocalPose[] Pose()=>[AlsLocalPose.Identity,AlsLocalPose.Identity,
        AlsLocalPose.Identity with {Position=new(.3f,0,0)},AlsLocalPose.Identity with {Position=new(.3f,0,0)},
        AlsLocalPose.Identity with {Position=new(0,.2f,0)},AlsLocalPose.Identity with {Position=new(.3f,0,0)},
        AlsLocalPose.Identity with {Position=new(.3f,0,0)}];
    private static AlsPrecisePose Anchor=>AlsPrecisePose.Identity with {Position=new(55,0,5)};
    private static AlsLocalPose World=>AlsLocalPose.Identity with {Rotation=AlsFootIkCoordinates.FbxToGodotRotation};

    [Fact]
    public void PartialReleaseUsesTheStraightEffectorPathWithFixedBoneLengths()
    {
        var solver=new AlsMantlingHandSolver(Names,Parents);var source=Pose();var output=new AlsLocalPose[source.Length];
        solver.Evaluate(source,output,World,AlsPrecisePose.Identity,new(Anchor,.5f),new(AlsPrecisePose.Identity,0),1);
        var hand=Component(output,3);
        Assert.InRange(System.Math.Abs(hand.Position.X-.575),0,1e-6);
        Assert.InRange(System.Math.Abs(hand.Position.Z-.025),0,1e-6);
        Assert.InRange(output[2].Position.Length(),.2999999f,.3000001f);
        Assert.InRange(output[3].Position.Length(),.2999999f,.3000001f);
        solver.Evaluate(source,output,World,AlsPrecisePose.Identity,
            new(Anchor,.5f) {TransitionLiftCm=3},new(AlsPrecisePose.Identity,0),1);
        Assert.InRange(System.Math.Abs(Component(output,3).Position.Z-.055),0,1e-6);
    }

    [Fact]
    public void BodyPreparationCanPrecedeContactWithoutChangingTheSupportWindow()
    {
        var curve=new AlsMantlingContactCurve([0,.2f,.4f,1],[0,0,1,0],new float[4],Anchor,
            AlsPrecisePose.Identity,.1f);
        Assert.Equal(.1f,curve.ApproachStartTime);
        Assert.Equal(0,curve.Sample(.1f,true).Weight);
        Assert.Equal(1,curve.Sample(.4f,true).Weight);
        Assert.Throws<ArgumentException>(()=>new AlsMantlingContactCurve([0,.2f,.4f,1],[0,0,1,0],new float[4],Anchor,
            AlsPrecisePose.Identity,.3f));
    }

    [Fact]
    public void ReachableHeightAdaptationBeyondExportBudgetKeepsExactAnchorAndBoneLengths()
    {
        var solver=new AlsMantlingHandSolver(Names,Parents);var source=Pose();var output=new AlsLocalPose[source.Length];
        var target=Anchor with {Position=new(40,0,10)};
        solver.Evaluate(source,output,World,AlsPrecisePose.Identity,new(target,1),new(AlsPrecisePose.Identity,0),1);
        Assert.True(solver.MaximumRequestedCorrectionCm>20);
        Assert.Equal(0,solver.MaximumReachExcessCm);
        Assert.InRange(solver.MaximumResidualCm,0,1e-6);
        var hand=Component(output,3);
        Assert.InRange(System.Math.Abs(hand.Position.X-.4),0,1e-6);
        Assert.InRange(System.Math.Abs(hand.Position.Z-.1),0,1e-6);
        Assert.InRange(output[2].Position.Length(),.2999999f,.3000001f);
        Assert.InRange(output[3].Position.Length(),.2999999f,.3000001f);
        Assert.Equal(source[4],output[4]);
        solver.Evaluate(source,output,World,AlsPrecisePose.Identity,new(target,1),new(AlsPrecisePose.Identity,0),.25f);
        Assert.InRange(System.Math.Abs(Component(output,3).Position.X-.4),0,1e-6);
        Assert.InRange(System.Math.Abs(Component(output,3).Position.Z-.1),0,1e-6);
        solver.Evaluate(source,output,World,AlsPrecisePose.Identity,new(target,0),new(AlsPrecisePose.Identity,0),1);
        Assert.Equal(source,output);
        Assert.Equal(0,solver.MaximumRequestedCorrectionCm);
        Assert.Equal(0,solver.MaximumReachExcessCm);
    }

    [Fact]
    public void SupportAnchorIsImmutableThroughFadeAndBodyTravel()
    {
        float[] keys=[0,.2f,.4f,.8f,1];float[] weights=[0,0,1,1,0];
        var curve=new AlsMantlingContactCurve(keys,weights,new float[5],Anchor,AlsPrecisePose.Identity);
        weights[2]=0;keys[2]=.7f;
        Assert.Equal(1,curve.Sample(.5f,true).Weight);Assert.Equal(.5f,curve.Sample(.3f,true).Weight,5);
        Assert.Equal(.2f,curve.ApproachStartTime);
        Assert.Equal(Anchor,curve.Sample(.3f,true).LedgeAnchor);Assert.Equal(Anchor,curve.Sample(.6f,true).LedgeAnchor);
        Assert.Equal(0,curve.Sample(-1,true).Weight);Assert.Equal(0,curve.Sample(1.1f,true).Weight);
        var solver=new AlsMantlingHandSolver(Names,Parents);var source=Pose();var output=new AlsLocalPose[source.Length];
        solver.Evaluate(source,output,World,AlsPrecisePose.Identity,curve.Sample(.5f,true),curve.Sample(.5f,false),1);
        Assert.True(solver.MaximumResidualCm<1e-6);Assert.Equal(source[4],output[4]);
        Assert.InRange(output[2].Position.Length(),.2999999f,.3000001f);Assert.InRange(output[3].Position.Length(),.2999999f,.3000001f);
        var end=Component(output,3);Assert.InRange(end.Position.X,.5499999,.5500001);Assert.InRange(end.Position.Z,.0499999,.0500001);
        var body=World with {Position=new(.01f,.015f,-.01f)};
        solver.Evaluate(source,output,body,AlsPrecisePose.Identity,curve.Sample(.6f,true),curve.Sample(.6f,false),1);
        var native=AlsFootIkCoordinates.FbxComponentToNativeWorld(body);
        var local=Component(output,3);var actual=AlsPrecisePose.Compose(local with {Position=new(local.Position.X*100,-local.Position.Y*100,local.Position.Z*100)},native);
        Assert.InRange(System.Math.Sqrt((actual.Position-Anchor.Position).LengthSquared),0,1e-4);
    }

    [Fact]
    public void MovingRotatingPlatformTransportsAnchorAndUnreachableTargetReportsFailure()
    {
        var solver=new AlsMantlingHandSolver(Names,Parents);var source=Pose();var output=new AlsLocalPose[source.Length];
        var platform=AlsPrecisePose.Identity with {Position=new(2,0,1),Rotation=new(0,0,System.Math.Sin(.01),System.Math.Cos(.01))};
        var contact=new AlsMantlingHandContact(Anchor,1);
        solver.Evaluate(source,output,World,platform,contact,new(AlsPrecisePose.Identity,0),1);
        Assert.InRange(solver.MaximumResidualCm,0,1e-5);
        var expected=AlsPrecisePose.Compose(Anchor,platform);var actual=Component(output,3);
        Assert.InRange(System.Math.Abs(actual.Position.X*100-expected.Position.X),0,1e-4);
        Assert.InRange(System.Math.Abs(-actual.Position.Y*100-expected.Position.Y),0,1e-4);
        solver.Evaluate(source,output,World,AlsPrecisePose.Identity,contact with {LedgeAnchor=Anchor with {Position=new(200,0,0)}},new(AlsPrecisePose.Identity,0),1);
        Assert.True(solver.MaximumResidualCm>130);Assert.InRange(output[3].Position.Length(),.2999999f,.3000001f);
        solver.Evaluate(source,output,World,AlsPrecisePose.Identity,contact,new(AlsPrecisePose.Identity,0),0);
        Assert.Equal(source,output);Assert.Equal(0,solver.MaximumResidualCm);
    }

    private static AlsPrecisePose Component(AlsLocalPose[] pose,int bone)=>Parents[bone]<0 ? new(pose[bone]) :
        AlsPrecisePose.Compose(new(pose[bone]),Component(pose,Parents[bone]));
}
