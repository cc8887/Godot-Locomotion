using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsFootSupportGeometryTests
{
    private static AlsFootSupportGeometry Shape() => new(2, [new(0, 2, true), new(2, 1, false)],
        [new(0, new(0, 0, -10), .75f), new(1, new(0, 0, -12), .25f), new(1, new(2, 0, -5), 1)]);
    private static AlsPrecisePose[] Pose() => [new(new(0, 0, 20), AlsQuaternion.Identity, AlsDoubleVector.One),
        new(new(0, 0, 30), AlsQuaternion.Identity, AlsDoubleVector.One)];

    [Fact]
    public void SkinWeightsCurrentToePoseAndWorldPlaneDetermineSupport()
    {
        var shape = Shape(); var pose = Pose();
        var hit = new AlsFootTraceRigHit(true, AlsDoubleVector.Zero, new(0, 0, 1));
        Assert.Equal(12, shape.MinimumDistance(true, pose, AlsPrecisePose.Identity, hit), 8);
        Assert.Equal(25, shape.MinimumDistance(false, pose, AlsPrecisePose.Identity, hit), 8);
        var rotation = AlsQuaternion.FromAxisAngle(Vector3.UnitY, .4f);
        var transform = new AlsPrecisePose(new(7, 8, 9), rotation, new(2, 2, 2));
        var plane = new AlsFootTraceRigHit(true, transform.Position, new AlsDoubleVector(0, 0, 1).Rotate(rotation));
        Assert.Equal(24, shape.MinimumDistance(true, pose, transform, plane), 5);
        pose[1] = pose[1] with { Rotation = new(1, 0, 0, 0) };
        Assert.Equal(18, shape.MinimumDistance(true, pose, AlsPrecisePose.Identity, hit), 8);
        Assert.Equal(35, shape.MinimumDistance(false, pose, AlsPrecisePose.Identity, hit), 8);
    }

    [Fact]
    public void InvalidLayoutsAndObservationsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new AlsFootSupportGeometry(2,
            [new(0, 1, true), new(1, 1, false)], [new(2, default, 1), new(0, default, 1)]));
        Assert.Throws<ArgumentException>(() => new AlsFootSupportGeometry(2,
            [new(0, 1, true), new(1, 1, false)], [new(0, default, .5f), new(1, default, 1)]));
        Assert.Throws<ArgumentException>(() => Shape().MinimumDistance(true, Pose(), AlsPrecisePose.Identity,
            new(true, default, new(0, 0, 2))));
        Assert.Throws<ArgumentException>(() => Shape().MinimumDistance(true, Pose(), AlsPrecisePose.Identity, default));
        Assert.Throws<ArgumentException>(() => Shape().MinimumDistance(true, Pose(),
            AlsPrecisePose.Identity with { Scale = new(0, 1, 1) }, new(true, default, new(0, 0, 1))));
    }

    [Fact]
    public void RuntimeSkinningAllocatesNoManagedMemory()
    {
        var shape = Shape(); var pose = Pose(); var transform = AlsPrecisePose.Identity;
        var hit = new AlsFootTraceRigHit(true, default, new(0, 0, 1));
        for (var i = 0; i < 1000; i++) shape.MinimumDistance(true, pose, transform, hit);
        var before = GC.GetAllocatedBytesForCurrentThread(); double sum = 0;
        for (var i = 0; i < 1000; i++) sum += shape.MinimumDistance(true, pose, transform, hit);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(12000, sum); Assert.Equal(0, allocated);
    }
}
