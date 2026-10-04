using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Sync;

public readonly record struct AlsBlendSpaceSampleWeight(int SampleId, float Weight, float WeightRate = 0);

// UBlendSpace sample smoothing without per-bone overrides. The caller owns
// history, invokes this at common Sync, and publishes only its whole candidate.
public static class AlsBlendSpaceWeightSmoothing
{
    public static int Evaluate(ReadOnlySpan<AlsBlendSpaceSampleWeight> previous,
        ReadOnlySpan<AlsBlendSpaceSampleWeight> target, float delta, float speed, bool easeInOut,
        Span<AlsBlendSpaceSampleWeight> output)
    {
        if (!float.IsFinite(delta) || delta < 0 || !float.IsFinite(speed) || speed < 0 ||
            previous.Length > 128 || target.IsEmpty || target.Length > 128 || output.Length < previous.Length + target.Length)
            throw new ArgumentException("Invalid BlendSpace weight smoothing inputs.");
        Validate(previous); Validate(target);
        Span<AlsBlendSpaceSampleWeight> result = stackalloc AlsBlendSpaceSampleWeight[256];
        var count = 0; var clampedTotal = 0f;
        foreach (var old in previous)
        {
            var found = false;
            foreach (var next in target)
            {
                if (old.SampleId != next.SampleId) continue;
                var smooth = Smooth(old, next.Weight, delta, speed, easeInOut);
                if (smooth.Weight > AlsPoseBlender.WeightThreshold)
                { result[count++] = smooth; clampedTotal += System.Math.Clamp(smooth.Weight, 0, 1); found = true; break; }
            }
            if (found) continue;
            var faded = Smooth(old, 0, delta, speed, easeInOut, old.WeightRate);
            if (faded.Weight > AlsPoseBlender.WeightThreshold)
            { result[count++] = faded; clampedTotal += System.Math.Clamp(faded.Weight, 0, 1); }
        }
        foreach (var next in target)
        {
            var found = false;
            for (var i = 0; i < count; i++) if (result[i].SampleId == next.SampleId) { found = true; break; }
            if (found) continue;
            var added = Smooth(new(next.SampleId, 0), next.Weight, delta, speed, easeInOut);
            if (added.Weight > AlsPoseBlender.WeightThreshold)
            { result[count++] = added; clampedTotal += System.Math.Clamp(added.Weight, 0, 1); }
        }
        if (clampedTotal <= AlsPoseBlender.WeightThreshold)
        { target.CopyTo(output); return target.Length; }
        var total = 0f;
        for (var i = 0; i < count; i++) total += result[i].Weight;
        if (total > AlsPoseBlender.WeightThreshold)
        {
            if (MathF.Abs(total - 1) > AlsPoseBlender.WeightThreshold)
                for (var i = 0; i < count; i++) result[i] = result[i] with { Weight = result[i].Weight / total };
        }
        else for (var i = 0; i < count; i++) result[i] = result[i] with { Weight = 1f / count };
        for (var i = 0; i < count; i++)
            if (!float.IsFinite(result[i].Weight) || !float.IsFinite(result[i].WeightRate))
                throw new ArgumentException("Nonfinite BlendSpace weight result.");
        result[..count].CopyTo(output);
        return count;
    }
    private static AlsBlendSpaceSampleWeight Smooth(in AlsBlendSpaceSampleWeight old, float target, float delta, float speed, bool ease, float retainedRate = 0)
    {
        if (speed <= 0) return old with { Weight = target, WeightRate = retainedRate };
        if (!ease)
        {
            var difference = target - old.Weight;
            return old with { Weight = difference * difference < 1e-8f ? target
                : old.Weight + System.Math.Clamp(difference, -speed * delta, speed * delta), WeightRate = retainedRate };
        }
        var smoothingTime = speed > 1.1920929e-7f ? 1 / (2.71828182845904523536f * speed) : 0;
        if (smoothingTime <= 1e-4f)
            return old with { Weight = target, WeightRate = delta > 0 ? (target - old.Weight) / delta : 0 };
        var frequency = 2 / smoothingTime;
        // The native BlendSpace kernel retains the expanded InvExpApprox tree.
        // The Rig Unit's separately verified Horner tree differs by one ULP at
        // 30 Hz and must keep its own evaluation order.
        var x = frequency * delta;
        var exponential = 1f / (1f + 1.00746054f * x + .45053901f * x * x + .25724632f * x * x * x);
        var error = old.Weight - target;
        var b = (old.WeightRate + error * frequency) * delta;
        return old with { Weight = target + (error + b) * exponential,
            WeightRate = (old.WeightRate - b * frequency) * exponential };
    }
    private static void Validate(ReadOnlySpan<AlsBlendSpaceSampleWeight> values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            if (value.SampleId < 0 || !float.IsFinite(value.Weight) || value.Weight < 0 || !float.IsFinite(value.WeightRate))
                throw new ArgumentException("Invalid BlendSpace weight history.");
            for (var j = 0; j < i; j++) if (values[j].SampleId == value.SampleId)
                throw new ArgumentException("Duplicate BlendSpace weight identity.");
        }
    }
}
