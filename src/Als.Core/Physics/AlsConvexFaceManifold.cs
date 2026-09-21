using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// Native one-shot vertex/plane manifold AFTER GJK and feature selection.
// Face loops keep their original winding; inputs are native local centimeters.
public static class AlsConvexFaceManifold
{
    public const int MaximumClippedPoints = 32;

    public static int Build(ReadOnlySpan<AlsDoubleVector> referenceFace, ReadOnlySpan<AlsDoubleVector> incidentFace,
        in AlsPrecisePose incidentToReference, AlsDoubleVector referenceNormal, AlsDoubleVector referencePoint,
        AlsDoubleVector normal1, bool referenceIsShape0, Span<AlsDetectedContact> destination,
        out int clippedCount, double referenceWinding = 1)
    {
        incidentToReference.Validate(1e-5);
        if (incidentToReference.Scale != AlsDoubleVector.One || referenceFace.Length < 3 || incidentFace.Length < 3 ||
            destination.Length < 4 || !referencePoint.IsFinite || !Unit(referenceNormal) || !Unit(normal1) ||
            referenceWinding is not (1 or -1)) throw new ArgumentException("Invalid selected convex faces or output capacity.");
        foreach (var p in referenceFace) if (!p.IsFinite) throw new ArgumentException("Nonfinite reference vertex.");
        foreach (var p in incidentFace) if (!p.IsFinite) throw new ArgumentException("Nonfinite incident vertex.");
        Span<AlsDoubleVector> a = stackalloc AlsDoubleVector[MaximumClippedPoints];
        Span<AlsDoubleVector> b = stackalloc AlsDoubleVector[MaximumClippedPoints];
        var count = System.Math.Min(incidentFace.Length, MaximumClippedPoints);
        for (var i = 0; i < count; i++) a[i] = incidentFace[i].Rotate(incidentToReference.Rotation) + incidentToReference.Position;
        var previous = referenceFace[^1];
        for (var i = 0; i < referenceFace.Length && count > 1; i++)
        {
            var current = referenceFace[i];
            var n = AlsDoubleVector.Cross(referenceNormal, previous - current) * referenceWinding;
            var lengthSquared = n.LengthSquared;
            // Chaos TVector::SafeNormalize uses the X axis for tiny edges.
            n = lengthSquared < (double)1e-4f ? new(1, 0, 0) : n * (1 / System.Math.Sqrt(lengthSquared));
            count = Clip(a[..count], b, n, AlsDoubleVector.Dot(current, n));
            var swap = a; a = b; b = swap; previous = current;
        }
        clippedCount = count;
        if (count == 4) Swap(a, 1, 2);
        else if (count > 4)
        {
            var rotation = SeparationToZ(referenceNormal);
            for (var i = 0; i < count; i++) a[i] = a[i].Rotate(rotation);
            Reduce(a[..count]); count = 4;
            for (var i = 0; i < count; i++) a[i] = a[i].Rotate(rotation.Conjugate());
        }
        // Commit only once input validation and the entire geometry stage finish.
        for (var i = 0; i < count; i++)
        {
            var p = a[i]; var projected = p - referenceNormal * AlsDoubleVector.Dot(p - referencePoint, referenceNormal);
            var incident = (p - incidentToReference.Position).Rotate(incidentToReference.Rotation.Conjugate());
            destination[i] = referenceIsShape0
                ? new(projected.ToSingle(), incident.ToSingle(), normal1.ToSingle())
                : new(incident.ToSingle(), projected.ToSingle(), normal1.ToSingle());
            var phiNormal=referenceIsShape0?normal1.Rotate(incidentToReference.Rotation):normal1*-1;
            destination[i]=destination[i] with {NativePhi=(float)AlsDoubleVector.Dot(projected-p,phiNormal)};
        }
        return count;
    }

    private static int Clip(ReadOnlySpan<AlsDoubleVector> input, Span<AlsDoubleVector> output,
        AlsDoubleVector normal, double distance)
    {
        var count = 0; var current = input[^1]; var dot = AlsDoubleVector.Dot(current, normal);
        var limit = distance + distance * (double)1e-8f;
        foreach (var point in input)
        {
            var previous = current; var previousDot = dot; current = point; dot = AlsDoubleVector.Dot(current, normal);
            if (dot <= limit)
            {
                if (previousDot > limit)
                {
                    output[count++] = Intersect(previous, current, previousDot, dot, distance);
                    if (count == output.Length) break;
                }
                output[count++] = current;
            }
            else if (previousDot < limit) output[count++] = Intersect(previous, current, previousDot, dot, distance);
            if (count == output.Length) break;
        }
        return count;
    }
    private static AlsDoubleVector Intersect(AlsDoubleVector a, AlsDoubleVector b, double da, double db, double d) =>
        System.Math.Abs(db - da) < (double)1e-8f ? a : a + (b - a) * ((d - da) / (db - da));

    private static void Reduce(Span<AlsDoubleVector> points)
    {
        var selected = 0; double deepest = float.MaxValue;
        for (var i = 0; i < points.Length; i++) if (points[i].Z < deepest) { selected = i; deepest = points[i].Z; }
        Swap(points, 0, selected); selected = 1; var farthest = -1d;
        for (var i = 1; i < points.Length; i++)
        {
            var d = points[i] - points[0]; var square = d.X * d.X + d.Y * d.Y;
            if (square > farthest) { selected = i; farthest = square; }
        }
        Swap(points, 1, selected); selected = 2; var area = 0d; var edge = points[1] - points[0];
        for (var i = 2; i < points.Length; i++)
        {
            var candidate = AlsDoubleVector.Cross(edge, points[i] - points[0]).Z;
            if (System.Math.Abs(candidate) > System.Math.Abs(area)) { selected = i; area = candidate; }
        }
        Swap(points, 2, selected); if (area < 0) Swap(points, 0, 1);
        selected = 3; area = 0;
        for (var i = 3; i < points.Length; i++) for (var j = 0; j < 3; j++)
        {
            var candidate = AlsDoubleVector.Cross(points[i] - points[j], points[(j + 1) % 3] - points[j]).Z;
            if (candidate > area) { selected = i; area = candidate; }
        }
        Swap(points, 3, selected);
    }
    private static AlsQuaternion SeparationToZ(AlsDoubleVector n)
    {
        // Chaos TRotation::FromRotatedVector, including its parallel-vector branch.
        var cross = AlsDoubleVector.Cross(n, new(0, 0, 1)); var size = System.Math.Sqrt(cross.LengthSquared);
        if (size == 0) return new(n.X, n.Y, n.Z, 0);
        var square = .5 * (1 + System.Math.Clamp(n.Z, -1, 1));
        cross *= System.Math.Sqrt(1 - square) / size;
        return new(cross.X, cross.Y, cross.Z, System.Math.Sqrt(square));
    }
    private static bool Unit(AlsDoubleVector v) => v.IsFinite && System.Math.Abs(v.LengthSquared - 1) <= 1e-5;
    private static void Swap(Span<AlsDoubleVector> p, int a, int b) { var value = p[a]; p[a] = p[b]; p[b] = value; }
}
