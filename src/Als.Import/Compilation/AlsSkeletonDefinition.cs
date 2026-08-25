using System.Numerics;

namespace GodotAls.Import.Compilation;

public sealed record AlsSkeletonDefinition(
    string AssetId,
    string ObjectPath,
    string SourceRestPoseHash,
    string TargetPhysicalRestPoseHash,
    AlsBoneDefinition[] LogicalBones,
    AlsBoneDefinition[] PhysicalBones,
    AlsVirtualBoneDefinition[] VirtualBones,
    AlsSocketDefinition[] Sockets,
    int[] LogicalToPhysical,
    int[] PhysicalToLogical,
    AlsRequiredBoneIds RequiredBones)
{
    public int GetLogicalBoneId(string name) => FindBone(LogicalBones, name);

    public int GetPhysicalBoneId(string name) => FindBone(PhysicalBones, name);

    private static int FindBone(AlsBoneDefinition[] bones, string name)
    {
        for (var index = 0; index < bones.Length; index++)
        {
            if (string.Equals(bones[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }
}

public sealed record AlsBoneDefinition(
    int LogicalId,
    int PhysicalId,
    string Name,
    int ParentLogicalId,
    int ParentPhysicalId,
    Vector3 Translation,
    Quaternion Rotation,
    Vector3 Scale);

public sealed record AlsVirtualBoneDefinition(
    int LogicalBoneId,
    int SourceLogicalBoneId,
    int TargetLogicalBoneId);

public sealed record AlsSocketDefinition(
    string Name,
    int LogicalBoneId,
    Vector3 Translation,
    Quaternion Rotation,
    Vector3 Scale);

public readonly record struct AlsRequiredBoneIds(int Root, int Pelvis, int FootLeft, int FootRight);
