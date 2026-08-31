using System.Runtime.InteropServices;

namespace GodotAls.Core.Sync;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsSyncMarkerDefinition(
    int MarkerId,
    int MarkerNameId,
    int AnimationId,
    int SourceIndex,
    int TrackIndex,
    float TimeSeconds);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsSyncGroupBinding(
    int GroupId,
    int MemberOffset,
    int MemberCount,
    int LeftMarkerNameId,
    int RightMarkerNameId);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsSyncMemberBinding(
    int GroupId,
    int AnimationId,
    float DurationSeconds,
    byte Loop,
    byte CanLead);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsSyncPlayback(
    int OccurrenceHandleId,
    int AnimationId,
    long PlaybackEpoch,
    double PreviousUnwrappedTimeSeconds,
    double CurrentUnwrappedTimeSeconds,
    float Weight);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsSyncMappedPlayback(
    int OccurrenceHandleId,
    int AnimationId,
    long PlaybackEpoch,
    float DurationSeconds,
    long PreviousCycle,
    long CurrentCycle,
    float PreviousTimeSeconds,
    float CurrentTimeSeconds,
    float MappedPlayRate);
