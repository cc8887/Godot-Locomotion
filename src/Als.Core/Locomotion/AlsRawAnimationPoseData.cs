using System.Numerics;
using ScalarMath = System.Math;

namespace GodotAls.Core.Locomotion;

// Resource identity only. A player, evaluator or occurrence must own its own runtime identity.
public readonly record struct AlsRawAnimationAssetIdentity(int AnimationId, string AssetId, string AssetPath, int SkeletonId);
public enum AlsRawAnimationInterpolation { Linear = 0, Step = 1 }

/// <summary>Immutable raw local keys before virtual-bone generation, retargeting, root lock
/// and additive conversion. Dense absent-track slots are ignored, not explicit identity tracks.</summary>
public sealed class AlsRawAnimationPoseData
{
    private readonly int[] _logicalToPhysical, _physicalToLogical, _virtualLogicalIndices;
    private readonly bool[] _logicalTrackPresence, _virtualTrackPresence;
    private readonly AlsLocalPose[] _physicalKeys, _virtualKeys;

    public AlsRawAnimationAssetIdentity Identity { get; }
    public int FrameRateNumerator { get; }
    public int FrameRateDenominator { get; }
    public int SampledKeyCount { get; }
    public double PlayLength { get; }
    public AlsRawAnimationInterpolation Interpolation { get; }
    public int LogicalBoneCount => _logicalToPhysical.Length;
    public int PhysicalBoneCount => _physicalToLogical.Length;
    public int VirtualBoneCount => _virtualLogicalIndices.Length;
    public ReadOnlySpan<int> LogicalToPhysical => _logicalToPhysical;
    public ReadOnlySpan<int> PhysicalToLogical => _physicalToLogical;
    public ReadOnlySpan<int> VirtualLogicalIndices => _virtualLogicalIndices;
    public ReadOnlySpan<bool> LogicalTrackPresence => _logicalTrackPresence;
    public ReadOnlySpan<bool> VirtualTrackPresence => _virtualTrackPresence;

    /// <param name="virtualLogicalIndices">Logical IDs in the skeleton's virtual-bone definition order.</param>
    /// <param name="physicalKeys">Key-major physical atoms. Only present tracks are validated and sampled.</param>
    /// <param name="virtualKeys">Key-major virtual atoms in virtualLogicalIndices order; absent slots are ignored.</param>
    public AlsRawAnimationPoseData(AlsRawAnimationAssetIdentity identity,
        int frameRateNumerator, int frameRateDenominator, int sampledKeyCount, double playLength,
        AlsRawAnimationInterpolation interpolation, ReadOnlySpan<int> logicalToPhysical,
        ReadOnlySpan<int> virtualLogicalIndices, ReadOnlySpan<bool> logicalTrackPresence,
        ReadOnlySpan<AlsLocalPose> physicalKeys, ReadOnlySpan<AlsLocalPose> virtualKeys)
    {
        if (identity.AnimationId < 0 || identity.SkeletonId < 0 || string.IsNullOrWhiteSpace(identity.AssetId) ||
            string.IsNullOrWhiteSpace(identity.AssetPath)) throw new ArgumentException("Raw animation requires its real resource identity.", nameof(identity));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameRateNumerator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameRateDenominator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampledKeyCount);
        if (!double.IsFinite(playLength) || playLength < 0) throw new ArgumentOutOfRangeException(nameof(playLength));
        var expectedDuration = (sampledKeyCount - 1) * (double)frameRateDenominator / frameRateNumerator;
        if (ScalarMath.Abs(playLength - expectedDuration) > ScalarMath.Max(1e-8, expectedDuration * 1e-6))
            throw new ArgumentException("Raw duration differs from its frame rate and key count.", nameof(playLength));
        if (interpolation is not (AlsRawAnimationInterpolation.Linear or AlsRawAnimationInterpolation.Step))
            throw new ArgumentOutOfRangeException(nameof(interpolation));
        if (logicalToPhysical.IsEmpty || logicalTrackPresence.Length != logicalToPhysical.Length)
            throw new ArgumentException("Raw logical track layout is incomplete.");

        _logicalToPhysical = logicalToPhysical.ToArray();
        _logicalTrackPresence = logicalTrackPresence.ToArray();
        _virtualLogicalIndices = virtualLogicalIndices.ToArray();
        var physicalCount = 0;
        foreach (var physical in _logicalToPhysical)
        {
            if (physical < -1 || physical >= LogicalBoneCount) throw new ArgumentException("Invalid raw logical-to-physical mapping.");
            if (physical >= 0) physicalCount++;
        }
        if (physicalCount == 0 || VirtualBoneCount != LogicalBoneCount - physicalCount)
            throw new ArgumentException("Raw mapping must cover every physical and virtual bone.");
        _physicalToLogical = new int[physicalCount]; Array.Fill(_physicalToLogical, -1);
        for (var logical = 0; logical < LogicalBoneCount; logical++)
        {
            var physical = _logicalToPhysical[logical];
            if (physical < 0) continue;
            if (physical >= physicalCount || _physicalToLogical[physical] >= 0)
                throw new ArgumentException("Raw physical mapping must be unique and contiguous.");
            _physicalToLogical[physical] = logical;
        }
        var seenVirtual = new bool[LogicalBoneCount];
        _virtualTrackPresence = new bool[VirtualBoneCount];
        for (var index = 0; index < VirtualBoneCount; index++)
        {
            var logical = _virtualLogicalIndices[index];
            if ((uint)logical >= (uint)LogicalBoneCount || _logicalToPhysical[logical] >= 0 || seenVirtual[logical])
                throw new ArgumentException("Raw virtual mapping must contain each virtual bone once.");
            seenVirtual[logical] = true; _virtualTrackPresence[index] = _logicalTrackPresence[logical];
        }
        if (physicalKeys.Length != (long)sampledKeyCount * physicalCount ||
            virtualKeys.Length != (long)sampledKeyCount * VirtualBoneCount)
            throw new ArgumentException("Raw key arrays must use the complete key-major bone stride.");
        _physicalKeys = physicalKeys.ToArray(); _virtualKeys = virtualKeys.ToArray();
        for (var key = 0; key < sampledKeyCount; key++)
        {
            for (var physical = 0; physical < physicalCount; physical++)
                if (_logicalTrackPresence[_physicalToLogical[physical]]) ValidatePose(_physicalKeys[key * physicalCount + physical]);
            for (var index = 0; index < VirtualBoneCount; index++)
                if (_virtualTrackPresence[index]) ValidatePose(_virtualKeys[key * VirtualBoneCount + index]);
        }

        Identity = identity; FrameRateNumerator = frameRateNumerator; FrameRateDenominator = frameRateDenominator;
        SampledKeyCount = sampledKeyCount; PlayLength = playLength; Interpolation = interpolation;
    }

    public ReadOnlySpan<AlsLocalPose> GetPhysicalKey(int index)
    {
        ValidateKey(index); return _physicalKeys.AsSpan(index * PhysicalBoneCount, PhysicalBoneCount);
    }
    public ReadOnlySpan<AlsLocalPose> GetVirtualKey(int index)
    {
        ValidateKey(index); return _virtualKeys.AsSpan(index * VirtualBoneCount, VirtualBoneCount);
    }
    private void ValidateKey(int index)
    { if ((uint)index >= (uint)SampledKeyCount) throw new ArgumentOutOfRangeException(nameof(index)); }

    internal static void ValidatePose(in AlsLocalPose pose, double rotationTolerance = 1e-4)
    {
        var q = pose.Rotation;
        var length = (double)q.X * q.X + (double)q.Y * q.Y + (double)q.Z * q.Z + (double)q.W * q.W;
        if (!Finite(pose.Position) || !Finite(pose.Scale) || !double.IsFinite(length) || ScalarMath.Abs(length - 1) > rotationTolerance)
            throw new ArgumentException("Raw local atoms require finite TRS and near-unit quaternions; values are never normalized on import.");
    }
    private static bool Finite(Vector3 vector) => float.IsFinite(vector.X) && float.IsFinite(vector.Y) && float.IsFinite(vector.Z);
}
