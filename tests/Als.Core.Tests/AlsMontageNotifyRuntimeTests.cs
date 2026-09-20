using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsMontageNotifyRuntimeTests
{
    [Fact]
    public void HiddenSlotStillQueuesDirectStateAndRetainsPreviousRelevanceForOneFrame()
    {
        var runtime=new AlsMontageNotifyRuntime(Binding());
        runtime.Begin(new(1,0,1),[Tick(1)]); runtime.Complete(0);
        Assert.Single(runtime.DirectNotifies.ToArray()); Assert.Empty(runtime.Notifies.ToArray()); Assert.Equal(2,runtime.FilteredCount);
        runtime.Commit(new(1,0,1)); runtime.Begin(new(2,0,1),[]); runtime.Complete(4); runtime.Commit(new(2,0,1));
        runtime.Begin(new(3,0,1),[Tick(1)]); runtime.Complete(0); Assert.Single(runtime.Notifies.ToArray()); runtime.Commit(new(3,0,1));
        runtime.Begin(new(4,0,1),[Tick(1)]); runtime.Complete(0); Assert.Empty(runtime.Notifies.ToArray());
    }
    [Fact]
    public void ConcurrentStateReferencesRetainFirstInstanceButInstantNotifiesDoNotDeduplicate()
    {
        var runtime=new AlsMontageNotifyRuntime(Binding());
        runtime.Begin(new(1,0,1),[Tick(55),Tick(77)]); runtime.Complete(4);
        Assert.Single(runtime.DirectNotifies.ToArray()); Assert.Equal(55,runtime.DirectNotifies[0].PlaybackEpoch);
        Assert.Equal(new long[]{55,77},runtime.Notifies.ToArray().Select(n=>n.PlaybackEpoch));
        var state=runtime.Candidate; var direct=runtime.DirectNotifies.ToArray(); var slot=runtime.Notifies.ToArray();
        runtime.Discard(); Assert.Equal(default,runtime.Committed);
        runtime.Begin(new(1,0,1),[Tick(55),Tick(77)]); runtime.Complete(4);
        Assert.Equal(state,runtime.Candidate); Assert.Equal(direct,runtime.DirectNotifies.ToArray()); Assert.Equal(slot,runtime.Notifies.ToArray());
    }
    [Theory]
    [InlineData(.499f,false,0,0)] [InlineData(.5f,false,0,2)]
    [InlineData(1,true,0,0)] [InlineData(1,false,2,0)]
    public void BothQueuesRespectNativeWeightServerAndLod(float weight,bool server,int lod,int expected)
    {
        var runtime=new AlsMontageNotifyRuntime(Binding());
        runtime.Begin(new(1,0,1),[Tick(1) with {NotifyWeight=weight}],server,lod); runtime.Complete(4);
        Assert.Equal(expected,runtime.FilteredCount);
    }
    [Fact]
    public void InterruptedInstancesSuppressBothQueuesAndNaturalTerminationDoesNot()
    {
        var runtime=new AlsMontageNotifyRuntime(Binding());
        runtime.Begin(new(1,0,1),[Tick(1) with {Interrupted=true},Tick(2) with {Terminated=true}]); runtime.Complete(4);
        Assert.Equal(2,runtime.FilteredCount); Assert.Equal(2,runtime.DirectNotifies[0].PlaybackEpoch);
        Assert.Equal(2,runtime.Notifies[0].PlaybackEpoch);
    }
    [Fact]
    public void SegmentMappingUsesSequenceTimeForExtractionAndMontageTimeForContext()
    {
        var runtime=new AlsMontageNotifyRuntime(Binding(.2f,2));
        runtime.Begin(new(1,0,1),[Tick(5) with {PreviousPosition=0,CurrentPosition=.1f}]); runtime.Complete(4);
        Assert.Single(runtime.Notifies.ToArray()); Assert.Equal(.1f,runtime.Notifies[0].Reference.CurrentTime);
    }
    [Fact]
    public void OverflowFaultsCandidateWithoutAdvancingCommittedRandomSeed()
    {
        var runtime=new AlsMontageNotifyRuntime(Binding());
        runtime.Begin(new(1,0,1),[Tick(1)]); runtime.Complete(4); runtime.Commit(new(1,0,1)); var before=runtime.Committed;
        Assert.Throws<InvalidOperationException>(()=>runtime.Begin(new(2,0,1),Enumerable.Range(1,65).Select(i=>Tick(i)).ToArray()));
        Assert.Throws<InvalidOperationException>(()=>runtime.Commit(new(2,0,1))); Assert.Equal(before,runtime.Committed);
        runtime.Discard(); runtime.Begin(new(2,0,1),[Tick(2)]); runtime.Complete(4); runtime.Commit(new(2,0,1));
    }
    [Fact]
    public void PreparingAndDiscardingMixedQueuesAllocatesNothingAfterWarmup()
    {
        var runtime=new AlsMontageNotifyRuntime(Binding()); var ticks=new[]{Tick(1),Tick(2)};
        for(var i=0;i<1000;i++)Run(); var before=GC.GetAllocatedBytesForCurrentThread(); for(var i=0;i<10000;i++)Run();
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
        void Run(){runtime.Begin(new(1,0,1),ticks);runtime.Complete(4);runtime.Discard();}
    }
    private static AlsMontageTraversal Tick(long id)=>new(id,20,AlsMontageSlot.BaseLayer,.1f,.4f,1,false,false,0);
    private static AlsMontageNotifyBinding Binding(float start=0,float rate=1)=>new([],
        [new(40,0,0,-1,40,0,.5f,1,AlsAssetNotifyFilterType.Lod,2,AlsTimelineTickMode.Queued,true,false,false),
         new(41,0,0,41,-1,1,.5f,1,AlsAssetNotifyFilterType.Lod,2,AlsTimelineTickMode.Queued,true,false,false)],
        [new(40,0,.8f),new(41,.3f,.3f)],
        [new(40,10,0,10,AlsTimelineSourceKind.Montage,0,0,0,0,.8f,.5f,AlsTimelineEventKind.Generic,AlsTimelineTickMode.Queued,default),
         new(41,20,0,11,AlsTimelineSourceKind.MontageSegmentAnimation,0,0,0,.3f,0,.5f,AlsTimelineEventKind.Generic,AlsTimelineTickMode.Queued,default)],
        [new(0,20,AlsMontageSlot.BaseLayer,10,0,1,1,0,1,true),new(0,20,AlsMontageSlot.BaseLayer,11,1,1,1,start,rate,false)]);
}
