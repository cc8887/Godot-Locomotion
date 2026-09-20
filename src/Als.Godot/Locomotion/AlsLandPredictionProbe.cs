using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using NVector3 = System.Numerics.Vector3;

namespace GodotAls.Locomotion;

// Query-only child of the motor. Disabled automatic processing prevents a second, stale cast.
internal sealed class AlsLandPredictionProbe
{
    private readonly CharacterBody3D _owner;
    private readonly CapsuleShape3D _shape;
    private readonly ShapeCast3D _cast;
    public int LastQueryCount { get; private set; }

    public AlsLandPredictionProbe(CharacterBody3D owner, CapsuleShape3D shape)
    {
        RequireMainThread(); _owner = owner; _shape = shape;
        _cast = new ShapeCast3D
        {
            Name = "AlsLandPrediction", Shape = shape, Enabled = false, ExcludeParent = true,
            CollideWithAreas = false, CollideWithBodies = true, Margin = 0, MaxResults = 1,
            TopLevel = true,
        };
        owner.AddChild(_cast);
        _cast.AddExceptionRid(owner.GetRid());
    }

    public AlsLandPredictionSample Gather(Vector3 capsuleCenter, NVector3 actualVelocity, bool inAir,
        in AlsLandPredictionSettings settings)
    {
        RequireMainThread(); LastQueryCount = 0;
        if (!inAir || !AlsLandPredictionModel.TryCreateMotion(actualVelocity, settings, out var motion)) return default;
        _cast.GlobalTransform = new Transform3D(Basis.Identity, capsuleCenter);
        _cast.CollisionMask = _owner.CollisionMask;
        // IsWalkable in UE rejects initial penetration. A separate zero-motion query is
        // necessary because a zero swept fraction alone also includes contact at the start.
        _cast.TargetPosition = Vector3.Zero;
        _cast.ForceShapecastUpdate(); LastQueryCount++;
        if (_cast.IsColliding())
        {
            // Zero-motion casts include mere touching. Recover the capsule's signed
            // separation from the reported contact plane before classifying penetration.
            var normal = _cast.GetCollisionNormal(0);
            var support = _shape.Radius + (_shape.Height * .5f - _shape.Radius) * MathF.Abs(normal.Y);
            var separation = (capsuleCenter - _cast.GetCollisionPoint(0)).Dot(normal) - support;
            var penetrating = normal.LengthSquared() < .5f || separation < -.000001f;
            return Sample(true, penetrating, motion) with { Time = 0, SafeTime = 0 };
        }
        _cast.TargetPosition = new Vector3(motion.X, motion.Y, motion.Z);
        _cast.ForceShapecastUpdate(); LastQueryCount++;
        var result = Sample(_cast.IsColliding(), false, motion);
        if (result.BlockingHit == 0) return result;
        // Godot Physics exposes a discrete safe/unsafe bracket (rather than an exact TOI).
        // Refine only hits, to <= 0.1 mm of travel, keeping the first obstruction's sample.
        var low = result.SafeTime; var high = result.Time; var distance = motion.Length();
        _cast.TargetPosition = Vector3.Zero;
        for (var iteration = 0; iteration < 12 && (high - low) * distance > .0001f; iteration++)
        {
            var middle = low + (high - low) * .5f;
            _cast.GlobalPosition = capsuleCenter + new Vector3(motion.X, motion.Y, motion.Z) * middle;
            _cast.ForceShapecastUpdate(); LastQueryCount++;
            if (_cast.IsColliding())
            {
                high = middle;
                result = Sample(true, false, motion) with { TraceStart = new(capsuleCenter.X, capsuleCenter.Y, capsuleCenter.Z) };
            }
            else low = middle;
        }
        return result with { Time = high, SafeTime = low };
    }

    private AlsLandPredictionSample Sample(bool blocking, bool penetrating, NVector3 motion)
    {
        var normal = blocking ? _cast.GetCollisionNormal(0) : Vector3.Zero;
        var point = blocking ? _cast.GetCollisionPoint(0) : Vector3.Zero;
        var collider = blocking ? _cast.GetCollider(0) : null;
        var vertical = normal.Dot(_owner.UpDirection);
        var walkable = blocking && !penetrating && vertical >= 1e-4f && vertical >= MathF.Cos(_owner.FloorMaxAngle);
        var start = _cast.GlobalPosition;
        return new(1, blocking ? (byte)1 : (byte)0, penetrating ? (byte)1 : (byte)0, walkable ? (byte)1 : (byte)0,
            penetrating ? 0 : _cast.GetClosestCollisionUnsafeFraction(), penetrating ? 0 : _cast.GetClosestCollisionSafeFraction(),
            new(point.X, point.Y, point.Z), new(normal.X, normal.Y, normal.Z), collider is null ? -1 : checked((long)collider.GetInstanceId()),
            new(start.X, start.Y, start.Z), motion, _shape.Radius, _shape.Height * .5f);
    }

    private static void RequireMainThread()
    { if (!GodotThread.IsMainThread()) throw new InvalidOperationException("Landing physics gathering belongs to the main thread."); }
}
