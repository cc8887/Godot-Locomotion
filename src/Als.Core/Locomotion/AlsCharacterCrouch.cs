using System.Numerics;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsCharacterCrouchSettings(float Radius,float StandingHalfHeight,float CrouchedHalfHeight,bool CanCrouch);
public readonly record struct AlsCharacterCrouchFloor(bool Hit,Vector3 Start,Vector3 Point);
public readonly record struct AlsCharacterCrouchAdjustment(bool Changed,bool Crouching,float HalfHeight,float Shift);
public interface IAlsCharacterCrouchAirQuery:IDisposable
{
    bool Overlap();
    float SweepFraction();
}
public interface IAlsCharacterCrouchWorld
{
    bool Blocked(float offset);
    AlsCharacterCrouchFloor Floor();
    IAlsCharacterCrouchAirQuery AirQuery(float radius);
}

// Stateless stance decision. Host applies the accepted capsule/model update
// once; animation cancellation cannot repeat these physical queries/writes.
public sealed class AlsCharacterCrouch
{
    private readonly IAlsCharacterCrouchWorld _world;
    private readonly AlsCharacterCrouchSettings _settings;
    public AlsCharacterCrouch(IAlsCharacterCrouchWorld world,AlsCharacterCrouchSettings settings)
    {
        ArgumentNullException.ThrowIfNull(world);
        if(!float.IsFinite(settings.Radius)||!float.IsFinite(settings.StandingHalfHeight)||!float.IsFinite(settings.CrouchedHalfHeight)||
            settings.Radius<=0||settings.CrouchedHalfHeight<settings.Radius||settings.StandingHalfHeight<settings.CrouchedHalfHeight)
            throw new ArgumentException("Invalid crouch settings.");
        _world=world;_settings=settings;
    }
    public AlsCharacterCrouchAdjustment Decide(bool desired,bool current,bool grounded,float currentHalfHeight)
    {
        if(!float.IsFinite(currentHalfHeight)||currentHalfHeight<_settings.Radius)throw new ArgumentException("Invalid current capsule height.");
        var unchanged=new AlsCharacterCrouchAdjustment(false,current,currentHalfHeight,0);
        desired&=_settings.CanCrouch;if(desired==current)return unchanged;
        float next=desired?_settings.CrouchedHalfHeight:_settings.StandingHalfHeight;
        float shift=grounded?next-currentHalfHeight:0;
        if(!desired)
        {
            if(grounded)shift+=.00001f;
            if(_world.Blocked(shift))
            {
                if(grounded)
                {
                    var floor=_world.Floor();var delta=floor.Start-floor.Point;
                    float gap=floor.Hit?MathF.Sqrt(AlsCharacterSweepMath.Dot(delta,delta))-currentHalfHeight:0;
                    if(gap<=.00001f)return unchanged;
                    shift-=gap-.00001f;
                }
                else
                {
                    using var query=_world.AirQuery(_settings.Radius);
                    if(query.Overlap())return unchanged;
                    float distance=query.SweepFraction()*_settings.Radius+_settings.Radius;
                    shift=-distance+_settings.StandingHalfHeight+.00002f+.0095f;
                }
                if(_world.Blocked(shift))return unchanged;
            }
        }
        return new(true,desired,next,shift);
    }
}
