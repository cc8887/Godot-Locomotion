using GodotAls.Core.Animation;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsAnimationGraphLifecycleTests
{
    [Fact]
    public void StartupAndInvalidationUseTheExistingTraversalCounter()
    {
        var startup=new AlsAnimationGraphStartup();var visits=0;
        Assert.False(startup.InitializationCounter.HasUpdated);
        startup.InitializeRoot(10,()=>{Assert.Equal(0,startup.InitializationCounter.Counter);visits++;});
        Assert.True(startup.CacheInvalidatedBones(10,()=>{Assert.Equal(0,startup.CachedBonesCounter.Counter);visits++;}));
        Assert.False(startup.CacheInvalidatedBones(20,()=>throw new InvalidOperationException("Unexpected cache")));
        startup.BoneCache.Invalidate();
        Assert.True(startup.CacheInvalidatedBones(20,()=>visits++));
        Assert.Equal(new AlsGraphTraversalCounter(1,20),startup.CachedBonesCounter);
        Assert.Equal(3,visits);
    }
    [Fact]
    public void FunctionsShareTheirInstanceGateWhileHiddenInstancesRetainInvalidation()
    {
        var shared=new AlsAnimationBoneCacheGate();var hidden=new AlsAnimationBoneCacheGate();var frame=new object();
        shared.Begin(frame);hidden.Begin(frame);var visits=0;
        Assert.True(shared.CacheRoot(frame,()=>visits++));
        Assert.False(shared.CacheRoot(frame,()=>throw new InvalidOperationException("Second function reused the instance")));
        shared.Cancel();hidden.Commit(frame);
        Assert.True(shared.BonesInvalidated);Assert.True(hidden.BonesInvalidated);
        frame=new object();shared.Begin(frame);
        Assert.True(shared.CacheRoot(frame,()=>visits++));shared.Commit(frame);
        Assert.False(shared.BonesInvalidated);Assert.Equal(2,visits);
    }
    [Fact]
    public void ASourceFailureCannotCommitPartialStateCacheHistory()
    {
        var gate=new AlsAnimationBoneCacheGate();var frame=new object();var stamp=new AlsGraphTraversalCounter(1,7);
        gate.Begin(frame);
        Assert.Throws<InvalidOperationException>(()=>gate.CacheRoot(frame,()=>
        {
            Assert.True(gate.CacheState(9,2,stamp,frame));throw new InvalidOperationException("Source failed");
        }));
        Assert.Throws<InvalidOperationException>(()=>gate.Commit(frame));gate.Cancel();
        frame=new object();gate.Begin(frame);
        Assert.True(gate.CacheRoot(frame,()=>Assert.True(gate.CacheState(9,2,stamp,frame))));gate.Commit(frame);
        gate.Invalidate();
        Assert.True(gate.CacheRoot(()=>Assert.False(gate.CacheState(9,2,stamp))));
    }
    [Fact]
    public void ForeignFramesAndPendingInvalidationLeaveTheGateIntact()
    {
        var gate=new AlsAnimationBoneCacheGate();var frame=new object();gate.Begin(frame);
        Assert.Throws<InvalidOperationException>(()=>gate.CacheRoot(new object(),()=>{}));
        Assert.Throws<InvalidOperationException>(()=>gate.Invalidate());
        Assert.True(gate.PreparedInvalidated(frame));gate.Cancel();
        Assert.True(gate.BonesInvalidated);Assert.False(gate.HasPending);
    }
    [Fact]
    public void AFailedPoseCacheSourceOnlyChangesTheCandidateAndRequiresCancellation()
    {
        var cache=new AlsPoseCacheLifecycle(78);var first=new AlsGraphTraversalCounter(1,5);var next=new AlsGraphTraversalCounter(2,6);
        cache.CacheBones(78,first,()=>{});var before=cache.History;var frame=new object();cache.Begin(frame);
        cache.SynchronizeEvaluation(frame,78,new(8,5));
        Assert.Throws<InvalidOperationException>(()=>cache.CacheBones(frame,78,next,()=>throw new InvalidOperationException("Source failed")));
        Assert.Equal(before.ToArray(),cache.History.ToArray());
        Assert.Equal(next,cache.Prepared[0].Bones);Assert.False(cache.Prepared[0].Evaluation.HasUpdated);
        Assert.Throws<InvalidOperationException>(()=>cache.Commit(frame));cache.Cancel();
        frame=new object();cache.Begin(frame);var visits=0;
        Assert.True(cache.CacheBones(frame,78,next,()=>visits++));cache.Commit(frame);
        Assert.Equal(next,cache.History[0].Bones);Assert.Equal(1,visits);
    }
    [Fact]
    public void InitializationIgnoresFrameWhileBoneCacheUsesCounterAndFrame()
    {
        var cache=new AlsPoseCacheLifecycle(78);var visits=0;
        Assert.True(cache.Initialize(78,new(4,10),()=>visits++));
        Assert.False(cache.Initialize(78,new(4,11),()=>visits++));
        Assert.True(cache.CacheBones(78,new(4,10),()=>visits++));
        Assert.True(cache.CacheBones(78,new(4,11),()=>visits++));
        Assert.False(cache.CacheBones(78,new(4,11),()=>visits++));Assert.Equal(3,visits);
    }
    [Fact]
    public void AnUnupdatedCounterNeverMakesAnUninitializedPoseCacheAHit()
    {
        var cache=new AlsPoseCacheLifecycle(78);var visits=0;
        Assert.True(cache.Initialize(78,default,()=>visits++));Assert.True(cache.Initialize(78,default,()=>visits++));
        Assert.True(cache.CacheBones(78,default,()=>visits++));Assert.True(cache.CacheBones(78,default,()=>visits++));
        Assert.Equal(4,visits);
    }
    [Fact]
    public void ProxyPhaseCandidatesShareCountersWithoutSharingFrameOwnership()
    {
        var main=new AlsAnimationProxyTraversal();var linked=new AlsAnimationProxyTraversal();var frame=new object();
        linked.SetIdle(AlsAnimationProxyPhase.CachedBones,new(5,9));
        main.Begin(frame,10);linked.Begin(frame,10);main.Advance(frame,AlsAnimationProxyPhase.Update);
        linked.Synchronize(frame,AlsAnimationProxyPhase.Update,main.Prepared(frame));
        Assert.Equal(0,linked.Prepared(frame).Update.Counter);Assert.Equal(5,linked.Prepared(frame).CachedBones.Counter);
        main.Advance(frame,AlsAnimationProxyPhase.Evaluation);linked.Synchronize(frame,AlsAnimationProxyPhase.Evaluation,main.Prepared(frame));
        Assert.True(linked.EvaluationEntered(frame));
        Assert.Throws<InvalidOperationException>(()=>linked.Commit(new object()));linked.Cancel();main.Cancel();
        Assert.False(linked.Committed.Update.HasUpdated);Assert.False(linked.Committed.Evaluation.HasUpdated);
        Assert.Equal(5,linked.Committed.CachedBones.Counter);
    }
}
