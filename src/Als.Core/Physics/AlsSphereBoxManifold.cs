using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// Native SphereBoxContactPoint + ConstructSphereBoxOneShotManifold. The box's
// PhiWithNormal uses its AABB directly, including interior ties and the unusual
// SafeNormalize near-surface fallback; polygon pair margin is not applied here.
public static class AlsSphereBoxManifold
{
    public static bool Build(Vector3 center, float radius, in AlsPrecisePose sphere,
        AlsDoubleVector min, AlsDoubleVector max, in AlsPrecisePose box, float cull, out AlsDetectedContact point)
    {
        point = default; sphere.Validate(1e-5); box.Validate(1e-5);
        if (!new AlsDoubleVector(center).IsFinite || !float.IsFinite(radius) || radius <= 0 ||
            !min.IsFinite || !max.IsFinite || min.X >= max.X || min.Y >= max.Y || min.Z >= max.Z ||
            !float.IsFinite(cull) || cull < 0 || sphere.Scale != AlsDoubleVector.One || box.Scale != AlsDoubleVector.One)
            throw new ArgumentException("Sphere-box needs native dimensions and rigid poses.");
        var worldCenter = new AlsDoubleVector(center).Rotate(sphere.Rotation) + sphere.Position;
        var localCenter = (worldCenter - box.Position).Rotate(box.Rotation.Conjugate());
        if (!worldCenter.IsFinite || !localCenter.IsFinite) throw new ArgumentException("Sphere-box transform overflow.");
        var upper = localCenter - max; var lower = min - localCenter;
        AlsDoubleVector normal; double distance;
        if (upper.X <= 0 && upper.Y <= 0 && upper.Z <= 0 && lower.X <= 0 && lower.Y <= 0 && lower.Z <= 0)
        {
            var distances = new AlsDoubleVector(System.Math.Max(upper.X, lower.X), System.Math.Max(upper.Y, lower.Y), System.Math.Max(upper.Z, lower.Z));
            var axis = distances.X > distances.Y ? (distances.X > distances.Z ? 0 : 2) : (distances.Y > distances.Z ? 1 : 2);
            var sign = upper[axis] > lower[axis] ? 1 : -1;
            normal = axis == 0 ? new(sign, 0, 0) : axis == 1 ? new(0, sign, 0) : new(0, 0, sign);
            distance = distances[axis];
        }
        else
        {
            static double Component(double upper, double lower) => upper > 0 ? upper : lower > 0 ? -lower : 0;
            normal = new(Component(upper.X, lower.X), Component(upper.Y, lower.Y), Component(upper.Z, lower.Z));
            var squared = normal.LengthSquared;
            if (!double.IsFinite(squared)) throw new ArgumentException("Sphere-box distance overflow.");
            if (squared < (double)1e-4f) { normal = new(1, 0, 0); distance = 0; }
            else { distance = System.Math.Sqrt(squared); normal *= 1 / distance; }
        }
        var phi = distance - radius; var nativePhi = (float)phi;
        if (!(phi < cull)) return false; // Native one-shot compares double before storing float.
        var worldNormal = normal.Rotate(box.Rotation);
        var location = worldCenter - worldNormal * radius;
        var p0 = (location - sphere.Position).Rotate(sphere.Rotation.Conjugate()).ToSingle();
        var p1 = (location - worldNormal * phi - box.Position).Rotate(box.Rotation.Conjugate()).ToSingle();
        if (!new AlsDoubleVector(p0).IsFinite || !new AlsDoubleVector(p1).IsFinite || !float.IsFinite(nativePhi))
            throw new ArgumentException("Sphere-box contact exceeds native storage.");
        point = new(p0, p1, normal.ToSingle()) { NativePhi = nativePhi }; return true;
    }
}
