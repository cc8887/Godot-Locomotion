using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Actions;

public enum AlsActionLifecycleMode : byte
{
    // Compatibility for manually constructed definitions, never asset-compiled Actions.
    LegacySectionEnd,
    MontageAutoBlendOut,
    MontageHoldAtEnd,
}

public enum AlsActionBlendOption : byte
{
    Linear,
    Cubic,
    HermiteCubic,
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsActionLifecycleSettings(
    AlsActionLifecycleMode Mode,
    float BlendInSeconds,
    AlsActionBlendOption BlendInOption,
    float BlendOutSeconds,
    AlsActionBlendOption BlendOutOption,
    float BlendOutTriggerSeconds);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsActionDefinition(
    int OccurrenceHandleId,
    int MontageAuthorityGroupId,
    int SequenceAuthorityGroupId,
    int DefinitionId,
    int MontageId,
    float MontageDurationSeconds,
    int SlotId,
    int StartSectionId,
    int Priority,
    float PlayRate,
    float BlendSeconds,
    byte Interruptible,
    byte Loop,
    AlsActionLifecycleSettings Lifecycle = default);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsActionSectionBinding(
    int ActionDefinitionId,
    int SectionId,
    int NextSectionId,
    float StartTime,
    float EndTime);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsActionSegmentBinding(
    int OccurrenceHandleId,
    int ActionDefinitionId,
    int SlotId,
    int SegmentId,
    int AnimationId,
    float MontageStartTime,
    float MontageEndTime,
    float AnimationStartTime,
    float AnimationEndTime,
    float PlayRate,
    int LoopCount);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsActionTraversalSlice(
    int ActionOccurrenceHandleId,
    int SegmentOccurrenceHandleId,
    int SectionId,
    int SegmentBindingIndex,
    int SegmentId,
    int AnimationId,
    long PlaybackEpoch,
    double PreviousMontageTime,
    double CurrentMontageTime,
    double PreviousClipUnwrappedTime,
    double CurrentClipUnwrappedTime,
    double FrameStartOffsetSeconds,
    double FrameEndOffsetSeconds,
    byte ActivatesActionAtSliceStart,
    byte ActivatesSegmentAtSliceStart,
    byte ClosesActionAfterSlice,
    byte ClosesSegmentAfterSlice);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsActionRequestResult(
    AlsActionPlayback InitialPlayback,
    int FirstSliceIndex,
    int AddedSliceCount,
    int ClosingSliceIndex,
    int InitialSliceIndex,
    float ClosingBlendSeconds,
    AlsActionResultCode ClosingReason,
    byte StartedOrReplacedThisFrame)
{
    public static AlsActionRequestResult CreateDefault() => new(
        AlsActionPlayback.CreateDefault(),
        -1, 0, -1, -1, 0f, AlsActionResultCode.None, 0);
}

[StructLayout(LayoutKind.Sequential)]
// ClosingReason remains terminal evidence when an already-closed source produces no slice.
public readonly record struct AlsActionAdvanceResult(
    AlsActionPlayback ContributingPlayback,
    int FirstSliceIndex,
    int AddedSliceCount,
    int ClosingSliceIndex,
    AlsActionResultCode ClosingReason,
    double CompletionOffsetSeconds = 0d)
{
    public static AlsActionAdvanceResult CreateDefault() => new(
        AlsActionPlayback.CreateDefault(),
        -1, 0, -1, AlsActionResultCode.None);
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsActionEarlyBlendOutResult(
    int ClosingSliceIndex,
    float BlendOutSeconds,
    byte Interrupted)
{
    public static AlsActionEarlyBlendOutResult CreateDefault() =>
        new(-1, 0f, 0);
}
