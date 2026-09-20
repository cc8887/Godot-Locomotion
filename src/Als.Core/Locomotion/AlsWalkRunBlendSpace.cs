using System.Numerics;
using System.Runtime.CompilerServices;

namespace GodotAls.Core.Locomotion;

internal struct AlsBlendInputSample
{
    public float Time;
    public Vector2 Input;
}

[InlineArray(256)]
internal struct AlsBlendInputHistory { private AlsBlendInputSample _element; }

public struct AlsWalkRunFilterState
{
    internal AlsBlendInputHistory History;
    internal int NextIndex;
    internal int Count;
    internal float Time;
    public Vector2 Output { get; internal set; }
    public readonly bool HasSamples => Count > 0;
}

public static class AlsWalkRunBlendSpace
{
    // UE FFIRFilterTimeBased BSIT_Cubic, using caller-owned bounded history so a
    // candidate can be discarded without changing the committed filter state.
    public static AlsWalkRunFilterState Advance(in AlsWalkRunFilterState previous,
        Vector2 input, float delta, Vector2 windows)
    {
        if (!float.IsFinite(input.X) || !float.IsFinite(input.Y) || !float.IsFinite(delta) || delta < 0 ||
            !float.IsFinite(windows.X) || !float.IsFinite(windows.Y) || windows.X <= 0 || windows.Y <= 0)
            throw new ArgumentOutOfRangeException(nameof(input));
        var next = previous;
        if (delta <= 0.0001f) return next;
        next.Time += delta;
        if (!float.IsFinite(next.Time)) throw new InvalidOperationException("WalkRun filter time overflow.");
        var longest = MathF.Max(windows.X, windows.Y);
        while (next.Count > 0)
        {
            var oldest = (next.NextIndex - next.Count + 256) % 256;
            if (next.Time - next.History[oldest].Time <= longest) break;
            next.Count--;
        }
        if (next.Count == 256)
            throw new InvalidOperationException("WalkRun filter history capacity exceeded; no samples were discarded.");
        next.History[next.NextIndex] = new AlsBlendInputSample { Time = next.Time, Input = input };
        next.NextIndex = (next.NextIndex + 1) % 256;
        next.Count++;
        var total = Vector2.Zero;
        var weighted = Vector2.Zero;
        for (var i = 0; i < next.Count; i++)
        {
            var index = (next.NextIndex - next.Count + i + 256) % 256;
            var sample = next.History[index];
            var age = next.Time - sample.Time;
            var weight = new Vector2(Coefficient(age, windows.X), Coefficient(age, windows.Y));
            total += weight;
            weighted += weight * sample.Input;
        }
        next.Output = weighted / total;
        return next;
    }

    // Native one-cell grid: WalkPose, Walk, RunPose, Run. This is bilinear,
    // not the barycentric interpolation used by a triangulated BlendSpace.
    public static Vector4 SampleWeights(Vector2 filteredInput)
    {
        if (!float.IsFinite(filteredInput.X) || !float.IsFinite(filteredInput.Y))
            throw new ArgumentOutOfRangeException(nameof(filteredInput));
        var x = System.Math.Clamp(filteredInput.X, 0, 1);
        var y = System.Math.Clamp(filteredInput.Y, 0, 1);
        return new Vector4((1 - x) * (1 - y), x * (1 - y), (1 - x) * y, x * y);
    }

    public static int SampleEvaluation(Vector2 input, Span<int> order, out Vector4 weights)
    {
        if (order.Length < 4) throw new ArgumentException("Four sample slots are required.", nameof(order));
        weights = SampleWeights(input);
        var count = 0;
        for (var corner = 0; corner < 4; corner++)
        {
            if (input.X >= 1 && corner % 2 == 0 || input.Y >= 1 && corner < 2) continue;
            order[count++] = corner;
        }
        // UE IntroSort uses this non-stable selection sort for up to eight samples.
        for (var end = count - 1; end > 0; end--)
        {
            var smallest = 0;
            for (var i = 1; i <= end; i++)
                if (weights[order[i]] < weights[order[smallest]]) smallest = i;
            (order[smallest], order[end]) = (order[end], order[smallest]);
        }
        var total = 0f;
        var kept = 0;
        for (var i = 0; i < count; i++)
        {
            var corner = order[i];
            if (weights[corner] < AlsPoseBlender.WeightThreshold) weights[corner] = 0;
            else { total += weights[corner]; kept++; }
        }
        weights /= total;
        return kept;
    }

    private static float Coefficient(float age, float window)
    {
        if (age > window) return 0;
        var fraction = age / window;
        return 1 - fraction * fraction * fraction;
    }
}
