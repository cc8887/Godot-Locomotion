namespace GodotAls.Core.Locomotion;

public readonly record struct AlsCharacterMotionSettings(AlsCharacterVelocitySettings Standing,
    AlsCharacterVelocitySettings Crouching,AlsCharacterFallingSettings Falling,float MaxAcceleration,float JumpVelocity);
public readonly record struct AlsCharacterMotionStep(AlsDoubleVector StartVelocity,AlsDoubleVector Acceleration,
    float Analog,bool Falling,AlsDoubleVector Velocity,AlsDoubleVector Displacement);

// One physical substep's input/jump/integration policy. The host supplies the
// consumed direction and collision receipt; this operation owns no clock.
// Vectors use centimetres and Z-up, as do the existing ALS velocity kernels.
public sealed class AlsCharacterMotion
{
    private readonly AlsCharacterMotionSettings _settings;
    public AlsCharacterMotion(AlsCharacterMotionSettings settings)
    {
        if(!float.IsFinite(settings.MaxAcceleration)||settings.MaxAcceleration<=0||
            !float.IsFinite(settings.JumpVelocity)||settings.JumpVelocity<0)
            throw new ArgumentException("Invalid character motion settings.");
        _settings=settings;
    }
    public AlsCharacterMotionStep Advance(AlsDoubleVector velocity,AlsDoubleVector consumedDirection,
        bool grounded,bool crouching,bool jump,bool rootMotion,float delta)
    {
        if(!velocity.IsFinite||!consumedDirection.IsFinite||!float.IsFinite(delta)||delta<=0||delta>.05f)
            throw new ArgumentException("Invalid character motion substep.");
        var acceleration=consumedDirection*_settings.MaxAcceleration;
        float analog=(float)System.Math.Clamp(AlsCharacterVelocity.InputAmount(acceleration,_settings.MaxAcceleration),0,1);
        bool falling=!grounded;
        if(jump&&!falling&&!crouching)
        {velocity=new(velocity.X,velocity.Y,System.Math.Max(velocity.Z,_settings.JumpVelocity));falling=true;}
        AlsDoubleVector next,displacement;
        if(falling)
        {
            var settings=_settings.Falling;
            if(rootMotion)settings=settings with{Velocity=settings.Velocity with{Friction=0,SeparateBrakingFriction=false,BrakingDeceleration=0}};
            var step=AlsCharacterFalling.Advance(velocity,rootMotion?default:acceleration,analog,delta,settings);
            next=step.Velocity;displacement=step.Displacement;
        }
        else
        {
            var planar=new AlsDoubleVector(velocity.X,velocity.Y,0);
            next=rootMotion?planar:AlsCharacterVelocity.Advance(planar,acceleration,analog,delta,
                crouching?_settings.Crouching:_settings.Standing);
            displacement=next*delta;
        }
        return new(velocity,acceleration,analog,falling,next,displacement);
    }
    public static AlsDoubleVector ResolveVelocity(in AlsCharacterMotionStep step,AlsDoubleVector physicalVelocity,
        AlsDoubleVector? nativeVelocity,int collisions,bool grounded,bool rootMotion)
    {
        if(!physicalVelocity.IsFinite||nativeVelocity is {IsFinite:false}||collisions<0)
            throw new ArgumentException("Invalid movement collision receipt.");
        return nativeVelocity??(!rootMotion&&collisions==0&&(!grounded||step.Velocity.Z>=0)?step.Velocity:physicalVelocity);
    }
}
