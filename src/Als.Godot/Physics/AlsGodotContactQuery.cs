using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using NVector = System.Numerics.Vector3;

namespace GodotAls.Physics;

// Geometry-only Jolt query space. It contains exactly one target shape, so every
// CollideShape point pair has an unambiguous registry identity. Core alone owns
// dynamic response. Convex shapes only for this adapter; no CCD or native Chaos
// narrow-phase parity is claimed. Godot's returned arrays allocate on Main.
internal sealed class AlsGodotContactQuery : IAlsContactGeometrySource, IDisposable
{
    private sealed class Binding
    {
        public required Shape3D Shape;
        public required Aabb Bounds;
        public required uint Revision;
        public required Action Changed;
        public bool Dirty;
    }
    private readonly AlsContactRegistry _registry;
    private readonly Binding?[] _bindings;
    private readonly PhysicsShapeQueryParameters3D _query = new() { CollisionMask = 1, CollideWithBodies = true, CollideWithAreas = false, Margin = 0 };
    private Rid _space, _body;
    private PhysicsDirectSpaceState3D _state = null!;
    private bool _bodyHasShape, _disposed;
    public int NarrowPhaseQueries { get; private set; }
    public bool IsInvalidated
    {
        get
        {
            Check();
            for (var i = 0; i < _bindings.Length; i++)
                if (_registry.Present(i) && (_bindings[i] is not { } binding || binding.Dirty ||
                    binding.Revision != _registry.Key(i).Revision || !GodotObject.IsInstanceValid(binding.Shape))) return true;
            return false;
        }
    }
    internal AlsGodotContactQuery(AlsContactRegistry registry)
    {
        Main(); _registry = registry; _bindings = new Binding[registry.Capacity];
        try
        {
            _space = PhysicsServer3D.SpaceCreate(); PhysicsServer3D.SpaceSetActive(_space, false);
            _body = PhysicsServer3D.BodyCreate(); PhysicsServer3D.BodySetMode(_body, PhysicsServer3D.BodyMode.Static);
            PhysicsServer3D.BodySetCollisionLayer(_body, 1); PhysicsServer3D.BodySetCollisionMask(_body, 0);
            PhysicsServer3D.BodySetSpace(_body, _space); _state = PhysicsServer3D.SpaceGetDirectState(_space);
        }
        catch { Dispose(); throw; }
    }
    internal void Bind(AlsContactShapeHandle handle, Shape3D shape)
    {
        Check(); if (_registry.IsLocked) throw new InvalidOperationException("Cannot bind query geometry during a solve.");
        if (!_registry.Present(handle.Slot) || _registry.Key(handle.Slot).Revision != handle.Revision) throw new ArgumentException("Stale shape binding.");
        var bounds = Bounds(shape); var old = _bindings[handle.Slot];
        if (old is not null && old.Revision == handle.Revision) throw new ArgumentException("Replace registry shape before rebinding geometry.");
        var binding = new Binding { Shape = shape, Bounds = bounds, Revision = handle.Revision, Changed = null! };
        binding.Changed = () => binding.Dirty = true;
        if (old is not null && GodotObject.IsInstanceValid(old.Shape)) old.Shape.Changed -= old.Changed;
        _bindings[handle.Slot] = binding; shape.Changed += binding.Changed;
    }
    public int Query(int shape0, in AlsPrecisePose world0, int shape1, in AlsPrecisePose world1, Span<AlsDetectedContact> destination)
    {
        Check(); var a = BindingAt(shape0); var b = BindingAt(shape1);
        // Rebase in double precision BEFORE converting to Godot float positions.
        // Only a common translation is removed; world axes/rotations are kept.
        // Returned points are converted directly from this pair frame to local
        // shape space, never round-tripped through large world coordinates.
        var t0 = ToGodot(world0 with { Position = AlsDoubleVector.Zero });
        var t1 = ToGodot(world1 with { Position = world1.Position - world0.Position });
        if (!(t0 * a.Bounds).Grow(1e-5f).Intersects((t1 * b.Bounds).Grow(1e-5f))) return 0;
        if (!_bodyHasShape) { PhysicsServer3D.BodyAddShape(_body, b.Shape.GetRid()); _bodyHasShape = true; }
        else PhysicsServer3D.BodySetShape(_body, 0, b.Shape.GetRid());
        PhysicsServer3D.BodySetState(_body, PhysicsServer3D.BodyState.Transform, t1);
        _query.Shape = a.Shape; _query.Transform = t0; _query.Motion = Vector3.Zero;
        var pairs = _state.CollideShape(_query, checked(destination.Length + 1)); NarrowPhaseQueries++;
        using var ownedPairs = (Godot.Collections.Array)pairs;
        if (pairs.Count % 2 != 0 || pairs.Count / 2 > destination.Length) throw new InvalidOperationException("Contact query capacity exceeded; manifold was not truncated.");
        if (pairs.Count == 0) return 0;
        // A face manifold can include separated points within Jolt's manifold
        // tolerance. Point1 - Point0 then reverses direction; it is NOT a normal.
        // This space has exactly one convex target and the query is also convex,
        // so every point belongs to the one hit returned by GetRestInfo.
        using var rest = _state.GetRestInfo(_query); NarrowPhaseQueries++;
        if (rest.Count == 0 || rest["rid"].AsRid() != _body || rest["shape"].AsInt32() != 0)
            throw new InvalidOperationException("Contact manifold has no matching geometric normal.");
        var normal = rest["normal"].AsVector3();
        if (!normal.IsFinite() || Math.Abs(normal.LengthSquared() - 1) > 1e-5f)
            throw new InvalidOperationException("Invalid contact normal.");
        var inverse0 = t0.AffineInverse(); var inverse1 = t1.AffineInverse();
        for (var i = 0; i < pairs.Count / 2; i++)
        {
            var p0 = pairs[2 * i]; var p1 = pairs[2 * i + 1];
            destination[i] = new(ToNative(inverse0 * p0).ToSingle(), ToNative(inverse1 * p1).ToSingle(),
                (ToNative(inverse1.Basis * normal) * .01).ToSingle());
        }
        return pairs.Count / 2;
    }
    private Binding BindingAt(int index)
    {
        if (!_registry.Present(index) || _bindings[index] is not { } binding || binding.Revision != _registry.Key(index).Revision ||
            binding.Dirty || !GodotObject.IsInstanceValid(binding.Shape))
            throw new InvalidOperationException("Query shape is missing, stale or changed; replace and rebind it.");
        return binding;
    }
    internal static Aabb Bounds(Shape3D shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var half = shape switch
        {
            SphereShape3D sphere => Vector3.One * sphere.Radius,
            BoxShape3D box => box.Size * .5f,
            CapsuleShape3D capsule => new Vector3(capsule.Radius, capsule.Height * .5f, capsule.Radius),
            ConvexPolygonShape3D => Vector3.Zero,
            _ => throw new NotSupportedException("Contact query currently requires sphere, box, capsule or convex shapes.")
        };
        if (shape is ConvexPolygonShape3D convex)
        {
            var points = convex.Points; if (points.Length < 4) throw new ArgumentException("Convex query shape needs at least four points.");
            var box = new Aabb(points[0], Vector3.Zero);
            foreach (var point in points) { if (!point.IsFinite()) throw new ArgumentException("Nonfinite convex point."); box = box.Expand(point); }
            return box;
        }
        if (!half.IsFinite() || half.X <= 0 || half.Y <= 0 || half.Z <= 0) throw new ArgumentException("Invalid query shape dimensions.");
        return new(-half, half * 2);
    }
    internal static Transform3D ToGodot(AlsPrecisePose native)
    {
        native.Validate(1e-5); if (native.Scale != AlsDoubleVector.One) throw new ArgumentException("Query pose must be rigid.");
        var q = native.Rotation; var p = AlsFootIkCoordinates.FromNative(native.Position);
        return new(new Basis(new Quaternion((float)-q.Y, (float)-q.Z, (float)q.X, (float)q.W).Normalized()), new(p.X, p.Y, p.Z));
    }
    private static AlsDoubleVector ToNative(Vector3 value) => AlsFootIkCoordinates.ToNative(new NVector(value.X, value.Y, value.Z));
    private void Check() { Main(); ObjectDisposedException.ThrowIf(_disposed, this); }
    private static void Main() { if (!GodotThread.IsMainThread()) throw new InvalidOperationException("Physics geometry queries require Main."); }
    public void Dispose()
    {
        Main(); if (_disposed) return; _disposed = true;
        foreach (var binding in _bindings) if (binding is not null && GodotObject.IsInstanceValid(binding.Shape)) binding.Shape.Changed -= binding.Changed;
        _query.Dispose();
        if (_body.IsValid) PhysicsServer3D.FreeRid(_body);
        if (_space.IsValid) PhysicsServer3D.FreeRid(_space);
    }
}
