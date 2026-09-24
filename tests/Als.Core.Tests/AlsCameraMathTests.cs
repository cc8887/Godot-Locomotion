using GodotAls.Core.Camera;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsCameraMathTests
{
    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void PivotLagUsesCameraAxesAndResetsOnTeleport(int hz)
    {
        var target = new AlsDoubleVector(100, 200, 300);
        var result = AlsCameraMath.PivotLag(default, target, 90, 1f / hz, 0, 1, 0, true);
        // At yaw 90 the instantaneous forward X damper acts along world Y.
        Assert.InRange(result.X, 0.1, 3);
        Assert.Equal(200, result.Y, 9);
        Assert.Equal(300, result.Z, 9);
        Assert.Equal(target, AlsCameraMath.PivotLag(default, target, 90, 1f / hz, 1, 1, 1, false));
        Assert.True(AlsCameraMath.AllowLag(true, default, new(200, 0, 0), 200));
        Assert.False(AlsCameraMath.AllowLag(true, default, new(200.0001, 0, 0), 200));
        Assert.True(AlsCameraMath.AllowLag(true, default, target, 0));
        Assert.False(AlsCameraMath.AllowLag(false, default, target, 0));
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void TraceRetractsImmediatelyAndExtendsGradually(int hz)
    {
        var end = new AlsDoubleVector(0, 400, 0);
        var hit = new AlsDoubleVector(0, 100, 0);
        var inward = AlsCameraMath.TraceDistance(default, end, hit, 1, 1f / hz, .2f, true, true);
        Assert.Equal(hit, inward.Location); Assert.Equal(.25f, inward.Ratio);
        var outward = AlsCameraMath.TraceDistance(default, end, end, inward.Ratio, 1f / hz, .2f, true, true);
        Assert.InRange(outward.Location.Y, 100.01, 140);
        Assert.InRange(outward.Ratio, .25001f, .35f);
        Assert.Equal((end, 1f), AlsCameraMath.TraceDistance(default, end, end, inward.Ratio, 1f / hz, .2f, false, true));
        Assert.Equal((hit, 1f), AlsCameraMath.TraceDistance(default, end, hit, inward.Ratio, 1f / hz, .2f, true, false));
        Assert.Equal((hit, 1f), AlsCameraMath.TraceDistance(default, default, hit, inward.Ratio, 1f / hz, .2f, true, true));
    }

    [Fact]
    public void ZeroTimeAndInvalidInputsDoNotAdvanceOrProduceNan()
    {
        Assert.Equal(0, AlsCameraMath.DamperAlpha(0, 0));
        Assert.Equal(new AlsAimingRotation(10, 20, 30),
            AlsCameraMath.Rotation(new(10, 20, 30), new(0, 40, 0), 0, .2f, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsCameraMath.DamperAlpha(float.NaN, .2f));
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsCameraMath.DamperAlpha(.1f, -1));
        Assert.Throws<ArgumentException>(() => AlsCameraMath.PivotLag(default, new(double.NaN, 0, 0), 0, .1f, 0, 0, 0, false));
    }
}
