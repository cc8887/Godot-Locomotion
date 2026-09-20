using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

internal sealed class AlsPreciseAnimationPoseSourceSampler
{
    private readonly AlsPreciseRawAnimationSourceSampler _target;
    private readonly AlsPreciseRawAnimationSourceSampler? _reference;
    private readonly AlsRawAnimationSourceDefinition _source, _baseSource;
    private readonly AlsRawAnimationSkeletonDefinition _skeleton;
    private readonly AlsPrecisePose[] _basePose;
    private readonly AlsInertialCurve[] _baseCurves;
    private readonly AlsQuaternion[] _rotations;
    private readonly bool _additive;
    private readonly int _curveCount;
    private int _sampling;

    public AlsPreciseAnimationPoseSourceSampler(AlsRawAnimationSourceDefinition source, AlsRawAnimationSourceBank bank,
        AlsAnimationSetDefinition set, ReadOnlySpan<string> curveNames)
    {
        if (!ReferenceEquals(bank.GetSource(source.PoseData.Identity.AnimationId), source)) throw new ArgumentException("Unverified precise source.");
        _source = source; _skeleton = bank.GetSkeleton(source.PoseData.Identity.SkeletonId); _curveCount = curveNames.Length;
        _target = new(source, _skeleton, set, curveNames);
        var policy = source.Policy;
        _additive = policy.AdditiveType != AlsRawAnimationAdditiveType.None && policy.BasePoseType switch
        {
            AlsRawAnimationBasePoseType.RefPose => true,
            AlsRawAnimationBasePoseType.AnimScaled => policy.BaseAnimationId >= 0,
            AlsRawAnimationBasePoseType.AnimFrame => policy.BaseAnimationId >= 0 && policy.BaseFrame >= 0,
            AlsRawAnimationBasePoseType.LocalAnimFrame => policy.BaseFrame >= 0,
            _ => false
        };
        _baseSource = source;
        _basePose = _additive ? new AlsPrecisePose[_skeleton.LogicalBoneCount] : [];
        _baseCurves = _additive ? new AlsInertialCurve[curveNames.Length] : [];
        _rotations = _additive && policy.AdditiveType == AlsRawAnimationAdditiveType.RotationOffsetMeshSpace ? new AlsQuaternion[_skeleton.LogicalBoneCount * 2] : [];
        if (_additive && policy.BasePoseType != AlsRawAnimationBasePoseType.RefPose)
        {
            _baseSource = policy.BasePoseType == AlsRawAnimationBasePoseType.LocalAnimFrame ? source : bank.GetSource(policy.BaseAnimationId);
            if (_baseSource.PoseData.Identity.SkeletonId != _skeleton.SkeletonId) throw new ArgumentException("Precise additive source skeleton differs.");
            _reference = new(_baseSource, _skeleton, set, curveNames);
        }
    }
    public AlsRawPoseKeySelection Sample(double seconds, bool retarget, bool extract, bool ignoreLock,
        Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves)
    {
        if (pose.Length != _skeleton.LogicalBoneCount || curves.Length != _curveCount) throw new ArgumentException("Precise animation output layout differs.");
        if (Interlocked.CompareExchange(ref _sampling, 1, 0) != 0) throw new InvalidOperationException("Precise additive scratch is already in use.");
        try
        {
            var keys = _target.Sample(seconds, retarget, extract, ignoreLock, pose, curves);
            if (!_additive) return keys;
            var policy = _source.Policy;
            if (_reference is null) { _skeleton.PreciseReferencePose.CopyTo(_basePose); _baseCurves.AsSpan().Clear(); }
            else
            {
                var time = AlsAdditiveReferenceTime.Resolve((AlsAdditiveReferenceKind)(int)policy.BasePoseType,
                    seconds, policy.SequencePlayLength, _baseSource.Policy.SequencePlayLength, _baseSource.PoseData.SampledKeyCount, policy.BaseFrame);
                _reference.Sample(time, retarget, extract, ignoreLock, _basePose, _baseCurves);
            }
            if (policy.AdditiveType == AlsRawAnimationAdditiveType.RotationOffsetMeshSpace)
                AlsPrecisePoseBlender.MeshDifference(pose, _basePose, _skeleton.LogicalParents, _rotations, pose);
            else for (var bone = 0; bone < pose.Length; bone++) pose[bone] = AlsPrecisePoseBlender.LocalDifference(pose[bone], _basePose[bone]);
            AlsLayeringCurves.Difference(curves, _baseCurves, curves); return keys;
        }
        finally { Volatile.Write(ref _sampling, 0); }
    }
}
