using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

public static class AlsConvexSupport
{
    // FConvex::SupportCoreScaled for zero margin. The float direction and strict
    // first-maximum comparison are essential to the native support vertex ID.
    public static AlsDoubleVector ZeroMargin(AlsConvexTopology hull, AlsDoubleVector direction,
        AlsDoubleVector scale, out int vertex)
    {
        ArgumentNullException.ThrowIfNull(hull);
        var local = (direction * scale).ToSingle();
        if (hull.Margin != 0 || !direction.IsFinite || !scale.IsFinite ||
            !float.IsFinite(local.X) || !float.IsFinite(local.Y) || !float.IsFinite(local.Z))
            throw new ArgumentException("Finite zero-margin convex support is required.");
        var maximum = -float.MaxValue; vertex = -1;
        for (var i = 0; i < hull.VertexCount; i++)
        {
            var p = hull.VertexAt(i); var dot = p.X * local.X + p.Y * local.Y + p.Z * local.Z;
            if (dot > maximum) { maximum = dot; vertex = i; }
        }
        return vertex < 0 ? default : new AlsDoubleVector(hull.VertexAt(vertex)) * scale;
    }
}
