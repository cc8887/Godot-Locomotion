using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Tests;

public sealed class AlsMontageEventPhaseTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(1f / 30)]
    [InlineData(1f / 60)]
    [InlineData(1f / 120)]
    public void EmptyBankEntersQueuePhaseAndCancelDoesNotPublish(float delta)
    {
        var bank=new AlsMontageRuntime([]);var id=new AlsFrameIdentity(0,1,4);
        Assert.False(bank.MontageEventsQueued);
        bank.Begin(id,delta);
        Assert.True(bank.MontageEventsQueued);Assert.False(bank.CommittedMontageEventsQueued);
        Assert.Empty(bank.Evaluation.ToArray());
        Assert.Throws<InvalidOperationException>(()=>bank.DispatchQueuedMontageEvents());
        bank.Discard();Assert.False(bank.MontageEventsQueued);
        bank.Begin(id,delta);bank.Commit(id);Assert.True(bank.MontageEventsQueued);
        bool called=false;
        bank.DispatchQueuedMontageEvents(()=>{called=true;Assert.False(bank.MontageEventsQueued);});
        Assert.True(called);Assert.False(bank.MontageEventsQueued);
    }

    [Fact]
    public void DispatchBelongsToEachActualInstanceAndBankCleanupDoesNotDispatch()
    {
        var first=new AlsMontageRuntime([]);var second=new AlsMontageRuntime([]);
        var id=new AlsFrameIdentity(0,1,4);
        first.Begin(id,.1f);first.Commit(id);second.Begin(id,.1f);second.Commit(id);
        first.DispatchQueuedMontageEvents();
        Assert.False(first.MontageEventsQueued);Assert.True(second.MontageEventsQueued);
        // Bank cleanup is not AnimInstance event dispatch. Only the latter
        // leaves the queue phase, including for an already empty bank.
        second.ClearForLifecycle();Assert.True(second.MontageEventsQueued);
        second.DispatchQueuedMontageEvents();Assert.False(second.MontageEventsQueued);
    }
}
