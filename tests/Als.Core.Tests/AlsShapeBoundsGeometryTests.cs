using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsShapeBoundsGeometryTests
{
    private static readonly AlsPrecisePose Identity=AlsPrecisePose.Identity;
    private static bool Allows(AlsShapeBoundsGeometry a,AlsPrecisePose pa,AlsShapeBoundsGeometry b,AlsPrecisePose pb,float cull,bool previous=false)
        =>AlsShapeBoundsGeometry.Allows(a,pa,a.Transform(pa),b,pb,b.Transform(pb),cull,previous);
    [Fact]
    public void RotatedThinBoxesUseObbOnlyWhenNotActiveLastStep()
    {
        var box=AlsShapeBoundsGeometry.Polygon(new(new(-10,-.25,-.25),new(10,.25,.25)));
        var a=Identity with {Rotation=AlsQuaternion.FromAxisAngle(Vector3.UnitZ,MathF.PI/4)};
        var b=a with {Position=new AlsDoubleVector(0,2,0).Rotate(a.Rotation)};
        Assert.True(box.Transform(a).Intersects(box.Transform(b)));
        Assert.False(Allows(box,a,box,b,1));
        Assert.False(Allows(box,b,box,a,1));
        Assert.True(Allows(box,a,box,b,1,true));
        Assert.True(Allows(box,a,box,b,1.6f));
        b=b with {Position=new(100,100,0)};
        Assert.False(Allows(box,a,box,b,1,true));
    }
    [Fact]
    public void SphereDistanceRejectsDiagonalAabbFalsePositiveEvenWhenPreviouslyActive()
    {
        var sphere=AlsShapeBoundsGeometry.Sphere(default,1);
        var b=Identity with {Position=new(2.5,2.5,0)};
        Assert.False(Allows(sphere,Identity,sphere,b,1));
        Assert.False(Allows(sphere,Identity,sphere,b,1,true));
        b=b with {Position=new(3,0,0)};
        Assert.True(Allows(sphere,Identity,sphere,b,1));
        b=b with {Position=new(3.00001,0,0)};
        Assert.False(Allows(sphere,Identity,sphere,b,1));
    }
    [Fact]
    public void CapsuleWorldBoundsTransformSegmentBeforeAddingRadius()
    {
        var capsule=AlsShapeBoundsGeometry.Capsule(new(0,0,-10),new(0,0,10),2);
        var pose=Identity with {Rotation=AlsQuaternion.FromAxisAngle(Vector3.UnitY,.7f),Position=new(11,22,33)};
        var tight=capsule.Transform(pose);var loose=capsule.LocalBounds.Transform(pose);
        Assert.True(tight.Min.X>loose.Min.X&&tight.Max.X<loose.Max.X);
        var expected=AlsContactBounds.Segment(new AlsDoubleVector(0,0,-10).Rotate(pose.Rotation)+pose.Position,
            new AlsDoubleVector(0,0,10).Rotate(pose.Rotation)+pose.Position,2);
        Assert.Equal(expected,tight);
    }
    [Fact]
    public void SphereAgainstRotatedBoxUsesOnlyTheNonSphereObbSideAndBothOrdersAgree()
    {
        var sphere=AlsShapeBoundsGeometry.Sphere(default,.1f);
        var box=AlsShapeBoundsGeometry.Polygon(new(new(-10,-.25,-.25),new(10,.25,.25)));
        var pb=Identity with {Rotation=AlsQuaternion.FromAxisAngle(Vector3.UnitZ,MathF.PI/4)};
        var ps=Identity with {Position=new AlsDoubleVector(0,2,0).Rotate(pb.Rotation)};
        Assert.True(sphere.Transform(ps).Intersects(box.Transform(pb)));
        Assert.False(Allows(sphere,ps,box,pb,1));Assert.False(Allows(box,pb,sphere,ps,1));
        Assert.True(Allows(sphere,ps,box,pb,1,true));
        Assert.Throws<ArgumentOutOfRangeException>(()=>Allows(sphere,ps,box,pb,float.NaN));
    }
}
