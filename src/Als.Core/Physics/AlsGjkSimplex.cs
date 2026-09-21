using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// Chaos indexed double SimplexFindClosestToOrigin. Active vertices, witnesses,
// and barycentrics compact together; inactive slots are intentionally unspecified.
public static class AlsGjkSimplex
{
    private const double MinimumNormal = 2.2250738585072014e-308;
    public static AlsDoubleVector Closest(Span<AlsDoubleVector> points, ref int count,
        Span<double> weights, Span<AlsDoubleVector> witnessA, Span<AlsDoubleVector> witnessB)
    {
        if (count is < 1 or > 4 || points.Length < count || weights.Length < count ||
            witnessA.Length < count || witnessB.Length < count) throw new ArgumentException("Invalid GJK simplex buffers.");
        for (var i = 0; i < count; i++) if (!points[i].IsFinite || !witnessA[i].IsFinite || !witnessB[i].IsFinite)
            throw new ArgumentException("GJK simplex requires finite points and witnesses.");
        Span<int> ids = stackalloc int[4] { 0,1,2,3 };
        Span<double> barycentric = stackalloc double[4]; barycentric.Clear();
        var n = count; var closest = Reduce(points, ids, ref n, barycentric);
        Span<AlsDoubleVector> p = stackalloc AlsDoubleVector[4];
        Span<AlsDoubleVector> a = stackalloc AlsDoubleVector[4];
        Span<AlsDoubleVector> b = stackalloc AlsDoubleVector[4];
        for (var i = 0; i < n; i++) { p[i] = points[ids[i]]; a[i] = witnessA[ids[i]]; b[i] = witnessB[ids[i]]; }
        for (var i = 0; i < n; i++) { points[i] = p[i]; witnessA[i] = a[i]; witnessB[i] = b[i]; weights[i] = barycentric[ids[i]]; }
        count = n; return closest;
    }
    private static AlsDoubleVector Reduce(ReadOnlySpan<AlsDoubleVector> p, Span<int> ids, ref int n, Span<double> w)
    {
        if (n == 1) { w[ids[0]] = 1; return p[ids[0]]; }
        if (n == 2) return Line(p, ids, ref n, w);
        if (n == 3) return Triangle(p, ids, ref n, w);
        return Tetrahedron(p, ids, ref n, w);
    }
    private static AlsDoubleVector Line(ReadOnlySpan<AlsDoubleVector> p, Span<int> ids, ref int n, Span<double> w)
    {
        var x0 = p[ids[0]]; var x1 = p[ids[1]]; var edge = x1 - x0;
        var dot = AlsDoubleVector.Dot(x0 * -1, edge);
        if (dot <= 0) { n = 1; w[ids[0]] = 1; return x0; }
        var squared = edge.LengthSquared;
        if (squared <= dot || squared <= MinimumNormal) { n = 1; ids[0] = ids[1]; w[ids[1]] = 1; return x1; }
        var ratio = System.Math.Clamp(dot / squared, 0, 1);
        w[ids[0]] = 1 - ratio; w[ids[1]] = ratio;
        return edge * ratio + x0;
    }
    private static AlsDoubleVector Triangle(ReadOnlySpan<AlsDoubleVector> p, Span<int> ids, ref int n, Span<double> w)
    {
        var i0 = ids[0]; var i1 = ids[1]; var i2 = ids[2];
        var x0 = p[i0]; var x1 = p[i1]; var x2 = p[i2];
        var normal = AlsDoubleVector.Cross(x1 - x0, x2 - x0); var squared = normal.LengthSquared;
        if ((x0 * MinimumNormal).LengthSquared >= squared) { n = 2; return Line(p, ids, ref n, w); }
        // Preserve component division before the dot (not reciprocal multiplication).
        var divided = new AlsDoubleVector(normal.X / squared, normal.Y / squared, normal.Z / squared);
        var projection = normal * AlsDoubleVector.Dot(x0, divided);
        var determinant = 0d; var bestU = -1; var bestV = 0; var maximum = 0d; var u = 1; var v = 2;
        for (var axis = 0; axis < 3; axis++)
        {
            var d = x1[u] * x2[v] - x2[u] * x1[v] + x2[u] * x0[v] - x0[u] * x2[v] + x0[u] * x1[v] - x1[u] * x0[v];
            if (bestU == -1 || System.Math.Abs(d) > maximum) { maximum = System.Math.Abs(d); determinant = d; bestU = u; bestV = v; }
            u = v; v = axis;
        }
        var p0 = x0 - projection; var p1 = x1 - projection; var p2 = x2 - projection;
        Span<double> cofactors = stackalloc double[3]
        {
            p1[bestU] * p2[bestV] - p2[bestU] * p1[bestV],
            -p0[bestU] * p2[bestV] + p2[bestU] * p0[bestV],
            p0[bestU] * p1[bestV] - p1[bestU] * p0[bestV]
        };
        Span<int> sub = stackalloc int[4]; Span<int> bestIds = stackalloc int[4];
        Span<double> subWeights = stackalloc double[4]; Span<double> bestWeights = stackalloc double[4];
        var best = -1; var bestCount = 0; var distance = 0d; var closest = AlsDoubleVector.Zero;
        for (var i = 0; i < 3; i++) if (!SignMatch(determinant, cofactors[i]))
        {
            sub[0] = i == 0 ? i1 : i0; sub[1] = i == 2 ? i1 : i2; var subCount = 2;
            subWeights.Clear(); var candidate = Line(p, sub, ref subCount, subWeights);
            if (best == -1 || candidate.LengthSquared < distance)
            {
                best = i; distance = candidate.LengthSquared; closest = candidate; bestCount = subCount;
                sub.CopyTo(bestIds); subWeights.CopyTo(bestWeights);
            }
        }
        if (best == -1)
        {
            var inverse = 1 / determinant; w[i0] = cofactors[0] * inverse; w[i1] = cofactors[1] * inverse; w[i2] = cofactors[2] * inverse;
            return projection;
        }
        bestIds[..bestCount].CopyTo(ids); n = bestCount; bestWeights.CopyTo(w); return closest;
    }
    private static AlsDoubleVector Tetrahedron(ReadOnlySpan<AlsDoubleVector> p, Span<int> ids, ref int n, Span<double> w)
    {
        var x0 = p[ids[0]]; var x1 = p[ids[1]]; var x2 = p[ids[2]]; var x3 = p[ids[3]];
        Span<double> cofactors = stackalloc double[4]
        {
            -AlsDoubleVector.Dot(x1, AlsDoubleVector.Cross(x2,x3)),
            AlsDoubleVector.Dot(x0, AlsDoubleVector.Cross(x2,x3)),
            -AlsDoubleVector.Dot(x0, AlsDoubleVector.Cross(x1,x3)),
            AlsDoubleVector.Dot(x0, AlsDoubleVector.Cross(x1,x2))
        };
        var determinant = (cofactors[0] + cofactors[1]) + (cofactors[2] + cofactors[3]);
        ReadOnlySpan<int> faces = [1,2,3, 0,2,3, 0,1,3, 0,1,2];
        Span<int> sub = stackalloc int[4]; Span<int> bestIds = stackalloc int[4];
        Span<double> subWeights = stackalloc double[4]; Span<double> bestWeights = stackalloc double[4];
        var best = -1; var bestCount = 0; var distance = 0d; var closest = AlsDoubleVector.Zero;
        for (var i = 0; i < 4; i++) if (!SignMatch(determinant, cofactors[i]))
        {
            faces.Slice(i * 3, 3).CopyTo(sub); var subCount = 3;
            subWeights.Clear(); var candidate = Triangle(p, sub, ref subCount, subWeights);
            if (best == -1 || candidate.LengthSquared < distance)
            {
                best = i; distance = candidate.LengthSquared; closest = candidate; bestCount = subCount;
                sub.CopyTo(bestIds); subWeights.CopyTo(bestWeights);
            }
        }
        if (best == -1) { for (var i = 0; i < 4; i++) w[ids[i]] = cofactors[i] / determinant; return default; }
        bestIds[..bestCount].CopyTo(ids); n = bestCount; bestWeights.CopyTo(w); return closest;
    }
    private static bool SignMatch(double a, double b) => a > 0 && b > 0 || a < 0 && b < 0;
}
