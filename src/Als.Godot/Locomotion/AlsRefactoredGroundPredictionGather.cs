using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Locomotion;

// Each bit denotes bodies that BOTH block Visibility and belong to this UE
// object category. A raw motor mask cannot express the original query contract.
internal readonly record struct AlsGroundPredictionCollisionLayers(uint WorldStatic, uint WorldDynamic, uint Destructible)
{
    public uint ValidateAndCombine()
    {
        if (WorldStatic == 0 || WorldDynamic == 0 || Destructible == 0 ||
            (WorldStatic & WorldDynamic) != 0 || (WorldStatic & Destructible) != 0 || (WorldDynamic & Destructible) != 0)
            throw new ArgumentException("Prediction requires disjoint explicit Visibility-blocking object category layers.");
        return WorldStatic | WorldDynamic | Destructible;
    }
}

// Main-physics bridge for the independent Refactored request. The cast node is
// query-only; its owned capsule does not change the character's collision shape.
internal sealed class AlsRefactoredGroundPredictionGather : IDisposable
{
    private readonly Node3D _owner;
    private readonly CapsuleShape3D _shape;
    private readonly ShapeCast3D _cast;
    private bool _disposed;
    public int LastQueryCount { get; private set; }

    public AlsRefactoredGroundPredictionGather(Node3D owner, AlsGroundPredictionCollisionLayers layers,
        ReadOnlySpan<Rid> ignoredActorBodies)
    {
        RequireMain(); ArgumentNullException.ThrowIfNull(owner);
        var mask = layers.ValidateAndCombine();
        _owner = owner; _shape = new CapsuleShape3D();
        _cast = new ShapeCast3D { Name = "RefactoredGroundPrediction", Shape = _shape, Enabled = false,
            ExcludeParent = false, TopLevel = true, CollisionMask = mask, CollideWithAreas = false,
            CollideWithBodies = true, Margin = 0, MaxResults = 1 };
        owner.AddChild(_cast);
        foreach (var body in ignoredActorBodies) _cast.AddExceptionRid(body);
    }

    public AlsGroundPredictionObservation Gather(in AlsGroundPredictionQuery query)
    {
        RequireMain(); ObjectDisposedException.ThrowIf(_disposed, this); LastQueryCount = 0;
        if (!Engine.IsInPhysicsFrame()) throw new InvalidOperationException("Prediction gathering requires the main physics phase.");
        if (query.Identity.SlotGeneration == 0 || query.Serial == 0) throw new ArgumentException("Unissued prediction query.");
        if (!query.Enabled) return new(query, false, false, 0, default);
        var start = Position(query.Start); var motion = Position(query.End - query.Start);
        var radius = query.Radius * .01f; var height = query.HalfHeight * .02f;
        if (!query.Start.IsFinite || !query.End.IsFinite || !start.IsFinite() || !motion.IsFinite() || motion.IsZeroApprox() ||
            !float.IsFinite(radius) || radius <= 0 || !float.IsFinite(height) || height < 2 * radius)
            throw new ArgumentException("Unrepresentable prediction capsule or motion.");
        if (!_owner.IsInsideTree()) throw new InvalidOperationException("Prediction requires a live physics world.");
        // Raise height first when increasing radius: CapsuleShape clamps invalid
        // dimensions eagerly. Finish with the exact world-space dimensions.
        _shape.Height = MathF.Max(height, _shape.Radius * 2);
        _shape.Radius = radius; _shape.Height = height;
        _cast.GlobalTransform = new(Basis.Identity, start);
        _cast.TargetPosition = Vector3.Zero;
        Update();
        if (_cast.IsColliding())
        {
            var normal = _cast.GetCollisionNormal(0);
            var support = radius + (height * .5f - radius) * MathF.Abs(normal.Y);
            var separation = (start - _cast.GetCollisionPoint(0)).Dot(normal) - support;
            // Mere contact is also reported by zero-motion ShapeCast. The
            // Refactored consumer accepts both; preserve their distinction.
            return Hit(query, normal, 0, separation < -.000001f);
        }

        _cast.TargetPosition = motion; Update();
        if (!_cast.IsColliding()) return new(query, false, false, 1, default);
        var low = _cast.GetClosestCollisionSafeFraction(); var high = _cast.GetClosestCollisionUnsafeFraction();
        var hitNormal = _cast.GetCollisionNormal(0); var hitPoint = _cast.GetCollisionPoint(0); var distance = motion.Length();
        if (!float.IsFinite(low) || !float.IsFinite(high) || low < 0 || high > 1 || low > high)
            throw new InvalidOperationException("Invalid Godot sweep bracket.");
        // Godot gives a safe/unsafe interval. Refine the first obstruction to
        // 0.1 mm of travel without substituting a ray or filtering steep hits.
        _cast.TargetPosition = Vector3.Zero;
        for (var iteration = 0; iteration < 12 && (high - low) * distance > .0001f; iteration++)
        {
            var middle = low + (high - low) * .5f;
            _cast.GlobalPosition = start + motion * middle; Update();
            if (_cast.IsColliding()) { high = middle; hitNormal = _cast.GetCollisionNormal(0); hitPoint = _cast.GetCollisionPoint(0); }
            else low = middle;
        }
        // The backend's contact tolerance can put even its unsafe fraction
        // slightly before geometric contact. Recover the capsule support-plane
        // time from the first hit's actual witness, rather than treating that
        // tolerance as extra capsule radius. This is exact for planar contacts;
        // curved contacts retain the backend's witness-normal accuracy.
        var approach = motion.Dot(hitNormal);
        if (approach < -1e-6f)
        {
            var support = radius + (height * .5f - radius) * MathF.Abs(hitNormal.Y);
            var planeTime = (support - (start - hitPoint).Dot(hitNormal)) / approach;
            if (float.IsFinite(planeTime) && planeTime >= 0 && planeTime <= 1) high = planeTime;
        }
        return Hit(query, hitNormal, high, false);
    }

    private static AlsGroundPredictionObservation Hit(in AlsGroundPredictionQuery query, Vector3 normal, float time, bool penetrating)
    {
        if (!normal.IsFinite() || normal.LengthSquared() < .5f)
            throw new InvalidOperationException("Prediction physics returned an unusable contact normal.");
        return new(query, true, penetrating, time, new(-normal.Z, normal.X, normal.Y));
    }
    private void Update() { _cast.ForceShapecastUpdate(); LastQueryCount++; }
    private static Vector3 Position(AlsDoubleVector native) => new((float)(native.Y * .01), (float)(native.Z * .01), (float)(-native.X * .01));
    private static void RequireMain()
    { if (!GodotThread.IsMainThread()) throw new InvalidOperationException("Prediction physics belongs to the main thread."); }
    public void Dispose()
    {
        RequireMain(); if (_disposed) return;
        _cast.Free(); _shape.Dispose(); _disposed = true;
    }
}
