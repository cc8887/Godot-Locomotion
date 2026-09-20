using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// Consumes the same frozen physical montage frame as the enclosing BaseLayer.
// It has sampling scratch only, no independent playback time or committed state.
internal sealed class AlsGroundedMontageSlot : IAlsGroundedSlotPoseSink, IAlsMontagePoseSource
{
    private readonly Dictionary<int, (int Additive, AlsPreciseAnimationPoseSourceSampler Sampler)> _sources;
    private readonly AlsMontageSlotPose _pose;
    private readonly AlsPrecisePose[] _input, _output;
    private AlsMontageFrame? _frame;
    private AlsFrameIdentity _identity;
    public int SampleCount { get; private set; }
    public int ChangedBones { get; private set; }
    public AlsGroundedMontageSlot(AlsRawAnimationSourceBank bank, AlsAnimationSetDefinition set,
        ReadOnlySpan<AlsSequenceMontageAsset> assets, ReadOnlySpan<string> curveNames, AlsRawAnimationSourceBank? stopBank = null)
    {
        if (assets.IsEmpty) throw new ArgumentException("Grounded Slot requires its sequence assets.");
        var skeleton = bank.GetSkeleton(set.Animations[assets[0].AnimationId].SkeletonId);
        _pose = new(skeleton.PreciseReferencePose, skeleton.LogicalParents, curveNames.Length);
        _input = new AlsPrecisePose[skeleton.LogicalBoneCount]; _output = new AlsPrecisePose[_input.Length];
        _sources = new();
        foreach (var asset in assets)
        {
            var owner = stopBank is not null && stopBank.RootAnimationIds.Contains(asset.AnimationId) ? stopBank : bank;
            var source = owner.GetSource(asset.AnimationId);
            if (asset.Slot != AlsMontageSlot.Grounded || source.PoseData.Identity.SkeletonId != skeleton.SkeletonId ||
                asset.AdditiveType != (int)source.Policy.AdditiveType)
                throw new ArgumentException("Grounded Slot asset and raw sampling policy differ.");
            _sources.Add(asset.AnimationId, (asset.AdditiveType, new(source, owner, set, curveNames)));
        }
    }
    public void Prepare(AlsMontageFrame frame, in AlsFrameIdentity identity)
    {
        if (frame.Identity != identity || identity.SlotGeneration == 0) throw new ArgumentException("Stale Grounded Slot montage frame.");
        _frame = frame; _identity = identity; ChangedBones = 0;
    }
    public void EvaluateSlot(in AlsSlotWeights weights, ReadOnlySpan<AlsLocalPose> source,
        ReadOnlySpan<AlsInertialCurve> sourceCurves, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves)
    {
        if (_frame is null || _frame.SlotWeights(AlsMontageSlot.Grounded) != weights || bones.Length != _output.Length ||
            !source.IsEmpty && source.Length != bones.Length) throw new ArgumentException("Grounded Slot frame/pose/weights differ.");
        for (var bone = 0; bone < source.Length; bone++) _input[bone] = new(source[bone]);
        _pose.Evaluate(_frame, _identity, AlsMontageSlot.Grounded, source.IsEmpty ? [] : _input, sourceCurves, _output, curves, this);
        ChangedBones = 0;
        for (var bone = 0; bone < bones.Length; bone++)
        {
            bones[bone] = _output[bone].ToSingle();
            if (!source.IsEmpty && bones[bone] != _input[bone].ToSingle()) ChangedBones++;
        }
    }
    public void Sample(in AlsMontageEvaluation entry, Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves)
    {
        if (!_sources.TryGetValue(entry.AnimationId, out var source) || source.Additive != entry.AdditiveType)
            throw new ArgumentException("Unbound Grounded Slot montage source.");
        source.Sampler.Sample(entry.Position, true, false, false, pose, curves); SampleCount++;
    }


    public void EvaluateSlot(in AlsSlotWeights weights, ReadOnlySpan<AlsPrecisePose> source,
        ReadOnlySpan<AlsInertialCurve> sourceCurves, Span<AlsPrecisePose> bones, Span<AlsInertialCurve> curves)
    {
        if (_frame is null || _frame.SlotWeights(AlsMontageSlot.Grounded) != weights || bones.Length != _output.Length ||
            !source.IsEmpty && source.Length != bones.Length) throw new ArgumentException("Grounded Slot frame/pose/weights differ.");
        source.CopyTo(_input);
        _pose.Evaluate(_frame, _identity, AlsMontageSlot.Grounded, source.IsEmpty ? [] : _input, sourceCurves, _output, curves, this);
        ChangedBones = 0;
        for (var bone = 0; bone < bones.Length; bone++)
        {
            bones[bone] = _output[bone];
            if (!source.IsEmpty && bones[bone] != _input[bone]) ChangedBones++;
        }
    }

}
