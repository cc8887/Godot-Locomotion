using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Tests;

public sealed class AlsMontageImmediateCallbackTests
{
    private static AlsFrameIdentity Id(int frame)=>new(frame,4,1);
    private static AlsMontageRuntime Bank()=>new([],Enumerable.Range(0,3).Select(i=>
        new AlsAuthoredMontageAsset(i,i,AlsMontageSlot.BaseLayer,0,2,0,1,
            new(AlsActionLifecycleMode.MontageAutoBlendOut,.1f,AlsActionBlendOption.Linear,.2f,AlsActionBlendOption.Linear,-1),i==2)).ToArray(),captureMontageEvents:true);

    [Fact]
    public void BlendInCallbackAppendsBeyondInitialCapacityAndAllChildrenAdvanceThisTick()
    {
        var bank=Bank();var effects=new List<string>();
        void Bind(AlsMontageRuntime b)=>b.BindInstanceMontageCallbacks(1,AlsMontageEventKind.BlendedIn,c=>{
            Assert.Equal(AlsMontageEventPhase.Weight,c.Phase);Assert.False(c.IsQueueing);
            c.DeferEffect(_=>effects.Add("before"));
            for(int n=0;n<10;n++)Assert.True(c.PlayAction(2,1,stopGroup:false));
            c.DeferEffect(_=>effects.Add("after"));
        });
        bank.BeginWithActionRequests(Id(0),.1f,[new(0,1)],beforeWeight:Bind);
        var expected=bank.Candidate.ToArray();Assert.Equal(11,expected.Length);
        Assert.All(expected,m=>{Assert.Equal(.1f,m.Position);Assert.Equal(1,m.Blend.CurrentWeight);});
        Assert.Equal(11,bank.CandidateRootMotionInstance);Assert.Empty(effects);
        bank.Discard();Assert.Empty(bank.Committed.ToArray());Assert.Empty(effects);
        bank.BeginWithActionRequests(Id(0),.1f,[new(0,1)],beforeWeight:Bind);
        Assert.Equal(expected,bank.Candidate.ToArray());bank.DeliverImmediateMontageEvents();
        Assert.Equal(["before","after"],effects);bank.Commit(Id(0));bank.DispatchMontageEventCallbacks();
        Assert.Equal(2,effects.Count);
    }
    [Fact]
    public void LaterCallbackStopsAlreadyWeightedInstanceWithoutChangingItsNotifyWeight()
    {
        var bank=Bank();
        bank.BeginWithActionRequests(Id(0),.1f,[new(0,1,StopGroup:false),new(1,1,StopGroup:false)],beforeWeight:b=>
            b.BindInstanceMontageCallbacks(2,AlsMontageEventKind.BlendedIn,c=>c.StopInstance(1,0,AlsActionBlendOption.Linear)));
        Assert.Single(bank.Candidate.ToArray());Assert.Equal(2,bank.Candidate[0].InstanceId);
        var old=bank.Traversal.ToArray().Single(t=>t.InstanceId==1);
        Assert.True(old.Terminated);Assert.Equal(1,old.NotifyWeight);Assert.Equal(0,old.CurrentPosition);
    }
    [Fact]
    public void StopDuringOwnBlendInUsesPreviousWeightForNotificationsAndPreservesNestedOrder()
    {
        var bank=Bank();var calls=new List<string>();
        void Bind(AlsMontageRuntime b)
        {
            b.BindInstanceMontageCallbacks(1,AlsMontageEventKind.BlendingOut,c=>{
                Assert.True(c.Instances[0].Playing);c.DeferEffect(_=>calls.Add("out"));
            });
            b.BindInstanceMontageCallbacks(1,AlsMontageEventKind.BlendedIn,c=>{
                c.DeferEffect(_=>calls.Add("in-before"));c.StopInstance(1,0,AlsActionBlendOption.Linear);
                Assert.False(c.Instances[0].Playing);c.DeferEffect(_=>calls.Add("in-after"));
            });
        }
        bank.BeginWithActionRequests(Id(0),.05f,[new(0,1)],beforeWeight:Bind);
        bank.Commit(Id(0));bank.DispatchMontageEventCallbacks();Assert.Empty(calls);
        bank.Begin(Id(1),.05f);Assert.Empty(bank.Candidate.ToArray());
        Assert.Equal(.5f,bank.Traversal[0].NotifyWeight);Assert.Empty(calls);
        bank.DeliverImmediateMontageEvents();Assert.Equal(["in-before","out","in-after"],calls);
    }
    [Fact]
    public void UpdateChangesGlobalBindingAtNativeBroadcastAndCancelDropsItsEffects()
    {
        var bank=Bank();var old=new object();var next=new object();var calls=new List<string>();
        bank.BindGlobalMontageCallbacks(AlsMontageEventKind.BlendedIn,old,c=>c.DeferEffect(_=>calls.Add("old")));
        void Bind(AlsMontageRuntime b)=>b.BindInstanceMontageCallbacks(1,AlsMontageEventKind.BlendedIn,c=>{
            c.RemoveGlobal(AlsMontageEventKind.BlendedIn,old);
            c.BindGlobal(AlsMontageEventKind.BlendedIn,next,n=>n.DeferEffect(_=>calls.Add("new")));
        });
        bank.BeginWithActionRequests(Id(0),.1f,[new(0,1)],beforeWeight:Bind);bank.Discard();Assert.Empty(calls);
        bank.BeginWithActionRequests(Id(0),.1f,[new(0,1)],beforeWeight:Bind);
        bank.DeliverImmediateMontageEvents();Assert.Equal(["new"],calls);
    }
    [Fact]
    public void ContextExpiresAndCallbackCannotPublishOrCancelOwningTransaction()
    {
        var bank=Bank();AlsMontageCallbackContext escaped=default;int effects=0;
        bank.BeginWithActionRequests(Id(0),.1f,[new(0,1)],beforeWeight:b=>
            b.BindInstanceMontageCallbacks(1,AlsMontageEventKind.BlendedIn,c=>{
                escaped=c;
                Assert.Throws<InvalidOperationException>(()=>b.Commit(Id(0)));
                Assert.Throws<InvalidOperationException>(b.Discard);
                Assert.Throws<InvalidOperationException>(()=>b.DeliverImmediateMontageEvents());
                Assert.Throws<InvalidOperationException>(()=>b.AcknowledgeImmediateMontageEvents([]));
                c.DeferEffect(_=>effects++);
            }));
        Assert.Throws<InvalidOperationException>(()=>escaped.PlayAction(1,1));
        Assert.Throws<InvalidOperationException>(()=>escaped.DeferEffect(_=>effects++));
        bank.Discard();Assert.Equal(0,effects);
    }
    [Fact]
    public void RequestStopCallbackReplaysAndFailedUpdateRestoresCommittedBindingsAndSerial()
    {
        var bank=Bank();int effects=0;
        bank.BeginWithActionRequests(Id(0),.1f,[new(0,1)],beforeWeight:b=>
            b.BindInstanceMontageCallbacks(1,AlsMontageEventKind.BlendingOut,c=>{
                Assert.Equal(AlsMontageEventPhase.Requests,c.Phase);Assert.True(c.PlayAction(2,1,stopGroup:false));
                c.DeferEffect(_=>effects++);throw new InvalidOperationException("injected");
            }));
        bank.Commit(Id(0));bank.DispatchMontageEventCallbacks();var committed=bank.Committed.ToArray();
        Assert.Throws<InvalidOperationException>(()=>bank.BeginWithActionRequests(Id(1),.1f,[],[new(0,.2f)]));
        Assert.Equal(committed,bank.Committed.ToArray());Assert.Equal(0,effects);
        bank.BeginWithActionRequests(Id(1),.1f,[new(1,1,StopGroup:false)]);
        Assert.Equal(2,bank.Candidate.Length);Assert.Equal(2,bank.Candidate[1].InstanceId);
    }
    [Fact]
    public void PhysicalReceiptRetryReexecutesStateChangesWithoutPublishingEffectsTwice()
    {
        var bank=Bank();int effects=0;
        void Bind(AlsMontageRuntime b)=>b.BindInstanceMontageCallbacks(1,AlsMontageEventKind.BlendedIn,c=>{
            c.PlayAction(2,1,stopGroup:false);c.DeferEffect(_=>effects++);
        });
        bank.BeginWithActionRequests(Id(0),.1f,[new(0,1)],beforeWeight:Bind);
        var expected=bank.Candidate.ToArray();var receipt=bank.ImmediateMontageEvents;
        bank.DeliverImmediateMontageEvents();Assert.Equal(1,effects);bank.Discard();
        bank.BeginWithActionRequests(Id(0),.1f,[new(0,1)],beforeWeight:Bind);
        Assert.Equal(expected,bank.Candidate.ToArray());bank.AcknowledgeImmediateMontageEvents(receipt);
        bank.Commit(Id(0));bank.DispatchMontageEventCallbacks();Assert.Equal(1,effects);
    }
    [Fact]
    public void QueuedUpdateDelegateSurvivesRemovalAndCreatesNextTickRootInstance()
    {
        var bank=Bank();int effects=0;
        bank.BeginWithActionRequests(Id(0),.1f,[new(0,1)],beforeWeight:b=>
            b.BindInstanceMontageCallbacks(1,AlsMontageEventKind.Ended,c=>{
                Assert.False(c.IsCandidate);Assert.Empty(c.Instances.ToArray());
                Assert.True(c.PlayAction(2,1));c.DeferEffect(_=>effects++);
            }));
        bank.Commit(Id(0));bank.DispatchMontageEventCallbacks();
        bank.BeginWithActionRequests(Id(1),.1f,[],[new(0,0)]);
        var proxy=bank.Frame;var frozen=proxy.Evaluations.ToArray();bank.Commit(Id(1));
        Assert.Equal(0,effects);bank.DispatchMontageEventCallbacks();Assert.Equal(1,effects);
        Assert.Equal(frozen,proxy.Evaluations.ToArray());Assert.Equal(2,bank.CommittedRootMotionInstance);
        Assert.Equal(0,Assert.Single(bank.Committed.ToArray()).Position);
        bank.Begin(Id(2),.1f);Assert.Equal(.1f,bank.Candidate[0].Position);
    }
    [Fact]
    public void FailedCandidateCannotLeakItsIdentityOrPhaseIntoCommittedCallback()
    {
        var bank=Bank();int effects=0;
        bank.BeginWithActionRequests(Id(0),.1f,[new(0,1)],beforeWeight:b=>
            b.BindInstanceMontageCallbacks(1,AlsMontageEventKind.BlendingOut,c=>{
                Assert.False(c.IsCandidate);Assert.Equal(Id(1),c.Identity);
                Assert.Equal(AlsMontageEventPhase.PostTick,c.Phase);c.DeferEffect(_=>effects++);
            }));
        bank.Commit(Id(0));bank.DispatchMontageEventCallbacks();
        bank.Begin(Id(1),.01f);bank.StopInstance(1,.2f,AlsActionBlendOption.Linear);bank.Commit(Id(1));
        Assert.Throws<ArgumentException>(()=>bank.BeginWithActionRequests(Id(2),.01f,[new(99,1)]));
        bank.DispatchMontageEventCallbacks();Assert.Equal(1,effects);
    }
}
