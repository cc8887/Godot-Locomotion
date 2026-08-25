using System.Numerics;
using System.Runtime.InteropServices;

namespace GodotAls.Core.Contracts;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsRootMotionDelta(
    Vector3 Translation,
    Quaternion Rotation)
{
    public static AlsRootMotionDelta Identity => new(Vector3.Zero, Quaternion.Identity);
}
