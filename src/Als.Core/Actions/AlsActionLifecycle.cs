using GodotAls.Core.Contracts;

namespace GodotAls.Core.Actions;

internal static class AlsActionLifecycle
{
    public static bool IsValid(in AlsActionLifecycleSettings settings) =>
        settings.Mode == AlsActionLifecycleMode.LegacySectionEnd
            ? settings == default
            : settings.Mode is AlsActionLifecycleMode.MontageAutoBlendOut or AlsActionLifecycleMode.MontageHoldAtEnd &&
                float.IsFinite(settings.BlendInSeconds) && settings.BlendInSeconds >= 0f &&
                float.IsFinite(settings.BlendOutSeconds) && settings.BlendOutSeconds >= 0f &&
                float.IsFinite(settings.BlendOutTriggerSeconds) &&
                settings.BlendInOption is >= AlsActionBlendOption.Linear and <= AlsActionBlendOption.HermiteCubic &&
                settings.BlendOutOption is >= AlsActionBlendOption.Linear and <= AlsActionBlendOption.HermiteCubic;

    public static AlsActionLifecycleState Start(in AlsActionLifecycleSettings settings)
    {
        var state = new AlsActionLifecycleState();
        if (settings.Mode != AlsActionLifecycleMode.LegacySectionEnd)
        {
            SetRange(ref state, 1f, settings.BlendInSeconds, settings.BlendInOption);
        }
        return state;
    }

    public static bool IsValid(in AlsActionLifecycleSettings settings, in AlsActionLifecycleState state)
    {
        if (settings.Mode == AlsActionLifecycleMode.LegacySectionEnd)
        {
            return IsDefault(state);
        }
        if (state.BlendingOut > 1 || state.TraversalFinished > 1 ||
            state.BlendingOut == 1 && settings.Mode != AlsActionLifecycleMode.MontageAutoBlendOut ||
            !IsWeight(state.Alpha) || !IsWeight(state.BeginWeight) || !IsWeight(state.CurrentWeight) ||
            !float.IsFinite(state.RemainingSeconds) || state.RemainingSeconds < 0f ||
            IsNegativeZero(state.RemainingSeconds) ||
            state.DesiredWeight != (state.BlendingOut == 1 ? 0f : 1f) || IsNegativeZero(state.DesiredWeight) ||
            state.BlendingOut == 0 && state.BeginWeight != 0f ||
            state.RemainingSeconds == 0f && state.Alpha != 1f ||
            state.BlendingOut == 1 && state.CurrentWeight == 0f ||
            state.TraversalFinished == 1 && settings.Mode == AlsActionLifecycleMode.MontageAutoBlendOut && state.BlendingOut == 0)
        {
            return false;
        }
        var maximumRemaining = state.BlendingOut == 0
            ? settings.BlendInSeconds
            : settings.BlendOutTriggerSeconds >= 0f
                ? settings.BlendOutSeconds : MathF.Max(settings.BlendOutSeconds, .0001f);
        if (state.RemainingSeconds > maximumRemaining)
        {
            return false;
        }
        var option = state.BlendingOut == 1 ? settings.BlendOutOption : settings.BlendInOption;
        var expected = state.BeginWeight + (state.DesiredWeight - state.BeginWeight) * Shape(state.Alpha, option);
        return state.CurrentWeight == expected;
    }

    public static bool IsDefault(in AlsActionLifecycleState state) =>
        BitConverter.SingleToInt32Bits(state.Alpha) == 0 &&
        BitConverter.SingleToInt32Bits(state.RemainingSeconds) == 0 &&
        BitConverter.SingleToInt32Bits(state.BeginWeight) == 0 &&
        BitConverter.SingleToInt32Bits(state.CurrentWeight) == 0 &&
        BitConverter.SingleToInt32Bits(state.DesiredWeight) == 0 &&
        state.BlendingOut == 0 && state.TraversalFinished == 0;

    public static void AdvanceWeight(
        in AlsActionLifecycleSettings settings, float delta, ref AlsActionLifecycleState state)
    {
        if (settings.Mode == AlsActionLifecycleMode.LegacySectionEnd || state.CurrentWeight == state.DesiredWeight)
        {
            return;
        }
        if (state.RemainingSeconds > delta)
        {
            state.Alpha += ((1f - state.Alpha) / state.RemainingSeconds) * delta;
            state.RemainingSeconds -= delta;
        }
        else
        {
            state.Alpha = 1f;
            state.RemainingSeconds = 0f;
        }
        SetAlpha(ref state, state.Alpha,
            state.BlendingOut == 1 ? settings.BlendOutOption : settings.BlendInOption);
    }

    public static void TryBeginBlendOut(
        in AlsActionLifecycleSettings settings, float remainingPlayTime,
        ref AlsActionLifecycleState state)
    {
        if (settings.Mode != AlsActionLifecycleMode.MontageAutoBlendOut || state.BlendingOut == 1)
        {
            return;
        }
        var custom = settings.BlendOutTriggerSeconds >= 0f;
        var trigger = custom ? settings.BlendOutTriggerSeconds : settings.BlendOutSeconds;
        if (remainingPlayTime <= MathF.Max(trigger, .0001f))
        {
            state.BlendingOut = 1;
            SetRange(ref state, 0f,
                custom ? settings.BlendOutSeconds : remainingPlayTime, settings.BlendOutOption);
        }
    }

    public static bool IsComplete(in AlsActionLifecycleState state) =>
        // UE completion uses the shaped output endpoint, which can precede timer zero.
        state.BlendingOut == 1 && state.CurrentWeight == state.DesiredWeight;

    private static void SetRange(
        ref AlsActionLifecycleState state, float desired, float duration, AlsActionBlendOption option)
    {
        state.BeginWeight = state.CurrentWeight;
        state.DesiredWeight = desired;
        var alpha = state.BeginWeight == desired ? 1f : 0f;
        SetAlpha(ref state, duration <= 0f ? 1f : alpha, option);
        state.RemainingSeconds = duration <= 0f ? 0f : duration * MathF.Abs(1f - state.Alpha);
    }

    private static void SetAlpha(ref AlsActionLifecycleState state, float alpha, AlsActionBlendOption option)
    {
        state.Alpha = System.Math.Clamp(alpha, 0f, 1f);
        state.CurrentWeight = state.BeginWeight +
            (state.DesiredWeight - state.BeginWeight) * Shape(state.Alpha, option);
    }

    private static float Shape(float alpha, AlsActionBlendOption option)
    {
        var alpha2 = alpha * alpha;
        var value = option switch
        {
            AlsActionBlendOption.Cubic => (-2f * (alpha2 * alpha)) + (3f * alpha2),
            AlsActionBlendOption.HermiteCubic => alpha2 * (3f - 2f * alpha),
            _ => alpha,
        };
        return System.Math.Clamp(value, 0f, 1f);
    }

    private static bool IsWeight(float value) => value is >= 0f and <= 1f && !IsNegativeZero(value);
    private static bool IsNegativeZero(float value) => BitConverter.SingleToInt32Bits(value) == int.MinValue;
}
