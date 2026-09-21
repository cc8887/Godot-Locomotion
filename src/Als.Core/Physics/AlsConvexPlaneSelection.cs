using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

public static class AlsConvexPlaneSelection
{
    // Unwrapped FConvex path: float stored planes promoted to double. Scaled
    // FConvex has a separate float implementation and must not use this API.
    public static int Unscaled(AlsConvexTopology hull, AlsDoubleVector point, AlsDoubleVector direction,
        double maximumDistance, int supportVertex, float minimumSearchDistance)
    {
        ArgumentNullException.ThrowIfNull(hull);
        if (!point.IsFinite || !direction.IsFinite || !double.IsFinite(maximumDistance) || maximumDistance < 0 ||
            !float.IsFinite(minimumSearchDistance) || minimumSearchDistance < 0 || (uint)supportVertex >= hull.VertexCount)
            throw new ArgumentException("Invalid native plane selection input.");
        var cache = hull.VertexPlanesAt(supportVertex);
        var distance = System.Math.Max(maximumDistance,minimumSearchDistance); var best = -1; var bestDot = 1d;
        var count = cache.Count > 3 ? hull.PlaneCount : cache.Count;
        for (var i = 0; i < count; i++)
        {
            var index = cache.Count > 3 ? i : cache.PlaneAt(i); var plane = hull.PlaneAt(index);
            var n = new AlsDoubleVector(plane.Normal); var x = new AlsDoubleVector(plane.Point);
            if (System.Math.Abs(AlsDoubleVector.Dot(point-x,n)) > distance) continue;
            var dot = AlsDoubleVector.Dot(direction,n);
            if (dot <= -(double)1e-8f && dot < bestDot) { best = index; bestDot = dot; }
        }
        if (best >= 0) return best;
        bestDot = double.MaxValue;
        for (var i = 0; i < hull.PlaneCount; i++)
        {
            var dot = AlsDoubleVector.Dot(new(hull.PlaneAt(i).Normal),direction);
            if (dot < bestDot) { best = i; bestDot = dot; }
        }
        return best;
    }
}
