using System.Numerics;
using System.Threading;
using ScalarMath = System.Math;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsRawPoseKeySelection(int FirstKey, int SecondKey, float Alpha,
    bool Interpolate, double SampleTimeSeconds);

// Captured engine builds differ in whether the inline FFrameTime constructor's
// (Subframe + .5f) - .5f cancellation is optimized away.
public enum AlsRawFrameTimeRounding { OptimizedCancellation, RoundSubframe }

/// <summary>Exclusive per-owner raw sequence sampler. Shared data is immutable; all scratch
/// belongs to this instance. Time selects a pose only: looping and time advancement belong
/// to the source player. Output is before retargeting, root lock and additive conversion.</summary>
public sealed class AlsRawSequencePoseSampler
{
    private const float KeySnapThreshold = 1e-4f;
    private readonly AlsLogicalPoseExpansion _expansion;
    private readonly AlsLocalPose[] _physicalReference, _physicalKey, _first, _second, _components;
    private int _sampling;
    public AlsRawAnimationPoseData Data { get; }

    /// <param name="logicalReferencePose">The caller's actual extraction reference pose,
    /// including the native virtual-bone rest atoms. Missing tracks read this snapshot.</param>
    public AlsRawSequencePoseSampler(AlsRawAnimationPoseData data, ReadOnlySpan<int> logicalParents,
        ReadOnlySpan<AlsLocalPose> logicalReferencePose, ReadOnlySpan<AlsLogicalVirtualBone> virtualBones)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (logicalReferencePose.Length != data.LogicalBoneCount || virtualBones.Length != data.VirtualBoneCount)
            throw new ArgumentException("Raw sampler reference or virtual layout differs from its source.");
        for (var index = 0; index < virtualBones.Length; index++)
            if (virtualBones[index].Bone != data.VirtualLogicalIndices[index])
                throw new ArgumentException("Raw sampler virtual-bone order differs from its source keys.");
        _expansion = new(logicalParents, data.LogicalToPhysical, logicalReferencePose, virtualBones);
        Data = data;
        _physicalReference = new AlsLocalPose[data.PhysicalBoneCount];
        for (var physical = 0; physical < _physicalReference.Length; physical++)
            _physicalReference[physical] = logicalReferencePose[data.PhysicalToLogical[physical]];
        _physicalKey = new AlsLocalPose[data.PhysicalBoneCount];
        _first = new AlsLocalPose[data.LogicalBoneCount]; _second = new AlsLocalPose[data.LogicalBoneCount];
        _components = new AlsLocalPose[data.LogicalBoneCount];
    }

    public AlsRawPoseKeySelection Sample(double seconds, Span<AlsLocalPose> logicalPose)
    {
        if (logicalPose.Length != Data.LogicalBoneCount) throw new ArgumentException("Raw sample output layout differs.", nameof(logicalPose));
        var keys = SelectKeys(Data, seconds);
        if (Interlocked.CompareExchange(ref _sampling, 1, 0) != 0)
            throw new InvalidOperationException("Raw sampler scratch is already in use by its owner.");
        try
        {
            ExpandKey(keys.FirstKey, _first);
            if (keys.Interpolate)
            {
                // UE AnimDataModel::ExtractPose builds both sets of missing VBs before
                // interpolating. Generating VBs from interpolated physical atoms differs.
                ExpandKey(keys.SecondKey, _second);
                for (var bone = 0; bone < logicalPose.Length; bone++)
                    logicalPose[bone] = BlendTransform(_first[bone], _second[bone], keys.Alpha);
            }
            else _first.CopyTo(logicalPose);
            return keys;
        }
        finally { Volatile.Write(ref _sampling, 0); }
    }

    private void ExpandKey(int index, Span<AlsLocalPose> output)
    {
        var key = Data.GetPhysicalKey(index);
        for (var physical = 0; physical < _physicalKey.Length; physical++)
            _physicalKey[physical] = Data.LogicalTrackPresence[Data.PhysicalToLogical[physical]] ? key[physical] : _physicalReference[physical];
        _expansion.Expand(_physicalKey, output, _components, Data.GetVirtualKey(index), Data.VirtualTrackPresence);
    }

    /// <summary>Matches GetBonePose's seconds entry, FEvaluationContext's frame-time
    /// round trip, then GetKeyIndicesFromTime and AnimDataModel's interpolation gates.
    /// Negative times select the first key; times past the end select the last without wrapping.</summary>
    public static AlsRawPoseKeySelection SelectKeys(AlsRawAnimationPoseData data, double seconds,
        AlsRawFrameTimeRounding rounding = AlsRawFrameTimeRounding.OptimizedCancellation)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!double.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (rounding is not (AlsRawFrameTimeRounding.OptimizedCancellation or AlsRawFrameTimeRounding.RoundSubframe))
            throw new ArgumentOutOfRangeException(nameof(rounding));
        var numerator = data.FrameRateNumerator; var denominator = data.FrameRateDenominator;
        var framePosition = (seconds * numerator) / denominator;
        if (!double.IsFinite(framePosition))
            throw new ArgumentOutOfRangeException(nameof(seconds), "Raw sample time overflows the native frame-time conversion.");
        var contextTime = AsFrameTime(framePosition, rounding);
        var sampleSeconds = ((long)contextTime.Frame * denominator + contextTime.Subframe * (double)denominator) / numerator;
        if (sampleSeconds <= 0 || data.SampledKeyCount == 1) return new(0, 0, 0, false, sampleSeconds);
        var keyTime = AsFrameTime((sampleSeconds * numerator) / denominator, rounding);
        if (keyTime.Frame >= data.SampledKeyCount - 1)
            return new(data.SampledKeyCount - 1, 0, 0, false, sampleSeconds);
        var first = ScalarMath.Clamp(keyTime.Frame, 0, data.SampledKeyCount - 1);
        var second = ScalarMath.Min(first + 1, data.SampledKeyCount - 1);
        var alpha = data.Interpolation == AlsRawAnimationInterpolation.Step ? 0 : keyTime.Subframe;
        if (alpha < KeySnapThreshold) return new(first, second, 0, false, sampleSeconds);
        if (alpha > 1f - KeySnapThreshold) return new(second, second, alpha, false, sampleSeconds);
        return new(first, second, alpha, true, sampleSeconds);
    }

    private static (int Frame, float Subframe) AsFrameTime(double framePosition, AlsRawFrameTimeRounding rounding)
        => AlsAnimationFrameTime.FromFramePosition(framePosition, rounding);

    /// <summary>UE's vectorized FTransform::Blend for alpha in [0,1]. Linear translation
    /// and scale; shortest-path quaternion lerp followed by normalization. Negative dot
    /// flips the first quaternion, preserving UE's second-input sign convention.</summary>
    public static AlsLocalPose BlendTransform(in AlsLocalPose first, in AlsLocalPose second, float alpha)
    {
        if (!float.IsFinite(alpha) || alpha < 0 || alpha > 1) throw new ArgumentOutOfRangeException(nameof(alpha));
        // Generated virtual rotations follow UE's general transform normalization check;
        // their input raw-track norm errors can accumulate before this blend normalizes.
        AlsRawAnimationPoseData.ValidatePose(first, .01); AlsRawAnimationPoseData.ValidatePose(second, .01);
        if (MathF.Abs(alpha) <= AlsPoseBlender.WeightThreshold) return first;
        if (MathF.Abs(alpha - 1) <= AlsPoseBlender.WeightThreshold) return second;
        var a = first.Rotation; var b = second.Rotation;
        var dot = (double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z + (double)a.W * b.W;
        var firstWeight = (dot >= 0 ? 1.0 : -1.0) * (1.0 - alpha);
        var x = b.X * (double)alpha + a.X * firstWeight;
        var y = b.Y * (double)alpha + a.Y * firstWeight;
        var z = b.Z * (double)alpha + a.Z * firstWeight;
        var w = b.W * (double)alpha + a.W * firstWeight;
        var reciprocalLength = 1.0 / ScalarMath.Sqrt(x * x + y * y + z * z + w * w);
        return new(Lerp(first.Position, second.Position, alpha),
            new Quaternion((float)(x * reciprocalLength), (float)(y * reciprocalLength), (float)(z * reciprocalLength), (float)(w * reciprocalLength)),
            Lerp(first.Scale, second.Scale, alpha));
    }

    private static Vector3 Lerp(Vector3 first, Vector3 second, float alpha) => new(
        (float)(first.X * (1.0 - alpha) + second.X * (double)alpha),
        (float)(first.Y * (1.0 - alpha) + second.Y * (double)alpha),
        (float)(first.Z * (1.0 - alpha) + second.Z * (double)alpha));
}
