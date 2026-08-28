namespace GodotAls.Import.Compilation;

internal static class AlsRotationYawCurveDeriver
{
    private const double DegreesToRadians = Math.PI / 180.0;

    internal static double[] DeriveRadiansPerSecond(
        ReadOnlySpan<double> timeSeconds,
        ReadOnlySpan<double> wrappedYawDegrees)
    {
        if (timeSeconds.Length != wrappedYawDegrees.Length)
        {
            throw new ArgumentException("Root yaw samples must have a matching time sample.", nameof(wrappedYawDegrees));
        }
        if (timeSeconds.Length < 2)
        {
            throw new ArgumentException("At least two root yaw samples are required.", nameof(timeSeconds));
        }

        var unwrappedYawDegrees = new double[wrappedYawDegrees.Length];
        for (var index = 0; index < wrappedYawDegrees.Length; index++)
        {
            if (!double.IsFinite(timeSeconds[index]))
            {
                throw new ArgumentException($"Root yaw time at frame {index} must be finite.", nameof(timeSeconds));
            }
            if (!double.IsFinite(wrappedYawDegrees[index]))
            {
                throw new ArgumentException($"Root yaw at frame {index} must be finite.", nameof(wrappedYawDegrees));
            }
            if (index > 0 && timeSeconds[index] <= timeSeconds[index - 1])
            {
                throw new ArgumentException($"Root yaw time at frame {index} must be strictly increasing.", nameof(timeSeconds));
            }

            unwrappedYawDegrees[index] = index == 0
                ? wrappedYawDegrees[index]
                : unwrappedYawDegrees[index - 1] + ShortestSignedDeltaDegrees(
                    wrappedYawDegrees[index] - wrappedYawDegrees[index - 1]);
        }

        var result = new double[unwrappedYawDegrees.Length];
        for (var index = 0; index < result.Length; index++)
        {
            var first = index == 0 ? 0 : index - 1;
            var last = index == result.Length - 1 ? result.Length - 1 : index + 1;
            result[index] = (unwrappedYawDegrees[last] - unwrappedYawDegrees[first]) /
                (timeSeconds[last] - timeSeconds[first]) * DegreesToRadians;
            if (!double.IsFinite(result[index]))
            {
                throw new ArgumentException($"Root yaw derivative at frame {index} is not finite.", nameof(wrappedYawDegrees));
            }
        }

        return result;
    }

    private static double ShortestSignedDeltaDegrees(double delta)
    {
        var normalized = delta % 360.0;
        if (normalized <= -180.0)
        {
            return normalized + 360.0;
        }
        return normalized > 180.0 ? normalized - 360.0 : normalized;
    }
}
