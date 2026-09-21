using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

public static class AlsSphereConvexManifold
{
    // Native point-to-full-hull GJKDistance. This is not GJKPenetration/EPA.
    public static int Build(Vector3 center, float radius, in AlsPrecisePose sphereToConvex,
        in AlsConvexPolygonShape convex, AlsDoubleVector centerOfMass, AlsDoubleVector boundsExtents,
        Span<AlsDetectedContact> destination, double cull)
    {
        convex.Validate(); sphereToConvex.Validate(1e-5);
        if (!new AlsDoubleVector(center).IsFinite || !float.IsFinite(radius) || radius <= 0 ||
            !centerOfMass.IsFinite || !boundsExtents.IsFinite || boundsExtents.X <= 0 || boundsExtents.Y <= 0 || boundsExtents.Z <= 0 ||
            !double.IsFinite(cull) || cull < 0 || convex.Margin != 0 || sphereToConvex.Scale != AlsDoubleVector.One || destination.Length < 4)
            throw new ArgumentException("Sphere-convex requires finite rigid geometry, zero support margin and capacity four.");
        var position = new AlsDoubleVector(center).Rotate(sphereToConvex.Rotation) + sphereToConvex.Position;
        var v = centerOfMass - position; var length = System.Math.Sqrt(v.LengthSquared); var mu = 0d; var iteration = 0;
        var count = 0; var phi = 0d; var normal = AlsDoubleVector.Zero; var separated = false;
        Span<AlsDoubleVector> simplex = stackalloc AlsDoubleVector[4];
        Span<AlsDoubleVector> a = stackalloc AlsDoubleVector[4]; Span<AlsDoubleVector> b = stackalloc AlsDoubleVector[4];
        Span<double> weights = stackalloc double[4];
        while (length > .001)
        {
            var support = convex.Support(v * -1, out _, out _); var w = support - position;
            mu = System.Math.Max(mu, AlsDoubleVector.Dot(v, w) / length);
            if (length - mu < .001 || ++iteration > 16)
            { phi = length; normal = v * (-1 / length); separated = true; break; }
            simplex[count] = w; a[count] = support; b[count] = position; count++;
            v = AlsGjkSimplex.Closest(simplex, ref count, weights, a, b); length = System.Math.Sqrt(v.LengthSquared);
        }
        if (!separated)
        {
            phi = double.MinValue;
            for (var plane = 0; plane < convex.Topology.PlaneCount; plane++)
            {
                convex.Plane(plane, out var n, out var p);
                var distance = AlsDoubleVector.Dot(position - p, n);
                if (distance > phi) { phi = distance; normal = n; }
            }
        }
        var point1 = position - normal * phi;
        var point0 = new AlsDoubleVector(center + (normal * -(double)radius).Rotate(sphereToConvex.Rotation.Conjugate()).ToSingle());
        var storedPhi = (float)(phi - radius);
        if (phi - radius > cull) return 0;
        Span<AlsDetectedContact> points = stackalloc AlsDetectedContact[4];
        points[0] = Contact(point0, point1, normal, storedPhi); count = 1;
        if (radius > System.Math.Max(boundsExtents.X, System.Math.Max(boundsExtents.Y, boundsExtents.Z)))
        {
            // One-shot manifold points remain double until the final constraint
            // stores them; use the unrounded normal for speculative vertices.
            var plane = -1; var minimum = double.MaxValue;
            for (var i = 0; i < convex.Topology.PlaneCount; i++)
            {
                var n = new AlsDoubleVector(convex.Topology.PlaneAt(i).Normal);
                if (convex.IsScaled)
                {
                    n = new(n.X / convex.Scale.X, n.Y / convex.Scale.Y, n.Z / convex.Scale.Z);
                    var squared = n.LengthSquared;
                    n = squared == 1 ? n : squared < (double)1e-8f ? AlsDoubleVector.Zero : n * (1 / System.Math.Sqrt(squared));
                }
                var dot = AlsDoubleVector.Dot(n, normal * -1);
                if (dot < minimum) { minimum = dot; plane = i; }
            }
            var vertices = convex.FaceCount(plane); var stride = System.Math.Max(1, vertices / 3);
            for (var i = 0; i < vertices && count < 4; i += stride)
            {
                var p = convex.FaceVertex(plane, i); var offset = position - p;
                var along = AlsDoubleVector.Dot(normal, offset);
                var discriminant = (double)radius * radius - (offset.LengthSquared - along * along);
                if (discriminant < 0) continue;
                var distance = along - System.Math.Sqrt(discriminant);
                if (distance < cull)
                    points[count++] = Contact((p + normal * distance - sphereToConvex.Position).Rotate(sphereToConvex.Rotation.Conjugate()), p, normal, (float)distance);
            }
        }
        points[..count].CopyTo(destination); return count;
    }

    private static AlsDetectedContact Contact(AlsDoubleVector a, AlsDoubleVector b, AlsDoubleVector normal, float phi)
    {
        var p0 = a.ToSingle(); var p1 = b.ToSingle(); var n = normal.ToSingle();
        if (!new AlsDoubleVector(p0).IsFinite || !new AlsDoubleVector(p1).IsFinite || !new AlsDoubleVector(n).IsFinite || !float.IsFinite(phi))
            throw new InvalidOperationException("Sphere-convex result exceeds native storage.");
        return new(p0, p1, n) { NativePhi = phi };
    }
}
