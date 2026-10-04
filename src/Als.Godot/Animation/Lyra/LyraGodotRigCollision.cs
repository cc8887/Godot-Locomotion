using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraRigSceneSweep(LyraRigSweepRequest Request, Vector3 WorldStart,
    Vector3 WorldEnd, float WorldRadius, LyraRigSweepHit Hit, ulong Collider, int Shape, bool InitialOverlap)
{ public bool GeometryNormal { get; init; } }

// World-dependent operations stay in the main physics phase. The returned
// execution frame carries a frozen component transform, world and actor RIDs;
// it cannot be used after another capture, physics tick or component move.
internal sealed class LyraGodotRigCollision : IDisposable
{
    private readonly Node3D _component, _actor;
    private readonly long _epoch;
    private readonly int _traceChannel;
    private readonly SphereShape3D _sphere;
    private readonly ShapeCast3D _cast;
    private long _serial, _lastFrame;
    private bool _disposed;
    public int QueryCount { get; private set; }
    public LyraRigSceneSweep LastSweep { get; private set; }
    public LyraGodotRigCollision(Node3D component, Node3D actor, long epoch, int traceChannel, uint traversableMask)
    {
        Main(); ArgumentNullException.ThrowIfNull(component); ArgumentNullException.ThrowIfNull(actor);
        if (epoch <= 0 || traceChannel < 0 || traversableMask == 0) throw new ArgumentException("Explicit Rig epoch/channel/layer mapping is required.");
        if (!component.IsInsideTree() || !actor.IsInsideTree() || component != actor && !actor.IsAncestorOf(component))
            throw new ArgumentException("Rig component must belong to its live actor.");
        _component = component; _actor = actor; _epoch = epoch; _traceChannel = traceChannel;
        _sphere = new() { Margin = 0 };
        _cast = new() { Name = "LyraFootPlantQuery", Shape = _sphere, Enabled = false, TopLevel = true,
            ExcludeParent = false, CollisionMask = traversableMask, CollideWithBodies = true, CollideWithAreas = false,
            Margin = 0, MaxResults = 1 };
        actor.AddChild(_cast);
    }
    internal sealed class Frame : ILyraFootPlantRigCollision
    {
        private readonly LyraGodotRigCollision _owner;
        internal readonly long Serial;
        internal readonly ulong Tick;
        internal readonly Rid Space;
        internal readonly Transform3D Transform;
        internal readonly Rid[] Ignored;
        internal bool BelongsTo(LyraGodotRigCollision owner)=>ReferenceEquals(_owner,owner);
        public AlsPrecisePose Component { get; }
        internal Frame(LyraGodotRigCollision owner, long serial, ulong tick, Rid space, Transform3D transform, Rid[] ignored)
        { _owner = owner; Serial = serial; Tick = tick; Space = space; Transform = transform; Ignored = ignored; Component = NativeTransform(transform); }
        public LyraRigSweepHit Sweep(in LyraRigSweepRequest request) => _owner.Sweep(this, request);
    }
    public Frame Capture(long epoch, long frame)
    {
        Check(); if (epoch != _epoch || frame <= 0 || frame < _lastFrame) throw new InvalidOperationException("Foreign or old Rig physics frame.");
        var transform = _component.GlobalTransform; _ = NativeTransform(transform);
        var ignored = Bodies(_actor); _cast.ClearExceptions(); foreach (var rid in ignored) _cast.AddExceptionRid(rid);
        _lastFrame = frame;
        return new(this, checked(++_serial), Engine.GetPhysicsFrames(), _component.GetWorld3D().Space, transform, ignored);
    }
    public void ValidateFrame(Frame frame)
    {
        Check();
        if (!frame.BelongsTo(this) || frame.Serial != _serial || frame.Tick != Engine.GetPhysicsFrames() || frame.Space != _component.GetWorld3D().Space ||
            frame.Transform != _component.GlobalTransform || !frame.Ignored.SequenceEqual(Bodies(_actor)))
            throw new InvalidOperationException("Rig collision execution frame expired or changed.");
    }
    private LyraRigSweepHit Sweep(Frame frame, in LyraRigSweepRequest request)
    {
        ValidateFrame(frame);
        if (request.TraceChannel != _traceChannel || !request.Start.IsFinite || !request.End.IsFinite ||
            !float.IsFinite(request.Radius) || request.Radius <= 0)
            throw new ArgumentException("Invalid or unmapped Rig sphere trace.");
        var component = frame.Component;
        var start = Position((component.Scale * request.Start).Rotate(component.Rotation) + component.Position);
        var end = Position((component.Scale * request.End).Rotate(component.Rotation) + component.Position);
        // Native sphere Radius is already world centimeters, independent of
        // the Rig component scale. The query basis must remain identity.
        float radius = request.Radius * .01f;
        if (!start.IsFinite() || !end.IsFinite() || !float.IsFinite(radius) || radius <= 0)
            throw new ArgumentException("Rig trace exceeds Godot scene precision.");
        _sphere.Radius = radius; _cast.GlobalTransform = new(Basis.Identity, start); _cast.TargetPosition = Vector3.Zero;
        _cast.ForceShapecastUpdate(); QueryCount++;
        bool overlap = _cast.IsColliding();
        if (!overlap && start != end)
        { _cast.TargetPosition = end - start; _cast.ForceShapecastUpdate(); QueryCount++; }
        var hit = new LyraRigSweepHit(false, default, new(0, 0, 1)); ulong collider = 0; int shape = -1; bool geometryNormal = false;
        if (_cast.IsColliding())
        {
            var point = _cast.GetCollisionPoint(0); var normal = _cast.GetCollisionNormal(0);
            if (!point.IsFinite() || !normal.IsFinite() || Math.Abs(normal.LengthSquared() - 1) > 1e-4f)
                throw new InvalidOperationException("Invalid Rig physics contact.");
            var body = _cast.GetCollider(0) as CollisionObject3D ?? throw new InvalidOperationException("Rig query collider expired.");
            shape = _cast.GetColliderShape(0); collider = body.GetInstanceId();
            if (!overlap) normal = OpposingBoxNormal(body, shape, end - start, normal, out geometryNormal);
            var inverse = component.Rotation.Conjugate();
            var reciprocal = new AlsDoubleVector(1 / component.Scale.X, 1 / component.Scale.Y, 1 / component.Scale.Z);
            var vmPoint = (NativePosition(point) - component.Position).Rotate(inverse) * reciprocal;
            var vmNormal = NativeVector(normal).Rotate(inverse) * reciprocal;
            hit = new(true, vmPoint, vmNormal);
            // Preserve inverse-scale normal magnitude; the original VM owns
            // any normalization and slope classification after the boundary.
        }
        LastSweep = new(request, start, end, radius, hit, collider, shape, overlap) { GeometryNormal = geometryNormal }; return hit;
    }
    internal static Vector3 OpposingBoxNormal(CollisionObject3D body, int shape, Vector3 direction, Vector3 original, out bool resolved)
    {
        resolved = false; var rid = body.GetRid();
        if (body is not PhysicsBody3D || shape < 0 || PhysicsServer3D.ShapeGetType(PhysicsServer3D.BodyGetShape(rid, shape)) != PhysicsServer3D.ShapeType.Box)
            return original;
        // Installed UE CollisionConversions::FindGeomOpposingNormal invokes
        // AABB::FindGeometryOpposingNormal for a sphere hitting a Box. Jolt's
        // RestInfo supplies the penetration axis instead. Resolve the same
        // opposing face from actual server geometry, not scene-node guesses.
        var world = PhysicsServer3D.BodyGetState(rid, PhysicsServer3D.BodyState.Transform).AsTransform3D() * PhysicsServer3D.BodyGetShapeTransform(rid, shape);
        var best = original; double bestDot = double.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            // Native Box X/Y/Z map to Godot Z/X/Y. Keep native traversal
            // order for equal opposing dots at a corner or edge.
            var face = world.Basis[axis == 0 ? 2 : axis == 1 ? 0 : 1].Normalized(); double n = original.Dot(face);
            if (n <= 1e-4f && n >= -1e-4f) continue;
            if (n < 0) face = -face;
            double dot = direction.Dot(face);
            if (dot < bestDot) { bestDot = dot; best = face; }
        }
        resolved = bestDot != double.MaxValue; return best;
    }
    private static Rid[] Bodies(Node root)
    {
        var result = new List<Rid>();
        void Visit(Node node)
        {
            if (node is CollisionObject3D body) result.Add(body.GetRid());
            foreach (var child in node.GetChildren()) Visit(child);
        }
        Visit(root); return result.ToArray();
    }
    private void Check()
    {
        Main(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Engine.IsInPhysicsFrame()) throw new InvalidOperationException("Rig queries require the main physics phase.");
        if (!GodotObject.IsInstanceValid(_component) || !GodotObject.IsInstanceValid(_actor) || !GodotObject.IsInstanceValid(_cast) ||
            !_component.IsInsideTree() || !_actor.IsInsideTree() || !_cast.IsInsideTree() ||
            _component.IsQueuedForDeletion() || _actor.IsQueuedForDeletion() || _component != _actor && !_actor.IsAncestorOf(_component))
            throw new InvalidOperationException("Rig physics owner expired.");
    }
    internal static Vector3 Position(AlsDoubleVector value) => new((float)(value.Y * .01), (float)(value.Z * .01), (float)(-value.X * .01));
    internal static AlsDoubleVector NativePosition(Vector3 value) => new(-(double)value.Z * 100, (double)value.X * 100, (double)value.Y * 100);
    internal static AlsDoubleVector NativeVector(Vector3 value) => new(-value.Z, value.X, value.Y);
    internal static AlsPrecisePose NativeTransform(Transform3D transform)
    {
        if (!transform.IsFinite()) throw new ArgumentException("Nonfinite Rig component transform.");
        var scale = transform.Basis.Scale;
        if (scale.X <= 1e-8f || scale.Y <= 1e-8f || scale.Z <= 1e-8f || transform.Basis.Determinant() <= 0)
            throw new ArgumentException("Rig component requires a positive invertible scale.");
        var rotation = transform.Basis.ScaledLocal(new(1 / scale.X, 1 / scale.Y, 1 / scale.Z));
        if (Math.Abs(rotation.X.Dot(rotation.Y)) > 1e-5f || Math.Abs(rotation.Y.Dot(rotation.Z)) > 1e-5f || Math.Abs(rotation.Z.Dot(rotation.X)) > 1e-5f)
            throw new ArgumentException("Rig component contains shear.");
        var q = rotation.GetRotationQuaternion();
        return new(NativePosition(transform.Origin), new AlsQuaternion(q.Z, -q.X, -q.Y, q.W).Normalized(), new(scale.Z, scale.X, scale.Y));
    }
    private static void Main()
    { if (!GodotThread.IsMainThread()) throw new InvalidOperationException("Rig physics belongs to the main thread."); }
    public void Dispose()
    { Main(); if (_disposed) return; _disposed = true; if (GodotObject.IsInstanceValid(_cast)) _cast.Free(); _sphere.Dispose(); }
}
