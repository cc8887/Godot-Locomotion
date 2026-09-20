using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

public readonly record struct AlsJointBodyInput(AlsPrecisePose Initial,AlsPrecisePose Predicted,
    AlsPrecisePose Connector,AlsJointInverseMass InverseMass);

// ALS joint contract: three locked linear axes, swing/twist angular rows,
// bSolvePositionLast=true, unit iteration stiffness and zero restitution.
public struct AlsCachedJoint
{
    private AlsCachedLinearJoint _linear;
    private AlsCachedAngularJoint _angular;

    public AlsCachedJoint(in AlsJointBodyInput parent,in AlsJointBodyInput child,
        in AlsAngularJointSettings settings,double dt)
    {
        _linear=new(parent.Predicted,child.Predicted,parent.Connector,child.Connector,
            parent.InverseMass,child.InverseMass,settings.ConditionMass,settings.UseSimd,
            settings.HardStiffness,settings.MinParentMassRatio,settings.MaxInertiaRatio);
        _angular=new(new(parent.Initial.Rotation,parent.Predicted.Rotation,parent.Connector.Rotation,parent.InverseMass),
            new(child.Initial.Rotation,child.Predicted.Rotation,child.Connector.Rotation,child.InverseMass),settings,dt);
    }

    public void SolvePosition(ref AlsProjectionDelta parent,ref AlsProjectionDelta child)
    {
        var p=parent.Rotation;var c=child.Rotation;
        _angular.SolveLimits(ref p,ref c);parent=parent with {Rotation=p};child=child with {Rotation=c};
        _linear.SolvePositions(ref parent,ref child);
        p=parent.Rotation;c=child.Rotation;
        _angular.SolveDrives(ref p,ref c);parent=parent with {Rotation=p};child=child with {Rotation=c};
    }

    public void SolveVelocity(ref AlsProjectionVelocity parent,ref AlsProjectionVelocity child)
    {
        var p=parent.Angular;var c=child.Angular;
        _angular.SolveVelocities(ref p,ref c);parent=parent with {Angular=p};child=child with {Angular=c};
        _linear.SolveVelocities(ref parent,ref child);
    }

    public static AlsProjectionVelocity AddImplicitVelocity(AlsProjectionVelocity velocity,
        AlsProjectionDelta delta,double dt,bool dynamic)
    {
        if (!double.IsFinite(dt)||dt<=0)throw new ArgumentOutOfRangeException(nameof(dt));
        if (!dynamic)return velocity;
        var inverse=1/(float)dt;
        if (!float.IsFinite(inverse))throw new ArgumentOutOfRangeException(nameof(dt));
        return new(velocity.Linear+delta.Position*inverse,velocity.Angular+delta.Rotation*inverse);
    }
}
