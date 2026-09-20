namespace GodotAls.Core.Sync;

// Marker symbols are compiled numeric identities shared by the entire animation set; zero is None.
public readonly record struct AlsAssetSyncMarker(int Symbol, float TimeSeconds);
public readonly record struct AlsAssetSyncSequence(int AnimationId, float DurationSeconds, float RateScale,
    int MarkerStart, int MarkerCount);
public enum AlsAssetSyncKind : byte { Sequence, BlendSpace }
public enum AlsAssetSyncRole : byte { CanBeLeader, AlwaysFollower }

/// <summary>Non-mirrored, non-evaluator source player. Time is normalized for a
/// BlendSpace, seconds for a Sequence. Samples are already filtered and in native cache order.</summary>
public readonly record struct AlsAssetSyncPlayer(int PlayerId, int AssetId, long Epoch, AlsAssetSyncKind Kind,
    float Time, float PlayRate, float Weight, int SampleStart, int SampleCount, ulong AssetMarkerMask,
    bool Looping = true, bool LegacyLength = true, bool MatchSyncPhases = false,
    bool RequestedInertialization = false, bool OverridePositionWhenJoining = false,
    AlsAssetSyncRole Role = AlsAssetSyncRole.CanBeLeader);

public readonly record struct AlsAssetSyncSample(int SampleId, int SequenceIndex, float Weight,
    float RateScale = 1, float CachedPlayRate = 1);

public readonly record struct AlsAssetMarkerRecord(int PreviousIndex, int NextIndex,
    float PreviousDistance, float NextDistance, bool Initialized = true)
{
    public static AlsAssetMarkerRecord Invalid => new(-2, -2, 0, 0, false);
}

public readonly record struct AlsAssetMarkerPosition(int PreviousSymbol, int NextSymbol, float Alpha)
{
    public bool Valid => PreviousSymbol != 0 || NextSymbol != 0;
}

public readonly record struct AlsAssetSampleHistory(int SampleId, int AnimationId, float Time,
    float PreviousTime, AlsAssetMarkerRecord Marker, float DeltaPrevious, float Delta);

public readonly record struct AlsAssetPlayerHistory(int PlayerId, int AssetId, long Epoch,
    float Time, float DeltaPrevious, float Delta, AlsAssetMarkerRecord Marker,
    int SampleStart, int SampleCount);

// Indexed like playerOutput, while Order records actual native dispatch order. Every attempted
// marker leader ticks with Leader=true, including an attempt before the final winning leader.
public readonly record struct AlsAssetPlayerTickContext(int PlayerId, int Order, bool Leader);

public readonly record struct AlsAssetSyncGroupHistory(int GroupId, bool HasLeader, int LeaderPlayerId,
    int LeaderAssetId, long LeaderEpoch, int SortedLeaderIndex, float LeaderScore, ulong ValidMarkerMask,
    float PreviousRatio, float Ratio, AlsAssetMarkerPosition MarkerStart, AlsAssetMarkerPosition MarkerEnd);
