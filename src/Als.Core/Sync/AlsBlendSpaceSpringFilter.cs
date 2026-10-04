namespace GodotAls.Core.Sync;

public readonly record struct AlsBlendSpaceSpringFilterState(bool Seeded, float Value, float Rate, float Output);

// FFIRFilterTimeBased's non-wrapping critical SpringDamper axis. History is
// immutable so a cancelled character frame cannot advance the input filter.
public static class AlsBlendSpaceSpringFilter
{
    public static AlsBlendSpaceSpringFilterState Advance(in AlsBlendSpaceSpringFilterState state,
        float input, float delta, float window, float min, float max)
    {
        if (!float.IsFinite(input) || !float.IsFinite(delta) || delta < 0 ||
            !float.IsFinite(window) || window < 0 || !float.IsFinite(min) || !float.IsFinite(max) || min > max)
            throw new ArgumentException("Invalid BlendSpace spring axis.");
        if (window <= 0) return state with { Output = input };
        if (delta <= 1e-4f) return state;
        var value = state.Seeded ? state.Value : input;
        var rate = state.Seeded ? state.Rate : 0;
        var time = window / 2.71828182845904523536f;
        var frequency = 1f / (3.1415926535897932f * time);
        var w = frequency * 6.2831853071795864f;
        var error = value - input;
        var c2 = rate + error * w;
        var x = w * delta;
        var exponential = 1f / (1f + 1.00746054f * x + .45053901f * x * x + .25724632f * x * x * x);
        value = input + (error + c2 * delta) * exponential;
        rate = (c2 - error * w - c2 * (w * delta)) * exponential;
        if (value > max) { value = max; if (rate > 0) rate = 0; }
        if (value < min) { value = min; if (rate < 0) rate = 0; }
        return new(true, value, rate, value);
    }
}
