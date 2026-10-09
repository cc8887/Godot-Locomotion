using System.Numerics;
using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsRootMotionSamplingPolicyTests
{
    [Fact]
    public void DisabledRootKeepsReferenceWhileChildStillAnimatesInBothPrecisions()
    {
        var child = new AlsLocalPose(new(0,0,50),Quaternion.Identity,Vector3.One);
        var moved = new AlsLocalPose(new(100,20,3),Quaternion.CreateFromAxisAngle(Vector3.UnitZ,1),Vector3.One);
        var animatedChild = child with { Position = new(2,0,60) };
        var data = new AlsRawAnimationPoseData(new(0,"asset","/source",0),1,1,2,1,
            AlsRawAnimationInterpolation.Linear,[0,1],[],[true,true],[AlsLocalPose.Identity,child,moved,animatedChild],[]);
        var reference = new[] { AlsLocalPose.Identity,child };
        var off = new AlsRawSequencePoseSampler(data,[-1,0],reference,[],false);
        var on = new AlsRawSequencePoseSampler(data,[-1,0],reference,[]);
        var a = new AlsLocalPose[2]; var b = new AlsLocalPose[2];
        Assert.Equal(on.Sample(.5,b),off.Sample(.5,a));
        Assert.Equal(AlsLocalPose.Identity,a[0]); Assert.NotEqual(a[0],b[0]); Assert.Equal(a[1],b[1]);
        var precise = reference.Select(p=>new AlsPrecisePose(p)).ToArray();
        var preciseOff = new AlsPreciseRawSequenceSampler(data,[-1,0],precise,[],sampleRootMotion:false);
        var preciseOn = new AlsPreciseRawSequenceSampler(data,[-1,0],precise,[]);
        var x = new AlsPrecisePose[2]; var y = new AlsPrecisePose[2];
        Assert.Equal(preciseOn.Sample(.5,y),preciseOff.Sample(.5,x));
        Assert.Equal(AlsPrecisePose.Identity,x[0]); Assert.NotEqual(x[0],y[0]); Assert.Equal(x[1],y[1]);
    }

    [Fact]
    public void ProceduralMantleNeedsNoRootSamplerAndReachesPlatformWithSameClock()
    {
        var settings = new AlsMantlingWarpSettings(1,0,1,0,AlsActionBlendOption.Linear,0,1,
            AlsActionBlendOption.Linear,AlsActionBlendOption.Linear);
        var target = AlsPrecisePose.Identity with { Position = new(60,0,100) };
        var anchors = new AlsMantlingWarpAnchors(AlsPrecisePose.Identity,target);
        var middle = AlsMantlingRootMotion.Prepare(0,.5f,.5f,settings,anchors,AlsPrecisePose.Identity,
            AlsQuaternion.Identity,new(0,0,1),true,false,AlsPrecisePose.Identity,null!,false);
        Assert.Equal(.5f,middle.Time); Assert.Equal(new AlsDoubleVector(30,0,50),middle.TargetActor.Position);
        var end = AlsMantlingRootMotion.Prepare(.5f,.5f,.5f,settings,anchors,middle.TargetActor,
            AlsQuaternion.Identity,new(0,0,1),true,false,AlsPrecisePose.Identity,null!,false);
        Assert.Equal(1,end.Time); Assert.Equal(target,end.TargetActor);
    }
}
