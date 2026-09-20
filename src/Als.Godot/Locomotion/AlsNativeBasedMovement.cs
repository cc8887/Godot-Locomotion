using Godot;

namespace GodotAls.Locomotion;

// Main-thread-only lifecycle data. No scene objects cross the worker boundary.
internal readonly record struct AlsMovementBaseHistory(CollisionObject3D? Collider, Transform3D Transform,
    Vector3 LinearVelocity, Vector3 AngularVelocity);

public partial class AlsCharacterMotor
{
    private AlsMovementBaseHistory _movementBase;
    private CollisionObject3D? _sampledFloorCollider;
    private PhysicsTestMotionParameters3D? _baseMotionParameters;
    private PhysicsTestMotionResult3D? _baseMotionResult;
    private readonly Godot.Collections.Array<Rid> _baseExclusions = new();
    internal Vector3 WorldMovementVelocity { get; private set; }
    internal Vector3 BaseTransportDelta { get; private set; }
    internal bool BaseTransportBlocked { get; private set; }
    internal AlsMovementBaseHistory MovementBaseHistory => _movementBase;

    private void ConfigureBasedMovement()
    {
        if (_movementRuntime is null) return;
        // Native based movement owns transport and departure inheritance. Keep
        // Godot's ordinary walking/collision solver, without a second base move.
        PlatformFloorLayers = 0; PlatformWallLayers = 0;
        PlatformOnLeave = PlatformOnLeaveEnum.DoNothing;
        _baseMotionParameters = new() { Margin = SafeMargin, RecoveryAsCollision = false, MaxCollisions = MaximumFloorSupportCollisions };
        _baseMotionResult = new();
    }

    private void MoveWithNativeBase(bool grounded, bool transport)
    {
        BaseTransportDelta = default; BaseTransportBlocked = false;
        if (_movementRuntime is null) return;
        var body = _movementBase.Collider;
        if (_releasePlatformOnNextStep || !grounded || body is null || !GodotObject.IsInstanceValid(body) ||
            (body.CollisionLayer & CollisionMask) == 0)
        { _movementBase = default; return; }
        var current = BaseTransform(body);
        var previous = _movementBase.Transform;
        ReadBaseVelocity(body, out var linear, out var angular);
        _movementBase = new(body, current, linear, angular);
        // ControlledCharacterMove checks jump input before PerformMovement's
        // based move. A successful jump inherits velocity and detaches first.
        if (!transport || current == previous) return;
        // UE UpdateBasedMovement uses rotation/translation matrices, not base
        // scale or angular-velocity Euler integration. Track the capsule bottom.
        var bottom = GlobalPosition - UpDirection * (_capsuleShape!.Height * .5f);
        var target = current * (previous.AffineInverse() * bottom) + UpDirection * (_capsuleShape.Height * .5f);
        var motion = target - GlobalPosition;
        if (motion == Vector3.Zero) return;
        RequireFiniteVector(motion, "Native base motion");
        var before = GlobalPosition;
        _baseExclusions.Clear(); _baseExclusions.Add(body.GetRid());
        var parameters = _baseMotionParameters!;
        parameters.From = GlobalTransform; parameters.Motion = motion; parameters.ExcludeBodies = _baseExclusions;
        // Like MOVECOMP_IgnoreBases, ignore only the current base during its
        // transport sweep. Ordinary movement still collides with that support.
        BaseTransportBlocked = PhysicsServer3D.BodyTestMotion(GetRid(), parameters, _baseMotionResult!);
        GlobalPosition += BaseTransportBlocked ? _baseMotionResult!.GetTravel() : motion;
        BaseTransportDelta = GlobalPosition - before;
    }

    private Vector3 NativeBaseDepartureVelocity()
    {
        var body = _movementBase.Collider;
        if (_movementRuntime is null || body is null || !GodotObject.IsInstanceValid(body)) return default;
        var bottom = GlobalPosition - UpDirection * (_capsuleShape!.Height * .5f);
        // The original V4 CDO enables all XYZ and angular inheritance flags.
        return _movementBase.LinearVelocity + _movementBase.AngularVelocity.Cross(bottom - _movementBase.Transform.Origin);
    }

    private void SaveNativeBase(bool grounded)
    {
        if (_movementRuntime is null) return;
        var body = _sampledFloorCollider;
        if (!grounded || _releasePlatformOnNextStep || body is null || !GodotObject.IsInstanceValid(body))
        { _movementBase = default; return; }
        ReadBaseVelocity(body, out var linear, out var angular);
        _movementBase = new(body, BaseTransform(body), linear, angular);
    }

    private static Transform3D BaseTransform(CollisionObject3D body)
    {
        var transform = body.GlobalTransform;
        transform.Basis = transform.Basis.Orthonormalized();
        if (!IsFinite(transform)) throw new InvalidOperationException("Nonfinite movement base transform.");
        return transform;
    }

    private static void ReadBaseVelocity(CollisionObject3D body, out Vector3 linear, out Vector3 angular)
    {
        var state = PhysicsServer3D.BodyGetDirectState(body.GetRid());
        linear = state?.LinearVelocity ?? Vector3.Zero;
        angular = state?.AngularVelocity ?? Vector3.Zero;
        if (body is StaticBody3D declared)
        {
            // Explicit kinematic/conveyor velocities take precedence when set.
            if (declared.ConstantLinearVelocity != Vector3.Zero) linear = declared.ConstantLinearVelocity;
            if (declared.ConstantAngularVelocity != Vector3.Zero) angular = declared.ConstantAngularVelocity;
        }
        if (!IsFinite(linear) || !IsFinite(angular)) throw new InvalidOperationException("Nonfinite movement base velocity.");
    }
}
