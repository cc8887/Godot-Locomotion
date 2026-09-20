using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

// Numeric values retain the native enums, including the deprecated non-additive ABPT_None.
public enum AlsRawAnimationAdditiveType { None = 0, LocalSpaceBase = 1, RotationOffsetMeshSpace = 2 }
public enum AlsRawAnimationBasePoseType { None = 0, RefPose = 1, AnimScaled = 2, AnimFrame = 3, LocalAnimFrame = 4 }
public enum AlsRawAnimationRootLock { RefPose = 0, AnimFirstFrame = 1, Zero = 2 }
public enum AlsRawAnimationRetargetMode { Animation = 0, Skeleton = 1, AnimationScaled = 2, AnimationRelative = 3, OrientAndScale = 4 }

/// <summary>Native extraction policy. All transforms use the same FBX bone basis as
/// PoseData; these are source/reference transforms, never evaluated animation poses.</summary>
public sealed class AlsRawAnimationEvaluationPolicy
{
    private readonly AlsLocalPose[] _retargetSourceAssetReferencePose, _retargetTransforms;
    private readonly AlsPrecisePose[] _preciseRetargetTransforms;
    private readonly string[] _floatCurveNames;
    public AlsRawAnimationInterpolation Interpolation { get; }
    public double SequencePlayLength { get; }
    public string RetargetSource { get; }
    public string? RetargetSourceAsset { get; }
    public ReadOnlySpan<AlsLocalPose> RetargetSourceAssetReferencePose => _retargetSourceAssetReferencePose;
    public string RetargetTransformsSourceName { get; }
    public ReadOnlySpan<AlsLocalPose> RetargetTransforms => _retargetTransforms;
    public ReadOnlySpan<AlsPrecisePose> PreciseRetargetTransforms => _preciseRetargetTransforms;
    public bool EnableRootMotion { get; }
    public bool ForceRootLock { get; }
    public AlsRawAnimationRootLock RootMotionRootLock { get; }
    public AlsLocalPose RootLockFirstFrame { get; }
    public AlsPrecisePose PreciseRootLockFirstFrame { get; }
    public bool UseNormalizedRootMotionScale { get; }
    public AlsRawAnimationAdditiveType AdditiveType { get; }
    public AlsRawAnimationBasePoseType BasePoseType { get; }
    public string? BaseAsset { get; }
    public int BaseAnimationId { get; }
    public int BaseFrame { get; }
    public int TransformCurveCount { get; }
    public int AnimatedBoneAttributeCount { get; }
    public int FloatCurveCount => _floatCurveNames.Length;
    public ReadOnlySpan<string> FloatCurveNames => _floatCurveNames;

    internal AlsRawAnimationEvaluationPolicy(AlsRawAnimationInterpolation interpolation, double sequencePlayLength, string retargetSource,
        string? retargetSourceAsset, ReadOnlySpan<AlsLocalPose> retargetSourceAssetReferencePose,
        string retargetTransformsSourceName, ReadOnlySpan<AlsLocalPose> retargetTransforms,
        bool enableRootMotion, bool forceRootLock, AlsRawAnimationRootLock rootMotionRootLock, AlsLocalPose rootLockFirstFrame,
        bool useNormalizedRootMotionScale, AlsRawAnimationAdditiveType additiveType,
        AlsRawAnimationBasePoseType basePoseType, string? baseAsset, int baseAnimationId, int baseFrame,
        int transformCurveCount, int animatedBoneAttributeCount, ReadOnlySpan<string> floatCurveNames,
        ReadOnlySpan<AlsPrecisePose> preciseRetargetTransforms, in AlsPrecisePose preciseRootLockFirstFrame)
    {
        Interpolation = interpolation; SequencePlayLength = sequencePlayLength;
        RetargetSource = retargetSource; RetargetSourceAsset = retargetSourceAsset;
        _retargetSourceAssetReferencePose = retargetSourceAssetReferencePose.ToArray();
        RetargetTransformsSourceName = retargetTransformsSourceName; _retargetTransforms = retargetTransforms.ToArray();
        EnableRootMotion = enableRootMotion; ForceRootLock = forceRootLock; RootMotionRootLock = rootMotionRootLock;
        RootLockFirstFrame = rootLockFirstFrame;
        _preciseRetargetTransforms = preciseRetargetTransforms.ToArray(); PreciseRootLockFirstFrame = preciseRootLockFirstFrame;
        UseNormalizedRootMotionScale = useNormalizedRootMotionScale; AdditiveType = additiveType;
        BasePoseType = basePoseType; BaseAsset = baseAsset; BaseAnimationId = baseAnimationId; BaseFrame = baseFrame;
        TransformCurveCount = transformCurveCount; AnimatedBoneAttributeCount = animatedBoneAttributeCount;
        _floatCurveNames = floatCurveNames.ToArray();
    }
}

public sealed class AlsRawAnimationSkeletonDefinition
{
    private readonly string[] _rawBoneNames, _logicalBoneNames;
    private readonly int[] _rawParents, _logicalParents, _logicalToPhysical, _physicalToLogical;
    private readonly AlsLogicalVirtualBone[] _virtualBones;
    private readonly AlsLocalPose[] _referencePose;
    private readonly AlsPrecisePose[] _preciseReferencePose;
    private readonly AlsRawAnimationRetargetMode[] _translationRetargetModes;
    public int SkeletonId { get; }
    public string AssetId { get; }
    public string Source { get; }
    public int PhysicalBoneCount => _rawBoneNames.Length;
    public int LogicalBoneCount => _logicalBoneNames.Length;
    public ReadOnlySpan<string> RawBoneNames => _rawBoneNames;
    public ReadOnlySpan<string> LogicalBoneNames => _logicalBoneNames;
    public ReadOnlySpan<int> RawParents => _rawParents;
    public ReadOnlySpan<int> LogicalParents => _logicalParents;
    public ReadOnlySpan<int> LogicalToPhysical => _logicalToPhysical;
    public ReadOnlySpan<int> PhysicalToLogical => _physicalToLogical;
    public ReadOnlySpan<AlsLogicalVirtualBone> VirtualBones => _virtualBones;
    public ReadOnlySpan<AlsLocalPose> ReferencePose => _referencePose;
    public ReadOnlySpan<AlsPrecisePose> PreciseReferencePose => _preciseReferencePose;
    public ReadOnlySpan<AlsRawAnimationRetargetMode> TranslationRetargetModes => _translationRetargetModes;

    internal AlsRawAnimationSkeletonDefinition(int skeletonId, string assetId, string source,
        ReadOnlySpan<string> rawBoneNames, ReadOnlySpan<int> rawParents, ReadOnlySpan<string> logicalBoneNames,
        ReadOnlySpan<int> logicalParents, ReadOnlySpan<int> logicalToPhysical, ReadOnlySpan<int> physicalToLogical,
        ReadOnlySpan<AlsLogicalVirtualBone> virtualBones, ReadOnlySpan<AlsLocalPose> referencePose,
        ReadOnlySpan<AlsRawAnimationRetargetMode> translationRetargetModes, ReadOnlySpan<AlsPrecisePose> preciseReferencePose)
    {
        SkeletonId = skeletonId; AssetId = assetId; Source = source;
        _rawBoneNames = rawBoneNames.ToArray(); _rawParents = rawParents.ToArray();
        _logicalBoneNames = logicalBoneNames.ToArray(); _logicalParents = logicalParents.ToArray();
        _logicalToPhysical = logicalToPhysical.ToArray(); _physicalToLogical = physicalToLogical.ToArray();
        _virtualBones = virtualBones.ToArray(); _referencePose = referencePose.ToArray();
        _translationRetargetModes = translationRetargetModes.ToArray();
        _preciseReferencePose = preciseReferencePose.ToArray();
    }
}

public sealed class AlsRawAnimationSourceDefinition
{
    public AlsRawAnimationPoseData PoseData { get; }
    public AlsRawAnimationEvaluationPolicy Policy { get; }
    public string SourceFile { get; }
    public string Sha256 { get; }
    internal AlsRawAnimationSourceDefinition(AlsRawAnimationPoseData poseData,
        AlsRawAnimationEvaluationPolicy policy, string sourceFile, string sha256)
    { PoseData = poseData; Policy = policy; SourceFile = sourceFile; Sha256 = sha256; }
}

/// <summary>Immutable, hash-verified resource closure. Player ownership stays in the graph runtime.</summary>
public sealed class AlsRawAnimationSourceBank
{
    private readonly AlsRawAnimationSkeletonDefinition[] _skeletons;
    private readonly AlsRawAnimationSourceDefinition[] _sources;
    private readonly int[] _rootAnimationIds;
    private readonly Dictionary<int, AlsRawAnimationSourceDefinition> _sourceById;
    private readonly Dictionary<int, AlsRawAnimationSkeletonDefinition> _skeletonById;
    public string DefinitionDigest { get; }
    public string BindingDigest { get; }
    public int PlayerCount { get; }
    public int SampleCount { get; }
    public ReadOnlySpan<int> RootAnimationIds => _rootAnimationIds;
    public ReadOnlySpan<AlsRawAnimationSkeletonDefinition> Skeletons => _skeletons;
    public ReadOnlySpan<AlsRawAnimationSourceDefinition> Sources => _sources;

    internal AlsRawAnimationSourceBank(string definitionDigest, string bindingDigest, int playerCount, int sampleCount,
        ReadOnlySpan<int> rootAnimationIds, ReadOnlySpan<AlsRawAnimationSkeletonDefinition> skeletons,
        ReadOnlySpan<AlsRawAnimationSourceDefinition> sources)
    {
        DefinitionDigest = definitionDigest; BindingDigest = bindingDigest; PlayerCount = playerCount; SampleCount = sampleCount;
        _rootAnimationIds = rootAnimationIds.ToArray(); _skeletons = skeletons.ToArray(); _sources = sources.ToArray();
        _sourceById = _sources.ToDictionary(s => s.PoseData.Identity.AnimationId);
        _skeletonById = _skeletons.ToDictionary(s => s.SkeletonId);
    }
    public AlsRawAnimationSourceDefinition GetSource(int animationId) => _sourceById.TryGetValue(animationId, out var source)
        ? source : throw new ArgumentOutOfRangeException(nameof(animationId), "Animation is outside the verified source closure.");
    public AlsRawAnimationSkeletonDefinition GetSkeleton(int skeletonId) => _skeletonById.TryGetValue(skeletonId, out var skeleton)
        ? skeleton : throw new ArgumentOutOfRangeException(nameof(skeletonId), "Skeleton is outside the verified source closure.");

    // Keep this graph's binding/root identities while sharing identical immutable
    // resource objects already loaded for another graph on the same manifest.
    public AlsRawAnimationSourceBank ReuseResourcesFrom(AlsRawAnimationSourceBank existing) => Reuse(existing, true);

    public AlsRawAnimationSourceBank ReuseOverlappingResourcesFrom(AlsRawAnimationSourceBank existing) => Reuse(existing, false);

    private AlsRawAnimationSourceBank Reuse(AlsRawAnimationSourceBank existing, bool requireAll)
    {
        ArgumentNullException.ThrowIfNull(existing);
        if (DefinitionDigest != existing.DefinitionDigest) throw new ArgumentException("Cannot share sources across manifests.");
        var sources = new AlsRawAnimationSourceDefinition[_sources.Length];
        for (var i = 0; i < sources.Length; i++)
        {
            var expected = _sources[i];
            if (!requireAll && !existing._sourceById.ContainsKey(expected.PoseData.Identity.AnimationId))
            { sources[i] = expected; continue; }
            var source = existing.GetSource(expected.PoseData.Identity.AnimationId);
            if (source.PoseData.Identity != expected.PoseData.Identity || source.SourceFile != expected.SourceFile ||
                !source.Sha256.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Shared source asset or raw content differs.");
            sources[i] = source;
        }
        var skeletons = new AlsRawAnimationSkeletonDefinition[_skeletons.Length];
        for (var i = 0; i < skeletons.Length; i++)
        {
            var a = _skeletons[i];
            if (!requireAll && !existing._skeletonById.ContainsKey(a.SkeletonId)) { skeletons[i] = a; continue; }
            var b = existing.GetSkeleton(a.SkeletonId);
            if (a.AssetId != b.AssetId || a.Source != b.Source || !a.RawBoneNames.SequenceEqual(b.RawBoneNames) ||
                !a.LogicalBoneNames.SequenceEqual(b.LogicalBoneNames) || !a.RawParents.SequenceEqual(b.RawParents) ||
                !a.LogicalParents.SequenceEqual(b.LogicalParents) || !a.LogicalToPhysical.SequenceEqual(b.LogicalToPhysical) ||
                !a.PhysicalToLogical.SequenceEqual(b.PhysicalToLogical) || !a.VirtualBones.SequenceEqual(b.VirtualBones) ||
                !a.ReferencePose.SequenceEqual(b.ReferencePose) || !a.PreciseReferencePose.SequenceEqual(b.PreciseReferencePose) ||
                !a.TranslationRetargetModes.SequenceEqual(b.TranslationRetargetModes))
                throw new ArgumentException("Shared source skeleton or retarget policy differs.");
            skeletons[i] = b;
        }
        return new(DefinitionDigest, BindingDigest, PlayerCount, SampleCount, _rootAnimationIds, skeletons, sources);
    }
}
