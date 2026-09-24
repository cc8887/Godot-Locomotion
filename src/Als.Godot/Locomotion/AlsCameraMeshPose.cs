using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Physics;

namespace GodotAls.Locomotion;

// Camera offsets/radius use native mesh Z scale. This is a camera transform,
// not permission to scale the rigid-body pose/history transport.
internal readonly record struct AlsCameraMeshPose(AlsQuaternion Rotation, float Scale)
{
    internal static AlsCameraMeshPose FromWorld(Transform3D fbxWorld)
    {
        if (!fbxWorld.IsFinite()) throw new ArgumentException("Nonfinite camera mesh transform.");
        var basis = fbxWorld.Basis;
        var scale = new Vector3(basis.X.Length(), basis.Y.Length(), basis.Z.Length());
        if (!scale.IsFinite() || scale.X <= 0 || scale.Y <= 0 || scale.Z <= 0)
            throw new ArgumentException("Camera mesh scale must be positive and nonsingular.");
        var rotation = new Basis(basis.X / scale.X, basis.Y / scale.Y, basis.Z / scale.Z);
        // UE's component FTransform has separate rotation/scale, not shear.
        // Reject unsupported mirrored/sheared matrices instead of silently
        // inventing a rotation through Gram-Schmidt.
        if (Math.Abs(rotation.X.Dot(rotation.Y)) > 1e-5f ||
            Math.Abs(rotation.Y.Dot(rotation.Z)) > 1e-5f ||
            Math.Abs(rotation.Z.Dot(rotation.X)) > 1e-5f || rotation.Determinant() <= 0)
            throw new ArgumentException("Camera mesh transform must not contain shear or reflection.");
        var rigid = new Transform3D(rotation.Orthonormalized(), fbxWorld.Origin);
        // Imported bone axes are (native X, -native Y, native Z). Local Z,
        // not Godot world Y or the largest component, is the authored scale.
        return new(AlsCorePhysicsPose.FromWorld(rigid).Rotation, scale.Z);
    }
}
