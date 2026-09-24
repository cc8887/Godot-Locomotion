namespace GodotAls.Core.Actions;

// AAlsCharacter::CalculateMantlingStartTime. Heights are native centimeters;
// sampleRootHeight must read the montage's absolute, unlocked root track, not
// ExtractRootMotion's interval delta or the rendered/retargeted skeleton.
public static class AlsMantlingStartTime
{
    public static float MapHeight(float height, float referenceLow, float referenceHigh, float timeLow, float timeHigh)
    {
        Finite(height); Finite(referenceLow); Finite(referenceHigh); Finite(timeLow); Finite(timeHigh);
        var range = referenceHigh - referenceLow;
        // FMath::GetRangePct handles a nearly-point range as a step function.
        var alpha = MathF.Abs(range) <= 1e-8f ? (height >= referenceHigh ? 1f : 0f)
            : System.Math.Clamp((height - referenceLow) / range, 0, 1);
        var result = timeLow + (timeHigh - timeLow) * alpha;
        Finite(result); return result;
    }

    public static float Find(float height, float montageLength, double samplingFramesPerSecond, Func<float, double> sampleRootHeight)
    {
        Finite(height); Finite(montageLength); ArgumentNullException.ThrowIfNull(sampleRootHeight);
        if (montageLength < 0 || !double.IsFinite(samplingFramesPerSecond) || samplingFramesPerSecond <= 0)
            throw new ArgumentException("Invalid mantling montage timing.");
        // FFrameRate::AsDecimal returns double; keep the native comparison's
        // double interval even though the binary-search times themselves float.
        var frameInterval = 1d / samplingFramesPerSecond;
        if (!double.IsFinite(frameInterval) || frameInterval <= 0) throw new ArgumentException("Unrepresentable sampling interval.");
        var start = 0f; var end = montageLength;
        var startZ = Sample(start); var endZ = Sample(end);
        var targetZ = System.Math.Max(0, endZ - height);
        if (!double.IsFinite(targetZ)) throw new ArgumentException("Mantling target height overflow.");
        if (System.Math.Abs(startZ - targetZ) <= 1) return start;
        while (true)
        {
            var time = (start + end) * .5f;
            if (!float.IsFinite(time)) throw new ArgumentException("Mantling search time overflow.");
            var z = Sample(time);
            // Keep the native midpoint, including when a plateau never reaches
            // the height. Do not snap to an endpoint or invent monotonic repair.
            if (System.Math.Abs(z - targetZ) <= 1 || end - start <= frameInterval) return time;
            if (time == start || time == end) throw new ArgumentException("Mantling search exceeds float time resolution.");
            if (z < targetZ) start = time; else end = time;
        }
        double Sample(float time)
        {
            var value = sampleRootHeight(time);
            return double.IsFinite(value) ? value : throw new ArgumentException("Nonfinite mantling root height.");
        }
    }

    private static void Finite(float value)
    { if (!float.IsFinite(value)) throw new ArgumentException("Nonfinite mantling start-time input."); }
}
