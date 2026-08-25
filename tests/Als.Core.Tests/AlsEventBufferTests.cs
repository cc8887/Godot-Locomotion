using GodotAls.Core.Events;

namespace GodotAls.Core.Tests;

public sealed class AlsEventBufferTests
{
    [Fact]
    public void PreservesInsertionOrderAndRejectsOverflow()
    {
        var buffer = new AlsEventBuffer();

        for (var index = 0; index < AlsEventBuffer.Capacity; index++)
        {
            Assert.True(buffer.TryAdd(
                new AlsAnimationEvent(
                    index,
                    index * 0.1f,
                    1f,
                    AlsAnimationEventPhase.Trigger)));
        }

        Assert.False(buffer.TryAdd(
            new AlsAnimationEvent(99, 0f, 1f, AlsAnimationEventPhase.Trigger)));
        Assert.Equal(AlsEventBuffer.Capacity, buffer.Count);
        Assert.Equal(0, buffer[0].EventId);
        Assert.Equal(
            AlsEventBuffer.Capacity - 1,
            buffer[AlsEventBuffer.Capacity - 1].EventId);
    }

    [Fact]
    public void ClearMakesThePreallocatedStorageReusable()
    {
        var buffer = new AlsEventBuffer();

        Assert.True(buffer.TryAdd(
            new AlsAnimationEvent(7, 0.25f, 0.8f, AlsAnimationEventPhase.Begin)));

        buffer.Clear();

        Assert.Equal(0, buffer.Count);
        Assert.True(buffer.TryAdd(
            new AlsAnimationEvent(8, 0.5f, 1f, AlsAnimationEventPhase.End)));
        Assert.Equal(8, buffer[0].EventId);
    }
}
