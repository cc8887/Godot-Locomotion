using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsBodyInertiaConditioningTests
{
    private static readonly AlsBodyInertiaSettings Settings=new(true,20,2.5f,0,1e-20f,1e-4f,1e-8f);
    [Fact]
    public void CubeConditioningIncludesNativeSmallBodyScale()
    {
        var scale=AlsBodyInertiaConditioning.Calculate(1,new(.06f),new(10),Settings);
        Assert.InRange(Vector3.Distance(scale,new(1.25f/6)),0,1e-7f);
        var large=AlsBodyInertiaConditioning.Calculate(1,new(.015f),new(20),Settings);
        Assert.InRange(Vector3.Distance(large,new(2.5f/6)),0,1e-7f);
    }
    [Fact]
    public void BoundsUseRotatedHalfSizeAndConnectorsUseComOffsets()
    {
        var q=new AlsQuaternion(0,0,System.Math.Sin(System.Math.PI/4),System.Math.Cos(System.Math.PI/4));
        var extents=AlsBodyInertiaConditioning.CollisionExtents(new(90,-2,-3),new(110,2,3),q);
        Assert.True((extents-new AlsDoubleVector(2,10,3)).NearlyZero(1e-12));
        var mass=new AlsPrecisePose(new(100,0,0),q,AlsDoubleVector.One);
        var extended=AlsBodyInertiaConditioning.IncludeConnector(extents,new(100,25,0),mass);
        Assert.True((extended-new AlsDoubleVector(25,10,3)).NearlyZero(1e-12));
    }
    [Fact]
    public void NativeToleranceAndDisabledBranchesReturnIdentity()
    {
        Assert.Equal(Vector3.One,AlsBodyInertiaConditioning.Calculate(0,new(1),new(1),Settings));
        Assert.Equal(Vector3.One,AlsBodyInertiaConditioning.Calculate(1,new(.0001f),new(1),Settings));
        Assert.Equal(Vector3.One,AlsBodyInertiaConditioning.Calculate(1,new(1),new(0,1,1),Settings));
        Assert.Equal(Vector3.One,AlsBodyInertiaConditioning.Calculate(1,new(1),new(1),Settings with{Enabled=false}));
        Assert.Throws<ArgumentOutOfRangeException>(()=>AlsBodyInertiaConditioning.Calculate(1,new(float.NaN),new(1),Settings));
        Assert.Throws<ArgumentOutOfRangeException>(()=>AlsBodyInertiaConditioning.Calculate(1,new(1),new(1),Settings with{MaxRotationRatio=0}));
    }
    [Fact]
    public void CalculationAllocatesNoManagedMemory()
    {
        for(var i=0;i<100;i++)AlsBodyInertiaConditioning.Calculate(1,new(.06f),new(10),Settings);
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<1000;i++)AlsBodyInertiaConditioning.Calculate(1,new(.06f),new(10),Settings);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
    }
}
