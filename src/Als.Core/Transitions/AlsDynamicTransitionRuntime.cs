using System.Numerics;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Transitions;

public static class AlsDynamicTransitionRuntime
{
    private const float AllowTransitionThreshold = 1f - 1e-5f;

    public static bool TryAdvance(
        in AlsDynamicTransitionBinding binding,
        float deltaTime,
        in AlsDynamicTransitionState current,
        out AlsDynamicTransitionState next,
        out AlsDynamicTransitionPlayback closingPlayback,
        out AlsDynamicTransitionPlayback playback,
        out AlsP5FailureCode failure)
    {
        next = current;
        closingPlayback = AlsDynamicTransitionPlayback.CreateDefault();
        playback = AlsDynamicTransitionPlayback.CreateDefault();

        if (!float.IsFinite(deltaTime) || deltaTime <= 0f)
        {
            failure = AlsP5FailureCode.InvalidDeltaTime;
            return false;
        }

        if (!IsValidBinding(binding))
        {
            failure = AlsP5FailureCode.InvalidBinding;
            return false;
        }

        if (!IsValidState(binding, current))
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        var candidate = current;
        var candidateClosing = AlsDynamicTransitionPlayback.CreateDefault();
        var cooldownBlocked = current.CooldownFrames > 0 ? (byte)1 : (byte)0;
        if (candidate.CooldownFrames > 0)
        {
            candidate.CooldownFrames--;
        }

        var activatesAtFrameStart = (byte)0;
        if (current.Queued == 1)
        {
            if (current.PlaybackEpoch == long.MaxValue)
            {
                failure = AlsP5FailureCode.NonFiniteOutput;
                return false;
            }

            if (current.Active == 1)
            {
                _ = TryResolveClip(binding, current.AnimationId, current.Foot, out var closingClip);
                candidateClosing = new AlsDynamicTransitionPlayback(
                    binding.OccurrenceHandleId,
                    current.AnimationId,
                    current.PlaybackEpoch,
                    current.PlaybackTime,
                    current.PlaybackTime,
                    closingClip.DurationSeconds,
                    binding.PlayRate,
                    binding.BlendSeconds,
                    0d,
                    0,
                    0,
                    0,
                    1);
            }

            candidate.AnimationId = current.QueuedAnimationId;
            candidate.Foot = current.QueuedFoot;
            candidate.PlaybackEpoch = current.PlaybackEpoch + 1;
            candidate.PreviousPlaybackTime = 0f;
            candidate.PlaybackTime = 0f;
            candidate.Active = 1;
            candidate.QueuedAnimationId = -1;
            candidate.QueuedFoot = AlsTransitionFoot.Left;
            candidate.Queued = 0;
            activatesAtFrameStart = 1;
        }

        var candidatePlayback = AlsDynamicTransitionPlayback.CreateDefault() with
        {
            CooldownBlockedThisFrame = cooldownBlocked,
        };

        if (candidate.Active == 1)
        {
            _ = TryResolveClip(binding, candidate.AnimationId, candidate.Foot, out var clip);
            var previous = (double)candidate.PlaybackTime;
            var duration = (double)clip.DurationSeconds;
            var rate = (double)binding.PlayRate;
            var frameDelta = (double)deltaTime;
            var advance = frameDelta * rate;
            var exactCurrent = previous + advance;
            if (!double.IsFinite(advance) || advance <= 0d || !double.IsFinite(exactCurrent))
            {
                failure = AlsP5FailureCode.NonFiniteOutput;
                return false;
            }

            var completes = exactCurrent >= duration;
            var frameEndOffset = completes
                ? System.Math.Min(frameDelta, (duration - previous) / rate)
                : frameDelta;
            if (!double.IsFinite(frameEndOffset) || frameEndOffset < 0d || frameEndOffset > frameDelta)
            {
                failure = AlsP5FailureCode.NonFiniteOutput;
                return false;
            }

            float currentTime;
            if (completes)
            {
                currentTime = clip.DurationSeconds;
            }
            else
            {
                currentTime = (float)exactCurrent;
                if (currentTime >= clip.DurationSeconds)
                {
                    currentTime = MathF.BitDecrement(clip.DurationSeconds);
                }

                if (!float.IsFinite(currentTime) ||
                    currentTime <= candidate.PlaybackTime ||
                    currentTime >= clip.DurationSeconds)
                {
                    failure = AlsP5FailureCode.NonFiniteOutput;
                    return false;
                }
            }

            candidatePlayback = new AlsDynamicTransitionPlayback(
                binding.OccurrenceHandleId,
                candidate.AnimationId,
                candidate.PlaybackEpoch,
                candidate.PlaybackTime,
                currentTime,
                clip.DurationSeconds,
                binding.PlayRate,
                binding.BlendSeconds,
                frameEndOffset,
                1,
                cooldownBlocked,
                activatesAtFrameStart,
                completes ? (byte)1 : (byte)0);

            if (completes)
            {
                ClearActive(ref candidate);
            }
            else
            {
                candidate.PreviousPlaybackTime = candidate.PlaybackTime;
                candidate.PlaybackTime = currentTime;
            }
        }

        next = candidate;
        closingPlayback = candidateClosing;
        playback = candidatePlayback;
        failure = AlsP5FailureCode.None;
        return true;
    }

    public static bool TryQueue(
        in AlsDynamicTransitionBinding binding,
        in AlsDynamicTransitionInput input,
        byte cooldownBlockedThisFrame,
        in AlsDynamicTransitionState current,
        out AlsDynamicTransitionState next,
        out AlsDynamicTransitionQueuedSelection queuedSelection,
        out AlsP5FailureCode failure)
    {
        next = current;
        queuedSelection = AlsDynamicTransitionQueuedSelection.CreateDefault();

        if (!IsValidBinding(binding))
        {
            failure = AlsP5FailureCode.InvalidBinding;
            return false;
        }

        if (!IsValidState(binding, current))
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        if (cooldownBlockedThisFrame is not 0 and not 1)
        {
            failure = AlsP5FailureCode.NonFiniteInput;
            return false;
        }

        if (cooldownBlockedThisFrame == 1)
        {
            failure = AlsP5FailureCode.None;
            return true;
        }

        if (!IsValidInput(input))
        {
            failure = AlsP5FailureCode.NonFiniteInput;
            return false;
        }

        if (input.AllowTransitions < AllowTransitionThreshold)
        {
            failure = AlsP5FailureCode.None;
            return true;
        }

        var thresholdSquared = (double)binding.DistanceMeters * binding.DistanceMeters;
        var leftSquared = input.LeftRelevant == 1
            ? SquaredDistance(input.LeftTarget, input.LeftLock)
            : 0d;
        var rightSquared = input.RightRelevant == 1
            ? SquaredDistance(input.RightTarget, input.RightLock)
            : 0d;
        var leftEligible = input.LeftRelevant == 1 && leftSquared > thresholdSquared;
        var rightEligible = input.RightRelevant == 1 && rightSquared > thresholdSquared;
        if (!leftEligible && !rightEligible)
        {
            failure = AlsP5FailureCode.None;
            return true;
        }

        var foot = leftEligible && (!rightEligible || leftSquared >= rightSquared)
            ? AlsTransitionFoot.Left
            : AlsTransitionFoot.Right;
        var clip = SelectClip(binding, input.Stance, foot);
        var candidate = current;
        candidate.QueuedAnimationId = clip.AnimationId;
        candidate.QueuedFoot = foot;
        candidate.Queued = 1;
        candidate.CooldownFrames = binding.CooldownFrames;

        next = candidate;
        queuedSelection = new AlsDynamicTransitionQueuedSelection(
            clip.AnimationId,
            foot,
            binding.BlendSeconds,
            binding.PlayRate,
            1);
        failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool IsValidBinding(in AlsDynamicTransitionBinding binding)
    {
        if (binding.OccurrenceHandleId < 0 ||
            binding.AuthorityGroupId < 0 ||
            !IsValidClip(binding.StandingLeft) ||
            !IsValidClip(binding.StandingRight) ||
            !IsValidClip(binding.CrouchingLeft) ||
            !IsValidClip(binding.CrouchingRight) ||
            binding.StandingRight.AdditiveBaseAnimationId != binding.StandingLeft.AdditiveBaseAnimationId ||
            binding.CrouchingLeft.AdditiveBaseAnimationId != binding.StandingLeft.AdditiveBaseAnimationId ||
            binding.CrouchingRight.AdditiveBaseAnimationId != binding.StandingLeft.AdditiveBaseAnimationId ||
            !HasCompatibleDuplicate(binding.StandingLeft, binding.StandingRight) ||
            !HasCompatibleDuplicate(binding.StandingLeft, binding.CrouchingLeft) ||
            !HasCompatibleDuplicate(binding.StandingLeft, binding.CrouchingRight) ||
            !HasCompatibleDuplicate(binding.StandingRight, binding.CrouchingLeft) ||
            !HasCompatibleDuplicate(binding.StandingRight, binding.CrouchingRight) ||
            !HasCompatibleDuplicate(binding.CrouchingLeft, binding.CrouchingRight) ||
            !float.IsFinite(binding.DistanceMeters) || binding.DistanceMeters <= 0f ||
            !float.IsFinite(binding.BlendSeconds) || binding.BlendSeconds < 0f ||
            !float.IsFinite(binding.PlayRate) || binding.PlayRate <= 0f ||
            binding.CooldownFrames < 0)
        {
            return false;
        }

        return true;
    }

    private static bool IsValidState(
        in AlsDynamicTransitionBinding binding,
        in AlsDynamicTransitionState state)
    {
        if (state.Active is not 0 and not 1 ||
            state.Queued is not 0 and not 1 ||
            !IsValidFoot(state.Foot) ||
            !IsValidFoot(state.QueuedFoot) ||
            state.PlaybackEpoch < 0 ||
            state.CooldownFrames < 0)
        {
            return false;
        }

        if (state.Active == 0)
        {
            if (state.AnimationId != -1 ||
                BitConverter.SingleToInt32Bits(state.PreviousPlaybackTime) != 0 ||
                BitConverter.SingleToInt32Bits(state.PlaybackTime) != 0 ||
                state.Foot != AlsTransitionFoot.Left)
            {
                return false;
            }
        }
        else
        {
            if (state.PlaybackEpoch <= 0 ||
                !TryResolveClip(binding, state.AnimationId, state.Foot, out var clip) ||
                !float.IsFinite(state.PreviousPlaybackTime) ||
                !float.IsFinite(state.PlaybackTime) ||
                state.PreviousPlaybackTime < 0f ||
                state.PlaybackTime < state.PreviousPlaybackTime ||
                state.PlaybackTime >= clip.DurationSeconds)
            {
                return false;
            }
        }

        if (state.Queued == 0)
        {
            return state.QueuedAnimationId == -1 &&
                state.QueuedFoot == AlsTransitionFoot.Left;
        }

        return TryResolveClip(binding, state.QueuedAnimationId, state.QueuedFoot, out _);
    }

    private static bool IsValidInput(in AlsDynamicTransitionInput input) =>
        input.Stance is AlsStance.Standing or AlsStance.Crouching &&
        float.IsFinite(input.AllowTransitions) &&
        IsFinite(input.LeftTarget) &&
        IsFinite(input.LeftLock) &&
        input.LeftRelevant is 0 or 1 &&
        IsFinite(input.RightTarget) &&
        IsFinite(input.RightLock) &&
        input.RightRelevant is 0 or 1;

    private static bool IsValidClip(in AlsDynamicTransitionClipBinding clip) =>
        clip.AnimationId >= 0 &&
        clip.AdditiveBaseAnimationId >= 0 &&
        float.IsFinite(clip.DurationSeconds) &&
        clip.DurationSeconds > 0f;

    private static bool HasCompatibleDuplicate(
        in AlsDynamicTransitionClipBinding first,
        in AlsDynamicTransitionClipBinding second) =>
        first.AnimationId != second.AnimationId ||
        first.AdditiveBaseAnimationId == second.AdditiveBaseAnimationId &&
        BitConverter.SingleToInt32Bits(first.DurationSeconds) ==
        BitConverter.SingleToInt32Bits(second.DurationSeconds);

    private static bool TryResolveClip(
        in AlsDynamicTransitionBinding binding,
        int animationId,
        AlsTransitionFoot foot,
        out AlsDynamicTransitionClipBinding clip)
    {
        if (foot == AlsTransitionFoot.Left)
        {
            if (binding.StandingLeft.AnimationId == animationId)
            {
                clip = binding.StandingLeft;
                return true;
            }

            if (binding.CrouchingLeft.AnimationId == animationId)
            {
                clip = binding.CrouchingLeft;
                return true;
            }
        }
        else if (foot == AlsTransitionFoot.Right)
        {
            if (binding.StandingRight.AnimationId == animationId)
            {
                clip = binding.StandingRight;
                return true;
            }

            if (binding.CrouchingRight.AnimationId == animationId)
            {
                clip = binding.CrouchingRight;
                return true;
            }
        }

        clip = default;
        return false;
    }

    private static AlsDynamicTransitionClipBinding SelectClip(
        in AlsDynamicTransitionBinding binding,
        AlsStance stance,
        AlsTransitionFoot foot) =>
        stance == AlsStance.Standing
            ? foot == AlsTransitionFoot.Left ? binding.StandingLeft : binding.StandingRight
            : foot == AlsTransitionFoot.Left ? binding.CrouchingLeft : binding.CrouchingRight;

    private static double SquaredDistance(in Vector3 target, in Vector3 locked)
    {
        var x = (double)target.X - locked.X;
        var y = (double)target.Y - locked.Y;
        var z = (double)target.Z - locked.Z;
        return x * x + y * y + z * z;
    }

    private static bool IsFinite(in Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static bool IsValidFoot(AlsTransitionFoot foot) =>
        foot is AlsTransitionFoot.Left or AlsTransitionFoot.Right;

    private static void ClearActive(ref AlsDynamicTransitionState state)
    {
        state.AnimationId = -1;
        state.PreviousPlaybackTime = 0f;
        state.PlaybackTime = 0f;
        state.Foot = AlsTransitionFoot.Left;
        state.Active = 0;
    }
}
