using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using NVector = System.Numerics.Vector3;

namespace GodotAls.Physics;

// Main-thread, fixed-topology geometry/motion binding for an explicitly selected
// environment subtree. Resources are borrowed from the scene, never disposed by
// this owner. It does not advance Godot bodies or resolve material combine rules.
internal sealed class AlsSceneContactSet : IDisposable
{
    private sealed class Entry
    {
        internal required StaticBody3D Body;
        internal required uint Owner;
        internal required int ShapeIndex, BodyIndex;
        internal required Shape3D Shape;
        internal AlsContactShapeHandle Handle;
        internal AlsRegisteredContactShape Registered;
        internal Action Changed = null!;
        internal bool Dirty, Present;
    }
    private readonly StaticBody3D[] _bodies;
    private readonly Entry[] _entries;
    private readonly AlsIslandKinematicTarget[] _targets;
    private readonly AlsPrecisePose[] _lastCapture;
    private readonly bool[] _teleported, _teleportRebound;
    private readonly int _firstBody;
    private AlsContactRegistry? _registry;
    private AlsGodotContactQuery? _query;
    private bool _disposed;
    private AlsJointIsland? _capturedIsland;
    internal int BodyCount => _bodies.Length;
    internal int ShapeCount => _entries.Length;
    internal StaticBody3D BodyAt(int index) { Check(); return _bodies[index]; }
    internal AlsContactShapeHandle ShapeAt(int index) { Check(); return _entries[index].Handle; }
    internal AlsRegisteredContactShape ShapeDefinitionAt(int index) { Check(); return _entries[index].Registered; }
    internal object[] ExportNativeEnvironment()
    {
        Check();
        static double[] V(AlsDoubleVector v) => [v.X, v.Y, v.Z];
        static object Pose(AlsPrecisePose p) => new { position = V(p.Position), rotation = new[] { p.Rotation.X, p.Rotation.Y, p.Rotation.Z, p.Rotation.W } };
        return _entries.Select(e =>
        {
            var world = FromWorld(e.Body.GlobalTransform * e.Body.ShapeOwnerGetTransform(e.Owner));
            object geometry = e.Shape switch
            {
                BoxShape3D box => new { type = "box", size = new[] { (double)box.Size.Z * 100, (double)box.Size.X * 100, (double)box.Size.Y * 100 } },
                ConvexPolygonShape3D convex => new { type = "convex", vertices = convex.Points.Select(p => V(ToNative(p))).ToArray() },
                _ => throw new NotSupportedException("Native world baseline currently supports environment boxes and convex hulls.")
            };
            return (object)new { name = e.Body.Name.ToString(), body = e.BodyIndex, world = Pose(world), geometry,
                kinematic = e.Body is AnimatableBody3D };
        }).ToArray();
    }

    internal AlsSceneContactSet(Node root, int firstBody)
    {
        Main(); ArgumentNullException.ThrowIfNull(root);
        if (!root.IsInsideTree() || firstBody < 0) throw new ArgumentException("Scene contacts require a live subtree and nonnegative body offset.");
        _firstBody = firstBody; var bodies = new List<StaticBody3D>(); var entries = new List<Entry>();
        void Visit(Node node)
        {
            if (node is PhysicsBody3D physics)
            {
                if (physics is not StaticBody3D body) throw new NotSupportedException("Environment binding cannot borrow another dynamic integration owner.");
                FromWorld(body.GlobalTransform); var index = firstBody + bodies.Count; bodies.Add(body);
                foreach (var ownerValue in body.GetShapeOwners())
                {
                    var owner = (uint)ownerValue; FromWorld(body.ShapeOwnerGetTransform(owner));
                    for (var shape = 0; shape < body.ShapeOwnerGetShapeCount(owner); shape++)
                    {
                        var resource = body.ShapeOwnerGetShape(owner, shape); ValidateShape(resource);
                        entries.Add(new() { Body = body, BodyIndex = index, Owner = owner, ShapeIndex = shape, Shape = resource });
                    }
                }
            }
            foreach (var child in node.GetChildren()) Visit(child);
        }
        Visit(root); _bodies = bodies.ToArray(); _entries = entries.ToArray();
        _targets = new AlsIslandKinematicTarget[_bodies.Length]; _lastCapture = new AlsPrecisePose[_bodies.Length];
        _teleported = new bool[_bodies.Length]; _teleportRebound = new bool[_bodies.Length];
        for (var i = 0; i < BodyCount; i++) _lastCapture[i] = FromWorld(_bodies[i].GlobalTransform);
    }

    internal void InitializeBodies(Span<AlsIslandBody> bodies, Span<AlsIslandBodyState> states)
    {
        Check(); if (bodies.Length < _firstBody + BodyCount || states.Length != bodies.Length) throw new ArgumentException("Scene body buffer is too small.");
        for (var i = 0; i < BodyCount; i++)
        {
            bodies[_firstBody + i] = new(AlsPrecisePose.Identity, default, GravityEnabled: false, ExternallyDriven: true);
            states[_firstBody + i] = new(_lastCapture[i], default);
        }
    }
    internal void Bind(AlsContactRegistry registry, AlsGodotContactQuery query)
    {
        Check(); if (_registry is not null || registry.BodyCount < _firstBody + BodyCount) throw new InvalidOperationException("Scene contact binding is already configured or has a foreign registry.");
        _registry = registry; _query = query;
        try
        {
            foreach (var entry in _entries)
            {
                entry.Registered = Definition(entry); entry.Handle = registry.Register(entry.Registered); entry.Present = true;
                BindQuery(query,entry.Handle,entry.Shape); Observe(entry);
            }
        }
        catch { Dispose(); throw; }
    }
    // Explicit teleport clears contact identity instead of applying a spurious
    // large kinematic velocity. The request remains until a capture succeeds.
    internal void MarkTeleported(StaticBody3D body)
    {
        Check(); var index = Array.IndexOf(_bodies, body);
        if (index < 0) throw new ArgumentException("Body does not belong to this scene binding.");
        _teleported[index] = true; _teleportRebound[index] = false;
    }
    internal ReadOnlySpan<AlsIslandKinematicTarget> Capture(AlsJointIsland island, double dt)
    {
        Check(); if (_registry is null || _registry.IsLocked || island.BodyCount != _registry.BodyCount)
            throw new InvalidOperationException("Scene capture must precede the matching island solve.");
        if (!double.IsFinite(dt) || dt <= 0) throw new ArgumentOutOfRangeException(nameof(dt));
        _capturedIsland = null;
        // Validate complete topology and geometry before updating registry slots.
        foreach (var body in _bodies) if (Live(body))
        {
            FromWorld(body.GlobalTransform);
            foreach (var ownerValue in body.GetShapeOwners())
            {
                var owner = (uint)ownerValue; FromWorld(body.ShapeOwnerGetTransform(owner));
                for (var shape = 0; shape < body.ShapeOwnerGetShapeCount(owner); shape++)
                {
                    var found = false;
                    foreach (var entry in _entries) if (entry.Body == body && entry.Owner == owner && entry.ShapeIndex == shape) { found = true; break; }
                    if (!found) throw new InvalidOperationException("Scene topology gained a shape; rebuild the fixed-capacity binding.");
                    ValidateShape(body.ShapeOwnerGetShape(owner, shape));
                }
            }
        }
        for (var i = 0; i < BodyCount; i++)
        {
            var body = _bodies[i]; var previous = island.BodyAt(_firstBody + i);
            var next = previous with { Velocity = default };
            if (Live(body))
            {
                var pose = FromWorld(body.GlobalTransform);
                next = body is AnimatableBody3D && !_teleported[i]
                    ? AlsKinematicMotion.PositionTarget(previous.Actor, pose, dt) : new(pose, default);
                if (body is not AnimatableBody3D && !_teleported[i])
                    next = next with { Velocity = new(ToNative(body.ConstantLinearVelocity).ToSingle(), AngularToNative(body.ConstantAngularVelocity)) };
            }
            if (!new AlsDoubleVector(next.Velocity.Linear).IsFinite || !new AlsDoubleVector(next.Velocity.Angular).IsFinite)
                throw new ArgumentException("Scene body velocity must be finite.");
            _targets[i] = new(_firstBody + i, next);
        }
        for (var i = 0; i < BodyCount; i++) if (Live(_bodies[i]))
        {
            var pose = _targets[i].State.Actor;
            if (_teleported[i] && !_teleportRebound[i] || _bodies[i] is not AnimatableBody3D && pose != _lastCapture[i])
            { _registry.RebindBody(_firstBody + i); _teleportRebound[i] |= _teleported[i]; }
            _lastCapture[i] = pose;
        }
        foreach (var entry in _entries)
        {
            var exists = Live(entry.Body) && entry.Body.GetShapeOwners().Contains((int)entry.Owner) &&
                entry.ShapeIndex < entry.Body.ShapeOwnerGetShapeCount(entry.Owner);
            if (!exists)
            {
                if (entry.Present) { _registry.Remove(entry.Handle); entry.Present = false; }
                continue;
            }
            var shape = entry.Body.ShapeOwnerGetShape(entry.Owner, entry.ShapeIndex); var definition = Definition(entry);
            if (!entry.Present || entry.Dirty || shape != entry.Shape || definition != entry.Registered)
            {
                if (GodotObject.IsInstanceValid(entry.Shape) && entry.Changed is not null) entry.Shape.Changed -= entry.Changed;
                entry.Shape = shape; entry.Registered = definition;
                entry.Handle = entry.Present ? _registry.Replace(entry.Handle, definition) : _registry.Register(definition);
                entry.Present = true; BindQuery(_query!,entry.Handle,shape); Observe(entry);
            }
        }
        _capturedIsland = island; return _targets;
    }
    internal void CommitCapture(AlsJointIsland island)
    {
        Check(); if (!ReferenceEquals(_capturedIsland, island)) throw new InvalidOperationException("No matching scene capture.");
        foreach (var target in _targets) if (island.BodyAt(target.Body) != target.State)
            throw new InvalidOperationException("External motion was not successfully published by the island.");
        Array.Clear(_teleported); Array.Clear(_teleportRebound); _capturedIsland = null;
    }
    private void Observe(Entry entry)
    { entry.Dirty = false; entry.Changed = () => entry.Dirty = true; entry.Shape.Changed += entry.Changed; }
    private static AlsRegisteredContactShape Definition(Entry entry) => new(entry.BodyIndex,
        FromWorld(entry.Body.ShapeOwnerGetTransform(entry.Owner)), entry.Body.CollisionLayer, entry.Body.CollisionMask,
        entry.Body.ShapeOwnerGetShape(entry.Owner, entry.ShapeIndex) is SphereShape3D or CapsuleShape3D,
        !entry.Body.IsShapeOwnerDisabled(entry.Owner), AlsSimulationFilter.WorldStatic);
    private static void ValidateShape(Shape3D shape)
    {
        if (shape is not (SphereShape3D or BoxShape3D or CapsuleShape3D or ConvexPolygonShape3D))
            throw new NotSupportedException("Environment queries need an explicitly supported convex shape.");
        _ = AlsGodotContactQuery.Bounds(shape);
    }
    internal static AlsPrecisePose FromWorld(Transform3D transform)
    {
        if (!transform.IsFinite() || !transform.Basis.Scale.IsEqualApprox(Vector3.One) || Math.Abs(transform.Basis.Determinant() - 1) > 1e-5f)
            throw new ArgumentException("Scene collision transforms must be rigid; bake scale into supported shape geometry.");
        var q = transform.Basis.Orthonormalized().GetRotationQuaternion();
        return new(ToNative(transform.Origin), new(q.Z, -q.X, -q.Y, q.W), AlsDoubleVector.One);
    }
    private static AlsDoubleVector ToNative(Vector3 v) => AlsFootIkCoordinates.ToNative(new NVector(v.X, v.Y, v.Z));
    private static void BindQuery(AlsGodotContactQuery query,AlsContactShapeHandle handle,Shape3D shape)
        =>query.Bind(handle,shape,nativeHalf:shape is BoxShape3D box
            ?new AlsDoubleVector((double)box.Size.Z*50,(double)box.Size.X*50,(double)box.Size.Y*50):null);
    private static NVector AngularToNative(Vector3 v) => new(v.Z, -v.X, -v.Y);
    private static bool Live(StaticBody3D body) => GodotObject.IsInstanceValid(body) && body.IsInsideTree();
    private static void Main() { if (!GodotThread.IsMainThread()) throw new InvalidOperationException("Scene contact capture requires Main."); }
    private void Check() { Main(); ObjectDisposedException.ThrowIf(_disposed, this); }
    public void Dispose()
    {
        Main(); if (_disposed) return;
        if (_registry?.IsLocked == true) throw new InvalidOperationException("Cannot dispose scene contacts during a solve.");
        _disposed = true;
        foreach (var entry in _entries)
        {
            if (GodotObject.IsInstanceValid(entry.Shape) && entry.Changed is not null) entry.Shape.Changed -= entry.Changed;
            if (entry.Present && _registry is not null) _registry.Remove(entry.Handle);
            entry.Present = false;
        }
    }
}
