using GodotAls.Core.Exchange;

namespace GodotAls.Core.Tests;

public sealed class AlsSlotRegistryTests
{
    [Fact]
    public void ReusingASlotChangesItsGeneration()
    {
        var registry = new AlsSlotRegistry(1);

        var first = registry.Acquire();
        Assert.True(registry.Release(first));

        var second = registry.Acquire();

        Assert.Equal(first.CharacterId, second.CharacterId);
        Assert.True(second.Generation > first.Generation);
        Assert.False(registry.IsCurrent(first));
        Assert.True(registry.IsCurrent(second));
    }

    [Fact]
    public void RejectsDuplicateReleaseAndCapacityOverflow()
    {
        var registry = new AlsSlotRegistry(1);
        var handle = registry.Acquire();

        Assert.Throws<InvalidOperationException>(() => registry.Acquire());
        Assert.True(registry.Release(handle));
        Assert.False(registry.Release(handle));
    }
}
