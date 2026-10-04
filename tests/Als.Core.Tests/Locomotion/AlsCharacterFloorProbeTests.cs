using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsCharacterFloorProbeTests
{
    private static readonly AlsCharacterFloorSettings Settings=new(.019f,.024f,.0015f,.45f,.1f,.15f,.7f);
    private sealed class World:IAlsCharacterFloorWorld<int>
    {
        public Vector3 Position {get;set;}=new(0,.9215f,0);
        public Vector3 Velocity {get;set;}
        public bool OnFloor {get;set;}
        public bool MissingFloor;
        public AlsCharacterFloorRay<int> RayResult;
        public AlsCharacterFloorHeightSweep? HeightHit;
        public Vector3 SnapOffset;
        public int Rays,HeightSweeps,Translations,ContactReads;
        public readonly List<float> Snaps=[];
        public readonly List<(Vector3 Start,float Radius,float Half,float Distance)> Sweeps=[];
        public readonly Queue<AlsCharacterFloorHit<int>> Hits=new();
        public readonly List<AlsCharacterFloorContact> ContactList=[];
        public AlsCharacterFloorHit<int> Sweep(Vector3 start,float radius,float half,float distance)
        {
            Sweeps.Add((start,radius,half,distance));
            if(Hits.Count>0)return Hits.Dequeue();
            if(MissingFloor)return new(false,false,1,default,default,start-Vector3.UnitY*distance,start,0);
            float gap=start.Y-half;return new(true,gap<0,gap/distance,new(start.X,0,start.Z),Vector3.UnitY,
                start-Vector3.UnitY*gap,start,11);
        }
        public AlsCharacterFloorRay<int> Ray(Vector3 start,float distance){Rays++;return RayResult;}
        public IEnumerable<AlsCharacterFloorContact> Contacts(Vector3 motion){ContactReads++;return ContactList;}
        public void Snap(float distance){Snaps.Add(distance);Position+=SnapOffset;}
        public AlsCharacterFloorHeightSweep SweepHeight(Vector3 motion){HeightSweeps++;return HeightHit??new(false,motion,1,1);}
        public void Translate(Vector3 travel){Translations++;Position+=travel;}
    }
    private static AlsCharacterFloorProbe<int> Probe(World w,AlsCharacterFloorSettings? s=null)=>new(w,.3f,s??Settings);
    private static AlsCharacterFloorHit<int> Hit=>new(true,false,.12f,Vector3.Zero,Vector3.UnitX,Vector3.UnitY,new(2,3,4),77);

    [Fact]
    public void ComputeUsesShortCapsuleAndPreservesOpaqueHitWithoutMovingActor()
    {
        var w=new World();var before=w.Position;var result=Probe(w).Compute(w.Position,.9f,.5f,.5f);
        Assert.True(result.Walkable);Assert.False(result.LineTrace);Assert.Equal(.0215f,result.FloorDistance,6);
        Assert.Equal(11,result.Hit.Collider);Assert.Equal(before,w.Position);Assert.Empty(w.Snaps);Assert.Equal(0,w.Translations);
        var query=Assert.Single(w.Sweeps);Assert.Equal(.84f,query.Half,6);Assert.Equal(.56f,query.Distance,6);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EdgeAndPenetrationRetryReduceRadiusAndCapsuleHeight(bool penetrating)
    {
        var w=new World();w.Hits.Enqueue(Hit with{Penetrating=penetrating,Point=Vector3.UnitX});
        var result=Probe(w).Compute(w.Position,.9f,.5f,.5f);
        Assert.True(result.Walkable);Assert.Equal(2,w.Sweeps.Count);
        Assert.Equal(.298499f,w.Sweeps[1].Radius,6);Assert.Equal(.36f,w.Sweeps[1].Half,6);
        Assert.Equal(1.04f,w.Sweeps[1].Distance,6);Assert.Equal(0,w.Rays);
    }
    [Fact]
    public void LineFallbackReplacesNormalAndColliderWhileKeepingSweepGeometry()
    {
        var w=new World{RayResult=new(true,Vector3.Zero,Vector3.UnitY,22)};w.Hits.Enqueue(Hit);
        var result=Probe(w).Compute(w.Position,.9f,.5f,.5f);
        Assert.True(result.LineTrace);Assert.True(result.Walkable);Assert.Equal(22,result.Hit.Collider);
        Assert.Equal(Hit.Point,result.Hit.Point);Assert.Equal(Hit.Location,result.Hit.Location);
        Assert.Equal(Hit.TraceStart,result.Hit.TraceStart);Assert.Equal(Hit.Time,result.Hit.Time);
        Assert.Equal(Vector3.UnitY,result.Hit.Normal);Assert.Equal(.0215f,result.LineDistance,6);Assert.Equal(1,w.Rays);
    }
    [Theory]
    [InlineData("none")]
    [InlineData("slope")]
    [InlineData("distance")]
    [InlineData("inside")]
    public void RejectedLineCannotTurnUnwalkableSweepIntoFloor(string reason)
    {
        var ray=new AlsCharacterFloorRay<int>(true,Vector3.Zero,Vector3.UnitY,22);
        if(reason=="none")ray=default;
        if(reason=="slope")ray=ray with{Normal=Vector3.UnitX};
        if(reason=="distance")ray=ray with{Point=-Vector3.UnitY*10};
        if(reason=="inside")ray=ray with{Point=Vector3.UnitY*2};
        var w=new World{RayResult=ray};w.Hits.Enqueue(Hit);
        var result=Probe(w).Compute(w.Position,.9f,.5f,.5f);
        Assert.False(result.Walkable);Assert.False(result.LineTrace);Assert.Equal(Hit,result.Hit);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyFloorSkipsRayAndUsesRequestedSweepDistance(bool zeroRadius)
    {
        var w=new World{MissingFloor=true};var result=Probe(w).Compute(w.Position,.9f,.5f,.5f,zeroRadius?0:null);
        Assert.False(result.Blocking);Assert.False(result.Walkable);Assert.Equal(.5f,result.FloorDistance);
        Assert.Equal(w.Position,result.Hit.Location);Assert.Equal(w.Position,result.Hit.TraceStart);
        Assert.Equal(0,w.Rays);Assert.Equal(zeroRadius?0:1,w.Sweeps.Count);
    }
    [Fact]
    public void PerchRejectsEdgeSupportWithoutDiscardingOriginalHit()
    {
        var w=new World{MissingFloor=true};var h=Hit with{Normal=Vector3.UnitY,Point=new(.25f,0,0)};w.Hits.Enqueue(h);
        var p=Probe(w);var result=p.Find(w.Position,.9f,true);
        Assert.False(result.Walkable);Assert.True(result.Blocking);Assert.Equal(h,result.Hit);
        Assert.Equal(1,p.PerchQueries);Assert.Equal(2,w.Sweeps.Count);Assert.Equal(.2f,w.Sweeps[1].Radius,6);
    }
    [Fact]
    public void PerchCanRestoreUnwalkableHitKeepingOriginalPointAndTime()
    {
        var w=new World();var h=Hit with{Point=new(.25f,0,0)};w.Hits.Enqueue(h);var p=Probe(w);
        var result=p.Find(w.Position,.9f,true);
        Assert.True(result.Walkable);Assert.True(result.LineTrace);Assert.Equal(1,p.PerchQueries);
        Assert.Equal(h.Point,result.Hit.Point);Assert.Equal(h.Time,result.Hit.Time);Assert.Equal(h.TraceStart,result.Hit.TraceStart);
        Assert.Equal(11,result.Hit.Collider);Assert.Equal(Vector3.UnitY,result.Hit.Normal);Assert.True(result.LineDistance>=Settings.Minimum);
    }
    [Theory]
    [InlineData("center")]
    [InlineData("disabled")]
    [InlineData("line")]
    public void UnneededPerchDoesNotIssueSecondFloorQuery(string reason)
    {
        var w=new World();var s=Settings;
        if(reason=="disabled"){s=s with{PerchRadiusThreshold=0};w.Hits.Enqueue(Hit with{Normal=Vector3.UnitY,Point=new(.25f,0,0)});}
        if(reason=="line"){w.Hits.Enqueue(Hit with{Point=new(.25f,0,0)});w.RayResult=new(true,Vector3.Zero,Vector3.UnitY,22);}
        var p=Probe(w,s);Assert.True(p.Find(w.Position,.9f,true).Walkable);Assert.Equal(0,p.PerchQueries);Assert.Single(w.Sweeps);
    }
    [Theory]
    [InlineData(.018f,.0215f)]
    [InlineData(.020f,.020f)]
    [InlineData(.024f,.024f)]
    [InlineData(.030f,.0215f)]
    public void FloorBandOnlyCorrectsOutsideInclusiveRange(float gap,float expectedGap)
    {
        var w=new World{Position=new(0,.9f+gap,0)};var result=Probe(w).AfterSweep(.9f);
        Assert.True(result.Grounded);Assert.Equal(.9f+expectedGap,w.Position.Y,6);
        Assert.Empty(w.Snaps);Assert.Equal(expectedGap==gap?0:1,w.HeightSweeps);
    }
    [Fact]
    public void InitialiseKeepsAbsoluteHeightTargetAcrossBackendSnap()
    {
        var w=new World{Position=new(0,.9f,0),SnapOffset=Vector3.UnitY*.005f};
        var result=Probe(w).Initialise(.9f);
        Assert.True(result.Grounded);Assert.Equal(.9215f,w.Position.Y,6);Assert.Equal(.0215f,result.Movement.Y,6);
        Assert.Equal(Settings.TraceDistance(true),Assert.Single(w.Snaps));Assert.Equal(1,w.HeightSweeps);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitialiseRisingOrMissingSupportCannotSnap(bool missing)
    {
        var w=new World{MissingFloor=missing,Velocity=Vector3.UnitY};
        Assert.False(Probe(w).Initialise(.9f).Grounded);Assert.Empty(w.Snaps);Assert.Equal(0,w.Translations);
    }
    [Fact]
    public void HeightCollisionPreservesBackendRecoveryAndPullsBackRequestedMotion()
    {
        var w=new World{Position=new(0,.9f,0),HeightHit=new(true,new(.003f,.005f,0),.5f,.5f)};
        var result=Probe(w).AfterSweep(.9f);
        Assert.True(result.Grounded);Assert.Equal(.003f,result.Movement.X,6);Assert.Equal(.90282845f,w.Position.Y,6);
        Assert.Equal(1,w.Translations);Assert.Equal(1,w.HeightSweeps);
    }
    [Fact]
    public void LineFloorWithinBandAvoidsHeightWriteWhenSweepGapIsSmaller()
    {
        var w=new World{Position=new(0,.92f,0),RayResult=new(true,Vector3.Zero,Vector3.UnitY,22)};w.Hits.Enqueue(Hit);
        var result=Probe(w).AfterSweep(.9f);
        Assert.True(result.Grounded);Assert.True(result.Floor.LineTrace);Assert.Equal(0,w.HeightSweeps);Assert.Equal(.92f,w.Position.Y);
    }
    [Theory]
    [InlineData(-1f,true)]
    [InlineData(1f,false)]
    public void AirborneAfterMoveNeedsDescendingGeometricContact(float speed,bool ground)
    {
        var w=new World();w.ContactList.Add(new(Vector3.Zero,Vector3.UnitY));
        var result=Probe(w).AfterMove(.9f,false,0,new(0,speed,0),Vector3.UnitZ);
        Assert.Equal(ground,result.Grounded);Assert.Equal(ground?1:0,w.ContactReads);Assert.Equal(ground?1:0,w.Snaps.Count);
    }
    [Theory]
    [InlineData("edge")]
    [InlineData("height")]
    [InlineData("normal")]
    public void InvalidSlideContactCannotCreateGroundedState(string reason)
    {
        var w=new World();var contact=new AlsCharacterFloorContact(Vector3.Zero,Vector3.UnitY);
        if(reason=="edge")contact=contact with{Point=Vector3.UnitX};
        if(reason=="height")contact=contact with{Point=Vector3.UnitY};
        if(reason=="normal")contact=contact with{Normal=Vector3.UnitZ};
        w.ContactList.Add(contact);Assert.False(Probe(w).AfterMove(.9f,false,0,-Vector3.UnitY,Vector3.UnitZ).Grounded);
        Assert.Empty(w.Snaps);Assert.Equal(0,w.Translations);
    }
    [Fact]
    public void WalkingUsesPredictedHeightAndDoesNotReadSlideContacts()
    {
        var w=new World{Position=new(0,.95f,0)};var result=Probe(w).AfterMove(.9f,true,.92f,Vector3.UnitY,Vector3.UnitZ);
        Assert.True(result.Grounded);Assert.Equal(.92f,w.Position.Y,6);Assert.Equal(-.03f,result.Movement.Y,6);Assert.Equal(0,w.ContactReads);
    }
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(-.1f)]
    public void InvalidSweepRadiusIsRejectedBeforeBackendQuery(float radius)
    {
        var w=new World();Assert.Throws<ArgumentException>(()=>Probe(w).Compute(w.Position,.9f,.5f,.5f,radius));Assert.Empty(w.Sweeps);Assert.Equal(0,w.Rays);
    }
}
