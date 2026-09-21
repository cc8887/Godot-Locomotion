using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsContactBoundsTests
{
    private static readonly AlsContactDetectorSettings Detector=new(3,.01f,1,1,3);
    [Fact]
    public void DynamicSweepIsBackwardsComponentClampedAndIndependentOfCullScale()
    {
        var box=new AlsContactBounds(new(-1,-2,-3),new(1,2,3));
        var expanded=box.Expand(true,new(300,-300,0),1d/30,Detector);
        Assert.Equal(new AlsDoubleVector(-7,-5,-6),expanded.Min);
        Assert.Equal(new AlsDoubleVector(4,8,6),expanded.Max);
        Assert.Equal(box,box.Expand(false,new(300,-300,0),1d/30,Detector));
        Assert.Equal(new AlsContactBounds(new(-4,-5,-6),new(4,5,6)),
            box.Expand(true,new(300,-300,0),1d/30,Detector with { MaximumVelocityExpansion=0 }));
    }
    [Fact]
    public void UnionIntersectsAtBoundaryAndExcludesStrictSeparation()
    {
        var a=AlsContactBounds.Segment(new(-2,0,0),new(2,0,0),1);
        Assert.True(a.Intersects(new(new(3,-1,-1),new(4,1,1))));
        Assert.False(a.Intersects(new(new(3.000001,-1,-1),new(4,1,1))));
        Assert.Equal(new AlsContactBounds(new(-3,-1,-1),new(8,2,3)),a.Union(new(new(7,0,0),new(8,2,3))));
    }
    [Fact]
    public void RotatedBoundsContainEveryCornerWithTranslationAndSignedScale()
    {
        var box=new AlsContactBounds(new(-1,-2,-3),new(4,5,6));
        var pose=AlsPrecisePose.Identity with {Position=new(100,20,-5),Scale=new(-2,3,1),Rotation=AlsQuaternion.FromAxisAngle(Vector3.UnitZ,.7f)};
        var actual=box.Transform(pose);
        for(var i=0;i<8;i++)
        {
            var p=new AlsDoubleVector((i&1)==0?-1:4,(i&2)==0?-2:5,(i&4)==0?-3:6);
            p=(p*pose.Scale).Rotate(pose.Rotation)+pose.Position;
            Assert.True(actual.Intersects(new(p,p)));
        }
        Assert.Throws<ArgumentException>(()=>new AlsContactBounds(new(1,0,0),default).Validate());
        Assert.Throws<ArgumentException>(()=>box.Expand(true,new(float.NaN,0,0),1d/60,Detector));
    }
}
