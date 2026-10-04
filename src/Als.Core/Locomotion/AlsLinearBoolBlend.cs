namespace GodotAls.Core.Locomotion;

// FInputAlphaBoolBlend with the original linear option and 0.2 second times.
// Initialize resets only Initialized; the embedded FAlphaBlend values survive.
public readonly record struct AlsLinearBoolBlend(bool Initialized,float Begin,float Target,
    float Alpha,float Value,float Time,float Remaining)
{
    public static AlsLinearBoolBlend Default=>new(false,0,1,0,0,.2f,.2f);
    public AlsLinearBoolBlend Reinitialize()=>this with{Initialized=false};
    public AlsLinearBoolBlend Step(bool enabled,float delta)
    {
        if(!float.IsFinite(delta)||delta<0)throw new ArgumentOutOfRangeException(nameof(delta));
        float target=enabled?1:0,begin=Begin,alpha=Alpha,value=Value,time=Time,remaining=Remaining;
        if(!Initialized)
        {
            begin=value;time=0;remaining=0;alpha=1;value=target;
        }
        else if(target!=Target)
        {
            begin=value;time=.2f;
            alpha=begin==target?1:(value-begin)/(target-begin);
            alpha=System.Math.Clamp(alpha,0,1);value=begin+(target-begin)*alpha;
            remaining=time*System.Math.Abs(1-alpha);
        }
        if(value!=target)
        {
            if(remaining>delta)
            {
                var distance=1-alpha;alpha+=(distance/remaining)*delta;remaining-=delta;
                alpha=System.Math.Clamp(alpha,0,1);value=begin+(target-begin)*alpha;
            }
            else{remaining=0;alpha=1;value=target;}
        }
        return new(true,begin,target,alpha,value,time,remaining);
    }
}
