using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsCharacterSweepTests
{
    private readonly record struct Hit(bool Penetrating,float Time,Vector3 Normal,float PenetrationDepth,bool Pawn=false):IAlsCharacterPenetrationHit
    {
        public bool Blocking=>Penetrating;
        public bool Valid=>Blocking&&!Penetrating;
        public bool CanStep=>false;
        public Vector3 Point=>default;
        public Vector3 ImpactNormal=>Normal;
        public Vector3 Location=>default;
    }
    private readonly record struct Checkpoint(Vector3 Position,int Marker);
    private sealed class World:IAlsCharacterSweepWorld<Hit,Checkpoint>
    {
        public Vector3 Position {get;private set;}=Vector3.UnitY;
        public int Marker=17;
        public bool Overlapping=true;
        public readonly Queue<Hit> Hits=new();
        public readonly List<(Vector3 Motion,float Half,bool Ignore)> Queries=[];
        public readonly List<(Checkpoint State,Vector3 Adjustment,float Radius,float Half,float Inflation)> Overlaps=[];
        public readonly List<Vector3> Writes=[];
        public Checkpoint Capture()=>new(Position,Marker);
        public Hit Query(Vector3 motion,float half,bool ignoreOutward)
        {Queries.Add((motion,half,ignoreOutward));return Hits.Count>0?Hits.Dequeue():Free;}
        public bool Overlap(Checkpoint start,Vector3 adjustment,float radius,float half,float inflation)
        {Overlaps.Add((start,adjustment,radius,half,inflation));return Overlapping;}
        public void Translate(Vector3 travel){Writes.Add(travel);Position+=travel;}
    }
    private static readonly Hit Free=new(false,1,default,0);
    private static readonly Hit Penetrating=new(true,0,Vector3.UnitX,.1f);
    private static readonly AlsCharacterPenetrationSettings Settings=new(.01f,.02f,.01f,1,.3f,.2f,.1f);
    private static AlsCharacterSweep<Hit,Checkpoint> Sweep(World world,bool proxy=false,AlsCharacterPenetrationSettings? settings=null)=>new(world,.3f,settings??Settings,proxy);
    private static void Enqueue(World w,params Hit[] hits){foreach(var h in hits)w.Hits.Enqueue(h);}

    [Fact]
    public void FreeAndUnsafeMovesDoNotRunRecovery()
    {
        var w=new World();var c=Sweep(w);Assert.Equal(Free,c.Move(Vector3.UnitX,.9f));
        Enqueue(w,Penetrating);Assert.Equal(Penetrating,c.Move(Vector3.UnitX,.9f,false));
        Assert.False(c.Recovered);Assert.Empty(w.Overlaps);Assert.Single(w.Writes);Assert.Equal(new Vector3(1,1,0),w.Position);
    }
    [Fact]
    public void TeleportRecoveryPreservesCheckpointAndThenRequeriesRequestedMotion()
    {
        var w=new World{Overlapping=false};Enqueue(w,Penetrating,Free with{Time=.25f});var c=Sweep(w);
        var result=c.Move(Vector3.UnitZ,.9f);
        Assert.True(c.Recovered);Assert.Equal(1,c.TeleportRecoveries);Assert.Equal(.25f,result.Time);
        var overlap=Assert.Single(w.Overlaps);Assert.Equal(new Checkpoint(Vector3.UnitY,17),overlap.State);
        Assert.Equal(.02f,overlap.Inflation);Assert.Equal(.3f,overlap.Radius);Assert.Equal(.9f,overlap.Half);
        Assert.Equal(new[]{false,false},w.Queries.Select(x=>x.Ignore));Assert.Equal(.11f,w.Position.X,6);Assert.Equal(.25f,w.Position.Z);
        c.Move(Vector3.Zero,.9f);Assert.False(c.Recovered);Assert.Equal(1,c.TeleportRecoveries);
    }
    [Fact]
    public void SweptRecoveryAcceptsActualPositionChangeAndPreservesQueryOrder()
    {
        var w=new World();Enqueue(w,Penetrating,Free with{Time=.5f},Free);var c=Sweep(w);c.Move(Vector3.UnitZ,.9f);
        Assert.True(c.Recovered);Assert.Equal(1,c.SweptRecoveries);Assert.Equal(0,c.TeleportRecoveries);
        Assert.Equal(new[]{false,true,false},w.Queries.Select(x=>x.Ignore));Assert.Equal(.055f,w.Position.X,6);Assert.Equal(1,w.Position.Z);
    }
    [Fact]
    public void CombinedRecoveryUsesSecondPenetrationWhenFirstSweepDoesNotMove()
    {
        var w=new World();Enqueue(w,Penetrating,Penetrating with{Normal=Vector3.UnitZ},Free with{Time=.5f},Free);
        var c=Sweep(w);c.Move(Vector3.UnitY,.9f);
        Assert.True(c.Recovered);Assert.Equal(1,c.CombinedRecoveries);Assert.Equal(0,c.SweptRecoveries);
        Assert.Equal(new Vector3(.11f,0,.11f),w.Queries[2].Motion);Assert.Equal(.055f,w.Position.X,6);Assert.Equal(.055f,w.Position.Z,6);
    }
    [Fact]
    public void AdjustedRecoveryAddsRequestedMotionAfterEqualAdjustmentWasRejected()
    {
        var w=new World();Enqueue(w,Penetrating,Penetrating,Free,Free);var c=Sweep(w);c.Move(Vector3.UnitZ,.9f);
        Assert.True(c.Recovered);Assert.Equal(1,c.AdjustedRecoveries);Assert.Equal(0,c.CombinedRecoveries);
        Assert.Equal(new Vector3(.11f,0,1),w.Queries[2].Motion);Assert.Equal(4,w.Queries.Count);
    }
    [Fact]
    public void OriginalRecoveryIsLastAndRequiresOutwardRequestedMotion()
    {
        var w=new World();Enqueue(w,Penetrating,Penetrating,Penetrating,Free with{Time=.25f},Free);
        var c=Sweep(w);c.Move(Vector3.UnitX,.9f);
        Assert.True(c.Recovered);Assert.Equal(1,c.OriginalRecoveries);Assert.Equal(0,c.AdjustedRecoveries);
        Assert.Equal(5,w.Queries.Count);Assert.Equal(Vector3.UnitX,w.Queries[3].Motion);Assert.Equal(1.25f,w.Position.X);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InwardOrZeroRequestDoesNotTryOriginalFallback(bool zero)
    {
        var w=new World();Enqueue(w,Penetrating,Penetrating,Penetrating);var c=Sweep(w);
        c.Move(zero?Vector3.Zero:-Vector3.UnitX,.9f);
        Assert.False(c.Recovered);Assert.Equal(zero?2:3,w.Queries.Count);Assert.Equal(Vector3.UnitY,w.Position);Assert.Empty(w.Writes);
    }
    [Fact]
    public void EqualAndOppositeSecondAdjustmentsDoNotIssueRedundantCombinedSweep()
    {
        var w=new World();Enqueue(w,Penetrating,Penetrating with{Normal=-Vector3.UnitX});var c=Sweep(w);
        c.Move(Vector3.Zero,.9f);Assert.Equal(2,w.Queries.Count);Assert.False(c.Recovered);Assert.Equal(0,c.CombinedRecoveries);
    }
    [Fact]
    public void NonpenetratingZeroFractionIsNotARecoveryWithoutActualDisplacement()
    {
        var w=new World();Enqueue(w,Penetrating,Free with{Time=0},Free with{Time=0});var c=Sweep(w);
        c.Move(Vector3.UnitZ,.9f);Assert.False(c.Recovered);Assert.Equal(0,c.SweptRecoveries);Assert.Equal(0,c.AdjustedRecoveries);
        Assert.Equal(Vector3.UnitY,w.Position);Assert.Equal(3,w.Queries.Count);
    }
    [Theory]
    [InlineData(false,false,1f)]
    [InlineData(false,true,.3f)]
    [InlineData(true,false,.2f)]
    [InlineData(true,true,.1f)]
    public void AdjustmentUsesGeometryPawnAndProxyLimit(bool pawn,bool proxy,float limit)
    {
        var c=Sweep(new World(),proxy);var adjustment=c.Adjustment(Penetrating with{Pawn=pawn,PenetrationDepth=10});
        Assert.Equal(new Vector3(limit,0,0),adjustment);Assert.Equal(Vector3.Zero,c.Adjustment(Free));
    }
    [Fact]
    public void MissingDepthUsesMinimumAndZeroLimitSkipsRecoveryQueries()
    {
        var c=Sweep(new World());Assert.Equal(.01125f,c.Adjustment(Penetrating with{PenetrationDepth=0}).X,6);
        var w=new World();Enqueue(w,Penetrating);var disabled=Sweep(w,settings:Settings with{Geometry=0});disabled.Move(Vector3.UnitX,.9f);
        Assert.False(disabled.Recovered);Assert.Empty(w.Overlaps);Assert.Single(w.Queries);Assert.Empty(w.Writes);
    }
    [Theory]
    [InlineData(1f,false)]
    [InlineData(-1f,true)]
    [InlineData(0f,true)]
    public void InitialOverlapDirectionPolicyUsesOriginalTolerance(float normalX,bool blocked)
    {
        var c=Sweep(new World());Assert.Equal(blocked,c.BlocksInitialOverlap(Vector3.UnitX,new(normalX,0,1-normalX*normalX)));
        Assert.True(c.BlocksInitialOverlap(Vector3.Zero,Vector3.UnitX));
    }
    [Fact]
    public void CapsuleSupportDepthNormalizesAxisAndClampsNegativeDepth()
    {
        var axis=Vector3.UnitY*3;var origin=new Vector3(0,.85f,0);
        Assert.Equal(.05f,AlsCharacterSweepMath.PenetrationDepth(origin,axis,Vector3.Zero,Vector3.UnitY,.9f,.3f),6);
        Assert.Equal(.05f,AlsCharacterSweepMath.PenetrationDepth(origin,axis,new(0,1.7f,0),-Vector3.UnitY,.9f,.3f),6);
        Assert.Equal(0,AlsCharacterSweepMath.PenetrationDepth(origin,axis,new(0,-.2f,0),Vector3.UnitY,.9f,.3f));
    }
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(.2f)]
    public void InvalidFrameIsRejectedBeforeQuery(float half)
    {
        var w=new World();Assert.Throws<ArgumentException>(()=>Sweep(w).Move(Vector3.UnitX,half));Assert.Empty(w.Queries);Assert.Empty(w.Writes);
    }
}
