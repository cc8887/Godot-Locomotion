using System.Numerics;

namespace GodotAls.Core.Locomotion;

public enum AlsOverlayAction { Default, Mantling, GettingUp, Rolling }
public readonly record struct AlsOverlayActionBlendState(bool Initialized, AlsOverlayAction Active,
    AlsBinaryBlendChannel Default, AlsBinaryBlendChannel Mantling, AlsBinaryBlendChannel GettingUp,
    AlsBinaryBlendChannel Rolling, Vector4 Weights);
public readonly record struct AlsOverlayActionBlendUpdate(AlsOverlayActionBlendState State, int ZeroWeightPrevious);

/// <summary>Four-child native standard linear BlendList, Default child-update policy.
/// Exact gameplay tag matching is performed by the resource binding.</summary>
public static class AlsOverlayActionBlend
{
    public static AlsOverlayActionBlendUpdate Advance(in AlsOverlayActionBlendState previous,
        AlsOverlayAction action, float delta, Vector4 times)
    {
        if ((uint)action > 3 || !float.IsFinite(delta) || delta < 0)
            throw new ArgumentException("Invalid Overlay action input.");
        for (var i = 0; i < 4; i++) if (!float.IsFinite(times[i]) || times[i] < 0) throw new ArgumentException("Invalid action blend time.");
        Span<AlsBinaryBlendChannel> channels = stackalloc AlsBinaryBlendChannel[4];
        channels[0] = previous.Initialized ? previous.Default : new(0, 1, 1, 1, 0);
        channels[1] = previous.Initialized ? previous.Mantling : new(0, 1, 0, 0, 0);
        channels[2] = previous.Initialized ? previous.GettingUp : new(0, 1, 0, 0, 0);
        channels[3] = previous.Initialized ? previous.Rolling : new(0, 1, 0, 0, 0);
        var weights = previous.Initialized ? previous.Weights : new Vector4(1, 0, 0, 0);
        if (previous.Initialized)
        {
            if ((uint)previous.Active > 3) throw new ArgumentException("Invalid previous action.");
            for (var i = 0; i < 4; i++)
            {
                var c = channels[i];
                if (!Unit(weights[i]) || !Unit(c.Begin) || !Unit(c.Target) || !Unit(c.Alpha) || !Unit(c.Value) || !float.IsFinite(c.Remaining) || c.Remaining < 0)
                    throw new ArgumentException("Invalid action blend history.");
            }
        }
        var zero = -1;
        if (!previous.Initialized || action != previous.Active)
        {
            var seconds = previous.Initialized ? times[(int)action] * System.Math.Clamp(1f - weights[(int)action], 0, 1) : 0;
            if (seconds == 0 && previous.Initialized) zero = (int)previous.Active;
            for (var i = 0; i < 4; i++)
            {
                var begin = weights[i]; var target = i == (int)action ? 1f : 0f;
                var alpha = begin == target ? 1 : System.Math.Clamp((channels[i].Value - begin) / (target - begin), 0, 1);
                if (seconds == 0) alpha = 1;
                channels[i] = new(begin, target, alpha, begin + (target - begin) * alpha, seconds * MathF.Abs(1 - alpha));
            }
        }
        var total = 0f;
        for (var i = 0; i < 4; i++)
        {
            var c = channels[i];
            if (c.Value != c.Target)
            {
                var alpha = c.Remaining > delta ? c.Alpha + (1f - c.Alpha) / c.Remaining * delta : 1;
                alpha = System.Math.Clamp(alpha, 0, 1);
                c = c with { Alpha = alpha, Remaining = MathF.Max(0, c.Remaining - delta), Value = c.Begin + (c.Target - c.Begin) * alpha };
                channels[i] = c;
            }
            weights[i] = c.Value; total += c.Value;
        }
        if (total > AlsPoseBlender.WeightThreshold && MathF.Abs(total - 1) > AlsPoseBlender.WeightThreshold)
            weights *= 1f / total;
        return new(new(true, action, channels[0], channels[1], channels[2], channels[3], weights), zero);
    }

    public static AlsOverlayAction Resolve(string tag) => tag switch
    {
        "Als.LocomotionAction.Mantling" => AlsOverlayAction.Mantling,
        "Als.LocomotionAction.GettingUp" => AlsOverlayAction.GettingUp,
        "Als.LocomotionAction.Rolling" => AlsOverlayAction.Rolling,
        _ => AlsOverlayAction.Default
    };
    private static bool Unit(float value) => float.IsFinite(value) && value is >= 0 and <= 1;
}
