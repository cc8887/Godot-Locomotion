using System.Numerics;
using GodotAls.Core.Contracts;
using M = System.Math;

namespace GodotAls.Core.Locomotion;

// World uses Godot axes/meters; component poses retain the skeleton's bone basis.
// The source is the previously committed final skeleton,
// never a partially evaluated pose. A default identity denotes cold reference pose.
public readonly record struct AlsFootIkPoseSample(AlsFrameIdentity Identity, AlsLocalPose ComponentToCharacter,
    AlsLocalPose LeftComponent, AlsLocalPose RightComponent, Vector3 RootComponent,
    float LeftEnableCurve, float RightEnableCurve);

// Main-thread scene observations; no Godot objects cross the frame boundary.
public readonly record struct AlsFootIkSceneSample(byte Captured, AlsFrameIdentity PoseIdentity,
    AlsLocalPose ComponentToWorld, AlsLocalPose LeftComponent, AlsLocalPose RightComponent,
    Vector3 RootWorld, Quaternion LastMovementRotation, Vector3 MovementVelocity, float WorldDelta)
{
    // Explicit movement-owner event, not inferred from an arbitrary distance.
    public ulong TeleportSequence { get; init; }
}

public static class AlsFootIkCoordinates
{
    public static AlsPrecisePose FbxComponentToNativeWorld(in AlsLocalPose fbxWorld)
    {
        new AlsPrecisePose(fbxWorld).Validate(.001);
        var q = fbxWorld.Rotation * Quaternion.Conjugate(FbxToGodotRotation);
        return new(ToNative(fbxWorld.Position), new AlsQuaternion(q.Z, -q.X, -q.Y, q.W).Normalized(), new(fbxWorld.Scale));
    }
    // FBX bone-local axes -> canonical Godot component axes: (-Y, Z, -X).
    public static Quaternion FbxToGodotRotation => new(-.5f,.5f,.5f,.5f);
    public static AlsDoubleVector ComponentToNative(Vector3 v, AlsFootIkPoseSpace space) =>
        space == AlsFootIkPoseSpace.Fbx ? new((double)v.X*100,-(double)v.Y*100,(double)v.Z*100) : ToNative(v);
    public static AlsDoubleVector ToNative(Vector3 v) => new(-(double)v.Z * 100, (double)v.X * 100, (double)v.Y * 100);
    public static Vector3 FromNative(AlsDoubleVector v) => new((float)(v.Y * .01), (float)(v.Z * .01), (float)(-v.X * .01));
    public static AlsAimingRotation Rotation(Quaternion value, AlsFootIkPoseSpace space = AlsFootIkPoseSpace.Godot)
    {
        var q = space == AlsFootIkPoseSpace.Fbx ? new AlsQuaternion(-value.X,value.Y,-value.Z,value.W) :
            new AlsQuaternion(value.Z, -value.X, -value.Y, value.W);
        if (!double.IsFinite(q.LengthSquared) || M.Abs(q.LengthSquared - 1) > .001)
            throw new ArgumentException("Foot scene rotation must be finite and normalized.");
        var singularity = q.Z * q.X - q.W * q.Y;
        var degrees = 180 / M.PI;
        // UE5.9 FQuat4d::Rotator fixes roll to zero at the singularities.
        if (singularity < -.4999995) return new(-90, AlsCharacterRotationMath.Normalize(-2 * M.Atan2(q.X, q.W) * degrees), 0);
        if (singularity > .4999995) return new(90, AlsCharacterRotationMath.Normalize(2 * M.Atan2(q.X, q.W) * degrees), 0);
        return new(M.Asin(M.Clamp(2 * singularity, -1, 1)) * degrees,
            M.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)) * degrees,
            M.Atan2(-2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y)) * degrees);
    }
}
