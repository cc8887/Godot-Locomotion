using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// Native TSegment in convex-local double coordinates. Orthogonal directions
// select endpoint 1; reducing this to a centered capsule changes GJK ties.
public readonly record struct AlsGjkSegmentShape(AlsDoubleVector Start, AlsDoubleVector Axis, double Length) : IAlsGjkShape
{
    public float Margin => 0;
    public AlsDoubleVector End => Start + Axis * Length;
    public void Validate()
    {
        if (!Start.IsFinite || !Axis.IsFinite || System.Math.Abs(Axis.LengthSquared - 1) > 1e-5 ||
            !double.IsFinite(Length) || Length <= 0 || !End.IsFinite) throw new ArgumentException("Invalid native segment.");
    }
    public AlsDoubleVector Support(AlsDoubleVector direction, out int vertex, out double delta)
    { vertex = AlsDoubleVector.Dot(direction, Axis) >= 0 ? 1 : 0; delta = 0; return vertex == 1 ? End : Start; }
    public AlsDoubleVector SupportWithDelta(AlsDoubleVector direction, out int vertex, ref double delta)
    { vertex = AlsDoubleVector.Dot(direction, Axis) >= 0 ? 1 : 0; return vertex == 1 ? End : Start; }
}

// Reusable scratch, not a warm-start cache: native capsule-convex constructs
// a fresh same-space simplex for each query. Do not share across workers.
public sealed class AlsCapsuleManifoldWorkspace
{
    internal AlsGjkCache Cache { get; } = new();
    internal AlsEpaWorkspace Epa { get; } = new();
}

public static class AlsCapsuleConvexManifold
{
    public static int Build<T>(in AlsCapsuleGeometry capsule, in AlsPrecisePose capsuleToConvex,
        in T convex, AlsCapsuleManifoldWorkspace work, Span<AlsDetectedContact> destination, double cull,
        double gjkEpsilon = (double)1e-6f, double epaEpsilon = (double)1e-6f,
        float minimumFaceSearchDistance = 1, float planeNormalEpsilon = .001f, float minContactDistanceFraction = .1f)
        where T : struct, IAlsPolygonShape
    {
        capsule.Validate(); capsuleToConvex.Validate(1e-5); convex.Validate(); ArgumentNullException.ThrowIfNull(work);
        if (capsuleToConvex.Scale != AlsDoubleVector.One || convex.Margin != 0 || destination.Length < 3 ||
            !double.IsFinite(cull) || cull < 0 || !float.IsFinite(minimumFaceSearchDistance) || minimumFaceSearchDistance < 0 ||
            !float.IsFinite(planeNormalEpsilon) || planeNormalEpsilon < 0 || !float.IsFinite(minContactDistanceFraction) || minContactDistanceFraction < 0)
            throw new ArgumentException("Capsule manifold requires rigid geometry, zero polygon support margin and capacity three.");
        var axis = new AlsDoubleVector(capsule.Axis).Rotate(capsuleToConvex.Rotation);
        var start = new AlsDoubleVector(capsule.Endpoint0).Rotate(capsuleToConvex.Rotation) + capsuleToConvex.Position;
        var segment = new AlsGjkSegmentShape(start, axis, capsule.Height);
        var contact = AlsGjkPenetration.Run(convex, segment, AlsPrecisePose.Identity, work.Cache, work.Epa, gjkEpsilon, epaEpsilon, false);
        var normal = contact.NormalA * -1; // Native GJK contact normal points segment -> convex.
        double radius = capsule.Radius;
        var capsulePoint = contact.PointB + normal * radius;
        var phi = -contact.Penetration - radius;
        if (phi > cull) return 0;
        var along = AlsDoubleVector.Dot(axis, normal);
        var plane = convex.SelectPlane(contact.PointA, normal, contact.VertexA, minimumFaceSearchDistance);
        if (plane < 0) return 0;
        convex.Plane(plane, out var planeNormal, out var planePoint);
        var planeContact = System.Math.Abs(AlsDoubleVector.Dot(planeNormal, normal) + 1) <= planeNormalEpsilon;
        Span<AlsDoubleVector> vertices = stackalloc AlsDoubleVector[2];
        Span<AlsDetectedContact> points = stackalloc AlsDetectedContact[3];
        var count = 0;
        if (System.Math.Abs(along) < .01)
        {
            var cylinderNormal = SafeNormal(normal - axis * along) * -1;
            var clipped = Clip(convex, plane, segment, cylinderNormal, vertices);
            var divisor = AlsDoubleVector.Dot(cylinderNormal, planeNormal);
            if (clipped > 0 && System.Math.Abs(divisor) > (double)1e-4f)
            {
                var multiplier = 1 / divisor;
                for (var i = 0; i < clipped; i++)
                {
                    var p = vertices[i] - cylinderNormal * radius;
                    var q = p + cylinderNormal * (AlsDoubleVector.Dot(planePoint - p, planeNormal) * multiplier);
                    points[count++] = Point(p, q, cylinderNormal, AlsDoubleVector.Dot(p - q, cylinderNormal), capsuleToConvex);
                }
                points[..count].CopyTo(destination); return count;
            }
        }
        else if (planeContact)
        {
            var axisDotPlane = AlsDoubleVector.Dot(planeNormal, axis);
            if (System.Math.Abs(axisDotPlane) < .707)
            {
                var cylinderNormal = planeNormal - axis * axisDotPlane;
                cylinderNormal *= 1 / System.Math.Sqrt(cylinderNormal.LengthSquared);
                var cylinder = new AlsGjkSegmentShape(start - cylinderNormal * radius, axis, capsule.Height);
                var clipped = Clip(convex, plane, cylinder, planeNormal, vertices);
                var minimum = (double)minContactDistanceFraction * radius; minimum *= minimum;
                for (var i = 0; i < clipped; i++)
                {
                    var p = vertices[i];
                    if ((p - capsulePoint).LengthSquared <= minimum) continue;
                    var q = p - planeNormal * AlsDoubleVector.Dot(p - planePoint, planeNormal);
                    points[count++] = Point(p, q, planeNormal, AlsDoubleVector.Dot(p - q, planeNormal), capsuleToConvex);
                }
            }
        }
        points[count++] = Point(capsulePoint, contact.PointA, normal * -1, phi, capsuleToConvex);
        points[..count].CopyTo(destination); return count;
    }

    private static AlsDetectedContact Point(AlsDoubleVector p, AlsDoubleVector q, AlsDoubleVector n, double phi, in AlsPrecisePose relative)
    {
        var p0 = (p - relative.Position).Rotate(relative.Rotation.Conjugate()).ToSingle();
        var p1 = q.ToSingle(); var normal = n.ToSingle(); var storedPhi = (float)phi;
        if (!new AlsDoubleVector(p0).IsFinite || !new AlsDoubleVector(p1).IsFinite || !new AlsDoubleVector(normal).IsFinite || !float.IsFinite(storedPhi))
            throw new InvalidOperationException("Capsule contact exceeds native storage.");
        return new(p0, p1, normal) { NativePhi = storedPhi };
    }
    private static AlsDoubleVector SafeNormal(AlsDoubleVector v)
    {
        var squared = v.LengthSquared;
        return squared == 1 ? v : squared < (double)1e-8f ? AlsDoubleVector.Zero : v * (1 / System.Math.Sqrt(squared));
    }
    private static int Clip<T>(in T convex, int plane, in AlsGjkSegmentShape segment, AlsDoubleVector normal, Span<AlsDoubleVector> vertices)
        where T : struct, IAlsPolygonShape
    {
        vertices[0] = segment.Start; vertices[1] = segment.End;
        var count = convex.FaceCount(plane); var previous = convex.FaceVertex(plane, count - 1);
        for (var i = 0; i < count; i++)
        {
            var current = convex.FaceVertex(plane, i);
            var clipNormal = SafeNormal(AlsDoubleVector.Cross(normal, previous - current)) * convex.Winding;
            previous = current;
            var d0 = AlsDoubleVector.Dot(vertices[0] - current, clipNormal);
            var d1 = AlsDoubleVector.Dot(vertices[1] - current, clipNormal);
            if (d0 > .01 && d1 > .01) return 0;
            if (d0 > .01 && d1 < .01) vertices[0] = vertices[1] + (vertices[0] - vertices[1]) * (d1 / (d1 - d0));
            else if (d1 > .01 && d0 < .01) vertices[1] = vertices[0] + (vertices[1] - vertices[0]) * (d0 / (d0 - d1));
        }
        return 2;
    }
}
