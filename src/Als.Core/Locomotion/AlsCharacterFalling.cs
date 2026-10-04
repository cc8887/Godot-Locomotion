namespace GodotAls.Core.Locomotion;

public readonly record struct AlsCharacterFallingSettings(AlsCharacterVelocitySettings Velocity,
    float MaxAcceleration,float AirControl,float BoostMultiplier,float BoostThreshold,
    float GravityZ,float TerminalVelocity);

public readonly record struct AlsCharacterFallingStep(AlsDoubleVector Acceleration,
    AlsDoubleVector Velocity,AlsDoubleVector Displacement);

// Original CMC lateral acceleration, CalcVelocity and NewFallVelocity for one
// unobstructed falling substep. Collision response and apex splitting belong
// to the physical host. All vectors use Unreal centimetres and Z-up.
public static class AlsCharacterFalling
{
    public static AlsCharacterFallingStep Advance(in AlsDoubleVector velocity,
        in AlsDoubleVector acceleration,float analog,float delta,in AlsCharacterFallingSettings settings)
    {
        if(!velocity.IsFinite||!acceleration.IsFinite||!float.IsFinite(delta)||delta<0||
            !Valid(settings.MaxAcceleration)||!Valid(settings.AirControl)||!Valid(settings.BoostMultiplier)||
            !Valid(settings.BoostThreshold)||!float.IsFinite(settings.GravityZ)||!Valid(settings.TerminalVelocity))
            throw new ArgumentException("Invalid falling input.");
        return AdvanceResolved(velocity,LateralAcceleration(velocity,acceleration,settings),analog,delta,settings);
    }
    // PhysFalling resolves air control once before its iteration loop. Apex
    // refunds must not recalculate the boost using a later substep's speed.
    public static AlsDoubleVector LateralAcceleration(in AlsDoubleVector velocity,
        in AlsDoubleVector acceleration,in AlsCharacterFallingSettings settings)
    {
        if(!velocity.IsFinite||!acceleration.IsFinite||!Valid(settings.MaxAcceleration)||
            !Valid(settings.AirControl)||!Valid(settings.BoostMultiplier)||!Valid(settings.BoostThreshold))
            throw new ArgumentException("Invalid falling acceleration.");
        var planar=new AlsDoubleVector(velocity.X,velocity.Y,0);
        var fallAcceleration=new AlsDoubleVector(acceleration.X,acceleration.Y,0);
        float control=settings.AirControl;
        if(control!=0&&settings.BoostMultiplier>0&&planar.LengthSquared<settings.BoostThreshold*settings.BoostThreshold)
            control=MathF.Min(1,settings.BoostMultiplier*control);
        fallAcceleration*=control;
        if(settings.MaxAcceleration<1e-4f)fallAcceleration=default;
        else if(fallAcceleration.LengthSquared>(double)settings.MaxAcceleration*settings.MaxAcceleration)
            fallAcceleration*=settings.MaxAcceleration/System.Math.Sqrt(fallAcceleration.LengthSquared);
        return fallAcceleration;
    }
    public static AlsCharacterFallingStep AdvanceResolved(in AlsDoubleVector velocity,
        in AlsDoubleVector fallAcceleration,float analog,float delta,in AlsCharacterFallingSettings settings)
    {
        if(!velocity.IsFinite||!fallAcceleration.IsFinite||!float.IsFinite(delta)||delta<0||
            !float.IsFinite(settings.GravityZ)||!Valid(settings.TerminalVelocity))
            throw new ArgumentException("Invalid resolved falling step.");
        var planar=new AlsDoubleVector(velocity.X,velocity.Y,0);
        var next=AlsCharacterVelocity.Advance(planar,fallAcceleration,analog,delta,settings.Velocity);
        double z=velocity.Z;
        if(delta>0)
        {
            z+=(double)settings.GravityZ*delta;
            if(settings.GravityZ<0&&z < -settings.TerminalVelocity)z=-settings.TerminalVelocity;
            else if(settings.GravityZ>0&&z > settings.TerminalVelocity)z=settings.TerminalVelocity;
        }
        next=new(next.X,next.Y,z);
        return new(fallAcceleration,next,(velocity+next)*.5*delta);
    }
    private static bool Valid(float value)=>float.IsFinite(value)&&value>=0;
}
