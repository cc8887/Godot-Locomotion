using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// Per-rig layout and source factories. The bank is shared by all characters; the
// physical Skeleton3D is touched only during construction and final pose writeback.
internal sealed class AlsMovementPoseSources
{
    private readonly AlsAnimationSetDefinition _set;
    private readonly Dictionary<string, int> _bones;
    private readonly string[] _curveNames;
    public AlsRawAnimationSourceBank Bank { get; }
    public AlsRawAnimationSkeletonDefinition Skeleton { get; }
    public ReadOnlySpan<AlsLocalPose> ReferencePose => Skeleton.ReferencePose;
    public ReadOnlySpan<AlsPrecisePose> PreciseReferencePose => Skeleton.PreciseReferencePose;
    public ReadOnlySpan<int> Parents => Skeleton.LogicalParents;
    public int[] GodotToLogical { get; }
    public int BoneCount => Skeleton.LogicalBoneCount;

    public AlsMovementPoseSources(AlsAnimationSetDefinition set, AlsRawAnimationSourceBank bank, Skeleton3D physical)
    {
        if (bank.DefinitionDigest != set.DefinitionDigest || bank.Skeletons.Length != 1)
            throw new ArgumentException("Movement source bank differs from its animation set.");
        _set = set; Bank = bank; Skeleton = bank.Skeletons[0];
        AlsAnimationBinder.ValidateTargetSkeleton(physical, set.Skeletons[Skeleton.SkeletonId], "Movement logical pose");
        _bones = Skeleton.LogicalBoneNames.ToArray().Select((name, index) => (name, index))
            .ToDictionary(p => p.name, p => p.index, StringComparer.OrdinalIgnoreCase);
        if (physical.GetBoneCount() != Skeleton.PhysicalBoneCount)
            throw new ArgumentException("Movement physical bone count differs.");
        GodotToLogical = new int[physical.GetBoneCount()];
        for (var bone = 0; bone < GodotToLogical.Length; bone++)
        {
            var logical = Bone(physical.GetBoneName(bone));
            if (Skeleton.LogicalToPhysical[logical] < 0) throw new ArgumentException("A virtual bone was bound to the mesh.");
            GodotToLogical[bone] = logical;
        }
        if (GodotToLogical.Distinct().Count() != GodotToLogical.Length)
            throw new ArgumentException("Movement physical mapping is aliased.");
        for (var bone = 0; bone < GodotToLogical.Length; bone++)
        {
            var parent = physical.GetBoneParent(bone);
            if (Parents[GodotToLogical[bone]] != (parent < 0 ? -1 : GodotToLogical[parent]))
                throw new ArgumentException("Movement physical parent mapping differs.");
        }
        _curveNames = bank.Sources.ToArray().SelectMany(s => s.Policy.FloatCurveNames.ToArray())
            .Concat(OS.GetCmdlineUserArgs().Contains("--refactored-pose-curves") || OS.GetCmdlineUserArgs().Contains("--refactored-state-curves")
                ? AlsRefactoredV4SourceCurves.TargetNames : [])
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
    }

    public int Bone(string name) => _bones.TryGetValue(name, out var bone) ? bone :
        throw new ArgumentException("Unknown movement logical bone: " + name);
    public AlsMovementAnimationSource Create(int animationId, bool rawBonePose = false) =>
        new(Bank.GetSource(animationId), Bank, _set, _curveNames, rawBonePose);
}

// One resource sampler's pure last-time cache. It does not contain a playback
// identity, advance time, emit events or retain blend/transition history.
internal sealed class AlsMovementAnimationSource : IDisposable
{
    private delegate AlsRawPoseKeySelection SampleSource(double seconds, bool retarget, bool extractRoot,
        bool ignoreRootLock, Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves);
    private readonly SampleSource _sample;
    private readonly Dictionary<string, int> _curveIndices;
    private readonly AlsLocalPose[] _pose;
    private readonly AlsPrecisePose[] _precisePose;
    private readonly AlsInertialCurve[] _curves;
    private double _time = double.NaN;
    private bool _disposed;
    public AlsRawAnimationSourceDefinition Definition { get; }
    public double Length => Definition.Policy.SequencePlayLength;

    public AlsMovementAnimationSource(AlsRawAnimationSourceDefinition definition, AlsRawAnimationSourceBank bank,
        AlsAnimationSetDefinition set, string[] curveNames, bool rawBonePose)
    {
        Definition = definition;
        _sample = rawBonePose
            ? new AlsPreciseRawAnimationSourceSampler(definition, bank.GetSkeleton(definition.PoseData.Identity.SkeletonId), set, curveNames).Sample
            : new AlsPreciseAnimationPoseSourceSampler(definition, bank, set, curveNames).Sample;
        _pose = new AlsLocalPose[definition.PoseData.LogicalBoneCount]; _curves = new AlsInertialCurve[curveNames.Length];
        _precisePose = new AlsPrecisePose[_pose.Length];
        _curveIndices = curveNames.Select((name, index) => (name, index)).ToDictionary(p => p.name, p => p.index, StringComparer.OrdinalIgnoreCase);
    }

    public void Sample(ReadOnlySpan<AlsLocalPose> reference, double seconds, Span<AlsLocalPose> output)
    {
        if (reference.Length != _pose.Length || output.Length != _pose.Length)
            throw new ArgumentException("Movement source requires a complete logical output.");
        EnsureSample(seconds); _pose.CopyTo(output);
    }

    public void SampleSourceSeconds(ReadOnlySpan<AlsLocalPose> reference, float seconds, float sourceDuration, Span<AlsLocalPose> output)
    {
        if (!float.IsFinite(sourceDuration) || sourceDuration <= 0 || sourceDuration != (float)Length ||
            !float.IsFinite(seconds) || seconds < 0 || seconds > sourceDuration)
            throw new ArgumentException("Movement source clock duration differs from its asset.");
        Sample(reference, seconds, output);
    }


    public void Sample(ReadOnlySpan<AlsPrecisePose> reference, double seconds, Span<AlsPrecisePose> output)
    {
        if (reference.Length != _precisePose.Length) throw new ArgumentException("Incomplete precise reference pose.");
        SamplePrecise(seconds, output);
    }

    public void SampleSourceSeconds(ReadOnlySpan<AlsPrecisePose> reference, float seconds, float sourceDuration, Span<AlsPrecisePose> output)
    {
        if (!float.IsFinite(sourceDuration) || sourceDuration <= 0 || sourceDuration != (float)Length ||
            !float.IsFinite(seconds) || seconds < 0 || seconds > sourceDuration)
            throw new ArgumentException("Movement source clock duration differs from its asset.");
        Sample(reference, seconds, output);
    }

    public void SamplePrecise(double seconds, Span<AlsPrecisePose> output)
    {
        if (output.Length != _precisePose.Length) throw new ArgumentException("Incomplete precise movement source output.");
        EnsureSample(seconds); _precisePose.CopyTo(output);
    }

    public AlsInertialCurve Curve(double seconds, string name)
    {
        EnsureSample(seconds);
        return _curveIndices.TryGetValue(name, out var index) ? _curves[index] : default;
    }

    private void EnsureSample(double seconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(seconds)) throw new ArgumentException("Movement source time is not finite.");
        // GetAnimationPose accepts a double extraction time; native raw key selection
        // clamps keys at its own DataModel endpoints. Do not clamp/round this time to
        // SequencePlayLength (a float-backed value). Playback owners validate clocks.
        if (BitConverter.DoubleToInt64Bits(seconds) == BitConverter.DoubleToInt64Bits(_time)) return;
        _time = double.NaN;
        _sample(seconds, true, false, false, _precisePose, _curves);
        for (var b=0;b<_pose.Length;b++) _pose[b]=_precisePose[b].ToSingle();
        _time = seconds;
    }
    public void Dispose() => _disposed = true;
}
