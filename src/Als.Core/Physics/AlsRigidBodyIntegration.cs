using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

public readonly record struct AlsPredictedRigidBody(AlsPrecisePose MassPose,AlsProjectionVelocity Velocity,AlsPrecisePose ActorPose);
// Explicit per-step inputs, already converted from force/torque to world-space
// acceleration and impulse velocity. Units: cm/s², rad/s², cm/s and rad/s.
// The caller resubmits continuous acceleration each step and impulses only once.
public readonly record struct AlsBodyStepForces(AlsDoubleVector Acceleration,
    AlsDoubleVector AngularAcceleration = default, AlsDoubleVector LinearImpulseVelocity = default,
    AlsDoubleVector AngularImpulseVelocity = default)
{
    public void Validate()
    {
        if (!Acceleration.IsFinite || !AngularAcceleration.IsFinite ||
            !LinearImpulseVelocity.IsFinite || !AngularImpulseVelocity.IsFinite)
            throw new ArgumentException("Step forces must be finite.");
    }
}

// Native particle integration. Actor rotation and velocity
// storage are float; COM position and solver rotation arithmetic are double.
// The world resolves gravity into acceleration. Gyroscopic torque, sleeping and
// speed caps are not implemented here.
public static class AlsRigidBodyIntegration
{
    public static AlsPredictedRigidBody Predict(in AlsPrecisePose actor,in AlsPrecisePose massLocal,
        AlsProjectionVelocity velocity,double linearDamping,double angularDamping,double dt)
        => Predict(actor, massLocal, velocity, linearDamping, angularDamping, dt, default);

    public static AlsPredictedRigidBody Predict(in AlsPrecisePose actor,in AlsPrecisePose massLocal,
        AlsProjectionVelocity velocity,double linearDamping,double angularDamping,double dt,
        in AlsBodyStepForces forces, bool dragBeforeIntegration = false)
    {
        if (!double.IsFinite(dt)||dt<=0||!double.IsFinite(linearDamping)||linearDamping<0||
            !double.IsFinite(angularDamping)||angularDamping<0)throw new ArgumentOutOfRangeException(nameof(dt));
        if (!new AlsDoubleVector(velocity.Linear).IsFinite||!new AlsDoubleVector(velocity.Angular).IsFinite)
            throw new ArgumentException("Prediction requires finite velocity.");
        if (actor.Scale!=AlsDoubleVector.One||massLocal.Scale!=AlsDoubleVector.One)
            throw new ArgumentException("Prediction requires rigid actor and mass transforms.");
        forces.Validate();
        var v=new AlsDoubleVector(velocity.Linear);
        var w=new AlsDoubleVector(velocity.Angular);
        var linearMultiplier=System.Math.Max(0,1-linearDamping*dt);
        var angularMultiplier=System.Math.Max(0,1-angularDamping*dt);
        if (dragBeforeIntegration) { v*=linearMultiplier; w*=angularMultiplier; }
        v+=forces.Acceleration*dt; w+=forces.AngularAcceleration*dt;
        v+=forces.LinearImpulseVelocity; w+=forces.AngularImpulseVelocity;
        if (!dragBeforeIntegration) { v*=linearMultiplier; w*=angularMultiplier; }
        if (!v.IsFinite || !w.IsFinite || !new AlsDoubleVector(v.ToSingle()).IsFinite || !new AlsDoubleVector(w.ToSingle()).IsFinite)
            throw new ArgumentException("Step forces overflowed particle velocity storage.");
        var mass=AlsPrecisePose.Compose(massLocal,actor);
        var dq=new AlsQuaternion(w.X,w.Y,w.Z,0)*mass.Rotation;
        mass=mass with {Position=mass.Position+v*dt,Rotation=(mass.Rotation+dq*(dt*.5)).Normalized()};
        // Native SetTransformPQCom stores the actor quaternion in float, then
        // Gather reconstructs COM from that particle. Preserve that boundary.
        var predicted=StoreActor(mass,massLocal);
        return new(AlsPrecisePose.Compose(massLocal,predicted),new(v.ToSingle(),w.ToSingle()),predicted);
    }

    public static AlsPrecisePose StoreActor(in AlsPrecisePose mass,in AlsPrecisePose massLocal)
    {
        var q=new AlsQuaternion((mass.Rotation*massLocal.Rotation.Conjugate()).ToSingle());
        return new(mass.Position-massLocal.Position.Rotate(q),q,AlsDoubleVector.One);
    }
}
