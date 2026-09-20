namespace GodotAls.Core.Sync;

/// <summary>One non-looping sequence tick in native graph traversal order. PlayRate includes asset RateScale.
/// This length-sync path requires CanBeLeader roles and no effective group markers.</summary>
public readonly record struct AlsLengthSyncPlayback(int OccurrenceHandleId, int AnimationId, long PlaybackEpoch,
    float DurationSeconds, float TimeSeconds, float PlayRate, float Weight,
    bool RequestedInertialization = false, bool OverridePositionWhenJoiningAsLeader = false);

/// <summary>AdvanceSeconds is UE's tick delta, not necessarily CurrentTimeSeconds - PreviousTimeSeconds.
/// In particular the leader preserves requested advance when clamped at a non-looping endpoint.</summary>
public readonly record struct AlsLengthSyncMappedPlayback(int OccurrenceHandleId, int AnimationId, long PlaybackEpoch,
    float PreviousTimeSeconds, float CurrentTimeSeconds, float AdvanceSeconds);

public readonly record struct AlsLengthSyncGroupState(int GroupId, int LeaderOccurrenceHandleId,
    int LeaderAnimationId, long LeaderPlaybackEpoch, float LeaderScore, float PreviousRatio, float Ratio, bool HasLeader);
