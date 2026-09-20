using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Events;

public enum AlsAnimationEventPhase : byte
{
    Trigger = 0,
    Begin = 1,
    Tick = 2,
    End = 3,
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsNativeNotifyEventContext(bool Present, int InstanceId, float CurrentAnimationTime,
    float CallbackSeconds, bool ActiveContext, bool ReachedEnd);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsAnimationEvent(
    int EventId,
    int SourceAnimationId,
    int SourceActionId,
    int OccurrenceHandleId,
    long PlaybackEpoch,
    long PlaybackCycle,
    ulong OwnerToken,
    long EventSequence,
    int BoundaryOrdinal,
    float AnimationTime,
    float Weight,
    AlsTimelineEventKind Kind,
    AlsAnimationEventPhase Phase,
    AlsCompactEventPayload Payload)
{
    // Native callbacks occur at frame dispatch; AnimationTime remains the frame-relative offset.
    // CurrentAnimationTime is the source Tick accumulator (normalized for BlendSpace), not that offset.
    public AlsNativeNotifyEventContext NativeContext { get; init; }
}
