using System.Numerics;
using System.Runtime.CompilerServices;

namespace GodotAls.Core.Locomotion;

[InlineArray(4)] internal struct AlsOverlayBlendChannels { private AlsBinaryBlendChannel _element; }
public struct AlsOverlayBlendListState
{
    public bool Initialized { get; internal set; }
    public int ActiveChild { get; internal set; }
    public Vector4 Weights { get; internal set; }
    internal AlsOverlayBlendChannels Channels;
}
public readonly record struct AlsOverlayBlendListUpdate(AlsOverlayBlendListState State, int ZeroWeightPreviousChild,
    int InitializeChild, float InertialSeconds);

public static class AlsOverlayPoseWeights
{
    // FInputScaleBiasClamp retains an unclamped interpolation history; the
    // TwoWay node clamps its final alpha separately after this operation.
    public static float Alpha(float input, in AlsOverlayAlphaPolicy policy, float delta, ref bool initialized, ref float history)
    {
        if (!float.IsFinite(input) || !float.IsFinite(delta) || delta < 0) throw new ArgumentException("Invalid Overlay alpha update.");
        var value = input;
        if (policy.MapRange)
        {
            var range = policy.InputMax - policy.InputMin;
            var percent = MathF.Abs(range) <= 1e-8f ? input >= policy.InputMax ? 1f : 0f : (input - policy.InputMin) / range;
            value = policy.OutputMin + (policy.OutputMax - policy.OutputMin) * percent;
        }
        value = value * policy.Scale + policy.Bias;
        if (policy.Clamp) value = System.Math.Clamp(value, policy.Minimum, policy.Maximum);
        if (policy.Interpolate)
        {
            if (initialized)
            {
                var speed = value >= history ? policy.Increasing : policy.Decreasing; var distance = value - history;
                if (speed > 0 && distance * distance >= 1e-8f)
                    value = history + distance * System.Math.Clamp(delta * speed, 0, 1);
            }
            history = value;
        }
        initialized = true;
        if (!float.IsFinite(value)) throw new ArgumentException("Nonfinite Overlay alpha.");
        return System.Math.Clamp(value, 0, 1);
    }

    public static Vector4 MultiWay(Vector4 desired, int count)
    {
        if (count is not (2 or 4) || !float.IsFinite(desired.LengthSquared())) throw new ArgumentException("Invalid Overlay multiway weights.");
        var total = 0f; for (var i = 0; i < count; i++) total += desired[i];
        var result = Vector4.Zero;
        if (System.Math.Clamp(total, 0, 1) > AlsPoseBlender.WeightThreshold)
            for (var i = 0; i < count; i++) result[i] = System.Math.Clamp(desired[i] / total, 0, 1);
        return result;
    }

    public static AlsOverlayBlendListUpdate BlendList(in AlsOverlayBlendListState previous, int selected,
        ReadOnlySpan<float> times, AlsTransitionBlend blend, bool inertial, bool resetChild, float delta)
    {
        if (times.Length is not (2 or 4) || !float.IsFinite(delta) || delta < 0 || blend is not (AlsTransitionBlend.Linear or AlsTransitionBlend.HermiteCubic))
            throw new ArgumentException("Invalid Overlay BlendList update.");
        foreach (var time in times) if (!float.IsFinite(time) || time < 0) throw new ArgumentException("Invalid blend duration.");
        var state = previous; selected = System.Math.Clamp(selected, 0, times.Length - 1);
        if (!state.Initialized)
        {
            state.Weights = new(1, 0, 0, 0);
            for (var i = 0; i < times.Length; i++) state.Channels[i] = new(0, 1, i == 0 ? 1 : 0, i == 0 ? 1 : 0, 0);
        }
        var zeroPrevious = -1; var initialize = -1; var request = -1f;
        if (!state.Initialized || state.ActiveChild != selected)
        {
            var currentWeight = state.Weights[selected];
            var seconds = !state.Initialized || inertial ? 0 : times[selected] * System.Math.Clamp(MathF.Abs(1 - currentWeight), 0, 1);
            if (state.Initialized && inertial) request = times[selected];
            if (state.Initialized && seconds == 0) zeroPrevious = state.ActiveChild;
            for (var i = 0; i < times.Length; i++)
            {
                var begin = state.Weights[i]; var target = i == selected ? 1f : 0f; var old = state.Channels[i];
                var alpha = begin == target ? 1 : System.Math.Clamp((old.Value - begin) / (target - begin), 0, 1);
                if (seconds <= 0) alpha = 1;
                state.Channels[i] = new(begin, target, alpha, begin + (target - begin) * AlsTransitionStack.Alpha(alpha, blend), seconds * MathF.Abs(1 - alpha));
            }
            if (resetChild && currentWeight <= AlsPoseBlender.WeightThreshold) initialize = selected;
            state.ActiveChild = selected; state.Initialized = true;
        }
        var sum = 0f; var weights = state.Weights;
        for (var i = 0; i < times.Length; i++)
        {
            var channel = state.Channels[i];
            if (channel.Value != channel.Target)
            {
                var alpha = channel.Remaining > delta ? channel.Alpha + (1 - channel.Alpha) / channel.Remaining * delta : 1;
                alpha = System.Math.Clamp(alpha, 0, 1);
                channel = channel with { Alpha = alpha, Remaining = MathF.Max(0, channel.Remaining - delta),
                    Value = channel.Begin + (channel.Target - channel.Begin) * AlsTransitionStack.Alpha(alpha, blend) };
            }
            state.Channels[i] = channel; weights[i] = channel.Value; sum += channel.Value;
        }
        if (sum > AlsPoseBlender.WeightThreshold && MathF.Abs(sum - 1) > AlsPoseBlender.WeightThreshold)
            weights *= 1 / sum;
        state.Weights = weights;
        return new(state, zeroPrevious, initialize, request);
    }
}
