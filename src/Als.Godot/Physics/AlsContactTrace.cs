using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Physics;

// Opt-in diagnostic decorator: records fresh geometry queries. Restored
// manifolds bypass the provider; capture the island step to inspect solver input.
internal sealed class AlsContactTrace(IAlsContactGeometrySource source, AlsContactRegistry registry,
    string mesh, string[] names, Func<int> frame, int first, int last, string[] bones,
    Func<int, object>? geometry = null, Func<int, int, float>? cullDistance = null) : IAlsContactGeometrySource
{
    public bool IsInvalidated => source.IsInvalidated;
    public void PrepareStep(ReadOnlySpan<AlsIslandBodyState> previous, ReadOnlySpan<AlsProjectionVelocity> velocities,
        ReadOnlySpan<AlsIslandBody> bodies, double dt) => source.PrepareStep(previous, velocities, bodies, dt);
    public void PrepareBounds(ReadOnlySpan<AlsPrecisePose> shapeWorld) => source.PrepareBounds(shapeWorld);
    public bool AllowsPair(int a, int b) => source.AllowsPair(a, b);
    public bool AllowsPair(int a, int b, bool collidedLastStep) => source.AllowsPair(a, b, collidedLastStep);
    public bool ShouldReversePair(int a, int b) => source.ShouldReversePair(a, b);
    public void StageCommit() => source.StageCommit();
    public void PublishCommit() => source.PublishCommit();
    public void Abort() => source.Abort();
    public void Reset() => source.Reset();
    public bool TryGetManifoldSettings(int a, int b, out AlsContactManifoldSettings settings) => source.TryGetManifoldSettings(a, b, out settings);
    public int Query(int a, in AlsPrecisePose p, int b, in AlsPrecisePose q, Span<AlsDetectedContact> points)
    {
        var count = source.Query(a, p, b, q, points); var step = frame();
        var body0 = names[registry.At(a).Body]; var body1 = names[registry.At(b).Body];
        if (step < first || step > last || !bones.Contains(body0) && !bones.Contains(body1)) return count;
        if (count == 0 && geometry is null) return count;
        var contacts = new object[count];
        for (var i = 0; i < count; i++)
        {
            var c = points[i]; var p0 = new AlsDoubleVector(c.Point0).Rotate(p.Rotation) + p.Position;
            var p1 = new AlsDoubleVector(c.Point1).Rotate(q.Rotation) + q.Position;
            var normal = new AlsDoubleVector(c.Normal1).Rotate(q.Rotation);
            contacts[i] = new { point0 = V(p0), point1 = V(p1), normal = V(normal),
                localPoint0 = V(new(c.Point0)), localPoint1 = V(new(c.Point1)), localNormal1 = V(new(c.Normal1)), nativePhi = c.NativePhi,
                gap = System.Numerics.Vector3.Dot((p0 - p1).ToSingle(), normal.ToSingle()) };
        }
        GD.Print("CORE_CONTACT_TRACE " + JsonSerializer.Serialize(new { frame = step, mesh, body0, body1, shape0 = a, shape1 = b,
            center0 = V(p.Position), center1 = V(q.Position), rotation0 = Q(p.Rotation), rotation1 = Q(q.Rotation),
            geometry0 = geometry?.Invoke(a), geometry1 = geometry?.Invoke(b), cullDistance = cullDistance?.Invoke(a, b), contacts }));
        return count;
    }
    private static double[] V(AlsDoubleVector p) => [p.X, p.Y, p.Z];
    private static double[] Q(AlsQuaternion q) => [q.X, q.Y, q.Z, q.W];
}
