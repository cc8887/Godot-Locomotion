using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsMantlingProgressCurveTests
{
    private static AlsMantlingProgressCurve Curve()=>new([0,.25f,.5f,1],[0,5,20,100],[0,40,80,100]);

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
