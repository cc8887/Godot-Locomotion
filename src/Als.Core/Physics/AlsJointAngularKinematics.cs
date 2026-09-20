using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// UE joint coordinates, radians. Axis order is X=Twist, Y=Swing2, Z=Swing1.
// Kinematics only: this does not apply a constraint, soft impulse or drive.
public readonly record struct AlsJointAngularKinematics(
    AlsQuaternion Swing, AlsQuaternion Twist, AlsDoubleVector Angles,
    AlsDoubleVector TwistAxis, AlsDoubleVector PyramidY, AlsDoubleVector PyramidZ,
    AlsDoubleVector LockedX, AlsDoubleVector LockedY, AlsDoubleVector LockedZ)
{
    public static AlsJointAngularKinematics Evaluate(AlsQuaternion parent, AlsQuaternion child)
    {
        Validate(parent); Validate(child);
        var relative = parent.Conjugate() * child;
        // Chaos::TRotation::ToSwingTwistX preserves the X == 0 branch.
        var twist = relative.X != 0 ? new AlsQuaternion(relative.X,0,0,relative.W).Normalized() : AlsQuaternion.Identity;
        var swing = relative * twist.Conjugate();
        var normalizedTwist = twist.Normalized();
        var twistAngle = 2 * System.Math.Acos(System.Math.Clamp(normalizedTwist.W,-1,1));
        // GetTwistAngle uses UE_PI (a float literal promoted to double).
        const double nativePi = 3.1415926535897932f;
        if (twistAngle > nativePi) twistAngle -= 2 * nativePi;
        if (twist.X < 0) twistAngle = -twistAngle;
        var angles = new AlsDoubleVector(twistAngle,
            4*System.Math.Atan2(swing.Y,1+swing.W),4*System.Math.Atan2(swing.Z,1+swing.W));
        var swingWorld = parent * swing;
        // The cached (linear) Chaos solver uses two pyramid axes, evaluated in
        // the swung parent frame. Do not replace these with Euler angles.
        var x0 = new AlsDoubleVector(parent.X,parent.Y,parent.Z);
        var x1 = new AlsDoubleVector(child.X,child.Y,child.Z);
        var c = x1*parent.W + x0*child.W;
        var d0 = parent.W*child.W; var d1 = AlsDoubleVector.Dot(x0,x1); var d = d0-d1;
        var lockedX = (x0*child.X+x1*parent.X+new AlsDoubleVector(d,c.Z,-c.Y))*.5;
        var lockedY = (x0*child.Y+x1*parent.Y+new AlsDoubleVector(-c.Z,d,c.X))*.5;
        var lockedZ = (x0*child.Z+x1*parent.Z+new AlsDoubleVector(c.Y,-c.X,d))*.5;
        if (System.Math.Abs(d0+d1) < 1e-8f)
        {
            lockedX += new AlsDoubleVector(1e-8f,0,0);
            lockedY += new AlsDoubleVector(0,1e-8f,0);
            lockedZ += new AlsDoubleVector(0,0,1e-8f);
        }
        return new(swing,twist,angles,new AlsDoubleVector(1,0,0).Rotate(child),
            new AlsDoubleVector(0,1,0).Rotate(swingWorld),new AlsDoubleVector(0,0,1).Rotate(swingWorld),
            lockedX,lockedY,lockedZ);
    }

    public static AlsDoubleVector SwingTwistDriveError(AlsQuaternion parent, AlsQuaternion child, AlsQuaternion target)
    {
        Validate(parent); Validate(child); Validate(target);
        var worldTarget = parent*target;
        if (AlsQuaternion.Dot(worldTarget,child) < 0) worldTarget = -worldTarget;
        var error = worldTarget.Conjugate()*child;
        var twistAxisError = new AlsDoubleVector(1,0,0).Rotate(error);
        // Native drive errors intentionally approximate sin(angle), unlike the
        // swing/twist angles used for the limit checks.
        return new(2*error.X,-twistAxisError.Z,twistAxisError.Y);
    }

    private static void Validate(AlsQuaternion q)
    {
        if (!double.IsFinite(q.LengthSquared) || System.Math.Abs(q.LengthSquared-1) > 1e-6)
            throw new ArgumentException("Joint kinematics require finite unit quaternions.");
    }
}
