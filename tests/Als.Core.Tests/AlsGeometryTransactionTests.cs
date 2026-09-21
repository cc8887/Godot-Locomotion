using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsGeometryTransactionTests
{
    private sealed class CachedGeometry(AlsContactRegistry registry) : IAlsContactGeometrySource
    {
        public readonly AlsGjkCache Committed = new();
        private readonly AlsGjkCache _pending = new();
        private readonly AlsConvexManifoldWorkspace _work = new();
        public string? Failure;
        public int Begins, Publishes, Aborts, Resets, Queries;
        public bool Restore;
        public bool Pending;
        public void PrepareStep(ReadOnlySpan<AlsIslandBodyState> previous, ReadOnlySpan<AlsProjectionVelocity> velocities,
            ReadOnlySpan<AlsIslandBody> bodies, double dt)
        {
            Assert.True(registry.IsLocked); Assert.False(Pending); Pending=true; Begins++;
            _pending.CopyFrom(Committed);
            if(Failure=="prepare")throw new InvalidOperationException("Injected prepare failure");
        }
        public int Query(int a,in AlsPrecisePose world0,int b,in AlsPrecisePose world1,Span<AlsDetectedContact> destination)
        {
            Queries++;
            Assert.True(Pending);
            var relative=AlsPrecisePose.Relative(world1,world0);
            var count=AlsPolygonManifold.Build(new AlsBoxPolygonShape(new(1,1,1)),new AlsBoxPolygonShape(new(1,1,1)),
                relative,_pending,_work,destination,0,1e-6,1e-6,1,.001f).Count;
            if(Failure=="query")throw new InvalidOperationException("Injected after GJK and manifold");
            return count;
        }
        public bool TryGetManifoldSettings(int a,int b,out AlsContactManifoldSettings settings)
        { settings=new(2);return Restore; }
        public void StageCommit()
        {
            Assert.True(registry.IsLocked);Assert.True(Pending);
            if(Failure=="stage")throw new InvalidOperationException("Injected stage failure");
        }
        public void PublishCommit()
        { Assert.True(registry.IsLocked);Committed.CopyFrom(_pending);Pending=false;Publishes++; }
        public void Abort()
        { Assert.True(registry.IsLocked);_pending.Reset();Pending=false;Aborts++; }
        public void Reset()
        { Assert.False(registry.IsLocked);Assert.False(Pending);Committed.Reset();_pending.Reset();Resets++; }
    }
    private static (AlsJointIsland Island,AlsContactRegistry Registry,AlsWorldContacts Contacts,CachedGeometry Source) Create()
    {
        var identity=AlsPrecisePose.Identity;var registry=new AlsContactRegistry(2,2);
        registry.Register(new(0,identity,1,1));registry.Register(new(1,identity,1,1));
        var source=new CachedGeometry(registry);
        var contacts=new AlsWorldContacts(registry,source,new(0,0,0),new(1f/60,0,2000));
        var island=new AlsJointIsland([new(identity,new(1,AlsDoubleVector.One)),new(identity,default)],[],
            [new(identity,default),new(identity with {Position=new(0,0,1.5)},default)]);
        return(island,registry,contacts,source);
    }
    [Theory]
    [InlineData("prepare")]
    [InlineData("query")]
    [InlineData("stage")]
    public void FailureLeavesCommittedGjkAndBodiesIntactAndRetryMatchesControl(string failure)
    {
        var actual=Create();var control=Create();
        actual.Island.StepForceFree(1d/60,actual.Contacts);control.Island.StepForceFree(1d/60,control.Contacts);
        Assert.True(actual.Source.Committed.Count>0);
        var points=actual.Source.Committed.WitnessA.ToArray();var weights=actual.Source.Committed.Weights.ToArray();
        var body=actual.Island.BodyAt(0);actual.Source.Failure=failure;
        Assert.Throws<InvalidOperationException>(()=>actual.Island.StepForceFree(1d/60,actual.Contacts));
        Assert.Equal(points,actual.Source.Committed.WitnessA.ToArray());Assert.Equal(weights,actual.Source.Committed.Weights.ToArray());
        Assert.Equal(body,actual.Island.BodyAt(0));Assert.Equal(1,actual.Contacts.CompletedSteps);
        Assert.Equal(1,actual.Source.Aborts);Assert.Equal(1,actual.Source.Publishes);
        Assert.False(actual.Registry.IsLocked);Assert.False(actual.Source.Pending);
        actual.Source.Failure=null;
        actual.Island.StepForceFree(1d/60,actual.Contacts);control.Island.StepForceFree(1d/60,control.Contacts);
        Assert.Equal(control.Island.BodyAt(0),actual.Island.BodyAt(0));
        Assert.Equal(control.Source.Committed.WitnessA.ToArray(),actual.Source.Committed.WitnessA.ToArray());
        Assert.Equal(control.Source.Committed.Weights.ToArray(),actual.Source.Committed.Weights.ToArray());
    }
    [Fact]
    public void ExplicitAbortAndResetHaveDistinctEffectsOnCommittedGeometry()
    {
        var state=Create();state.Island.StepForceFree(1d/60,state.Contacts);
        var saved=state.Source.Committed.WitnessA.ToArray();
        var poses=new[]{state.Island.BodyAt(0).Actor,state.Island.BodyAt(1).Actor};
        var bodies=new[]{new AlsIslandBody(AlsPrecisePose.Identity,new(1,AlsDoubleVector.One)),new AlsIslandBody(AlsPrecisePose.Identity,default)};
        state.Contacts.Gather(poses,new AlsProjectionVelocity[2],bodies,1d/60);
        state.Contacts.StageCommit();
        Assert.Throws<InvalidOperationException>(()=>state.Contacts.Reset());
        state.Contacts.Abort();state.Contacts.Abort();
        Assert.Equal(1,state.Source.Aborts);Assert.Equal(saved,state.Source.Committed.WitnessA.ToArray());
        state.Contacts.Reset();Assert.Equal(1,state.Source.Resets);Assert.Equal(0,state.Source.Committed.Count);
        Assert.Equal(0,state.Contacts.CompletedSteps);
    }
    [Fact]
    public void RestoredManifoldStillBeginsAndPublishesGeometryWithoutFreshQuery()
    {
        var state=Create();state.Source.Restore=true;
        var poses=new[]{AlsPrecisePose.Identity,AlsPrecisePose.Identity with {Position=new(0,0,1.5)}};
        var bodies=new[]{new AlsIslandBody(AlsPrecisePose.Identity,new(1,AlsDoubleVector.One)),new AlsIslandBody(AlsPrecisePose.Identity,default)};
        for(var i=0;i<2;i++)
        {
            state.Contacts.Gather(poses,new AlsProjectionVelocity[2],bodies,1d/60);
            state.Contacts.StageCommit();state.Contacts.Commit();
        }
        Assert.Equal(1,state.Source.Queries);Assert.Equal(1,state.Contacts.LastRestoredPairs);
        Assert.Equal(2,state.Source.Begins);Assert.Equal(2,state.Source.Publishes);
        Assert.False(state.Source.Pending);
    }
    [Fact]
    public void EmptyFilteredStepStillClosesGeometryTransaction()
    {
        var state=Create();state.Registry.DisableBodyPair(0,1,true);
        state.Island.StepForceFree(1d/60,state.Contacts);
        Assert.Equal(1,state.Source.Begins);Assert.Equal(1,state.Source.Publishes);
        Assert.False(state.Source.Pending);Assert.False(state.Registry.IsLocked);
        Assert.Equal(0,state.Source.Committed.Count);
    }
}
