namespace GodotAls.Core.Locomotion;

public readonly record struct AlsCharacterVelocitySettings(float MaxSpeed,float MinAnalogSpeed,
    float Friction,bool SeparateBrakingFriction,float BrakingFriction,float BrakingFrictionFactor,
    float BrakingDeceleration,float BrakingSubStepTime);

// UCharacterMovementComponent::CalcVelocity's input-driven, non-fluid path.
// Collision, gravity, navigation requests and animation root motion belong to
// the physical motor; this operation owns no frame or animation history.
public static class AlsCharacterVelocity
{
    public static double InputAmount(in AlsDoubleVector acceleration,float maxAcceleration)
    {
        if(!acceleration.IsFinite||!float.IsFinite(maxAcceleration)||maxAcceleration<0)
            throw new ArgumentException("Invalid movement input amount.");
        return maxAcceleration>0?System.Math.Sqrt(acceleration.LengthSquared)/maxAcceleration:0;
    }
    public static AlsDoubleVector Advance(in AlsDoubleVector velocity,in AlsDoubleVector acceleration,
        float analog,float delta,in AlsCharacterVelocitySettings settings)
    {
        if(!velocity.IsFinite||!acceleration.IsFinite||!float.IsFinite(delta)||delta<0||
            !float.IsFinite(analog)||analog is <0 or >1||
            !Valid(settings.MaxSpeed)||!Valid(settings.MinAnalogSpeed)||!Valid(settings.Friction)||!Valid(settings.BrakingFriction)||
            !Valid(settings.BrakingFrictionFactor)||!Valid(settings.BrakingDeceleration)||!Valid(settings.BrakingSubStepTime))
            throw new ArgumentException("Invalid character velocity input.");
        if(delta<1e-6f)return velocity;
        var next=velocity;float friction=settings.Friction;
        float max=MathF.Max(settings.MaxSpeed*analog,settings.MinAnalogSpeed);
        bool zero=acceleration==default,over=Exceeds(next,max);
        if(zero||over)
        {
            next=Brake(next,delta,settings.SeparateBrakingFriction?settings.BrakingFriction:friction,
                settings.BrakingFrictionFactor,settings.BrakingDeceleration,settings.BrakingSubStepTime);
            if(over&&next.LengthSquared<max*max&&AlsDoubleVector.Dot(acceleration,velocity)>0)
                next=Normal(velocity)*max;
        }
        else
        {
            float speed=(float)System.Math.Sqrt(next.LengthSquared);
            next-= (next-Normal(acceleration)*speed)*MathF.Min(delta*friction,1);
        }
        if(!zero)
        {
            float limit=Exceeds(next,max)?(float)System.Math.Sqrt(next.LengthSquared):max;
            next+=acceleration*delta;
            if(limit<1e-4f)next=default;
            else if(next.LengthSquared>(double)limit*limit)next*=limit*(1d/System.Math.Sqrt(next.LengthSquared));
        }
        return next;
    }
    private static bool Valid(float value)=>float.IsFinite(value)&&value>=0;
    private static bool Exceeds(in AlsDoubleVector value,float speed)=>value.LengthSquared>speed*speed*1.01f;
    private static AlsDoubleVector Normal(in AlsDoubleVector v)=>
        v.LengthSquared==1?v:v.LengthSquared<1e-8?default:v*(1d/System.Math.Sqrt(v.LengthSquared));
    private static AlsDoubleVector Brake(AlsDoubleVector velocity,float delta,float friction,float factor,float deceleration,float substep)
    {
        if(velocity==default)return velocity;
        friction=MathF.Max(0,friction*MathF.Max(0,factor));
        if(friction==0&&deceleration==0)return velocity;
        var previous=velocity;var reverse=deceleration==0?default:Normal(velocity)*-deceleration;
        float remaining=delta,max=System.Math.Clamp(substep,1f/75f,1f/20f);
        while(remaining>=1e-6f)
        {
            float step=remaining>max&&friction!=0?MathF.Min(max,remaining*.5f):remaining;
            remaining-=step;velocity+=(velocity*-friction+reverse)*step;
            if(AlsDoubleVector.Dot(velocity,previous)<=0)return default;
        }
        float size=(float)velocity.LengthSquared;
        return size<=1e-4f||deceleration!=0&&size<=100?default:velocity;
    }
}
