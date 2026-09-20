namespace GodotAls.Import.Compilation;

internal static class AlsLogicalBoneBranches
{
    // A depth-zero branch filter covers descendants in the complete reference skeleton,
    // including virtual bones parented to its source bone.
    public static int[] Descendants(AlsSkeletonDefinition skeleton, ReadOnlySpan<string> names)
    {
        var roots = names.ToArray().Select(skeleton.GetLogicalBoneId).ToHashSet();
        if (roots.Contains(-1) || roots.Count != names.Length) throw new FormatException("Unknown or duplicate logical branch root.");
        var bones = skeleton.LogicalBones; var result = new List<int>();
        for (var bone = 0; bone < bones.Length; bone++)
            for (var ancestor = bone; ancestor >= 0; ancestor = bones[ancestor].ParentLogicalId)
            {
                if (ancestor >= bones.Length || bones[ancestor].ParentLogicalId >= ancestor)
                    throw new FormatException("Logical branch requires a parent-first skeleton.");
                if (roots.Contains(ancestor)) { result.Add(bone); break; }
            }
        return result.ToArray();
    }
}
