using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsGjkSimplexTests
{
    [Fact]
    public void OriginInsideTetrahedronRetainsFourWitnesses()
    {
        AlsDoubleVector[] p=[new(1,0,0),new(0,1,0),new(0,0,1),new(-1,-1,-1)];
        var a=p.ToArray();var b=new AlsDoubleVector[4];var weights=new double[4];var count=4;
        Assert.Equal(default,AlsGjkSimplex.Closest(p,ref count,weights,a,b));Assert.Equal(4,count);
        Assert.All(weights,w=>Assert.Equal(.25,w));
    }
    [Fact]
    public void DegenerateLineCompactsWitnessIdentityAndRejectsNonfiniteWithoutMutation()
    {
        AlsDoubleVector[] p=[new(2,0,0),new(1,0,0)];AlsDoubleVector[] a=[new(7,0,0),new(8,0,0)];
        AlsDoubleVector[] b=[new(5,0,0),new(7,0,0)];var weights=new double[2];var count=2;
        Assert.Equal(new AlsDoubleVector(1,0,0),AlsGjkSimplex.Closest(p,ref count,weights,a,b));
        Assert.Equal(1,count);Assert.Equal(new AlsDoubleVector(8,0,0),a[0]);Assert.Equal(1,weights[0]);
        p[0]=new(double.NaN,0,0);var old=a.ToArray();
        Assert.Throws<ArgumentException>(()=>AlsGjkSimplex.Closest(p,ref count,weights,a,b));Assert.Equal(old,a);Assert.Equal(1,count);
    }
    [Fact]
    public void RepeatedTriangleReductionDoesNotAllocate()
    {
        Span<AlsDoubleVector> p=stackalloc AlsDoubleVector[4],a=stackalloc AlsDoubleVector[4],b=stackalloc AlsDoubleVector[4];
        Span<double> weights=stackalloc double[4];b.Clear();
        long before=0;
        for(var i=0;i<1020;i++)
        {
            if(i==20)before=GC.GetAllocatedBytesForCurrentThread();
            p[0]=new(1,0,2);p[1]=new(-1,1,2);p[2]=new(-1,-1,2);p.CopyTo(a);var count=3;
            AlsGjkSimplex.Closest(p,ref count,weights,a,b);
        }
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
    }
}
