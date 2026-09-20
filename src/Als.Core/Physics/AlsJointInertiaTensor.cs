using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

internal readonly record struct AlsJointInertiaTensor(AlsDoubleVector X, AlsDoubleVector Y, AlsDoubleVector Z)
{
    public static AlsJointInertiaTensor World(AlsQuaternion q, AlsJointInverseMass mass)
    {
        if (mass.Mass == 0) return default;
        var x = new AlsDoubleVector(1,0,0).Rotate(q);
        var y = new AlsDoubleVector(0,1,0).Rotate(q);
        var z = new AlsDoubleVector(0,0,1).Rotate(q);
        var i = mass.Inertia;
        return new(x*(i.X*x.X)+y*(i.Y*y.X)+z*(i.Z*z.X),
            x*(i.X*x.Y)+y*(i.Y*y.Y)+z*(i.Z*z.Y),
            x*(i.X*x.Z)+y*(i.Y*y.Z)+z*(i.Z*z.Z));
    }
    public AlsDoubleVector Multiply(AlsDoubleVector a, bool single) => single
        ? new(X.ToSingle()*(float)a.X + (Y.ToSingle()*(float)a.Y + Z.ToSingle()*(float)a.Z))
        : X*a.X+Y*a.Y+Z*a.Z;
}
