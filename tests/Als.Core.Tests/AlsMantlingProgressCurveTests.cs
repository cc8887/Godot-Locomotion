using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsMantlingProgressCurveTests
{
    private static AlsMantlingProgressCurve Curve()=>new([0,.25f,.5f,1],[0,5,20,100],[0,40,80,100]);

    [Theory]
    [InlineData(0f, false, 1f)][InlineData(.25f, false, 1f)]
    [InlineData(0f, true, 1f)][InlineData(.25f, true, 1f)]
    [InlineData(0f, false, 2f)][InlineData(.25f, false, 2f)]
    [InlineData(0f, true, 2f)][InlineData(.25f, true, 2f)]
    public void SampledRootContactTravelUsesActorEndpointsAndMatchesTheControlledRootOffPath(float entry, bool relative, float rate)
    {
        var mesh=new AlsQuaternion(0,0,-System.Math.Sqrt(.5),System.Math.Sqrt(.5));
        var basis=AlsPrecisePose.Identity with {Position=new(25,-10,0),
            Rotation=new(0,0,System.Math.Sin(.15),System.Math.Cos(.15))};
        var actor=AlsPrecisePose.Identity with {Position=new(10,15,30)};
        var target=actor with {Position=new(100,-20,120)};
        var destination=relative ? AlsPrecisePose.Relative(target,basis) : target;
        var data=new AlsRawAnimationPoseData(new(0,"root","/test",0),1,1,3,2,
            AlsRawAnimationInterpolation.Linear,[0],[],[true],
            [new(new(0,0,10),System.Numerics.Quaternion.Identity,System.Numerics.Vector3.One),
             new(new(0,20,100),System.Numerics.Quaternion.Identity,System.Numerics.Vector3.One),
             new(new(0,30,150),System.Numerics.Quaternion.Identity,System.Numerics.Vector3.One)],[]);
        var roots=new AlsMontageRootTransformSampler([new(0,0,rate,1,rate,1,new(data,AlsPrecisePose.Identity,false))]);
        var on=AlsMantlingRootMotion.CreateAnchors(actor,destination,mesh,roots.Sample(entry),roots.Sample(1),relative,basis);
        var off=AlsMantlingRootMotion.CreateAnchors(actor,destination,mesh,AlsPrecisePose.Identity,AlsPrecisePose.Identity,relative,basis);
        var settings=new AlsMantlingWarpSettings(1-entry,entry,1,0,AlsActionBlendOption.Linear,0,1,
            AlsActionBlendOption.Linear,AlsActionBlendOption.Linear);
        var moved=basis with {Position=basis.Position+new AlsDoubleVector(3,7,0)};
        var enabled=AlsMantlingRootMotion.Prepare(0,.5f-entry,.1f,settings,on,actor,mesh,new(0,0,1),true,relative,moved,roots,true,Curve());
        var disabled=AlsMantlingRootMotion.Prepare(0,.5f-entry,.1f,settings,off,actor,mesh,new(0,0,1),true,relative,moved,null!,false,Curve());
        Assert.Equal(.5f,enabled.MontageTime);
        Assert.Equal(disabled.Time,enabled.Time);
        Assert.InRange((enabled.TargetActor.Position-disabled.TargetActor.Position).LengthSquared,0,1e-18);
        Assert.InRange((enabled.Velocity-disabled.Velocity).LengthSquared,0,1e-14);
        var end=AlsMantlingRootMotion.Prepare(0,1-entry,.1f,settings,on,actor,mesh,new(0,0,1),true,relative,moved,roots,true,Curve());
        var expected=relative ? AlsPrecisePose.Compose(AlsPrecisePose.Relative(target,basis),moved) : target;
        Assert.InRange((end.TargetActor.Position-expected.Position).LengthSquared,0,1e-18);
    }

    [Fact]
    public void TallerLedgeFinishesSurplusLiftBeforeGripWithoutChangingTheLandingOrClock()
    {
        var curve=Curve();
        Assert.Equal(new AlsMantlingProgressSample(0,0),curve.Sample(0,0,0,120,.25f));
        var mesh=new AlsQuaternion(0,0,-System.Math.Sqrt(.5),System.Math.Sqrt(.5));
        var actor=AlsPrecisePose.Identity;var target=actor with {Position=new(100,0,120)};
        var anchors=AlsMantlingRootMotion.CreateAnchors(actor,target,mesh,actor,actor,false,actor);
        var settings=new AlsMantlingWarpSettings(1,0,1,0,AlsActionBlendOption.Linear,0,1,
            AlsActionBlendOption.Linear,AlsActionBlendOption.Linear)
            {ProgressTargetHeight=120,ProgressPreparationEndTime=.25f};
        var step=AlsMantlingRootMotion.Prepare(0,.5f,.5f,settings,anchors,actor,mesh,new(0,0,1),true,false,actor,null!,false,curve);
        Assert.Equal(.5f,step.Time);Assert.Equal(.5f,step.MontageTime);
        Assert.InRange(step.TargetActor.Position.X,19.99999,20.00001);
        Assert.InRange(step.TargetActor.Position.Z,99.99999,100.00001);
        Assert.Equal(new AlsMantlingProgressSample(1,1),curve.Sample(1,0,0,120,.25f));
        Assert.Equal(curve.Sample(.5f,0,.25f),curve.Sample(.5f,0,.25f,60,.25f));
    }

    [Fact]
    public void HeightPreparationRequiresPairedFiniteMetadataAndKeepsLegacySampling()
    {
        var curve=Curve();
        Assert.Equal(curve.Sample(.2f,0,0),curve.Sample(.2f,0,0,null,null));
        Assert.Throws<ArgumentException>(()=>curve.Sample(.2f,0,0,120,null));
        Assert.Throws<ArgumentException>(()=>curve.Sample(.2f,0,0,float.NaN,.25f));
        Assert.Throws<ArgumentException>(()=>curve.Sample(.2f,.2f,.2f,120,.1f));
    }

    [Fact]
    public void EarlierApproachDoesNotAdvanceHeightMatchedLift()
    {
        var curve=Curve();var liftStart=curve.MatchStartTime(60);
        var approach=curve.Sample(.1f,0,liftStart);
        Assert.True(approach.Planar>0);Assert.Equal(0,approach.Lift);
        var middle=curve.Sample(.5f,0,liftStart);
        Assert.Equal(curve.Sample(.5f,liftStart).Lift,middle.Lift);
        Assert.Equal(new AlsMantlingProgressSample(1,1),curve.Sample(1,0,liftStart));
        Assert.Throws<ArgumentException>(()=>curve.Sample(.5f,.5f,.25f));
    }

    [Fact]
    public void RemainingHeightMatchingChoosesPoseAndRebasesBothAxes()
    {
        var curve=Curve();var start=curve.MatchStartTime(60);
        Assert.Equal(.25f,start,6);Assert.Equal(new AlsMantlingProgressSample(0,0),curve.Sample(start,start));
        var mid=curve.Sample(.5f,start);
        Assert.Equal(15/95f,mid.Planar,6);Assert.Equal(40/60f,mid.Lift,6);
        Assert.Equal(new AlsMantlingProgressSample(1,1),curve.Sample(1,start));
        Assert.Equal(0,curve.MatchStartTime(200));
    }

    [Fact]
    public void LiftAndAdvanceUseOneClockWithoutAReadableRootSampler()
    {
        var mesh=new AlsQuaternion(0,0,-System.Math.Sqrt(.5),System.Math.Sqrt(.5));
        var actor=AlsPrecisePose.Identity;var target=actor with {Position=new(100,0,100)};
        var anchors=AlsMantlingRootMotion.CreateAnchors(actor,target,mesh,actor,actor,false,actor);
        var settings=new AlsMantlingWarpSettings(1,0,1,0,AlsActionBlendOption.Linear,0,1,
            AlsActionBlendOption.Linear,AlsActionBlendOption.Linear);
        var step=AlsMantlingRootMotion.Prepare(0,.5f,.5f,settings,anchors,actor,mesh,new(0,0,1),true,false,actor,null!,false,Curve());
        Assert.Equal(.5f,step.Time);Assert.Equal(.5f,step.MontageTime);
        Assert.InRange(step.TargetActor.Position.X,19.99999,20.00001);
        Assert.InRange(step.TargetActor.Position.Z,79.99999,80.00001);
        Assert.InRange(System.Math.Abs(AlsQuaternion.Dot(step.TargetActor.Rotation,actor.Rotation)),.99999999,1.00000001);
    }

    [Fact]
    public void ContactPlateausDoNotInventProgressAndRetainTheMontageTimeDomain()
    {
        var curve=new AlsMantlingProgressCurve([0,.2f,.4f,.8f,1],[0,0,10,100,100],[0,0,100,100,100]);
        Assert.Equal(.3f,curve.MatchStartTime(50),6);
        Assert.Equal(new AlsMantlingProgressSample(0,0),curve.Sample(.1f,0));
        Assert.Equal(1,curve.Sample(.6f,0).Lift);
        Assert.InRange(curve.Sample(.6f,0).Planar,.54999f,.55001f);
    }

    [Fact]
    public void CurvesAreImmutableAndRejectReversedMotionInsteadOfFittingItAway()
    {
        float[] times=[0,1],planar=[0,10],lift=[0,100];var curve=new AlsMantlingProgressCurve(times,planar,lift);
        lift[1]=0;times[1]=0;planar[1]=0;
        Assert.Equal(new AlsMantlingProgressSample(1,1),curve.Sample(1,0));
        Assert.Throws<ArgumentException>(()=>new AlsMantlingProgressCurve([0,1],[0,10],[0,0]));
        Assert.Throws<ArgumentException>(()=>new AlsMantlingProgressCurve([0,.5f,1],[0,20,10],[0,50,100]));
        Assert.Throws<ArgumentException>(()=>curve.MatchStartTime(float.NaN));
    }
}
