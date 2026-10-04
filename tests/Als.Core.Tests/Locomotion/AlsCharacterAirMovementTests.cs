using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Math;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsCharacterAirMovementTests
{
    private readonly record struct Hit(bool Blocking,bool Penetrating,float Time,Vector3 Point,Vector3 Normal,
        Vector3 ImpactNormal,Vector3 Location):IAlsCharacterGroundHit
    {public bool Valid=>Blocking&&!Penetrating;public bool CanStep=>false;}
    private readonly record struct Floor(bool Walkable,bool LineTrace):IAlsCharacterGroundFloor
    {public bool Blocking=>Walkable;public float FloorDistance=>0;public float LineDistance=>0;
     public Vector3 Point=>default;public Vector3 Normal=>Vector3.UnitY;}
    private readonly record struct Adjustment(bool Grounded):IAlsCharacterFloorAdjustment;
    private sealed class World:IAlsCharacterAirWorld<Hit,Floor,Adjustment>
    {
        public Vector3 Position {get;private set;}=Vector3.UnitY;
        public bool Recovered=>Recover;
        public bool Recover,DropSupport,FreezePosition;
        public Floor Current=new(true,false);
        public int Adjustments,FloorReads,Conversions;
        public readonly Queue<Hit> Hits=new();
        public readonly List<Vector3> Moves=[];
        public readonly List<(Vector3 Velocity,float Delta,bool Root)> Walks=[];
        public Hit Move(Vector3 motion,float halfHeight)
        {Moves.Add(motion);var h=Hits.Count>0?Hits.Dequeue():Free;if(!FreezePosition)Position+=motion*h.Time;return h;}
        public Floor Find(float halfHeight){FloorReads++;return Current;}
        public Hit FloorHit(Floor floor){Conversions++;return Landing;}
        public Adjustment AfterLanding(float halfHeight){Adjustments++;return new(true);}
        public AlsCharacterGroundMove<Hit,Adjustment> Walk(Vector3 velocity,float delta,float halfHeight,bool root)
        {Walks.Add((velocity,delta,root));Position+=velocity*delta;return new(velocity,new(!DropSupport),[],false,false);}
    }
    private static readonly Hit Free=new(false,false,1,default,default,default,default);
    private static readonly Hit Landing=new(true,false,.5f,default,Vector3.UnitY,Vector3.UnitY,Vector3.UnitY);
    private static readonly Hit Wall=new(true,false,.5f,default,Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitY);
    private static readonly AlsCharacterVelocitySettings Velocity=new(600,0,0,false,0,0,0,.02f);
    private static readonly AlsCharacterAirSettings Settings=new(.3f,.0015f,.7f,.1f,2,.05f,8,500,
        new(Velocity,2000,.2f,2,25,-1000,4000),Velocity,Velocity with{MaxSpeed=200});
    private static AlsCharacterAirMovement<Hit,Floor,Adjustment> Controller(World world,AlsCharacterAirSettings? settings=null)
        =>new(world,settings??Settings,123);
    private static AlsCharacterFallingInput Input(AlsDoubleVector velocity,bool crouch=false)=>new(velocity,default,1,crouch);

    [Fact]
    public void FreeFallUsesMidpointMotionAndExistingLateralKernel()
    {
        var w=new World();var c=Controller(w);var i=new AlsCharacterFallingInput(new(100,20,200),new(100,50,0),1,false);
        var expected=AlsCharacterFalling.Advance(i.Velocity,i.Acceleration,i.Analog,.02f,Settings.Falling);
        var result=c.Fall(i,.02f,.9f);
        Assert.Equal(expected.Velocity,result.Velocity);
        Assert.Equal(new Vector3((float)(expected.Displacement.Y*.01),(float)(expected.Displacement.Z*.01),(float)(-expected.Displacement.X*.01)),Assert.Single(w.Moves));
        Assert.Empty(result.Contacts);Assert.False(result.Floor.Grounded);Assert.Equal(0,w.FloorReads);Assert.Equal(123u,c.RandomSeed);
    }
    [Fact]
    public void ApexRefundSplitsMovementWithoutSpendingAnotherIteration()
    {
        var w=new World();var result=Controller(w).Fall(Input(new(0,0,10)),.02f,.9f);
        Assert.Equal(1,result.ApexSplits);Assert.Equal(2,result.Substeps);
        Assert.Equal(.0005f,w.Moves[0].Y,7);Assert.Equal(-.0005f,w.Moves[1].Y,7);
        Assert.Equal(-10,result.Velocity.Z,5);Assert.Equal(1,w.Position.Y,6);
    }
    [Fact]
    public void ApexCanBeDisabledWithoutChangingEndVelocity()
    {
        var w=new World();var result=Controller(w,Settings with{MaxJumpApexAttempts=0}).Fall(Input(new(0,0,10)),.02f,.9f);
        Assert.Equal(0,result.ApexSplits);Assert.Equal(1,result.Substeps);Assert.Equal(-10,result.Velocity.Z,5);
    }
    [Fact]
    public void IterationBudgetConsumesLastRemainderAndClampsTerminalSpeed()
    {
        var w=new World();var result=Controller(w,Settings with{MaxSimulationIterations=2}).Fall(Input(new(0,0,-3990)),.2f,.9f);
        Assert.Equal(2,result.Substeps);Assert.Equal(-4000,result.Velocity.Z);Assert.Equal(2,w.Moves.Count);
    }
    [Fact]
    public void RootOverrideRetainsGravityAndBypassesLateralAcceleration()
    {
        var w=new World();var result=Controller(w).Fall(new(new(10,20,100),new(1000,1000,0),1,false),.02f,.9f,new(250,-100,900));
        Assert.Equal(new AlsDoubleVector(250,-100,80.00000044703484),result.Velocity);
        Assert.Empty(w.Walks);
    }
    [Theory]
    [InlineData(false,600f)]
    [InlineData(true,200f)]
    public void LandingUsesRemainingTimeAndStanceVelocitySettings(bool crouch,float speed)
    {
        var w=new World();w.Hits.Enqueue(Landing);
        var result=Controller(w).Fall(new(new(0,0,-100),new(100000,0,0),1,crouch),.02f,.9f);
        Assert.True(result.Floor.Grounded);Assert.Equal(.01f,result.LandingRemaining);Assert.Equal(1,w.Adjustments);
        var walking=Assert.Single(w.Walks);Assert.Equal(.01f,walking.Delta);Assert.False(walking.Root);
        Assert.Equal(speed,result.Velocity.X,4);Assert.Equal(0,result.Velocity.Z);Assert.Equal(Landing,Assert.Single(result.Contacts));
    }
    [Fact]
    public void LandingRootMotionAndLostSupportStopWalkingRemainder()
    {
        var w=new World{DropSupport=true};w.Hits.Enqueue(Landing);
        var result=Controller(w).Fall(Input(new(100,0,-100)),.2f,.9f,new(150,50,800));
        Assert.False(result.Floor.Grounded);Assert.Equal(new AlsDoubleVector(150,50,0),result.Velocity);
        Assert.True(Assert.Single(w.Walks).Root);
    }
    [Theory]
    [InlineData("slope")]
    [InlineData("edge")]
    [InlineData("height")]
    [InlineData("no-support")]
    public void InvalidLandingGeometryCannotPublishWalkingFloor(string reason)
    {
        var w=new World();var h=Landing;
        if(reason=="slope")h=h with{Normal=Vector3.UnitZ,ImpactNormal=Vector3.UnitZ};
        if(reason=="edge")h=h with{Point=Vector3.UnitX};
        if(reason=="height")h=h with{Point=Vector3.UnitY};
        if(reason=="no-support")w.Current=new(false,false);
        w.Hits.Enqueue(h);var result=Controller(w).Fall(Input(new(100,50,-100)),.02f,.9f);
        Assert.False(result.Floor.Grounded);Assert.Empty(w.Walks);Assert.Equal(0,w.Adjustments);Assert.Single(result.Contacts);
    }
    [Fact]
    public void RoundedEdgeUsesSweepFloorAndRejectsLineFallback()
    {
        var edge=Landing with{ImpactNormal=Vector3.UnitZ};
        var a=new World();a.Hits.Enqueue(edge);var landed=Controller(a).Fall(Input(new(100,0,-100)),.02f,.9f);
        Assert.True(landed.Floor.Grounded);Assert.Equal(1,a.Conversions);
        var b=new World{Current=new(true,true)};b.Hits.Enqueue(edge);var falling=Controller(b).Fall(Input(new(100,0,-100)),.02f,.9f);
        Assert.False(falling.Floor.Grounded);Assert.Equal(0,b.Conversions);
    }
    [Fact]
    public void WallSlideRemovesNormalSpeedWhileRetainingTangentAndGravity()
    {
        var w=new World();w.Hits.Enqueue(Wall);var result=Controller(w).Fall(Input(new(100,50,-100)),.02f,.9f);
        Assert.Equal(0,result.Velocity.X);Assert.Equal(50,result.Velocity.Y,8);Assert.Equal(-120,result.Velocity.Z,5);
        Assert.Equal(2,w.Moves.Count);Assert.Equal(0,w.Moves[1].Z);
    }
    [Fact]
    public void PenetrationRecoveryPreservesIntegratedVelocity()
    {
        var w=new World{Recover=true};w.Hits.Enqueue(Wall with{Penetrating=true});
        var result=Controller(w).Fall(Input(new(100,50,-100)),.02f,.9f);
        Assert.Equal(100,result.Velocity.X);Assert.Equal(50,result.Velocity.Y);Assert.Equal(-120,result.Velocity.Z,5);
    }
    [Fact]
    public void TwoWallsConstrainHorizontalMotionWithoutInventingUpwardVelocity()
    {
        var w=new World();w.Hits.Enqueue(Wall);w.Hits.Enqueue(Wall with{Normal=-Vector3.UnitX,ImpactNormal=-Vector3.UnitX});
        var result=Controller(w).Fall(Input(new(100,50,-100)),.02f,.9f);
        Assert.Equal(2,result.Contacts.Length);Assert.Equal(3,w.Moves.Count);
        Assert.Equal(0,result.Velocity.X);Assert.Equal(0,result.Velocity.Y);Assert.True(result.Velocity.Z<0);
    }
    [Fact]
    public void ConsecutiveFramesClearContactsAndKeepPerCharacterSeed()
    {
        var w=new World();var c=Controller(w);w.Hits.Enqueue(Wall);c.Fall(Input(new(100,50,-100)),.02f,.9f);
        var first=c.Fall(Input(new(100,50,-100)),.02f,.9f);Assert.Empty(first.Contacts);Assert.Equal(123u,c.RandomSeed);
        uint seed=123;var fraction=AlsRandomStream.NextFraction(ref seed);
        Assert.Equal(3579439330u,seed);Assert.InRange(fraction,0f,.99999999f);
    }
    [Fact]
    public void PerchEscapeDrawsTwoFractionsOnlyForStuckCharacter()
    {
        static World Stuck()
        {
            var w=new World{FreezePosition=true};
            w.Hits.Enqueue(Wall with{ImpactNormal=Vector3.UnitY,Point=Vector3.UnitY});
            w.Hits.Enqueue(Wall with{Normal=-Vector3.UnitX,ImpactNormal=-Vector3.UnitX});
            return w;
        }
        var a=Stuck();var ca=Controller(a);var ra=ca.Fall(Input(new(100,50,-100)),.02f,.9f);
        var b=Stuck();var cb=Controller(b);var rb=cb.Fall(Input(new(100,50,-100)),.02f,.9f);
        Assert.True(ra.RandomEscape);Assert.Equal(125,ra.Velocity.Z);Assert.Equal(4,a.Moves.Count);
        Assert.Equal(ra.Velocity,rb.Velocity);Assert.Equal(ca.RandomSeed,cb.RandomSeed);Assert.NotEqual(123u,ca.RandomSeed);
        var noPerch=Stuck();var cc=Controller(noPerch,Settings with{PerchRadiusThreshold=0});
        Assert.False(cc.Fall(Input(new(100,50,-100)),.02f,.9f).RandomEscape);Assert.Equal(123u,cc.RandomSeed);
    }
    [Fact]
    public void ZeroTimeCornerTriesSideEscapeThenLandsWithoutRefund()
    {
        var w=new World();w.Hits.Enqueue(Wall);w.Hits.Enqueue(Wall with{Normal=-Vector3.UnitX,ImpactNormal=-Vector3.UnitX});
        w.Hits.Enqueue(Wall with{Time=0});w.Hits.Enqueue(Wall with{Time=0});
        var result=Controller(w).Fall(Input(new(100,50,-100)),.02f,.9f);
        Assert.True(result.Floor.Grounded);Assert.Equal(4,result.Contacts.Length);Assert.Empty(w.Walks);
        Assert.Equal(0,result.LandingRemaining);Assert.Equal(0,w.Moves[3].Y);
    }
    [Fact]
    public void UphillUnwalkableSlideDoesNotBoostFallingCharacterUpward()
    {
        var w=new World();var normal=new Vector3(0,.6f,.8f);
        w.Hits.Enqueue(Wall with{Normal=normal,ImpactNormal=normal});
        var result=Controller(w).Fall(Input(new(300,50,-10)),.02f,.9f);
        Assert.False(result.Floor.Grounded);Assert.True(result.Velocity.Z<=0);
        Assert.True(w.Moves.All(m=>m.Y<=0));
    }
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void InvalidFrameIsRejectedBeforePhysicalMutation(float delta)
    {
        var w=new World();Assert.Throws<ArgumentException>(()=>Controller(w).Fall(Input(default),delta,.9f));Assert.Empty(w.Moves);
    }
}
