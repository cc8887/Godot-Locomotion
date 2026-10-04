using System.Numerics;
using ScalarMath = System.Math;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsCharacterGroundSettings(float Radius, float Minimum, float Maximum,
    float EdgeReject, float StepHeight, float WalkableZ);
public interface IAlsCharacterGroundHit
{
    bool Blocking { get; }
    bool Penetrating { get; }
    bool Valid { get; }
    bool CanStep { get; }
    float Time { get; }
    Vector3 Point { get; }
    Vector3 Normal { get; }
    Vector3 ImpactNormal { get; }
    Vector3 Location { get; }
}
public interface IAlsCharacterGroundFloor
{
    bool Blocking { get; }
    bool Walkable { get; }
    bool LineTrace { get; }
    float FloorDistance { get; }
    float LineDistance { get; }
    Vector3 Point { get; }
    Vector3 Normal { get; }
}
// Queries and physical writes belong to the host. Checkpoints remain opaque,
// preserving the backend's full transform without converting its rotation.
public interface IAlsCharacterGroundWorld<THit, TFloor, TAdjustment, TCheckpoint>
    where THit : struct, IAlsCharacterGroundHit where TFloor : struct, IAlsCharacterGroundFloor
    where TAdjustment : struct where TCheckpoint : struct
{
    Vector3 Position { get; }
    bool Recovered { get; }
    TCheckpoint Capture();
    void Restore(TCheckpoint checkpoint);
    THit Move(Vector3 motion, float halfHeight, bool safe);
    TFloor Find(float halfHeight, bool walking);
    TAdjustment AfterSweep(float halfHeight);
}
public readonly record struct AlsCharacterGroundMove<THit, TAdjustment>(Vector3 Velocity, TAdjustment Floor,
    THit[] Contacts, bool Stepped, bool StepReverted);

// Per-character ground controller, Y-up in meters. Step rollback, contact
// ordering, two-wall response and measured velocity live independently of
// engine objects. Acceleration/braking remain in the existing ALS kernel.
public sealed class AlsCharacterGroundMovement<THit, TFloor, TAdjustment, TCheckpoint>
    where THit : struct, IAlsCharacterGroundHit where TFloor : struct, IAlsCharacterGroundFloor
    where TAdjustment : struct where TCheckpoint : struct
{
    private readonly IAlsCharacterGroundWorld<THit, TFloor, TAdjustment, TCheckpoint> _world;
    private readonly AlsCharacterGroundSettings _settings;
    private readonly List<THit> _contacts = [];
    private TFloor _current;
    private float _half;
    private bool _teleported;
    public AlsCharacterGroundMovement(IAlsCharacterGroundWorld<THit, TFloor, TAdjustment, TCheckpoint> world,
        AlsCharacterGroundSettings settings)
    {
        ArgumentNullException.ThrowIfNull(world);
        float[] fields = [settings.Radius, settings.Minimum, settings.Maximum, settings.EdgeReject, settings.StepHeight, settings.WalkableZ];
        if (fields.Any(f => !float.IsFinite(f)) || settings.Radius <= 0 || settings.Minimum <= 0 ||
            settings.Maximum < settings.Minimum || settings.EdgeReject < 0 || settings.StepHeight < 0 || settings.WalkableZ is <= 0 or > 1)
            throw new ArgumentException("Invalid ground movement settings.");
        _world = world; _settings = settings;
    }
    // Retain scalar binary32 ordering at this physical boundary. Numerics SIMD
    // Dot/Normalize may use a different reduction or reciprocal ordering.
    private static float Dot(Vector3 a, Vector3 b) => AlsCharacterSweepMath.Dot(a,b);
    private static float LengthSquared(Vector3 v) => Dot(v,v);
    private static Vector3 Cross(Vector3 a, Vector3 b) => new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
    private static Vector3 Normalize(Vector3 v)=>AlsCharacterSweepMath.Normalize(v);
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private bool Walkable(THit hit)=>hit.Valid&&hit.ImpactNormal.Y>=_settings.WalkableZ;
    private bool Edge(Vector3 location,Vector3 point)
    {
        var delta=point-location;delta.Y=0;
        float r=ScalarMath.Max(_settings.EdgeReject+.000001f,_settings.Radius-_settings.EdgeReject);
        return LengthSquared(delta)<r*r;
    }
    private THit Move(Vector3 motion,bool safe=true)
    {
        var hit=_world.Move(motion,_half,safe);
        _teleported|=_world.Recovered;
        if(hit.Blocking)_contacts.Add(hit);
        return hit;
    }
    private Vector3 Ramp(Vector3 delta,Vector3 normal,bool line)
    {
        if(!line&&normal.Y>1e-4f&&normal.Y<1-1e-4f&&normal.Y>=_settings.WalkableZ)
            delta.Y=-Dot(normal,delta)/normal.Y;
        return delta;
    }
    private Vector3 SlideVector(Vector3 delta,float time,Vector3 normal)=>AlsCharacterSweepMath.ProjectPlane(delta,normal)*time;
    private Vector3 TwoWall(Vector3 delta,THit hit,Vector3 oldNormal)
    {
        var normal=hit.Normal;var desired=delta;
        if(Dot(oldNormal,normal)<=0)
        {
            var direction=Normalize(Cross(normal,oldNormal));delta=direction*Dot(delta,direction)*(1-hit.Time);
            if(Dot(desired,delta)<0)delta=-delta;
        }
        else
        {
            delta=SlideVector(delta,1-hit.Time,normal);
            if(Dot(delta,desired)<=0)delta=Vector3.Zero;
            else if(ScalarMath.Abs(Dot(normal,oldNormal)-1)<1e-4f)delta+=normal*.0001f;
        }
        if(delta.Y>0)
        {
            if(normal.Y>=_settings.WalkableZ||hit.ImpactNormal.Y>=_settings.WalkableZ)
            {
                if(normal.Y>1e-4f)
                {
                    var scaled=Normalize(delta)*MathF.Sqrt(LengthSquared(desired));
                    delta=new Vector3(desired.X,scaled.Y/normal.Y,desired.Z)*(1-hit.Time);
                }
                else delta.Y=0;
                if(delta.Y>_settings.StepHeight)delta*=_settings.StepHeight/delta.Y;
            }
            else delta.Y=0;
        }
        else if(delta.Y<0&&_current.Blocking&&_current.FloorDistance<_settings.Minimum)delta.Y=0;
        return delta;
    }
    private float Slide(Vector3 delta,float time,THit hit)
    {
        var normal=hit.Normal;
        if(normal.Y>0&&!Walkable(hit)){normal.Y=0;normal=Normalize(normal);}
        else if(normal.Y< -1e-4f&&_current.Blocking&&_current.FloorDistance<_settings.Minimum)
        {
            var floorNormal=_current.Normal;
            if(Dot(delta,floorNormal)<0&&floorNormal.Y<1-1e-5f)normal=floorNormal;
            normal.Y=0;normal=Normalize(normal);
        }
        var slide=SlideVector(delta,time,normal);
        if(Dot(slide,delta)<=0)return 0;
        var next=Move(slide);float applied=next.Time;
        if(next.Valid)
        {
            var second=TwoWall(slide,next,normal);
            if(LengthSquared(second)>1e-10f&&Dot(second,delta)>0)
            {var last=Move(second);applied+=last.Time*(1-next.Time);}
        }
        return ScalarMath.Clamp(applied,0,1);
    }
    private bool Step(Vector3 delta,THit hit)
    {
        if(!hit.CanStep||_settings.StepHeight<=0)return false;
        var old=_world.Capture();var oldPosition=_world.Position;
        float floorGap=_current.LineTrace?_current.LineDistance:_current.FloorDistance;
        float baseY=oldPosition.Y-_half-ScalarMath.Max(0,floorGap);
        if(hit.Point.Y>oldPosition.Y+_half-_settings.Radius||hit.Point.Y<=baseY)return false;
        bool Fail(){_world.Restore(old);return false;}
        float up=ScalarMath.Max(0,_settings.StepHeight-ScalarMath.Max(0,floorGap));
        var upHit=Move(Vector3.UnitY*up,false);if(upHit.Penetrating)return Fail();
        var forward=Move(delta,false);if(forward.Penetrating)return Fail();
        if(forward.Blocking&&forward.Time==0&&Slide(delta,1-forward.Time,forward)==0)return Fail();
        else if(forward.Blocking&&forward.Time>0)Slide(delta,1-forward.Time,forward);
        var down=Move((-Vector3.UnitY)*(_settings.StepHeight+2*_settings.Maximum),false);
        if(down.Penetrating)return Fail();
        if(down.Valid)
        {
            float floorY=!_current.LineTrace&&Edge(hit.Location,hit.Point)?_current.Point.Y:oldPosition.Y-_half-_current.FloorDistance;
            float rise=down.Point.Y-floorY;
            if(rise>_settings.StepHeight||!Edge(down.Location,down.Point))return Fail();
            if(!Walkable(down)&&(Dot(delta,down.ImpactNormal)<0||down.Location.Y>oldPosition.Y))return Fail();
            if(rise>0&&!down.CanStep)return Fail();
            var floor=_world.Find(_half,true);
            if(down.Location.Y>oldPosition.Y&&!floor.Blocking&&hit.ImpactNormal.Y<.08f)return Fail();
        }
        return true;
    }
    public AlsCharacterGroundMove<THit, TAdjustment> Walk(Vector3 velocity,Vector3 endVelocity,float delta,float half,bool root)
    {
        if (!float.IsFinite(delta) || delta <= 0 || !float.IsFinite(half) || half < _settings.Radius || !Finite(velocity) || !Finite(endVelocity))
            throw new ArgumentException("Invalid ground movement frame.");
        _half=half;_contacts.Clear();_teleported=false;var start=_world.Position;
        _current=_world.Find(half,true);
        var motion=velocity*delta;motion.Y=0;bool stepped=false,reverted=false;
        if(_current.Walkable)
        {
            var hit=Move(Ramp(motion,_current.Normal,_current.LineTrace));
            if(hit.Penetrating)Slide(motion,1,hit);
            else if(hit.Valid)
            {
                float time=hit.Time;
                if(time>0&&hit.Normal.Y>1e-4f&&Walkable(hit))
                {float remain=1-time;hit=Move(Ramp(motion*remain,hit.ImpactNormal,false));time=ScalarMath.Clamp(time+hit.Time*remain,0,1);}
                if(hit.Valid)
                {
                    stepped=Step(motion*(1-time),hit);reverted=!stepped;
                    if(!stepped)Slide(motion,1-time,hit);
                }
            }
        }
        var floorAdjustment=_world.AfterSweep(half);
        var next=endVelocity;next.Y=0;
        // PhysWalking reconstructs velocity from the actual component move,
        // including StepUp rollback and floor correction. Summing requested
        // sweep fractions can miss recovery and backend position rounding.
        if(!root&&!_teleported&&_contacts.Count>0)
        {var moved=_world.Position-start;next=new(moved.X/delta,0,moved.Z/delta);}
        return new(next,floorAdjustment,_contacts.ToArray(),stepped,reverted);
    }
}
