using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsSphereBoxManifoldTests
{
    private static readonly AlsPrecisePose Identity = AlsPrecisePose.Identity;
    private static bool Build(AlsDoubleVector position, float cull, out AlsDetectedContact point) => AlsSphereBoxManifold.Build(
        Vector3.Zero, 5, Identity with { Position = position }, new(-10, -10, -10), new(10, 10, 10), Identity, cull, out point);
    [Fact]
    public void NativeTieTinyGapAndStrictCullRulesArePreserved()
    {
        Assert.True(Build(default, 0, out var inside)); Assert.Equal(-Vector3.UnitZ, inside.Normal1); Assert.Equal(-15, inside.NativePhi);
        Assert.True(Build(new(0, -10.0001, 0), 0, out var tiny)); Assert.Equal(Vector3.UnitX, tiny.Normal1); Assert.Equal(-5, tiny.NativePhi);
        Assert.False(Build(new(18, 0, 0), 3, out _));
        Assert.True(Build(new(17.999, 0, 0), 3, out var separated)); Assert.True(separated.NativePhi > 0);
        Assert.True(Build(new(11, 11, 11), 0, out var corner));
        Assert.InRange(Vector3.Distance(Vector3.Normalize(Vector3.One), corner.Normal1), 0, 1e-6f);
    }
    [Fact]
    public void InvalidDimensionsAndTransformsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => AlsSphereBoxManifold.Build(Vector3.Zero, -1, Identity, new(-10, -10, -10), new(10, 10, 10), Identity, 0, out _));
        Assert.Throws<ArgumentException>(() => Build(new(double.MaxValue, 0, 0), 0, out _));
    }
    [Fact]
    public void RepeatedContactGenerationDoesNotAllocate()
    {
        for (var i = 0; i < 256; i++) Build(new(11, 11, 11), 3, out _);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2048; i++) Build(new(11, 11, 11), 3, out _);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
