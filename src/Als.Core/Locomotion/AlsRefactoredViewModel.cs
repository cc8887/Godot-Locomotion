using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

// Native UE degrees. World rotations keep their double boundary until the
// explicit UE_REAL_TO_FLOAT casts; time, curves and persistent state are float.
public readonly record struct AlsRefactoredViewInput(
    double ViewYaw, double ViewPitch, double CharacterYaw, double CharacterPitch,
    float ViewYawSpeed, float CharacterYawVelocity, double InputYaw, double TargetYaw,
    bool HasInput, bool HasAction, AlsRotationMode RotationMode, bool FirstPerson,
    bool PendingUpdate, bool RelativeBaseRotation, double BaseDeltaYaw,
    float Delta, float RealDelta, float ViewBlock, float PoseAiming);

public readonly record struct AlsRefactoredViewState(float YawAngle,float PitchAngle,float PitchAmount,float HeadBlendAmount)
{
    public static AlsRefactoredViewState Initial=>new(0,0,.5f,1);
}
public readonly record struct AlsRefactoredSpineState(bool Allowed,float Amount,float Scale,float Bias,
    float LastYaw,float LastWorldYaw,float Yaw,float FinalYaw)
{
    public static AlsRefactoredSpineState Initial=>new(false,0,1,0,0,0,0,0);
}
public readonly record struct AlsRefactoredHeadState(bool InitializationRequired,bool SwitchingSides,
    float Pitch,float Yaw,float YawVelocity,float YawAmount)
{
    public static AlsRefactoredHeadState Initial=>new(true,false,0,0,0,.5f);
}
public readonly record struct AlsRefactoredHeadSettings(float PitchHalfLife,float YawHalfLife,
    float SwitchSidesHalfLife,float FirstPersonPitchHalfLife,float FirstPersonYawHalfLife);

/// <summary>Separate native RefreshView/RefreshSpine and graph Head callbacks.
/// Pure candidate transforms: the owning graph must choose callback order and commit
/// state atomically with its pose. No implicit Head update when the graph skips it.</summary>
public static class AlsRefactoredViewModel
{
    public static (AlsRefactoredViewState View,AlsRefactoredSpineState Spine) RefreshView(
        in AlsRefactoredViewInput input,in AlsRefactoredViewState previous,in AlsRefactoredSpineState previousSpine)
    {
        Validate(input);Finite([previous.YawAngle,previous.PitchAngle,previous.PitchAmount,previous.HeadBlendAmount,
            previousSpine.Amount,previousSpine.Scale,previousSpine.Bias,previousSpine.LastYaw,
            previousSpine.LastWorldYaw,previousSpine.Yaw,previousSpine.FinalYaw]);
        var view=previous;
        if(!input.HasAction)
        {
            var pitch=Unwind((float)(input.ViewPitch-input.CharacterPitch));
            view=view with {YawAngle=Unwind((float)(input.ViewYaw-input.CharacterYaw)),PitchAngle=pitch,PitchAmount=.5f-pitch/180};
        }
        var viewAmount=1-System.Math.Clamp(input.ViewBlock,0,1);
        var aiming=System.Math.Clamp(input.PoseAiming,0,1);
        view=view with {HeadBlendAmount=viewAmount*(1-aiming)};
        var spine=previousSpine;
        var allowed=input.RotationMode==AlsRotationMode.Aiming || input.FirstPerson;
        if(spine.Allowed!=allowed)
        {
            var scale=allowed ? Full(spine.Amount)?1:1/(1-spine.Amount) : Relevant(spine.Amount)?1/spine.Amount:1;
            spine=spine with {Allowed=allowed,Scale=scale,Bias=allowed&&!Full(spine.Amount)?-spine.Amount*scale:0,
                LastYaw=spine.Yaw,LastWorldYaw=(float)input.CharacterYaw};
        }
        if(allowed)
        {
            if(input.PendingUpdate || Full(spine.Amount))spine=spine with {Amount=1,Yaw=view.YawAngle};
            else
            {
                var amount=Damp(spine.Amount,1,input.Delta,.1f);
                spine=spine with {Amount=amount,Yaw=LerpAngle(spine.LastYaw,view.YawAngle,amount*spine.Scale+spine.Bias)};
            }
        }
        else if(input.PendingUpdate || !Relevant(spine.Amount))spine=spine with {Amount=0,Yaw=0};
        else
        {
            var multiplier=input.ViewYawSpeed>40 ? 40/input.ViewYawSpeed:1;
            var amount=Damp(spine.Amount,0,input.Delta,.7f*multiplier);
            var world=spine.LastWorldYaw;
            if(input.RelativeBaseRotation)world=Unwind((float)(world+input.BaseDeltaYaw));
            var offset=System.Math.Clamp(Unwind((float)(world-input.CharacterYaw)),-30,30);
            spine=spine with {Amount=amount,LastWorldYaw=Unwind((float)(offset+input.CharacterYaw)),
                Yaw=LerpAngle(0,spine.LastYaw+offset,amount*spine.Scale+spine.Bias)};
        }
        spine=spine with {FinalYaw=LerpAngle(0,spine.Yaw,viewAmount*aiming)};
        return (view,spine);
    }

    public static AlsRefactoredHeadState InitializeHead(in AlsRefactoredHeadState state)=>state with {InitializationRequired=true};

    public static AlsRefactoredHeadState RefreshHead(in AlsRefactoredViewInput input,
        in AlsRefactoredViewState view,in AlsRefactoredHeadState previous,in AlsRefactoredHeadSettings settings)
    {
        Validate(input);Finite([view.YawAngle,view.PitchAngle,previous.Pitch,previous.Yaw,previous.YawVelocity,previous.YawAmount]);
        ReadOnlySpan<float> times=[settings.PitchHalfLife,settings.YawHalfLife,settings.SwitchSidesHalfLife,
            settings.FirstPersonPitchHalfLife,settings.FirstPersonYawHalfLife];
        Finite(times);foreach(var time in times)if(time<0)throw new ArgumentException("Negative Head half life.");
        var head=previous;
        var yaw=head.Yaw;
        if(input.RelativeBaseRotation)yaw+=(float)input.BaseDeltaYaw;
        yaw=System.Math.Clamp(yaw-input.CharacterYawVelocity*input.Delta,-180,180);
        float targetPitch,targetYaw;var ready=false;
        if(input.RotationMode==AlsRotationMode.VelocityDirection)
        {
            targetPitch=0;
            targetYaw=Unwind((float)((input.HasInput?input.InputYaw:input.TargetYaw)-input.CharacterYaw));
            if(MathF.Abs(input.CharacterYawVelocity)>20)targetYaw=MathF.Sign(input.CharacterYawVelocity)*MathF.Abs(targetYaw);
            else if(MathF.Abs(targetYaw)>175)targetYaw=yaw;
            head=head with {SwitchingSides=false};
        }
        else
        {
            targetPitch=view.PitchAngle;targetYaw=System.Math.Clamp(view.YawAngle,-175,175);
            if(!input.FirstPerson)
            {
                if(yaw>=90 && targetYaw<=-160){targetYaw=175;ready=!input.HasInput;}
                else if(yaw<=-90 && targetYaw>=160){targetYaw=-175;ready=!input.HasInput;}
            }
        }
        if(head.InitializationRequired)
            head=head with {InitializationRequired=false,Pitch=targetPitch,Yaw=targetYaw,YawVelocity=0,SwitchingSides=false};
        else
        {
            var deltaPitch=Unwind(targetPitch-head.Pitch);
            var pitch=MathF.Abs(deltaPitch)<=.0001f ? targetPitch :
                Unwind(head.Pitch+Remap(deltaPitch)*AlsRefactoredRigMath.DamperAlpha(input.FirstPerson?input.RealDelta:input.Delta,
                    input.FirstPerson?settings.FirstPersonPitchHalfLife:settings.PitchHalfLife));
            var switching=ready || (head.SwitchingSides && MathF.Abs(Unwind(targetYaw-yaw))>10);
            var halfLife=switching?settings.SwitchSidesHalfLife:input.FirstPerson?settings.FirstPersonYawHalfLife:settings.YawHalfLife;
            var smoothing=halfLife/.69314718056f;var velocity=head.YawVelocity;
            if(smoothing<1e-8f){yaw=targetYaw;velocity=0;}
            else
            {
                var y=(4/MathF.Max(smoothing,1e-8f))/2;
                var j0=yaw-targetYaw;var j1=velocity+j0*y;var e=AlsRefactoredRigMath.InvExp(y*input.Delta);
                yaw=e*(j0+j1*input.Delta)+targetYaw;
                velocity=e*(velocity-j1*y*input.Delta);
            }
            head=head with {Pitch=pitch,Yaw=yaw,YawVelocity=velocity,SwitchingSides=switching};
        }
        return head with {YawAmount=head.Yaw/360+.5f};
    }

    private static float Damp(float from,float to,float delta,float halfLife)=>from+(to-from)*AlsRefactoredRigMath.DamperAlpha(delta,halfLife);
    private static bool Full(float weight)=>weight>=1-AlsPoseBlender.WeightThreshold;
    private static bool Relevant(float weight)=>weight>AlsPoseBlender.WeightThreshold;
    private static float Remap(float angle)=>angle>175?angle-360:angle;
    private static float LerpAngle(float from,float to,float alpha)=>Unwind(from+Remap(Unwind(to-from))*alpha);
    private static float Unwind(float angle)
    {
        if(!float.IsFinite(angle))throw new ArgumentException("Non-finite view angle.");
        angle%=360;return angle>180?angle-360:angle< -180?angle+360:angle;
    }
    private static void Finite(ReadOnlySpan<float> values)
    {foreach(var value in values)if(!float.IsFinite(value))throw new ArgumentException("Non-finite view state.");}
    private static void Validate(in AlsRefactoredViewInput input)
    {
        ReadOnlySpan<double> angles=[input.ViewYaw,input.ViewPitch,input.CharacterYaw,input.CharacterPitch,input.InputYaw,input.TargetYaw,input.BaseDeltaYaw];
        foreach(var angle in angles)if(!double.IsFinite(angle)||!float.IsFinite((float)angle))throw new ArgumentException("Invalid view rotation.");
        Finite([input.ViewYawSpeed,input.CharacterYawVelocity,input.Delta,input.RealDelta,input.ViewBlock,input.PoseAiming]);
        if(input.Delta<0||input.RealDelta<0||input.ViewYawSpeed<0||(uint)input.RotationMode>2)
            throw new ArgumentException("Invalid view update input.");
    }
}
