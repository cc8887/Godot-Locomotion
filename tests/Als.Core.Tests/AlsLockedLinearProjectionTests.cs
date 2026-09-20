using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsLockedLinearProjectionTests
{
    [Fact]
    public void TeleportUsesStrictPerAxisThresholdAndCanRunWithoutSoftProjection()
    {
        var identity=AlsPrecisePose.Identity;
        foreach(var offset in new[]{4f,5f,5.01f,-5.01f})
        {
            var child=identity with {Position=new(offset,offset,0)};
            var solver=new AlsLockedLinearProjection(identity,child,identity,identity,1,Vector3.One,1,0,5);
            var delta=default(AlsProjectionDelta);
            var velocity=solver.Apply(default,ref delta,1d/60,.1f);
            var expected=MathF.Abs(offset)>5?new Vector3(-offset,-offset,0):Vector3.Zero;
            Assert.Equal(expected,delta.Position);
            Assert.Equal(Vector3.Zero,delta.Rotation);
            Assert.True(Vector3.Distance(expected*6,velocity.Linear)<1e-5f);
        }
    }

    [Fact]
    public void CachedChainPropagatesParentCorrectionWithoutMovingItsParent()
    {
        var a=AlsPrecisePose.Identity;
        var b=a with {Position=new(2,0,0)};var c=a with {Position=new(4,0,0)};
        var first=new AlsLockedLinearProjection(a,b,a,a,1,Vector3.One,1,1,0);
        var second=new AlsLockedLinearProjection(b,c,a,a,1,Vector3.One,1,1,0);
        var da=default(AlsProjectionDelta);var db=default(AlsProjectionDelta);var dc=default(AlsProjectionDelta);
        first.Apply(da,ref db,1d/60,.1f);
        second.Apply(db,ref dc,1d/60,.1f);
        Assert.Equal(default,da);
        Assert.Equal(a.Position,AlsLockedLinearProjection.Correct(b,db).Position);
        Assert.Equal(a.Position,AlsLockedLinearProjection.Correct(c,dc).Position);
    }

    [Fact]
    public void DisabledProjectionDoesNotChangeExistingDeltaOrVelocity()
    {
        var p=AlsPrecisePose.Identity;var c=p with {Position=new(100,0,0)};
        var solver=new AlsLockedLinearProjection(p,c,p,p,1,Vector3.One,1,1,5,false);
        var delta=new AlsProjectionDelta(Vector3.One,Vector3.One);var before=delta;
        Assert.Equal(default,solver.Apply(default,ref delta,1d/60,.1f));Assert.Equal(before,delta);
    }

    [Fact]
    public void KinematicChildSkipsTeleportAndVelocityCorrection()
    {
        var p=AlsPrecisePose.Identity;var c=p with {Position=new(100,0,0)};
        var solver=new AlsLockedLinearProjection(p,c,p,p,0,Vector3.One,1,1,5);
        var delta=default(AlsProjectionDelta);
        Assert.Equal(default,solver.Apply(default,ref delta,1d/60,.1f));Assert.Equal(default,delta);
    }

    [Fact]
    public void CacheAndApplyAllocateNoManagedMemoryAfterWarmup()
    {
        var p=AlsPrecisePose.Identity;var c=p with {Position=new(.3,.2,.1)};
        float sum=0;
        for(var i=0;i<256;i++)sum+=Run(p,c);
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<2048;i++)sum+=Run(p,c);
        var allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Assert.Equal(0,allocated);Assert.True(float.IsFinite(sum));
    }
    private static float Run(AlsPrecisePose p,AlsPrecisePose c)
    {
        var solver=new AlsLockedLinearProjection(p,c,p,p,1,Vector3.One,1,1,5);
        var delta=default(AlsProjectionDelta);return solver.Apply(default,ref delta,1d/60,.1f).Linear.X;
    }
}
