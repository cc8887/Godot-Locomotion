using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsCharacterCrouchTests
{
    private static readonly AlsCharacterCrouchSettings Settings=new(.3f,.9f,.65f,true);
    private sealed class World:IAlsCharacterCrouchWorld
    {
        public readonly Queue<bool> Blocks=new();
        public readonly List<float> Offsets=[];
        public AlsCharacterCrouchFloor Ground=new(true,new(0,.7f,0),Vector3.Zero);
        public bool AirBlocked,FailAir;
        public float AirFraction=.5f;
        public int Floors,AirCreated,AirOverlap,AirSweep,AirDisposed;
        public bool Blocked(float offset){Offsets.Add(offset);return Blocks.Count>0&&Blocks.Dequeue();}
        public AlsCharacterCrouchFloor Floor(){Floors++;return Ground;}
        public IAlsCharacterCrouchAirQuery AirQuery(float radius){Assert.Equal(.3f,radius);AirCreated++;return new Air(this);}
        private sealed class Air(World owner):IAlsCharacterCrouchAirQuery
        {
            public bool Overlap(){owner.AirOverlap++;if(owner.FailAir)throw new InvalidOperationException("Physics unavailable");return owner.AirBlocked;}
            public float SweepFraction(){owner.AirSweep++;return owner.AirFraction;}
            public void Dispose()=>owner.AirDisposed++;
        }
    }
    private static AlsCharacterCrouch Controller(World w,AlsCharacterCrouchSettings? settings=null)=>new(w,settings??Settings);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameStanceDoesNotQueryOrRequestPhysicalWrites(bool crouched)
    {
        var w=new World();var half=crouched?.65f:.9f;var result=Controller(w).Decide(crouched,crouched,true,half);
        Assert.False(result.Changed);Assert.Equal(crouched,result.Crouching);Assert.Equal(half,result.HalfHeight);Assert.Equal(0,result.Shift);Assert.Empty(w.Offsets);
    }
    [Fact]
    public void DisabledCrouchRequestLeavesStandingCapsule()
    {
        var w=new World();Assert.False(Controller(w,Settings with{CanCrouch=false}).Decide(true,false,true,.9f).Changed);Assert.Empty(w.Offsets);
    }
    [Theory]
    [InlineData(false,0f)]
    [InlineData(true,-.25f)]
    public void CrouchingPreservesGroundBaseOrAirCenter(bool grounded,float shift)
    {
        var w=new World();var result=Controller(w).Decide(true,false,grounded,.9f);
        Assert.True(result.Changed);Assert.True(result.Crouching);Assert.Equal(.65f,result.HalfHeight);Assert.Equal(shift,result.Shift,6);Assert.Empty(w.Offsets);
    }
    [Theory]
    [InlineData(false,0f)]
    [InlineData(true,.25001f)]
    public void ClearStandingUsesGroundEpsilonAndOneOverlap(bool grounded,float shift)
    {
        var w=new World();var result=Controller(w).Decide(false,true,grounded,.65f);
        Assert.True(result.Changed);Assert.False(result.Crouching);Assert.Equal(.9f,result.HalfHeight);Assert.Equal(shift,result.Shift,6);
        Assert.Equal(shift,Assert.Single(w.Offsets),6);Assert.Equal(0,w.Floors);Assert.Equal(0,w.AirCreated);
    }
    [Fact]
    public void BlockedGroundStandingRetriesAfterFloorGapCorrection()
    {
        var w=new World();w.Blocks.Enqueue(true);w.Blocks.Enqueue(false);
        var result=Controller(w).Decide(false,true,true,.65f);
        Assert.True(result.Changed);Assert.Equal(.20002f,result.Shift,6);Assert.Equal(1,w.Floors);Assert.Equal(2,w.Offsets.Count);Assert.Equal(0,w.AirCreated);
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("touching")]
    [InlineData("roof")]
    public void GroundRefusalDoesNotPublishNewStance(string reason)
    {
        var w=new World();w.Blocks.Enqueue(true);w.Blocks.Enqueue(true);
        if(reason=="missing")w.Ground=w.Ground with{Hit=false};
        if(reason=="touching")w.Ground=w.Ground with{Start=new(0,.65f,0)};
        var result=Controller(w).Decide(false,true,true,.65f);
        Assert.False(result.Changed);Assert.True(result.Crouching);Assert.Equal(.65f,result.HalfHeight);Assert.Equal(0,result.Shift);
        Assert.Equal(reason=="roof"?2:1,w.Offsets.Count);Assert.Equal(0,w.AirCreated);
    }
    [Fact]
    public void FloorGapUsesFullRayDistanceIncludingHorizontalOffset()
    {
        var w=new World{Ground=new(true,new(.3f,.4f,0),default)};w.Blocks.Enqueue(true);
        var result=Controller(w).Decide(false,true,true,.3f);Assert.True(result.Changed);
        Assert.Equal(.40002f,result.Shift,6);Assert.Equal(1,w.Floors);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AirStandingUsesSameScopedSphereAndRetriesAtComputedBase(bool blocked)
    {
        var w=new World();w.Blocks.Enqueue(true);w.Blocks.Enqueue(blocked);
        var result=Controller(w).Decide(false,true,false,.65f);
        Assert.Equal(!blocked,result.Changed);Assert.Equal(.45952f,w.Offsets[1],6);
        Assert.Equal(blocked?0:.45952f,result.Shift,6);Assert.Equal(0,w.Floors);
        Assert.Equal(1,w.AirCreated);Assert.Equal(1,w.AirOverlap);Assert.Equal(1,w.AirSweep);Assert.Equal(1,w.AirDisposed);
    }
    [Fact]
    public void AirInitialOverlapRejectsBeforeCastAndDisposesQuery()
    {
        var w=new World{AirBlocked=true};w.Blocks.Enqueue(true);
        Assert.False(Controller(w).Decide(false,true,false,.65f).Changed);
        Assert.Equal(1,w.AirOverlap);Assert.Equal(0,w.AirSweep);Assert.Equal(1,w.AirDisposed);Assert.Single(w.Offsets);
    }
    [Fact]
    public void AirQueryFailureDisposesScopeBeforePropagating()
    {
        var w=new World{FailAir=true};w.Blocks.Enqueue(true);
        Assert.Throws<InvalidOperationException>(()=>Controller(w).Decide(false,true,false,.65f));Assert.Equal(1,w.AirDisposed);Assert.Equal(0,w.AirSweep);
    }
    [Fact]
    public void RequestsDoNotMutateStanceAndRepeatedDecisionIsIdentical()
    {
        var w=new World();var c=Controller(w);var a=c.Decide(true,false,true,.9f);var b=c.Decide(true,false,true,.9f);
        Assert.Equal(a,b);Assert.Empty(w.Offsets);
        Assert.False(c.Decide(true,true,true,a.HalfHeight).Changed);
    }
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(.2f)]
    public void InvalidCurrentHeightIsRejectedBeforeQueries(float half)
    {
        var w=new World();Assert.Throws<ArgumentException>(()=>Controller(w).Decide(false,true,true,half));Assert.Empty(w.Offsets);
    }
}
