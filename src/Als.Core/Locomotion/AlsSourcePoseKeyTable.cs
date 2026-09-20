using System.Numerics;
using ScalarMath = System.Math;

namespace GodotAls.Core.Locomotion;

/// <summary>Immutable source animation keys in physical-bone order. These are raw local
/// atoms before virtual-bone generation and retargeting, not evaluated or blended poses.
/// This table exposes exact keys only; it does not implement sampling between keys.</summary>
public sealed class AlsSourcePoseKeyTable
{
    private readonly AlsLocalPose[] _keys;

    public AlsBasePoseEvaluatorDefinition Evaluator { get; }
    public int FrameRateNumerator { get; }
    public int FrameRateDenominator { get; }
    public double PlayLength { get; }
    public int PhysicalBoneCount { get; }
    public int SampledKeyCount => _keys.Length / PhysicalBoneCount;

    /// <param name="keys">Key-major local poses: every physical bone of key 0,
    /// then every physical bone of key 1, and so on. Values are copied verbatim.</param>
    public AlsSourcePoseKeyTable(AlsBasePoseEvaluatorDefinition evaluator,
        int frameRateNumerator, int frameRateDenominator, double playLength,
        int physicalBoneCount, ReadOnlySpan<AlsLocalPose> keys)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameRateNumerator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameRateDenominator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(physicalBoneCount);
        if (!double.IsFinite(playLength) || playLength < 0)
            throw new ArgumentOutOfRangeException(nameof(playLength), "Source duration must be finite and nonnegative.");
        if (keys.IsEmpty || keys.Length % physicalBoneCount != 0)
            throw new ArgumentException("Source keys must contain a positive whole number of physical-bone poses.", nameof(keys));

        var keyCount = keys.Length / physicalBoneCount;
        var expectedDuration = (keyCount - 1) * (double)frameRateDenominator / frameRateNumerator;
        // UE's sequence duration is double, while evaluator/manifest duration is float.
        var durationTolerance = ScalarMath.Max(1e-8, expectedDuration * 1e-6);
        if (ScalarMath.Abs(playLength - expectedDuration) > durationTolerance)
            throw new ArgumentException("Source duration does not match its key count and frame rate.", nameof(playLength));
        if (!float.IsFinite(evaluator.Length) || evaluator.Length < 0 ||
            ScalarMath.Abs(evaluator.Length - playLength) > durationTolerance)
            throw new ArgumentException("Evaluator duration does not match its source key table.", nameof(evaluator));

        _keys = keys.ToArray();
        for (var index = 0; index < _keys.Length; index++)
        {
            var pose = _keys[index];
            var q = pose.Rotation;
            var lengthSquared = (double)q.X * q.X + (double)q.Y * q.Y + (double)q.Z * q.Z + (double)q.W * q.W;
            if (!Finite(pose.Position) || !Finite(pose.Scale) || !double.IsFinite(lengthSquared) ||
                ScalarMath.Abs(lengthSquared - 1) > 1e-4)
                throw new ArgumentException($"Source key {index / physicalBoneCount}, bone {index % physicalBoneCount} requires finite TRS and a near-unit quaternion.", nameof(keys));
            // Raw float quaternions need not have exactly unit length. Normalization or a
            // matrix/Euler round trip here would alter the authored source data.
        }

        Evaluator = evaluator;
        FrameRateNumerator = frameRateNumerator;
        FrameRateDenominator = frameRateDenominator;
        PlayLength = playLength;
        PhysicalBoneCount = physicalBoneCount;
    }

    public ReadOnlySpan<AlsLocalPose> GetKey(int index)
    {
        if ((uint)index >= (uint)SampledKeyCount) throw new ArgumentOutOfRangeException(nameof(index));
        return _keys.AsSpan(index * PhysicalBoneCount, PhysicalBoneCount);
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
