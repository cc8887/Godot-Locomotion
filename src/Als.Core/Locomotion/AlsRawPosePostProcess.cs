using System.Numerics;

namespace GodotAls.Core.Locomotion;

/// <summary>FRetargetingScope's tracked-bone pass for one sequence on the same skeleton.
/// The current ALS skeleton authors Animation/Skeleton modes only. Missing tracks and
/// generated virtual bones are never added to this pass.</summary>
public sealed class AlsRawPoseRetargetModel
{
    private readonly int[] _logicalIds;
    private readonly Vector3[] _translations;
    private readonly int _logicalCount;
    private readonly bool _hasRetargetTransforms;

    public AlsRawPoseRetargetModel(ReadOnlySpan<int> logicalToPhysical, ReadOnlySpan<int> physicalModes,
        ReadOnlySpan<bool> logicalTrackPresence, ReadOnlySpan<AlsLocalPose> targetReference,
        bool hasRetargetTransforms)
    {
        if (logicalToPhysical.IsEmpty || logicalTrackPresence.Length != logicalToPhysical.Length ||
            targetReference.Length != logicalToPhysical.Length || physicalModes.IsEmpty)
            throw new ArgumentException("Raw retarget layout differs from the complete source skeleton.");
        var seen = new bool[physicalModes.Length]; var ids = new List<int>(); var positions = new List<Vector3>();
        for (var logical = 0; logical < logicalToPhysical.Length; logical++)
        {
            AlsRawRootLock.Validate(targetReference[logical]);
            var physical = logicalToPhysical[logical];
            if (physical == -1) continue; // USkeleton returns Animation for virtual BoneTree indices.
            if ((uint)physical >= (uint)physicalModes.Length || seen[physical])
                throw new ArgumentException("Raw retarget physical mapping is invalid or duplicated.");
            seen[physical] = true;
            if (physicalModes[physical] is not (0 or 1))
                throw new ArgumentException("This raw retarget pass requires authored Animation/Skeleton modes.");
            if (logicalTrackPresence[logical] && physicalModes[physical] == 1)
            { ids.Add(logical); positions.Add(targetReference[logical].Position); }
        }
        if (seen.Any(present => !present)) throw new ArgumentException("Raw retarget physical mapping is incomplete.");
        _logicalCount = logicalToPhysical.Length; _logicalIds = ids.ToArray(); _translations = positions.ToArray();
        _hasRetargetTransforms = hasRetargetTransforms;
    }

    public void Apply(Span<AlsLocalPose> logicalPose, bool shouldRetarget)
    {
        if (logicalPose.Length != _logicalCount) throw new ArgumentException("Raw retarget output layout differs.");
        foreach (var atom in logicalPose) AlsRawRootLock.Validate(atom);
        if (!shouldRetarget || !_hasRetargetTransforms) return;
        for (var index = 0; index < _logicalIds.Length; index++)
        {
            var logical = _logicalIds[index];
            logicalPose[logical] = logicalPose[logical] with { Position = _translations[index] };
        }
    }
}

/// <summary>Root reset after raw sequence sampling/retargeting. Raw GetBonePose is never
/// baked additive, even for an asset later converted to an additive delta.</summary>
public static class AlsRawRootLock
{
    public static AlsLocalPose Apply(in AlsLocalPose sampledRoot, in AlsLocalPose referenceRoot,
        in AlsLocalPose animationFirstFrame, int rootLock, bool rootMotionEnabled,
        bool forceRootLock, bool extractRootMotion, bool ignoreRootLock)
    {
        Validate(sampledRoot); Validate(referenceRoot); Validate(animationFirstFrame);
        if (rootLock is < 0 or > 2) throw new ArgumentException("Unsupported native root lock mode.");
        if (!(extractRootMotion && rootMotionEnabled) && !(forceRootLock && !ignoreRootLock)) return sampledRoot;
        return rootLock switch { 0 => referenceRoot, 1 => animationFirstFrame, _ => AlsLocalPose.Identity };
    }

    internal static void Validate(in AlsLocalPose atom)
    {
        var q = atom.Rotation;
        var norm = (double)q.X * q.X + (double)q.Y * q.Y + (double)q.Z * q.Z + (double)q.W * q.W;
        if (!Finite(atom.Position) || !Finite(atom.Scale) || !double.IsFinite(norm) || System.Math.Abs(norm - 1) > .01)
            throw new ArgumentException("Raw pose requires finite TRS and a near-unit rotation.");
    }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
