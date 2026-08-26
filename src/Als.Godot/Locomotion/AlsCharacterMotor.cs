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

    private IAlsLocomotionCommandSource? _source;
    private CollisionShape3D? _collisionNode;
    private CapsuleShape3D? _capsuleShape;
    private ShapeCast3D? _standClearance;
    private KinematicCollision3D? _initialFloorProbe;
    private AlsMotorSettings _settings;
    private AlsStance _actualStance = AlsStance.Standing;
    private NumericsVector3 _previousActualVelocity;
    private long _lastFrameId = -1;
    private bool _configured;

    public AlsCharacterMotor()
    {
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = 0;
    }

    public void Configure(in AlsMotorSettings settings, IAlsLocomotionCommandSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        settings.Validate();
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

        _capsuleShape = capsuleShape;
        _settings = settings;
        _source = source;
        _actualStance = AlsStance.Standing;
        _previousActualVelocity = NumericsVector3.Zero;
        _lastFrameId = -1;
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
        float deltaTime)
    {
        ValidateStep(frameId, characterId, generation, deltaTime);
        var source = _source!;
        var command = source.GetCommand(frameId);
        if (command.JumpPressed > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                "JumpPressed must be zero or one.");
        }

        var currentStanceCommand = AlsLocomotionCommandResolver.Resolve(command, _actualStance);
        var currentVelocity = Velocity;
        RequireFiniteVector(currentVelocity, nameof(Velocity));
        var characterYaw = GetCharacterYaw();
        if (!float.IsFinite(characterYaw))
        {
            throw new ArgumentOutOfRangeException(nameof(GlobalTransform), "Character yaw must be finite.");
        }

        var requestedStanceCommand = AlsLocomotionCommandResolver.Resolve(
            command,
            currentStanceCommand.RequestedStance);
        var currentDesiredSpeed = CalculateDesiredSpeed(currentStanceCommand, characterYaw, _actualStance);
        var requestedDesiredSpeed = CalculateDesiredSpeed(
            requestedStanceCommand,
            characterYaw,
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

        var actualVelocity = ToNumerics(GetRealVelocity());
        var actualAcceleration = (actualVelocity - _previousActualVelocity) / deltaTime;
        var grounded = IsOnFloor();
        var floor = CreateFloorSample(grounded);
        var identity = new AlsFrameIdentity(frameId, (uint)characterId, (uint)generation);
        var input = new AlsFrameInput(
            Identity: identity,
            DeltaTime: deltaTime,
            CharacterTransform: ToNumerics(GlobalTransform),
            ActualVelocity: actualVelocity,
            ActualAcceleration: actualAcceleration,
            InputDirection: resolvedCommand.WorldDirection,
            DesiredSpeed: desiredSpeed,
            ViewRotation: NumericsQuaternion.CreateFromAxisAngle(NumericsVector3.UnitY, command.ViewYaw),
            AimRotation: NumericsQuaternion.CreateFromAxisAngle(NumericsVector3.UnitY, command.AimYaw),
            Floor: floor,
            LeftFootHit: new AlsFootHit(0, NumericsVector3.Zero, NumericsVector3.UnitY),
            RightFootHit: new AlsFootHit(0, NumericsVector3.Zero, NumericsVector3.UnitY),
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

    public void ApplyTargetYaw(float targetYaw)
    {
        EnsureMainThread();
        EnsureLiveInTree();
        if (!_configured)
        {
            throw new InvalidOperationException("ALS character motor must be configured before rotation commit.");
        }
        if (!float.IsFinite(targetYaw))
        {
            throw new ArgumentOutOfRangeException(nameof(targetYaw));
        }

        GlobalBasis = new Basis(Vector3.Up, targetYaw);
    }

    public float GetAppliedYaw()
    {
        EnsureMainThread();
        EnsureLiveInTree();
        return GetCharacterYaw();
    }

    private void ValidateStep(long frameId, int characterId, int generation, float deltaTime)
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

        // P3A does not expose moving-platform data; enumerating slide collisions allocates in Godot C#.
        return new AlsFloorSample(
            1,
            ToNumerics(GetFloorNormal()),
            -1,
            NumericsMatrix4x4.Identity,
            NumericsVector3.Zero);
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

    private float GetCharacterYaw()
    {
        var basis = GlobalBasis.Orthonormalized();
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

    private static Vector3 ToGodot(in NumericsVector3 value) =>
        new(value.X, value.Y, value.Z);

    private static NumericsMatrix4x4 ToNumerics(in Transform3D value) => new(
        value.Basis.X.X, value.Basis.X.Y, value.Basis.X.Z, 0f,
        value.Basis.Y.X, value.Basis.Y.Y, value.Basis.Y.Z, 0f,
        value.Basis.Z.X, value.Basis.Z.Y, value.Basis.Z.Z, 0f,
        value.Origin.X, value.Origin.Y, value.Origin.Z, 1f);
}
