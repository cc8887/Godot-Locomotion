using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using NumericsQuaternion = System.Numerics.Quaternion;
using NumericsVector3 = System.Numerics.Vector3;

namespace GodotAls.Locomotion;

public partial class AlsCharacterMotor : CharacterBody3D
{
    private const float ClearanceMargin = 0.002f;
    private static readonly StringName ColliderKey = "collider";
    private static readonly StringName NormalKey = "normal";
    private static readonly StringName PositionKey = "position";

    private IAlsLocomotionCommandSource? _source;
    private CollisionShape3D? _collisionNode;
    private CapsuleShape3D? _capsuleShape;
    private ShapeCast3D? _standClearance;
    private KinematicCollision3D? _initialFloorProbe;
    private readonly PhysicsRayQueryParameters3D[] _footQueries =
        new PhysicsRayQueryParameters3D[AlsP4FootProbeExchange.FootCount];
    private AlsP4FootProbeExchange _footProbeExchange = null!;
    private AlsP4FootGatherSettings _footGatherSettings;
    private AlsP3RuntimeContext? _runtimeContext;
    private AlsMotorSettings _settings;
    private AlsStance _actualStance = AlsStance.Standing;
    private NumericsVector3 _previousActualVelocity;
    private long _lastFrameId = -1;
    private Transform3D _previousGatherTransform;
    private bool _hasPreviousGatherTransform;
    private bool _configured;

    internal long LastFootGatherManagedAllocations { get; private set; }

    internal AlsFrameIdentity LastFootGatherRequestIdentity { get; private set; }

    internal Vector3 LastLeftFootQueryWorldOrigin { get; private set; }

    internal Vector3 LastRightFootQueryWorldOrigin { get; private set; }

    internal bool LastFootGatherConsumed { get; private set; }

    public AlsCharacterMotor()
    {
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = 0;
    }

    public void Configure(in AlsMotorSettings settings, IAlsLocomotionCommandSource source)
    {
        Configure(
            settings,
            source,
            new AlsP4FootProbeExchange(),
            AlsP4FootGatherSettings.CreateReference(),
            runtimeContext: null);
    }

    internal void Configure(
        in AlsMotorSettings settings,
        IAlsLocomotionCommandSource source,
        AlsP4FootProbeExchange footProbeExchange,
        in AlsP4FootGatherSettings footGatherSettings,
        AlsP3RuntimeContext? runtimeContext)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(footProbeExchange);
        settings.Validate();
        if (!footGatherSettings.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(footGatherSettings));
        }
        EnsureMainThread();
        EnsureLiveInTree();
        if (_configured)
        {
            throw new InvalidOperationException("The ALS character motor is already configured.");
        }

        var capsuleShape = new CapsuleShape3D
        {
            Radius = settings.CapsuleRadius,
            Height = settings.StandingHeight,
        };
        var clearanceShape = new CapsuleShape3D
        {
            Radius = settings.CapsuleRadius,
            Height = settings.StandingHeight,
        };

        var collisionNode = new CollisionShape3D
        {
            Name = "AlsCapsuleCollision",
            Shape = capsuleShape,
        };
        var standClearance = new ShapeCast3D
        {
            Name = "AlsStandClearance",
            Shape = clearanceShape,
            TargetPosition = Vector3.Zero,
            CollisionMask = settings.CollisionMask,
            CollideWithAreas = false,
            CollideWithBodies = true,
            ExcludeParent = true,
            Enabled = true,
            Margin = 0f,
        };
        AddChild(collisionNode);
        AddChild(standClearance);
        _collisionNode = collisionNode;
        _standClearance = standClearance;
        _initialFloorProbe = new KinematicCollision3D();
        var excludedBodies = new Godot.Collections.Array<Rid> { GetRid() };
        for (var index = 0; index < _footQueries.Length; index++)
        {
            _footQueries[index] = PhysicsRayQueryParameters3D.Create(
                Vector3.Zero,
                Vector3.Zero,
                settings.CollisionMask,
                excludedBodies);
            _footQueries[index].CollideWithAreas = false;
            _footQueries[index].CollideWithBodies = true;
            _footQueries[index].HitFromInside = false;
        }

        _capsuleShape = capsuleShape;
        _footProbeExchange = footProbeExchange;
        _footGatherSettings = footGatherSettings;
        _runtimeContext = runtimeContext;
        _settings = settings;
        _source = source;
        _actualStance = AlsStance.Standing;
        _previousActualVelocity = NumericsVector3.Zero;
        _lastFrameId = -1;
        _previousGatherTransform = GlobalTransform;
        _hasPreviousGatherTransform = false;
        Velocity = Vector3.Zero;
        CollisionMask = settings.CollisionMask;
        MotionMode = MotionModeEnum.Grounded;
        UpDirection = Vector3.Up;
        FloorSnapLength = 0.1f;
        FloorStopOnSlope = true;
        _configured = true;
    }

    public AlsFrameInput Step(
        long frameId,
        int characterId,
        int generation,
        float deltaTime,
        byte hasTargetYaw = 0,
        float targetYaw = 0f)
    {
        ValidateStep(frameId, characterId, generation, deltaTime, hasTargetYaw, targetYaw);
        var source = _source!;
        var command = source.GetCommand(frameId);
        if (command.JumpPressed > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                "JumpPressed must be zero or one.");
        }

        var currentStanceCommand = AlsLocomotionCommandResolver.Resolve(command, _actualStance);
        var requestedStanceCommand = AlsLocomotionCommandResolver.Resolve(
            command,
            currentStanceCommand.RequestedStance);
        var currentVelocity = Velocity;
        RequireFiniteVector(currentVelocity, nameof(Velocity));
        if (hasTargetYaw == 1)
        {
            GlobalBasis = new Basis(Vector3.Up, targetYaw);
        }

        var movementYaw = GetCharacterYaw();
        if (!float.IsFinite(movementYaw))
        {
            throw new ArgumentOutOfRangeException(nameof(GlobalTransform), "Character yaw must be finite.");
        }

        var currentDesiredSpeed = CalculateDesiredSpeed(currentStanceCommand, movementYaw, _actualStance);
        var requestedDesiredSpeed = CalculateDesiredSpeed(
            requestedStanceCommand,
            movementYaw,
            currentStanceCommand.RequestedStance);
        var currentDesiredVelocity = ToGodot(currentStanceCommand.WorldDirection) * currentDesiredSpeed;
        var requestedDesiredVelocity = ToGodot(requestedStanceCommand.WorldDirection) * requestedDesiredSpeed;
        RequireFiniteVector(currentDesiredVelocity, nameof(currentDesiredVelocity));
        RequireFiniteVector(requestedDesiredVelocity, nameof(requestedDesiredVelocity));

        var currentHorizontal = new Vector3(currentVelocity.X, 0f, currentVelocity.Z);
        var currentStanceHorizontalVelocity = IntegrateHorizontalVelocity(
            currentHorizontal,
            currentDesiredVelocity,
            _settings.MaxAcceleration,
            _settings.MaxBrakingDeceleration,
            deltaTime);
        var requestedStanceHorizontalVelocity = IntegrateHorizontalVelocity(
            currentHorizontal,
            requestedDesiredVelocity,
            _settings.MaxAcceleration,
            _settings.MaxBrakingDeceleration,
            deltaTime);

        var standingRequestBlocked = UpdateStance(currentStanceCommand.RequestedStance);
        var usedRequestedStance = _actualStance == currentStanceCommand.RequestedStance;
        var resolvedCommand = usedRequestedStance ? requestedStanceCommand : currentStanceCommand;
        var desiredSpeed = usedRequestedStance ? requestedDesiredSpeed : currentDesiredSpeed;
        var horizontalVelocity = usedRequestedStance
            ? requestedStanceHorizontalVelocity
            : currentStanceHorizontalVelocity;
        RequireFiniteVector(horizontalVelocity, nameof(horizontalVelocity));

        var verticalVelocity = currentVelocity.Y;
        byte jumpAccepted = 0;
        var groundedBeforeMove = IsOnFloor() || (_lastFrameId < 0 && ProbeInitialFloor());
        if (groundedBeforeMove)
        {
            if (resolvedCommand.JumpPressed == 1 && !standingRequestBlocked)
            {
                verticalVelocity = _settings.JumpSpeed;
                jumpAccepted = 1;
            }
            else if (verticalVelocity < 0f)
            {
                verticalVelocity = 0f;
            }
        }
        else
        {
            verticalVelocity -= _settings.Gravity * deltaTime;
        }

        var nextVelocity = new Vector3(horizontalVelocity.X, verticalVelocity, horizontalVelocity.Z);
        RequireFiniteVector(nextVelocity, nameof(nextVelocity));
        Velocity = nextVelocity;
        MoveAndSlide();

        var characterTransform = GlobalTransform;
        var characterYaw = GetCharacterYaw(characterTransform.Basis);
        if (!float.IsFinite(characterYaw))
        {
            throw new ArgumentOutOfRangeException(nameof(GlobalTransform), "Final character yaw must be finite.");
        }
        var identity = new AlsFrameIdentity(frameId, (uint)characterId, (uint)generation);
        var allocatedBeforeFootGather = GC.GetAllocatedBytesForCurrentThread();
        GatherFootHits(
            identity,
            characterTransform,
            out var leftFootHit,
            out var rightFootHit);
        var actualVelocity = ToNumerics(GetRealVelocity());
        var actualAcceleration = (actualVelocity - _previousActualVelocity) / deltaTime;
        var grounded = IsOnFloor();
        var floor = CreateFloorSample(grounded);
        LastFootGatherManagedAllocations =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeFootGather;
        if (_runtimeContext is not null)
        {
            Interlocked.Add(
                ref _runtimeContext.FootGatherManagedAllocations,
                LastFootGatherManagedAllocations);
        }
        var input = new AlsFrameInput(
            Identity: identity,
            DeltaTime: deltaTime,
            CharacterTransform: ToNumerics(characterTransform),
            ActualVelocity: actualVelocity,
            ActualAcceleration: actualAcceleration,
            InputDirection: resolvedCommand.WorldDirection,
            DesiredSpeed: desiredSpeed,
            ViewRotation: NumericsQuaternion.CreateFromYawPitchRoll(
                command.ViewYaw,
                command.ViewPitch,
                0f),
            AimRotation: NumericsQuaternion.CreateFromYawPitchRoll(
                command.AimYaw,
                command.AimPitch,
                0f),
            Floor: floor,
            LeftFootHit: leftFootHit,
            RightFootHit: rightFootHit,
            MantleProbe: new AlsMantleProbeResult(0, NumericsMatrix4x4.Identity, -1),
            RequestedGait: command.RequestedGait,
            Stance: _actualStance,
            RotationMode: resolvedCommand.RotationMode,
            RequestedAction: AlsLocomotionAction.None,
            CurrentDriveMode: AlsDriveMode.MotorDriven,
            RagdollState: AlsRagdollState.Inactive,
            AnimationQualityTier: AlsAnimationQualityTier.Tier0,
            Command: command,
            CharacterYaw: characterYaw,
            MaxAcceleration: _settings.MaxAcceleration,
            MaxBrakingDeceleration: _settings.MaxBrakingDeceleration,
            JumpAccepted: jumpAccepted);

        _previousActualVelocity = actualVelocity;
        _lastFrameId = frameId;
        return input;
    }

    private void GatherFootHits(
        in AlsFrameIdentity identity,
        in Transform3D characterTransform,
        out AlsFootHit left,
        out AlsFootHit right)
    {
        left = AlsFootHit.Invalid;
        right = AlsFootHit.Invalid;
        LastFootGatherConsumed = false;
        LastFootGatherRequestIdentity = default;
        LastLeftFootQueryWorldOrigin = default;
        LastRightFootQueryWorldOrigin = default;
        if (HasCharacterDiscontinuity(characterTransform))
        {
            _footProbeExchange.Clear();
            RememberGatherTransform(characterTransform);
            return;
        }
        RememberGatherTransform(characterTransform);

        if (!_footProbeExchange.TryReadForGather(identity, out var leftRequest, out var rightRequest))
        {
            return;
        }

        LastFootGatherConsumed = true;
        LastFootGatherRequestIdentity = leftRequest.Identity;
        LastLeftFootQueryWorldOrigin = characterTransform * ToGodot(
            leftRequest.CharacterLocalOrigin);
        LastRightFootQueryWorldOrigin = characterTransform * ToGodot(
            rightRequest.CharacterLocalOrigin);

        left = GatherFootHit(leftRequest, characterTransform, _footQueries[0]);
        right = GatherFootHit(rightRequest, characterTransform, _footQueries[1]);
        if (_runtimeContext is not null)
        {
            Interlocked.Add(ref _runtimeContext.FootGatherQueries, 2);
        }
    }

    private AlsFootHit GatherFootHit(
        in AlsP4FootProbeRequest request,
        in Transform3D characterTransform,
        PhysicsRayQueryParameters3D query)
    {
        var localOrigin = ToGodot(request.CharacterLocalOrigin);
        var worldOrigin = characterTransform * localOrigin;
        var up = characterTransform.Basis.Y.Normalized();
        if (!IsFinite(worldOrigin) || !IsFinite(up) || up.IsZeroApprox())
        {
            return AlsFootHit.Invalid;
        }

        query.From = worldOrigin + (up * _footGatherSettings.TraceUpMeters);
        query.To = worldOrigin - (up * _footGatherSettings.TraceDownMeters);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0 ||
            !hit.TryGetValue(PositionKey, out var positionVariant) ||
            !hit.TryGetValue(NormalKey, out var normalVariant) ||
            !hit.TryGetValue(ColliderKey, out var colliderVariant))
        {
            return AlsFootHit.Invalid;
        }

        var position = positionVariant.AsVector3();
        var normal = normalVariant.AsVector3();
        var collider = colliderVariant.AsGodotObject() as CollisionObject3D;
        if (!IsFinite(position) || !IsFinite(normal) || normal.IsZeroApprox() ||
            collider is null || !GodotObject.IsInstanceValid(collider))
        {
            return AlsFootHit.Invalid;
        }
        normal = normal.Normalized();
        if (!IsFinite(normal))
        {
            return AlsFootHit.Invalid;
        }

        var rawColliderId = collider.GetInstanceId();
        if (rawColliderId > long.MaxValue)
        {
            return AlsFootHit.Invalid;
        }
        var colliderId = (long)rawColliderId;
        var movingPlatform = IsMovingPlatform(collider);
        var platformId = movingPlatform ? CreatePlatformId(rawColliderId) : -1;
        var platformTransform = collider.GlobalTransform;
        var platformBasis = platformTransform.Basis.Orthonormalized();
        var platformRotation = platformBasis.GetRotationQuaternion().Normalized();
        if (!TryGetPointVelocity(collider, position, out var pointVelocity))
        {
            return AlsFootHit.Invalid;
        }
        if (!IsFinite(platformTransform.Origin) || !IsFinite(platformBasis) ||
            !IsFinite(platformRotation) || !IsFinite(pointVelocity))
        {
            return AlsFootHit.Invalid;
        }

        var walkable = IsFloorCollision(normal, up, FloorMaxAngle) ? (byte)1 : (byte)0;
        return new AlsFootHit(
            1,
            walkable,
            ToNumerics(position),
            ToNumerics(normal),
            platformId,
            movingPlatform ? ToNumerics(platformTransform.Origin) : NumericsVector3.Zero,
            movingPlatform
                ? new NumericsQuaternion(
                    platformRotation.X,
                    platformRotation.Y,
                    platformRotation.Z,
                    platformRotation.W)
                : NumericsQuaternion.Identity,
            colliderId,
            ToNumerics(pointVelocity));
    }

    private bool HasCharacterDiscontinuity(in Transform3D current)
    {
        if (!_hasPreviousGatherTransform)
        {
            return false;
        }

        var translation = current.Origin - _previousGatherTransform.Origin;
        var maximumDistance = _footGatherSettings.CharacterTeleportDistanceMeters;
        if (!IsFinite(translation) || translation.LengthSquared() > maximumDistance * maximumDistance)
        {
            return true;
        }

        var previousRotation = _previousGatherTransform.Basis.Orthonormalized()
            .GetRotationQuaternion().Normalized();
        var currentRotation = current.Basis.Orthonormalized()
            .GetRotationQuaternion().Normalized();
        return !IsFinite(previousRotation) || !IsFinite(currentRotation) ||
               previousRotation.AngleTo(currentRotation) >
               _footGatherSettings.CharacterTeleportAngleRadians;
    }

    private void RememberGatherTransform(in Transform3D current)
    {
        _previousGatherTransform = current;
        _hasPreviousGatherTransform = true;
    }

    private static bool TryGetPointVelocity(
        CollisionObject3D collider,
        in Vector3 worldPoint,
        out Vector3 pointVelocity)
    {
        pointVelocity = Vector3.Zero;
        switch (collider)
        {
            case RigidBody3D rigid:
            {
                var directState = PhysicsServer3D.BodyGetDirectState(rigid.GetRid());
                if (directState is null ||
                    !IsFinite(directState.Transform) ||
                    !IsFinite(directState.CenterOfMassLocal) ||
                    !IsFinite(directState.LinearVelocity) ||
                    !IsFinite(directState.AngularVelocity))
                {
                    return false;
                }
                var state = directState!;
                var worldCenterOfMass =
                    state.Transform * state.CenterOfMassLocal;
                if (!IsFinite(worldCenterOfMass))
                {
                    return false;
                }
                pointVelocity = state.LinearVelocity +
                    state.AngularVelocity.Cross(worldPoint - worldCenterOfMass);
                break;
            }
            case StaticBody3D staticBody:
                pointVelocity = staticBody.ConstantLinearVelocity +
                    staticBody.ConstantAngularVelocity.Cross(
                        worldPoint - staticBody.GlobalPosition);
                break;
            case CharacterBody3D character:
                pointVelocity = character.GetRealVelocity();
                break;
        }
        return IsFinite(pointVelocity);
    }

    private static bool IsMovingPlatform(CollisionObject3D collider) => collider switch
    {
        AnimatableBody3D => true,
        RigidBody3D => true,
        CharacterBody3D => true,
        StaticBody3D staticBody =>
            !staticBody.ConstantLinearVelocity.IsZeroApprox() ||
            !staticBody.ConstantAngularVelocity.IsZeroApprox(),
        _ => false,
    };

    internal static int CreatePlatformId(ulong colliderId)
    {
        var folded = colliderId ^ (colliderId >> 32);
        return (int)(folded & int.MaxValue);
    }

    private static bool IsFinite(in Basis value) =>
        IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);

    private static bool IsFinite(in Transform3D value) =>
        IsFinite(value.Basis) && IsFinite(value.Origin);

    private static bool IsFinite(in Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static bool IsFinite(in Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private void ValidateStep(
        long frameId,
        int characterId,
        int generation,
        float deltaTime,
        byte hasTargetYaw,
        float targetYaw)
    {
        EnsureMainThread();
        EnsureLiveInTree();
        if (!_configured)
        {
            throw new InvalidOperationException("The ALS character motor must be configured before stepping.");
        }

        if (!float.IsFinite(deltaTime) || deltaTime <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaTime), "Delta time must be positive and finite.");
        }
        if (hasTargetYaw > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(hasTargetYaw), "HasTargetYaw must be zero or one.");
        }
        if (hasTargetYaw == 1 && !float.IsFinite(targetYaw))
        {
            throw new ArgumentOutOfRangeException(nameof(targetYaw));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(frameId);
        ArgumentOutOfRangeException.ThrowIfNegative(characterId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generation);
        if (frameId <= _lastFrameId)
        {
            throw new InvalidOperationException(
                $"Frame IDs must increase strictly. Last frame was {_lastFrameId}, received {frameId}.");
        }
    }

    private bool UpdateStance(AlsStance requestedStance)
    {
        if (requestedStance == _actualStance)
        {
            return false;
        }

        if (requestedStance == AlsStance.Standing && !CanStand())
        {
            return true;
        }

        var previousHeight = _actualStance == AlsStance.Standing
            ? _settings.StandingHeight
            : _settings.CrouchingHeight;
        var nextHeight = requestedStance == AlsStance.Standing
            ? _settings.StandingHeight
            : _settings.CrouchingHeight;
        _capsuleShape!.Height = nextHeight;
        GlobalPosition += Vector3.Up * ((nextHeight - previousHeight) * 0.5f);
        _actualStance = requestedStance;
        return false;
    }

    private bool CanStand()
    {
        var heightDifference = _settings.StandingHeight - _settings.CrouchingHeight;
        _standClearance!.Position = Vector3.Up * ((heightDifference * 0.5f) + ClearanceMargin);
        _standClearance.ForceShapecastUpdate();
        return !_standClearance.IsColliding();
    }

    private float CalculateDesiredSpeed(
        in AlsResolvedLocomotionCommand command,
        float characterYaw,
        AlsStance actualStance)
    {
        if (command.InputAmount <= 0f)
        {
            return 0f;
        }

        var sin = MathF.Sin(characterYaw);
        var cos = MathF.Cos(characterYaw);
        var localRight = (command.WorldDirection.X * cos) - (command.WorldDirection.Z * sin);
        var localForward = (-command.WorldDirection.X * sin) - (command.WorldDirection.Z * cos);
        var localYaw = MathF.Atan2(localRight, localForward);
        var stanceSpeeds = actualStance == AlsStance.Standing
            ? _settings.StandingSpeeds
            : _settings.CrouchingSpeeds;
        var directionalSpeeds = command.MaxAllowedGait switch
        {
            AlsGait.Walking => stanceSpeeds.Walking,
            AlsGait.Running => stanceSpeeds.Running,
            AlsGait.Sprinting => stanceSpeeds.Sprinting,
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
        return command.InputAmount * AlsLocomotionModel.SampleDirectionalSpeed(
            directionalSpeeds,
            localYaw,
            _settings.DirectionalSpeedForwardAngle,
            _settings.DirectionalSpeedBackwardAngle);
    }

    internal static Vector3 IntegrateHorizontalVelocity(
        in Vector3 currentVelocity,
        in Vector3 desiredVelocity,
        float acceleration,
        float brakingDeceleration,
        float deltaTime)
    {
        RequireFiniteVector(currentVelocity, nameof(currentVelocity));
        RequireFiniteVector(desiredVelocity, nameof(desiredVelocity));
        RequirePositiveFinite(acceleration, nameof(acceleration));
        RequirePositiveFinite(brakingDeceleration, nameof(brakingDeceleration));
        RequirePositiveFinite(deltaTime, nameof(deltaTime));

        var currentX = (double)currentVelocity.X;
        var currentY = (double)currentVelocity.Y;
        var currentZ = (double)currentVelocity.Z;
        var desiredX = (double)desiredVelocity.X;
        var desiredY = (double)desiredVelocity.Y;
        var desiredZ = (double)desiredVelocity.Z;
        var differenceX = desiredX - currentX;
        var differenceY = desiredY - currentY;
        var differenceZ = desiredZ - currentZ;
        var distance = Math.Sqrt(
            (differenceX * differenceX) +
            (differenceY * differenceY) +
            (differenceZ * differenceZ));
        if (distance == 0d)
        {
            return desiredVelocity;
        }

        var currentDotDifference =
            (currentX * differenceX) +
            (currentY * differenceY) +
            (currentZ * differenceZ);
        if (currentDotDifference >= 0d)
        {
            return MoveToward(
                currentX,
                currentY,
                currentZ,
                desiredX,
                desiredY,
                desiredZ,
                distance,
                (double)acceleration * deltaTime);
        }

        var brakingDistance = Math.Min(-currentDotDifference / distance, distance);
        var brakingTime = brakingDistance / brakingDeceleration;
        if (brakingTime >= deltaTime)
        {
            return MoveToward(
                currentX,
                currentY,
                currentZ,
                desiredX,
                desiredY,
                desiredZ,
                distance,
                (double)brakingDeceleration * deltaTime);
        }

        if (brakingDistance >= distance)
        {
            return desiredVelocity;
        }

        var inverseDistance = 1d / distance;
        var minimumX = currentX + (differenceX * inverseDistance * brakingDistance);
        var minimumY = currentY + (differenceY * inverseDistance * brakingDistance);
        var minimumZ = currentZ + (differenceZ * inverseDistance * brakingDistance);
        return MoveToward(
            minimumX,
            minimumY,
            minimumZ,
            desiredX,
            desiredY,
            desiredZ,
            distance - brakingDistance,
            (double)acceleration * (deltaTime - brakingTime));
    }

    private static Vector3 MoveToward(
        double currentX,
        double currentY,
        double currentZ,
        double desiredX,
        double desiredY,
        double desiredZ,
        double distance,
        double maximumDistance)
    {
        if (maximumDistance >= distance)
        {
            return CreateFiniteVector(desiredX, desiredY, desiredZ);
        }

        var amount = maximumDistance / distance;
        return CreateFiniteVector(
            currentX + ((desiredX - currentX) * amount),
            currentY + ((desiredY - currentY) * amount),
            currentZ + ((desiredZ - currentZ) * amount));
    }

    private static Vector3 CreateFiniteVector(double x, double y, double z) => new(
        ToFiniteFloat(x),
        ToFiniteFloat(y),
        ToFiniteFloat(z));

    private static float ToFiniteFloat(double value)
    {
        if (!double.IsFinite(value) || value < -float.MaxValue || value > float.MaxValue)
        {
            throw new InvalidOperationException("Velocity integration produced a non-finite component.");
        }

        return (float)value;
    }

    private static void RequireFiniteVector(in Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Velocity components must be finite.");
        }
    }

    private static void RequirePositiveFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value must be positive and finite.");
        }
    }

    private AlsFloorSample CreateFloorSample(bool grounded)
    {
        if (!grounded)
        {
            return new AlsFloorSample(
                0,
                NumericsVector3.UnitY,
                -1,
                NumericsMatrix4x4.Identity,
                NumericsVector3.Zero);
        }

        var floorNormal = GetFloorNormal();
        if (TryFindFloorCollider(floorNormal, out var floorCollider) &&
            IsMovingPlatform(floorCollider))
        {
            var platformTransform = floorCollider.GlobalTransform;
            var platformBasis = platformTransform.Basis.Orthonormalized();
            var platformRotation = platformBasis.GetRotationQuaternion().Normalized();
            var angularVelocity = ToNumerics(GetPlatformAngularVelocity());
            var colliderId = floorCollider.GetInstanceId();
            if (colliderId <= long.MaxValue &&
                IsFinite(platformTransform.Origin) &&
                IsFinite(platformBasis) && IsFinite(platformRotation) &&
                IsFinite(angularVelocity))
            {
                var transform = NumericsMatrix4x4.CreateFromQuaternion(
                    new NumericsQuaternion(
                        platformRotation.X,
                        platformRotation.Y,
                        platformRotation.Z,
                        platformRotation.W));
                transform.Translation = ToNumerics(platformTransform.Origin);
                return new AlsFloorSample(
                    1,
                    ToNumerics(floorNormal),
                    CreatePlatformId(colliderId),
                    transform,
                    angularVelocity);
            }
        }

        return new AlsFloorSample(
            1,
            ToNumerics(floorNormal),
            -1,
            NumericsMatrix4x4.Identity,
            NumericsVector3.Zero);
    }

    private bool TryFindFloorCollider(
        in Vector3 floorNormal,
        out CollisionObject3D collider)
    {
        // MoveAndSlide owns grounded/platform evidence. TestMove only supplies allocation-free
        // support candidates for deterministic matching against that evidence.
        collider = null!;
        var platformVelocity = GetPlatformVelocity();
        var platformAngularVelocity = GetPlatformAngularVelocity();
        if (!IsFinite(platformVelocity) || !IsFinite(platformAngularVelocity))
        {
            return false;
        }
        var bestVelocityError = float.PositiveInfinity;
        var bestAngularError = float.PositiveInfinity;
        var bestMovingEligibility = false;
        var bestAlignment = -1f;
        var bestColliderId = ulong.MaxValue;
        var collision = _initialFloorProbe!;
        if (!TestMove(
                GlobalTransform,
                -UpDirection * FloorSnapLength,
                collision,
                SafeMargin,
                recoveryAsCollision: true,
                maxCollisions: 4))
        {
            return false;
        }
        for (var collisionIndex = 0;
             collisionIndex < collision.GetCollisionCount();
             collisionIndex++)
        {
            var normal = collision.GetNormal(collisionIndex);
            if (!IsFinite(normal) ||
                !IsFloorCollision(normal, UpDirection, FloorMaxAngle))
            {
                continue;
            }
            var alignment = normal.Normalized().Dot(floorNormal);
            var candidate = collision.GetCollider(collisionIndex) as CollisionObject3D;
            var candidateVelocity = collision.GetColliderVelocity(collisionIndex);
            if (candidate is null || !GodotObject.IsInstanceValid(candidate) ||
                !IsFinite(candidateVelocity) ||
                !TryGetAngularVelocity(candidate, out var candidateAngularVelocity))
            {
                continue;
            }
            var velocityError = (candidateVelocity - platformVelocity).LengthSquared();
            var angularError =
                (candidateAngularVelocity - platformAngularVelocity).LengthSquared();
            if (!float.IsFinite(velocityError) || !float.IsFinite(angularError) ||
                !float.IsFinite(alignment))
            {
                continue;
            }
            var movingEligibility = IsMovingPlatform(candidate);
            var colliderId = candidate.GetInstanceId();
            if (!IsBetterFloorCandidate(
                    velocityError,
                    angularError,
                    movingEligibility,
                    alignment,
                    colliderId,
                    bestVelocityError,
                    bestAngularError,
                    bestMovingEligibility,
                    bestAlignment,
                    bestColliderId))
            {
                continue;
            }
            bestVelocityError = velocityError;
            bestAngularError = angularError;
            bestMovingEligibility = movingEligibility;
            bestAlignment = alignment;
            bestColliderId = colliderId;
            collider = candidate;
        }
        return collider is not null;
    }

    private static bool TryGetAngularVelocity(
        CollisionObject3D collider,
        out Vector3 angularVelocity)
    {
        angularVelocity = Vector3.Zero;
        switch (collider)
        {
            case RigidBody3D rigid:
            {
                var state = PhysicsServer3D.BodyGetDirectState(rigid.GetRid());
                if (state is null || !IsFinite(state.AngularVelocity))
                {
                    return false;
                }
                angularVelocity = state.AngularVelocity;
                break;
            }
            case StaticBody3D staticBody:
                angularVelocity = staticBody.ConstantAngularVelocity;
                break;
        }
        return IsFinite(angularVelocity);
    }

    private static bool IsBetterFloorCandidate(
        float velocityError,
        float angularError,
        bool movingEligibility,
        float alignment,
        ulong colliderId,
        float bestVelocityError,
        float bestAngularError,
        bool bestMovingEligibility,
        float bestAlignment,
        ulong bestColliderId)
    {
        const float comparisonEpsilon = 1e-8f;
        if (velocityError < bestVelocityError - comparisonEpsilon)
        {
            return true;
        }
        if (velocityError > bestVelocityError + comparisonEpsilon)
        {
            return false;
        }
        if (angularError < bestAngularError - comparisonEpsilon)
        {
            return true;
        }
        if (angularError > bestAngularError + comparisonEpsilon)
        {
            return false;
        }
        if (movingEligibility != bestMovingEligibility)
        {
            return movingEligibility;
        }
        if (alignment > bestAlignment + comparisonEpsilon)
        {
            return true;
        }
        if (alignment < bestAlignment - comparisonEpsilon)
        {
            return false;
        }
        return colliderId < bestColliderId;
    }

    private bool ProbeInitialFloor()
    {
        var collision = _initialFloorProbe!;
        if (!TestMove(
                GlobalTransform,
                -UpDirection * FloorSnapLength,
                collision,
                SafeMargin,
                recoveryAsCollision: true,
                maxCollisions: 4))
        {
            return false;
        }

        for (var index = 0; index < collision.GetCollisionCount(); index++)
        {
            if (IsFloorCollision(collision.GetNormal(index), UpDirection, FloorMaxAngle))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsFloorCollision(
        in Vector3 normal,
        in Vector3 upDirection,
        float floorMaxAngle) => normal.Dot(upDirection) >= MathF.Cos(floorMaxAngle);

    private float GetCharacterYaw() => GetCharacterYaw(GlobalBasis);

    private static float GetCharacterYaw(in Basis sourceBasis)
    {
        var basis = sourceBasis.Orthonormalized();
        return MathF.Atan2(basis.Z.X, basis.Z.Z);
    }

    private static void EnsureMainThread()
    {
        if (!GodotThread.IsMainThread())
        {
            throw new InvalidOperationException("ALS character motor stepping is restricted to Godot's main thread.");
        }
    }

    private void EnsureLiveInTree()
    {
        if (!GodotObject.IsInstanceValid(this) || IsQueuedForDeletion() || !IsInsideTree())
        {
            throw new InvalidOperationException("ALS character motor must be a live node inside the scene tree.");
        }
    }

    private static NumericsVector3 ToNumerics(in Vector3 value) =>
        new(value.X, value.Y, value.Z);

    private static bool IsFinite(in NumericsVector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static Vector3 ToGodot(in NumericsVector3 value) =>
        new(value.X, value.Y, value.Z);

    private static NumericsMatrix4x4 ToNumerics(in Transform3D value) => new(
        value.Basis.X.X, value.Basis.X.Y, value.Basis.X.Z, 0f,
        value.Basis.Y.X, value.Basis.Y.Y, value.Basis.Y.Z, 0f,
        value.Basis.Z.X, value.Basis.Z.Y, value.Basis.Z.Z, 0f,
        value.Origin.X, value.Origin.Y, value.Origin.Z, 1f);
}
