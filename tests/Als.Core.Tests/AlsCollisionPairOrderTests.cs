using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsCollisionPairOrderTests
{
    [Fact]
    public void CreationOrderIsIndependentOfShapeSlotsAndAdvancesOnlyOnBodyReuse()
    {
        var registry = new AlsContactRegistry(3, 2); var pose = AlsPrecisePose.Identity;
        var last = registry.Register(new(2, pose, 1, 1));
        registry.Register(new(0, pose, 1, 1));
        Assert.Equal(2ul, registry.ParticleOrderAt(2)); Assert.Equal(0ul, registry.ParticleOrderAt(0));
        registry.Replace(last, new(2, pose, 1, 1));
        Assert.Equal(2ul, registry.ParticleOrderAt(2));
        Assert.True(AlsCollisionPairOrder.ShouldReverse(AlsBoundsShapeKind.Capsule, registry.ParticleOrderAt(0), true,
            AlsBoundsShapeKind.Capsule, registry.ParticleOrderAt(2), true));
        registry.RebindBody(0);
        Assert.Equal(3ul, registry.ParticleOrderAt(0));
        Assert.False(AlsCollisionPairOrder.ShouldReverse(AlsBoundsShapeKind.Capsule, registry.ParticleOrderAt(0), true,
            AlsBoundsShapeKind.Capsule, registry.ParticleOrderAt(2), true));
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.RebindBody(3));
        registry.RebindBody(2); Assert.Equal(4ul, registry.ParticleOrderAt(2));
    }
    [Theory]
    [InlineData(AlsBoundsShapeKind.Polygon, false, AlsBoundsShapeKind.Polygon, true, true)]
    [InlineData(AlsBoundsShapeKind.Polygon, true, AlsBoundsShapeKind.Polygon, false, false)]
    [InlineData(AlsBoundsShapeKind.Sphere, false, AlsBoundsShapeKind.Polygon, true, false)]
    [InlineData(AlsBoundsShapeKind.Polygon, true, AlsBoundsShapeKind.Capsule, false, true)]
    [InlineData(AlsBoundsShapeKind.Capsule, true, AlsBoundsShapeKind.Sphere, true, true)]
    public void ShapeDispatchTakesPrecedenceOverBroadphaseMotionOrder(AlsBoundsShapeKind a, bool dynamicA,
        AlsBoundsShapeKind b, bool dynamicB, bool expected) =>
        Assert.Equal(expected, AlsCollisionPairOrder.ShouldReverse(a, 0, dynamicA, b, 1, dynamicB));
}
