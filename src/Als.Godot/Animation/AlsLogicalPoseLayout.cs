using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// One complete logical layout for a source sampler. Physical Skeleton3D output remains separate.
// Expand is for a sampled key; interpolated sequences must expand both keys before interpolating.
internal sealed class AlsLogicalPoseLayout
{
    private readonly AlsLogicalPoseExpansion _expansion;
    public readonly string[] Names;
    public readonly int[] Parents;
    public readonly AlsLocalPose[] PhysicalReferencePose, ReferencePose;
    public int VirtualCount => _expansion.VirtualCount;

    public AlsLogicalPoseLayout(AlsSkeletonDefinition definition, Skeleton3D skeleton)
    {
        AlsAnimationBinder.ValidateTargetSkeleton(skeleton, definition, "Logical pose layout");
        Names = definition.LogicalBones.Select(b => b.Name).ToArray();
        Parents = definition.LogicalBones.Select(b => b.ParentLogicalId).ToArray();
        PhysicalReferencePose = new AlsLocalPose[skeleton.GetBoneCount()];
        for (var bone = 0; bone < PhysicalReferencePose.Length; bone++)
        {
            if (skeleton.GetBoneParent(bone) != definition.PhysicalBones[bone].ParentPhysicalId)
                throw new ArgumentException("Logical pose physical parent differs from the source skeleton.");
            var rest = skeleton.GetBoneRest(bone); var q = rest.Basis.GetRotationQuaternion(); var s = rest.Basis.Scale;
            PhysicalReferencePose[bone] = new(new(rest.Origin.X, rest.Origin.Y, rest.Origin.Z),
                new(q.X, q.Y, q.Z, q.W), new(s.X, s.Y, s.Z));
        }
        var logicalRest = definition.LogicalBones.Select(b =>
            AlsFbxBonePoseSpace.FromCanonical(new AlsLocalPose(b.Translation, b.Rotation, b.Scale))).ToArray();
        var virtualBones = definition.VirtualBones.Select(b =>
            new AlsLogicalVirtualBone(b.LogicalBoneId, b.SourceLogicalBoneId, b.TargetLogicalBoneId)).ToArray();
        _expansion = new(Parents, definition.LogicalToPhysical, logicalRest, virtualBones);
        ReferencePose = new AlsLocalPose[Names.Length];
        _expansion.Expand(PhysicalReferencePose, ReferencePose, new AlsLocalPose[Names.Length]);
    }

    public void ExpandKey(ReadOnlySpan<AlsLocalPose> physical, Span<AlsLocalPose> logical, Span<AlsLocalPose> components) =>
        _expansion.Expand(physical, logical, components);
}
