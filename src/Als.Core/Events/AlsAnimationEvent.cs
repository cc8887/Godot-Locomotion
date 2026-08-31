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
    AlsCompactEventPayload Payload);
