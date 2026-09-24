using System.Runtime.InteropServices;

namespace GodotAls.Core.Contracts;

public enum AlsTimelineEventKind : byte
{
    Generic = 0,
    Footstep = 1,
    SetAction = 2,
    SetGroundedEntry = 3,
    EarlyBlendOut = 4,
    RootMotionScale = 5,
}

public enum AlsTimelineTickMode : byte
{
    Queued = 0,
    BranchingPoint = 1,
}

public enum AlsTimelineSourceKind : byte
{
    Animation = 0,
    Montage = 1,
    MontageSegmentAnimation = 2,
}

public enum AlsActionCommand : byte
{
    None = 0,
    Start = 1,
    Cancel = 2,
    CancelForRuntimeFailure = 3,
}

public enum AlsTransitionFoot : byte
{
    Left = 0,
    Right = 1,
}

public enum AlsTimelineFoot : byte
{
    Unspecified = 0,
    Left = 1,
    Right = 2,
}

public enum AlsTimelineAction : byte
{
    None = 0,
    Rolling = 1,
    Mantling = 2,
    Ragdolling = 3,
    GettingUp = 4,
}

public enum AlsTimelineGroundedEntryMode : byte
{
    None = 0,
    FromRoll = 1,
}

public enum AlsTimelineLocomotionMode : byte
{
    Grounded = 0,
    InAir = 1,
    Mantling = 2,
    Ragdoll = 3,
    Recovering = 4,
}

public enum AlsTimelineRotationMode : byte
{
    VelocityDirection = 0,
    LookingDirection = 1,
    Aiming = 2,
}

public enum AlsTimelineStance : byte
{
    Standing = 0,
    Crouching = 1,
}

public enum AlsP5FailureCode : ushort
{
    None = 0,
    InvalidDeltaTime = 1,
    NonFiniteInput = 2,
    InvalidBinding = 3,
    InvalidTimeline = 4,
    InvalidSyncGroup = 5,
    EventBufferOverflow = 6,
    StalePreparedFrame = 7,
    NonFiniteOutput = 8,
}

public enum AlsActionResultCode : ushort
{
    None = 0,
    Accepted = 1,
    Completed = 2,
    RejectedInvalidRequest = 3,
    RejectedMissingDefinition = 4,
    RejectedBusy = 5,
    RejectedLowerPriority = 6,
    InterruptedByReplacement = 7,
    InterruptedByExplicitCancel = 8,
    InterruptedByEarlyBlendOut = 9,
    InterruptedByLifecycle = 10,
    InterruptedByGeneration = 11,
    InterruptedByRuntimeFailure = 12,
    InterruptedByRagdoll = 13,
}

public enum AlsP5OccurrenceSourceKind : byte
{
    Base = 1,
    Turn = 2,
    Rotate = 3,
    Transition = 4,
    ActionMontage = 5,
    ActionSequence = 6,
    SourceSample = 7,
    SourceEvaluator = 8,
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsCompactEventPayload(
    int SemanticId,
    int EnumValue0,
    int EnumValue1,
    int EnumValue2,
    float ScalarValue0,
    ushort Flags,
    AlsActionResultCode TerminationReason);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsActionRequest(
    long RequestId,
    AlsActionCommand Command,
    int ActionDefinitionId,
    int StartSectionId,
    int Priority,
    uint SlotGeneration)
{
    public static AlsActionRequest None => new(
        -1,
        AlsActionCommand.None,
        -1,
        -1,
        0,
        0);
}

[StructLayout(LayoutKind.Sequential)]
public struct AlsActionLifecycleState
{
    public float Alpha;
    public float RemainingSeconds;
    public float BeginWeight;
    public float CurrentWeight;
    public float DesiredWeight;
    public byte BlendingOut;
    public byte TraversalFinished;
}

[StructLayout(LayoutKind.Sequential)]
public struct AlsActionPlayerState
{
    public int ActionDefinitionId;
    public int SectionId;
    public int SegmentBindingIndex;
    public long RequestId;
    public long LastProcessedRequestId;
    public long LastProcessedCommandRequestId;
    public AlsActionCommand LastProcessedCommand;
    public long PlaybackEpoch;
    public float PlaybackTime;
    public int Priority;
    public byte Playing;
    public byte Interruptible;
    public AlsActionLifecycleState Lifecycle;

    public static AlsActionPlayerState CreateDefault() => new()
    {
        ActionDefinitionId = -1,
        SectionId = -1,
        SegmentBindingIndex = -1,
        RequestId = -1,
        LastProcessedRequestId = 0,
        LastProcessedCommandRequestId = -1,
        LastProcessedCommand = AlsActionCommand.None,
    };
}

[StructLayout(LayoutKind.Sequential)]
public struct AlsDynamicTransitionState
{
    public int AnimationId;
    public int QueuedAnimationId;
    public long PlaybackEpoch;
    public float PreviousPlaybackTime;
    public float PlaybackTime;
    public int CooldownFrames;
    public AlsTransitionFoot Foot;
    public AlsTransitionFoot QueuedFoot;
    public byte Active;
    public byte Queued;

    public static AlsDynamicTransitionState CreateDefault() => new()
    {
        AnimationId = -1,
        QueuedAnimationId = -1,
    };
}

[StructLayout(LayoutKind.Sequential)]
public struct AlsLaneBlendState
{
    public int OutgoingOccurrenceHandleId;
    public int OutgoingAnimationId;
    public int OutgoingBindingIndex;
    public long OutgoingPlaybackEpoch;
    public float OutgoingClipTime;
    public float LaneWeight;
    public float IncomingMix;
    public float BlendSeconds;
    public byte VisualActive;
    public byte OutgoingActive;

    public static AlsLaneBlendState CreateDefault() => new()
    {
        OutgoingOccurrenceHandleId = -1,
        OutgoingAnimationId = -1,
        OutgoingBindingIndex = -1,
    };
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsLaneGraphSource(
    int OccurrenceHandleId,
    int AnimationId,
    int BindingIndex,
    long PlaybackEpoch,
    float PreviousClipTime,
    float CurrentClipTime,
    float ContributingDeltaSeconds,
    float PlayRate,
    byte Active);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsLaneGraphInstruction(
    AlsLaneGraphSource Outgoing,
    AlsLaneGraphSource Incoming,
    float LaneWeight,
    float IncomingMix,
    float OutgoingEffectiveWeight,
    float IncomingEffectiveWeight);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsSyncResult(
    int GroupId,
    int LeaderOccurrenceHandleId,
    int LeaderAnimationId,
    long LeaderPlaybackEpoch,
    int PreviousMarkerId,
    int NextMarkerId,
    long Cycle,
    float Phase,
    float LeftFootPhase,
    float RightFootPhase)
{
    public static AlsSyncResult CreateDefault() => new(
        -1, -1, -1, 0, -1, -1, 0, 0f, 0f, 0f);
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsDynamicTransitionQueuedSelection(
    int AnimationId,
    AlsTransitionFoot Foot,
    float BlendSeconds,
    float PlayRate,
    byte Active)
{
    public static AlsDynamicTransitionQueuedSelection CreateDefault() => new(
        -1, AlsTransitionFoot.Left, 0f, 0f, 0);
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsDynamicTransitionPlaybackSummary(
    int AnimationId,
    AlsTransitionFoot Foot,
    float BlendSeconds,
    float PlayRate,
    float EffectiveWeight,
    byte Active)
{
    public static AlsDynamicTransitionPlaybackSummary CreateDefault() => new(
        -1, AlsTransitionFoot.Left, 0f, 0f, 0f, 0);
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsActionPlayback(
    int OccurrenceHandleId,
    int ActionDefinitionId,
    int AnimationId,
    int SectionId,
    int SegmentId,
    long PlaybackEpoch,
    float PreviousTime,
    float CurrentTime,
    float PreviousClipTime,
    float CurrentClipTime,
    float FinalSegmentDeltaSeconds,
    float PlayRate,
    float BlendSeconds,
    float EffectiveWeight,
    byte Active)
{
    public static AlsActionPlayback CreateDefault() => new(
        -1, -1, -1, -1, -1, 0,
        0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0);
}
