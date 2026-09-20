using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// Actual fixed-time BasePoses source data. Node lifecycle and tick history belong to Core,
// not to the AnimationPlayer that also contains these assets for the locomotion idle nodes.
internal sealed class AlsBasePosesSourceSampler : IAlsBasePosesSink
{
    private readonly AlsBasePoseEvaluatorDefinition[] _evaluators;
    private readonly AlsSourcePoseKeyTable[] _sourceKeys;
    private readonly AlsCurveSampler[] _curves = new AlsCurveSampler[2];
    private readonly int[][] _curveIds = new int[2][];
    private readonly AlsBasePoseRetargetModel[] _retarget = new AlsBasePoseRetargetModel[2];
    private readonly AlsLocalPose[] _components;
    private readonly int _curveCount;
    public AlsLogicalPoseLayout Layout { get; }

    public AlsBasePosesSourceSampler(AlsBasePosesDefinition definition, AlsAnimationSetDefinition set,
        AlsAnimationLibraryBuildResult library, ReadOnlySpan<string> curveNames,
        ReadOnlySpan<AlsBasePoseRetargetDefinition> retarget, ReadOnlySpan<AlsSourcePoseKeyTable> sourceKeys)
    {
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(set);
        _evaluators = definition.Evaluators.ToArray(); _curveCount = curveNames.Length;
        if (retarget.Length != _evaluators.Length) throw new ArgumentException("Missing BasePoses retarget definitions.");
        if (sourceKeys.Length != _evaluators.Length) throw new ArgumentException("Missing BasePoses raw source keys.");
        _sourceKeys = sourceKeys.ToArray();
        Layout = new(set.Skeletons[definition.SkeletonId], library.Skeleton);
        _components = new AlsLocalPose[Layout.ReferencePose.Length];
        for (var source = 0; source < _evaluators.Length; source++)
        {
            var evaluator = _evaluators[source]; var animation = set.Animations[evaluator.AnimationId];
            if (animation.StableId != evaluator.AssetId || animation.ObjectPath != evaluator.AssetPath ||
                animation.SkeletonId != definition.SkeletonId || animation.PlayLength != evaluator.Length ||
                animation.AdditiveType != 0 || evaluator.ExplicitTime != 0)
                throw new ArgumentException("BasePoses sampler asset differs from the compiled evaluator.");
            if (retarget[source].Evaluator != evaluator) throw new ArgumentException("BasePoses retarget evaluator identity differs.");
            if (_sourceKeys[source] is null || _sourceKeys[source].Evaluator != evaluator ||
                _sourceKeys[source].PhysicalBoneCount != Layout.PhysicalReferencePose.Length)
                throw new ArgumentException("BasePoses raw key identity or bone layout differs.");
            _retarget[source] = new(retarget[source], Layout.ReferencePose);
            var curves = animation.Curves; _curves[source] = new(curves);
            var mapping = _curveIds[source] = new int[curveNames.Length]; Array.Fill(mapping, -1);
            foreach (var curve in curves)
            {
                var index = curveNames.IndexOf(curve.SourceName);
                if (index < 0) throw new ArgumentException("BasePoses output layout omits an authored curve.");
                mapping[index] = curve.CurveId;
            }
        }
    }

    public void InitializeEvaluator(in AlsBasePoseEvaluatorDefinition evaluator) => _ = Resolve(evaluator);
    public void CacheEvaluatorBones(in AlsBasePoseEvaluatorDefinition evaluator) => _ = Resolve(evaluator);
    public void UpdateEvaluator(in AlsBasePoseEvaluatorDefinition evaluator, in AlsBasePoseEvaluatorTick tick,
        in AlsPoseUpdateContext context)
    {
        _ = Resolve(evaluator);
        if (tick.Identity != context.Identity || tick.NodeIndex != evaluator.NodeIndex ||
            tick.AnimationId != evaluator.AnimationId || tick.CurrentTime != 0 || tick.DeltaTime != 0 || tick.PlayRate != 0)
            throw new ArgumentException("BasePoses evaluator tick is not its independent fixed-time source.");
    }

    public void EvaluateEvaluator(in AlsBasePoseEvaluatorDefinition evaluator, float seconds,
        Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves)
    {
        var source = Resolve(evaluator);
        if (seconds != 0 || pose.Length != Layout.ReferencePose.Length || curves.Length != _curveCount)
            throw new ArgumentException("BasePoses fixed-key output layout or sample time differs.");
        SampleKeyBeforeRetarget(evaluator, pose);
        _retarget[source].Apply(pose);
        curves.Clear();
        for (var curve = 0; curve < curves.Length; curve++)
            if (_curveIds[source][curve] >= 0)
            {
                if (!_curves[source].TrySample(_curveIds[source][curve], seconds, out var value))
                    throw new InvalidOperationException("Authored BasePoses curve could not be sampled.");
                curves[curve] = new(value);
            }
    }

    internal void SampleKeyBeforeRetarget(in AlsBasePoseEvaluatorDefinition evaluator, Span<AlsLocalPose> pose)
    {
        var source = Resolve(evaluator);
        // The authored quaternion keys bypass the FBX Euler conversion. Expand virtual
        // bones from the raw sampled key, then EvaluateEvaluator retargets physical bones.
        Layout.ExpandKey(_sourceKeys[source].GetKey(0), pose, _components);
    }

    private int Resolve(in AlsBasePoseEvaluatorDefinition evaluator)
    {
        for (var source = 0; source < _evaluators.Length; source++)
            if (evaluator == _evaluators[source]) return source;
        throw new ArgumentException("Foreign BasePoses evaluator identity.");
    }
}
