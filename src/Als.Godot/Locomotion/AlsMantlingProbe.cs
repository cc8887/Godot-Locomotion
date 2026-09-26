using Godot;
using GodotAls.Core.Actions;

namespace GodotAls.Locomotion;

internal readonly record struct AlsMantlingTarget(CollisionObject3D Body, Transform3D World,
    bool Relative, float Height, AlsMantlingType Type);

/// <summary>Main-only translation of AAlsCharacter::StartMantling sweeps.
/// Visibility blockers use the same explicit static/dynamic/destructible layers
/// as ground prediction. Character capsules cannot be stepped onto.</summary>
internal sealed class AlsMantlingProbe
{
    private readonly AlsCharacterMotor _owner;
    private readonly CapsuleShape3D _shape = new();
    private readonly ShapeCast3D _cast;
    internal string Rejection { get; private set; } = "NotRequested";
    internal AlsMantlingProbe(AlsCharacterMotor owner)
    {
        _owner = owner;
        _cast = new() { Name = "MantleProbe", Shape = _shape, Enabled = false, TopLevel = true,
            ExcludeParent = false, CollisionMask = 7, CollideWithAreas = false, CollideWithBodies = true,
            Margin = 0, MaxResults = 1 };
        owner.AddChild(_cast); _cast.AddExceptionRid(owner.GetRid());
    }

    internal bool Find(bool grounded, Vector3 input, Vector3 velocity, float yaw, float radius, float halfHeight,
        float scale, out AlsMantlingTarget target)
    {
        target = default; Rejection = "Direction";
        // Native units below are converted to metres exactly once.
        var min = .5f * scale; var max = (grounded ? 2.25f : 1.5f) * scale;
        var reach = (grounded ? .75f : .7f) * scale;
        var hasInput = input.LengthSquared() > 1e-8f;
        var hasVelocity = new Vector2(velocity.X, velocity.Z).LengthSquared() > .0001f;
        var traceYaw = hasVelocity ? MathF.Atan2(velocity.X, -velocity.Z) : hasInput ? MathF.Atan2(input.X, -input.Z) : -yaw;
        var maxAngle = Mathf.DegToRad(50);
        if (hasVelocity && hasInput)
            traceYaw += Math.Clamp(Mathf.Wrap(MathF.Atan2(input.X, -input.Z) - traceYaw, -MathF.PI, MathF.PI), -maxAngle, maxAngle);
        var delta = Mathf.Wrap(traceYaw + yaw, -MathF.PI, MathF.PI);
        if (MathF.Abs(delta) > Mathf.DegToRad(110)) return false;
        traceYaw = -yaw + Math.Clamp(delta, -maxAngle, maxAngle);
        var direction = new Vector3(MathF.Sin(traceYaw), 0, -MathF.Cos(traceYaw));
        var bottom = _owner.GlobalPosition - Vector3.Up * halfHeight;
        var r = radius - .01f; var heightDelta = max - min;
        if (r <= 0 || scale <= 0) throw new InvalidOperationException("Invalid mantle capsule scale.");
        var start = bottom - direction * radius + Vector3.Up * ((min + max) * .5f - .024f);
        var end = start + direction * (radius + reach + .01f * scale);
        Rejection = "Forward";
        if (!Sweep(start, end, r, heightDelta * .5f, out var forward) || forward.InitialOverlap ||
            forward.Body is not CollisionObject3D body || forward.Normal.Y >= MathF.Cos(_owner.FloorMaxAngle)) return false;
        Rejection = "Target";
        // ACharacter's capsule defaults to ECB_No, even when it blocks the
        // trace channel. Do not allow idle demo NPCs to become ledges.
        if (body is CharacterBody3D) return false;
        if (body.HasMeta("als_mantle_disabled") && body.GetMeta("als_mantle_disabled").AsBool()) return false;
        if (body is RigidBody3D rigid && rigid.LinearVelocity.LengthSquared() > .01f ||
            body is CharacterBody3D character && character.Velocity.LengthSquared() > .01f ||
            body is StaticBody3D platform && platform.ConstantLinearVelocity.LengthSquared() > .01f) return false;
        var inward = new Vector3(-forward.Normal.X, 0, -forward.Normal.Z).Normalized();
        if (inward.IsZeroApprox()) return false;
        var downStart = forward.Point + inward * (.15f * scale);
        downStart.Y = bottom.Y + heightDelta + 2.5f * r + .019f;
        var downEnd = downStart; downEnd.Y = bottom.Y + min + r - .024f;
        Rejection = "Top";
        if (!Sweep(downStart, downEnd, r, r, out var top) || top.InitialOverlap) return false;
        var slope = MathF.Cos(Mathf.DegToRad(35));
        if (top.Normal.Y < slope || top.Normal.Y < MathF.Cos(_owner.FloorMaxAngle) ||
            (top.Center - top.Point).Normalized().Y < slope) return false;
        var targetBottom = new Vector3(top.Center.X, top.Point.Y + .019f, top.Center.Z);
        var center = targetBottom + Vector3.Up * halfHeight;
        Rejection = "DestinationClearance";
        if (Overlap(center, radius, halfHeight)) return false;
        var clearance = forward.Point - inward * (.55f * scale);
        clearance.Y = (top.Center.Y + downEnd.Y) * .5f;
        Rejection = "ApproachClearance";
        if (Overlap(clearance, r, (top.Center.Y - downEnd.Y) * .5f + r)) return false;
        var height = (targetBottom.Y - bottom.Y) * 100 / scale;
        var targetYaw = MathF.Atan2(-inward.X, -inward.Z);
        target = new(body, new(new Basis(Vector3.Up, targetYaw), center),
            body is AnimatableBody3D or RigidBody3D or CharacterBody3D, height,
            !grounded ? AlsMantlingType.InAir : height > 125 ? AlsMantlingType.High : AlsMantlingType.Low);
        Rejection = "Accepted"; return true;
    }

    private readonly record struct Hit(GodotObject Body, Vector3 Point, Vector3 Normal, Vector3 Center, bool InitialOverlap);
    private void Shape(float radius, float halfHeight)
    {
        // UE FCollisionShape capsules with half-height below radius degenerate
        // to spheres. Godot eagerly clamps dimensions, so set in this order.
        var height = MathF.Max(halfHeight, radius) * 2;
        _shape.Height = MathF.Max(height, _shape.Radius * 2); _shape.Radius = radius; _shape.Height = height;
    }
    private bool Overlap(Vector3 center, float radius, float halfHeight)
    {
        Shape(radius, halfHeight); _cast.GlobalTransform = new(Basis.Identity, center);
        _cast.TargetPosition = Vector3.Zero; _cast.ForceShapecastUpdate(); return _cast.IsColliding();
    }
    private bool Sweep(Vector3 start, Vector3 end, float radius, float halfHeight, out Hit hit)
    {
        hit = default;
        if (Overlap(start, radius, halfHeight))
        { hit = new(_cast.GetCollider(0), _cast.GetCollisionPoint(0), _cast.GetCollisionNormal(0), start, true); return true; }
        var motion = end - start; _cast.TargetPosition = motion; _cast.ForceShapecastUpdate();
        if (!_cast.IsColliding()) return false;
        var body = _cast.GetCollider(0); var point = _cast.GetCollisionPoint(0); var normal = _cast.GetCollisionNormal(0);
        var fraction = _cast.GetClosestCollisionUnsafeFraction();
        var approach = motion.Dot(normal);
        if (approach < -1e-8f)
        {
            var support = radius + (MathF.Max(radius, halfHeight) - radius) * MathF.Abs(normal.Y);
            fraction = Math.Clamp((support - (start - point).Dot(normal)) / approach, 0, 1);
        }
        hit = new(body, point, normal, start + motion * fraction, false); return true;
    }
}
