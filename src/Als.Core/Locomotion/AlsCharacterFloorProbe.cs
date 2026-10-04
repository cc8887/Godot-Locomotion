using System.Numerics;
using ScalarMath = System.Math;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsCharacterFloorSettings(float Minimum,float Maximum,float EdgeReject,float StepHeight,
    float PerchRadiusThreshold,float PerchAdditionalHeight,float WalkableZ)
{
    public float Average=>(Minimum+Maximum)*.5f;
    public float TraceDistance(bool walking)=>ScalarMath.Max(Maximum,StepHeight+(walking?Maximum+.000001f:-Maximum));
}
public readonly record struct AlsCharacterFloorHit<TCollider>(bool Hit,bool Penetrating,float Time,Vector3 Point,
    Vector3 Normal,Vector3 Location,Vector3 TraceStart,TCollider Collider)
{public bool Valid=>Hit&&!Penetrating;}
public readonly record struct AlsCharacterFloorResult<TCollider>(AlsCharacterFloorHit<TCollider> Hit,bool Walkable,
    bool LineTrace,float FloorDistance,float LineDistance):IAlsCharacterGroundFloor
{
    public bool Blocking=>Hit.Valid;
    public Vector3 Point=>Hit.Point;
    public Vector3 Normal=>Hit.Normal;
}
public readonly record struct AlsCharacterFloorAdjustment<TCollider>(bool Grounded,AlsCharacterFloorResult<TCollider> Floor,
    Vector3 Movement):IAlsCharacterFloorAdjustment;
public readonly record struct AlsCharacterFloorRay<TCollider>(bool Hit,Vector3 Point,Vector3 Normal,TCollider Collider);
public readonly record struct AlsCharacterFloorContact(Vector3 Point,Vector3 Normal);
public readonly record struct AlsCharacterFloorHeightSweep(bool Blocking,Vector3 Travel,float UnsafeFraction,float SafeFraction);

// Backend operations use Y-up meters/binary32 and retain an opaque collider.
// Query methods do not move the character. Only Snap/Translate write physics.
public interface IAlsCharacterFloorWorld<TCollider>
{
    Vector3 Position {get;}
    Vector3 Velocity {get;}
    bool OnFloor {get;}
    AlsCharacterFloorHit<TCollider> Sweep(Vector3 start,float radius,float half,float distance);
    AlsCharacterFloorRay<TCollider> Ray(Vector3 start,float distance);
    IEnumerable<AlsCharacterFloorContact> Contacts(Vector3 motion);
    void Snap(float distance);
    AlsCharacterFloorHeightSweep SweepHeight(Vector3 motion);
    void Translate(Vector3 travel);
}

public sealed class AlsCharacterFloorProbe<TCollider>
{
    private readonly IAlsCharacterFloorWorld<TCollider> _world;
    private readonly float _radius;
    public AlsCharacterFloorSettings Settings {get;}
    public int PerchQueries {get;private set;}
    public AlsCharacterFloorProbe(IAlsCharacterFloorWorld<TCollider> world,float radius,AlsCharacterFloorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(world);
        float[] fields=[radius,settings.Minimum,settings.Maximum,settings.EdgeReject,settings.StepHeight,
            settings.PerchRadiusThreshold,settings.PerchAdditionalHeight,settings.WalkableZ];
        if(fields.Any(v=>!float.IsFinite(v))||radius<=0||settings.Minimum<=0||settings.Maximum<settings.Minimum||
            settings.EdgeReject<0||settings.StepHeight<0||settings.PerchRadiusThreshold<0||settings.PerchAdditionalHeight<0||
            settings.WalkableZ is <=0 or >1)throw new ArgumentException("Invalid floor settings.");
        _world=world;_radius=radius;Settings=settings;
    }
    private static float LengthSquared(Vector3 v)=>v.X*v.X+v.Y*v.Y+v.Z*v.Z;
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
    private bool Edge(Vector3 center,Vector3 point,float radius)
    {
        var d=point-center;d.Y=0;float reduced=ScalarMath.Max(Settings.EdgeReject+.000001f,radius-Settings.EdgeReject);
        return LengthSquared(d)<reduced*reduced;
    }
    private bool Walkable(AlsCharacterFloorHit<TCollider> hit)=>hit.Valid&&hit.Normal.Y>=.0001f&&hit.Normal.Y>=Settings.WalkableZ;
    public AlsCharacterFloorResult<TCollider> Compute(Vector3 start,float half,float line,float sweep,float? sweepRadius=null)
    {
        if(!Finite(start)||!float.IsFinite(half)||half<_radius||!float.IsFinite(sweep)||
            !float.IsFinite(line)||line<0||sweep<line||sweepRadius.HasValue&&(!float.IsFinite(sweepRadius.Value)||sweepRadius.Value<0))throw new ArgumentException("Invalid capsule floor query.");
        float radius=sweepRadius??_radius,shrink=(half-_radius)*(1-.9f);
        var hit=new AlsCharacterFloorHit<TCollider>(false,false,1,default,default,start,start,default!);
        var result=new AlsCharacterFloorResult<TCollider>(hit,false,false,0,0);
        if(sweep>0&&radius>0)
        {
            float trace=sweep+shrink;hit=_world.Sweep(start,radius,half-shrink,trace);
            if(hit.Hit)
            {
                if(hit.Penetrating||!Edge(start,hit.Point,radius))
                {
                    radius=ScalarMath.Max(0,radius-Settings.EdgeReject-.000001f);
                    if(radius>.000001f)
                    {shrink=(half-_radius)*(1-.1f);trace=sweep+shrink;hit=_world.Sweep(start,radius,ScalarMath.Max(half-shrink,radius),trace);}
                }
                float gap=ScalarMath.Max(-ScalarMath.Max(Settings.Maximum,_radius),hit.Time*trace-shrink);
                result=new(hit,Walkable(hit)&&gap<=sweep,false,gap,0);
                if(result.Walkable)return result;
            }
        }
        if(!result.Blocking&&!hit.Penetrating)return result with{FloorDistance=sweep};
        if(line>0)
        {
            float distance=line+half;
            var lineHitResult=_world.Ray(start,distance);
            if(lineHitResult.Hit)
            {
                var point=lineHitResult.Point;var normal=lineHitResult.Normal;float t=(start.Y-point.Y)/distance;
                float gap=ScalarMath.Max(-ScalarMath.Max(Settings.Maximum,_radius),t*distance-half);
                if(t>0&&gap<=line&&normal.Y>=Settings.WalkableZ)
                {
                    // Native SetFromLineTrace restores the old sweep point,
                    // location, trace and time; it replaces the surface normal.
                    var lineHit=hit with{Hit=true,Penetrating=false,Normal=normal,Collider=lineHitResult.Collider};
                    return new(lineHit,true,true,result.FloorDistance,gap);
                }
            }
        }
        return result with{Walkable=false};
    }
    public AlsCharacterFloorResult<TCollider> Find(Vector3 start,float half,bool walking)
    {
        float distance=Settings.TraceDistance(walking);var floor=Compute(start,half,distance,distance);
        float perchRadius=ScalarMath.Clamp(_radius-Settings.PerchRadiusThreshold,.0011f,_radius);
        var d=floor.Hit.Point-floor.Hit.Location;d.Y=0;
        if(!floor.Blocking||floor.LineTrace||Settings.PerchRadiusThreshold<=Settings.EdgeReject||LengthSquared(d)<=perchRadius*perchRadius)return floor;
        PerchQueries++;
        float maxDistance=distance+(walking?Settings.PerchAdditionalHeight:0);
        var location=floor.Hit.Location;float above=ScalarMath.Max(0,floor.Hit.Point.Y-location.Y+half);
        var perch=Compute(location,half,ScalarMath.Max(0,maxDistance-above),maxDistance+_radius,perchRadius);
        if(!perch.Walkable||above+perch.FloorDistance>maxDistance)return floor with{Walkable=false};
        if(Settings.Average-floor.FloorDistance+perch.FloorDistance>=maxDistance)floor=floor with{FloorDistance=Settings.Average};
        if(!floor.Walkable)
            floor=floor with{Walkable=true,LineTrace=true,LineDistance=ScalarMath.Max(floor.FloorDistance,Settings.Minimum),
                Hit=floor.Hit with{Penetrating=false,Normal=perch.Hit.Normal,Collider=perch.Hit.Collider}};
        return floor;
    }
    private float Height(Vector3 location,AlsCharacterFloorResult<TCollider> floor)
    {
        float gap=floor.FloorDistance;
        if(floor.LineTrace)
        {
            if(gap<Settings.Minimum&&floor.LineDistance>=Settings.Minimum)return location.Y;
            gap=floor.LineDistance;
        }
        return gap<Settings.Minimum||gap>Settings.Maximum?location.Y+Settings.Average-gap:location.Y;
    }
    private void MoveHeight(float y)
    {
        var motion=Vector3.UnitY*(y-_world.Position.Y);
        if(motion==Vector3.Zero)return;
        var hit=_world.SweepHeight(motion);var travel=hit.Travel;
        if(hit.Blocking)
        {
            float t=AlsCharacterSweepMath.PullBackFraction(motion,hit.UnsafeFraction);
            travel+=motion*(t-hit.SafeFraction);
        }
        _world.Translate(travel);
    }
    public AlsCharacterFloorAdjustment<TCollider> Initialise(float half)
    {
        var start=_world.Position;var floor=Find(start,half,true);
        if(!floor.Walkable||_world.Velocity.Y>0)return new(false,floor,default);
        float target=Height(start,floor);_world.Snap(Settings.TraceDistance(true));MoveHeight(target);
        return new(true,floor,_world.Position-start);
    }
    public AlsCharacterFloorAdjustment<TCollider> AfterMove(float half,bool walking,float predictedY,Vector3 endVelocity,Vector3 motion)
    {
        var start=_world.Position;var location=start;
        if(walking)location.Y=predictedY;
        var floor=Find(location,half,walking);bool contact=_world.OnFloor;
        if(!walking&&endVelocity.Y<=0)
        {
            foreach(var contactHit in _world.Contacts(motion))
            {
                var normal=contactHit.Normal;var point=contactHit.Point;
                if(normal.Y>=Settings.WalkableZ&&point.Y<start.Y-half+_radius&&Edge(start,point,_radius))contact=true;
            }
        }
        bool ground=floor.Walkable&&(walking||contact&&endVelocity.Y<=0);
        if(ground){float target=Height(location,floor);_world.Snap(Settings.TraceDistance(walking));MoveHeight(target);}
        return new(ground,floor,_world.Position-start);
    }
    public AlsCharacterFloorAdjustment<TCollider> AfterSweep(float half)
    {
        var start=_world.Position;var floor=Find(start,half,true);
        if(floor.Walkable)MoveHeight(Height(start,floor));
        return new(floor.Walkable,floor,_world.Position-start);
    }
}
