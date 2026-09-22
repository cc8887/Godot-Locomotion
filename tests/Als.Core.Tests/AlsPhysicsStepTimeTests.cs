using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsPhysicsStepTimeTests
{
    [Fact]
    public void UnrepresentableOrUnusableStepsAreRejectedBeforeWorldCapture()
    {
        foreach (var seconds in new[] { 0, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity,
                     double.MaxValue, double.Epsilon, (double)float.Epsilon })
            Assert.Throws<ArgumentOutOfRangeException>(() => AlsPhysicsStepTime.FromEngineSeconds(seconds));
    }
}
