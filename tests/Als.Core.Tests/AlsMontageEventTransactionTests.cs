using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Tests;

public sealed class AlsMontageEventTransactionTests
{
    private static AlsFrameIdentity Id(int frame)=>new(frame,1,1);
    private static AlsAuthoredMontageAsset Asset(int id)=>new(id,id,AlsMontageSlot.BaseLayer,0,1,0,1,
        new(AlsActionLifecycleMode.MontageAutoBlendOut,.1f,AlsActionBlendOption.Linear,.2f,AlsActionBlendOption.Linear,-1),MontageId:id);

    [Fact]
    public void ImmediateInterruptionReceiptRejectsForeignPayloadAndRetryDoesNotDeliverTwice()
    {
        var bank=new AlsMontageRuntime([], [Asset(0),Asset(1)],captureMontageEvents:true);
        bank.BeginWithActionRequests(Id(0),.1f,[new(0,1)]);bank.Commit(Id(0));bank.DispatchMontageEventCallbacks();
        bank.BeginWithActionRequests(Id(1),.01f,[new(1,1)]);
        var receipt=bank.ImmediateMontageEvents;
        Assert.Single(receipt);Assert.Equal(AlsMontageEventKind.BlendingOut,receipt[0].Kind);
        Assert.True(receipt[0].Interrupted);Assert.Equal(1,receipt[0].InstanceId);
        int callbacks=0;bank.DeliverImmediateMontageEvents(_=>callbacks++);
        Assert.Equal(1,callbacks);bank.Discard();Assert.False(bank.MontageEventsQueued);
        bank.BeginWithActionRequests(Id(1),.01f,[new(1,1)]);
        Assert.Throws<InvalidOperationException>(()=>bank.AcknowledgeImmediateMontageEvents([receipt[0] with{Interrupted=false}]));
        Assert.Equal(receipt,bank.ImmediateMontageEvents);
        bank.AcknowledgeImmediateMontageEvents(receipt);bank.Commit(Id(1));bank.DispatchMontageEventCallbacks(_=>callbacks++);
        Assert.Equal(1,callbacks);
        bank.Begin(Id(2),.2f);
        var ended=bank.QueuedMontageEvents(AlsMontageEventKind.Ended);
        Assert.Single(ended);Assert.Equal(1,ended[0].InstanceId);Assert.True(ended[0].Interrupted);
        bank.Discard();bank.Begin(Id(2),.2f);Assert.Equal(ended,bank.QueuedMontageEvents(AlsMontageEventKind.Ended));
    }

    [Fact]
    public void HoldingDispatchKeepsWeightCompletionQueuedAndCancellationRetainsCommittedContainers()
    {
        var bank=new AlsMontageRuntime([], [Asset(0)],captureMontageEvents:true);
        bank.BeginWithActionRequests(Id(0),.01f,[new(0,1)]);bank.Commit(Id(0));
        bank.Begin(Id(1),.1f);
        Assert.Empty(bank.ImmediateMontageEvents);Assert.Single(bank.QueuedMontageEvents(AlsMontageEventKind.BlendedIn));
        bank.Commit(Id(1));bank.Begin(Id(2),.1f);bank.Discard();
        Assert.True(bank.MontageEventsQueued);Assert.Single(bank.QueuedMontageEvents(AlsMontageEventKind.BlendedIn));
        var calls=new List<AlsMontageEvent>();bank.DispatchMontageEventCallbacks(calls.Add);
        Assert.Single(calls);Assert.False(bank.MontageEventsQueued);
    }
}
