using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Events;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsTimelineEventDefinition(
    int EventId,
    int SourceAnimationId,
    int SourceActionId,
    int RequiredOccurrenceHandleId,
    AlsTimelineSourceKind SourceKind,
    int SourceIndex,
    int TrackIndex,
    int BoundaryOrdinal,
    float TimeSeconds,
    float DurationSeconds,
    float TriggerWeightThreshold,
    AlsTimelineEventKind Kind,
    AlsTimelineTickMode TickMode,
    AlsCompactEventPayload Payload);

[StructLayout(LayoutKind.Sequential)]
public struct AlsTimelineCursor
{
    public int OccurrenceHandleId;
    public int AnimationId;
    public int ActionId;
    public long PlaybackEpoch;
    public double ConsumedUnwrappedTimeSeconds;

    public static AlsTimelineCursor CreateDefault() => new()
    {
        OccurrenceHandleId = -1,
        AnimationId = -1,
        ActionId = -1,
        PlaybackEpoch = 0,
        ConsumedUnwrappedTimeSeconds = 0d,
    };
}

[StructLayout(LayoutKind.Sequential)]
public struct AlsTimelineAuthorityState
{
    public int GroupId;
    public int OccurrenceHandleId;
    public int AnimationId;
    public int ActionId;
    public long PlaybackEpoch;
    public byte Active;

    public static AlsTimelineAuthorityState CreateDefault(int groupId) => new()
    {
        GroupId = groupId,
        OccurrenceHandleId = -1,
        AnimationId = -1,
        ActionId = -1,
        PlaybackEpoch = 0,
        Active = 0,
    };
}

[StructLayout(LayoutKind.Sequential)]
public struct AlsNotifyStateOwnership
{
    public int EventId;
    public int BoundaryOrdinal;
    public int OccurrenceHandleId;
    public int AnimationId;
    public int ActionId;
    public long PlaybackEpoch;
    public long PlaybackCycle;
    public ulong OwnerToken;
    public byte Active;

    public static AlsNotifyStateOwnership CreateDefault() => new()
    {
        EventId = -1,
        BoundaryOrdinal = 0,
        OccurrenceHandleId = -1,
        AnimationId = -1,
        ActionId = -1,
        PlaybackEpoch = 0,
        PlaybackCycle = 0,
        OwnerToken = 0,
        Active = 0,
    };
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsTimelinePlayback(
    int OccurrenceHandleId,
    int AnimationId,
    int ActionId,
    int AuthorityGroupId,
    long PlaybackEpoch,
    double PreviousUnwrappedTimeSeconds,
    double CurrentUnwrappedTimeSeconds,
    double FrameStartOffsetSeconds,
    double FrameEndOffsetSeconds,
    float DurationSeconds,
    float Weight,
    AlsActionResultCode TerminationReason,
    byte Loop,
    byte ActivatesAtWindowStart,
    byte ClosesAfterWindow);

[StructLayout(LayoutKind.Sequential)]
public struct AlsTimelineOccurrence
{
    internal int EventId;
    internal int SourceAnimationId;
    internal int SourceActionId;
    internal int OccurrenceHandleId;
    internal long PlaybackEpoch;
    internal long PlaybackCycle;
    internal ulong OwnerToken;
    internal long TickFrameId;
    internal int SourceIndex;
    internal int BoundaryOrdinal;
    internal float SourceTimeSeconds;
    internal double FrameOccurrenceTimeSeconds;
    internal float Weight;
    internal AlsTimelineEventKind Kind;
    internal AlsAnimationEventPhase Phase;
    internal AlsCompactEventPayload Payload;
    internal byte OldOwnerEnd;
}
