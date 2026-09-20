using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Locomotion;

// Call during the main physics phase, AFTER the worker has produced the current
// rig requests. The next worker phase receives only the value-type observations.
// Visibility's Godot layer mapping is explicit; it is not the motor's floor mask.
internal sealed class AlsRefactoredFootQueryGather : IDisposable
{
    private readonly Node3D _worldOwner;
    private readonly PhysicsRayQueryParameters3D _ray;
    private bool _disposed;
    public int LastQueryCount { get; private set; }

    public AlsRefactoredFootQueryGather(Node3D worldOwner, uint visibilityMask,
        ReadOnlySpan<Rid> ignoredActorBodies)
    {
        RequireMainThread();
        ArgumentNullException.ThrowIfNull(worldOwner);
        if (visibilityMask == 0) throw new ArgumentException("Foot queries require an explicit Visibility layer mapping.");
        _worldOwner = worldOwner;
        var excluded = new Godot.Collections.Array<Rid>();
        foreach (var body in ignoredActorBodies) excluded.Add(body);
        _ray = new PhysicsRayQueryParameters3D
        {
            CollisionMask = visibilityMask, Exclude = excluded,
            CollideWithBodies = true, CollideWithAreas = false,
            HitBackFaces = false, HitFromInside = false,
        };
    }

    public AlsFootRigObservations Gather(in AlsFootRigQueries queries)
    {
        // This guard must run before touching any Godot object, including the owner.
        RequireMainThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        LastQueryCount = 0;
        if (!Engine.IsInPhysicsFrame()) throw new InvalidOperationException("Foot queries require the main physics phase.");
        if (queries.Identity.SlotGeneration == 0 || queries.RequestSerial == 0)
            throw new ArgumentException("Unissued foot rig query.");
        // Validate both legs before making the first engine call.
        if (queries.LeftEnabled) Validate(queries.Left);
        if (queries.RightEnabled) Validate(queries.Right);
        if (!queries.LeftEnabled && !queries.RightEnabled) return new(queries, default, default);
        if (!_worldOwner.IsInsideTree()) throw new InvalidOperationException("Foot queries require a live physics world.");
        var space = _worldOwner.GetWorld3D().DirectSpaceState;
        var left = queries.LeftEnabled ? Trace(space, queries.Left) : default;
        var right = queries.RightEnabled ? Trace(space, queries.Right) : default;
        return new(queries, left, right);
    }

    private AlsFootTraceRigHit Trace(PhysicsDirectSpaceState3D space, AlsFootTraceSegment segment)
    {
        // The segment already includes the VM-to-world transform. Applying it
        // again here, or replacing it with last frame's socket, moves the cast.
        _ray.From = Position(segment.Start); _ray.To = Position(segment.End);
        LastQueryCount++;
        using var hit = space.IntersectRay(_ray);
        if (hit.Count == 0) return default;
        var point = hit["position"].AsVector3(); var normal = hit["normal"].AsVector3();
        if (!point.IsFinite() || !normal.IsFinite()) throw new InvalidOperationException("Nonfinite foot physics observation.");
        return new(true, new(-(double)point.Z * 100, (double)point.X * 100, (double)point.Y * 100),
            new(-normal.Z, normal.X, normal.Y)) { ColliderIdentity = hit["collider_id"].AsUInt64() };
        // No normalization or walkability filtering here: the native consumer
        // applies inverse VM scale before testing its authored slope limit.
    }

    private static Vector3 Position(AlsDoubleVector native) =>
        new((float)(native.Y * .01), (float)(native.Z * .01), (float)(-native.X * .01));
    private static void Validate(AlsFootTraceSegment segment)
    {
        if (!segment.Start.IsFinite || !segment.End.IsFinite ||
            !Position(segment.Start).IsFinite() || !Position(segment.End).IsFinite() ||
            Position(segment.Start) == Position(segment.End))
            throw new ArgumentException("Invalid or unrepresentable foot ray.");
    }
    private static void RequireMainThread()
    {
        if (!GodotThread.IsMainThread()) throw new InvalidOperationException("Refactored foot physics belongs to the main thread.");
    }
    public void Dispose()
    {
        RequireMainThread();
        if (_disposed) return;
        _ray.Dispose(); _disposed = true;
    }
}
