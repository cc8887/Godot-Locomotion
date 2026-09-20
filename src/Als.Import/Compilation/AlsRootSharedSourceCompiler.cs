using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Import.Compilation;

public sealed record AlsRootSharedSourceProfile(AlsLocomotionSourceProfile Sources, AlsOverlaySharedSourceProfile Overlay,
    int RagdollPlayerId, int RagdollSampleId, string RagdollBindingDigest);

// Append the final root's SequencePlayer without renumbering Main/Overlay or
// deduplicating playback identity by animation asset. ALS_Flail is also in Jump.
public static class AlsRootSharedSourceCompiler
{
    public static AlsRootSharedSourceProfile Compile(AlsOverlaySharedSourceProfile overlay, AlsRagdollPoseProfile pose,
        AlsRagdollFrameDefinition frame, AlsAnimationSetDefinition set)
    {
        var old = overlay.Sources; var players = old.Players; var samples = old.Samples;
        if (players.Length != 223 || samples.Length != 257 || old.AnimationSetDefinitionDigest != set.DefinitionDigest ||
            old.SkeletonId != pose.SkeletonId || pose.AnimationId != frame.Sequence.AnimationId || pose.PlayerNodeIndex != frame.PlayerNodeIndex ||
            players.Any(p => p.CompiledNodeIndex == frame.PlayerNodeIndex || p.SourceNode == pose.PlayerPath || p.Domain == AlsLocomotionSourceDomain.Ragdoll))
            throw new ArgumentException("Root source closure is stale, aliased or incomplete.");
        var animation = set.Animations.Single(a => a.Id == pose.AnimationId);
        var sequence = old.SyncSequences.Single(s => s.AnimationId == animation.Id);
        if (sequence.DurationSeconds != frame.Sequence.DurationSeconds || sequence.RateScale != frame.Sequence.RateScale ||
            sequence.MarkerCount != 0 || frame.Sequence.MarkerCount != 0 || animation.Timeline.Length != 0 || animation.SyncMarkers.Length != 0)
            throw new ArgumentException("Root Flail source differs from the shared asset policy.");
        var view = old.CreateCoreView();
        var range = view.NotifyRanges.ToArray().Single(r => r.AnimationId == animation.Id);
        if (range.Count != 0) throw new ArgumentException("Ragdoll Flail requires additional notify bindings.");
        var player = new AlsLocomotionSourcePlayer(players.Length, pose.PlayerPath, frame.PlayerNodeIndex,
            AlsLocomotionSourceKind.Sequence, AlsLocomotionSourceDomain.Ragdoll, -1, 0, 1, 1, "FlailRate", true,
            samples.Length, 1, -1, "", "");
        var sample = new AlsLocomotionSourceSample(samples.Length, player.PlayerId, 0, animation.Id,
            0, 0, 0, 1, sequence.RateScale, sequence.DurationSeconds, animation.AdditiveBasePoseAnimationId);
        var sync = new AlsLocomotionSourceSyncBinding(player.PlayerId, animation.Id, 0, false, false, false, AlsBlendSpaceNotifyMode.AllAnimations);
        var combined = new AlsLocomotionSourceProfile(old.SkeletonId, old.SourceGraphDigest, set.DefinitionDigest,
            [..players, player], [..samples, sample], old.SyncGroups,
            new([..old.RuntimeSyncPlayers, sync], old.SyncSequences, old.SyncMarkers, old.MarkerSymbols),
            new(view.NotifyRanges.ToArray(), view.NotifyDefinitions.ToArray(), view.NotifyPolicies.ToArray(), old.NotifyObjects, old.NotifyNames),
            old.Sprint, old.Digest + "\nragdoll\n" + pose.BindingDigest);
        return new(combined, new(combined, overlay.MovementPlayerCount, overlay.MovementSampleCount,
            overlay.Overlay.ToArray(), overlay.ClockBindingDigest), player.PlayerId, sample.SampleId, pose.BindingDigest);
    }
}
