using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsPolygonQueryCacheTests
{
    private static readonly AlsContactPairKey Key=new(new(1,1,0,1),new(2,1,1,1));
    private static readonly AlsPrecisePose Pose=AlsPrecisePose.Identity with {Position=new(.1,.2,1.5)};
    private static AlsConvexManifoldResult Query(AlsPolygonQueryCache owner,AlsContactPairKey key,float margin=0)
    {
        Span<AlsDetectedContact> points=stackalloc AlsDetectedContact[4];
        return owner.Query(key,new AlsBoxPolygonShape(new(1,1,1),margin),new AlsBoxPolygonShape(new(1,1,1),margin),
            Pose,points,0,1e-6,1e-6,1,.001f);
    }
    private static void Commit(AlsPolygonQueryCache owner){owner.StageCommit();owner.PublishCommit();}
    private static AlsGjkCache Snapshot(AlsPolygonQueryCache owner,AlsContactPairKey key)
    {var result=new AlsGjkCache();Assert.True(owner.CopyCommitted(key,result));return result;}
    [Fact]
    public void EndpointReversalUsesOneSlotButNeverReusesOppositeWitnessCoordinates()
    {
        var owner = new AlsPolygonQueryCache(2, captureQueries: true);
        var reverse = new AlsContactPairKey(Key.Shape1, Key.Shape0);
        owner.PrepareStep(); Query(owner, Key); Commit(owner);
        owner.PrepareStep(); Query(owner, reverse);
        var before = new AlsGjkCache(); var after = new AlsGjkCache();
        Assert.True(owner.CopyPendingQuery(reverse, before, after)); Assert.Equal(0, before.Count);
        owner.Abort(); Assert.True(owner.CopyCommitted(Key, new())); Assert.False(owner.CopyCommitted(reverse, new()));
        owner.PrepareStep(); Query(owner, reverse); Commit(owner);
        Assert.Equal(1, owner.CachedPairs); Assert.False(owner.CopyCommitted(Key, new()));
        var cold = new AlsPolygonQueryCache(2); cold.PrepareStep(); Query(cold, reverse); Commit(cold);
        Assert.Equal(Snapshot(cold, reverse).WitnessA.ToArray(), Snapshot(owner, reverse).WitnessA.ToArray());
        Assert.Equal(Snapshot(cold, reverse).Weights.ToArray(), Snapshot(owner, reverse).Weights.ToArray());
        owner.PrepareStep(); owner.Release(Key); Commit(owner);
        Assert.True(owner.CopyCommitted(reverse, new()));
        owner.PrepareStep(); owner.Release(reverse); Commit(owner); Assert.Equal(0, owner.CachedPairs);
    }
    [Fact]
    public void QueryAndReleaseAreProvisionalUntilPublication()
    {
        var owner=new AlsPolygonQueryCache(3);owner.PrepareStep();Assert.True(Query(owner,Key).Count>0);
        Assert.False(owner.CopyCommitted(Key,new()));owner.Abort();Assert.Equal(0,owner.CachedPairs);
        owner.PrepareStep();Query(owner,Key);Commit(owner);var saved=Snapshot(owner,Key);
        owner.PrepareStep();owner.Release(Key);owner.StageCommit();owner.Abort();
        Assert.Equal(saved.WitnessA.ToArray(),Snapshot(owner,Key).WitnessA.ToArray());Assert.Equal(1,owner.CachedPairs);
        owner.PrepareStep();owner.Release(Key);Commit(owner);Assert.False(owner.CopyCommitted(Key,new()));Assert.Equal(0,owner.CachedPairs);
    }
    [Fact]
    public void OmittedQueriesRetainConstraintCacheUntilExplicitRetirement()
    {
        var owner=new AlsPolygonQueryCache(2);owner.PrepareStep();Query(owner,Key);Commit(owner);
        var saved=Snapshot(owner,Key);
        owner.PrepareStep();Commit(owner);
        Assert.Equal(saved.WitnessA.ToArray(),Snapshot(owner,Key).WitnessA.ToArray());
        owner.Reset();Assert.Equal(0,owner.CachedPairs);Assert.Equal(0,owner.CompletedSteps);
    }
    [Fact]
    public void ParticleSeparationRetiresWithoutQueryButFailedAttemptPreservesCache()
    {
        var registry=new AlsContactRegistry(2,2);
        registry.Register(new(0,AlsPrecisePose.Identity,1,1));registry.Register(new(1,AlsPrecisePose.Identity,1,1));
        var owner=new AlsPolygonQueryCache(2);owner.PrepareStep();Query(owner,Key);Commit(owner);
        var saved=Snapshot(owner,Key);
        AlsContactBounds?[] bounds=[new(new(-1,-1,-1),new(1,1,1)),new(new(10,10,10),new(11,11,11))];
        owner.PrepareStep();owner.RetireSeparatedPairs(registry,bounds);owner.StageCommit();owner.Abort();
        Assert.Equal(saved.WitnessA.ToArray(),Snapshot(owner,Key).WitnessA.ToArray());Assert.Equal(1,owner.CachedPairs);
        owner.PrepareStep();owner.RetireSeparatedPairs(registry,bounds);Commit(owner);
        Assert.Equal(0,owner.CachedPairs);Assert.False(owner.CopyCommitted(Key,new()));
        owner.PrepareStep();Query(owner,Key);Commit(owner);
        var cold=new AlsPolygonQueryCache(2);cold.PrepareStep();Query(cold,Key);Commit(cold);
        Assert.Equal(Snapshot(cold,Key).WitnessA.ToArray(),Snapshot(owner,Key).WitnessA.ToArray());
        Assert.Equal(Snapshot(cold,Key).Weights.ToArray(),Snapshot(owner,Key).Weights.ToArray());
    }
    [Fact]
    public void OverlappingParticleBoundsPreserveOmittedShapeQueriesAndTouchingBounds()
    {
        var registry=new AlsContactRegistry(2,2);
        registry.Register(new(0,AlsPrecisePose.Identity,1,1));registry.Register(new(1,AlsPrecisePose.Identity,1,1));
        var owner=new AlsPolygonQueryCache(2);owner.PrepareStep();Query(owner,Key);Commit(owner);
        AlsContactBounds?[] bounds=[new(new(-1,-1,-1),new(1,1,1)),new(new(1,-1,-1),new(3,1,1))];
        owner.PrepareStep();owner.RetireSeparatedPairs(registry,bounds);Commit(owner);
        Assert.Equal(1,owner.CachedPairs);Assert.True(owner.CopyCommitted(Key,new()));
        registry.RebindBody(0);owner.PrepareStep();owner.RetireSeparatedPairs(registry,bounds);Commit(owner);
        Assert.Equal(0,owner.CachedPairs);
    }
    [Fact]
    public void RemovedShapesRetireAndInvalidBoundsDoNotPartiallyPrune()
    {
        var registry=new AlsContactRegistry(2,2);
        var a=registry.Register(new(0,AlsPrecisePose.Identity,1,1));registry.Register(new(1,AlsPrecisePose.Identity,1,1));
        var owner=new AlsPolygonQueryCache(2);owner.PrepareStep();Query(owner,Key);Commit(owner);
        owner.PrepareStep();
        Assert.Throws<ArgumentException>(()=>owner.RetireSeparatedPairs(registry,[default,new(new(2,0,0),default)]));
        owner.Abort();Assert.Equal(1,owner.CachedPairs);
        registry.Remove(a);owner.PrepareStep();owner.RetireSeparatedPairs(registry,[default,default]);Commit(owner);
        Assert.Equal(0,owner.CachedPairs);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReplacementAndBodyGenerationProduceColdIdentityAndStaleReleaseIsIgnored(bool shapeRevision)
    {
        var changed=Key with {Shape0=shapeRevision?Key.Shape0 with {Revision=2}:Key.Shape0 with {Generation=2}};
        var owner=new AlsPolygonQueryCache(2);owner.PrepareStep();Query(owner,Key);Commit(owner);
        owner.PrepareStep();Query(owner,changed);owner.Release(Key);Commit(owner);
        var cold=new AlsPolygonQueryCache(2);cold.PrepareStep();Query(cold,changed);Commit(cold);
        Assert.False(owner.CopyCommitted(Key,new()));
        Assert.Equal(Snapshot(cold,changed).WitnessA.ToArray(),Snapshot(owner,changed).WitnessA.ToArray());
        Assert.Equal(Snapshot(cold,changed).Weights.ToArray(),Snapshot(owner,changed).Weights.ToArray());
        Assert.Equal(1,owner.CachedPairs);
    }
    [Fact]
    public void PairMarginChangeInvalidatesCachedCoreWitnesses()
    {
        var owner=new AlsPolygonQueryCache(2);owner.PrepareStep();Query(owner,Key);Commit(owner);
        owner.PrepareStep();Query(owner,Key,.2f);Commit(owner);
        var cold=new AlsPolygonQueryCache(2);cold.PrepareStep();Query(cold,Key,.2f);Commit(cold);
        Assert.Equal(Snapshot(cold,Key).WitnessA.ToArray(),Snapshot(owner,Key).WitnessA.ToArray());
        Assert.Equal(Snapshot(cold,Key).Weights.ToArray(),Snapshot(owner,Key).Weights.ToArray());
    }
    [Fact]
    public void FailedReplacementCanAbortAndRetryWithoutPublishingNewIdentity()
    {
        var owner=new AlsPolygonQueryCache(2);owner.PrepareStep();Query(owner,Key);Commit(owner);
        var changed=Key with {Shape1=Key.Shape1 with {Revision=2}};
        owner.PrepareStep();
        Assert.Throws<ArgumentException>(()=>owner.Query(changed,new AlsBoxPolygonShape(new(1,1,1)),new AlsBoxPolygonShape(new(1,1,1)),
            Pose,new AlsDetectedContact[1],0,1e-6,1e-6,1,.001f));
        Assert.Throws<InvalidOperationException>(()=>owner.StageCommit());
        Assert.Throws<InvalidOperationException>(()=>Query(owner,changed));
        owner.Abort();Assert.True(owner.CopyCommitted(Key,new()));Assert.False(owner.CopyCommitted(changed,new()));
        owner.PrepareStep();Query(owner,changed);Commit(owner);Assert.True(owner.CopyCommitted(changed,new()));
    }
    [Fact]
    public void PairOrderAndStagingGuardsPreventCrossPairOrLateMutation()
    {
        var owner=new AlsPolygonQueryCache(3);
        Assert.Throws<InvalidOperationException>(()=>Query(owner,Key));
        owner.PrepareStep();
        Assert.Throws<ArgumentException>(()=>Query(owner,new(Key.Shape0,Key.Shape1 with { Shape=Key.Shape0.Shape })));
        Query(owner,Key);owner.StageCommit();
        Assert.Throws<InvalidOperationException>(()=>Query(owner,Key));Assert.Throws<InvalidOperationException>(()=>owner.Reset());
        owner.PublishCommit();
        var destination=new AlsGjkCache();Assert.True(owner.CopyCommitted(Key,destination));destination.Reset();
        Assert.True(Snapshot(owner,Key).Count>0);
    }
    [Fact]
    public void QuerySnapshotsIncludeResetAndCannotExposeOmittedOrAbortedQueries()
    {
        var owner=new AlsPolygonQueryCache(2,true);var before=new AlsGjkCache();var after=new AlsGjkCache();
        owner.PrepareStep();Query(owner,Key);
        Assert.True(owner.CopyPendingQuery(Key,before,after));Assert.Equal(0,before.Count);Assert.True(after.Count>0);
        var saved=after.WitnessA.ToArray();after.Reset();
        Assert.True(owner.CopyPendingQuery(Key,before,after));Assert.Equal(saved,after.WitnessA.ToArray());
        Commit(owner);owner.PrepareStep();
        Assert.False(owner.CopyPendingQuery(Key,before,after));Assert.Equal(0,after.Count);
        Query(owner,Key);Assert.True(owner.CopyPendingQuery(Key,before,after));Assert.Equal(saved,before.WitnessA.ToArray());
        owner.Abort();Assert.Throws<InvalidOperationException>(()=>owner.CopyPendingQuery(Key,before,after));
        owner.PrepareStep();Query(owner,Key,.2f);
        Assert.True(owner.CopyPendingQuery(Key,before,after));Assert.Equal(0,before.Count);
        owner.Release(Key);Assert.False(owner.CopyPendingQuery(Key,before,after));owner.Abort();
        var disabled=new AlsPolygonQueryCache(2);disabled.PrepareStep();Query(disabled,Key);
        Assert.False(disabled.CopyPendingQuery(Key,before,after));disabled.Abort();
    }
    [Fact]
    public void WarmOwnerQueriesAllocateNothingAfterWarmup()
    {
        var owner=new AlsPolygonQueryCache(2);
        for(var i=0;i<20;i++){owner.PrepareStep();Query(owner,Key);Commit(owner);}
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<1000;i++){owner.PrepareStep();Query(owner,Key);Commit(owner);}
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
    }
}
