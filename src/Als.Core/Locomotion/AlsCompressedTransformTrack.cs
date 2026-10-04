using M = System.Math;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsCompressedTransformChannel(float[][] Keys, int[] Frames);
public readonly record struct AlsCompressedTransformTiming(int Numerator, int Denominator, int NumberOfFrames,
    int CompressedKeys, float Length, bool Step, bool PerTrack);

// Immutable decoded keys. This owns codec time selection/interpolation, not
// compressed-byte parsing, source addresses, retargeting or playback clocks.
public sealed class AlsCompressedTransformTrack
{
    private readonly AlsCompressedTransformChannel[] _channels;
    private readonly AlsCompressedTransformTiming _timing;
    public float Length => _timing.Length;
    public AlsCompressedTransformTrack(IEnumerable<AlsCompressedTransformChannel> channels, AlsCompressedTransformTiming timing)
    {
        ArgumentNullException.ThrowIfNull(channels);
        if (timing.Numerator <= 0 || timing.Denominator <= 0 || timing.NumberOfFrames <= 0 || timing.CompressedKeys < 2 ||
            !float.IsFinite(timing.Length) || timing.Length <= 0) throw new ArgumentException("Invalid compressed transform timing.");
        _timing = timing;
        _channels = channels.Select(c => new AlsCompressedTransformChannel(c.Keys.Select(k => (float[])k.Clone()).ToArray(), (int[])c.Frames.Clone())).ToArray();
        if (_channels.Length != 3 || _channels.Select((c, i) => c.Keys.Length == 0 || c.Keys.Any(k => k.Length != (i == 1 ? 4 : 3) ||
            k.Any(v => !float.IsFinite(v))) || c.Frames.Length != 0 && (c.Frames.Length != c.Keys.Length ||
            c.Frames[0] != 0 || c.Frames[^1] != timing.CompressedKeys - 1 || c.Frames.Any(f => f < 0 || f >= timing.CompressedKeys) ||
            c.Frames.Zip(c.Frames.Skip(1), (a, b) => a >= b).Any(v => v))).Any(v => v))
            throw new ArgumentException("Invalid compressed transform channel.");
    }
    private (int First, int Last, float Alpha) Select(AlsCompressedTransformChannel channel, float relative)
    {
        var last = channel.Keys.Length - 1;
        if (last == 0 || relative <= 0) return (0, 0, 0);
        if (relative >= 1) return (last, last, 0);
        if (channel.Frames.Length == 0)
        {
            var position = relative * last; var floor = MathF.Floor(position); var first = M.Min((int)floor, last);
            return (first, M.Min(first + 1, last), _timing.Step ? 0 : position - floor);
        }
        var totalFrames = _timing.CompressedKeys - 1; var framePosition = relative * totalFrames;
        var frameFloor = M.Clamp((int)framePosition, 0, totalFrames - 1);
        var low = 0; while (low < last && channel.Frames[low + 1] <= frameFloor) low++;
        var high = M.Min(low + 1, last);
        return (low, high, _timing.Step ? 0 : (framePosition - channel.Frames[low]) / M.Max(channel.Frames[high] - channel.Frames[low], 1));
    }
    public AlsPrecisePose Sample(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        var position = seconds * _timing.Numerator / _timing.Denominator;
        var (whole, alpha) = AlsAnimationFrameTime.FromFramePosition(position, AlsRawFrameTimeRounding.RoundSubframe);
        var relative = (float)M.Min((whole + (double)alpha) / _timing.NumberOfFrames, 1);
        AlsDoubleVector Vector(AlsCompressedTransformChannel channel)
        {
            var (first, last, a) = Select(channel, relative); var x = channel.Keys[first]; var y = channel.Keys[last];
            if (first == last) return new(x[0], x[1], x[2]);
            return new(x[0] + a * (y[0] - x[0]), x[1] + a * (y[1] - x[1]), x[2] + a * (y[2] - x[2]));
        }
        var rotations = _channels[1]; var (r0, r1, ra) = Select(rotations, relative);
        var q0 = rotations.Keys[r0]; var q1 = rotations.Keys[r1]; var rotation = new AlsQuaternion(q0[0], q0[1], q0[2], q0[3]);
        if (r0 != r1)
        {
            var dot = q0[0] * q1[0] + q0[1] * q1[1] + q0[2] * q1[2] + q0[3] * q1[3]; var weight = (dot >= 0 ? 1f : -1f) * (1f - ra);
            rotation = new(q1[0] * ra + q0[0] * weight, q1[1] * ra + q0[1] * weight, q1[2] * ra + q0[2] * weight, q1[3] * ra + q0[3] * weight);
            if (!_timing.PerTrack)
            {
                var x = (float)rotation.X; var y = (float)rotation.Y; var z = (float)rotation.Z; var w = (float)rotation.W;
                var size = (x * x + y * y) + (z * z + w * w); var inverse = 1f / MathF.Sqrt(size);
                rotation = new(x * inverse, y * inverse, z * inverse, w * inverse);
            }
        }
        if (_timing.PerTrack) rotation = rotation.Normalized();
        return new(Vector(_channels[0]), rotation, Vector(_channels[2]));
    }
    public AlsRawRootMotionIntervalSampler CreateRootMotionSampler(AlsPrecisePose reference, bool normalizedScale)
    {
        var inverse = AlsPrecisePose.Inverse(reference);
        AlsPrecisePose Extract(double start, double end)
        {
            var first = Sample(start); var last = Sample(end);
            if (normalizedScale) { first = first with { Scale = AlsDoubleVector.One }; last = last with { Scale = AlsDoubleVector.One }; }
            return AlsPrecisePose.Relative(AlsPrecisePose.Compose(inverse, last), AlsPrecisePose.Compose(inverse, first));
        }
        return new(Length, Sample, Extract);
    }
}
