using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// One owner's sequence sampling scratch, using immutable resource data shared across
// players. No engine API, player clock, sync event or final-pose history is owned here.
internal sealed class AlsRawAnimationSourceSampler
{
    private readonly AlsRawSequencePoseSampler _withRetargetReference, _withoutRetargetReference;
    private readonly AlsRawPoseRetargetModel _retarget;
    private readonly AlsCurveSampler _curves;
    private readonly int[] _curveIds;
    private readonly AlsRawAnimationSkeletonDefinition _skeleton;
    public AlsRawAnimationSourceDefinition Definition { get; }

    public AlsRawAnimationSourceSampler(AlsRawAnimationSourceDefinition source,
        AlsRawAnimationSkeletonDefinition skeleton, AlsAnimationSetDefinition set, ReadOnlySpan<string> curveNames)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(skeleton);
        var data = source.PoseData; var policy = source.Policy;
        if (data.Identity.SkeletonId != skeleton.SkeletonId || data.LogicalBoneCount != skeleton.LogicalBoneCount ||
            policy.TransformCurveCount != 0 || policy.AnimatedBoneAttributeCount != 0)
            throw new ArgumentException("Raw source sampler requires its supported full skeleton and source policy.");
        var animation = set.Animations[data.Identity.AnimationId];
        if (animation.StableId != data.Identity.AssetId || animation.ObjectPath != data.Identity.AssetPath)
            throw new ArgumentException("Raw source sampler asset identity differs from its manifest.");
        _skeleton = skeleton; Definition = source;
        _withRetargetReference = new(data, skeleton.LogicalParents, skeleton.ReferencePose, skeleton.VirtualBones);
        var authoredReference = skeleton.ReferencePose.ToArray();
        // UE's retarget-disabled extraction starts from the authored source reference;
        // stale/missing virtual entries fall back to the actual target skeleton reference.
        policy.RetargetTransforms.CopyTo(authoredReference.AsSpan(0, policy.RetargetTransforms.Length));
        _withoutRetargetReference = new(data, skeleton.LogicalParents, authoredReference, skeleton.VirtualBones);
        _retarget = new(skeleton.LogicalToPhysical, skeleton.TranslationRetargetModes.ToArray().Select(m => (int)m).ToArray(),
            data.LogicalTrackPresence, skeleton.ReferencePose, !policy.RetargetTransforms.IsEmpty);
        var curves = animation.Curves.Where(c => c.Provenance == AlsCurveProvenance.SourceCurve).ToArray();
        if (curves.Any(c => c.PreInfinity != AlsCurveInfinityMode.Constant || c.PostInfinity != AlsCurveInfinityMode.Constant))
            throw new ArgumentException("Raw source curve sampling requires the authored constant infinity policy.");
        _curves = new(curves);
        var names = curveNames.ToArray();
        if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            throw new ArgumentException("Raw source curve output requires unique FName identities.");
        _curveIds = new int[names.Length]; Array.Fill(_curveIds, -1);
        foreach (var curve in curves)
        {
            var index = Array.FindIndex(names, n => n.Equals(curve.SourceName, StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw new ArgumentException("Raw output layout omits an authored source curve.");
            _curveIds[index] = curve.CurveId;
        }
        AlsRefactoredV4SourceCurves.Bind(animation.ObjectPath, curves, names, _curveIds);
    }

    public AlsRawPoseKeySelection Sample(double seconds, bool shouldRetarget, bool extractRootMotion,
        bool ignoreRootLock, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves)
    {
        if (pose.Length != _skeleton.LogicalBoneCount || curves.Length != _curveIds.Length)
            throw new ArgumentException("Raw source output layout differs.");
        var selection = (shouldRetarget ? _withRetargetReference : _withoutRetargetReference).Sample(seconds, pose);
        _retarget.Apply(pose, shouldRetarget);
        var policy = Definition.Policy;
        pose[0] = AlsRawRootLock.Apply(pose[0], _skeleton.ReferencePose[0], policy.RootLockFirstFrame,
            (int)policy.RootMotionRootLock, policy.EnableRootMotion, policy.ForceRootLock, extractRootMotion, ignoreRootLock);
        curves.Clear();
        for (var index = 0; index < curves.Length; index++)
            if (_curveIds[index] >= 0)
            {
                if (!_curves.TrySample(_curveIds[index], (float)selection.SampleTimeSeconds, out var value))
                    throw new InvalidOperationException("Raw authored source curve could not be sampled.");
                curves[index] = new(value);
            }
        return selection;
    }
}
