using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Actions;

public readonly record struct AlsMantlingWarpSettings(float Duration, float MontageStartTime, float MontageRate,
    float BlendInTime, AlsActionBlendOption BlendInOption, float WarpStart, float WarpEnd,
    AlsActionBlendOption LocationOption, AlsActionBlendOption RotationOption)
{
    public float? ProgressLiftStartTime {get;init;}
    public float? ProgressTargetHeight { get; init; }
    public float? ProgressPreparationEndTime { get; init; }
}

// Start/Target have unit scale, as the native source stores only location and rotation.
// Relative space is selected by the host's actual movement-base policy each frame.
public readonly record struct AlsMantlingWarpAnchors(AlsPrecisePose Start, AlsPrecisePose Target)
{
    public bool HasActorEndpoints { get; init; }
    public AlsPrecisePose ActorStart { get; init; }
    public AlsPrecisePose ActorTarget { get; init; }
}

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
        return new(UnitScale(start),UnitScale(end))
        {
            HasActorEndpoints=true,
            ActorStart=relative ? UnitScale(AlsPrecisePose.Relative(actor,targetBase)) : UnitScale(actor),
            ActorTarget=relative ? UnitScale(AlsPrecisePose.Relative(worldTarget,targetBase)) : worldTarget
        };
    }

    public static AlsMantlingRootMotionStep Prepare(float time, float simulationDelta, float delta,
        in AlsMantlingWarpSettings settings, in AlsMantlingWarpAnchors anchors,
        in AlsPrecisePose actor, in AlsQuaternion meshRotation, in AlsDoubleVector gravityUp,
        bool targetExists, bool relative, in AlsPrecisePose targetBase,
        AlsMontageRootTransformSampler rootSampler, bool useRootMotion = true, AlsMantlingProgressCurve? progress = null)
    {
        Nonnegative(time);Nonnegative(simulationDelta);Nonnegative(delta);
        var nextTime = time + simulationDelta;
        if (!float.IsFinite(nextTime)) throw new ArgumentException("Mantle clock overflow.");
        if (!float.IsFinite(settings.Duration)) throw new ArgumentException("Nonfinite mantle duration.");
        // Native advances its clock even when the source cannot emit motion.
        if (settings.Duration <= 1e-8f || delta <= 1e-8f || !targetExists)
            return new(nextTime,false,0,0,0,0,AlsPrecisePose.Identity,default,AlsQuaternion.Identity);
        if (useRootMotion) ArgumentNullException.ThrowIfNull(rootSampler);
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
        var displacement=target.Position-start.Position;
        var position=start.Position+displacement*locationWeight;
        if(!useRootMotion && progress is not null)
        {
            var sample=progress.Sample(montageTime,settings.MontageStartTime,
                settings.ProgressLiftStartTime ?? settings.MontageStartTime,
                settings.ProgressTargetHeight, settings.ProgressPreparationEndTime);
            locationWeight=blend*sample.Planar;var liftWeight=blend*sample.Lift;
            var vertical=gravityUp*AlsDoubleVector.Dot(gravityUp,displacement);
            position=start.Position+(displacement-vertical)*locationWeight+vertical*liftWeight;
        }
        var bias=AlsQuaternion.Dot(start.Rotation,target.Rotation)>=0 ? 1 : -1;
        var rotation=(start.Rotation*(bias*(1.0-rotationWeight))+target.Rotation*rotationWeight).Normalized();
        var projection=gravityUp*AlsDoubleVector.Dot(gravityUp,new(rotation.X,rotation.Y,rotation.Z));
        rotation=new AlsQuaternion(projection.X,projection.Y,projection.Z,rotation.W).Normalized();
        // Anchors are in the mesh basis even when authored root sampling is
        // disabled. Always cancel that basis before emitting an actor pose.
        var root=Reverse(mesh,useRootMotion ? UnitScale(rootSampler.Sample(montageTime)) : AlsPrecisePose.Identity);
        var targetActor=AlsPrecisePose.Compose(root,new(position,rotation,AlsDoubleVector.One));
        if (useRootMotion && progress is not null)
        {
            // Contact progress describes actor travel, whereas native anchors
            // have the authored root removed. Use the retained actor endpoints
            // when applying clearance/lift; applying progress directly to the
            // native anchor displacement would consume the authored travel twice.
            if (!anchors.HasActorEndpoints) throw new ArgumentException("Contact progress requires explicit actor endpoints.");
            var firstActor=anchors.ActorStart;var lastActor=anchors.ActorTarget;
            firstActor.Validate();lastActor.Validate();
            if(relative)
            {
                firstActor=UnitScale(AlsPrecisePose.Compose(firstActor,targetBase));
                lastActor=UnitScale(AlsPrecisePose.Compose(lastActor,targetBase));
            }
            var travel=lastActor.Position-firstActor.Position;
            var vertical=gravityUp*AlsDoubleVector.Dot(gravityUp,travel);
            var sample=progress.Sample(montageTime,settings.MontageStartTime,
                settings.ProgressLiftStartTime ?? settings.MontageStartTime,
                settings.ProgressTargetHeight,settings.ProgressPreparationEndTime);
            locationWeight=blend*sample.Planar;
            targetActor=targetActor with {Position=firstActor.Position+(travel-vertical)*locationWeight+vertical*(blend*sample.Lift)};
        }
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
