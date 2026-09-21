using GodotAls.Core.Locomotion;
using System.Numerics;

namespace GodotAls.Core.Physics;

public readonly record struct AlsCapsuleGeometry(Vector3 Endpoint0, Vector3 Axis, float Height, float Radius)
{
    public void Validate()
    {
        if (!new AlsDoubleVector(Endpoint0).IsFinite || !new AlsDoubleVector(Axis).IsFinite ||
            MathF.Abs(Axis.LengthSquared() - 1) > 1e-5f || !float.IsFinite(Height) || Height <= 0 ||
            !float.IsFinite(Radius) || Radius <= 0) throw new ArgumentException("Invalid native capsule geometry.");
    }
}

// Native capsule/convex manifold rules for a proven box-face interior. The
// capsule axis must lie outside that face and its complete radius-expanded
// segment inside the lateral face bounds. Edge/deep contacts remain the geometry
// provider's responsibility. Native units are cm. No GJK approximation is used
// to claim coverage outside this region.
public static class AlsCapsuleBoxManifold
{
    public static bool TryInteriorFace(double radius, double length, in AlsPrecisePose capsule,
        AlsDoubleVector boxHalf, in AlsPrecisePose box, double cullDistance,
        Span<AlsDetectedContact> destination, out int count, double edgeInset = 0)
    {
        if (!double.IsFinite(radius) || radius <= 0 || !double.IsFinite(length) || length <= 0)
            throw new ArgumentException("Invalid capsule dimensions.");
        return TryInteriorFace(new(new(0, 0, (float)(-.5 * (float)length)), Vector3.UnitZ, (float)length, (float)radius),
            capsule, boxHalf, box, cullDistance, destination, out count, edgeInset);
    }

    public static bool TryInteriorFace(in AlsCapsuleGeometry geometry, in AlsPrecisePose capsule,
        AlsDoubleVector boxHalf, in AlsPrecisePose box, double cullDistance,
        Span<AlsDetectedContact> destination, out int count, double edgeInset = 0)
    {
        count = 0; geometry.Validate(); capsule.Validate(1e-5); box.Validate(1e-5);
        double radius = geometry.Radius, length = geometry.Height;
        if (!double.IsFinite(radius) || radius <= 0 || !double.IsFinite(length) || length <= 0 ||
            !boxHalf.IsFinite || boxHalf.X <= 0 || boxHalf.Y <= 0 || boxHalf.Z <= 0 ||
            !double.IsFinite(cullDistance) || cullDistance < 0 || !double.IsFinite(edgeInset) || edgeInset < 0 ||
            !float.IsFinite((float)radius) || (float)radius <= 0 || !float.IsFinite((float)length) || (float)length <= 0 ||
            capsule.Scale != AlsDoubleVector.One || box.Scale != AlsDoubleVector.One)
            throw new ArgumentException("Capsule-box manifold requires finite positive dimensions and rigid poses.");
        if (destination.Length < 3) throw new ArgumentException("Capsule manifold requires capacity for three points.");
        // FCapsule stores the local segment and radius in float. Relative-world
        // geometry remains double, including large common world translations.
        radius = (float)radius; length = (float)length;
        var relative = AlsPrecisePose.Relative(capsule, box);
        var axis = new AlsDoubleVector(geometry.Axis).Rotate(relative.Rotation);
        var start = new AlsDoubleVector(geometry.Endpoint0).Rotate(relative.Rotation) + relative.Position;
        var end = start + axis * length;
        var normal = AlsDoubleVector.Zero; var plane = AlsDoubleVector.Zero; var found = false;
        for (var a = 0; a < 3 && !found; a++) for (var sign = -1; sign <= 1 && !found; sign += 2)
        {
            // Strictly separated core excludes penetration/EPA and normal ties
            // at the edge of the box. The full capsule fits over this one face.
            if (System.Math.Min(sign * start[a], sign * end[a]) - boxHalf[a] <= 1e-4) continue;
            var inside = true;
            for (var t = 0; t < 3; t++) if (t != a)
                inside &= System.Math.Min(start[t], end[t]) - radius > -boxHalf[t] + edgeInset + 1e-3 &&
                    System.Math.Max(start[t], end[t]) + radius < boxHalf[t] - edgeInset - 1e-3;
            if (!inside) continue;
            normal = a == 0 ? new(sign, 0, 0) : a == 1 ? new(0, sign, 0) : new(0, 0, sign);
            plane = normal * boxHalf[a]; found = true;
        }
        if (!found) return false;
        var d0 = AlsDoubleVector.Dot(start - plane, normal); var d1 = AlsDoubleVector.Dot(end - plane, normal);
        if (System.Math.Min(d0, d1) - radius > cullDistance) return true;
        var along = AlsDoubleVector.Dot(axis, normal);
        var cylinderNormal = (normal - axis * along).RemoveScaling();
        var inverse = relative.Rotation.Conjugate();
        Span<AlsDetectedContact> points = stackalloc AlsDetectedContact[3];
        if (System.Math.Abs(along) < .01)
        {
            // Native cylinder contacts project along the cylinder normal, not
            // the box face normal. Preserve endpoint order and both points.
            var divisor = AlsDoubleVector.Dot(cylinderNormal, normal);
            var p0 = start - cylinderNormal * radius; var p1 = end - cylinderNormal * radius;
            Add(p0, p0 + cylinderNormal * (AlsDoubleVector.Dot(plane - p0, normal) / divisor), cylinderNormal, points, ref count, relative.Position, inverse);
            Add(p1, p1 + cylinderNormal * (AlsDoubleVector.Dot(plane - p1, normal) / divisor), cylinderNormal, points, ref count, relative.Position, inverse);
        }
        else
        {
            var cap = (d0 < d1 ? start : end) - normal * radius;
            if (System.Math.Abs(along) < .707)
            {
                var threshold = (double).1f * radius; threshold *= threshold;
                var p0 = start - cylinderNormal * radius; var p1 = end - cylinderNormal * radius;
                if ((p0 - cap).LengthSquared > threshold)
                    Add(p0, p0 - normal * AlsDoubleVector.Dot(p0 - plane, normal), normal, points, ref count, relative.Position, inverse);
                if ((p1 - cap).LengthSquared > threshold)
                    Add(p1, p1 - normal * AlsDoubleVector.Dot(p1 - plane, normal), normal, points, ref count, relative.Position, inverse);
            }
            // End-cap GJK result comes last, including near-duplicate rejection
            // against the cylinder points above. No per-point cull of supports.
            Add(cap, cap - normal * AlsDoubleVector.Dot(cap - plane, normal), normal, points, ref count, relative.Position, inverse);
        }
        // A very long, thin, slightly tilted cylinder can project past a face
        // edge even if its bounds fit. Such a segment needs native clipping.
        for (var i = 0; i < count; i++) for (var a = 0; a < 3; a++) if (normal[a] == 0 &&
            System.Math.Abs(new AlsDoubleVector(points[i].Point1)[a]) >= boxHalf[a] - edgeInset - 1e-4)
        { count = 0; return false; }
        points[..count].CopyTo(destination);
        return true;
    }
    private static void Add(AlsDoubleVector p, AlsDoubleVector q, AlsDoubleVector normal,
        Span<AlsDetectedContact> destination, ref int count, AlsDoubleVector relativePosition, AlsQuaternion inverse)
        => destination[count++] = new((p - relativePosition).Rotate(inverse).ToSingle(), q.ToSingle(), normal.ToSingle());
}
