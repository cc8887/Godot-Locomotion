using System.Numerics;
using System.Runtime.InteropServices;

namespace GodotAls.Core.Contracts;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsFloorSample(
    byte IsGrounded,
    Vector3 Normal,
    int PlatformId,
    Matrix4x4 PlatformTransform,
    Vector3 PlatformAngularVelocity);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsFootHit(
    byte HasHit,
    Vector3 Position,
    Vector3 Normal);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsMantleProbeResult(
    byte HasTarget,
    Matrix4x4 TargetTransform,
    int PlatformId);
