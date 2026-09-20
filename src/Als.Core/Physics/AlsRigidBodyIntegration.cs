using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

public readonly record struct AlsPredictedRigidBody(AlsPrecisePose MassPose,AlsProjectionVelocity Velocity);

// Isolated, force-free native particle integration. Actor rotation and velocity
// storage are float; COM position and solver rotation arithmetic are double.
// Collision, gravity, gyroscopic torque, sleeping and speed caps belong to the world.
public static class AlsRigidBodyIntegration
{
    public static AlsPredictedRigidBody Predict(in AlsPrecisePose actor,in AlsPrecisePose massLocal,
        AlsProjectionVelocity velocity,double linearDamping,double angularDamping,double dt)
    {
        if (!double.IsFinite(dt)||dt<=0||!double.IsFinite(linearDamping)||linearDamping<0||
            !double.IsFinite(angularDamping)||angularDamping<0)throw new ArgumentOutOfRangeException(nameof(dt));
        if (!new AlsDoubleVector(velocity.Linear).IsFinite||!new AlsDoubleVector(velocity.Angular).IsFinite)
            throw new ArgumentException("Prediction requires finite velocity.");
        if (actor.Scale!=AlsDoubleVector.One||massLocal.Scale!=AlsDoubleVector.One)
            throw new ArgumentException("Prediction requires rigid actor and mass transforms.");
        var v=new AlsDoubleVector(velocity.Linear)*System.Math.Max(0,1-linearDamping*dt);
        var w=new AlsDoubleVector(velocity.Angular)*System.Math.Max(0,1-angularDamping*dt);
        var mass=AlsPrecisePose.Compose(massLocal,actor);
        var dq=new AlsQuaternion(w.X,w.Y,w.Z,0)*mass.Rotation;
        mass=mass with {Position=mass.Position+v*dt,Rotation=(mass.Rotation+dq*(dt*.5)).Normalized()};
        // Native SetTransformPQCom stores the actor quaternion in float, then
        // Gather reconstructs COM from that particle. Preserve that boundary.
        var predicted=StoreActor(mass,massLocal);
        return new(AlsPrecisePose.Compose(massLocal,predicted),new(v.ToSingle(),w.ToSingle()));
    }

    public static AlsPrecisePose StoreActor(in AlsPrecisePose mass,in AlsPrecisePose massLocal)
    {
        var q=new AlsQuaternion((mass.Rotation*massLocal.Rotation.Conjugate()).ToSingle());
        return new(mass.Position-massLocal.Position.Rotate(q),q,AlsDoubleVector.One);
    }
}
