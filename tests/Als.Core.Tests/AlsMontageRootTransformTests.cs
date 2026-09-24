using System.Numerics;
using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsMontageRootTransformTests
{
    [Fact]
    public void AbsoluteRootPreservesOriginScaleAndIgnoresExtractionNormalization()
    {
        var root=Source(10,2,true);
        Assert.Equal(60,root.SampleAbsolute(.5).Position.X,5);
        Assert.Equal(new AlsDoubleVector(2,2,2),root.SampleAbsolute(.5).Scale);
        Assert.NotEqual(root.SampleAbsolute(.5),root.Extract(0,.5));
    }
    [Fact]
    public void LaterSegmentWinsSharedBoundaryAndLastSegmentHandlesGaps()
    {
        var sampler=new AlsMontageRootTransformSampler([
            new(0,0,1,1,1,1,Source(0)),new(1,0,1,1,1,1,Source(100)),new(3,0,1,1,1,1,Source(200))]);
        Assert.Equal(100,sampler.Sample(1).Position.X,5);
        Assert.Equal(100,sampler.Sample(2.5f).Position.X,5); // Blends identity key -1 with first key 200.
        Assert.Equal(0,sampler.Sample(-.5f).Position.X,5);
        Assert.Equal(0,sampler.Sample(10).Position.X,5);
    }
    [Fact]
    public void RatesLoopsAndZeroRateFallbackFollowNativeSegmentTime()
    {
        var root=Source(0);
        var forward=new AlsMontageRootTransformSampler([new(0,0,1,2,.5f,2,root)]);
        Assert.Equal(25,forward.Sample(1.25f).Position.X,5);
        Assert.Equal(100,forward.Sample(2).Position.X,5);
        var zero=new AlsMontageRootTransformSampler([new(0,0,1,0,2,1,root)]);
        Assert.Equal(50,zero.Sample(.5f).Position.X,5);
        var reverse=new AlsMontageRootTransformSampler([new(0,0,1,-1,1,1,root)]);
        Assert.Equal(75,reverse.Sample(.25f).Position.X,5);
    }
    [Fact]
    public void LastRootUsesNativeDirectEndPositionNotMappedMontagePosition()
    {
        var sampler=new AlsMontageRootTransformSampler([new(.2f,.1f,.3f,1,1,1,Source(100))]);
        Assert.Equal(130,sampler.Sample(.4f).Position.X,4);
        Assert.Equal(140,sampler.SampleLast().Position.X,4);
    }
    [Fact]
    public void AbsoluteTrackUsesIdentityOutsideKeysAndNearestFrameForStep()
    {
        var source=Source(10);
        Assert.Equal(5,source.SampleAbsolute(-.5).Position.X,5);
        Assert.Equal(55,source.SampleAbsolute(1.5).Position.X,5);
        Assert.Equal(AlsPrecisePose.Identity,source.SampleAbsolute(2));
        var keys=new[]{new AlsLocalPose(new(10,0,0),Quaternion.Identity,Vector3.One),
            new AlsLocalPose(new(110,0,0),Quaternion.Identity,Vector3.One)};
        var data=new AlsRawAnimationPoseData(new(0,"root","/test",0),1,1,2,1,
            AlsRawAnimationInterpolation.Step,[0],[],[true],keys,[]);
        var step=new AlsRawRootMotionSampler(data,AlsPrecisePose.Identity,false);
        Assert.Equal(10,step.SampleAbsolute(.49).Position.X);
        Assert.Equal(110,step.SampleAbsolute(.5).Position.X);
        var shortChannel=new AlsRawRootMotionSampler(data,AlsPrecisePose.Identity,false,1);
        Assert.Equal(AlsPrecisePose.Identity,shortChannel.SampleAbsolute(1));
    }
    [Fact]
    public void SourceReferencesAndSegmentOrderAreFrozenAtConstruction()
    {
        var rows=new[]{new AlsMontageRootSegment(0,0,1,1,1,1,Source(10))};
        var sampler=new AlsMontageRootTransformSampler(rows);rows[0]=rows[0] with {Source=Source(100)};
        Assert.Equal(10,sampler.Sample(0).Position.X);
        Assert.Throws<ArgumentException>(()=>sampler.Sample(float.NaN));
        Assert.Throws<ArgumentException>(()=>new AlsMontageRootTransformSampler([]));
        Assert.Throws<ArgumentException>(()=>new AlsMontageRootTransformSampler([rows[0] with {LoopCount=0}]));
    }
    private static AlsRawRootMotionSampler Source(float origin,float scale=1,bool normalized=false)
    {
        var keys=new[]{new AlsLocalPose(new(origin,0,0),Quaternion.Identity,new(scale)),
            new AlsLocalPose(new(origin+100,0,0),Quaternion.Identity,new(scale))};
        return new(new(new(0,"root","/test",0),1,1,2,1,AlsRawAnimationInterpolation.Linear,[0],[],[true],keys,[]),
            new(new(50,0,0),AlsQuaternion.Identity,AlsDoubleVector.One),normalized);
    }
}
