using Godot;
using GodotAls.Animation;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using NumericsQuaternion = System.Numerics.Quaternion;
using NumericsVector3 = System.Numerics.Vector3;

namespace GodotAls.Locomotion;

internal readonly record struct AlsCharacterMotorLifecycleSnapshot(
    Transform3D GlobalTransform,
    Vector3 Velocity,
    AlsStance ActualStance,
    NumericsVector3 PreviousActualVelocity,
    long LastFrameId,
    Transform3D PreviousGatherTransform,
    bool HasPreviousGatherTransform,
    bool WasGrounded,
    CollisionObject3D? PreviousLeftFootPlatform,
    int PreviousLeftFootPlatformId,
    long PreviousLeftFootColliderId,
    CollisionObject3D? PreviousRightFootPlatform,
    int PreviousRightFootPlatformId,
    long PreviousRightFootColliderId,
    float PreviousControlDegrees)
{
    public AlsCharacterRotationHistory RotationHistory { get; init; }
    public AlsGait RotationGait { get; init; }
    public float RotationWalkSpeed { get; init; }
    public float RotationRunSpeed { get; init; }
    public AlsCharacterMovementHistory MovementHistory { get; init; }
    public AlsMovementBaseHistory MovementBase { get; init; }
    public Vector3 WorldVelocity { get; init; }
    public AlsMotorMantlingState Mantling { get; init; }
}

public partial class AlsCharacterMotor : CharacterBody3D
{
    internal const int MaximumFloorSupportCollisions = 32;

    private const float ClearanceMargin = 0.002f;
    private static readonly StringName ColliderKey = "collider";
    private static readonly StringName NormalKey = "normal";
    private static readonly StringName PositionKey = "position";

    private IAlsLocomotionCommandSource? _source;
    private CollisionShape3D? _collisionNode;
    private CapsuleShape3D? _capsuleShape;
    private ShapeCast3D? _ragdollGroundCast;
    internal bool RagdollGrounded { get; private set; }
    internal Vector3 RagdollTarget { get; private set; }
    internal bool GetUpInputBlocked { get; set; }
    internal AlsActionRequest RecoveryRequest { get; set; } = AlsActionRequest.None;

    internal void ImportDemoHandoff(in DemoPlayerHandoff state)
    {
        EnsureMainThread(); state.Validate();
        if (!_configured || _lastFrameId != -1 || CollisionLayer != 0 || CollisionMask != 0)
            throw new InvalidOperationException("Handoff requires a fresh inactive motor.");
        _actualStance = state.Crouching ? AlsStance.Crouching : AlsStance.Standing;
        _capsuleShape!.Height = state.Crouching ? _settings.CrouchingHeight : _settings.StandingHeight;
        GlobalTransform = new(new Basis(Vector3.Up, state.Yaw), state.Feet + Vector3.Up * (_capsuleShape.Height * .5f));
        Velocity = state.Velocity; _previousActualVelocity = ToNumerics(Velocity);
        _rotationHistory = AlsCharacterRotationModel.Initialize(-state.Yaw * (180d / System.Math.PI));
        _previousGatherTransform = GlobalTransform; _hasPreviousGatherTransform = false;
        _movementBase = default; WorldMovementVelocity = Velocity;
        _restoredGroundedBeforeMove = state.Grounded; _hasRestoredGroundedState = true;
        _committedLifecycleSnapshot = CaptureLifecycleSnapshot(state.Grounded);
        _committedLifecycleFrameId = 0;
    }

    internal void RestoreFromRagdoll(Transform3D actor, bool grounded, Vector3 fallingVelocity)
    {
        EnsureMainThread(); EnsureLiveInTree();
        if (!_configured || CollisionLayer != 0 || CollisionMask != 0 || !actor.IsFinite() || !fallingVelocity.IsFinite())
            throw new InvalidOperationException("Ragdoll exit requires a disabled capsule and finite restored state.");
        GlobalTransform = actor; Velocity = grounded ? Vector3.Zero : fallingVelocity;
        _previousActualVelocity = ToNumerics(Velocity);
        _rotationHistory = AlsCharacterRotationModel.Initialize(-GetCharacterYaw() * (180d / System.Math.PI));
        _movementBase = default; WorldMovementVelocity = Velocity;
        BaseTransportDelta = default; BaseTransportBlocked = false;
        _restoredGroundedBeforeMove = grounded; _hasRestoredGroundedState = true;
        _releasePlatformOnNextStep = true;
        _committedLifecycleSnapshot = CaptureLifecycleSnapshot(grounded);
        _committedLifecycleFrameId = _lastFrameId;
        CollisionLayer = 1; CollisionMask = _settings.CollisionMask;
    }

    internal void FollowRagdoll(Vector3 pelvis)
    {
        EnsureMainThread(); EnsureLiveInTree();
        if (!_configured || CollisionLayer != 0 || CollisionMask != 0 || !pelvis.IsFinite())
            throw new InvalidOperationException("Ragdoll following requires a finite pelvis and disabled capsule.");
        if (_ragdollGroundCast is null)
        {
            _ragdollGroundCast = new ShapeCast3D
            {
                Name = "AlsRagdollGroundCast", Shape = new SphereShape3D(), Enabled = false,
                TopLevel = true, CollisionMask = _settings.CollisionMask,
                CollideWithBodies = true, CollideWithAreas = false, ExcludeParent = true, Margin = 0,
            };
            AddChild(_ragdollGroundCast);
            _ragdollGroundCast.AddException(this);
        }
        var scale = _collisionNode!.GlobalBasis.Scale.Abs();
        var radius = _capsuleShape!.Radius * Mathf.Min(scale.X, scale.Z);
        var halfHeight = Mathf.Max(radius, _capsuleShape.Height * scale.Y * .5f);
        // ALS uses the actor location only when the replicated target is exactly zero.
        var target = pelvis == Vector3.Zero ? GlobalPosition : pelvis;
        var start = target + Vector3.Up * (2 * radius);
        var motion = Vector3.Down * (halfHeight + radius);
        ((SphereShape3D)_ragdollGroundCast.Shape).Radius = radius;
        _ragdollGroundCast.GlobalTransform = new Transform3D(Basis.Identity, start);
        // Godot motion casts can skip initial overlaps. Check the starting sphere
        // explicitly so penetration retains UE's blocking time-zero hit.
        _ragdollGroundCast.TargetPosition = Vector3.Zero;
        _ragdollGroundCast.ForceShapecastUpdate();
        var initiallyOverlapping = _ragdollGroundCast.IsColliding();
        if (!initiallyOverlapping)
        {
            _ragdollGroundCast.TargetPosition = motion;
            _ragdollGroundCast.ForceShapecastUpdate();
        }
        RagdollGrounded = _ragdollGroundCast.IsColliding();
        RagdollTarget = target;
        if (RagdollGrounded)
        {
            // UE Hit.Location is the swept sphere CENTER, not the contact point.
            var center = start + motion * (initiallyOverlapping ? 0 : _ragdollGroundCast.GetClosestCollisionUnsafeFraction());
            target.Y = center.Y + halfHeight - radius + .019f;
        }
        GlobalPosition = target;
        Velocity = Vector3.Zero;
    }
    private ShapeCast3D? _standClearance;
    private KinematicCollision3D? _initialFloorProbe;
    private AlsLandPredictionProbe _landPredictionProbe = null!;
    private AlsRefactoredGroundPredictionGather? _refactoredPredictionGather;
    private AlsRefactoredGroundPrediction? _refactoredPrediction;
    private ulong _refactoredPredictionSerial;
    private AlsLandPredictionSettings _landPredictionSettings;
    private readonly PhysicsRayQueryParameters3D[] _footQueries =
        new PhysicsRayQueryParameters3D[AlsP4FootProbeExchange.FootCount];
    private AlsP4FootProbeExchange _footProbeExchange = null!;
    private AlsP4FootGatherSettings _footGatherSettings;
    private AlsP3RuntimeContext? _runtimeContext;
    private AlsMotorSettings _settings;
    private AlsStance _actualStance = AlsStance.Standing;
    private NumericsVector3 _previousActualVelocity;
    private float _previousControlDegrees;
    private AlsCharacterRotationModel? _characterRotation;
    private AlsCharacterMovementRuntime? _movementRuntime;
    private AlsCharacterMovementHistory _movementHistory;
    internal AlsCharacterMovementHistory MovementHistory => _movementHistory;
    internal AlsCharacterMovementStep MovementDiagnostics { get; private set; }
    private AlsCharacterRotationHistory _rotationHistory;
    private AlsGait _rotationGait;
    private float _rotationWalkSpeed, _rotationRunSpeed;
    internal AlsCharacterRotationUpdate RotationDiagnostics { get; private set; }
    private long _lastFrameId = -1;
    private Transform3D _previousGatherTransform;
    private bool _hasPreviousGatherTransform;
    private AlsCharacterMotorLifecycleSnapshot _candidateLifecycleSnapshot;
    private AlsCharacterMotorLifecycleSnapshot _committedLifecycleSnapshot;
    private long _candidateLifecycleFrameId = -1;
    private long _committedLifecycleFrameId = -1;
    private bool _hasRestoredGroundedState;
    private bool _restoredGroundedBeforeMove;
    private bool _releasePlatformOnNextStep;
    private bool _publishedVelocityCheckpointPending;
    private CollisionObject3D? _previousLeftFootPlatform;
    private int _previousLeftFootPlatformId = -1;
    private long _previousLeftFootColliderId = -1;
    private CollisionObject3D? _previousRightFootPlatform;
    private int _previousRightFootPlatformId = -1;
    private long _previousRightFootColliderId = -1;
    private bool _configured;

    internal long LastFootGatherManagedAllocations { get; private set; }
    public bool FirstPersonView { get; set; }
    internal int LastLandPredictionQueries => _landPredictionProbe.LastQueryCount;

    internal NumericsVector3 LifecycleActualVelocity => _previousActualVelocity;

    internal bool HasPublishedVelocityCheckpoint => _publishedVelocityCheckpointPending;
    internal long IntegrationCount { get; private set; }
    internal AlsMontageRootMotionRange LastConsumedRootMotion { get; private set; }
    internal AlsRootMotionDelta LastRootMotionWorldDelta { get; private set; } = AlsRootMotionDelta.Identity;
    internal bool ConsumeMontageRootMotion { get; set; }
    internal bool RollingGameplay { get; set; }
    // ACharacter::OnStartCrouch keeps the skeletal mesh at its original height
    // when the capsule center lowers. The authored pose supplies the crouch.
    private float MeshHeightOffset => RollingGameplay && _actualStance == AlsStance.Crouching
        ? (_settings.StandingHeight - _settings.CrouchingHeight) * .5f : 0;
    private AlsLocalPose NativeComponentToCharacter => _initialNativeFeet.ComponentToCharacter with
    { Position = _initialNativeFeet.ComponentToCharacter.Position + NumericsVector3.UnitY * MeshHeightOffset };

    internal AlsFrameIdentity LastFootGatherRequestIdentity { get; private set; }

    internal Vector3 LastLeftFootQueryWorldOrigin { get; private set; }

    internal Vector3 LastRightFootQueryWorldOrigin { get; private set; }

    internal bool LastFootGatherConsumed { get; private set; }

    internal bool LastFloorSelectionUsedSlideEvidence { get; private set; }

    public AlsCharacterMotor()
    {
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = 0;
    }

    public void Configure(in AlsMotorSettings settings, IAlsLocomotionCommandSource source, AlsCharacterMovementRuntime? movement = null)
    {
        Configure(
            settings,
            source,
            new AlsP4FootProbeExchange(),
            AlsP4FootGatherSettings.CreateReference(),
            runtimeContext: null, movement: movement);
    }

    internal void Configure(
        in AlsMotorSettings settings,
        IAlsLocomotionCommandSource source,
        AlsP4FootProbeExchange footProbeExchange,
        in AlsP4FootGatherSettings footGatherSettings,
        AlsP3RuntimeContext? runtimeContext, AlsCharacterMovementRuntime? movement = null)
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
        if (AlsAnimationRuntimeOptions.Has("--refactored-stance-hosts"))
        { _mantleResources = AlsMantlingDemoResources.Shared.Value; _mantleProbe = new(this); }
        _landPredictionProbe = new AlsLandPredictionProbe(this, capsuleShape);
        if (AlsAnimationRuntimeOptions.Has("--refactored-pose-curves"))
        {
            _refactoredPrediction = AlsRefactoredGroundPredictionCompiler.Compile(Godot.FileAccess.GetFileAsString(
                "res://assets/config/refactored_ground_prediction_inputs.json")).Model;
            _refactoredPredictionGather = new(this, new(1, 2, 4), [GetRid()]);
        }
        _landPredictionSettings = runtimeContext?.MovementGraph?.LandPrediction.Settings ?? AlsLandPredictionSettings.Reference;
        _footProbeExchange = footProbeExchange;
        _footGatherSettings = footGatherSettings;
        _runtimeContext = runtimeContext;
        _settings = settings;
        _source = source;
        _actualStance = AlsStance.Standing;
        _previousActualVelocity = NumericsVector3.Zero;
        _lastFrameId = -1;
        _previousGatherTransform = GlobalTransform;
        _previousControlDegrees = 0;
        _characterRotation = runtimeContext?.MovementGraph?.CharacterRotation;
        _movementRuntime = movement ?? runtimeContext?.MovementGraph?.CharacterMovementRuntime;
        _movementHistory = _movementRuntime?.InitialState ?? default;
        _rotationHistory = AlsCharacterRotationModel.Initialize(-GetCharacterYaw() * (180d / System.Math.PI));
        _rotationGait = _characterRotation?.InitialGait ?? AlsGait.Walking;
        _rotationWalkSpeed = _characterRotation?.InitialWalkSpeed ?? 0;
        _rotationRunSpeed = _characterRotation?.InitialRunSpeed ?? 0;
        _hasPreviousGatherTransform = false;
        _candidateLifecycleFrameId = -1;
        _committedLifecycleFrameId = 0;
        _hasRestoredGroundedState = false;
        _restoredGroundedBeforeMove = false;
        _releasePlatformOnNextStep = false;
        _publishedVelocityCheckpointPending = false;
        ClearPreviousFootPlatforms();
        Velocity = Vector3.Zero;
        CollisionMask = settings.CollisionMask;
        MotionMode = MotionModeEnum.Grounded;
        UpDirection = Vector3.Up;
        FloorSnapLength = 0.1f;
        FloorStopOnSlope = true;
        ConfigureBasedMovement();
        _configured = true;
        _committedLifecycleSnapshot = CaptureLifecycleSnapshot(ProbeInitialFloor());
    }

    public AlsFrameInput Step(
        long frameId,
        int characterId,
        int generation,
        float deltaTime,
        byte hasTargetYaw = 0,
        float targetYaw = 0f,
        AlsCharacterRotationFeedback rotationFeedback = default, AlsRefactoredAnimationFeedback refactoredFeedback = default,
        AlsMontageRootMotionRange rootMotionSource = default, AlsRootMotionDelta rootMotion = default,
        AlsRollingState rolling = default)
    {
        ValidateStep(frameId, characterId, generation, deltaTime, hasTargetYaw, targetYaw);
        var hasRootMotion = rootMotionSource.HasMotion;
        if (hasRootMotion && (!ConsumeMontageRootMotion || !_nativeFootGatherEnabled ||
            rootMotionSource.Identity != new AlsFrameIdentity(frameId, (uint)characterId, (uint)generation)))
            throw new InvalidOperationException("Root motion requires the matching physical frame and configured mesh.");
        LastConsumedRootMotion = default; LastRootMotionWorldDelta = AlsRootMotionDelta.Identity;
        rotationFeedback.ValidateForFrame(new AlsFrameIdentity(frameId, (uint)characterId, (uint)generation));
        _publishedVelocityCheckpointPending = false;
        var source = _source!;
        var command = source.GetCommand(frameId);
        if (GetUpInputBlocked) command = command with { MovementAxes = default, JumpPressed = 0 };
        var isRolling = RollingGameplay && rolling.Active;
        // Desired stance stays in the input adapter; only the effective command
        // crouches during Rolling, then restores that desired stance with clearance.
        if (isRolling) command = command with { RequestedStance = AlsStance.Crouching, JumpPressed = 0 };
        var actionRequest = source is IAlsActionRequestSource actionSource
            ? actionSource.GetActionRequest(new(frameId, (uint)characterId, (uint)generation)) : AlsActionRequest.None;
        if (RecoveryRequest.Command != AlsActionCommand.None) actionRequest = RecoveryRequest;
        else if (GetUpInputBlocked) actionRequest = AlsActionRequest.None;
        if (actionRequest.Command != AlsActionCommand.None && _runtimeContext?.MovementGraph is null)
            throw new InvalidOperationException("Action requests require the complete movement runtime.");
        if (command.JumpPressed > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                "JumpPressed must be zero or one.");
        }

        var worldStart = GlobalPosition;
        var currentStanceCommand = AlsLocomotionCommandResolver.Resolve(command, _actualStance);
        var requestedStanceCommand = AlsLocomotionCommandResolver.Resolve(
            command,
            currentStanceCommand.RequestedStance);
        var currentVelocity = Velocity;
        RequireFiniteVector(currentVelocity, nameof(Velocity));
        if (hasTargetYaw == 1 && _characterRotation is null)
        {
            GlobalBasis = new Basis(Vector3.Up, targetYaw);
        }

        var movementYaw = GetCharacterYaw();
        if (!float.IsFinite(movementYaw))
        {
            throw new ArgumentOutOfRangeException(nameof(GlobalTransform), "Character yaw must be finite.");
        }

        var currentDesiredSpeed = _movementRuntime is null ? CalculateDesiredSpeed(currentStanceCommand, movementYaw, _actualStance) : 0;
        var requestedDesiredSpeed = _movementRuntime is null ? CalculateDesiredSpeed(
            requestedStanceCommand,
            movementYaw,
            currentStanceCommand.RequestedStance) : 0;
        var currentDesiredVelocity = ToGodot(currentStanceCommand.WorldDirection) * currentDesiredSpeed;
        var requestedDesiredVelocity = ToGodot(requestedStanceCommand.WorldDirection) * requestedDesiredSpeed;
        RequireFiniteVector(currentDesiredVelocity, nameof(currentDesiredVelocity));
        RequireFiniteVector(requestedDesiredVelocity, nameof(requestedDesiredVelocity));

        var currentHorizontal = new Vector3(currentVelocity.X, 0f, currentVelocity.Z);
        var currentStanceHorizontalVelocity = _movementRuntime is null ? IntegrateHorizontalVelocity(
            currentHorizontal,
            currentDesiredVelocity,
            _settings.MaxAcceleration,
            _settings.MaxBrakingDeceleration,
            deltaTime) : currentHorizontal;
        var requestedStanceHorizontalVelocity = _movementRuntime is null ? IntegrateHorizontalVelocity(
            currentHorizontal,
            requestedDesiredVelocity,
            _settings.MaxAcceleration,
            _settings.MaxBrakingDeceleration,
            deltaTime) : currentHorizontal;

        var standingRequestBlocked = _mantling.Frame.Active ? false : UpdateStance(currentStanceCommand.RequestedStance);
        var usedRequestedStance = _actualStance == currentStanceCommand.RequestedStance;
        var resolvedCommand = usedRequestedStance ? requestedStanceCommand : currentStanceCommand;
        var desiredSpeed = usedRequestedStance ? requestedDesiredSpeed : currentDesiredSpeed;
        var horizontalVelocity = usedRequestedStance
            ? requestedStanceHorizontalVelocity
            : currentStanceHorizontalVelocity;
        RequireFiniteVector(horizontalVelocity, nameof(horizontalVelocity));

        var verticalVelocity = currentVelocity.Y;
        byte jumpAccepted = 0;
        var groundedBeforeMove = _hasRestoredGroundedState
            ? _restoredGroundedBeforeMove
            : IsOnFloor() || (_lastFrameId < 0 && ProbeInitialFloor());
        _hasRestoredGroundedState = false;
        var isMantling = StepMantling(groundedBeforeMove, resolvedCommand, command.RequestedOverlay,
            GetUpInputBlocked || isRolling || hasRootMotion || standingRequestBlocked, command.CancelAction, actionRequest, deltaTime);
        if (isMantling) resolvedCommand = resolvedCommand with { JumpPressed = 0 };
        if (groundedBeforeMove && !isMantling)
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

        if (!isMantling) MoveWithNativeBase(groundedBeforeMove, transport: jumpAccepted == 0);
        var movementStep = default(AlsCharacterMovementStep);
        if (jumpAccepted == 1 && _movementRuntime is not null)
        {
            var inherited = NativeBaseDepartureVelocity();
            currentHorizontal += new Vector3(inherited.X, 0, inherited.Z);
            verticalVelocity += inherited.Y;
        }
        if (_movementRuntime is not null)
        {
            // UE CMC ticks before Character. Saved parameters drive this move;
            // the new physical speed updates the parameters only after movement.
            var direction = resolvedCommand.WorldDirection;
            movementStep = _movementRuntime.Integrate(_movementHistory,
                new(-currentHorizontal.Z * 100d, currentHorizontal.X * 100d, 0),
                new(-(double)direction.Z * resolvedCommand.InputAmount, (double)direction.X * resolvedCommand.InputAmount, 0),
                _actualStance, groundedBeforeMove && jumpAccepted == 0 && !isMantling, deltaTime, hasRootMotion || isMantling);
            horizontalVelocity = new((float)(movementStep.Velocity.Y * .01), 0, (float)(-movementStep.Velocity.X * .01));
            desiredSpeed = movementStep.MaxSpeed * movementStep.Analog * .01f;
            MovementDiagnostics = movementStep;
        }
        var nextVelocity = new Vector3(horizontalVelocity.X, verticalVelocity, horizontalVelocity.Z);
        var rootMotionBasis = Basis.Identity;
        if (hasRootMotion)
        {
            var canonicalToBone = new AlsPrecisePose(default,
                new(System.Numerics.Quaternion.Conjugate(AlsFootIkCoordinates.FbxToGodotRotation)), AlsDoubleVector.One);
            var componentToCharacter = AlsPrecisePose.Compose(canonicalToBone, new(NativeComponentToCharacter));
            var world = AlsRootMotionKinematics.ToWorld(rootMotion, componentToCharacter, new(AlsFootIkGodot.Pose(GlobalTransform)));
            nextVelocity = ToGodot(world.Translation) / deltaTime;
            // Falling retains gravity; the montage only overrides horizontal velocity.
            if (!groundedBeforeMove || jumpAccepted == 1) nextVelocity.Y = verticalVelocity;
            LastRootMotionWorldDelta = world;
            var q = world.Rotation;
            rootMotionBasis = new Basis(new Quaternion(q.X, q.Y, q.Z, q.W));
            if ((rootMotionBasis * GlobalBasis).Y.Normalized().Dot(Vector3.Up) < .99999f)
                throw new InvalidOperationException("Root motion cannot tilt the upright character capsule.");
        }
        RequireFiniteVector(nextVelocity, nameof(nextVelocity));
        if (!isMantling) { Velocity = nextVelocity; MoveAndSlide(); }
        if (isRolling)
        {
            var yaw = AlsRollingGameplay.Rotate(-GetCharacterYaw() * (180f / MathF.PI), rolling.TargetYawDegrees, deltaTime);
            GlobalBasis = new Basis(Vector3.Up, -yaw * (MathF.PI / 180f));
        }
        if (hasRootMotion)
        {
            // CMC applies PhysicsRotation after movement, then the extracted
            // root rotation. Translation used the pre-rotation mesh transform.
            GlobalBasis = rootMotionBasis * GlobalBasis;
            LastConsumedRootMotion = rootMotionSource;
        }

        if (!isMantling && _movementRuntime is not null && groundedBeforeMove && !IsOnFloor() && jumpAccepted == 0)
            Velocity += NativeBaseDepartureVelocity();
        WorldMovementVelocity = (GlobalPosition - worldStart) / deltaTime;

        // ALS SetEssentialValues reads APawn::GetVelocity -> CMC.Velocity.
        // Based movement transports the capsule without adding that displacement
        // to the locomotion velocity. Godot's real velocity includes transport;
        // use the collision-adjusted movement velocity for the native ALS path.
        // This also keeps animation, dynamic movement parameters and foot locking
        // on the same velocity source. World displacement remains in the transform.
        var actualVelocity = ToNumerics(_movementRuntime is null ? GetRealVelocity() : Velocity);
        var grounded = !isMantling && IsOnFloor();
        var movementAction = !isMantling && RollingGameplay && _lastFrameId > 0
            ? AlsMovementActionRules.Evaluate(groundedBeforeMove, grounded, _previousActualVelocity,
                -GetCharacterYaw() * (180f / MathF.PI), isRolling) : default;
        var actionParameters = default(AlsMontageActionParameters);
        // A landing edge and its start parameters are gathered only once. Retry
        // reuses this exact frame; X cancellation takes precedence over auto Roll.
        if (!GetUpInputBlocked && movementAction.Trigger == AlsMovementActionTrigger.LandingRoll && actionRequest.Command != AlsActionCommand.Cancel)
        {
            var policy = _runtimeContext!.MovementGraph!.ActionPolicies[0];
            actionRequest = new(frameId, AlsActionCommand.Start, policy.DefinitionId, policy.StartSectionId, 100, (uint)generation);
            actionParameters = new(AlsMovementActionRules.LandingRollPlayRate, true, movementAction.TargetYawDegrees);
        }
        if (_movementRuntime is not null)
        {
            var acceleration = movementStep.Acceleration;
            var relativeYaw = System.Math.Atan2(acceleration.Y, acceleration.X) * (180 / System.Math.PI) + command.ViewYaw * (180 / System.Math.PI);
            var speedCm = System.Math.Sqrt((double)actualVelocity.X * actualVelocity.X + (double)actualVelocity.Z * actualVelocity.Z) * 100;
            _movementHistory = _movementRuntime.UpdateCharacter(_movementHistory, speedCm, movementStep, _actualStance,
                resolvedCommand.RotationMode, command.RequestedGait, relativeYaw, grounded,
                !groundedBeforeMove && grounded && movementAction.Trigger == AlsMovementActionTrigger.None, deltaTime);
            resolvedCommand = resolvedCommand with { MaxAllowedGait = _movementHistory.AllowedGait };
        }
        var aimRate = AlsAimYawRate.Gather(command.ViewYaw, _previousControlDegrees, deltaTime);
        var lastMovementRotation = GlobalBasis.GetRotationQuaternion();
        var rotationSample = ApplyCharacterRotation(deltaTime, actualVelocity, grounded, resolvedCommand, aimRate, rotationFeedback, hasRootMotion || isMantling, isRolling);
        var characterTransform = GlobalTransform;
        var characterYaw = GetCharacterYaw(characterTransform.Basis);
        if (!float.IsFinite(characterYaw))
        {
            throw new ArgumentOutOfRangeException(nameof(GlobalTransform), "Final character yaw must be finite.");
        }
        var identity = new AlsFrameIdentity(frameId, (uint)characterId, (uint)generation);
        var allocatedBeforeFootGather = GC.GetAllocatedBytesForCurrentThread();
        var releaseSignals = CapturePlatformRemovalSignals();
        AlsFootHit leftFootHit, rightFootHit;
        CollisionObject3D? leftFootPlatform, rightFootPlatform;
        var footScene = default(AlsFootIkSceneSample);
        if (_nativeFootGatherEnabled)
            footScene = GatherNativeFeet(identity, characterTransform, lastMovementRotation, deltaTime, grounded,
                out leftFootHit, out rightFootHit, out leftFootPlatform, out rightFootPlatform);
        else GatherFootHits(identity, characterTransform, out leftFootHit, out rightFootHit, out leftFootPlatform, out rightFootPlatform);
        UpdatePreviousFootPlatforms(
            in releaseSignals,
            in leftFootHit,
            leftFootPlatform,
            in rightFootHit,
            rightFootPlatform);
        var actualAcceleration = (actualVelocity - _previousActualVelocity) / deltaTime;
        var floor = CreateFloorSample(grounded);
        SaveNativeBase(grounded);
        if (_releasePlatformOnNextStep)
        {
            leftFootHit = AlsFootHit.Invalid;
            rightFootHit = AlsFootHit.Invalid;
            floor = new AlsFloorSample(
                floor.IsGrounded,
                floor.Normal,
                -1,
                NumericsMatrix4x4.Identity,
                NumericsVector3.Zero,
                -1);
            _releasePlatformOnNextStep = false;
        }
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
            MantleProbe: new AlsMantleProbeResult(isMantling ? (byte)1 : (byte)0,
                isMantling ? ToNumerics(MantleDestination) : NumericsMatrix4x4.Identity,
                isMantling && _mantling.Relative ? CreatePlatformId(_mantling.Target!.GetInstanceId()) : -1),
            RequestedGait: command.RequestedGait,
            Stance: _actualStance,
            RotationMode: resolvedCommand.RotationMode,
            RequestedAction: isMantling ? AlsLocomotionAction.Mantling : AlsLocomotionAction.None,
            CurrentDriveMode: hasRootMotion || isMantling ? AlsDriveMode.AnimationDriven : AlsDriveMode.MotorDriven,
            RagdollState: AlsRagdollState.Inactive,
            AnimationQualityTier: AlsAnimationQualityTier.Tier0,
            Command: command,
            CharacterYaw: characterYaw,
            MaxAcceleration: _movementRuntime is null ? _settings.MaxAcceleration : _movementHistory.Values.MaxAcceleration * .01f,
            MaxBrakingDeceleration: _movementRuntime is null ? _settings.MaxBrakingDeceleration :
                (grounded ? _movementHistory.Values.BrakingDeceleration : _movementRuntime.Settings.AirBraking) * .01f,
            JumpAccepted: jumpAccepted)
        {
            FootPlacementReleaseSignals = releaseSignals,
            ActionRequest = actionRequest,
            GameplayAction = isMantling ? AlsTimelineAction.Mantling : GetUpInputBlocked ? AlsTimelineAction.GettingUp : RollingGameplay ? (isRolling ? AlsTimelineAction.Rolling :
                rotationFeedback.Action == AlsTimelineAction.Rolling ? AlsTimelineAction.None : rotationFeedback.Action) : default,
            MeshHeightOffset = MeshHeightOffset,
            Mantling = _mantling.Frame,
            ActionParameters = actionParameters,
            MovementAction = movementAction,
            AimYawRateDegrees = aimRate.RateDegrees,
            FirstPerson = FirstPersonView ? (byte)1 : (byte)0,
            CharacterRotation = rotationSample,
            FootIk = footScene,
            MovementInput = _movementRuntime is null ? default : new(1, (float)movementStep.InputAmount),
            LandPrediction = _landPredictionProbe.Gather(_collisionNode!.GlobalPosition, actualVelocity, !grounded, _landPredictionSettings),
            RefactoredGroundPrediction = GatherRefactoredPrediction(identity, actualVelocity, grounded, refactoredFeedback,
                MathF.Abs(footScene.ComponentToWorld.Scale.Y)),
        };

        _previousActualVelocity = actualVelocity;
        _previousControlDegrees = aimRate.ControlDegrees;
        _lastFrameId = frameId;
        _candidateLifecycleSnapshot = CaptureLifecycleSnapshot(grounded);
        _candidateLifecycleFrameId = frameId;
        IntegrationCount++;
        return input;
    }

    internal AlsFrameInput StepPhysicsDriven(AlsFrameIdentity identity, float delta,
        in AlsFrameInput previous, in AlsRagdollPhysicsSample physics, AlsRefactoredAnimationFeedback feedback)
    {
        ValidateStep(identity.FrameId, checked((int)identity.CharacterId), checked((int)identity.SlotGeneration), delta, 0, 0);
        physics.Validate(identity);
        if (CollisionLayer != 0 || CollisionMask != 0)
            throw new InvalidOperationException("Physical drive requires a disabled movement capsule.");
        var command = _source!.GetCommand(identity.FrameId);
        var action = _source is IAlsActionRequestSource actions ? actions.GetActionRequest(identity) : AlsActionRequest.None;
        var aim = AlsAimYawRate.Gather(command.ViewYaw, _previousControlDegrees, delta);
        var transform = GlobalTransform;
        var feet = GatherNativeFeet(identity, transform, transform.Basis.GetRotationQuaternion(), delta, false,
            out _, out _, out _, out _);
        LastConsumedRootMotion = default; LastRootMotionWorldDelta = AlsRootMotionDelta.Identity;
        _publishedVelocityCheckpointPending = false;
        _hasRestoredGroundedState = false;
        Velocity = Vector3.Zero; // CharacterMovement is disabled; physics owns velocity.
        SaveNativeBase(false);
        var input = previous with
        {
            Identity = identity, DeltaTime = delta, CharacterTransform = ToNumerics(transform),
            // UE MOVE_None clears CharacterMovement velocity. Keep locomotion
            // globals separate from the physical pelvis observation used by Flail.
            ActualVelocity = NumericsVector3.Zero, ActualAcceleration = -_previousActualVelocity / delta,
            InputDirection = NumericsVector3.Zero, DesiredSpeed = 0, Command = command,
            ViewRotation = NumericsQuaternion.CreateFromYawPitchRoll(command.ViewYaw, command.ViewPitch, 0),
            AimRotation = NumericsQuaternion.CreateFromYawPitchRoll(command.AimYaw, command.AimPitch, 0),
            Floor = new(0, NumericsVector3.UnitY, -1, NumericsMatrix4x4.Identity, NumericsVector3.Zero),
            LeftFootHit = AlsFootHit.Invalid, RightFootHit = AlsFootHit.Invalid,
            CurrentDriveMode = AlsDriveMode.PhysicsDriven, RagdollState = AlsRagdollState.Active,
            RequestedAction = AlsLocomotionAction.None, ActionRequest = action,
            GameplayAction = AlsTimelineAction.Ragdolling, MovementAction = default, ActionParameters = default,
            Mantling = default, MantleProbe = new(0, NumericsMatrix4x4.Identity, -1),
            CharacterYaw = GetCharacterYaw(), JumpAccepted = 0, MovementInput = new(1, 0),
            CharacterRotation = new(1, GetCharacterYaw(), 0, AlsCharacterRotationBranch.Hold, _rotationGait, default),
            AimYawRateDegrees = aim.RateDegrees, FirstPerson = FirstPersonView ? (byte)1 : (byte)0,
            FootIk = feet, FootPlacementReleaseSignals = AlsFootPlacementReleaseSignals.CreateDefault(),
            LandPrediction = default, RagdollPhysics = physics,
            RefactoredGroundPrediction = GatherRefactoredPrediction(identity, NumericsVector3.Zero, false, feedback,
                MathF.Abs(feet.ComponentToWorld.Scale.Y), suppressQuery: true),
        };
        _previousActualVelocity = input.ActualVelocity; _previousControlDegrees = aim.ControlDegrees; _lastFrameId = identity.FrameId;
        _candidateLifecycleSnapshot = CaptureLifecycleSnapshot(false); _candidateLifecycleFrameId = identity.FrameId;
        return input; // No movement integration and no root-motion consumption.
    }

    internal AlsFrameInput RecaptureRefactoredPrediction(in AlsFrameInput input) => input with
    {
        // Replacement animation starts cold; query its current capsule instead
        // of relabeling the retired generation's request or cached pose history.
        RefactoredGroundPrediction = GatherRefactoredPrediction(input.Identity, input.ActualVelocity,
            input.Floor.IsGrounded == 1, default, MathF.Abs(input.FootIk.ComponentToWorld.Scale.Y)),
    };

    private AlsRefactoredGroundPredictionSample GatherRefactoredPrediction(AlsFrameIdentity identity,
        NumericsVector3 velocity, bool grounded, AlsRefactoredAnimationFeedback feedback, float componentScale, bool suppressQuery = false)
    {
        if (_refactoredPrediction is null) return default;
        var prior = feedback.Pose.Identity;
        if (prior != default && (prior.CharacterId != identity.CharacterId || prior.SlotGeneration != identity.SlotGeneration || prior.FrameId >= identity.FrameId))
            throw new ArgumentException("Foreign Refactored pose feedback at physics gathering.");
        var center = _collisionNode!.GlobalPosition;
        var capsuleScale = _collisionNode.GlobalTransform.Basis.Scale.Abs();
        var request = _refactoredPrediction.Prepare(new(identity, new(-center.Z * 100d, center.X * 100d, center.Y * 100d),
            AlsFootIkCoordinates.ToNative(velocity), componentScale,
            // UCapsuleComponent uses the smaller horizontal component scale.
            _capsuleShape!.Radius * MathF.Min(capsuleScale.X, capsuleScale.Z) * 100,
            _capsuleShape.Height * capsuleScale.Y * 50, MathF.Cos(FloorMaxAngle), feedback.GroundPredictionBlock), ++_refactoredPredictionSerial);
        if (grounded || suppressQuery) request = request with { Enabled = false };
        return new(1, feedback, _refactoredPredictionGather!.Gather(request));
    }

    public override void _ExitTree()
    {
        _refactoredPredictionGather?.Dispose(); _refactoredPredictionGather = null;
    }

    private AlsCharacterRotationSample ApplyCharacterRotation(float delta, NumericsVector3 velocity, bool grounded,
        in AlsResolvedLocomotionCommand command, in AlsAimYawRateSample aim, in AlsCharacterRotationFeedback feedback, bool hasRootMotion, bool isRolling)
    {
        if (_characterRotation is null) return default;
        var speed = (float)System.Math.Sqrt((double)velocity.X * velocity.X + (double)velocity.Z * velocity.Z);
        if (grounded)
        {
            // UE updates actual gait from the current settings before replacing
            // those settings for a changed stance/rotation mode in this tick.
            _rotationGait = AlsLocomotionModel.CalculateActualGait(speed, _rotationWalkSpeed, _rotationRunSpeed, command.MaxAllowedGait);
            var movement = _characterRotation.Movement(command.RotationMode, _actualStance);
            _rotationWalkSpeed = movement.WalkSpeed; _rotationRunSpeed = movement.RunSpeed;
        }
        var before = GetCharacterYaw();
        var input = new AlsCharacterRotationInput(delta, -before * (180d / System.Math.PI), aim.ControlDegrees,
            WorldYaw(velocity), WorldYaw(command.WorldDirection), speed, command.InputAmount > 0 && _settings.MaxAcceleration > 0,
            hasRootMotion, grounded ? AlsMovementStateInput.Grounded : AlsMovementStateInput.InAir,
            command.RotationMode, _actualStance, _rotationGait,
            _mantling.Frame.Active ? AlsTimelineAction.Mantling : GetUpInputBlocked ? AlsTimelineAction.GettingUp : isRolling ? AlsTimelineAction.Rolling : RollingGameplay && feedback.Action == AlsTimelineAction.Rolling ? AlsTimelineAction.None : feedback.Action,
            FirstPersonView, aim.RateDegrees,
            feedback.YawOffsetPresent ? feedback.YawOffset : 0, feedback.RotationAmountPresent ? feedback.RotationAmount : 0);
        var rotation = _characterRotation.Evaluate(input, _rotationHistory);
        if (isRolling)
            rotation = rotation with { ActorYaw = input.ActorYaw, History = rotation.History with { TargetYaw = input.ActorYaw }, Branch = AlsCharacterRotationBranch.Rolling };
        var yaw = (float)(-rotation.ActorYaw * (System.Math.PI / 180));
        GlobalBasis = new Basis(Vector3.Up, yaw);
        _rotationHistory = rotation.History; RotationDiagnostics = rotation;
        return new(1, (float)(-rotation.History.TargetYaw * (System.Math.PI / 180)),
            GodotAls.Core.Math.AlsMath.NormalizeAngleRadians(yaw - before), rotation.Branch, _rotationGait, feedback.Identity);
        static double WorldYaw(NumericsVector3 value) => System.Math.Atan2(value.X, -value.Z) * (180 / System.Math.PI);
    }

    internal void CommitLifecycleFrame(long frameId)
    {
        EnsureMainThread();
        if (!_configured || _candidateLifecycleFrameId != frameId)
        {
            throw new InvalidOperationException(
                "Motor lifecycle checkpoint does not match the committed frame.");
        }
        _committedLifecycleSnapshot = _candidateLifecycleSnapshot;
        _committedLifecycleFrameId = frameId;
    }

    internal AlsCharacterMotorLifecycleSnapshot CaptureCommittedLifecycleSnapshot(
        long completedFrameId)
    {
        EnsureMainThread();
        if (!_configured || _committedLifecycleFrameId != completedFrameId)
        {
            throw new InvalidOperationException(
                "Motor does not own the requested committed lifecycle checkpoint.");
        }
        return _committedLifecycleSnapshot;
    }

    internal AlsCharacterMotorLifecycleSnapshot CapturePublishedLifecycleSnapshot(
        long publishedFrameId)
    {
        EnsureMainThread();
        if (!_configured || _candidateLifecycleFrameId != publishedFrameId)
        {
            throw new InvalidOperationException(
                "Motor does not own the requested published lifecycle checkpoint.");
        }
        return _candidateLifecycleSnapshot;
    }

    internal void RestorePublishedLifecycleSnapshot(
        in AlsCharacterMotorLifecycleSnapshot snapshot,
        long publishedFrameId,
        bool releasePlatformOnNextStep)
    {
        EnsureMainThread();
        if (!_configured || ProcessMode != ProcessModeEnum.Disabled ||
            CollisionLayer != 0 || CollisionMask != 0)
        {
            throw new InvalidOperationException(
                "Only an inactive Motor can restore a published lifecycle checkpoint.");
        }
        if (publishedFrameId <= 0 || snapshot.LastFrameId != publishedFrameId)
        {
            throw new InvalidOperationException(
                "Motor lifecycle checkpoint does not match the published frame.");
        }

        GlobalTransform = snapshot.GlobalTransform;
        Velocity = snapshot.Velocity;
        _actualStance = snapshot.ActualStance;
        _capsuleShape!.Height = snapshot.ActualStance == AlsStance.Standing
            ? _settings.StandingHeight
            : _settings.CrouchingHeight;
        _previousActualVelocity = snapshot.PreviousActualVelocity;
        _previousControlDegrees = snapshot.PreviousControlDegrees;
        RestoreRotationHistory(snapshot);
        _lastFrameId = snapshot.LastFrameId;
        _previousGatherTransform = snapshot.PreviousGatherTransform;
        _hasPreviousGatherTransform = snapshot.HasPreviousGatherTransform;
        RestorePreviousFootPlatforms(in snapshot);
        _candidateLifecycleSnapshot = snapshot;
        _candidateLifecycleFrameId = publishedFrameId;
        _restoredGroundedBeforeMove = snapshot.WasGrounded;
        _hasRestoredGroundedState = true;
        _releasePlatformOnNextStep = releasePlatformOnNextStep;
        _publishedVelocityCheckpointPending = true;
    }

    internal void RestoreCommittedLifecycleSnapshot(
        in AlsCharacterMotorLifecycleSnapshot snapshot,
        long completedFrameId)
    {
        EnsureMainThread();
        if (!_configured || ProcessMode != ProcessModeEnum.Disabled ||
            CollisionLayer != 0 || CollisionMask != 0)
        {
            throw new InvalidOperationException(
                "Only an inactive Motor can restore a lifecycle checkpoint.");
        }
        var snapshotMatchesFrame = completedFrameId == 0
            ? snapshot.LastFrameId is -1 or 0
            : snapshot.LastFrameId == completedFrameId;
        if (completedFrameId < 0 || !snapshotMatchesFrame)
        {
            throw new InvalidOperationException(
                "Motor lifecycle checkpoint does not match the completed frame.");
        }

        GlobalTransform = snapshot.GlobalTransform;
        Velocity = snapshot.Velocity;
        _actualStance = snapshot.ActualStance;
        _capsuleShape!.Height = snapshot.ActualStance == AlsStance.Standing
            ? _settings.StandingHeight
            : _settings.CrouchingHeight;
        _previousActualVelocity = snapshot.PreviousActualVelocity;
        _previousControlDegrees = snapshot.PreviousControlDegrees;
        RestoreRotationHistory(snapshot);
        _lastFrameId = snapshot.LastFrameId;
        _previousGatherTransform = snapshot.PreviousGatherTransform;
        _hasPreviousGatherTransform = snapshot.HasPreviousGatherTransform;
        RestorePreviousFootPlatforms(in snapshot);
        _candidateLifecycleFrameId = -1;
        _committedLifecycleSnapshot = snapshot;
        _committedLifecycleFrameId = completedFrameId;
        _restoredGroundedBeforeMove = snapshot.WasGrounded;
        _hasRestoredGroundedState = true;
    }

    private AlsCharacterMotorLifecycleSnapshot CaptureLifecycleSnapshot(bool wasGrounded) => new(
        GlobalTransform,
        Velocity,
        _actualStance,
        _previousActualVelocity,
        _lastFrameId,
        _previousGatherTransform,
        _hasPreviousGatherTransform,
        wasGrounded,
        _previousLeftFootPlatform,
        _previousLeftFootPlatformId,
        _previousLeftFootColliderId,
        _previousRightFootPlatform,
        _previousRightFootPlatformId,
        _previousRightFootColliderId,
        _previousControlDegrees)
    {
        RotationHistory = _rotationHistory, RotationGait = _rotationGait,
        RotationWalkSpeed = _rotationWalkSpeed, RotationRunSpeed = _rotationRunSpeed,
        MovementHistory = _movementHistory,
        MovementBase = _movementBase, WorldVelocity = WorldMovementVelocity,
        Mantling = _mantling,
    };

    private void RestoreRotationHistory(in AlsCharacterMotorLifecycleSnapshot snapshot)
    {
        _rotationHistory = snapshot.RotationHistory; _rotationGait = snapshot.RotationGait;
        _rotationWalkSpeed = snapshot.RotationWalkSpeed; _rotationRunSpeed = snapshot.RotationRunSpeed;
        RotationDiagnostics = default;
        _movementHistory = snapshot.MovementHistory;
        MovementDiagnostics = default;
        _movementBase = snapshot.MovementBase; WorldMovementVelocity = snapshot.WorldVelocity;
        _mantling = snapshot.Mantling; MantleTargetDestroyed = false;
        BaseTransportDelta = default; BaseTransportBlocked = false;
    }

    private void GatherFootHits(
        in AlsFrameIdentity identity,
        in Transform3D characterTransform,
        out AlsFootHit left,
        out AlsFootHit right,
        out CollisionObject3D? leftPlatform,
        out CollisionObject3D? rightPlatform)
    {
        left = AlsFootHit.Invalid;
        right = AlsFootHit.Invalid;
        leftPlatform = null;
        rightPlatform = null;
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

        left = GatherFootHit(
            leftRequest, characterTransform, _footQueries[0], out leftPlatform);
        right = GatherFootHit(
            rightRequest, characterTransform, _footQueries[1], out rightPlatform);
        if (_runtimeContext is not null)
        {
            Interlocked.Add(ref _runtimeContext.FootGatherQueries, 2);
        }
    }

    private AlsFootHit GatherFootHit(
        in AlsP4FootProbeRequest request,
        in Transform3D characterTransform,
        PhysicsRayQueryParameters3D query,
        out CollisionObject3D? platform)
    {
        platform = null;
        var localOrigin = ToGodot(request.CharacterLocalOrigin);
        var worldOrigin = characterTransform * localOrigin;
        var up = characterTransform.Basis.Y.Normalized();
        if (!IsFinite(worldOrigin) || !IsFinite(up) || up.IsZeroApprox())
        {
            return AlsFootHit.Invalid;
        }

        query.From = worldOrigin + (up * _footGatherSettings.TraceUpMeters);
        query.To = worldOrigin - (up * _footGatherSettings.TraceDownMeters);
        return ReadFootHit(query, up, out platform);
    }

    private AlsFootHit ReadFootHit(PhysicsRayQueryParameters3D query, Vector3 up, out CollisionObject3D? platform)
    {
        platform = null;
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
        platform = movingPlatform ? collider : null;
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

    private AlsFootPlacementReleaseSignals CapturePlatformRemovalSignals()
    {
        var leftRemoved = _previousLeftFootPlatform is not null &&
                          !GodotObject.IsInstanceValid(_previousLeftFootPlatform);
        var rightRemoved = _previousRightFootPlatform is not null &&
                           !GodotObject.IsInstanceValid(_previousRightFootPlatform);
        return new AlsFootPlacementReleaseSignals(
            leftRemoved ? (byte)1 : (byte)0,
            leftRemoved ? _previousLeftFootPlatformId : -1,
            leftRemoved ? _previousLeftFootColliderId : -1,
            rightRemoved ? (byte)1 : (byte)0,
            rightRemoved ? _previousRightFootPlatformId : -1,
            rightRemoved ? _previousRightFootColliderId : -1);
    }

    private void UpdatePreviousFootPlatforms(
        in AlsFootPlacementReleaseSignals releaseSignals,
        in AlsFootHit leftHit,
        CollisionObject3D? leftPlatform,
        in AlsFootHit rightHit,
        CollisionObject3D? rightPlatform)
    {
        if (releaseSignals.LeftPlatformRemoved == 1 || LastFootGatherConsumed)
        {
            SetPreviousFootPlatform(
                leftPlatform,
                leftHit.PlatformId,
                leftHit.ColliderId,
                left: true);
        }
        if (releaseSignals.RightPlatformRemoved == 1 || LastFootGatherConsumed)
        {
            SetPreviousFootPlatform(
                rightPlatform,
                rightHit.PlatformId,
                rightHit.ColliderId,
                left: false);
        }
    }

    private void SetPreviousFootPlatform(
        CollisionObject3D? platform,
        int platformId,
        long colliderId,
        bool left)
    {
        if (platform is null || !GodotObject.IsInstanceValid(platform) || platformId < 0)
        {
            platform = null;
            platformId = -1;
            colliderId = -1;
        }
        if (left)
        {
            _previousLeftFootPlatform = platform;
            _previousLeftFootPlatformId = platformId;
            _previousLeftFootColliderId = colliderId;
        }
        else
        {
            _previousRightFootPlatform = platform;
            _previousRightFootPlatformId = platformId;
            _previousRightFootColliderId = colliderId;
        }
    }

    private void ClearPreviousFootPlatforms()
    {
        _previousLeftFootPlatform = null;
        _previousLeftFootPlatformId = -1;
        _previousLeftFootColliderId = -1;
        _previousRightFootPlatform = null;
        _previousRightFootPlatformId = -1;
        _previousRightFootColliderId = -1;
    }

    private void RestorePreviousFootPlatforms(
        in AlsCharacterMotorLifecycleSnapshot snapshot)
    {
        _previousLeftFootPlatform = snapshot.PreviousLeftFootPlatform;
        _previousLeftFootPlatformId = snapshot.PreviousLeftFootPlatformId;
        _previousLeftFootColliderId = snapshot.PreviousLeftFootColliderId;
        _previousRightFootPlatform = snapshot.PreviousRightFootPlatform;
        _previousRightFootPlatformId = snapshot.PreviousRightFootPlatformId;
        _previousRightFootColliderId = snapshot.PreviousRightFootColliderId;
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
            case AlsCharacterMotor motor:
                pointVelocity = motor.WorldMovementVelocity;
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
        _sampledFloorCollider = null;
        LastFloorSelectionUsedSlideEvidence = false;
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
        if (TryFindFloorCollider(floorNormal, out var floorCollider))
        {
            var colliderId = floorCollider.GetInstanceId();
            if (colliderId > long.MaxValue)
            {
                return new AlsFloorSample(
                    1,
                    ToNumerics(floorNormal),
                    -1,
                    NumericsMatrix4x4.Identity,
                    NumericsVector3.Zero);
            }
            var fullColliderId = checked((long)colliderId);
            if (!IsMovingPlatform(floorCollider))
            {
                return new AlsFloorSample(
                    1,
                    ToNumerics(floorNormal),
                    -1,
                    NumericsMatrix4x4.Identity,
                    NumericsVector3.Zero,
                    fullColliderId);
            }

            var platformTransform = floorCollider.GlobalTransform;
            _sampledFloorCollider = floorCollider;
            var platformBasis = platformTransform.Basis.Orthonormalized();
            var platformRotation = platformBasis.GetRotationQuaternion().Normalized();
            var angularVelocity = ToNumerics(GetPlatformAngularVelocity());
            if (_movementRuntime is not null)
            {
                ReadBaseVelocity(floorCollider, out _, out var nativeAngular);
                angularVelocity = ToNumerics(nativeAngular);
            }
            if (IsFinite(platformTransform.Origin) &&
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
                    angularVelocity,
                    fullColliderId);
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
        // MoveAndSlide owns grounded evidence. The legacy path matches platform
        // velocities; native based movement owns transport and selects actual
        // floor contacts without treating Godot's disabled carrier velocity as evidence.
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
        CollisionObject3D? bestCollider = null;
        for (var slideIndex = 0; slideIndex < GetSlideCollisionCount(); slideIndex++)
        {
            using var slide = GetSlideCollision(slideIndex);
            TryAccumulateFloorCandidates(
                slide,
                floorNormal,
                platformVelocity,
                platformAngularVelocity,
                ref bestVelocityError,
                ref bestAngularError,
                ref bestMovingEligibility,
                ref bestAlignment,
                ref bestColliderId,
                ref bestCollider);
        }
        if (bestCollider is not null)
        {
            collider = bestCollider;
            LastFloorSelectionUsedSlideEvidence = true;
            return true;
        }

        var collision = _initialFloorProbe!;
        if (TestMove(
                GlobalTransform,
                -UpDirection * FloorSnapLength,
                collision,
                SafeMargin,
                recoveryAsCollision: true,
                maxCollisions: MaximumFloorSupportCollisions))
        {
            TryAccumulateFloorCandidates(
                collision,
                floorNormal,
                platformVelocity,
                platformAngularVelocity,
                ref bestVelocityError,
                ref bestAngularError,
                ref bestMovingEligibility,
                ref bestAlignment,
                ref bestColliderId,
                ref bestCollider);
        }
        if (bestCollider is null)
        {
            return false;
        }
        collider = bestCollider;
        return true;
    }

    internal bool TryFindFloorColliderInSupportProbe(
        KinematicCollision3D collision,
        in Vector3 floorNormal,
        in Vector3 platformVelocity,
        in Vector3 platformAngularVelocity,
        out CollisionObject3D collider)
    {
        ArgumentNullException.ThrowIfNull(collision);
        collider = null!;
        if (!IsFinite(floorNormal) || !IsFinite(platformVelocity) ||
            !IsFinite(platformAngularVelocity))
        {
            return false;
        }

        var bestVelocityError = float.PositiveInfinity;
        var bestAngularError = float.PositiveInfinity;
        var bestMovingEligibility = false;
        var bestAlignment = -1f;
        var bestColliderId = ulong.MaxValue;
        CollisionObject3D? bestCollider = null;
        TryAccumulateFloorCandidates(
            collision,
            floorNormal,
            platformVelocity,
            platformAngularVelocity,
            ref bestVelocityError,
            ref bestAngularError,
            ref bestMovingEligibility,
            ref bestAlignment,
            ref bestColliderId,
            ref bestCollider);
        if (bestCollider is null)
        {
            return false;
        }
        collider = bestCollider;
        return true;
    }

    private bool TryAccumulateFloorCandidates(
        KinematicCollision3D collision,
        in Vector3 floorNormal,
        in Vector3 platformVelocity,
        in Vector3 platformAngularVelocity,
        ref float bestVelocityError,
        ref float bestAngularError,
        ref bool bestMovingEligibility,
        ref float bestAlignment,
        ref ulong bestColliderId,
        ref CollisionObject3D? collider)
    {
        var selected = false;
        for (var collisionIndex = 0;
             collisionIndex < collision.GetCollisionCount();
             collisionIndex++)
        {
            selected |= TrySelectFloorCandidate(
                collision,
                collisionIndex,
                floorNormal,
                platformVelocity,
                platformAngularVelocity,
                ref bestVelocityError,
                ref bestAngularError,
                ref bestMovingEligibility,
                ref bestAlignment,
                ref bestColliderId,
                ref collider);
        }
        return selected;
    }

    private bool TrySelectFloorCandidate(
        KinematicCollision3D collision,
        int collisionIndex,
        in Vector3 floorNormal,
        in Vector3 platformVelocity,
        in Vector3 platformAngularVelocity,
        ref float bestVelocityError,
        ref float bestAngularError,
        ref bool bestMovingEligibility,
        ref float bestAlignment,
        ref ulong bestColliderId,
        ref CollisionObject3D? collider)
    {
        var normal = collision.GetNormal(collisionIndex);
        if (!IsFinite(normal) ||
            !IsFloorCollision(normal, UpDirection, FloorMaxAngle))
        {
            return false;
        }
        var alignment = normal.Normalized().Dot(floorNormal);
        var candidate = collision.GetCollider(collisionIndex) as CollisionObject3D;
        var candidateVelocity = collision.GetColliderVelocity(collisionIndex);
        if (candidate is null || !GodotObject.IsInstanceValid(candidate) ||
            !IsFinite(candidateVelocity) ||
            !TryGetAngularVelocity(candidate, out var candidateAngularVelocity))
        {
            return false;
        }
        var velocityError = _movementRuntime is null ? (candidateVelocity - platformVelocity).LengthSquared() : 0;
        var angularError =
            _movementRuntime is null ? (candidateAngularVelocity - platformAngularVelocity).LengthSquared() : 0;
        if (!float.IsFinite(velocityError) || !float.IsFinite(angularError) ||
            !float.IsFinite(alignment))
        {
            return false;
        }
        var movingEligibility = IsMovingPlatform(candidate);
        var colliderId = candidate.GetInstanceId();
        var better = _movementRuntime is not null
            ? IsBetterNativeFloorCandidate(candidate, alignment, colliderId, collider, bestAlignment, bestColliderId)
            : IsBetterFloorCandidate(
                velocityError,
                angularError,
                movingEligibility,
                alignment,
                colliderId,
                bestVelocityError,
                bestAngularError,
                bestMovingEligibility,
                bestAlignment,
                bestColliderId);
        if (!better)
        {
            return false;
        }
        bestVelocityError = velocityError;
        bestAngularError = angularError;
        bestMovingEligibility = movingEligibility;
        bestAlignment = alignment;
        bestColliderId = colliderId;
        collider = candidate;
        return true;
    }

    private bool IsBetterNativeFloorCandidate(CollisionObject3D candidate, float alignment, ulong id,
        CollisionObject3D? selected, float selectedAlignment, ulong selectedId)
    {
        // Prefer the support matching the solver's actual floor normal. At an
        // equal-height seam retain the current live base while it still has a
        // contact, then use stable identity to resolve an otherwise equal tie.
        if (selected is null || alignment > selectedAlignment + 1e-6f) return true;
        if (alignment < selectedAlignment - 1e-6f) return false;
        var current = _movementBase.Collider;
        if ((candidate == current) != (selected == current)) return candidate == current;
        return id < selectedId;
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
                maxCollisions: MaximumFloorSupportCollisions))
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
