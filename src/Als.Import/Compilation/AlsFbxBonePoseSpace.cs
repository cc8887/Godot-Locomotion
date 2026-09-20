using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

// FBX Skeleton3D bone atoms use a different basis from the canonical character-space
// contract. This is the inverse of the existing imported-rest-pose audit conversion.
public static class AlsFbxBonePoseSpace
{
    private static readonly Matrix4x4 ImportedToCanonical = new(
        0, 0, -1, 0,
        -1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 0, 1);
    private static readonly Matrix4x4 CanonicalToImported = Matrix4x4.Transpose(ImportedToCanonical);

    public static AlsLocalPose FromCanonical(in AlsLocalPose pose) => new(
        new(-pose.Position.Z, -pose.Position.X, pose.Position.Y),
        Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(ImportedToCanonical *
            Matrix4x4.CreateFromQuaternion(pose.Rotation) * CanonicalToImported)),
        new(pose.Scale.Z, pose.Scale.X, pose.Scale.Y));
}
