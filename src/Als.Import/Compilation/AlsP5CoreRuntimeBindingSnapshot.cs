using GodotAls.Core.Actions;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Curves;
using GodotAls.Core.Events;
using GodotAls.Core.Sync;
using GodotAls.Core.Transitions;
using CoreOccurrenceEntry = GodotAls.Core.Contracts.AlsP5OccurrenceLayoutEntry;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsP5GraphSample(
    int AnimationId, float X, float Y, float RateScale);

public readonly record struct AlsP5GraphMaskHeader(
    AlsPoseMaskKind Kind,
    int LogicalRootBoneId,
    int BoneOffset,
    int BoneCount);

public readonly ref struct AlsP5GraphBuildView
{
    public readonly int Version;
    public readonly ulong Digest;
    public readonly int SkeletonId;
    public readonly int MannequinMeshId;
    public readonly int RootMotionExtractionLogicalBoneId;
    public readonly int RootMotionExtractionPhysicalBoneId;
    public readonly AlsPresentationDefinition Presentation;
    public readonly int StandingIdleAnimationId;
    public readonly int CrouchingIdleAnimationId;
    public readonly int JumpStartAnimationId;
    public readonly int FallLoopAnimationId;
    public readonly int LandAnimationId;
    public readonly int LeanAdditiveBaseAnimationId;
    public readonly ReadOnlySpan<AlsP5GraphSample> StandingSamples;
    public readonly ReadOnlySpan<AlsP5GraphSample> CrouchingSamples;
    public readonly ReadOnlySpan<AlsP5GraphSample> LeanSamples;
    public readonly ReadOnlySpan<int> AllAnimationIds;
    public readonly AlsAimProfile Aim;
    public readonly ReadOnlySpan<AlsTurnProfile> Turns;
    public readonly ReadOnlySpan<AlsRotateProfile> Rotates;
    public readonly ReadOnlySpan<AlsP5GraphMaskHeader> MaskHeaders;
    public readonly ReadOnlySpan<int> LogicalBoneIds;
    public readonly ReadOnlySpan<int> NormalizedAnimationIds;

    public AlsP5GraphBuildView(
        int version,
        ulong digest,
        int skeletonId,
        int mannequinMeshId,
        int rootMotionExtractionLogicalBoneId,
        int rootMotionExtractionPhysicalBoneId,
        AlsPresentationDefinition presentation,
        int standingIdleAnimationId,
        int crouchingIdleAnimationId,
        int jumpStartAnimationId,
        int fallLoopAnimationId,
        int landAnimationId,
        int leanAdditiveBaseAnimationId,
        ReadOnlySpan<AlsP5GraphSample> standingSamples,
        ReadOnlySpan<AlsP5GraphSample> crouchingSamples,
        ReadOnlySpan<AlsP5GraphSample> leanSamples,
        ReadOnlySpan<int> allAnimationIds,
        AlsAimProfile aim,
        ReadOnlySpan<AlsTurnProfile> turns,
        ReadOnlySpan<AlsRotateProfile> rotates,
        ReadOnlySpan<AlsP5GraphMaskHeader> maskHeaders,
        ReadOnlySpan<int> logicalBoneIds,
        ReadOnlySpan<int> normalizedAnimationIds)
    {
        if (version != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }
        if (digest == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(digest));
        }

        Version = version;
        Digest = digest;
        SkeletonId = skeletonId;
        MannequinMeshId = mannequinMeshId;
        RootMotionExtractionLogicalBoneId = rootMotionExtractionLogicalBoneId;
        RootMotionExtractionPhysicalBoneId = rootMotionExtractionPhysicalBoneId;
        Presentation = presentation;
        StandingIdleAnimationId = standingIdleAnimationId;
        CrouchingIdleAnimationId = crouchingIdleAnimationId;
        JumpStartAnimationId = jumpStartAnimationId;
        FallLoopAnimationId = fallLoopAnimationId;
        LandAnimationId = landAnimationId;
        LeanAdditiveBaseAnimationId = leanAdditiveBaseAnimationId;
        StandingSamples = standingSamples;
        CrouchingSamples = crouchingSamples;
        LeanSamples = leanSamples;
        AllAnimationIds = allAnimationIds;
        Aim = aim;
        Turns = turns;
        Rotates = rotates;
        MaskHeaders = maskHeaders;
        LogicalBoneIds = logicalBoneIds;
        NormalizedAnimationIds = normalizedAnimationIds;
    }
}

public sealed class AlsP5CoreRuntimeBindingSnapshot
{
    private readonly AlsCurveKey[] _curveKeys;
    private readonly AlsCurveBinding[] _curveBindings;
    private readonly AlsP5CurveBindingIdentity[] _curveBindingIdentities;
    private readonly AlsAnimationCurveRange[] _animationCurveRanges;
    private readonly int[] _allowTransitionsBindingIndices;
    private readonly AlsP4FootCurveRuntimeBinding[] _footCurveBindings;
    private readonly AlsTimelineEventDefinition[] _timelineDefinitions;
    private readonly AlsSyncMarkerDefinition[] _syncMarkers;
    private readonly AlsSyncMemberBinding[] _syncMembers;
    private readonly AlsP5SyncOccurrenceBinding[] _syncOccurrences;
    private readonly AlsActionDefinition[] _actionDefinitions;
    private readonly AlsActionSectionBinding[] _actionSections;
    private readonly AlsActionSegmentBinding[] _actionSegments;
    private readonly AlsActionTimelineRange[] _actionTimelineRanges;
    private readonly CoreOccurrenceEntry[] _occurrenceEntries;
    private readonly AlsP5GraphSample[] _standingSamples;
    private readonly AlsP5GraphSample[] _crouchingSamples;
    private readonly AlsP5GraphSample[] _leanSamples;
    private readonly int[] _allAnimationIds;
    private readonly AlsTurnProfile[] _turns;
    private readonly AlsRotateProfile[] _rotates;
    private readonly AlsP5GraphMaskHeader[] _maskHeaders;
    private readonly int[] _logicalBoneIds;
    private readonly int[] _normalizedAnimationIds;
    private readonly AlsP5CurveSemanticPolicy _allowTransitionsPolicy;
    private readonly float _groundedIkWeight;
    private readonly float _jumpStartIkWeight;
    private readonly float _fallLoopIkWeight;
    private readonly float _landRecoveryIkWeight;
    private readonly AlsSyncGroupBinding _syncGroup;
    private readonly AlsDynamicTransitionBinding _dynamicTransition;
    private readonly int _skeletonId;
    private readonly int _mannequinMeshId;
    private readonly int _rootMotionExtractionLogicalBoneId;
    private readonly int _rootMotionExtractionPhysicalBoneId;
    private readonly AlsPresentationDefinition _presentation;
    private readonly int _standingIdleAnimationId;
    private readonly int _crouchingIdleAnimationId;
    private readonly int _jumpStartAnimationId;
    private readonly int _fallLoopAnimationId;
    private readonly int _landAnimationId;
    private readonly int _leanAdditiveBaseAnimationId;
    private readonly AlsAimProfile _aim;

    public int Version { get; }
    public ulong Digest { get; }
    public ulong LayoutDigest { get; }
    public ulong GraphDigest { get; }
    public string AnimationSetDefinitionDigest { get; }

    internal AlsP5CoreRuntimeBindingSnapshot(
        int version,
        ulong digest,
        ulong layoutDigest,
        ulong graphDigest,
        string animationSetDefinitionDigest,
        AlsCurveKey[] curveKeys,
        AlsCurveBinding[] curveBindings,
        AlsP5CurveBindingIdentity[] curveBindingIdentities,
        AlsAnimationCurveRange[] animationCurveRanges,
        AlsP5CurveSemanticPolicy allowTransitionsPolicy,
        int[] allowTransitionsBindingIndices,
        AlsP4FootCurveRuntimeBinding[] footCurveBindings,
        float groundedIkWeight,
        float jumpStartIkWeight,
        float fallLoopIkWeight,
        float landRecoveryIkWeight,
        AlsTimelineEventDefinition[] timelineDefinitions,
        AlsSyncMarkerDefinition[] syncMarkers,
        AlsSyncGroupBinding syncGroup,
        AlsSyncMemberBinding[] syncMembers,
        AlsP5SyncOccurrenceBinding[] syncOccurrences,
        AlsDynamicTransitionBinding dynamicTransition,
        AlsActionDefinition[] actionDefinitions,
        AlsActionSectionBinding[] actionSections,
        AlsActionSegmentBinding[] actionSegments,
        AlsActionTimelineRange[] actionTimelineRanges,
        CoreOccurrenceEntry[] occurrenceEntries,
        int skeletonId,
        int mannequinMeshId,
        int rootMotionExtractionLogicalBoneId,
        int rootMotionExtractionPhysicalBoneId,
        AlsPresentationDefinition presentation,
        int standingIdleAnimationId,
        int crouchingIdleAnimationId,
        int jumpStartAnimationId,
        int fallLoopAnimationId,
        int landAnimationId,
        int leanAdditiveBaseAnimationId,
        AlsP5GraphSample[] standingSamples,
        AlsP5GraphSample[] crouchingSamples,
        AlsP5GraphSample[] leanSamples,
        int[] allAnimationIds,
        AlsAimProfile aim,
        AlsTurnProfile[] turns,
        AlsRotateProfile[] rotates,
        AlsP5GraphMaskHeader[] maskHeaders,
        int[] logicalBoneIds,
        int[] normalizedAnimationIds)
    {
        Version = version;
        Digest = digest;
        LayoutDigest = layoutDigest;
        GraphDigest = graphDigest;
        AnimationSetDefinitionDigest = animationSetDefinitionDigest;
        _curveKeys = curveKeys.ToArray();
        _curveBindings = curveBindings.ToArray();
        _curveBindingIdentities = curveBindingIdentities.ToArray();
        _animationCurveRanges = animationCurveRanges.ToArray();
        _allowTransitionsPolicy = allowTransitionsPolicy;
        _allowTransitionsBindingIndices = allowTransitionsBindingIndices.ToArray();
        _footCurveBindings = footCurveBindings.ToArray();
        _groundedIkWeight = groundedIkWeight;
        _jumpStartIkWeight = jumpStartIkWeight;
        _fallLoopIkWeight = fallLoopIkWeight;
        _landRecoveryIkWeight = landRecoveryIkWeight;
        _timelineDefinitions = timelineDefinitions.ToArray();
        _syncMarkers = syncMarkers.ToArray();
        _syncGroup = syncGroup;
        _syncMembers = syncMembers.ToArray();
        _syncOccurrences = syncOccurrences.ToArray();
        _dynamicTransition = dynamicTransition;
        _actionDefinitions = actionDefinitions.ToArray();
        _actionSections = actionSections.ToArray();
        _actionSegments = actionSegments.ToArray();
        _actionTimelineRanges = actionTimelineRanges.ToArray();
        _occurrenceEntries = occurrenceEntries.ToArray();
        _skeletonId = skeletonId;
        _mannequinMeshId = mannequinMeshId;
        _rootMotionExtractionLogicalBoneId = rootMotionExtractionLogicalBoneId;
        _rootMotionExtractionPhysicalBoneId = rootMotionExtractionPhysicalBoneId;
        _presentation = presentation;
        _standingIdleAnimationId = standingIdleAnimationId;
        _crouchingIdleAnimationId = crouchingIdleAnimationId;
        _jumpStartAnimationId = jumpStartAnimationId;
        _fallLoopAnimationId = fallLoopAnimationId;
        _landAnimationId = landAnimationId;
        _leanAdditiveBaseAnimationId = leanAdditiveBaseAnimationId;
        _standingSamples = standingSamples.ToArray();
        _crouchingSamples = crouchingSamples.ToArray();
        _leanSamples = leanSamples.ToArray();
        _allAnimationIds = allAnimationIds.ToArray();
        _aim = aim;
        _turns = turns.ToArray();
        _rotates = rotates.ToArray();
        _maskHeaders = maskHeaders.ToArray();
        _logicalBoneIds = logicalBoneIds.ToArray();
        _normalizedAnimationIds = normalizedAnimationIds.ToArray();
    }

    public AlsP5RuntimeBindings CreateCoreView() => new(
        Version, Digest, LayoutDigest, _curveKeys, _curveBindings, _curveBindingIdentities,
        _animationCurveRanges, _allowTransitionsPolicy, _allowTransitionsBindingIndices,
        _footCurveBindings, _groundedIkWeight, _jumpStartIkWeight, _fallLoopIkWeight,
        _landRecoveryIkWeight, _timelineDefinitions, _syncMarkers, _syncGroup, _syncMembers,
        _syncOccurrences, _dynamicTransition, _actionDefinitions, _actionSections,
        _actionSegments, _actionTimelineRanges);

    public AlsP5OccurrenceLayoutView CreateOccurrenceLayoutView() =>
        new(Version, LayoutDigest, _occurrenceEntries);

    public AlsP5GraphBuildView CreateGraphBuildView() => new(
        Version, GraphDigest, _skeletonId, _mannequinMeshId,
        _rootMotionExtractionLogicalBoneId, _rootMotionExtractionPhysicalBoneId,
        _presentation, _standingIdleAnimationId, _crouchingIdleAnimationId,
        _jumpStartAnimationId, _fallLoopAnimationId, _landAnimationId,
        _leanAdditiveBaseAnimationId, _standingSamples, _crouchingSamples, _leanSamples,
        _allAnimationIds, _aim, _turns, _rotates, _maskHeaders, _logicalBoneIds,
        _normalizedAnimationIds);
}
