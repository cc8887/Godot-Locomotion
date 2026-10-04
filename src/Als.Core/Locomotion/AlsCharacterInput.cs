using System.Numerics;

namespace GodotAls.Core.Locomotion;

// Input-space mapping supplies the requested vector/up axis. Core owns the
// consumed length, local-to-world rotation and requested speed fraction.
public static class AlsCharacterInput
{
    public static Vector3 Consume(Vector3 requested,Vector3 up,float yawRadians,bool worldSpace,float scale)
    {
        if(!Finite(requested)||!Finite(up)||!float.IsFinite(yawRadians)||!float.IsFinite(scale)||scale<0||
            MathF.Abs(AlsCharacterSweepMath.Dot(up,up)-1)>1e-5f)
            throw new ArgumentException("Invalid consumed character input.");
        var direction=AlsCharacterSweepMath.LimitLength(requested);
        if(!worldSpace)direction=AlsCharacterSweepMath.Rotate(direction,up,yawRadians);
        return scale==1?direction:direction*scale;
    }
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
}
