using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Tests;

public sealed class AlsMontageCallbackBindingTests
{
    private static AlsFrameIdentity Id(int frame)=>new(frame,1,1);
    private static AlsAuthoredMontageAsset Asset(int id,bool root=false)=>new(id,id,AlsMontageSlot.BaseLayer,0,1,0,1,
        new(AlsActionLifecycleMode.MontageAutoBlendOut,.1f,AlsActionBlendOption.Linear,.2f,AlsActionBlendOption.Linear,-1),root,MontageId:id);
    private static AlsMontageRuntime Bank()=>new([], [Asset(0),Asset(1),Asset(2,true)],captureMontageEvents:true);
    private static void Tick(AlsMontageRuntime bank,int frame,float delta=.1f)
    {bank.Begin(Id(frame),delta);bank.Commit(Id(frame));bank.DispatchMontageEventCallbacks();}

    [Fact]
    public void RealStoppedInstanceCapturesOldDelegateAndGlobalUsesCurrentBinding()
    {
        var bank=Bank();var calls=new List<string>();var old=new object();var next=new object();
        bank.BindGlobalMontageEvent(AlsMontageEventKind.BlendingOut,old,_=>calls.Add("old-global"));
        bank.BeginWithActionRequests(Id(0),.1f,[new(0,1)],beforeWeight:b=>
            b.BindInstanceMontageEvent(1,AlsMontageEventKind.BlendingOut,_=>{
                calls.Add("captured");b.RemoveGlobalMontageEvent(AlsMontageEventKind.BlendingOut,old);
                b.BindGlobalMontageEvent(AlsMontageEventKind.BlendingOut,next,_=>calls.Add("current-global"));
            }));
        bank.DeliverImmediateMontageEvents();bank.Commit(Id(0));bank.DispatchMontageEventCallbacks();
        bank.Begin(Id(1),.01f);bank.StopInstance(1,.2f,AlsActionBlendOption.Linear);
        Assert.True(bank.BindInstanceMontageEvent(1,AlsMontageEventKind.BlendingOut,_=>calls.Add("rebound")));
        bank.Commit(Id(1));bank.DispatchMontageEventCallbacks();
        Assert.Equal(["captured","current-global"],calls);
    }
    [Fact]
    public void NestedStopReadsUpdatedLookupAndPreservesShorterSelfStop()
    {
        var bank=Bank();var calls=new List<string>();
        bank.BeginWithActionRequests(Id(0),.1f,[new(0,1,StopGroup:false),new(1,1,StopGroup:false)],beforeWeight:b=>{
            b.BindInstanceMontageEvent(1,AlsMontageEventKind.BlendingOut,e=>{
                calls.Add("outer");Assert.Equal(0,b.ActiveActionInstance(0));
                Assert.Equal(0,b.LiveInstances[0].Blend.DesiredWeight);
                b.StopInstance(1,.01f,AlsActionBlendOption.Linear);
                b.StopInstance(2,.05f,AlsActionBlendOption.Linear);calls.Add("return");
            });
            b.BindInstanceMontageEvent(2,AlsMontageEventKind.BlendingOut,_=>calls.Add("nested"));
        });
        bank.DeliverImmediateMontageEvents();bank.Commit(Id(0));bank.DispatchMontageEventCallbacks();
        bank.Begin(Id(1),.01f);bank.StopInstance(1,.2f,AlsActionBlendOption.Linear);bank.Commit(Id(1));bank.DispatchMontageEventCallbacks();
        Assert.Equal(["outer","nested","return"],calls);
        Assert.Equal(.01f,bank.Committed[0].BlendTime);Assert.Equal(.05f,bank.Committed[1].BlendTime);
        Assert.All(bank.Committed.ToArray(),x=>Assert.False(x.OwnsActiveActionLookup));
    }
    [Fact]
    public void EndedCallbackReplaysIntoLiveBankWithoutChangingFrozenProxyPose()
    {
        var bank=Bank();int callbacks=0;
        bank.BeginWithActionRequests(Id(0),.1f,[new(0,1)],beforeWeight:b=>
            b.BindInstanceMontageEvent(1,AlsMontageEventKind.Ended,_=>{callbacks++;Assert.True(b.PlayAction(2,1));}));
        bank.DeliverImmediateMontageEvents();bank.Commit(Id(0));bank.DispatchMontageEventCallbacks();
        bank.Begin(Id(1),.01f);bank.StopInstance(1,0,AlsActionBlendOption.Linear);bank.Commit(Id(1));bank.DispatchMontageEventCallbacks();
        bank.Begin(Id(2),.01f);var proxy=bank.Frame;var frozen=proxy.Evaluations.ToArray();bank.Commit(Id(2));bank.DispatchMontageEventCallbacks();
        Assert.Equal(1,callbacks);Assert.Equal(frozen,proxy.Evaluations.ToArray());
        var child=Assert.Single(bank.Committed.ToArray());Assert.Equal(2,child.InstanceId);Assert.Equal(2,child.ActionDefinitionId);
        Assert.Equal(0,child.Position);Assert.Equal(0,child.Blend.CurrentWeight);Assert.Equal(2,bank.CommittedRootMotionInstance);
        Tick(bank,3);Assert.Equal(.1f,bank.Committed[0].Position);
    }
    [Fact]
    public void CancelledBindingAndGlobalChangesDoNotReplaceCommittedDelegates()
    {
        var bank=Bank();var calls=new List<string>();var listener=new object();
        bank.BeginWithActionRequests(Id(0),.1f,[new(0,1)],beforeWeight:b=>{
            b.BindInstanceMontageEvent(1,AlsMontageEventKind.BlendingOut,_=>calls.Add("old"));
            b.BindGlobalMontageEvent(AlsMontageEventKind.BlendingOut,listener,_=>calls.Add("global"));
        });
        bank.DeliverImmediateMontageEvents();bank.Commit(Id(0));bank.DispatchMontageEventCallbacks();
        bank.Begin(Id(1),.01f);bank.BindInstanceMontageEvent(1,AlsMontageEventKind.BlendingOut,_=>calls.Add("cancelled"));
        bank.RemoveGlobalMontageEvent(AlsMontageEventKind.BlendingOut,listener);bank.StopInstance(1,.2f,AlsActionBlendOption.Linear);bank.Discard();
        Assert.Empty(calls);bank.Begin(Id(1),.01f);bank.StopInstance(1,.2f,AlsActionBlendOption.Linear);bank.Commit(Id(1));bank.DispatchMontageEventCallbacks();
        Assert.Equal(["old","global"],calls);
    }
    [Fact]
    public void CapturedEndedDelegateSurvivesPhysicalRemovalAndIdleRebindingTargetsLiveInstancesOnly()
    {
        var bank=Bank();int ended=0;
        bank.BeginWithActionRequests(Id(0),.1f,[new(0,1)]);bank.Commit(Id(0));bank.DispatchMontageEventCallbacks();
        Assert.True(bank.BindInstanceMontageEvent(1,AlsMontageEventKind.Ended,_=>ended++));
        bank.Begin(Id(1),.01f);bank.StopInstance(1,0,AlsActionBlendOption.Linear);bank.Commit(Id(1));bank.DispatchMontageEventCallbacks();
        bank.Begin(Id(2),.01f);Assert.Empty(bank.Candidate.ToArray());bank.Commit(Id(2));
        Assert.False(bank.BindInstanceMontageEvent(1,AlsMontageEventKind.Ended,null));
        bank.DispatchMontageEventCallbacks();Assert.Equal(1,ended);
    }
    [Fact]
    public void DeferredImmediateDelegateHasReceiptAndExplicitlyRejectsPreAdvanceMutation()
    {
        var bank=Bank();int calls=0;
        bank.BeginWithActionRequests(Id(0),.1f,[new(0,1)],beforeWeight:b=>
            b.BindInstanceMontageEvent(1,AlsMontageEventKind.BlendingOut,_=>{
                calls++;Assert.Throws<InvalidOperationException>(()=>b.PlayAction(2,1));
                Assert.Throws<InvalidOperationException>(()=>b.BindInstanceMontageEvent(1,AlsMontageEventKind.Ended,null));
                Assert.Throws<InvalidOperationException>(()=>b.Commit(Id(1)));
                Assert.Throws<InvalidOperationException>(b.Discard);
                Assert.Throws<InvalidOperationException>(b.StopForRagdoll);
                Assert.Throws<InvalidOperationException>(()=>b.AcknowledgeImmediateMontageEvents([]));
            }));
        bank.Commit(Id(0));bank.DispatchMontageEventCallbacks();
        bank.BeginWithActionRequests(Id(1),.01f,[new(1,1)]);var receipt=bank.ImmediateMontageEvents;
        Assert.Equal(0,calls);bank.DeliverImmediateMontageEvents();Assert.Equal(1,calls);bank.Discard();
        bank.BeginWithActionRequests(Id(1),.01f,[new(1,1)]);bank.AcknowledgeImmediateMontageEvents(receipt);
        bank.Commit(Id(1));bank.DispatchMontageEventCallbacks();Assert.Equal(1,calls);
    }
    [Fact]
    public void ZeroDurationStopKeepsPlayingThroughInstanceAndGlobalCallbacks()
    {
        var bank=Bank();var seen=new List<bool>();
        bank.BindGlobalMontageEvent(AlsMontageEventKind.BlendingOut,new object(),_=>seen.Add(bank.LiveInstances[0].Playing));
        bank.BeginWithActionRequests(Id(0),.01f,[new(0,1)],beforeWeight:b=>{
            b.BindInstanceMontageEvent(1,AlsMontageEventKind.BlendingOut,_=>{
                seen.Add(b.LiveInstances[0].Playing);Assert.Equal(0,b.ActiveActionInstance(0));Assert.Equal(0,b.LiveInstances[0].Blend.DesiredWeight);
            });
            b.BindInstanceMontageEvent(1,AlsMontageEventKind.BlendedIn,_=>{
                b.StopInstance(1,0,AlsActionBlendOption.Linear);Assert.False(b.LiveInstances[0].Playing);
            });
        });
        bank.Commit(Id(0)); // Hold dispatch, so completion is queued next tick.
        bank.Begin(Id(1),.1f);bank.Commit(Id(1));bank.DispatchMontageEventCallbacks();
        Assert.Equal([true,true],seen);Assert.False(bank.Committed[0].Playing);
    }
    [Fact]
    public void DispatchExceptionsRestoreBankAndLifecycleCleanupReleasesBindings()
    {
        var bank=Bank();bank.BeginWithActionRequests(Id(0),.1f,[new(0,1)],beforeWeight:b=>
            b.BindInstanceMontageEvent(1,AlsMontageEventKind.BlendingOut,_=>{b.PlayAction(2,1,stopGroup:false);throw new InvalidOperationException("injected");}));
        bank.Commit(Id(0));bank.DispatchMontageEventCallbacks();bank.Begin(Id(1),.01f);bank.StopInstance(1,.2f,AlsActionBlendOption.Linear);bank.Commit(Id(1));
        Assert.Throws<InvalidOperationException>(()=>bank.DispatchMontageEventCallbacks());Assert.Equal(2,bank.Committed.Length);
        bank.ClearForLifecycle();Assert.Empty(bank.Committed.ToArray());Tick(bank,2);
    }
}
