using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Actions;

public readonly record struct AlsMantlingWarpSettings(float Duration, float MontageStartTime, float MontageRate,
    float BlendInTime, AlsActionBlendOption BlendInOption, float WarpStart, float WarpEnd,
    AlsActionBlendOption LocationOption, AlsActionBlendOption RotationOption);

// Start/Target have unit scale, as the native source stores only location and rotation.
// Relative space is selected by the host's actual movement-base policy each frame.
public readonly record struct AlsMantlingWarpAnchors(AlsPrecisePose Start, AlsPrecisePose Target);

public readonly record struct AlsMantlingRootMotionStep(float Time, bool HasRootMotion, float MontageTime,
    float MontagePosition, float LocationWeight, float RotationWeight, AlsPrecisePose TargetActor,
    AlsDoubleVector Velocity, AlsQuaternion RotationDelta);

/// <summary>Pure native-space (centimeters) Mantling RootMotionSource math.
/// A host owns action identity, clock publication, scene/base lifetime and montage seeking.</summary>
public static class AlsMantlingRootMotion
{
    public static AlsMantlingWarpAnchors CreateAnchors(in AlsPrecisePose actor,
        in AlsPrecisePose target, in AlsQuaternion meshRotation, in AlsPrecisePose startRoot,
        in AlsPrecisePose lastRoot, bool relative, in AlsPrecisePose targetBase)
    {
        actor.Validate(); target.Validate(); startRoot.Validate(); lastRoot.Validate();
        var mesh = new AlsPrecisePose(default,meshRotation,AlsDoubleVector.One);mesh.Validate();
        var start = AlsPrecisePose.Compose(Reverse(UnitScale(startRoot),mesh),actor);
        var worldTarget = UnitScale(target);
        if (relative)
        {
            targetBase.Validate();
            start = AlsPrecisePose.Relative(start,targetBase);
            worldTarget = UnitScale(AlsPrecisePose.Compose(worldTarget,targetBase));
        }
        var end = AlsPrecisePose.Compose(Reverse(UnitScale(lastRoot),mesh),worldTarget);
        if (relative) end = AlsPrecisePose.Relative(end,targetBase);
        return new(UnitScale(start),UnitScale(end));
    }

    public static AlsMantlingRootMotionStep Prepare(float time, float simulationDelta, float delta,
        in AlsMantlingWarpSettings settings, in AlsMantlingWarpAnchors anchors,
        in AlsPrecisePose actor, in AlsQuaternion meshRotation, in AlsDoubleVector gravityUp,
        bool targetExists, bool relative, in AlsPrecisePose targetBase,
        AlsMontageRootTransformSampler rootSampler)
    {
        Nonnegative(time);Nonnegative(simulationDelta);Nonnegative(delta);
        var nextTime = time + simulationDelta;
        if (!float.IsFinite(nextTime)) throw new ArgumentException("Mantle clock overflow.");
        if (!float.IsFinite(settings.Duration)) throw new ArgumentException("Nonfinite mantle duration.");
        // Native advances its clock even when the source cannot emit motion.
        if (settings.Duration <= 1e-8f || delta <= 1e-8f || !targetExists)
            return new(nextTime,false,0,0,0,0,AlsPrecisePose.Identity,default,AlsQuaternion.Identity);
        ArgumentNullException.ThrowIfNull(rootSampler);
        Nonnegative(settings.MontageStartTime);Nonnegative(settings.BlendInTime);
        if (!float.IsFinite(settings.MontageRate) || settings.MontageRate <= 0 ||
            !float.IsFinite(settings.WarpStart) || !float.IsFinite(settings.WarpEnd) || settings.WarpEnd <= settings.WarpStart)
            throw new ArgumentException("Invalid mantle timing.");
        actor.Validate();anchors.Start.Validate();anchors.Target.Validate();
        if (!gravityUp.IsFinite || System.Math.Abs(gravityUp.LengthSquared-1)>1e-6)
            throw new ArgumentException("Mantle gravity axis must be unit length.");
        var mesh = new AlsPrecisePose(default,meshRotation,AlsDoubleVector.One);mesh.Validate();
        var montageTime = settings.MontageStartTime + nextTime * settings.MontageRate;
        if (!float.IsFinite(montageTime)) throw new ArgumentException("Mantle montage time overflow.");
        var blend = settings.BlendInTime > 1e-8f ? Shape(nextTime/settings.BlendInTime,settings.BlendInOption) : 1;
        var amount = System.Math.Clamp((montageTime-settings.WarpStart)/(settings.WarpEnd-settings.WarpStart),0,1);
        var locationWeight = blend * Shape(amount,settings.LocationOption);
        var rotationWeight = blend * Shape(amount,settings.RotationOption);
        var start=UnitScale(anchors.Start);var target=UnitScale(anchors.Target);
        if (relative)
        {
            targetBase.Validate();
            start=UnitScale(AlsPrecisePose.Compose(start,targetBase));
            target=UnitScale(AlsPrecisePose.Compose(target,targetBase));
        }
        var position=start.Position+(target.Position-start.Position)*locationWeight;
        var bias=AlsQuaternion.Dot(start.Rotation,target.Rotation)>=0 ? 1 : -1;
        var rotation=(start.Rotation*(bias*(1.0-rotationWeight))+target.Rotation*rotationWeight).Normalized();
        var projection=gravityUp*AlsDoubleVector.Dot(gravityUp,new(rotation.X,rotation.Y,rotation.Z));
        rotation=new AlsQuaternion(projection.X,projection.Y,projection.Z,rotation.W).Normalized();
        var root=Reverse(mesh,UnitScale(rootSampler.Sample(montageTime)));
        var targetActor=AlsPrecisePose.Compose(root,new(position,rotation,AlsDoubleVector.One));
        var velocity=(targetActor.Position-actor.Position)*(1.0/delta);
        var rotationDelta=targetActor.Rotation*actor.Rotation.Conjugate();
        if (!velocity.IsFinite) throw new ArgumentException("Mantle velocity overflow.");
        return new(nextTime,true,montageTime,MathF.Max(0,montageTime-delta),locationWeight,rotationWeight,
            targetActor,velocity,rotationDelta);
    }

    // FTransform::GetRelativeTransformReverse; operands here have unit scale.
    // Unlike Relative(other,self), rotation multiplication is in reverse order.
    private static AlsPrecisePose Reverse(in AlsPrecisePose self,in AlsPrecisePose other)
    {
        var rotation=other.Rotation*self.Rotation.Conjugate();
        return new(other.Position-self.Position.Rotate(rotation),rotation,AlsDoubleVector.One);
    }
    private static AlsPrecisePose UnitScale(in AlsPrecisePose pose)=>pose with {Scale=AlsDoubleVector.One};
    private static float Shape(float alpha,AlsActionBlendOption option)
    {
        if (!float.IsFinite(alpha)) throw new ArgumentException("Nonfinite mantle blend input.");
        var value=option switch
        {
            AlsActionBlendOption.Linear=>alpha,
            AlsActionBlendOption.Cubic=>(-2f*(alpha*alpha*alpha))+(3f*(alpha*alpha)),
            AlsActionBlendOption.HermiteCubic=>alpha<=0 ? 0 : alpha>=1 ? 1 : (alpha*alpha)*(3f-2f*alpha),
            _=>throw new ArgumentException("Unsupported mantle blend option.")
        };
        return System.Math.Clamp(value,0,1);
    }
    private static void Nonnegative(float value)
    { if(!float.IsFinite(value)||value<0) throw new ArgumentException("Expected finite nonnegative mantle time."); }
}
