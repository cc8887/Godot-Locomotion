using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Actions;

public static class AlsRootMotionKinematics
{
    // USkeletalMeshComponent::ConvertLocalRootMotionToWorld. Include the actor's
    // offset from the mesh pivot: rotating an offset mesh also moves the actor.
    public static AlsRootMotionDelta ToWorld(in AlsRootMotionDelta local,
        in AlsPrecisePose componentToCharacter, in AlsPrecisePose characterToWorld)
    {
        var componentWorld = AlsPrecisePose.Compose(componentToCharacter, characterToWorld);
        var actorInComponent = AlsPrecisePose.Relative(characterToWorld, componentWorld);
        var motion = new AlsPrecisePose(new(local.Translation), new(local.Rotation), AlsDoubleVector.One);
        var newComponent = AlsPrecisePose.Compose(motion, componentWorld);
        var newActor = AlsPrecisePose.Compose(actorInComponent, newComponent);
        var rotation = (componentWorld.Rotation * motion.Rotation * componentWorld.Rotation.Conjugate()).Normalized();
        return new((newActor.Position - characterToWorld.Position).ToSingle(), rotation.ToSingle());
    }
}
