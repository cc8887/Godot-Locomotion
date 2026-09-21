using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

public interface IAlsGjkShape
{
    float Margin { get; }
    void Validate();
    AlsDoubleVector Support(AlsDoubleVector direction, out int vertex, out double supportDelta);
}

public readonly record struct AlsGjkConvexShape(AlsConvexTopology Topology, AlsDoubleVector Scale) : IAlsGjkShape
{
    public float Margin => 0;
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Topology);
        if (Topology.Margin != 0 || !Scale.IsFinite) throw new ArgumentException("GJK convex adapter requires a finite zero-margin hull.");
    }
    public AlsDoubleVector Support(AlsDoubleVector direction, out int vertex, out double supportDelta)
    { supportDelta = 0; return AlsConvexSupport.ZeroMargin(Topology, direction, Scale, out vertex); }
}

public readonly record struct AlsGjkBoxShape(AlsDoubleVector Half, float Margin = 0) : IAlsGjkShape
{
    public void Validate()
    {
        if (!Half.IsFinite || Half.X <= 0 || Half.Y <= 0 || Half.Z <= 0 || !float.IsFinite(Margin) || Margin < 0 ||
            Margin >= System.Math.Min(Half.X, System.Math.Min(Half.Y, Half.Z))) throw new ArgumentException("Invalid GJK box core.");
    }
    public AlsDoubleVector Support(AlsDoubleVector direction, out int vertex, out double supportDelta)
    {
        vertex = (direction.X < 0 ? 0 : 1) + (direction.Y < 0 ? 0 : 2) + (direction.Z < 0 ? 0 : 4);
        supportDelta = (1.7320508075688772935274463415059 - 1) * Margin;
        return new(direction.X < 0 ? -Half.X + Margin : Half.X - Margin,
            direction.Y < 0 ? -Half.Y + Margin : Half.Y - Margin,
            direction.Z < 0 ? -Half.Z + Margin : Half.Z - Margin);
    }
}

// The pair owner must Reset when either shape identity/geometry changes.
// Points remain in their own shape's local cm coordinates across calls.
public sealed class AlsGjkCache
{
    private readonly AlsDoubleVector[] _a = new AlsDoubleVector[4], _b = new AlsDoubleVector[4];
    private readonly double[] _weights = new double[4];
    public int Count { get; private set; }
    public ReadOnlySpan<AlsDoubleVector> WitnessA => _a.AsSpan(0, Count);
    public ReadOnlySpan<AlsDoubleVector> WitnessB => _b.AsSpan(0, Count);
    public ReadOnlySpan<double> Weights => _weights.AsSpan(0, Count);
    public void Reset() => Count = 0;
    // A future pair owner stages this cache across GJK + EPA + manifold creation,
    // then publishes it only if that whole geometry transaction succeeds.
    public void CopyFrom(AlsGjkCache source)
    { ArgumentNullException.ThrowIfNull(source); Commit(source.WitnessA, source.WitnessB, source.Weights, source.Count); }
    internal void Commit(ReadOnlySpan<AlsDoubleVector> a, ReadOnlySpan<AlsDoubleVector> b, ReadOnlySpan<double> weights, int count)
    { a[..count].CopyTo(_a); b[..count].CopyTo(_b); weights[..count].CopyTo(_weights); Count = count; }
}

// Provisional GJK state, not a contact when NeedsEpa is true. CoreDistance and
// NormalA must be preserved for EPA's BadInitialSimplex/Degenerate fallbacks.
public readonly record struct AlsGjkCandidate(bool NeedsEpa, bool HitIterationLimit, int Iterations, int RestoredCount,
    double CoreDistance, AlsDoubleVector NormalA, AlsDoubleVector NormalB,
    AlsDoubleVector PointA, AlsDoubleVector PointB, int VertexA, int VertexB, double MaxSupportDelta);

public static class AlsGjkSearch
{
    public static AlsGjkCandidate Run<TA, TB>(in TA shapeA, in TB shapeB, in AlsPrecisePose bToA,
        AlsGjkCache cache, double epsilon, bool warmStart = true) where TA : struct, IAlsGjkShape where TB : struct, IAlsGjkShape
    {
        ArgumentNullException.ThrowIfNull(cache); shapeA.Validate(); shapeB.Validate(); bToA.Validate(1e-5);
        if (bToA.Scale != AlsDoubleVector.One || !double.IsFinite(epsilon) || epsilon <= 0)
            throw new ArgumentException("GJK requires a rigid relative pose and positive epsilon.");
        Span<AlsDoubleVector> simplex = stackalloc AlsDoubleVector[4], a = stackalloc AlsDoubleVector[4], b = stackalloc AlsDoubleVector[4];
        Span<double> weights = stackalloc double[4];
        var count = warmStart ? cache.Count : 0; var v = new AlsDoubleVector(-1,0,0); double distance = float.MaxValue;
        var inverse = bToA.Rotation.Conjugate();
        if (count > 0)
        {
            cache.WitnessA.CopyTo(a); cache.WitnessB.CopyTo(b); cache.Weights.CopyTo(weights);
            for (var i = 0; i < count; i++) simplex[i] = a[i] - (b[i].Rotate(bToA.Rotation) + bToA.Position);
            var restored = AlsGjkSimplex.Closest(simplex, ref count, weights, a, b);
            var restoredDistance = System.Math.Sqrt(restored.LengthSquared);
            if (restoredDistance > epsilon) { v = Divide(restored, restoredDistance); distance = restoredDistance; }
            else count = 0;
        }
        var restoredCount = count; var normal = v * -1; var needsEpa = false; var stopped = false; var limit = false;
        var iterations = 0; var vertexA = -1; var vertexB = -1; var maximumDelta = 0d;
        while (!needsEpa && !stopped)
        {
            if (++iterations >= 32) { limit = true; break; }
            var supportA = shapeA.Support(v * -1, out vertexA, out var deltaA);
            var supportB = shapeB.Support(v.Rotate(inverse), out vertexB, out var deltaB);
            if (!supportA.IsFinite || !supportB.IsFinite || !double.IsFinite(deltaA) || !double.IsFinite(deltaB) || vertexA < 0 || vertexB < 0)
                throw new InvalidOperationException("Invalid GJK support; cache was not committed.");
            maximumDelta = System.Math.Max(deltaA, deltaB); // Native overwrites, not a running maximum.
            a[count] = supportA; b[count] = supportB;
            simplex[count++] = supportA - (supportB.Rotate(bToA.Rotation) + bToA.Position);
            v = AlsGjkSimplex.Closest(simplex, ref count, weights, a, b);
            var nextDistance = System.Math.Sqrt(v.LengthSquared);
            if (!double.IsFinite(nextDistance)) throw new InvalidOperationException("Nonfinite GJK simplex; cache was not committed.");
            needsEpa = nextDistance < epsilon;
            stopped = nextDistance >= distance;
            if (!needsEpa) { v = Divide(v, nextDistance); normal = v * -1; }
            distance = nextDistance;
        }
        var pointA = AlsDoubleVector.Zero; var pointB = AlsDoubleVector.Zero;
        for (var i = 0; i < count; i++) { pointA += a[i] * weights[i]; pointB += b[i] * weights[i]; }
        var normalB = normal.Rotate(inverse);
        if (!pointA.IsFinite || !pointB.IsFinite || !normal.IsFinite || !normalB.IsFinite)
            throw new InvalidOperationException("Nonfinite GJK candidate; cache was not committed.");
        var candidate = new AlsGjkCandidate(needsEpa, limit, iterations, restoredCount, distance, normal, normalB,
            pointA + normal * shapeA.Margin, pointB - normalB * shapeB.Margin, vertexA, vertexB, maximumDelta);
        cache.Commit(a, b, weights, count); return candidate;
    }
    private static AlsDoubleVector Divide(AlsDoubleVector v, double d) => v * (1 / d);
}
