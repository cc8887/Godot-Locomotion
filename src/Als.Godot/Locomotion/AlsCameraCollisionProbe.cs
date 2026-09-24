using Godot;
using GodotAls.Core.Camera;
using GodotAls.Core.Locomotion;

namespace GodotAls.Locomotion;

// Transient scene-query failures may be retried from committed camera history.
// Invalid inputs, expired owners and programming errors remain terminal.
internal sealed class AlsCameraQueryException(string message) : InvalidOperationException(message);

// Main-thread, query-only adapter. Mask is explicitly Godot's mask; it is never
// taken directly from the exported UE TraceChannel enum.
internal sealed class AlsCameraCollisionProbe : IDisposable
{
    private const int Capacity = 64;
    private readonly ShapeCast3D _cast;
    private readonly SphereShape3D _sphere = new();
    private bool _disposed;
    internal int QueryCount { get; private set; }
    internal bool StartedPenetrating { get; private set; }
    internal bool AdjustedStart { get; private set; }
    internal AlsCameraCollisionProbe(Node parent, uint mask, IEnumerable<Rid> excluded)
    {
        Main();
        if (!parent.IsInsideTree()) throw new InvalidOperationException("Camera query parent must be in the scene tree.");
        _cast = new ShapeCast3D { Name = "AlsCameraCollision", Shape = _sphere, Enabled = false,
            TopLevel = true, ExcludeParent = false, CollisionMask = mask, CollideWithAreas = false,
            CollideWithBodies = true, Margin = 0, MaxResults = Capacity };
        parent.AddChild(_cast);
        SetExcludedBodies(excluded);
    }
    // Ragdoll activation/recovery can replace the owner's physical RIDs.
    internal void SetExcludedBodies(IEnumerable<Rid> excluded)
    {
        Main(); ObjectDisposedException.ThrowIf(_disposed, this);
        var bodies = excluded.ToArray();
        _cast.ClearExceptions(); foreach (var rid in bodies) _cast.AddExceptionRid(rid);
    }
    internal AlsCameraTraceResponse Query(AlsCameraTraceRequest request, AlsDoubleVector ownerLocation, float meshScale)
    {
        Main(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (!GodotObject.IsInstanceValid(_cast) || !_cast.IsInsideTree()) throw new InvalidOperationException("Camera query owner expired.");
        if (!request.Start.IsFinite || !request.End.IsFinite || !ownerLocation.IsFinite ||
            !float.IsFinite(request.Radius) || request.Radius <= 0 || !float.IsFinite(meshScale) || meshScale <= 0)
            throw new ArgumentException("Invalid camera collision input.");
        QueryCount = 0; StartedPenetrating = AdjustedStart = false;
        var start = GodotPosition(request.Start); var end = GodotPosition(request.End); var radius = request.Radius * .01f;
        Overlap(start, radius);
        StartedPenetrating = HasPenetration(start, radius);
        if (StartedPenetrating)
        {
            // Native recovery sums the inflated-sphere MTD of each overlapping
            // body/shape once, then rejects adjustments away from the actor.
            var inflated = radius + meshScale * .01f; Overlap(start, inflated);
            var contacts = new Dictionary<(Rid, int), Vector3>();
            for (var i = 0; i < _cast.GetCollisionCount(); i++)
            {
                var normal = _cast.GetCollisionNormal(i);
                if (!normal.IsFinite() || normal.LengthSquared() < .5f) return Response(start, start);
                var depth = inflated - (start - _cast.GetCollisionPoint(i)).Dot(normal);
                if (depth <= .000001f) continue;
                var key = (_cast.GetColliderRid(i), _cast.GetColliderShape(i));
                var adjustment = normal * depth;
                if (!contacts.TryGetValue(key, out var existing) || existing.LengthSquared() < adjustment.LengthSquared()) contacts[key] = adjustment;
            }
            var sum = Vector3.Zero; foreach (var adjustment in contacts.Values) sum += adjustment;
            if (!sum.IsFinite() || sum.LengthSquared() <= 1e-12f ||
                (GodotPosition(ownerLocation) - start).Normalized().Dot(sum.Normalized()) < -.0001f) return Response(start, start);
            start += sum; AdjustedStart = true;
            Overlap(start, radius);
            if (HasPenetration(start, radius)) return Response(start, start);
        }
        _sphere.Radius = radius; _cast.GlobalTransform = new(Basis.Identity, start); _cast.TargetPosition = end - start;
        _cast.ForceShapecastUpdate(); QueryCount++;
        if (!_cast.IsColliding()) return Response(start, end);
        var fraction = _cast.GetClosestCollisionSafeFraction();
        if (!float.IsFinite(fraction) || fraction < 0 || fraction > 1) throw new AlsCameraQueryException("Invalid camera sweep fraction.");
        return Response(start, start + (end - start) * fraction);
    }
    private void Overlap(Vector3 start, float radius)
    {
        _sphere.Radius = radius; _cast.GlobalTransform = new(Basis.Identity, start); _cast.TargetPosition = Vector3.Zero;
        _cast.ForceShapecastUpdate(); QueryCount++;
        if (_cast.GetCollisionCount() >= Capacity) throw new AlsCameraQueryException("Camera overlap capacity exceeded.");
    }
    private bool HasPenetration(Vector3 start, float radius)
    {
        for (var i = 0; i < _cast.GetCollisionCount(); i++)
        {
            var normal = _cast.GetCollisionNormal(i);
            if (!normal.IsFinite() || normal.LengthSquared() < .5f || radius - (start - _cast.GetCollisionPoint(i)).Dot(normal) > .000001f) return true;
        }
        return false;
    }
    private static Vector3 GodotPosition(AlsDoubleVector value)
    {
        var p = new Vector3((float)(value.Y * .01), (float)(value.Z * .01), (float)(-value.X * .01));
        return p.IsFinite() ? p : throw new ArgumentException("Camera location exceeds scene precision.");
    }
    private static AlsCameraTraceResponse Response(Vector3 start, Vector3 result) => new(Native(start), Native(result));
    internal static AlsDoubleVector Native(Vector3 p) => new(-(double)p.Z * 100, (double)p.X * 100, (double)p.Y * 100);
    private static void Main() { if (!GodotThread.IsMainThread()) throw new InvalidOperationException("Camera queries belong to Main."); }
    public void Dispose()
    { Main(); if (_disposed) return; _disposed = true; if (GodotObject.IsInstanceValid(_cast)) _cast.Free(); _sphere.Dispose(); }
}
