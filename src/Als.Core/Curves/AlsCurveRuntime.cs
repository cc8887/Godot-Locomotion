using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Curves;

public enum AlsCurveInterpolationMode : byte
{
    Constant = 0,
    Linear = 1,
    Cubic = 2,
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsCurveKey(
    float TimeSeconds,
    float Value,
    float ArriveTangent,
    float LeaveTangent,
    AlsCurveInterpolationMode Interpolation);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsCurveBinding(
    int CurveId,
    int KeyOffset,
    int KeyCount,
    float DurationSeconds,
    byte Required,
    byte Loop);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsCurveBlendSample(
    int BindingIndex,
    long Cycle,
    float TimeSeconds,
    float Weight);

public static class AlsCurveRuntime
{
    public static bool TrySample(
        in AlsCurveBinding binding,
        ReadOnlySpan<AlsCurveKey> keys,
        long cycle,
        float timeSeconds,
        out float value,
        out AlsP5FailureCode failure)
    {
        value = 0f;
        failure = AlsP5FailureCode.None;

        if (!TryValidateBinding(binding, keys.Length, out var missing))
        {
            failure = AlsP5FailureCode.InvalidBinding;
            return false;
        }
        if (!TryValidateTimeline(binding, cycle, timeSeconds, out failure))
        {
            return false;
        }
        if (missing)
        {
            return true;
        }

        var curveKeys = keys.Slice(binding.KeyOffset, binding.KeyCount);
        ref readonly var first = ref curveKeys[0];
        ref readonly var last = ref curveKeys[^1];
        if (!float.IsFinite(first.TimeSeconds) ||
            !float.IsFinite(last.TimeSeconds) ||
            !float.IsFinite(first.Value) ||
            !float.IsFinite(last.Value) ||
            (curveKeys.Length > 1 && last.TimeSeconds <= first.TimeSeconds))
        {
            failure = AlsP5FailureCode.InvalidBinding;
            return false;
        }

        if (timeSeconds <= first.TimeSeconds)
        {
            value = first.Value;
            return true;
        }
        if (timeSeconds >= last.TimeSeconds)
        {
            value = last.Value;
            return true;
        }

        var low = 0;
        var high = curveKeys.Length - 1;
        var lowTime = first.TimeSeconds;
        var highTime = last.TimeSeconds;
        while (high - low > 1)
        {
            var middle = low + ((high - low) >> 1);
            var middleTime = curveKeys[middle].TimeSeconds;
            if (!float.IsFinite(middleTime) ||
                middleTime <= lowTime ||
                middleTime >= highTime)
            {
                failure = AlsP5FailureCode.InvalidBinding;
                return false;
            }
            if (middleTime <= timeSeconds)
            {
                low = middle;
                lowTime = middleTime;
            }
            else
            {
                high = middle;
                highTime = middleTime;
            }
        }

        ref readonly var left = ref curveKeys[low];
        ref readonly var right = ref curveKeys[high];
        if (!IsFinite(left) ||
            !IsFinite(right) ||
            (uint)left.Interpolation > (uint)AlsCurveInterpolationMode.Cubic)
        {
            failure = AlsP5FailureCode.InvalidBinding;
            return false;
        }

        var duration = (double)right.TimeSeconds - left.TimeSeconds;
        if (!double.IsFinite(duration) || duration <= 0d)
        {
            failure = AlsP5FailureCode.InvalidBinding;
            return false;
        }
        if (timeSeconds == left.TimeSeconds)
        {
            value = left.Value;
            return true;
        }
        if (timeSeconds == right.TimeSeconds)
        {
            value = right.Value;
            return true;
        }

        var alpha = ((double)timeSeconds - left.TimeSeconds) / duration;
        var calculated = left.Interpolation switch
        {
            AlsCurveInterpolationMode.Constant => left.Value,
            AlsCurveInterpolationMode.Linear =>
                ((double)left.Value * (1d - alpha)) + ((double)right.Value * alpha),
            AlsCurveInterpolationMode.Cubic => Hermite(left, right, duration, alpha),
            _ => double.NaN,
        };
        if (!IsFiniteFloat(calculated))
        {
            failure = AlsP5FailureCode.NonFiniteOutput;
            return false;
        }

        var sampled = (float)calculated;
        if (!float.IsFinite(sampled))
        {
            failure = AlsP5FailureCode.NonFiniteOutput;
            return false;
        }
        value = sampled;
        return true;
    }

    public static bool TryBlend(
        ReadOnlySpan<AlsCurveBinding> bindings,
        ReadOnlySpan<AlsCurveKey> keys,
        ReadOnlySpan<AlsCurveBlendSample> samples,
        out float value,
        out AlsP5FailureCode failure)
    {
        value = 0f;
        failure = AlsP5FailureCode.None;
        var accumulated = 0d;
        var totalWeight = 0d;

        for (var sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
        {
            ref readonly var sample = ref samples[sampleIndex];
            if (!float.IsFinite(sample.Weight) || sample.Weight < 0f)
            {
                failure = AlsP5FailureCode.NonFiniteInput;
                return false;
            }
            if ((uint)sample.BindingIndex >= (uint)bindings.Length)
            {
                failure = AlsP5FailureCode.InvalidBinding;
                return false;
            }
            if (!TrySample(
                    bindings[sample.BindingIndex], keys, sample.Cycle, sample.TimeSeconds,
                    out var sampleValue, out failure))
            {
                return false;
            }

            accumulated += (double)sampleValue * sample.Weight;
            totalWeight += sample.Weight;
            if (!IsFiniteFloat(accumulated) || !IsFiniteFloat(totalWeight))
            {
                failure = AlsP5FailureCode.NonFiniteOutput;
                return false;
            }
        }

        if (totalWeight == 0d)
        {
            return true;
        }

        var blended = accumulated / totalWeight;
        if (!IsFiniteFloat(blended))
        {
            failure = AlsP5FailureCode.NonFiniteOutput;
            return false;
        }
        value = (float)blended;
        if (!float.IsFinite(value))
        {
            value = 0f;
            failure = AlsP5FailureCode.NonFiniteOutput;
            return false;
        }
        return true;
    }

    public static bool TryBlendAdditiveToDefault(
        float defaultValue,
        float clampMinimum,
        float clampMaximum,
        ReadOnlySpan<AlsCurveBinding> bindings,
        ReadOnlySpan<AlsCurveKey> keys,
        ReadOnlySpan<AlsCurveBlendSample> samples,
        out float value,
        out AlsP5FailureCode failure)
    {
        value = 0f;
        failure = AlsP5FailureCode.None;
        if (!float.IsFinite(defaultValue) ||
            !float.IsFinite(clampMinimum) ||
            !float.IsFinite(clampMaximum) ||
            clampMinimum > clampMaximum)
        {
            failure = AlsP5FailureCode.InvalidBinding;
            return false;
        }

        var accumulated = (double)defaultValue;
        for (var sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
        {
            ref readonly var sample = ref samples[sampleIndex];
            if (!float.IsFinite(sample.Weight) || sample.Weight < 0f)
            {
                failure = AlsP5FailureCode.NonFiniteInput;
                return false;
            }
            if ((uint)sample.BindingIndex >= (uint)bindings.Length)
            {
                failure = AlsP5FailureCode.InvalidBinding;
                return false;
            }

            ref readonly var binding = ref bindings[sample.BindingIndex];
            if (!TrySample(
                    binding, keys, sample.Cycle, sample.TimeSeconds,
                    out var sampleValue, out failure))
            {
                return false;
            }
            if (binding.KeyCount != 0)
            {
                accumulated += (double)sampleValue * sample.Weight;
                if (!IsFiniteFloat(accumulated))
                {
                    failure = AlsP5FailureCode.NonFiniteOutput;
                    return false;
                }
            }
        }

        var clamped = System.Math.Clamp(accumulated, (double)clampMinimum, clampMaximum);
        if (!IsFiniteFloat(clamped))
        {
            failure = AlsP5FailureCode.NonFiniteOutput;
            return false;
        }
        value = (float)clamped;
        if (!float.IsFinite(value))
        {
            value = 0f;
            failure = AlsP5FailureCode.NonFiniteOutput;
            return false;
        }
        if (value == 0f)
        {
            value = 0f;
        }
        return true;
    }

    public static bool TryAdvancePlaybackTime(
        float durationSeconds,
        byte loop,
        long currentCycle,
        float currentTimeSeconds,
        float deltaSeconds,
        float playRate,
        out long nextCycle,
        out float nextTimeSeconds,
        out AlsP5FailureCode failure)
    {
        nextCycle = 0;
        nextTimeSeconds = 0f;
        failure = AlsP5FailureCode.None;

        if (loop > 1 ||
            !float.IsFinite(durationSeconds) ||
            durationSeconds < 0f ||
            (loop == 1 && durationSeconds <= 0f))
        {
            failure = AlsP5FailureCode.InvalidBinding;
            return false;
        }
        if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0f)
        {
            failure = AlsP5FailureCode.InvalidDeltaTime;
            return false;
        }
        if (!float.IsFinite(playRate) || playRate < 0f)
        {
            failure = AlsP5FailureCode.NonFiniteInput;
            return false;
        }
        if (!float.IsFinite(currentTimeSeconds))
        {
            failure = AlsP5FailureCode.NonFiniteInput;
            return false;
        }

        if (loop == 0)
        {
            if (currentCycle != 0 ||
                currentTimeSeconds < 0f ||
                currentTimeSeconds > durationSeconds)
            {
                failure = AlsP5FailureCode.InvalidTimeline;
                return false;
            }

            var advanced = (double)currentTimeSeconds + ((double)deltaSeconds * playRate);
            if (!double.IsFinite(advanced))
            {
                failure = AlsP5FailureCode.NonFiniteOutput;
                return false;
            }
            if (advanced <= 0d || durationSeconds == 0f)
            {
                return true;
            }

            nextTimeSeconds = advanced >= durationSeconds
                ? durationSeconds
                : (float)advanced;
            if (!float.IsFinite(nextTimeSeconds))
            {
                nextTimeSeconds = 0f;
                failure = AlsP5FailureCode.NonFiniteOutput;
                return false;
            }
            return true;
        }

        if (currentCycle < 0 ||
            currentTimeSeconds < 0f ||
            currentTimeSeconds >= durationSeconds)
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        var advance = (double)deltaSeconds * playRate;
        if (!double.IsFinite(advance))
        {
            failure = AlsP5FailureCode.NonFiniteOutput;
            return false;
        }
        if (!TrySplitForwardAdvance(
                advance, durationSeconds,
                out var advanceCycles, out var advanceRemainder, out failure))
        {
            return false;
        }

        var combinedRemainder = advanceRemainder + currentTimeSeconds;
        if (!double.IsFinite(combinedRemainder))
        {
            failure = AlsP5FailureCode.NonFiniteOutput;
            return false;
        }
        uint carry = 0;
        if (combinedRemainder >= durationSeconds)
        {
            combinedRemainder -= durationSeconds;
            carry = 1;
        }
        if (combinedRemainder < 0d || combinedRemainder >= durationSeconds)
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        var resultingTime = (float)combinedRemainder;
        if (!float.IsFinite(resultingTime))
        {
            failure = AlsP5FailureCode.NonFiniteOutput;
            return false;
        }
        if (resultingTime >= durationSeconds)
        {
            carry++;
            resultingTime = 0f;
        }
        else if (resultingTime == 0f)
        {
            resultingTime = 0f;
        }

        var availableCycles = (UInt128)(ulong)(long.MaxValue - currentCycle);
        if (advanceCycles > availableCycles || carry > availableCycles - advanceCycles)
        {
            failure = AlsP5FailureCode.InvalidTimeline;
            return false;
        }

        var cycleIncrement = advanceCycles + carry;
        nextCycle = currentCycle + (long)cycleIncrement;
        nextTimeSeconds = resultingTime;
        return true;
    }

    private static bool TrySplitForwardAdvance(
        double advance,
        float durationSeconds,
        out UInt128 cycles,
        out double remainder,
        out AlsP5FailureCode failure)
    {
        cycles = 0;
        remainder = 0d;
        failure = AlsP5FailureCode.None;
        if (advance == 0d)
        {
            return true;
        }

        DecomposePositive(advance, out var advanceSignificand, out var advanceExponent);
        DecomposePositive(durationSeconds, out var durationSignificand, out var durationExponent);
        var exponentShift = advanceExponent - durationExponent;
        UInt128 remainderSignificand;
        int remainderExponent;
        if (exponentShift >= 0)
        {
            var source = (UInt128)advanceSignificand;
            if (exponentShift >= 128 || source > (UInt128.MaxValue >> exponentShift))
            {
                failure = AlsP5FailureCode.InvalidTimeline;
                return false;
            }

            var numerator = source << exponentShift;
            cycles = numerator / durationSignificand;
            remainderSignificand = numerator % durationSignificand;
            remainderExponent = durationExponent;
        }
        else
        {
            var divisorShift = -exponentShift;
            var source = (UInt128)advanceSignificand;
            var unshiftedDivisor = (UInt128)durationSignificand;
            if (divisorShift >= 128 ||
                unshiftedDivisor > (UInt128.MaxValue >> divisorShift))
            {
                remainderSignificand = source;
                remainderExponent = advanceExponent;
            }
            else
            {
                var divisor = unshiftedDivisor << divisorShift;
                cycles = source / divisor;
                remainderSignificand = source % divisor;
                remainderExponent = advanceExponent;
            }
        }

        remainder = System.Math.ScaleB((double)(ulong)remainderSignificand, remainderExponent);
        if (!double.IsFinite(remainder) || remainder < 0d || remainder >= durationSeconds)
        {
            cycles = 0;
            remainder = 0d;
            failure = AlsP5FailureCode.NonFiniteOutput;
            return false;
        }
        return true;
    }

    private static void DecomposePositive(
        double value,
        out ulong significand,
        out int exponent)
    {
        var bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
        var exponentField = (int)((bits >> 52) & 0x7FFUL);
        significand = bits & 0x000F_FFFF_FFFF_FFFFUL;
        if (exponentField == 0)
        {
            exponent = -1074;
            return;
        }

        significand |= 1UL << 52;
        exponent = exponentField - 1075;
    }

    private static void DecomposePositive(
        float value,
        out uint significand,
        out int exponent)
    {
        var bits = unchecked((uint)BitConverter.SingleToInt32Bits(value));
        var exponentField = (int)((bits >> 23) & 0xFFU);
        significand = bits & 0x007F_FFFFU;
        if (exponentField == 0)
        {
            exponent = -149;
            return;
        }

        significand |= 1U << 23;
        exponent = exponentField - 150;
    }

    private static bool TryValidateBinding(
        in AlsCurveBinding binding,
        int keysLength,
        out bool missing)
    {
        missing = false;
        if (binding.Required > 1 ||
            binding.Loop > 1 ||
            !float.IsFinite(binding.DurationSeconds) ||
            binding.DurationSeconds < 0f ||
            (binding.Loop == 1 && binding.DurationSeconds <= 0f) ||
            binding.KeyOffset < 0 ||
            binding.KeyOffset > keysLength ||
            binding.KeyCount < 0 ||
            binding.KeyCount > keysLength - binding.KeyOffset)
        {
            return false;
        }

        if (binding.KeyCount == 0)
        {
            if (binding.Required != 0 || binding.CurveId != -1)
            {
                return false;
            }
            missing = true;
            return true;
        }

        return binding.CurveId >= 0;
    }

    private static bool TryValidateTimeline(
        in AlsCurveBinding binding,
        long cycle,
        float timeSeconds,
        out AlsP5FailureCode failure)
    {
        failure = AlsP5FailureCode.None;
        if (!float.IsFinite(timeSeconds))
        {
            failure = AlsP5FailureCode.NonFiniteInput;
            return false;
        }

        if (binding.Loop == 0)
        {
            if (cycle == 0)
            {
                return true;
            }
        }
        else if (cycle >= 0 && timeSeconds >= 0f && timeSeconds < binding.DurationSeconds)
        {
            return true;
        }

        failure = AlsP5FailureCode.InvalidTimeline;
        return false;
    }

    private static bool IsFinite(in AlsCurveKey key) =>
        float.IsFinite(key.TimeSeconds) &&
        float.IsFinite(key.Value) &&
        float.IsFinite(key.ArriveTangent) &&
        float.IsFinite(key.LeaveTangent);

    private static bool IsFiniteFloat(double value) =>
        double.IsFinite(value) && value >= -float.MaxValue && value <= float.MaxValue;

    private static double Hermite(
        in AlsCurveKey left,
        in AlsCurveKey right,
        double duration,
        double alpha)
    {
        var alpha2 = alpha * alpha;
        var alpha3 = alpha2 * alpha;
        var h00 = (2d * alpha3) - (3d * alpha2) + 1d;
        var h10 = alpha3 - (2d * alpha2) + alpha;
        var h01 = (-2d * alpha3) + (3d * alpha2);
        var h11 = alpha3 - alpha2;
        return (h00 * left.Value) +
            (h10 * left.LeaveTangent * duration) +
            (h01 * right.Value) +
            (h11 * right.ArriveTangent * duration);
    }
}
