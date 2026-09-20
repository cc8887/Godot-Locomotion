using System.Numerics;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsLeanGridVertex(int Sample, float Weight);

/// <summary>Native ALS ground/air Lean grid, five samples, no axis or sample-weight smoothing.</summary>
public sealed class AlsLeanBlendSpace
{
    private readonly AlsLeanGridVertex[] _vertices;
    private readonly int _width, _pointCount;
    private readonly double _step;

    public AlsLeanBlendSpace(ReadOnlySpan<AlsLeanGridVertex> vertices, int divisions = 4)
    {
        if (divisions is not (2 or 4)) throw new ArgumentException("Unsupported native Lean grid divisions.");
        _width = divisions + 1; _pointCount = _width * _width; _step = 2d / divisions;
        if (vertices.Length != _pointCount * 3) throw new ArgumentException("Lean requires three vertices per grid point.");
        for (var i = 0; i < vertices.Length; i++)
            if (vertices[i].Sample < -1 || vertices[i].Sample >= 5 || !float.IsFinite(vertices[i].Weight) ||
                vertices[i].Weight is < 0 or > 1 || vertices[i].Sample < 0 && vertices[i].Weight != 0)
                throw new ArgumentException("Invalid Lean grid vertex.");
        for (var i = 0; i < _pointCount; i++)
            if (MathF.Abs(vertices[i * 3].Weight + vertices[i * 3 + 1].Weight + vertices[i * 3 + 2].Weight - 1) > 1e-5f)
                throw new ArgumentException("Incomplete Lean grid weights.");
        _vertices = vertices.ToArray();
    }

    public int Evaluate(Vector2 input, Span<float> weights, Span<int> order)
    {
        if (!float.IsFinite(input.X) || !float.IsFinite(input.Y) || weights.Length != 5 || order.Length != 5)
            throw new ArgumentException("Invalid Lean input or sample buffers.");
        weights.Clear(); order.Fill(-1);
        // UE uses double FVector grid coordinates, then casts each corner weight to float.
        var x = (System.Math.Clamp((double)input.X, -1, 1) + 1) / _step;
        var y = (System.Math.Clamp((double)input.Y, -1, 1) + 1) / _step;
        var ix = (int)x; var iy = (int)y; var rx = x - ix; var ry = y - iy;
        var count = 0; var seen = 0;
        for (var corner = 0; corner < 4; corner++)
        {
            var right = corner % 2; var top = corner / 2;
            // GetEditorElement checks the flattened index, not the separate X/Y bounds.
            var grid = (iy + top) * _width + ix + right;
            if ((uint)grid >= _pointCount) continue;
            var factor = (float)((right == 0 ? 1 - rx : rx) * (top == 0 ? 1 - ry : ry));
            for (var vertex = 0; vertex < 3; vertex++)
            {
                var item = _vertices[grid * 3 + vertex]; if (item.Sample < 0) continue;
                if ((seen & (1 << item.Sample)) == 0) { order[count++] = item.Sample; seen |= 1 << item.Sample; }
                weights[item.Sample] += item.Weight * factor;
            }
        }
        // UE IntroSort's small-array path is non-stable selection sort, including equal weights.
        for (var end = count - 1; end > 0; end--)
        {
            var smallest = 0;
            for (var i = 1; i <= end; i++) if (weights[order[i]] < weights[order[smallest]]) smallest = i;
            (order[smallest], order[end]) = (order[end], order[smallest]);
        }
        var kept = 0; var total = 0f;
        for (var i = 0; i < count; i++)
        {
            var sample = order[i];
            if (weights[sample] < AlsPoseBlender.WeightThreshold) { weights[sample] = 0; order[i] = -1; }
            else { total += weights[sample]; kept++; }
        }
        if (kept == 0) throw new InvalidOperationException("Lean grid produced no relevant samples.");
        // Match the native export's float reciprocal normalization (division rounds differently).
        var inverseTotal = 1f / total;
        for (var i = 0; i < kept; i++) weights[order[i]] *= inverseTotal;
        return kept;
    }

    public static void Apply(ReadOnlySpan<AlsLocalPose> basis, ReadOnlySpan<AlsLocalPose> additives,
        ReadOnlySpan<float> weights, ReadOnlySpan<int> order, Span<AlsLocalPose> output, float alpha=1)
    {
        ValidateSamples(weights, order);
        if (basis.Length == 0 || additives.Length != basis.Length * 5 || output.Length != basis.Length ||
            additives.Overlaps(output) || basis.Overlaps(output, out var offset) && offset != 0)
            throw new ArgumentException("Invalid Lean pose buffers.");
        for (var bone = 0; bone < basis.Length; bone++)
        {
            var first = order[0]; var mixed = AlsPoseBlender.Scale(additives[first * basis.Length + bone], weights[first]);
            for (var i = 1; i < order.Length; i++)
                mixed = AlsPoseBlender.Accumulate(mixed, additives[order[i] * basis.Length + bone], weights[order[i]]);
            output[bone] = AlsLocalAdditivePose.Apply(basis[bone], AlsPoseBlender.Normalize(mixed),alpha);
        }
    }

    public static float Curve(float basis, ReadOnlySpan<float> additives, ReadOnlySpan<float> weights, ReadOnlySpan<int> order)
    {
        ValidateSamples(weights, order);
        if (!float.IsFinite(basis) || additives.Length != 5) throw new ArgumentException("Invalid Lean curve buffers.");
        var mixed = 0f;
        foreach (var sample in order)
        {
            if (!float.IsFinite(additives[sample])) throw new ArgumentException("Invalid Lean curve value.");
            mixed += additives[sample] * weights[sample];
        }
        return basis + mixed;
    }

    public static void Apply(ReadOnlySpan<AlsPrecisePose> basis,ReadOnlySpan<AlsPrecisePose> additives,
        ReadOnlySpan<float> weights,ReadOnlySpan<int> order,Span<AlsPrecisePose> output,float alpha=1)
    {
        ValidateSamples(weights,order);
        if(basis.Length==0 || additives.Length!=basis.Length*5 || output.Length!=basis.Length ||
            additives.Overlaps(output) || basis.Overlaps(output,out var offset) && offset!=0)
            throw new ArgumentException("Invalid precise Lean pose buffers.");
        for(var b=0;b<basis.Length;b++)
        {
            var mixed=AlsPrecisePoseBlender.Scale(additives[order[0]*basis.Length+b],weights[order[0]]);
            for(var i=1;i<order.Length;i++)
                mixed=AlsPrecisePoseBlender.Accumulate(mixed,additives[order[i]*basis.Length+b],weights[order[i]]);
            output[b]=AlsPrecisePoseBlender.LocalApply(basis[b],mixed.Normalized(),alpha);
        }
    }

    private static void ValidateSamples(ReadOnlySpan<float> weights, ReadOnlySpan<int> order)
    {
        if (weights.Length != 5 || order.Length is < 1 or > 5) throw new ArgumentException("Invalid Lean sample layout.");
        var seen = 0; var sum = 0f;
        foreach (var sample in order)
        {
            if ((uint)sample >= 5 || (seen & (1 << sample)) != 0 || !float.IsFinite(weights[sample]) || weights[sample] <= 0)
                throw new ArgumentException("Invalid Lean sample order/weights.");
            seen |= 1 << sample; sum += weights[sample];
        }
        if (MathF.Abs(sum - 1) > 1e-5f) throw new ArgumentException("Lean sample weights are not normalized.");
    }
}
