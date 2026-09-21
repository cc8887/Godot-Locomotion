using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

public static class AlsCapsuleCapsuleManifold
{
    // Native one-shot float pair space, including dynamic radius ownership.
    // A supplemental point exactly on the other segment axis has no normal.
    // Omit only that undefined point; retain the native closest contact and
    // all other finite supports so the solver can resolve the overlap.
    public static int Build(in AlsCapsuleGeometry a, in AlsPrecisePose poseA, bool dynamicA,
        in AlsCapsuleGeometry b, in AlsPrecisePose poseB, bool dynamicB, float cull, Span<AlsDetectedContact> destination,
        float alignedThreshold = .8f, float deepFraction = .05f, float radialFraction = .25f)
    {
        a.Validate(); b.Validate(); poseA.Validate(1e-5); poseB.Validate(1e-5);
        if (destination.Length < 3 || !float.IsFinite(cull) || cull < 0 || poseA.Scale != AlsDoubleVector.One || poseB.Scale != AlsDoubleVector.One ||
            !float.IsFinite(alignedThreshold) || alignedThreshold < 0 || alignedThreshold > 1 || !float.IsFinite(deepFraction) || deepFraction < 0 ||
            !float.IsFinite(radialFraction) || radialFraction < 0) throw new ArgumentException("Invalid capsule pair settings or poses.");
        var relative = AlsPrecisePose.Relative(poseA, poseB); var translation = relative.Position.ToSingle();
        var q = relative.Rotation; var rotation = new Quaternion((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);
        var axisA = Rotate(a.Axis, rotation); var axisB = b.Axis;
        var halfA = a.Height / 2f; var halfB = b.Height / 2f;
        var radiusA = dynamicA ? a.Radius : float.MaxValue; var radiusB = dynamicB ? b.Radius : float.MaxValue;
        var dot = Dot(axisA, axisB); if (dot < 0) { dot = -dot; axisA = -axisA; }
        var centerA = Rotate(a.Endpoint0 + (.5f * a.Height) * a.Axis, rotation) + translation;
        var centerB = b.Endpoint0 + (.5f * b.Height) * b.Axis;
        SegmentClosest(centerA + halfA * axisA, centerA - halfA * axisA,
            centerB + halfB * axisB, centerB - halfB * axisB, out var closestA, out var closestB);
        var delta = closestB - closestA; var distance = MathF.Sqrt(Dot(delta, delta));
        var phi = distance - (a.Radius + b.Radius); if (phi > cull) return 0;
        var normal = distance > 1e-4f ? -delta * (1f / distance) : radiusA <= radiusB ? Vector3.UnitZ : -Vector3.UnitZ;
        Span<AlsDetectedContact> points = stackalloc AlsDetectedContact[3];
        points[0] = Point(closestA - normal * a.Radius, closestB + normal * b.Radius, normal, phi, rotation, translation);
        var count = 1;
        var ta = Dot(closestA - centerA, axisA) / halfA; var tb = Dot(closestB - centerB, axisB) / halfB;
        if (!(ta < -1f + .2f && tb > 1f - .2f) && !(tb < -1f + .2f && ta > 1f - .2f) &&
            (dot > alignedThreshold || phi < -deepFraction * MathF.Min(radiusA, radiusB)))
        {
            if (radiusA <= radiusB) Extra(ta, centerA, axisA, halfA, a.Radius, centerB, axisB, halfB, b.Radius,
                normal, dot, false, rotation, translation, cull, radialFraction, points, ref count);
            else Extra(tb, centerB, axisB, halfB, b.Radius, centerA, axisA, halfA, a.Radius,
                normal, dot, true, rotation, translation, cull, radialFraction, points, ref count);
        }
        points[..count].CopyTo(destination); return count;
    }

    private static void Extra(float t, Vector3 firstCenter, Vector3 firstAxis, float firstHalf, float firstRadius,
        Vector3 secondCenter, Vector3 secondAxis, float secondHalf, float secondRadius, Vector3 closestNormal, float dot,
        bool swap, Quaternion rotation, Vector3 translation, float cull, float radial, Span<AlsDetectedContact> points, ref int count)
    {
        var orthogonal = Cross(firstAxis, Cross(firstAxis, closestNormal)); var squared = Dot(orthogonal, orthogonal);
        if (!(squared > .35f * .35f)) return;
        orthogonal *= 1f / MathF.Sqrt(squared);
        if (Dot(orthogonal, secondCenter - firstCenter) < 0) orthogonal = -orthogonal;
        var projected = 2f * firstHalf * dot;
        // The compiled native path shares this reciprocal between both limits.
        var inverseProjected = 1f / projected;
        var clippedMin = Dot((secondCenter - secondHalf * secondAxis) - (firstCenter + firstHalf * firstAxis), secondAxis) * inverseProjected;
        var clippedMax = Dot((secondCenter + secondHalf * secondAxis) - (firstCenter - firstHalf * firstAxis), secondAxis) * inverseProjected;
        var radialDelta = radial * (secondRadius / firstHalf); var radialMax = radialDelta + dot * (1f - radialDelta);
        var min = Max(Max(-1f, clippedMin), -radialMax); var max = Min(Min(1f, clippedMax), radialMax);
        if (min < t - .2f) Add(min, firstCenter, firstAxis, firstHalf, firstRadius, secondCenter, secondAxis, secondHalf, secondRadius,
            orthogonal, swap, rotation, translation, cull, points, ref count);
        if (max > t + .2f) Add(max, firstCenter, firstAxis, firstHalf, firstRadius, secondCenter, secondAxis, secondHalf, secondRadius,
            orthogonal, swap, rotation, translation, cull, points, ref count);
    }
    private static void Add(float t, Vector3 firstCenter, Vector3 firstAxis, float firstHalf, float firstRadius,
        Vector3 secondCenter, Vector3 secondAxis, float secondHalf, float secondRadius, Vector3 orthogonal,
        bool swap, Quaternion rotation, Vector3 translation, float cull, Span<AlsDetectedContact> points, ref int count)
    {
        var first = firstCenter + (t * firstHalf) * firstAxis + orthogonal * firstRadius;
        var second = ClosestLine(secondCenter - secondHalf * secondAxis, secondCenter + secondHalf * secondAxis, first);
        var delta = first - second; var distance = MathF.Sqrt(Dot(delta, delta));
        if (distance == 0) return; // Native divides by zero here and publishes NaN.
        var direction = delta * (1f / distance);
        var phi = distance - secondRadius;
        if (!(phi < cull)) return;
        var contact = second + secondRadius * direction;
        points[count++] = swap ? Point(contact, first, -direction, phi, rotation, translation) : Point(first, contact, direction, phi, rotation, translation);
    }
    private static AlsDetectedContact Point(Vector3 a, Vector3 b, Vector3 n, float phi, Quaternion q, Vector3 translation)
    {
        var local = Rotate(a - translation, Quaternion.Conjugate(q));
        if (!new AlsDoubleVector(local).IsFinite || !new AlsDoubleVector(b).IsFinite || !new AlsDoubleVector(n).IsFinite || !float.IsFinite(phi))
            throw new InvalidOperationException("Nonfinite native capsule pair contact.");
        return new(local, b, n) { NativePhi = phi };
    }
    private static Vector3 Rotate(Vector3 v, Quaternion q)
    {
        var xyz = new Vector3(q.X, q.Y, q.Z); var twice = 2f * Cross(xyz, v);
        return v + q.W * twice + Cross(xyz, twice);
    }
    // Vector3.Cross changes its intrinsic evaluation across runtime versions.
    // Preserve the native scalar product/subtraction rounding on both net8/net9.
    private static Vector3 Cross(Vector3 a, Vector3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    private static float Dot(Vector3 a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    // UE comparison semantics matter when clipping perpendicular axes yields NaN.
    private static float Min(float a, float b) => a < b ? a : b;
    private static float Max(float a, float b) => a > b ? a : b;
    private static Vector3 SafeNormal(Vector3 v)
    { var squared = Dot(v, v); return squared == 1 ? v : squared < 1e-8f ? Vector3.Zero : v * (1f / MathF.Sqrt(squared)); }
    private static Vector3 ClosestLine(Vector3 start, Vector3 end, Vector3 point)
    {
        var segment = end - start; var t = -Dot(start - point, segment) / Dot(segment, segment);
        t = t < 0 ? 0 : t < 1 ? t : 1; return start + t * segment;
    }
    private static void SegmentClosest(Vector3 a, Vector3 aEnd, Vector3 b, Vector3 bEnd, out Vector3 p, out Vector3 q)
    {
        var s1 = aEnd - a; var s2 = bEnd - b; var s3 = a - b;
        var u = SafeNormal(s1); var v = SafeNormal(s2);
        if (u == Vector3.Zero) { p = a; q = v == Vector3.Zero ? b : ClosestLine(b, bEnd, a); return; }
        if (v == Vector3.Zero) { p = ClosestLine(a, aEnd, b); q = b; return; }
        var near = Dot(u, u) * Dot(v, v) - Dot(u, v) * Dot(u, v) < 1e-4f;
        var d11 = Dot(s1, s1); var d12 = Dot(s1, s2); var d13 = Dot(s1, s3); var d22 = Dot(s2, s2); var d23 = Dot(s2, s3);
        var d = d11 * d22 - d12 * d12; var d1 = d; var d2 = d; float n1, n2;
        if (near || d < 1e-4f) { n1 = 0; d1 = 1; n2 = d23; d2 = d22; }
        else
        {
            n1 = d12 * d23 - d22 * d13; n2 = d11 * d23 - d12 * d13;
            if (n1 < 0) { n1 = 0; n2 = d23; d2 = d22; }
            else if (n1 > d1) { n1 = d1; n2 = d23 + d12; d2 = d22; }
        }
        if (n2 < 0)
        { n2 = 0; if (-d13 < 0) n1 = 0; else if (-d13 > d11) n1 = d1; else { n1 = -d13; d1 = d11; } }
        else if (n2 > d2)
        { n2 = d2; var edge = -d13 + d12; if (edge < 0) n1 = 0; else if (edge > d11) n1 = d1; else { n1 = edge; d1 = d11; } }
        var t1 = MathF.Abs(n1) < 1e-4f ? 0 : n1 / d1; var t2 = MathF.Abs(n2) < 1e-4f ? 0 : n2 / d2;
        p = a + t1 * s1; q = b + t2 * s2;
    }
}
