using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraEvaluatorSourceCandidate(AlsSequenceEvaluatorTick Preparation,
    AlsAssetSyncPlayer Player);

// The original graph supplies the resolved sequence and explicit seconds.
// This adapter owns no extra clock: its caller supplies occurrence history and
// sends the returned record to the character's one common Sync pass.
internal static class LyraEvaluatorSourceTick
{
    public static LyraEvaluatorSourceCandidate Prepare(LyraSourceNode node, int playerId, int assetId,
        long epoch, float previousTime, float explicitTime, in AlsAssetSyncSequence sequence,
        float delta, float weight, int sampleStart, ulong markerMask, bool reinitialized,
        bool requestedInertialization = false, AlsAssetSyncRole graphScopeRole = AlsAssetSyncRole.CanBeLeader,
        AlsAssetMarkerRecord? markerRecord = null)
    {
        if (node.Kind != LyraSourceKind.SequenceEvaluator || playerId < 0 || assetId < 0 || epoch <= 0 ||
            !float.IsFinite(weight) || weight < 0 || sampleStart < 0)
            throw new ArgumentException("Invalid Lyra evaluator occurrence.");
        var role = node.Method == LyraSourceSyncMethod.Graph ? graphScopeRole : node.Role switch
        { LyraSourceGroupRole.CanBeLeader => AlsAssetSyncRole.CanBeLeader,
            LyraSourceGroupRole.AlwaysFollower => AlsAssetSyncRole.AlwaysFollower,
            LyraSourceGroupRole.AlwaysLeader => AlsAssetSyncRole.AlwaysLeader,
            _ => throw new NotSupportedException("Transition/exclusive source roles require their native registration policy.") };
        if (!Enum.IsDefined(role)) throw new NotSupportedException("Invalid outer Graph Sync role.");
        var preparation = AlsSequenceEvaluatorPreparation.Prepare(explicitTime, previousTime,
            sequence.DurationSeconds, delta, sequence.RateScale, node.Looping,
            node.Settings.GetProperty("teleport").GetBoolean(), node.Group is not ("" or "None"),
            node.Method == LyraSourceSyncMethod.Graph, reinitialized,
            (AlsSequenceEvaluatorReinitialization)node.Settings.GetProperty("reinitialization").GetInt32(),
            node.Settings.GetProperty("startPosition").GetSingle());
        return new(preparation, new(playerId, assetId, epoch, AlsAssetSyncKind.Sequence,
            preparation.Time, preparation.PlayRate, weight, sampleStart, 1, markerMask,
            Looping: node.Looping, RequestedInertialization: requestedInertialization,
            OverridePositionWhenJoining: node.OverridePositionWhenJoining, Role: role, IsEvaluator: true, MarkerRecord: markerRecord));
    }
}
