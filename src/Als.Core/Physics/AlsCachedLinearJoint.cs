using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// Three hard locked linear axes, as used by all constrained ALS bodies.
// Cached at the predicted COM pose; position and velocity phases share the same
// arms, local conditioned mass, axes and accumulated position impulses.
public struct AlsCachedLinearJoint
{
    private struct Row
    {
        public AlsDoubleVector Axis, ParentResponse, ChildResponse;
        public double Error, InverseMass, Lambda;
    }
    private Row _x,_y,_z;
    private readonly AlsDoubleVector _parentArm,_childArm;
    private readonly double _parentMass,_childMass,_stiffness;
    private readonly bool _simultaneous;
    public AlsDoubleVector Lambda=>new(_x.Lambda,_y.Lambda,_z.Lambda);

    public AlsCachedLinearJoint(in AlsPrecisePose parent,in AlsPrecisePose child,
        in AlsPrecisePose parentFrame,in AlsPrecisePose childFrame,
        AlsJointInverseMass parentMass,AlsJointInverseMass childMass,bool conditionMass=true,
        bool simultaneous=true,double stiffness=1,double minParentMassRatio=.2f,double maxInertiaRatio=5)
    {
        this=default;
        parent.Validate(1e-6);child.Validate(1e-6);parentFrame.Validate(1e-6);childFrame.Validate(1e-6);
        if (parent.Scale!=AlsDoubleVector.One||child.Scale!=AlsDoubleVector.One||
            parentFrame.Scale!=AlsDoubleVector.One||childFrame.Scale!=AlsDoubleVector.One)
            throw new ArgumentException("Locked linear rows require rigid COM/connector frames.");
        if (!double.IsFinite(stiffness)||stiffness is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(stiffness));
        var masses=AlsJointMassConditioning.Apply(parentMass,childMass,conditionMass?minParentMassRatio:0,conditionMass?maxInertiaRatio:0);
        _parentMass=masses.Parent.Mass;_childMass=masses.Child.Mass;_stiffness=stiffness;_simultaneous=simultaneous;
        var pi=AlsJointInertiaTensor.World(parent.Rotation,masses.Parent);var ci=AlsJointInertiaTensor.World(child.Rotation,masses.Child);
        var p=AlsPrecisePose.Compose(parentFrame,parent);var c=AlsPrecisePose.Compose(childFrame,child);
        var ax=new AlsDoubleVector(1,0,0).Rotate(p.Rotation);var ay=new AlsDoubleVector(0,1,0).Rotate(p.Rotation);var az=new AlsDoubleVector(0,0,1).Rotate(p.Rotation);
        var dx=c.Position-p.Position;
        var ex=AlsDoubleVector.Dot(dx,ax);var ey=AlsDoubleVector.Dot(dx,ay);var ez=AlsDoubleVector.Dot(dx,az);
        // Preserve the native projection arithmetic rather than replacing it by
        // parent connector minus COM (algebraically equal for all locked axes).
        _parentArm=c.Position-parent.Position-ax*ex-ay*ey-az*ez;
        _childArm=c.Position-child.Position;
        _x=Make(ax,ex,pi,ci);_y=Make(ay,ey,pi,ci);_z=Make(az,ez,pi,ci);
    }

    public void SolvePositions(ref AlsProjectionDelta p,ref AlsProjectionDelta c)
    {
        if (_simultaneous)
        {
            var cx=(c.Position-p.Position)+(Cross(c.Rotation,_childArm.ToSingle())-Cross(p.Rotation,_parentArm.ToSingle()));
            var dp=default(AlsProjectionDelta);var dc=default(AlsProjectionDelta);
            PositionSingle(ref _x,cx,ref dp,ref dc);PositionSingle(ref _y,cx,ref dp,ref dc);PositionSingle(ref _z,cx,ref dp,ref dc);
            // The reference Win64 kernel factors parent mass after summing all
            // three impulses. Child position uses three scaled subtractions.
            p=Add(p,new(dp.Position*(float)_parentMass,dp.Rotation));c=Add(c,dc);
        }
        else {PositionDouble(ref _x,ref p,ref c);PositionDouble(ref _y,ref p,ref c);PositionDouble(ref _z,ref p,ref c);}
    }

    public readonly void SolveVelocities(ref AlsProjectionVelocity p,ref AlsProjectionVelocity c)
    {
        if (_simultaneous)
        {
            // Native SIMD tests ANY accumulated row, then solves all three.
            if (System.Math.Abs(_x.Lambda)<=1e-8f&&System.Math.Abs(_y.Lambda)<=1e-8f&&System.Math.Abs(_z.Lambda)<=1e-8f) return;
            var cv=(c.Linear+Cross(c.Angular,_childArm.ToSingle()))-(p.Linear+Cross(p.Angular,_parentArm.ToSingle()));
            var dp=default(AlsProjectionDelta);var dc=default(AlsProjectionDelta);
            VelocitySingle(_x,cv,ref dp,ref dc);VelocitySingle(_y,cv,ref dp,ref dc);VelocitySingle(_z,cv,ref dp,ref dc);
            // Both velocity responses sum first, then scale; unlike child DP.
            p=new(p.Linear+dp.Position*(float)_parentMass,p.Angular+dp.Rotation);
            c=new(c.Linear+dc.Position*-(float)_childMass,c.Angular+dc.Rotation);
        }
        else {VelocityDouble(_x,ref p,ref c);VelocityDouble(_y,ref p,ref c);VelocityDouble(_z,ref p,ref c);}
    }

    private readonly Row Make(AlsDoubleVector axis,double error,AlsJointInertiaTensor pi,AlsJointInertiaTensor ci)
    {
        if (error<0) {axis*= -1;error=-error;}
        AlsDoubleVector a0,a1;
        if (_simultaneous)
        {
            axis=new(axis.ToSingle());error=(float)error;
            a0=new(Cross(_parentArm.ToSingle(),axis.ToSingle()));a1=new(Cross(_childArm.ToSingle(),axis.ToSingle()));
        }
        else {a0=AlsDoubleVector.Cross(_parentArm,axis);a1=AlsDoubleVector.Cross(_childArm,axis);}
        var pr=pi.Multiply(a0,_simultaneous);var cr=ci.Multiply(a1,_simultaneous)*-1;
        var im=_simultaneous?(float)(_parentMass+_childMass)+(Vector3.Dot(a0.ToSingle(),pr.ToSingle())-Vector3.Dot(a1.ToSingle(),cr.ToSingle()))
            : _parentMass+_childMass+AlsDoubleVector.Dot(a0,pr)-AlsDoubleVector.Dot(a1,cr);
        if (!double.IsFinite(im)||!pr.IsFinite||!cr.IsFinite) throw new ArgumentOutOfRangeException(nameof(pi));
        return new(){Axis=axis,Error=error,ParentResponse=pr,ChildResponse=cr,InverseMass=im};
    }
    private readonly void PositionDouble(ref Row r,ref AlsProjectionDelta p,ref AlsProjectionDelta c)
    {
        if (r.InverseMass<=0) return;
        var cx=new AlsDoubleVector(c.Position-p.Position)+AlsDoubleVector.Cross(new(c.Rotation),_childArm)-AlsDoubleVector.Cross(new(p.Rotation),_parentArm);
        var lambda=_stiffness*(r.Error+AlsDoubleVector.Dot(cx,r.Axis))/r.InverseMass;r.Lambda+=lambda;
        ApplyDouble(r,lambda,ref p,ref c);
    }
    private readonly void PositionSingle(ref Row r,Vector3 cx,ref AlsProjectionDelta p,ref AlsProjectionDelta c)
    {
        if (r.InverseMass<=0) return;
        var lambda=(float)_stiffness*((float)r.Error+Vector3.Dot(cx,r.Axis.ToSingle()))/(float)r.InverseMass;
        r.Lambda=(float)r.Lambda+lambda;ApplySingle(r,lambda,ref p,ref c);
    }
    private readonly void VelocityDouble(in Row r,ref AlsProjectionVelocity p,ref AlsProjectionVelocity c)
    {
        if (r.InverseMass<=0||System.Math.Abs(r.Lambda)<=1e-8f) return;
        var cv0=new AlsDoubleVector(p.Linear)+AlsDoubleVector.Cross(new(p.Angular),_parentArm);
        var cv1=new AlsDoubleVector(c.Linear)+AlsDoubleVector.Cross(new(c.Angular),_childArm);
        var lambda=_stiffness*AlsDoubleVector.Dot(cv1-cv0,r.Axis)/r.InverseMass;
        var dp=new AlsProjectionDelta(p.Linear,p.Angular);var dc=new AlsProjectionDelta(c.Linear,c.Angular);
        ApplyDouble(r,lambda,ref dp,ref dc);p=new(dp.Position,dp.Rotation);c=new(dc.Position,dc.Rotation);
    }
    private readonly void VelocitySingle(in Row r,Vector3 cv,ref AlsProjectionDelta p,ref AlsProjectionDelta c)
    {
        if (r.InverseMass<=0) return;
        var lambda=(float)_stiffness*Vector3.Dot(cv,r.Axis.ToSingle())/(float)r.InverseMass;
        ApplySingle(r,lambda,ref p,ref c,velocity:true);
    }
    private readonly void ApplyDouble(in Row r,double lambda,ref AlsProjectionDelta p,ref AlsProjectionDelta c)
    {
        var impulse=r.Axis*lambda;
        p=new(p.Position+(impulse*_parentMass).ToSingle(),p.Rotation+(r.ParentResponse*lambda).ToSingle());
        c=new(c.Position+(impulse*-_childMass).ToSingle(),c.Rotation+(r.ChildResponse*lambda).ToSingle());
    }
    private readonly void ApplySingle(in Row r,float lambda,ref AlsProjectionDelta p,ref AlsProjectionDelta c,bool velocity=false)
    {
        var impulse=r.Axis.ToSingle()*lambda;
        p=new(p.Position+impulse,p.Rotation+r.ParentResponse.ToSingle()*lambda);
        c=new(velocity?c.Position+impulse:c.Position-impulse*(float)_childMass,c.Rotation+r.ChildResponse.ToSingle()*lambda);
    }
    private static AlsProjectionDelta Add(AlsProjectionDelta a,AlsProjectionDelta b)=>new(a.Position+b.Position,a.Rotation+b.Rotation);
    // Match the reference engine's separate float products/subtraction on both
    // .NET 8 and the .NET 9 Godot host (Vector3.Cross may fuse its operations).
    private static Vector3 Cross(Vector3 a,Vector3 b)=>
        new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
}
