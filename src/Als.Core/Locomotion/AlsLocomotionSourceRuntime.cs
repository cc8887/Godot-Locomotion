using GodotAls.Core.Contracts;
using GodotAls.Core.Sync;

namespace GodotAls.Core.Locomotion;

public static class AlsLocomotionSourceRuntime
{
    public static bool HasValidInputPolicy(in AlsLocomotionSourcePlayerBinding player)
    {
        var rotation = player.PlayRateInput == AlsSourceRateInput.RotateRate;
        return player.PlayRateInput <= AlsSourceRateInput.FlailRate && player.LoopInput <= AlsSourceLoopInput.RotateRight &&
            (player.PlayRateInput != AlsSourceRateInput.FlailRate ||
                player.Domain == AlsLocomotionSourceDomain.Ragdoll && player.Kind == AlsLocomotionSourceKind.Sequence && player.SyncGroupId == -1 &&
                player.InputX == AlsSourceAxisInput.None && player.InputY == AlsSourceAxisInput.None && player.Loop) &&
            (player.PlayRateInput != AlsSourceRateInput.JumpPlayRate ||
                player.Domain == AlsLocomotionSourceDomain.Jump && player.Kind == AlsLocomotionSourceKind.Sequence && player.SyncGroupId >= 0 &&
                player.InputX == AlsSourceAxisInput.None && player.InputY == AlsSourceAxisInput.None) &&
            (player.PlayRateInput != AlsSourceRateInput.CrouchingPlayRate ||
                player.Domain == AlsLocomotionSourceDomain.Crouching && player.Kind == AlsLocomotionSourceKind.Sequence && player.SyncGroupId >= 0 &&
                player.InputX == AlsSourceAxisInput.None && player.InputY == AlsSourceAxisInput.None) &&
            rotation == (player.LoopInput != AlsSourceLoopInput.Constant) && (!rotation ||
                player.Kind == AlsLocomotionSourceKind.Sequence && player.Domain is AlsLocomotionSourceDomain.Standing or AlsLocomotionSourceDomain.Crouching &&
                player.SyncGroupId == -1 && player.InputX == AlsSourceAxisInput.None && player.InputY == AlsSourceAxisInput.None);
    }

    /// <summary>Bind resolved graph contributions to native asset ticks. This does not advance
    /// time or allocate occurrence handles. Tick identity, rates and sample ownership come from
    /// one immutable source view; foreign views and teleport evaluators fail before any output.</summary>
    public static bool TryBuildTicks(in AlsLocomotionSourceView source, in AlsLocomotionSourceStamp expectedStamp,
        ReadOnlySpan<AlsLocomotionSourceUpdate> updates, ReadOnlySpan<AlsLocomotionSampleUpdate> samples,
        float standingPlayRate, Span<AlsAssetSyncPlayer> playerOutput, Span<AlsAssetSyncSample> sampleOutput,
        Span<int> groupOutput, out AlsP5FailureCode failure, AlsSourceRotationInput? rotationInput = null, float? crouchingPlayRate = null,
        float? jumpPlayRate = null, float? flailRate = null)
    {
        failure = AlsP5FailureCode.InvalidBinding;
        if (!source.Stamp.IsValid || source.Stamp != expectedStamp || source.Players.IsEmpty ||
            source.Players.Length != source.SyncPlayers.Length || source.GroupIds.Length > AlsSyncRuntime.MaxAssetSyncBatchGroups ||
            updates.Length > AlsSyncRuntime.MaxAssetSyncBatchPlayers || samples.Length > AlsSyncRuntime.MaxAssetSyncBatchSamples ||
            playerOutput.Length < updates.Length || groupOutput.Length < updates.Length || sampleOutput.Length < samples.Length) return false;
        if (!float.IsFinite(standingPlayRate)) { failure = AlsP5FailureCode.NonFiniteInput; return false; }
        if (rotationInput is { } rotation && !float.IsFinite(rotation.Rate))
        { failure = AlsP5FailureCode.NonFiniteInput; return false; }
        if (crouchingPlayRate is { } crouching && !float.IsFinite(crouching))
        { failure = AlsP5FailureCode.NonFiniteInput; return false; }
        if (jumpPlayRate is { } jump && !float.IsFinite(jump))
        { failure = AlsP5FailureCode.NonFiniteInput; return false; }
        if (flailRate is { } flail && !float.IsFinite(flail))
        { failure = AlsP5FailureCode.NonFiniteInput; return false; }
        Span<AlsAssetSyncPlayer> ticks = stackalloc AlsAssetSyncPlayer[updates.Length];
        Span<AlsAssetSyncSample> sampleTicks = stackalloc AlsAssetSyncSample[samples.Length];
        Span<int> groups = stackalloc int[updates.Length];
        Span<int> identities = stackalloc int[System.Math.Max(updates.Length, samples.Length)];
        var cursor = 0;
        for (var i = 0; i < updates.Length; i++)
        {
            var update = updates[i];
            if ((uint)update.PlayerId >= source.Players.Length || update.Epoch <= 0 || update.SampleStart != cursor ||
                update.SampleCount < 1 || update.SampleCount > samples.Length - cursor) return false;
            var player = source.Players[update.PlayerId]; var sync = source.SyncPlayers[update.PlayerId];
            if (player.PlayerId != update.PlayerId || sync.PlayerId != update.PlayerId || sync.AssetId < 0 || sync.Role > AlsAssetSyncRole.AlwaysFollower ||
                player.Kind is not (AlsLocomotionSourceKind.BlendSpace or AlsLocomotionSourceKind.Sequence) ||
                !HasValidInputPolicy(player) || player.PlayRateInput == AlsSourceRateInput.RotateRate && rotationInput is null ||
                player.PlayRateInput == AlsSourceRateInput.CrouchingPlayRate && crouchingPlayRate is null ||
                player.PlayRateInput == AlsSourceRateInput.JumpPlayRate && jumpPlayRate is null ||
                player.PlayRateInput == AlsSourceRateInput.FlailRate && flailRate is null ||
                player.SampleStart < 0 || player.SampleCount <= 0 ||
                player.SampleStart > source.Samples.Length - player.SampleCount || update.SampleCount > player.SampleCount ||
                player.SyncGroupId < -1 || player.SyncGroupId >= 0 && !source.GroupIds.Contains(player.SyncGroupId) ||
                !float.IsFinite(player.DefaultPlayRate) || !float.IsFinite(player.PlayRateBasis) || player.PlayRateBasis <= 0 ||
                player.Kind == AlsLocomotionSourceKind.BlendSpace && player.PlayRateBasis != 1 ||
                player.Kind == AlsLocomotionSourceKind.Sequence && (player.SampleCount != 1 || update.SampleCount != 1)) return false;
            // Absolute update-path weight follows FAnimationUpdateContext into
            // FAnimTickRecord unchanged. It is not a normalized sample weight;
            // LayeredBoneBlend may supply an authored/rounded value above one.
            if (!float.IsFinite(update.Time) || update.Time < 0 || !float.IsFinite(update.Weight) || update.Weight < 0)
            { failure = AlsP5FailureCode.NonFiniteInput; return false; }
            var length = player.Kind == AlsLocomotionSourceKind.BlendSpace ? 1 : source.Samples[player.SampleStart].DurationSeconds;
            if (!float.IsFinite(length) || length <= 0 || update.Time > length) return false;
            var playRate = player.PlayRateInput switch
            {
                AlsSourceRateInput.StandingPlayRate => standingPlayRate * player.DefaultPlayRate,
                AlsSourceRateInput.RotateRate => rotationInput!.Value.Rate,
                AlsSourceRateInput.CrouchingPlayRate => crouchingPlayRate!.Value,
                AlsSourceRateInput.JumpPlayRate => jumpPlayRate!.Value,
                AlsSourceRateInput.FlailRate => flailRate!.Value,
                _ => player.DefaultPlayRate,
            };
            var loop = player.LoopInput switch
            {
                AlsSourceLoopInput.RotateLeft => rotationInput!.Value.Left,
                AlsSourceLoopInput.RotateRight => rotationInput!.Value.Right,
                _ => player.Loop,
            };
            if (!float.IsFinite(playRate)) { failure = AlsP5FailureCode.NonFiniteOutput; return false; }
            if (player.Kind == AlsLocomotionSourceKind.Sequence)
                playRate = MathF.Abs(player.PlayRateBasis) <= 1e-8f ? 0 : playRate / player.PlayRateBasis;
            if (!float.IsFinite(playRate)) { failure = AlsP5FailureCode.NonFiniteOutput; return false; }
            for (var n = cursor; n < cursor + update.SampleCount; n++)
            {
                var input = samples[n];
                if (input.SampleId < player.SampleStart || input.SampleId >= player.SampleStart + player.SampleCount) return false;
                var sample = source.Samples[input.SampleId];
                if (sample.SampleId != input.SampleId || sample.PlayerId != player.PlayerId ||
                    sample.SourceIndex != input.SampleId - player.SampleStart || (uint)sample.SequenceIndex >= source.Sequences.Length ||
                    source.Sequences[sample.SequenceIndex].AnimationId != sample.AnimationId ||
                    source.Sequences[sample.SequenceIndex].DurationSeconds != sample.DurationSeconds ||
                    source.Sequences[sample.SequenceIndex].RateScale != sample.AssetRateScale ||
                    !float.IsFinite(sample.SampleRateScale) || sample.SampleRateScale <= 0) return false;
                if (!float.IsFinite(input.Weight) || input.Weight is < 0 or > 1 || !float.IsFinite(input.CachedPlayRate))
                { failure = AlsP5FailureCode.NonFiniteInput; return false; }
                if (player.Kind == AlsLocomotionSourceKind.Sequence && (input.Weight != 1 || input.CachedPlayRate != 1)) return false;
                sampleTicks[n] = new(input.SampleId, sample.SequenceIndex, input.Weight, sample.SampleRateScale, input.CachedPlayRate);
            }
            ticks[i] = new(player.PlayerId, sync.AssetId, update.Epoch,
                player.Kind == AlsLocomotionSourceKind.BlendSpace ? AlsAssetSyncKind.BlendSpace : AlsAssetSyncKind.Sequence,
                update.Time, playRate, update.Weight, cursor, update.SampleCount, sync.MarkerMask,
                loop, sync.LegacyLength, sync.MatchSyncPhases, update.RequestedInertialization, Role: sync.Role);
            groups[i] = player.SyncGroupId; identities[i] = player.PlayerId;
            cursor += update.SampleCount;
        }
        if (cursor != samples.Length || Duplicate(identities[..updates.Length])) return false;
        for (var i = 0; i < samples.Length; i++) identities[i] = samples[i].SampleId;
        if (Duplicate(identities[..samples.Length])) return false;
        ticks.CopyTo(playerOutput); sampleTicks.CopyTo(sampleOutput); groups.CopyTo(groupOutput);
        failure = AlsP5FailureCode.None;
        return true;

        static bool Duplicate(Span<int> ids)
        {
            ids.Sort();
            for (var i = 1; i < ids.Length; i++) if (ids[i] == ids[i - 1]) return true;
            return false;
        }
    }
}
