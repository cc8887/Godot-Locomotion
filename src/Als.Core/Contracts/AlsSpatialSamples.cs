using System.Numerics;
using System.Runtime.InteropServices;

namespace GodotAls.Core.Contracts;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsFloorSample(
    byte IsGrounded,
    Vector3 Normal,
    int PlatformId,
    Matrix4x4 PlatformTransform,
    Vector3 PlatformAngularVelocity,
    long ColliderId)
{
    public AlsFloorSample(
        byte IsGrounded,
        Vector3 Normal,
        int PlatformId,
        Matrix4x4 PlatformTransform,
        Vector3 PlatformAngularVelocity)
        : this(
            IsGrounded,
            Normal,
            PlatformId,
            PlatformTransform,
            PlatformAngularVelocity,
            -1)
    {
    }

    public void Deconstruct(
        out byte IsGrounded,
        out Vector3 Normal,
        out int PlatformId,
        out Matrix4x4 PlatformTransform,
        out Vector3 PlatformAngularVelocity)
    {
        IsGrounded = this.IsGrounded;
        Normal = this.Normal;
        PlatformId = this.PlatformId;
        PlatformTransform = this.PlatformTransform;
        PlatformAngularVelocity = this.PlatformAngularVelocity;
    }
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsFootHit(
    byte Valid,
    byte Walkable,
    Vector3 Position,
    Vector3 Normal,
    int PlatformId,
    Vector3 PlatformPosition,
    Quaternion PlatformRotation,
    long ColliderId,
    Vector3 PointVelocity)
{
    public static AlsFootHit Invalid => new(
        0,
        0,
        Vector3.Zero,
        Vector3.UnitY,
        -1,
        Vector3.Zero,
        Quaternion.Identity,
        -1,
        Vector3.Zero);
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsMantleProbeResult(
    byte HasTarget,
    Matrix4x4 TargetTransform,
    int PlatformId);
