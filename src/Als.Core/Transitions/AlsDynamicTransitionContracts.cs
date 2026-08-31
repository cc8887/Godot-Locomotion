using System.Numerics;
using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Transitions;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsDynamicTransitionClipBinding(
    int AnimationId,
    int AdditiveBaseAnimationId,
    float DurationSeconds);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsDynamicTransitionBinding(
    int OccurrenceHandleId,
    int AuthorityGroupId,
    AlsDynamicTransitionClipBinding StandingLeft,
    AlsDynamicTransitionClipBinding StandingRight,
    AlsDynamicTransitionClipBinding CrouchingLeft,
    AlsDynamicTransitionClipBinding CrouchingRight,
    float DistanceMeters,
    float BlendSeconds,
    float PlayRate,
    int CooldownFrames);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsDynamicTransitionInput(
    AlsStance Stance,
    float AllowTransitions,
    Vector3 LeftTarget,
    Vector3 LeftLock,
    byte LeftRelevant,
    Vector3 RightTarget,
    Vector3 RightLock,
    byte RightRelevant);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsDynamicTransitionPlayback(
    int OccurrenceHandleId,
    int AnimationId,
    long PlaybackEpoch,
    float PreviousTime,
    float CurrentTime,
    float DurationSeconds,
    float PlayRate,
    float BlendSeconds,
    double FrameEndOffsetSeconds,
    byte ContributesThisFrame,
    byte CooldownBlockedThisFrame,
    byte ActivatesAtFrameStart,
    byte ClosesAfterFrame)
{
    public static AlsDynamicTransitionPlayback CreateDefault() => new(
        -1,
        -1,
        0,
        0f,
        0f,
        0f,
        0f,
        0f,
        0d,
        0,
        0,
        0,
        0);
}
