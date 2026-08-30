using Godot;

namespace GodotAls.Animation;

public sealed class AlsPoseScratch
{
    public AlsPoseScratch(Skeleton3D skeleton)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        var count = skeleton.GetBoneCount();
        if (count <= 0)
        {
            throw new InvalidOperationException("Pose modifier requires a non-empty Skeleton3D.");
        }

        OriginalPositions = new Vector3[count];
        OriginalRotations = new Quaternion[count];
        OriginalScales = new Vector3[count];
        ResultPositions = new Vector3[count];
        ResultRotations = new Quaternion[count];
        ResultScales = new Vector3[count];
        OriginalPose = new Transform3D[count];
        LocalPose = new Transform3D[count];
        OriginalComponentPose = new Transform3D[count];
        ComponentPose = new Transform3D[count];
        ComponentInverses = new Transform3D[count];
        ComponentInverseValid = new bool[count];
        BaseLocalPose = new Transform3D[count];
        BaseComponentPose = new Transform3D[count];
        BindComponentPose = new Transform3D[count];
        DownLocalPose = new Transform3D[count];
        DownComponentPose = new Transform3D[count];
        ForwardLocalPose = new Transform3D[count];
        ForwardComponentPose = new Transform3D[count];
        UpLocalPose = new Transform3D[count];
        UpComponentPose = new Transform3D[count];
        Parents = new int[count];
        Affected = new bool[count];
        AimAffected = new bool[count];
        AimSampled = new bool[count];
        Modified = new bool[count];
        ValidationVisited = new bool[count];
        Order = new int[count];
        AffectedOrder = new int[count];
        AimAffectedOrder = new int[count];
        AimSampleOrder = new int[count];

        BuildParentOrder(skeleton);
    }

    public int BoneCount => Parents.Length;

    public Vector3[] OriginalPositions { get; }

    public Quaternion[] OriginalRotations { get; }

    public Vector3[] OriginalScales { get; }

    public Vector3[] ResultPositions { get; }

    public Quaternion[] ResultRotations { get; }

    public Vector3[] ResultScales { get; }

    public Transform3D[] OriginalPose { get; }

    public Transform3D[] LocalPose { get; }

    public Transform3D[] OriginalComponentPose { get; }

    public Transform3D[] ComponentPose { get; }

    public Transform3D[] ComponentInverses { get; }

    public bool[] ComponentInverseValid { get; }

    public Transform3D[] BaseLocalPose { get; }

    public Transform3D[] BaseComponentPose { get; }

    public Transform3D[] BindComponentPose { get; }

    public Transform3D[] DownLocalPose { get; }

    public Transform3D[] DownComponentPose { get; }

    public Transform3D[] ForwardLocalPose { get; }

    public Transform3D[] ForwardComponentPose { get; }

    public Transform3D[] UpLocalPose { get; }

    public Transform3D[] UpComponentPose { get; }

    public int[] Parents { get; }

    public bool[] Affected { get; }

    public bool[] AimAffected { get; }

    public bool[] AimSampled { get; }

    public bool[] Modified { get; }

    public bool[] ValidationVisited { get; }

    public int[] Order { get; }

    public int[] AffectedOrder { get; }

    public int[] AimAffectedOrder { get; }

    public int[] AimSampleOrder { get; }

    public int AffectedCount { get; internal set; }

    public int AimAffectedCount { get; internal set; }

    public int AimSampleCount { get; internal set; }

    public int FootChainRebuildCount { get; internal set; }

    public int FootFullSkeletonRebuildCount { get; internal set; }

    public int FootComponentPropagationCount { get; internal set; }

    public long TotalFullSkeletonBuildCount { get; internal set; }

    public long TotalFootChainRebuildCount { get; internal set; }

    public long TotalComponentPropagationCount { get; internal set; }

    private void BuildParentOrder(Skeleton3D skeleton)
    {
        for (var boneId = 0; boneId < Parents.Length; boneId++)
        {
            var parent = skeleton.GetBoneParent(boneId);
            if (parent < -1 || parent >= Parents.Length || parent == boneId)
            {
                throw new InvalidOperationException($"Skeleton has an invalid parent: bone={boneId} parent={parent}");
            }
            Parents[boneId] = parent;
        }

        var written = 0;
        while (written < Parents.Length)
        {
            var progress = false;
            for (var boneId = 0; boneId < Parents.Length; boneId++)
            {
                if (Contains(Order, written, boneId))
                {
                    continue;
                }
                var parent = Parents[boneId];
                if (parent >= 0 && !Contains(Order, written, parent))
                {
                    continue;
                }
                Order[written++] = boneId;
                progress = true;
            }
            if (!progress)
            {
                throw new InvalidOperationException("Skeleton hierarchy contains a cycle.");
            }
        }
    }

    private static bool Contains(int[] values, int count, int value)
    {
        for (var index = 0; index < count; index++)
        {
            if (values[index] == value)
            {
                return true;
            }
        }
        return false;
    }
}
