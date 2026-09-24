using System.Numerics;
using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsMantlingRootMotionTests
{
    private static readonly AlsPrecisePose Identity=AlsPrecisePose.Identity;
    private static readonly AlsDoubleVector Up=new(0,0,1);
    private static readonly AlsMantlingWarpSettings Settings=new(1,0,1,0,AlsActionBlendOption.Linear,
        0,1,AlsActionBlendOption.Linear,AlsActionBlendOption.HermiteCubic);

    [Fact]
    public void RootCorrectedAnchorsReconstructStartAndEndWithMeshOffset()
    {
        var roots=Roots();var mesh=Yaw(-System.Math.PI/2);
        var actor=Pose(10,20,30,Yaw(.4));var target=Pose(90,80,130,Yaw(1.2));
        var anchors=AlsMantlingRootMotion.CreateAnchors(actor,target,mesh,roots.Sample(0),roots.SampleLast(),false,Identity);
        var first=AlsMantlingRootMotion.Prepare(0,0,.01f,Settings,anchors,actor,mesh,Up,true,false,Identity,roots);
        Near(actor,first.TargetActor);Near(AlsDoubleVector.Zero,first.Velocity);
        var end=AlsMantlingRootMotion.Prepare(.75f,.25f,.1f,Settings,anchors,actor,mesh,Up,true,false,Identity,roots);
        Near(target,end.TargetActor);
        Near(target.Position,actor.Position+end.Velocity*.1f);
        Near(target.Rotation,end.RotationDelta*actor.Rotation);
    }

    [Fact]
    public void SeparateSimulationClockAndMovementDeltaControlSeekAndVelocity()
    {
        var anchors=new AlsMantlingWarpAnchors(Identity,Pose(100,0,0));
        var settings=Settings with {MontageStartTime=.2f,MontageRate=2,BlendInTime=.4f};
        var step=AlsMantlingRootMotion.Prepare(.1f,.05f,.02f,settings,anchors,Identity,
            AlsQuaternion.Identity,Up,true,false,Identity,Roots(false));
        Assert.Equal(.15f,step.Time,6);Assert.Equal(.5f,step.MontageTime,6);Assert.Equal(.48f,step.MontagePosition,6);
        Assert.Equal(.1875f,step.LocationWeight,6);
        Assert.Equal(18.75,step.TargetActor.Position.X,4);
        Assert.Equal(937.5,step.Velocity.X,3);
    }

    [Theory]
    [InlineData(0,1,true)][InlineData(1,0,true)][InlineData(1,1,false)]
    public void ClearedSourceStillAdvancesTimeWithoutSampling(float duration,float delta,bool alive)
    {
        var step=AlsMantlingRootMotion.Prepare(.3f,.1f,delta,Settings with {Duration=duration},default,
            default,default,default,alive,true,default,null!);
        Assert.False(step.HasRootMotion);Assert.Equal(.4f,step.Time,6);
        Assert.Equal(AlsDoubleVector.Zero,step.Velocity);Assert.Equal(AlsQuaternion.Identity,step.RotationDelta);
    }

    [Fact]
    public void MovingBaseRebuildsBothWorldAnchorsAndDiscardsScaleAfterComposition()
    {
        var roots=Roots();var actor=Pose(10,20,30);var targetLocal=Pose(40,50,100);
        var initialBase=Pose(30,40,0,Yaw(.3)) with {Scale=new(2,3,1.5)};
        var anchors=AlsMantlingRootMotion.CreateAnchors(actor,targetLocal,AlsQuaternion.Identity,
            roots.Sample(0),roots.SampleLast(),true,initialBase);
        var initial=AlsMantlingRootMotion.Prepare(0,0,.01f,Settings,anchors,actor,AlsQuaternion.Identity,
            Up,true,true,initialBase,roots);
        Near(actor,initial.TargetActor);
        var shifted=initialBase with {Position=initialBase.Position+new AlsDoubleVector(17,-9,6)};
        var moved=AlsMantlingRootMotion.Prepare(0,0,.01f,Settings,anchors,actor,AlsQuaternion.Identity,
            Up,true,true,shifted,roots);
        Near(actor.Position+new AlsDoubleVector(17,-9,6),moved.TargetActor.Position);
        Assert.Equal(AlsDoubleVector.One,moved.TargetActor.Scale);
        var rotated=shifted with {Rotation=Yaw(1.1)};
        var end=AlsMantlingRootMotion.Prepare(.9f,.1f,.1f,Settings,anchors,actor,AlsQuaternion.Identity,
            Up,true,true,rotated,roots);
        var targetWorld=AlsPrecisePose.Compose(targetLocal,rotated) with {Scale=AlsDoubleVector.One};
        Near(targetWorld,end.TargetActor);
    }

    [Fact]
    public void GravityTwistRemovesSwingAndQuaternionSignsUseShortestPath()
    {
        var tilt=AlsQuaternion.FromAxisAngle(Vector3.UnitX,.7f);
        var yaw=Yaw(.8);var anchors=new AlsMantlingWarpAnchors(Pose(0,0,0,tilt*yaw),Pose(0,0,0,-(tilt*yaw)));
        var step=AlsMantlingRootMotion.Prepare(0,.5f,.1f,Settings,anchors,Identity,AlsQuaternion.Identity,
            Up,true,false,Identity,Roots(false));
        Near(Up,Up.Rotate(step.TargetActor.Rotation));Near(yaw,step.TargetActor.Rotation);
        var sideways=new AlsDoubleVector(1,0,0);
        var side=AlsMantlingRootMotion.Prepare(0,.5f,.1f,Settings,anchors,Identity,AlsQuaternion.Identity,
            sideways,true,false,Identity,Roots(false));
        Near(sideways,sideways.Rotate(side.TargetActor.Rotation));
    }

    [Fact]
    public void InvalidFrameCannotMutateImmutableAnchorsOrSampleBeforeValidation()
    {
        var anchors=new AlsMantlingWarpAnchors(Identity,Pose(100,0,0));var before=anchors;
        Assert.Throws<ArgumentException>(()=>AlsMantlingRootMotion.Prepare(0,.1f,.1f,Settings,anchors,Identity,
            AlsQuaternion.Identity,new(0,0,2),true,false,Identity,Roots()));
        Assert.Equal(before,anchors);
    }

    private static AlsMontageRootTransformSampler Roots(bool moving=true)
    {
        var keys=new[]{new AlsLocalPose(new(0,0,moving?10:0),Quaternion.Identity,Vector3.One),
            new AlsLocalPose(new(0,moving?20:0,moving?100:0),Quaternion.Identity,Vector3.One)};
        var data=new AlsRawAnimationPoseData(new(0,"root","/test",0),1,1,2,1,
            AlsRawAnimationInterpolation.Linear,[0],[],[true],keys,[]);
        return new([new(0,0,1,1,1,1,new(data,Identity,false))]);
    }
    private static AlsQuaternion Yaw(double value)=>new(0,0,System.Math.Sin(value/2),System.Math.Cos(value/2));
    private static AlsPrecisePose Pose(double x,double y,double z,AlsQuaternion? q=null)=>new(new(x,y,z),q??AlsQuaternion.Identity,AlsDoubleVector.One);
    private static void Near(AlsPrecisePose expected,AlsPrecisePose actual) { Near(expected.Position,actual.Position);Near(expected.Rotation,actual.Rotation); }
    private static void Near(AlsDoubleVector expected,AlsDoubleVector actual)=>Assert.True((expected-actual).LengthSquared<1e-10,$"{expected} != {actual}");
    private static void Near(AlsQuaternion expected,AlsQuaternion actual)=>Assert.True(System.Math.Abs(1-System.Math.Abs(AlsQuaternion.Dot(expected,actual)))<1e-10);
}
