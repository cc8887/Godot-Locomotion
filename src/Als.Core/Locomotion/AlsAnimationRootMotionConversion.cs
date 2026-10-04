namespace GodotAls.Core.Locomotion;

// SkeletalMeshComponent converts the complete mesh/actor relation, including
// the lever arm of a root rotation. This is distinct from a pose attribute.
public static class AlsAnimationRootMotionConversion
{
    public static AlsPrecisePose ToWorld(in AlsPrecisePose local,in AlsPrecisePose actor,in AlsPrecisePose component)
    {
        local.Validate();actor.Validate();component.Validate();
        var componentToActor=AlsPrecisePose.Relative(actor,component);
        var newComponent=AlsPrecisePose.Compose(local,component);
        var newActor=AlsPrecisePose.Compose(componentToActor,newComponent);
        var rotation=component.Rotation*local.Rotation*component.Rotation.Conjugate();
        return new(newActor.Position-actor.Position,rotation,AlsDoubleVector.One);
    }
    public static AlsDoubleVector Velocity(in AlsPrecisePose world,float delta,in AlsDoubleVector current,bool falling)
    {
        world.Validate();if(!float.IsFinite(delta)||delta<0||!current.IsFinite)throw new ArgumentException("Invalid animation root movement inputs.");
        var velocity=delta>0?world.Position*(1d/delta):current;
        return falling?new(velocity.X,velocity.Y,current.Z):velocity;
    }
}
