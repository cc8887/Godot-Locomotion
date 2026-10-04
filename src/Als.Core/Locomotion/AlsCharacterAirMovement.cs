using System.Numerics;
using GodotAls.Core.Math;
using ScalarMath = System.Math;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsCharacterFallingInput(AlsDoubleVector Velocity,AlsDoubleVector Acceleration,float Analog,bool Crouching);
public readonly record struct AlsCharacterAirMove<THit,TAdjustment>(AlsDoubleVector Velocity,TAdjustment Floor,
    THit[] Contacts,int Substeps,int ApexSplits,float LandingRemaining,bool RandomEscape);
public readonly record struct AlsCharacterAirSettings(float Radius,float EdgeReject,float WalkableZ,float PerchRadiusThreshold,
    int MaxJumpApexAttempts,float MaxSimulationTimeStep,int MaxSimulationIterations,float JumpVelocity,
    AlsCharacterFallingSettings Falling,AlsCharacterVelocitySettings Standing,AlsCharacterVelocitySettings Crouching);
public interface IAlsCharacterFloorAdjustment { bool Grounded {get;} }

// Physical queries and their opaque collision payload stay with the backend.
// Geometry is Y-up meters; velocity/integration retain ALS Z-up centimeters.
public interface IAlsCharacterAirWorld<THit,TFloor,TAdjustment>
    where THit:struct,IAlsCharacterGroundHit where TFloor:struct,IAlsCharacterGroundFloor
    where TAdjustment:struct,IAlsCharacterFloorAdjustment
{
    Vector3 Position {get;}
    bool Recovered {get;}
    THit Move(Vector3 motion,float halfHeight);
    TFloor Find(float halfHeight);
    THit FloorHit(TFloor floor);
    TAdjustment AfterLanding(float halfHeight);
    AlsCharacterGroundMove<THit,TAdjustment> Walk(Vector3 velocity,float delta,float halfHeight,bool root);
}

public sealed class AlsCharacterAirMovement<THit,TFloor,TAdjustment>
    where THit:struct,IAlsCharacterGroundHit where TFloor:struct,IAlsCharacterGroundFloor
    where TAdjustment:struct,IAlsCharacterFloorAdjustment
{
    private const float MinTick=1e-6f,VerticalSlope=.001f;
    private readonly IAlsCharacterAirWorld<THit,TFloor,TAdjustment> _world;
    private readonly AlsCharacterAirSettings _settings;
    private readonly List<THit> _contacts=[];
    private float _half;
    private bool _teleported;
    private uint _random;
    public uint RandomSeed=>_random;
    public AlsCharacterAirMovement(IAlsCharacterAirWorld<THit,TFloor,TAdjustment> world,AlsCharacterAirSettings settings,uint randomSeed)
    {
        ArgumentNullException.ThrowIfNull(world);
        float[] fields=[settings.Radius,settings.EdgeReject,settings.WalkableZ,settings.PerchRadiusThreshold,settings.MaxSimulationTimeStep,settings.JumpVelocity];
        if(fields.Any(v=>!float.IsFinite(v))||settings.Radius<=0||settings.EdgeReject<0||settings.WalkableZ is <=0 or >1||
            settings.PerchRadiusThreshold<0||settings.MaxSimulationTimeStep<MinTick||settings.MaxSimulationIterations<1||settings.MaxJumpApexAttempts<0||settings.JumpVelocity<0||
            !float.IsFinite(settings.Falling.GravityZ)||settings.Falling.GravityZ>=0||!float.IsFinite(settings.Falling.TerminalVelocity)||settings.Falling.TerminalVelocity<=0)
            throw new ArgumentException("Invalid air movement settings.");
        _world=world;_settings=settings;_random=randomSeed;
    }
    private static float LengthSquared(Vector3 v)=>v.X*v.X+v.Y*v.Y+v.Z*v.Z;
    private static Vector3 Position(AlsDoubleVector v)=>new((float)(v.Y*.01),(float)(v.Z*.01),(float)(-v.X*.01));
    private static AlsDoubleVector NativePosition(Vector3 v)=>new(-(double)v.Z*100,(double)v.X*100,(double)v.Y*100);
    private static AlsDoubleVector Planar(AlsDoubleVector v)=>new(v.X,v.Y,0);
    private static AlsDoubleVector Normal(AlsDoubleVector v)=>v.SafeNormal(1e-8);
    private static AlsDoubleVector Project(AlsDoubleVector v,AlsDoubleVector n)=>v-n*AlsDoubleVector.Dot(v,n);
    private static AlsDoubleVector N(Vector3 v)=>new(-v.Z,v.X,v.Y);
    private THit Move(AlsDoubleVector delta)
    {var hit=_world.Move(Position(delta),_half);_teleported|=_world.Recovered;if(hit.Blocking)_contacts.Add(hit);return hit;}
    private bool Edge(Vector3 location,Vector3 point)
    {var d=point-location;d.Y=0;float r=ScalarMath.Max(_settings.EdgeReject+.000001f,_settings.Radius-_settings.EdgeReject);return LengthSquared(d)<r*r;}
    private bool Landing(THit hit)
    {
        if(!hit.Blocking)return false;
        if(hit.Penetrating){if(hit.Normal.Y<1e-4f)return false;}
        else if(hit.ImpactNormal.Y<_settings.WalkableZ||hit.Point.Y>=hit.Location.Y-_half+_settings.Radius||!Edge(hit.Location,hit.Point))return false;
        return _world.Find(_half).Walkable;
    }
    private bool EdgeLanding(THit hit)
    {
        if(hit.Penetrating||hit.Normal.Y<=1e-4f)return false;
        if(ScalarMath.Abs(hit.Normal.X-hit.ImpactNormal.X)<=1e-4f&&ScalarMath.Abs(hit.Normal.Y-hit.ImpactNormal.Y)<=1e-4f&&ScalarMath.Abs(hit.Normal.Z-hit.ImpactNormal.Z)<=1e-4f)return false;
        if(!Edge(_world.Position,hit.Point))return false;
        var floor=_world.Find(_half);
        if(!floor.Walkable||floor.LineTrace)return false;
        return Landing(_world.FloorHit(floor));
    }
    private static AlsDoubleVector LimitAir(AlsDoubleVector acceleration,THit hit)
    {
        var n=N(hit.Normal);
        if(hit.Valid&&n.Z>VerticalSlope&&AlsDoubleVector.Dot(acceleration,n)<0)return Project(acceleration,Normal(Planar(n)));
        if(hit.Penetrating&&AlsDoubleVector.Dot(acceleration,n)<=0)return default;
        return acceleration;
    }
    private static AlsDoubleVector SlopeBoost(AlsDoubleVector result,AlsDoubleVector delta,float time,AlsDoubleVector normal)
    {
        float z=(float)result.Z,limit=(float)delta.Z*time;
        if(z>0&&z-limit>1e-4f)
        {
            var original=result;result=limit>0?result*(limit/z):default;
            result+=Project(Planar(original-result),Normal(Planar(normal)));
        }
        return result;
    }
    private static AlsDoubleVector Slide(AlsDoubleVector delta,float time,AlsDoubleVector normal)=>SlopeBoost(Project(delta,normal)*time,delta,time,normal);
    private static AlsDoubleVector TwoWall(AlsDoubleVector delta,THit hit,AlsDoubleVector oldNormal)
    {
        var n=N(hit.Normal);var desired=delta;
        if(AlsDoubleVector.Dot(oldNormal,n)<=0)
        {var direction=Normal(AlsDoubleVector.Cross(n,oldNormal));delta=direction*(AlsDoubleVector.Dot(delta,direction)*(1-hit.Time));if(AlsDoubleVector.Dot(desired,delta)<0)delta*= -1;}
        else
        {delta=Slide(delta,1-hit.Time,n);if(AlsDoubleVector.Dot(delta,desired)<=0)delta=default;else if(ScalarMath.Abs(AlsDoubleVector.Dot(n,oldNormal)-1)<1e-4f)delta+=n*.01f;}
        return delta;
    }
    public AlsCharacterAirMove<THit,TAdjustment> Fall(AlsCharacterFallingInput input,float delta,float half,AlsDoubleVector? rootVelocity=null)
    {
        if(!input.Velocity.IsFinite || !input.Acceleration.IsFinite || !float.IsFinite(input.Analog) || input.Analog<0 || input.Analog>1 ||
            !float.IsFinite(delta) || delta<=0 || !float.IsFinite(half) || half<_settings.Radius || rootVelocity.HasValue&&!rootVelocity.Value.IsFinite)
            throw new ArgumentException("Invalid air movement frame.");
        _half=half;_contacts.Clear();var velocity=input.Velocity;float remaining=delta;
        int iterations=0,substeps=0,apex=0;bool randomEscape=false;float landingRemaining=0;
        var settings=_settings.Falling;
        var fallAcceleration=AlsCharacterFalling.LateralAcceleration(velocity,input.Acceleration,settings);
        bool limited=Planar(fallAcceleration).LengthSquared>0;
        AlsDoubleVector Integrate(AlsDoubleVector old,AlsDoubleVector accel,float t,float gravityTime)
        {
            double z=ScalarMath.Max(-settings.TerminalVelocity,old.Z+(double)settings.GravityZ*gravityTime);
            if(rootVelocity.HasValue)
                return new(old.X,old.Y,z);
            var next=AlsCharacterFalling.AdvanceResolved(old,accel,input.Analog,t,settings).Velocity;
            return new(next.X,next.Y,z);
        }
        AlsCharacterAirMove<THit,TAdjustment> Land(float time)
        {
            landingRemaining=time;velocity=Planar(velocity);var floor=_world.AfterLanding(_half);
            while(time>=MinTick&&iterations<_settings.MaxSimulationIterations)
            {
                iterations++;float walkingTick=time;
                if(walkingTick>_settings.MaxSimulationTimeStep&&iterations<_settings.MaxSimulationIterations)
                    walkingTick=ScalarMath.Min(_settings.MaxSimulationTimeStep,walkingTick*.5f);
                walkingTick=ScalarMath.Max(MinTick,walkingTick);time-=walkingTick;
                velocity=rootVelocity.HasValue?Planar(rootVelocity.Value):AlsCharacterVelocity.Advance(velocity,input.Acceleration,input.Analog,walkingTick,
                    input.Crouching?_settings.Crouching:_settings.Standing);
                var v=Position(velocity);var walk=_world.Walk(v,walkingTick,_half,rootVelocity.HasValue);
                _contacts.AddRange(walk.Contacts);floor=walk.Floor;
                if(walk.Contacts.Length>0&&!rootVelocity.HasValue)velocity=NativePosition(walk.Velocity);
                // The broader walking ledge/refund lifecycle remains outside
                // this supported landing remainder; never walk without support.
                if(!floor.Grounded||velocity.NearlyZero(1e-4f))break;
            }
            return new(velocity,floor,_contacts.ToArray(),substeps,apex,landingRemaining,randomEscape);
        }
        while(remaining>=MinTick&&iterations<_settings.MaxSimulationIterations)
        {
            iterations++;substeps++;float tick=remaining;
            if(tick>_settings.MaxSimulationTimeStep&&iterations<_settings.MaxSimulationIterations)tick=ScalarMath.Min(_settings.MaxSimulationTimeStep,tick*.5f);
            tick=ScalarMath.Max(MinTick,tick);remaining-=tick;_teleported=false;
            var oldLocation=_world.Position;var old=velocity;float gravityTime=tick;
            velocity=Integrate(old,fallAcceleration,tick,gravityTime);
            if(rootVelocity.HasValue)velocity=new(rootVelocity.Value.X,rootVelocity.Value.Y,velocity.Z);
            if(old.Z>0&&velocity.Z<=0&&apex<_settings.MaxJumpApexAttempts)
            {
                var derived=(velocity-old)*(1d/tick);
                if(ScalarMath.Abs(derived.Z)>1e-8)
                {
                    float toApex=(float)(-old.Z/derived.Z);
                    if(toApex>=.0001f&&toApex<tick)
                    {velocity=old+derived*toApex;velocity=Planar(velocity);remaining+=tick-toApex;tick=toApex;iterations--;apex++;}
                }
            }
            var adjusted=(old+velocity)*.5*tick;var hit=Move(adjusted);
            float lastSlice=tick,subRemaining=tick*(1-hit.Time);
            if(hit.Blocking)
            {
                if(Landing(hit))return Land(remaining+subRemaining);
                adjusted=velocity*tick;
                if(EdgeLanding(hit))return Land(remaining+subRemaining);
                var noAir=old;var airAcceleration=input.Acceleration;
                if(limited)
                {noAir=Integrate(old,default,tick,gravityTime);airAcceleration=(velocity-noAir)*(1d/tick);adjusted=(noAir+LimitAir(airAcceleration,hit)*tick)*tick;}
                var oldNormal=N(hit.Normal);var oldImpact=N(hit.ImpactNormal);
                var slide=Slide(adjusted,1-hit.Time,oldNormal);
                void SetDeflected(AlsDoubleVector deflected,float time)
                {if(time>1e-4f&&!_teleported){var next=deflected*(1d/time);velocity=rootVelocity.HasValue?new(velocity.X,velocity.Y,next.Z):next;}}
                SetDeflected(slide,subRemaining);
                if(subRemaining>1e-4f&&AlsDoubleVector.Dot(slide,adjusted)>0)
                {
                    hit=Move(slide);
                    if(hit.Blocking)
                    {
                        lastSlice=subRemaining;subRemaining*=1-hit.Time;
                        if(Landing(hit))return Land(remaining+subRemaining);
                        if(limited&&hit.Normal.Y>VerticalSlope)slide=Slide(noAir*lastSlice,1,oldNormal);
                        var beforeTwo=slide;slide=TwoWall(slide,hit,oldNormal);slide=SlopeBoost(slide,beforeTwo,1-hit.Time,N(hit.Normal));
                        if(limited){var air=LimitAir(airAcceleration,hit)*subRemaining;if(AlsDoubleVector.Dot(air,oldNormal)>0)slide+=air*subRemaining;}
                        SetDeflected(slide,subRemaining);
                        bool ditch=oldImpact.Z>0&&hit.ImpactNormal.Y>0&&ScalarMath.Abs(slide.Z)<=1e-4f&&AlsDoubleVector.Dot(N(hit.ImpactNormal),oldImpact)<0;
                        hit=Move(slide);
                        if(hit.Time==0)
                        {var side=Normal(Planar(oldNormal+N(hit.ImpactNormal)));if(side.NearlyZero(1e-4f))side=Normal(new(oldNormal.Y,-oldNormal.X,0));hit=Move(side);}
                        if(ditch||Landing(hit)||hit.Time==0)return Land(0);
                        if(_settings.PerchRadiusThreshold>0&&hit.Time==1&&oldImpact.Z>=_settings.WalkableZ)
                        {
                            var moved=NativePosition(_world.Position-oldLocation);
                            if(ScalarMath.Abs(moved.Z)<=.2f*tick&&ScalarMath.Sqrt(Planar(moved).LengthSquared)<=4f*tick)
                            {
                                float speed=_settings.Standing.MaxSpeed;
                                velocity=new(velocity.X+.25f*speed*(AlsRandomStream.NextFraction(ref _random)-.5f),velocity.Y+.25f*speed*(AlsRandomStream.NextFraction(ref _random)-.5f),ScalarMath.Max(_settings.JumpVelocity*.25f,1));
                                Move(velocity*tick);randomEscape=true;
                            }
                        }
                    }
                }
            }
            if(Planar(velocity).LengthSquared<=1e-4f*10f)velocity=new(0,0,velocity.Z);
        }
        // Entering Falling clears CMC CurrentFloor. Support queries made for
        // possible landing are local results, not the walking floor cache.
        return new(velocity,default,_contacts.ToArray(),substeps,apex,landingRemaining,randomEscape);
    }
}
