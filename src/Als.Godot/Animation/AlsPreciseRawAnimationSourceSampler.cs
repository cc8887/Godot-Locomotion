using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

/// <summary>Precise raw GetBonePose entry, before additive conversion. No playback clock is owned here.</summary>
internal sealed class AlsPreciseRawAnimationSourceSampler
{
    private readonly AlsPreciseRawSequenceSampler _withReference, _withoutReference;
    private readonly AlsRawAnimationSourceDefinition _source;
    private readonly AlsRawAnimationSkeletonDefinition _skeleton;
    private readonly int[] _curveIds;
    private readonly AlsPrecisePoseRetargetModel _retarget;
    private readonly AlsCurveSampler _curves;

    public AlsPreciseRawAnimationSourceSampler(AlsRawAnimationSourceDefinition source,
        AlsRawAnimationSkeletonDefinition skeleton, AlsAnimationSetDefinition set, ReadOnlySpan<string> curveNames)
    {
        var data = source.PoseData; var policy = source.Policy;
        if (data.Identity.SkeletonId != skeleton.SkeletonId || data.LogicalBoneCount != skeleton.LogicalBoneCount ||
            policy.TransformCurveCount != 0 || policy.AnimatedBoneAttributeCount != 0)
            throw new ArgumentException("Precise source requires its full supported source policy and logical skeleton.");
        _source = source; _skeleton = skeleton;
        _withReference = new(data, skeleton.LogicalParents, skeleton.PreciseReferencePose, skeleton.VirtualBones);
        var authored = skeleton.PreciseReferencePose.ToArray(); policy.PreciseRetargetTransforms.CopyTo(authored);
        _withoutReference = new(data, skeleton.LogicalParents, authored, skeleton.VirtualBones);
        _retarget = new(skeleton.LogicalToPhysical,skeleton.TranslationRetargetModes.ToArray().Select(m=>(int)m).ToArray(),
            data.LogicalTrackPresence,skeleton.PreciseReferencePose,policy.PreciseRetargetTransforms,100);
        var animation = set.Animations[data.Identity.AnimationId];
        if (animation.StableId != data.Identity.AssetId || animation.ObjectPath != data.Identity.AssetPath)
            throw new ArgumentException("Precise raw source identity differs from its manifest.");
        var curves = animation.Curves.Where(c => c.Provenance == AlsCurveProvenance.SourceCurve).ToArray();
        if (curves.Any(c => c.PreInfinity != AlsCurveInfinityMode.Constant || c.PostInfinity != AlsCurveInfinityMode.Constant))
            throw new ArgumentException("Precise raw curves require the authored constant infinity policy.");
        var names = curveNames.ToArray();
        if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            throw new ArgumentException("Precise curve layout requires unique names.");
        _curves = new(curves); _curveIds = new int[names.Length]; Array.Fill(_curveIds, -1);
        foreach (var curve in curves)
        {
            var id = Array.FindIndex(names, n => n.Equals(curve.SourceName, StringComparison.OrdinalIgnoreCase));
            if (id < 0) throw new ArgumentException("Precise output omits an authored curve.");
            _curveIds[id] = curve.CurveId;
        }
        AlsRefactoredV4SourceCurves.Bind(animation.ObjectPath, curves, names, _curveIds);
    }
    public AlsRawPoseKeySelection Sample(double seconds, bool shouldRetarget, bool extractRootMotion,
        bool ignoreRootLock, Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves)
    {
        if (pose.Length != _skeleton.LogicalBoneCount || curves.Length != _curveIds.Length)
            throw new ArgumentException("Precise raw output layout differs.");
        var key = (shouldRetarget ? _withReference : _withoutReference).Sample(seconds, pose);
        var policy = _source.Policy;
        _retarget.Apply(pose,shouldRetarget);
        if (extractRootMotion && policy.EnableRootMotion || policy.ForceRootLock && !ignoreRootLock)
            pose[0] = policy.RootMotionRootLock switch
            {
                AlsRawAnimationRootLock.RefPose => _skeleton.PreciseReferencePose[0],
                AlsRawAnimationRootLock.AnimFirstFrame => policy.PreciseRootLockFirstFrame,
                AlsRawAnimationRootLock.Zero => AlsPrecisePose.Identity,
                _ => throw new ArgumentException("Unsupported precise root lock.")
            };
        curves.Clear();
        for (var index = 0; index < curves.Length; index++)
            if (_curveIds[index] >= 0)
            {
                if (!_curves.TrySample(_curveIds[index], (float)key.SampleTimeSeconds, out var value)) throw new InvalidOperationException("Missing precise source curve.");
                curves[index] = new(value);
            }
        return key;
    }
}
