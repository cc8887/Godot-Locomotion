namespace GodotAls.Core.Locomotion;

public readonly record struct AlsAimGridVertex(int Sample, float Weight);

// ALS_N_Look: the native one-dimensional Pitch grid, retaining its vertex order.
// This grid has no axis or sample-weight smoothing; the compiler verifies that.
public sealed class AlsAimBlendSpace
{
    private readonly AlsAimGridVertex[] _vertices;
    public AlsAimBlendSpace(ReadOnlySpan<AlsAimGridVertex> vertices)
    {
        if (vertices.Length != 15) throw new ArgumentException("Aim requires five three-vertex grid points.");
        for (var point = 0; point < 5; point++)
        {
            var total = 0f;
            for (var vertex = 0; vertex < 3; vertex++)
            {
                var value = vertices[point * 3 + vertex];
                if (value.Sample is < -1 or > 2 || !float.IsFinite(value.Weight) || value.Weight is < 0 or > 1 ||
                    value.Sample < 0 && value.Weight != 0) throw new ArgumentException("Invalid Aim grid vertex.");
                total += value.Weight;
            }
            if (MathF.Abs(total - 1) > 1e-6f) throw new ArgumentException("Incomplete Aim grid point.");
        }
        _vertices = vertices.ToArray();
    }

    public int Evaluate(float pitch, Span<float> weights, Span<int> order)
    {
        if (!float.IsFinite(pitch) || weights.Length != 3 || order.Length != 3)
            throw new ArgumentException("Invalid Aim sample buffers or pitch.");
        weights.Clear(); order.Fill(-1);
        var position = (System.Math.Clamp((double)pitch, -90, 90) + 90) / 45;
        var index = (int)position;
        // UE 1D converts the double coordinate remainder to float before 1-r.
        var remainder = (float)(position - index);
        var count = 0; var seen = 0;
        for (var side = 0; side < 2; side++)
        {
            if (index + side >= 5) continue;
            var factor = side == 0 ? 1 - remainder : remainder;
            for (var vertex = 0; vertex < 3; vertex++)
            {
                var item = _vertices[(index + side) * 3 + vertex]; if (item.Sample < 0) continue;
                if ((seen & (1 << item.Sample)) == 0) { seen |= 1 << item.Sample; order[count++] = item.Sample; }
                weights[item.Sample] += item.Weight * factor;
            }
        }
        // Native IntroSort uses this non-stable small-array selection sort.
        for (var end = count - 1; end > 0; end--)
        {
            var smallest = 0;
            for (var item = 1; item <= end; item++) if (weights[order[item]] < weights[order[smallest]]) smallest = item;
            (order[smallest], order[end]) = (order[end], order[smallest]);
        }
        var kept = 0; var sum = 0f;
        for (var i = 0; i < count; i++)
        {
            var sample = order[i];
            if (weights[sample] < AlsPoseBlender.WeightThreshold) { weights[sample] = 0; order[i] = -1; }
            else { sum += weights[sample]; kept++; }
        }
        if (kept == 0) throw new InvalidOperationException("Aim grid produced no relevant sample.");
        var inverse = 1f / sum;
        for (var i = 0; i < kept; i++) weights[order[i]] *= inverse;
        return kept;
    }
}
