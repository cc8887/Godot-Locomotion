using GodotAls.Core.Physics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsSimulationFilterTests
{
    [Fact]
    public void NativeFilteringRetainsHighChannelsAndRequiresMutualBlocking()
    {
        var a = new AlsSimulationFilter(1UL << 63, 1UL << 40);
        var b = new AlsSimulationFilter(1UL << 40, 1UL << 63);
        Assert.True(a.Allows(b)); Assert.True(b.Allows(a));
        Assert.False(a.Allows(b with { BlockChannels = 0 }));
    }

    [Fact]
    public void NativeRootIgnoreSuppressesFootContactWithoutDisablingKinematicSceneContacts()
    {
        var registry = new AlsContactRegistry(3, 3);
        var root = registry.Register(new(0, AlsPrecisePose.Identity, 1, 1,
            SimulationFilter: new(1UL << 5, 0)));
        var foot = registry.Register(new(1, AlsPrecisePose.Identity, 1, 1,
            SimulationFilter: new(1UL << 5, ulong.MaxValue)));
        var floor = registry.Register(new(2, AlsPrecisePose.Identity, 1, 1,
            SimulationFilter: AlsSimulationFilter.WorldStatic));
        Assert.False(registry.Allows(root.Slot, foot.Slot));
        Assert.True(registry.Allows(foot.Slot, floor.Slot));
        floor = registry.Replace(floor, registry.At(floor.Slot) with { Mask = 2 });
        Assert.False(registry.Allows(foot.Slot, floor.Slot));
        floor = registry.Replace(floor, registry.At(floor.Slot) with { Mask = 1 });
        registry.DisableBodyPair(1, 2, true);
        Assert.False(registry.Allows(foot.Slot, floor.Slot));
    }
}
