using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsGroundVelocityTests
{
    [Fact]
    public void BelowNativeMinimumTickDoesNotChangeVelocity()
    {
        var settings = new AlsGroundVelocity.Settings(2, 1f / 33, false, 0, 25);
        var velocity = new AlsDoubleVector(400, 200, 0);
        Assert.Equal(velocity, AlsGroundVelocity.Calculate(velocity, new(-1500, 0, 0), 1e-7f, 8, 800, 375, 1, settings));
    }

    [Fact]
    public void IntegrationHasNoManagedAllocationAfterWarmup()
    {
        var settings = new AlsGroundVelocity.Settings(2, 1f / 33, false, 0, 25);
        var velocity = new AlsDoubleVector(400, 200, 0);
        for (var i = 0; i < 1000; i++) velocity = Tick(velocity, i, settings);
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) velocity = Tick(velocity, i, settings);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(0, allocated);
        Assert.True(velocity.IsFinite);
    }

    [Fact]
    public void InvalidInputsCannotPoisonMovementState()
    {
        var settings = new AlsGroundVelocity.Settings(0, 1f / 33, false, 0, 25);
        Assert.Throws<ArgumentException>(() => AlsGroundVelocity.Calculate(new(double.NaN, 0, 0), default, .1f, 5, 800, 375, 1, settings));
        Assert.Throws<ArgumentException>(() => AlsGroundVelocity.Calculate(default, default, .1f, 5, 800, 375, 2, settings));
        Assert.Throws<ArgumentException>(() => AlsGroundVelocity.Calculate(default, default, .1f, 5, 800, 375, 1, settings with { BrakingSubStepTime = 0 }));
    }

    private static AlsDoubleVector Tick(AlsDoubleVector velocity, int index, AlsGroundVelocity.Settings settings) =>
        AlsGroundVelocity.Calculate(velocity, index % 100 < 10 ? default : new(index % 200 < 100 ? 1500 : -1500, 0, 0),
            index % 5 == 0 ? .08f : 1f / 60, 4, 800, 375, 1, settings);
}
