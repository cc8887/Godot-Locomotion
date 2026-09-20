using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

public readonly record struct AlsProjectionDelta(Vector3 Position,Vector3 Rotation);
public readonly record struct AlsProjectionVelocity(Vector3 Linear,Vector3 Angular);

// Cached Chaos projection for three locked linear axes and no angular
// projection. Inputs use consistent units; SIMD row arithmetic is single precision.
// Cache every joint before applying any joint's correction to shared bodies.
public readonly struct AlsLockedLinearProjection
{
    public const float ReferenceVelocityAlpha=.1f;
    private readonly record struct Row(Vector3 Axis,Vector3 RotationAxis,float Error,float InverseMass);
    private readonly Row _x,_y,_z;
    private readonly Vector3 _parentArm,_childArm;
    private readonly float _inverseMass,_alpha,_stiffness,_teleport;
    private readonly bool _enabled;

    public AlsLockedLinearProjection(in AlsPrecisePose parent,in AlsPrecisePose child,
        in AlsPrecisePose parentFrame,in AlsPrecisePose childFrame,float childInverseMass,
        Vector3 childInverseInertia,float stiffness,float alpha,float teleportDistance,bool enabled=true)
    {
        ReadOnlySpan<float> values=stackalloc float[]{childInverseMass,childInverseInertia.X,childInverseInertia.Y,
            childInverseInertia.Z,stiffness,alpha,teleportDistance};
        foreach(var value in values)
            if(!float.IsFinite(value)||value<0)throw new ArgumentOutOfRangeException(nameof(childInverseMass));
        if(alpha>1||stiffness>1)throw new ArgumentOutOfRangeException(nameof(alpha));
        if(parent.Scale!=AlsDoubleVector.One||child.Scale!=AlsDoubleVector.One||
           parentFrame.Scale!=AlsDoubleVector.One||childFrame.Scale!=AlsDoubleVector.One)
            throw new ArgumentException("Projection requires rigid mass and connector frames.");
        var p=AlsPrecisePose.Compose(parentFrame,parent);var c=AlsPrecisePose.Compose(childFrame,child);
        var dx=c.Position-p.Position;var arm0=c.Position-parent.Position;
        var ax=new AlsDoubleVector(1,0,0).Rotate(p.Rotation);
        var ay=new AlsDoubleVector(0,1,0).Rotate(p.Rotation);
        var az=new AlsDoubleVector(0,0,1).Rotate(p.Rotation);
        var ex=AlsDoubleVector.Dot(dx,ax);var ey=AlsDoubleVector.Dot(dx,ay);var ez=AlsDoubleVector.Dot(dx,az);
        arm0-=ax*ex;arm0-=ay*ey;arm0-=az*ez;
        _parentArm=arm0.ToSingle();_childArm=(c.Position-child.Position).ToSingle();
        // InitProjection deliberately replaces anisotropic inertia by a sphere.
        var inverseI=MathF.Min(childInverseInertia.X,MathF.Min(childInverseInertia.Y,childInverseInertia.Z));
        _x=MakeRow(ax,ex,_childArm,childInverseMass,inverseI);
        _y=MakeRow(ay,ey,_childArm,childInverseMass,inverseI);
        _z=MakeRow(az,ez,_childArm,childInverseMass,inverseI);
        _inverseMass=childInverseMass;_alpha=alpha;_stiffness=stiffness;_teleport=teleportDistance;_enabled=enabled;
    }

    public AlsProjectionVelocity Apply(in AlsProjectionDelta parent,ref AlsProjectionDelta child,double dt,
        float velocityAlpha,bool firstIteration=true,bool lastIteration=true)
    {
        if(!double.IsFinite(dt)||dt<=0||!float.IsFinite(velocityAlpha)||velocityAlpha<0)
            throw new ArgumentOutOfRangeException(nameof(dt));
        // InitProjection makes the parent immovable. The container then skips
        // the entire row, including teleports, when the child is also static.
        if(!_enabled||_inverseMass<=0)return default;
        if(firstIteration&&_teleport>0)
        {
            var dp=Teleport(_x,_teleport)+Teleport(_y,_teleport)+Teleport(_z,_teleport);
            child=child with {Position=child.Position+dp};
        }
        if(_alpha>0)
        {
            var cx=child.Position-parent.Position+Vector3.Cross(child.Rotation,_childArm)-Vector3.Cross(parent.Rotation,_parentArm);
            var dp=Vector3.Zero;var dq=Vector3.Zero;
            Solve(_x,cx,ref dp,ref dq);Solve(_y,cx,ref dp,ref dq);Solve(_z,cx,ref dp,ref dq);
            child=new(child.Position+dp,child.Rotation+dq);
        }
        var scale=lastIteration?velocityAlpha/(float)dt:0;
        return new(child.Position*scale,child.Rotation*scale);
    }

    public static AlsPrecisePose Correct(in AlsPrecisePose initial,in AlsProjectionDelta delta)
    {
        var dq=new AlsQuaternion(delta.Rotation.X,delta.Rotation.Y,delta.Rotation.Z,0)*initial.Rotation;
        return initial with {Position=initial.Position+new AlsDoubleVector(delta.Position),
            Rotation=delta.Rotation==Vector3.Zero?initial.Rotation:(initial.Rotation+dq*.5).Normalized()};
    }
    private static Row MakeRow(AlsDoubleVector axis,double error,Vector3 arm,float inverseMass,float inverseI)
    {
        var a=(axis*(error<0?-1:1)).ToSingle();var angular=Vector3.Cross(arm,a);var rotationAxis=-angular*inverseI;
        return new(a,rotationAxis,(float)System.Math.Abs(error),inverseMass-Vector3.Dot(angular,rotationAxis));
    }
    private static Vector3 Teleport(in Row row,float threshold)=>row.Error>threshold?row.Axis*(-row.Error):Vector3.Zero;
    private void Solve(in Row row,Vector3 cx,ref Vector3 dp,ref Vector3 dq)
    {
        var lambda=_stiffness*(row.Error+Vector3.Dot(cx,row.Axis))/row.InverseMass;
        dp+=(-_alpha*_inverseMass)*(row.Axis*lambda);
        dq+=(_alpha*row.RotationAxis)*lambda;
    }
}
