using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

/// <summary>GetAnimationPose on raw source data. Owns scratch only; player clocks,
/// relevance, sync, events and transaction histories remain with their existing owners.</summary>
internal sealed class AlsAnimationPoseSourceSampler
{
    private readonly AlsRawAnimationSourceSampler _target;
    private readonly AlsRawAnimationSourceSampler? _reference;
    private readonly AlsRawAnimationSkeletonDefinition _skeleton;
    private readonly AlsLocalPose[] _basePose;
    private readonly AlsInertialCurve[] _baseCurves;
    private readonly Quaternion[] _rotations;
    private readonly int _curveCount;
    private int _sampling;
    public AlsRawAnimationSourceDefinition Definition => _target.Definition;
    public bool IsAdditive { get; }

    public AlsAnimationPoseSourceSampler(AlsRawAnimationSourceDefinition source, AlsRawAnimationSourceBank bank,
        AlsAnimationSetDefinition set, ReadOnlySpan<string> curveNames)
    {
        if (!ReferenceEquals(bank.GetSource(source.PoseData.Identity.AnimationId), source))
            throw new ArgumentException("Animation sampling requires its verified immutable source bank.");
        _skeleton = bank.GetSkeleton(source.PoseData.Identity.SkeletonId);
        _target = new(source, _skeleton, set, curveNames); _curveCount = curveNames.Length;
        var policy = source.Policy;
        IsAdditive = policy.AdditiveType != AlsRawAnimationAdditiveType.None && policy.BasePoseType switch
        {
            AlsRawAnimationBasePoseType.RefPose => true,
            AlsRawAnimationBasePoseType.AnimScaled => policy.BaseAnimationId >= 0,
            AlsRawAnimationBasePoseType.AnimFrame => policy.BaseAnimationId >= 0 && policy.BaseFrame >= 0,
            AlsRawAnimationBasePoseType.LocalAnimFrame => policy.BaseFrame >= 0,
            _ => false,
        };
        _basePose = IsAdditive ? new AlsLocalPose[_skeleton.LogicalBoneCount] : [];
        _baseCurves = IsAdditive ? new AlsInertialCurve[curveNames.Length] : [];
        _rotations = IsAdditive && policy.AdditiveType == AlsRawAnimationAdditiveType.RotationOffsetMeshSpace
            ? new Quaternion[_skeleton.LogicalBoneCount * 2] : [];
        if (IsAdditive && policy.BasePoseType != AlsRawAnimationBasePoseType.RefPose)
        {
            var basis = policy.BasePoseType == AlsRawAnimationBasePoseType.LocalAnimFrame
                ? source : bank.GetSource(policy.BaseAnimationId);
            if (basis.PoseData.Identity.SkeletonId != _skeleton.SkeletonId)
                throw new ArgumentException("Cross-skeleton additive retargeting is not part of this source bank.");
            // Native base extraction calls GetBonePose, even if the base asset itself is additive.
            _reference = new(basis, _skeleton, set, curveNames);
        }
    }

    public AlsRawPoseKeySelection Sample(double seconds, bool shouldRetarget, bool extractRootMotion,
        bool ignoreRootLock, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves)
    {
        if (pose.Length != _skeleton.LogicalBoneCount || curves.Length != _curveCount)
            throw new ArgumentException("Animation pose output layout differs.");
        if (Interlocked.CompareExchange(ref _sampling, 1, 0) != 0)
            throw new InvalidOperationException("Animation sampler scratch is already in use by its owner.");
        try
        {
            var selected = _target.Sample(seconds, shouldRetarget, extractRootMotion, ignoreRootLock, pose, curves);
            if (!IsAdditive) return selected; // IsValidAdditive=false takes the ordinary GetBonePose path.
            var policy = Definition.Policy;
            if (_reference is null)
            {
                _skeleton.ReferencePose.CopyTo(_basePose); _baseCurves.AsSpan().Clear();
            }
            else
            {
                var reference = _reference.Definition;
                var time = AlsAdditiveReferenceTime.Resolve((AlsAdditiveReferenceKind)(int)policy.BasePoseType,
                    seconds, policy.SequencePlayLength, reference.Policy.SequencePlayLength,
                    reference.PoseData.SampledKeyCount, policy.BaseFrame);
                _reference.Sample(time, shouldRetarget, extractRootMotion, ignoreRootLock, _basePose, _baseCurves);
            }
            if (policy.AdditiveType == AlsRawAnimationAdditiveType.RotationOffsetMeshSpace)
                AlsMeshSpaceAdditivePose.Difference(pose, _basePose, _skeleton.LogicalParents, _rotations, pose);
            else
                for (var bone = 0; bone < pose.Length; bone++) pose[bone] = AlsLocalAdditivePose.Difference(pose[bone], _basePose[bone]);
            AlsLayeringCurves.Difference(curves, _baseCurves, curves);
            return selected;
        }
        finally { Volatile.Write(ref _sampling, 0); }
    }
}
