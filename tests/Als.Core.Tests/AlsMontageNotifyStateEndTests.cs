using System.Collections.Immutable;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Core.Tests;

public sealed class AlsMontageNotifyStateEndTests
{
    private readonly record struct State(int Id,long Instance,bool Direct=true,bool Trigger=true);
    private static AlsMontageNotifyStateEndList<State> Inventory(params State[] states)
    {
        var list=new AlsMontageNotifyStateEndList<State>(s=>s.Direct?s.Instance:0);
        list.Replace(states.ToImmutableArray());return list;
    }
    [Fact]
    public void ReverseTraversalUsesPhysicalInstanceAndSwapRemoval()
    {
        var list=Inventory(new(0,1),new(1,2),new(2,1),new(3,1,false),new(4,1));
        var ends=new List<int>();
        Assert.True(list.End(1,s=>{Assert.Contains(s,list.States);ends.Add(s.Id);}));
        Assert.Equal([4,2,0],ends);Assert.Equal([3,1],list.States.Select(s=>s.Id));
    }
    [Fact]
    public void FilteredStateIsStillRemovedButSequenceOwnerAndMissingContextRemain()
    {
        var list=Inventory(new(0,1,Trigger:false),new(1,1,false),new(2,0));var ends=new List<int>();
        Assert.True(list.End(1,s=>ends.Add(s.Id),s=>s.Trigger));Assert.Empty(ends);
        Assert.Equal([2,1],list.States.Select(s=>s.Id));
    }
    [Fact]
    public void ClearingFromNotifyEndSuppressesEndedDelegates()
    {
        var list=Inventory(new(0,1),new(1,1));var ends=new List<int>();
        Assert.False(list.End(1,s=>{ends.Add(s.Id);list.Clear();}));
        Assert.Equal([1],ends);Assert.Empty(list.States);
    }
    [Fact]
    public void NoComponentKeepsStatesAndPermitsEndedDelegates()
    {
        var list=Inventory(new State(0,1));Assert.True(list.End(1,_=>Assert.Fail("No component must not end a state."),hasComponent:false));
        Assert.Single(list.States);
    }
    [Fact]
    public void NestedEndUsesLiveInventoryAndDoesNotReuseRemovedReference()
    {
        var list=Inventory(new(0,2),new(1,1),new(2,1));var ends=new List<int>();
        Assert.False(list.End(1,s=>{ends.Add(s.Id);if(s.Id==2)Assert.True(list.End(2,t=>ends.Add(t.Id)));}));
        Assert.Equal([2,0],ends);Assert.Equal([2,1],list.States.Select(s=>s.Id));
    }
    [Fact]
    public void CallbackAppendDoesNotVisitNewTailAndSwapMovesItIntoRemovedSlot()
    {
        var list=Inventory(new(0,1),new(1,2));
        Assert.True(list.End(1,_=>list.Replace(list.States.Add(new(2,1)))));
        Assert.Equal([2,1],list.States.Select(s=>s.Id));
    }
    private static AlsMontageRuntime Bank()=>new([],[new(0,0,AlsMontageSlot.BaseLayer,0,2,0,1,
        new(AlsActionLifecycleMode.MontageAutoBlendOut,0,AlsActionBlendOption.Linear,.2f,AlsActionBlendOption.Linear,-1))],captureMontageEvents:true);
    private static AlsFrameIdentity Id(int frame)=>new(frame,9,1);
    [Fact]
    public void CommittedNotifyEndPrecedesCapturedInstanceAndGlobalDelegateAfterRetry()
    {
        var bank=Bank();var list=Inventory(new(0,1),new(1,1));var calls=new List<string>();
        bank.BindMontageNotifyStateEnd(c=>{
            Assert.False(c.IsCandidate);Assert.Equal(AlsMontageEventPhase.PostTick,c.Phase);
            Assert.Empty(c.Instances.ToArray());return list.End(c.Event.InstanceId,s=>calls.Add("notify"+s.Id));});
        bank.BindGlobalMontageEvent(AlsMontageEventKind.Ended,this,_=>calls.Add("global"));
        bank.BeginWithActionRequests(Id(0),.1f,[new(0,1)],beforeWeight:b=>
            b.BindInstanceMontageEvent(1,AlsMontageEventKind.Ended,_=>calls.Add("instance")));
        bank.Commit(Id(0));bank.DispatchMontageEventCallbacks();
        bank.BeginWithActionRequests(Id(1),.1f,[],[new(0,0)]);bank.Discard();Assert.Empty(calls);Assert.Equal(2,list.States.Length);
        bank.BeginWithActionRequests(Id(1),.1f,[],[new(0,0)]);bank.Commit(Id(1));bank.DispatchMontageEventCallbacks();
        Assert.Equal(["notify1","notify0","instance","global"],calls);Assert.Empty(list.States);
        bank.DispatchMontageEventCallbacks();Assert.Equal(4,calls.Count);
    }
    [Fact]
    public void NotifyEndTeardownSuppressesInstanceGlobalAndObserver()
    {
        var bank=Bank();var list=Inventory(new State(0,1));
        bank.BindMontageNotifyStateEnd(c=>list.End(c.Event.InstanceId,_=>list.Clear()));
        bank.BindGlobalMontageEvent(AlsMontageEventKind.Ended,this,_=>Assert.Fail("Global after teardown."));
        bank.BeginWithActionRequests(Id(0),.1f,[new(0,1)],beforeWeight:b=>
            b.BindInstanceMontageEvent(1,AlsMontageEventKind.Ended,_=>Assert.Fail("Instance after teardown.")));
        bank.Commit(Id(0));bank.DispatchMontageEventCallbacks();
        bank.BeginWithActionRequests(Id(1),.1f,[],[new(0,0)]);bank.Commit(Id(1));
        bank.DispatchMontageEventCallbacks(e=>{if(e.Kind==AlsMontageEventKind.Ended)Assert.Fail("Observer after teardown.");});
        Assert.Empty(list.States);
    }
}
