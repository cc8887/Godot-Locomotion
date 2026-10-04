namespace GodotAls.Core.Curves;

/// <summary>The native nonlooping sequence distance advancement. Sampling is
/// supplied by the resource's actual curve codec; the result is explicit time,
/// which must still be registered with the common animation synchronizer.</summary>
public static class AlsDistanceMatching
{
    /// <summary>Native DistanceMatchToTarget buffer lookup. The signed target
    /// is negated inside the library and extrapolation remains explicit time;
    /// source preparation owns the subsequent clip clamp.</summary>
    public static float MatchToTarget(float distanceToTarget, ReadOnlySpan<float> times, ReadOnlySpan<float> values)
    {
        if (!float.IsFinite(distanceToTarget) || times.Length < 2 || times.Length != values.Length)
            throw new ArgumentException("Invalid distance buffer lookup.");
        var target = -distanceToTarget;
        var first = 1; var count = values.Length - 2;
        while (count > 0)
        {
            var step = count / 2; var middle = first + step;
            if (target > values[middle]) { first = middle + 1; count -= step + 1; }
            else count = step;
        }
        var difference = values[first] - values[first - 1];
        var alpha = MathF.Abs(difference) <= 1e-8f ? 0 : (target - values[first - 1]) / difference;
        var time = times[first - 1] + alpha * (times[first] - times[first - 1]);
        return float.IsFinite(time) ? time : throw new InvalidOperationException("Nonfinite distance buffer result.");
    }

    public static float AdvanceNonLooping(float currentTime, float distance, float frameDelta,
        float length, float distanceRange, Func<float, float> sample, double clampMin, double clampMax)
    {
        if (!float.IsFinite(currentTime) || !float.IsFinite(distance) || !float.IsFinite(frameDelta) || frameDelta < 0 ||
            !float.IsFinite(length) || length <= 0 || !float.IsFinite(distanceRange) ||
            !double.IsFinite(clampMin) || !double.IsFinite(clampMax) || sample is null)
            throw new ArgumentException("Invalid native distance matching inputs.");
        if (frameDelta <= 0 || distance <= 0) return currentTime;
        var next = currentTime;
        if (MathF.Abs(distanceRange) > 1e-8f)
        {
            const float step = 1f / 30f;
            float accumulatedDistance = 0, accumulatedTime = 0;
            while (accumulatedDistance < distance && next + step < length)
            {
                var distanceNow = sample(next);
                var distanceAfter = sample(next + step);
                if (!float.IsFinite(distanceNow) || !float.IsFinite(distanceAfter))
                    throw new InvalidOperationException("Nonfinite distance curve sample.");
                var stepDistance = distanceAfter - distanceNow;
                if (MathF.Abs(stepDistance) > 1e-8f)
                {
                    if (accumulatedDistance + stepDistance < distance)
                    {
                        next = System.Math.Clamp(next + step, 0, length);
                        accumulatedDistance += stepDistance;
                    }
                    else
                    {
                        var alpha = (distance - accumulatedDistance) / stepDistance;
                        next = System.Math.Clamp(next + alpha * step, 0, length);
                        break;
                    }
                }
                else next += step;
                accumulatedTime += step;
                if (accumulatedTime >= length) break;
            }
        }
        if (next < currentTime) next += length;
        var effectiveRate = (next - currentTime) / frameDelta;
        if (clampMin >= 0 && clampMin < clampMax)
            effectiveRate = (float)System.Math.Clamp((double)effectiveRate, clampMin, clampMax);
        var result = System.Math.Clamp(currentTime + effectiveRate * frameDelta, 0, length);
        return float.IsFinite(result) ? result : throw new InvalidOperationException("Nonfinite distance matching time.");
    }
}
