using System.Runtime.InteropServices;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Curves;
using GodotAls.Core.Events;
using GodotAls.Core.Sync;
using GodotAls.Core.Transitions;

namespace GodotAls.Core.Animation;

public enum AlsP5CurveCombineMode : byte
{
    AdditiveToDefault = 1,
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsP5CurveBindingIdentity(
    int AnimationId,
    int CurveId);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsAnimationCurveRange(
    int AnimationId,
    int BindingOffset,
    int BindingCount);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsP5CurveSemanticPolicy(
    float MissingValue,
    AlsP5CurveCombineMode CombineMode,
    float ClampMinimum,
    float ClampMaximum);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsP4FootCurveRuntimeBinding(
    int AnimationId,
    int LeftLockCurveId,
    int RightLockCurveId,
    float LeftLockDefault,
    float RightLockDefault);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsP5SyncOccurrenceBinding(
    int GroupId,
    int GroupMemberIndex,
    int AnimationId,
    int OccurrenceHandleId);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsActionTimelineRange(
    int ActionDefinitionId,
    int DefinitionOffset,
    int DefinitionCount);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsBasePlaybackDescriptor(
    int OccurrenceHandleId,
    int AnimationId,
    int AuthorityGroupId,
    long PlaybackEpoch,
    double PreviousUnwrappedTimeSeconds,
    double CurrentUnwrappedTimeSeconds,
    double FrameStartOffsetSeconds,
    double FrameEndOffsetSeconds,
    float DurationSeconds,
    float Weight,
    byte Loop,
    byte ActivatesAtFrameStart,
    byte ClosesAfterFrame);

public readonly ref struct AlsP4CurveFrameInput
{
    public readonly ReadOnlySpan<AlsBasePlaybackDescriptor> Base;
    public readonly ReadOnlySpan<AlsBasePlaybackDescriptor> TurnBanks;
    public readonly ReadOnlySpan<AlsBasePlaybackDescriptor> RotateBanks;
    public readonly AlsAnimationState AnimationState;
    public readonly float ActionBlendAmount;
    public readonly float ActionModeBlendAmount;

    public AlsP4CurveFrameInput(
        ReadOnlySpan<AlsBasePlaybackDescriptor> @base,
        ReadOnlySpan<AlsBasePlaybackDescriptor> turnBanks,
        ReadOnlySpan<AlsBasePlaybackDescriptor> rotateBanks,
        AlsAnimationState animationState,
        float actionBlendAmount,
        float actionModeBlendAmount)
    {
        Base = @base;
        TurnBanks = turnBanks;
        RotateBanks = rotateBanks;
        AnimationState = animationState;
        ActionBlendAmount = actionBlendAmount;
        ActionModeBlendAmount = actionModeBlendAmount;
    }
}

public readonly ref struct AlsP5RuntimeBindings
{
    public const int CurrentVersion = 2;

    public readonly int Version;
    public readonly ulong Digest;
    public readonly ulong LayoutDigest;
    public readonly ReadOnlySpan<AlsCurveKey> CurveKeys;
    public readonly ReadOnlySpan<AlsCurveBinding> CurveBindings;
    public readonly ReadOnlySpan<AlsP5CurveBindingIdentity> CurveBindingIdentities;
    public readonly ReadOnlySpan<AlsAnimationCurveRange> AnimationCurveRanges;
    public readonly AlsP5CurveSemanticPolicy AllowTransitionsPolicy;
    public readonly ReadOnlySpan<int> AllowTransitionsBindingIndices;
    public readonly ReadOnlySpan<AlsP4FootCurveRuntimeBinding> FootCurveBindings;
    public readonly float GroundedIkWeight;
    public readonly float JumpStartIkWeight;
    public readonly float FallLoopIkWeight;
    public readonly float LandRecoveryIkWeight;
    public readonly ReadOnlySpan<AlsTimelineEventDefinition> TimelineDefinitions;
    public readonly ReadOnlySpan<AlsSyncMarkerDefinition> SyncMarkers;
    public readonly AlsSyncGroupBinding SyncGroup;
    public readonly ReadOnlySpan<AlsSyncMemberBinding> SyncMembers;
    public readonly ReadOnlySpan<AlsP5SyncOccurrenceBinding> SyncOccurrences;
    public readonly AlsDynamicTransitionBinding DynamicTransition;
    public readonly ReadOnlySpan<AlsActionDefinition> ActionDefinitions;
    public readonly ReadOnlySpan<AlsActionSectionBinding> ActionSections;
    public readonly ReadOnlySpan<AlsActionSegmentBinding> ActionSegments;
    public readonly ReadOnlySpan<AlsActionTimelineRange> ActionTimelineRanges;

    public AlsP5RuntimeBindings(
        int version,
        ulong digest,
        ulong layoutDigest,
        ReadOnlySpan<AlsCurveKey> curveKeys,
        ReadOnlySpan<AlsCurveBinding> curveBindings,
        ReadOnlySpan<AlsP5CurveBindingIdentity> curveBindingIdentities,
        ReadOnlySpan<AlsAnimationCurveRange> animationCurveRanges,
        AlsP5CurveSemanticPolicy allowTransitionsPolicy,
        ReadOnlySpan<int> allowTransitionsBindingIndices,
        ReadOnlySpan<AlsP4FootCurveRuntimeBinding> footCurveBindings,
        float groundedIkWeight,
        float jumpStartIkWeight,
        float fallLoopIkWeight,
        float landRecoveryIkWeight,
        ReadOnlySpan<AlsTimelineEventDefinition> timelineDefinitions,
        ReadOnlySpan<AlsSyncMarkerDefinition> syncMarkers,
        AlsSyncGroupBinding syncGroup,
        ReadOnlySpan<AlsSyncMemberBinding> syncMembers,
        ReadOnlySpan<AlsP5SyncOccurrenceBinding> syncOccurrences,
        AlsDynamicTransitionBinding dynamicTransition,
        ReadOnlySpan<AlsActionDefinition> actionDefinitions,
        ReadOnlySpan<AlsActionSectionBinding> actionSections,
        ReadOnlySpan<AlsActionSegmentBinding> actionSegments,
        ReadOnlySpan<AlsActionTimelineRange> actionTimelineRanges)
    {
        if (version != CurrentVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }
        if (digest == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(digest));
        }
        if (layoutDigest == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(layoutDigest));
        }

        Version = version;
        Digest = digest;
        LayoutDigest = layoutDigest;
        CurveKeys = curveKeys;
        CurveBindings = curveBindings;
        CurveBindingIdentities = curveBindingIdentities;
        AnimationCurveRanges = animationCurveRanges;
        AllowTransitionsPolicy = allowTransitionsPolicy;
        AllowTransitionsBindingIndices = allowTransitionsBindingIndices;
        FootCurveBindings = footCurveBindings;
        GroundedIkWeight = groundedIkWeight;
        JumpStartIkWeight = jumpStartIkWeight;
        FallLoopIkWeight = fallLoopIkWeight;
        LandRecoveryIkWeight = landRecoveryIkWeight;
        TimelineDefinitions = timelineDefinitions;
        SyncMarkers = syncMarkers;
        SyncGroup = syncGroup;
        SyncMembers = syncMembers;
        SyncOccurrences = syncOccurrences;
        DynamicTransition = dynamicTransition;
        ActionDefinitions = actionDefinitions;
        ActionSections = actionSections;
        ActionSegments = actionSegments;
        ActionTimelineRanges = actionTimelineRanges;
    }
}

public readonly ref struct AlsP5FrameInput
{
    public readonly AlsFrameIdentity Identity;
    public readonly double FrameStartTimeSeconds;
    public readonly double FrameEndTimeSeconds;
    public readonly float DeltaTimeSeconds;
    public readonly uint CurrentSlotGeneration;
    public readonly AlsActionRequest ActionRequest;
    public readonly byte CancelActionForRuntimeFailure;
    public readonly byte HasInput;
    public readonly AlsTimelineLocomotionMode LocomotionMode;
    public readonly AlsTimelineRotationMode RotationMode;
    public readonly AlsTimelineStance Stance;
    public readonly AlsP4CurveFrameInput P4Curves;

    public AlsP5FrameInput(
        AlsFrameIdentity identity,
        double frameStartTimeSeconds,
        double frameEndTimeSeconds,
        float deltaTimeSeconds,
        uint currentSlotGeneration,
        AlsActionRequest actionRequest,
        byte cancelActionForRuntimeFailure,
        byte hasInput,
        AlsTimelineLocomotionMode locomotionMode,
        AlsTimelineRotationMode rotationMode,
        AlsTimelineStance stance,
        AlsP4CurveFrameInput p4Curves)
    {
        Identity = identity;
        FrameStartTimeSeconds = frameStartTimeSeconds;
        FrameEndTimeSeconds = frameEndTimeSeconds;
        DeltaTimeSeconds = deltaTimeSeconds;
        CurrentSlotGeneration = currentSlotGeneration;
        ActionRequest = actionRequest;
        CancelActionForRuntimeFailure = cancelActionForRuntimeFailure;
        HasInput = hasInput;
        LocomotionMode = locomotionMode;
        RotationMode = rotationMode;
        Stance = stance;
        P4Curves = p4Curves;
    }
}

public enum AlsP5RuntimeScratchPhase : byte
{
    Empty = 0,
    Prepared = 1,
}

[StructLayout(LayoutKind.Sequential)]
public struct AlsP5RuntimeScratchControl
{
    public ulong OwnerCookie;
    public ulong AttemptRevision;
    public ulong PreparedRevision;
    public AlsFrameIdentity PreparedIdentity;
    public ulong PreparedBindingDigest;
    public ulong PreparedLayoutDigest;
    public AlsP5RuntimeScratchPhase Phase;

    public AlsP5RuntimeScratchControl(ulong ownerCookie)
    {
        if (ownerCookie == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ownerCookie));
        }

        OwnerCookie = ownerCookie;
        AttemptRevision = 0;
        PreparedRevision = 0;
        PreparedIdentity = default;
        PreparedBindingDigest = 0;
        PreparedLayoutDigest = 0;
        Phase = AlsP5RuntimeScratchPhase.Empty;
    }

    public static AlsP5RuntimeScratchControl Create(ulong ownerCookie) => new(ownerCookie);
}

public ref struct AlsP5RuntimeScratch
{
    public readonly int BaseCapacity;
    public readonly int MaximumBaseContributorCount;
    public readonly Span<AlsP5RuntimeScratchControl> Control;
    public readonly Span<AlsTimelineCursor> CandidateCursors;
    public readonly Span<AlsTimelineAuthorityState> CandidateAuthorities;
    public readonly Span<AlsNotifyStateOwnership> CandidateOwnership;
    public readonly Span<AlsTimelineOccurrence> TimelineOccurrences;
    public readonly Span<AlsActionTraversalSlice> ActionTraversalSlices;
    public readonly Span<AlsTimelinePlayback> TimelinePlaybacks;
    public readonly Span<AlsSyncPlayback> SyncPlaybacks;
    public readonly Span<AlsSyncMappedPlayback> SyncMappedPlaybacks;
    public readonly Span<AlsCurveBlendSample> CurveSamples;

    internal ulong ViewPreparedRevision;
    internal AlsActionPlayerState CandidateActionPlayer;
    internal AlsDynamicTransitionState CandidateDynamicTransition;
    internal AlsLaneBlendState CandidateActionBlendLane;
    internal AlsLaneBlendState CandidateDynamicTransitionBlendLane;
    internal AlsLaneGraphInstruction ActionGraph;
    internal AlsLaneGraphInstruction TransitionGraph;
    internal AlsSyncResult Sync;
    internal int SyncMappingCount;
    internal float LeftIk;
    internal float RightIk;
    internal float LeftLock;
    internal float RightLock;
    internal float AllowTransitions;
    internal float TransitionReplacedClosingWeight;
    internal AlsEventBuffer Events;
    internal AlsActionOutcomeBuffer ActionOutcomes;
    internal AlsActionPlayback ActionPlayback;
    internal AlsDynamicTransitionPlaybackSummary DynamicTransitionSummary;
    internal AlsDynamicTransitionBinding PreparedTransitionBinding;
    internal ulong NextOwnerToken;
    internal byte TransitionCooldownBlockedThisFrame;

    public AlsP5RuntimeScratch(
        int baseCapacity,
        int maximumBaseContributorCount,
        Span<AlsP5RuntimeScratchControl> control,
        Span<AlsTimelineCursor> candidateCursors,
        Span<AlsTimelineAuthorityState> candidateAuthorities,
        Span<AlsNotifyStateOwnership> candidateOwnership,
        Span<AlsTimelineOccurrence> timelineOccurrences,
        Span<AlsActionTraversalSlice> actionTraversalSlices,
        Span<AlsTimelinePlayback> timelinePlaybacks,
        Span<AlsSyncPlayback> syncPlaybacks,
        Span<AlsSyncMappedPlayback> syncMappedPlaybacks,
        Span<AlsCurveBlendSample> curveSamples)
    {
        BaseCapacity = baseCapacity;
        MaximumBaseContributorCount = maximumBaseContributorCount;
        Control = control;
        CandidateCursors = candidateCursors;
        CandidateAuthorities = candidateAuthorities;
        CandidateOwnership = candidateOwnership;
        TimelineOccurrences = timelineOccurrences;
        ActionTraversalSlices = actionTraversalSlices;
        TimelinePlaybacks = timelinePlaybacks;
        SyncPlaybacks = syncPlaybacks;
        SyncMappedPlaybacks = syncMappedPlaybacks;
        CurveSamples = curveSamples;
        ViewPreparedRevision = 0;
        CandidateActionPlayer = AlsActionPlayerState.CreateDefault();
        CandidateDynamicTransition = AlsDynamicTransitionState.CreateDefault();
        CandidateActionBlendLane = AlsLaneBlendState.CreateDefault();
        CandidateDynamicTransitionBlendLane = AlsLaneBlendState.CreateDefault();
        ActionGraph = default;
        TransitionGraph = default;
        Sync = AlsSyncResult.CreateDefault();
        SyncMappingCount = 0;
        LeftIk = 0f;
        RightIk = 0f;
        LeftLock = 0f;
        RightLock = 0f;
        AllowTransitions = 0f;
        TransitionReplacedClosingWeight = 0f;
        Events = default;
        ActionOutcomes = default;
        ActionPlayback = AlsActionPlayback.CreateDefault();
        DynamicTransitionSummary = AlsDynamicTransitionPlaybackSummary.CreateDefault();
        PreparedTransitionBinding = default;
        NextOwnerToken = 0;
        TransitionCooldownBlockedThisFrame = 0;
    }
}

public readonly ref struct AlsP5PreparedFrame
{
    public readonly ulong OwnerCookie;
    public readonly ulong Revision;
    public readonly AlsFrameIdentity Identity;
    public readonly ulong BindingDigest;
    public readonly ulong LayoutDigest;
    public readonly AlsLaneGraphInstruction ActionGraph;
    public readonly AlsLaneGraphInstruction TransitionGraph;
    public readonly AlsSyncResult Sync;
    public readonly ReadOnlySpan<AlsSyncMappedPlayback> SyncMappedPlaybacks;
    public int SyncMappingCount => SyncMappedPlaybacks.Length;
    public readonly float LeftIk;
    public readonly float RightIk;
    public readonly float LeftLock;
    public readonly float RightLock;
    public readonly float AllowTransitions;
    public readonly float TransitionReplacedClosingWeight;

    internal AlsP5PreparedFrame(
        ulong ownerCookie,
        ulong revision,
        AlsFrameIdentity identity,
        ulong bindingDigest,
        ulong layoutDigest,
        AlsLaneGraphInstruction actionGraph,
        AlsLaneGraphInstruction transitionGraph,
        AlsSyncResult sync,
        ReadOnlySpan<AlsSyncMappedPlayback> syncMappedPlaybacks,
        float leftIk,
        float rightIk,
        float leftLock,
        float rightLock,
        float allowTransitions,
        float transitionReplacedClosingWeight)
    {
        OwnerCookie = ownerCookie;
        Revision = revision;
        Identity = identity;
        BindingDigest = bindingDigest;
        LayoutDigest = layoutDigest;
        ActionGraph = actionGraph;
        TransitionGraph = transitionGraph;
        Sync = sync;
        SyncMappedPlaybacks = syncMappedPlaybacks;
        LeftIk = leftIk;
        RightIk = rightIk;
        LeftLock = leftLock;
        RightLock = rightLock;
        AllowTransitions = allowTransitions;
        TransitionReplacedClosingWeight = transitionReplacedClosingWeight;
    }
}
