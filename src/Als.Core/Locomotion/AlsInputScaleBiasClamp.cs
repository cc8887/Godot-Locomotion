namespace GodotAls.Core.Locomotion;

// Shared FInputScaleBiasClamp operation. The existing policy is also used by
// ALS animation nodes; callers decide whether to clamp the final output to 0..1.
public static class AlsInputScaleBiasClamp
{
    public static float Apply(float input, in AlsOverlayAlphaPolicy policy, float delta,
        ref bool initialized, ref float history)
    {
        if (!float.IsFinite(input) || !float.IsFinite(delta) || delta < 0)
            throw new ArgumentException("Invalid scale/bias/clamp update.");
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
                var speed = value >= history ? policy.Increasing : policy.Decreasing;
                var distance = value - history;
                if (speed > 0 && distance * distance >= 1e-8f)
                    value = history + distance * System.Math.Clamp(delta * speed, 0, 1);
            }
            history = value;
        }
        initialized = true;
        if (!float.IsFinite(value)) throw new ArgumentException("Nonfinite scale/bias/clamp output.");
        return value;
    }
}
