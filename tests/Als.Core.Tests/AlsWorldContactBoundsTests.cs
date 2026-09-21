using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsWorldContactBoundsTests
{
    [Fact]
    public void RejectionPrecedesRestoreAndCommittedGapClearsGeometryAndFriction()
    {
        var (contacts,source,registry)=Create();
        Step(contacts);Assert.Equal(1,source.Queries);
        Step(contacts);Assert.Equal(1,contacts.LastRestoredPairs);Assert.Equal(1,source.Queries);
        source.Allow=false;Step(contacts);
        Assert.Equal(0,contacts.LastActivePairs);Assert.Equal(0,contacts.LastRestoredPairs);
        Assert.Equal(1,source.Queries);
        source.Allow=true;Gather(contacts);
        Assert.Equal(2,source.Queries);Assert.Equal(-1,contacts.HistoryPreparedAt(0,0).SavedIndex);
        Assert.True(contacts.HistoryGatherAt(0).Settings.InitialManifold);
        contacts.Abort();Assert.False(registry.IsLocked);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FailedBoundsOrPairCallbackPreservesCommittedHistoryAndUnlocks(bool preparation)
    {
        var (contacts,source,registry)=Create();Step(contacts);
        source.FailPrepare=preparation;source.FailPair=!preparation;
        Assert.Throws<InvalidOperationException>(()=>Gather(contacts));
        Assert.False(registry.IsLocked);Assert.Equal(1,contacts.CompletedSteps);Assert.Equal(1,source.Aborts);
        source.FailPrepare=source.FailPair=false;Step(contacts);
        Assert.Equal(1,source.Queries);Assert.Equal(1,contacts.LastRestoredPairs);
    }
    [Fact]
    public void AbortedRejectedAttemptDoesNotInvalidateTheCommittedManifold()
    {
        var (contacts,source,_)=Create();Step(contacts);source.Allow=false;
        Gather(contacts);contacts.Abort();source.Allow=true;Step(contacts);
        Assert.Equal(1,source.Queries);Assert.Equal(1,contacts.LastRestoredPairs);
    }
    private static (AlsWorldContacts,Source,AlsContactRegistry) Create()
    {
        var registry=new AlsContactRegistry(2,2);
        registry.Register(new(0,AlsPrecisePose.Identity,1,1));registry.Register(new(1,AlsPrecisePose.Identity,1,1));
        var source=new Source();return(new(registry,source,new(0,0,0),new(1f/60,0,2000)),source,registry);
    }
    private static void Gather(AlsWorldContacts contacts)=>contacts.Gather(
        [AlsPrecisePose.Identity with {Position=new(0,0,-.1)},AlsPrecisePose.Identity],
        new AlsProjectionVelocity[2],
        [new(AlsPrecisePose.Identity,new(1,AlsDoubleVector.One)),new(AlsPrecisePose.Identity,default)],1d/60);
    private static void Step(AlsWorldContacts contacts){Gather(contacts);contacts.StageCommit();contacts.Commit();}
    private sealed class Source:IAlsContactGeometrySource
    {
        public bool Allow=true,FailPrepare,FailPair;public int Queries,Aborts;private bool _prepared;
        public void PrepareBounds(ReadOnlySpan<AlsPrecisePose> poses)
        {
            Assert.Equal(2,poses.Length);Assert.Equal(-.1,poses[0].Position.Z);_prepared=true;
            if(FailPrepare)throw new InvalidOperationException("Injected bounds failure.");
        }
        public bool AllowsPair(int a,int b)
        {Assert.True(_prepared);if(FailPair)throw new InvalidOperationException("Injected pair failure.");return Allow;}
        public bool TryGetManifoldSettings(int a,int b,out AlsContactManifoldSettings settings){settings=new(2,3);return true;}
        public int Query(int a,in AlsPrecisePose p,int b,in AlsPrecisePose q,Span<AlsDetectedContact> points)
        {Queries++;points[0]=new(Vector3.Zero,Vector3.Zero,Vector3.UnitZ);return 1;}
        public void Abort(){Aborts++;_prepared=false;}
    }
}
