namespace GodotAls.Core.Locomotion;

public readonly record struct AlsBinaryBlendSettings(float FirstSeconds, float SecondSeconds, AlsTransitionBlend Blend);
public readonly record struct AlsBinaryBlendChannel(float Begin, float Target, float Alpha, float Value, float Remaining);
public readonly record struct AlsBinaryBlendState(bool Initialized, int ActiveChild,
    AlsBinaryBlendChannel First, AlsBinaryBlendChannel Second, float FirstWeight, float SecondWeight);
public readonly record struct AlsBinaryBlendUpdate(AlsBinaryBlendState State, int ZeroWeightPreviousChild);

/// <summary>Two-child UE BlendList with StandardBlend, no profile and Default child update mode.
/// The caller owns node initialization/relevance and dispatches nonzero children in pin order.</summary>
public static class AlsBinaryBlendList
{
    public static AlsBinaryBlendUpdate Advance(in AlsBinaryBlendState previous, int child, float delta,
        in AlsBinaryBlendSettings settings)
    {
        if ((uint)child > 1 || !float.IsFinite(delta) || delta < 0 ||
            !float.IsFinite(settings.FirstSeconds) || settings.FirstSeconds < 0 ||
            !float.IsFinite(settings.SecondSeconds) || settings.SecondSeconds < 0 ||
            (uint)settings.Blend > (uint)AlsTransitionBlend.HermiteCubic ||
            previous.Initialized && ((uint)previous.ActiveChild > 1 ||
                !Valid(previous.First) || !Valid(previous.Second) ||
                !Weight(previous.FirstWeight) || !Weight(previous.SecondWeight)))
            throw new ArgumentException("Invalid binary BlendList state or input.");
        var a = previous.Initialized ? previous.First : new(0, 1, 1, 1, 0);
        var b = previous.Initialized ? previous.Second : new AlsBinaryBlendChannel(0, 1, 0, 0, 0);
        var wa = previous.Initialized ? previous.FirstWeight : 1;
        var wb = previous.Initialized ? previous.SecondWeight : 0;
        var zeroPrevious = -1;
        if (!previous.Initialized || previous.ActiveChild != child)
        {
            var seconds = previous.Initialized ? (child == 0 ? settings.FirstSeconds : settings.SecondSeconds) *
                System.Math.Clamp(1 - (child == 0 ? wa : wb), 0, 1) : 0;
            if (seconds == 0 && previous.Initialized) zeroPrevious = previous.ActiveChild;
            a = Retarget(a.Value, wa, child == 0 ? 1 : 0, seconds, settings.Blend);
            b = Retarget(b.Value, wb, child == 1 ? 1 : 0, seconds, settings.Blend);
        }
        a = Tick(a, delta, settings.Blend); b = Tick(b, delta, settings.Blend);
        wa = a.Value; wb = b.Value;
        var total = wa + wb;
        if (total > AlsPoseBlender.WeightThreshold && MathF.Abs(total - 1) > AlsPoseBlender.WeightThreshold)
        { var reciprocal = 1 / total; wa *= reciprocal; wb *= reciprocal; }
        return new(new(true, child, a, b, wa, wb), zeroPrevious);
    }

    private static AlsBinaryBlendChannel Retarget(float value, float begin, float target, float seconds, AlsTransitionBlend blend)
    {
        var alpha = begin == target ? 1 : System.Math.Clamp((value - begin) / (target - begin), 0, 1);
        if (seconds <= 0) alpha = 1;
        return new(begin, target, alpha, begin + (target - begin) * AlsTransitionStack.Alpha(alpha, blend),
            seconds * MathF.Abs(1 - alpha));
    }

    private static AlsBinaryBlendChannel Tick(AlsBinaryBlendChannel value, float delta, AlsTransitionBlend blend)
    {
        if (value.Value == value.Target) return value;
        var alpha = value.Remaining > delta ? value.Alpha + (1 - value.Alpha) / value.Remaining * delta : 1;
        alpha = System.Math.Clamp(alpha, 0, 1);
        return value with { Alpha = alpha, Remaining = MathF.Max(0, value.Remaining - delta),
            Value = value.Begin + (value.Target - value.Begin) * AlsTransitionStack.Alpha(alpha, blend) };
    }
    private static bool Weight(float value) => float.IsFinite(value) && value is >= 0 and <= 1;
    private static bool Valid(in AlsBinaryBlendChannel value) => Weight(value.Begin) && Weight(value.Target) &&
        Weight(value.Alpha) && Weight(value.Value) && float.IsFinite(value.Remaining) && value.Remaining >= 0;
}
