using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using NumericsVector3 = System.Numerics.Vector3;
using NumericsVector2 = System.Numerics.Vector2;

namespace GodotAls.Locomotion;

public partial class P4FootGatherSmoke : Node
{
    private static readonly NumericsVector3 LeftLocalOrigin = new(-0.2f, -0.77f, 0f);
    private static readonly NumericsVector3 RightLocalOrigin = new(0.2f, -0.77f, 0f);

    private readonly AlsP4FootProbeExchange _exchange = new();
    private AlsCharacterMotor _motor = null!;
    private AnimatableBody3D _platform = null!;
    private StaticBody3D _staticWorld = null!;
    private RigidBody3D _rigidPlatform = null!;
    private AlsCharacterMotor _seamMotor = null!;
    private StaticBody3D _seamStatic = null!;
    private AnimatableBody3D _seamPlatform = null!;
    private static readonly Vector3 SeamPlatformVelocity = new(0.7f, 0f, 0.15f);
    private AlsCharacterMotor _manyContactMotor = null!;
    private AnimatableBody3D _manyContactPlatform = null!;
    private AlsCharacterMotor _stationaryTransitionMotor = null!;
    private AnimatableBody3D _stationaryPlatform = null!;
    private RightCommandSource _stationaryTransitionSource = null!;
    private readonly AlsP4FootProbeExchange _removalExchange = new();
    private AlsCharacterMotor _removalMotor = null!;
    private AnimatableBody3D _removalLeftPlatform = null!;
    private AnimatableBody3D _removalRightPlatform = null!;
    private long _removedLeftColliderId;
    private int _removedLeftPlatformId;
    private long _retainedRightColliderId;
    private int _retainedRightPlatformId;
    private int _stage;
    private bool _finished;

    public override void _Ready()
    {
        try
        {
            ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
            ProcessThreadGroupOrder = 4;

            _platform = new AnimatableBody3D
            {
                Name = "MovingPlatform",
                Position = new Vector3(0f, -0.1f, 0f),
                CollisionLayer = 1,
                CollisionMask = 1,
            };
            _platform.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(4f, 0.2f, 4f) },
            });
            AddChild(_platform);

            _staticWorld = new StaticBody3D
            {
                Name = "StaticWorld",
                Position = new Vector3(0f, -0.3f, 0f),
                CollisionLayer = 1,
                CollisionMask = 1,
            };
            _staticWorld.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(20f, 0.2f, 20f) },
            });
            AddChild(_staticWorld);

            _rigidPlatform = new RigidBody3D
            {
                Name = "OffsetComRigidPlatform",
                Position = new Vector3(6f, -0.1f, 0f),
                CollisionLayer = 1,
                CollisionMask = 1,
                GravityScale = 0f,
                CanSleep = false,
                CenterOfMassMode = RigidBody3D.CenterOfMassModeEnum.Custom,
                CenterOfMass = new Vector3(0.65f, 0f, 0f),
                LinearVelocity = new Vector3(0.2f, 0f, 0.1f),
                AngularVelocity = new Vector3(0f, 1.25f, 0f),
            };
            _rigidPlatform.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(2f, 0.2f, 2f) },
            });
            AddChild(_rigidPlatform);

            _seamStatic = CreateSeamBody<StaticBody3D>(
                "SeamStatic",
                new Vector3(9f, -0.1f, 0f));
            AddChild(_seamStatic);
            _seamPlatform = CreateSeamBody<AnimatableBody3D>(
                "SeamPlatform",
                new Vector3(11f, -0.1f, 0f));
            _seamPlatform.ConstantLinearVelocity = SeamPlatformVelocity;
            AddChild(_seamPlatform);
            _seamMotor = new AlsCharacterMotor
            {
                Name = "SeamMotor",
                Position = new Vector3(10f, 0.9f, 0f),
            };
            AddChild(_seamMotor);
            _seamMotor.Configure(CreateMotorSettings(), new IdleCommandSource());

            var manyContactStatic = CreateSeamBody<StaticBody3D>(
                "ManyContactStatic",
                new Vector3(29f, -0.1f, 0f));
            for (var index = 0; index < 5; index++)
            {
                manyContactStatic.AddChild(new CollisionShape3D
                {
                    Name = $"ManyContactStaticShape{index}",
                    Shape = new BoxShape3D { Size = new Vector3(2.4f, 0.2f, 4f) },
                });
            }
            AddChild(manyContactStatic);
            _manyContactPlatform = CreateSeamBody<AnimatableBody3D>(
                "ManyContactPlatform",
                new Vector3(31f, -0.1f, 0f));
            AddChild(_manyContactPlatform);
            _manyContactMotor = new AlsCharacterMotor
            {
                Name = "ManyContactMotor",
                Position = new Vector3(30f, 0.9f, 0f),
            };
            AddChild(_manyContactMotor);
            _manyContactMotor.Configure(CreateMotorSettings(), new IdleCommandSource());

            var transitionStatic = CreateSeamBody<StaticBody3D>(
                "TransitionStatic",
                new Vector3(19f, -0.1f, 0f));
            AddChild(transitionStatic);
            _stationaryPlatform = CreateSeamBody<AnimatableBody3D>(
                "StationaryPlatform",
                new Vector3(21f, -0.1f, 0f));
            AddChild(_stationaryPlatform);
            _stationaryTransitionMotor = new AlsCharacterMotor
            {
                Name = "StationaryTransitionMotor",
                Position = new Vector3(19.3f, 0.9f, 0f),
            };
            AddChild(_stationaryTransitionMotor);
            _stationaryTransitionSource = new RightCommandSource();
            _stationaryTransitionMotor.Configure(
                CreateMotorSettings(),
                _stationaryTransitionSource);

            _motor = new AlsCharacterMotor
            {
                Name = "Motor",
                Position = new Vector3(0f, 0.9f, 0f),
            };
            AddChild(_motor);
            _motor.Configure(
                CreateMotorSettings(),
                new IdleCommandSource(),
                _exchange,
                AlsP4FootGatherSettings.CreateReference(),
                runtimeContext: null);
            ConfigureRemovalFixture();
            ValidateMainOwnershipSource();
        }
        catch (Exception exception)
        {
            Fail("init", exception);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_finished)
        {
            return;
        }

        try
        {
            if (_stage == 0)
            {
                CommitFrameOne(delta);
                _stage = 1;
                return;
            }
            if (_stage == 1)
            {
                GatherFrameTwoAndValidateLifecycle(delta);
                PrepareRealPlatformRemoval(delta);
                _stage = 2;
                return;
            }
            ValidateRealPlatformRemoval(delta);
        }
        catch (Exception exception)
        {
            Fail("smoke", exception);
        }
    }

    private void CommitFrameOne(double delta)
    {
        Require(
            AlsCharacterMotor.CreatePlatformId(0x00000001_00000002UL) ==
            AlsCharacterMotor.CreatePlatformId(0x00000002_00000001UL),
            "compact platform collision fixture was not constructed");
        var input = _motor.Step(1, 0, 1, checked((float)delta));
        Require(input.Identity.FrameId == 1,
            "motor/input frame one was delayed");
        Require(input.LeftFootHit == AlsFootHit.Invalid &&
                input.RightFootHit == AlsFootHit.Invalid,
            "first Gather consumed a request before Commit N");

        var result = AlsFrameResult.CreateDefault(input.Identity);
        result.NextLeftFootProbeOrigin = LeftLocalOrigin;
        result.NextRightFootProbeOrigin = RightLocalOrigin;
        Require(AlsP3CommitStage.TryCopyFootProbeRequests(
                _exchange,
                input.Identity,
                result),
            "Commit N rejected finite local probe requests");

        var rejectedExchange = new AlsP4FootProbeExchange();
        var wrongGeneration = result;
        wrongGeneration.Identity = new AlsFrameIdentity(1, 0, 2);
        Require(!AlsP3CommitStage.TryCopyFootProbeRequests(
                rejectedExchange,
                input.Identity,
                wrongGeneration) && !rejectedExchange.HasRequests,
            "Commit cached a generation-mismatched probe request");
        var nonFinite = result;
        nonFinite.NextLeftFootProbeOrigin = new NumericsVector3(float.NaN, 0f, 0f);
        Require(!AlsP3CommitStage.TryCopyFootProbeRequests(
                rejectedExchange,
                input.Identity,
                nonFinite) && !rejectedExchange.HasRequests,
            "Commit cached a non-finite probe request");

        _platform.Position = new Vector3(0.18f, -0.1f, 0.12f);
        _platform.Rotation = new Vector3(0f, 0.3f, 0f);
        _platform.ConstantLinearVelocity = new Vector3(0.4f, 0f, 0.2f);
        _platform.ConstantAngularVelocity = new Vector3(0f, 0.5f, 0f);
    }

    private void GatherFrameTwoAndValidateLifecycle(double delta)
    {
        var expectedPlatformTransform = _platform.GlobalTransform;
        var input = _motor.Step(2, 0, 1, checked((float)delta));
        Require(input.Identity.FrameId == 2,
            "motor/input frame N+1 was delayed by environment feedback");
        ValidateHit(input.LeftFootHit, LeftLocalOrigin, expectedPlatformTransform);
        ValidateHit(input.RightFootHit, RightLocalOrigin, expectedPlatformTransform);
        Require(input.Floor.PlatformId == input.LeftFootHit.PlatformId &&
                input.Floor.ColliderId == checked((long)_platform.GetInstanceId()),
            "Gather did not publish current foot-platform identity into the floor evidence");
        Require(_motor.LastFootGatherManagedAllocations > 0,
            "Godot IntersectRay Dictionary allocation was incorrectly reported as zero bytes");

        var leftSnapshot = input.LeftFootHit;
        var rightSnapshot = input.RightFootHit;
        var originalPlatformPosition = _platform.Position;
        var originalPlatformRotation = _platform.Rotation;
        _platform.Position += new Vector3(0.5f, 0f, -0.25f);
        _platform.Rotation += new Vector3(0f, 0.2f, 0f);
        Require(input.LeftFootHit == leftSnapshot && input.RightFootHit == rightSnapshot,
            "published foot hits were not immutable value snapshots");
        _platform.Position = originalPlatformPosition;
        _platform.Rotation = originalPlatformRotation;

        var farProbe = AlsFrameResult.CreateDefault(new AlsFrameIdentity(2, 0, 1));
        farProbe.NextLeftFootProbeOrigin = new NumericsVector3(-3f, -0.77f, 0f);
        farProbe.NextRightFootProbeOrigin = new NumericsVector3(3f, -0.77f, 0f);
        Require(AlsP3CommitStage.TryCopyFootProbeRequests(
                _exchange,
                farProbe.Identity,
                farProbe),
            "static-world classification request setup failed");
        var splitEvidence = _motor.Step(3, 0, 1, checked((float)delta));
        Require(splitEvidence.LeftFootHit.Valid == 1 &&
                splitEvidence.RightFootHit.Valid == 1 &&
                splitEvidence.LeftFootHit.PlatformId == -1 &&
                splitEvidence.RightFootHit.PlatformId == -1 &&
                splitEvidence.LeftFootHit.ColliderId == checked((long)_staticWorld.GetInstanceId()) &&
                splitEvidence.RightFootHit.ColliderId == checked((long)_staticWorld.GetInstanceId()),
            "ordinary static world was misclassified as a moving platform");
        Require(splitEvidence.Floor.PlatformId ==
                AlsCharacterMotor.CreatePlatformId(_platform.GetInstanceId()) &&
                splitEvidence.Floor.ColliderId == checked((long)_platform.GetInstanceId()),
            "floor evidence followed foot probes instead of the authoritative movement base");

        var rigidProbe = AlsFrameResult.CreateDefault(new AlsFrameIdentity(3, 0, 1));
        rigidProbe.NextLeftFootProbeOrigin = new NumericsVector3(5.8f, -0.77f, 0f);
        rigidProbe.NextRightFootProbeOrigin = new NumericsVector3(6.2f, -0.77f, 0f);
        Require(AlsP3CommitStage.TryCopyFootProbeRequests(
                _exchange,
                rigidProbe.Identity,
                rigidProbe),
            "offset-COM rigid probe setup failed");
        var rigidEvidence = _motor.Step(4, 0, 1, checked((float)delta));
        ValidateRigidPointVelocity(rigidEvidence.LeftFootHit);
        ValidateRigidPointVelocity(rigidEvidence.RightFootHit);
        ValidateSeamMovementBase(delta);
        ValidateStationaryPlatformTransition(delta);

        var missing = _motor.Step(5, 0, 1, checked((float)delta));
        Require(missing.Identity.FrameId == 5 &&
                missing.LeftFootHit == AlsFootHit.Invalid &&
                missing.RightFootHit == AlsFootHit.Invalid &&
                !_exchange.HasRequests,
            "missing Commit leaked a stale foot request into a later Gather");

        var generationOne = AlsFrameResult.CreateDefault(new AlsFrameIdentity(5, 0, 1));
        generationOne.NextLeftFootProbeOrigin = LeftLocalOrigin;
        generationOne.NextRightFootProbeOrigin = RightLocalOrigin;
        Require(AlsP3CommitStage.TryCopyFootProbeRequests(
                _exchange,
                generationOne.Identity,
                generationOne),
            "generation-one request setup failed");
        var replacement = _motor.Step(6, 0, 2, checked((float)delta));
        Require(replacement.LeftFootHit == AlsFootHit.Invalid &&
                replacement.RightFootHit == AlsFootHit.Invalid &&
                !_exchange.HasRequests,
            "replacement generation consumed an old probe request");

        var generationTwo = AlsFrameResult.CreateDefault(new AlsFrameIdentity(6, 0, 2));
        generationTwo.NextLeftFootProbeOrigin = LeftLocalOrigin;
        generationTwo.NextRightFootProbeOrigin = RightLocalOrigin;
        Require(AlsP3CommitStage.TryCopyFootProbeRequests(
                _exchange,
                generationTwo.Identity,
                generationTwo),
            "generation-two request setup failed");
        _exchange.Clear();
        var deactivated = _motor.Step(7, 0, 2, checked((float)delta));
        Require(deactivated.LeftFootHit == AlsFootHit.Invalid &&
                deactivated.RightFootHit == AlsFootHit.Invalid,
            "deactivation clear leaked a cached probe request");

        var beforeTeleport = AlsFrameResult.CreateDefault(new AlsFrameIdentity(7, 0, 2));
        beforeTeleport.NextLeftFootProbeOrigin = LeftLocalOrigin;
        beforeTeleport.NextRightFootProbeOrigin = RightLocalOrigin;
        Require(AlsP3CommitStage.TryCopyFootProbeRequests(
                _exchange,
                beforeTeleport.Identity,
                beforeTeleport),
            "teleport request setup failed");
        _motor.GlobalPosition += new Vector3(2f, 0f, 0f);
        var teleported = _motor.Step(8, 0, 2, checked((float)delta));
        Require(teleported.LeftFootHit == AlsFootHit.Invalid &&
                teleported.RightFootHit == AlsFootHit.Invalid &&
                !_exchange.HasRequests,
            "character teleport leaked a pre-discontinuity probe request");

    }

    private void ConfigureRemovalFixture()
    {
        var staticFloor = CreateSeamBody<StaticBody3D>(
            "RemovalStaticFloor",
            new Vector3(40f, -0.3f, 0f));
        staticFloor.GetChild<CollisionShape3D>(0).Shape =
            new BoxShape3D { Size = new Vector3(8f, 0.2f, 8f) };
        AddChild(staticFloor);

        _removalLeftPlatform = CreateSeamBody<AnimatableBody3D>(
            "RemovalLeftPlatform",
            new Vector3(39f, -0.1f, 0f));
        _removalLeftPlatform.GetChild<CollisionShape3D>(0).Shape =
            new BoxShape3D { Size = new Vector3(0.6f, 0.2f, 1f) };
        AddChild(_removalLeftPlatform);
        _removalRightPlatform = CreateSeamBody<AnimatableBody3D>(
            "RemovalRightPlatform",
            new Vector3(41f, -0.1f, 0f));
        _removalRightPlatform.GetChild<CollisionShape3D>(0).Shape =
            new BoxShape3D { Size = new Vector3(0.6f, 0.2f, 1f) };
        AddChild(_removalRightPlatform);

        _removalMotor = new AlsCharacterMotor
        {
            Name = "RemovalMotor",
            Position = new Vector3(40f, 0.9f, 0f),
        };
        AddChild(_removalMotor);
        _removalMotor.Configure(
            CreateMotorSettings(),
            new IdleCommandSource(),
            _removalExchange,
            AlsP4FootGatherSettings.CreateReference(),
            runtimeContext: null);
    }

    private void PrepareRealPlatformRemoval(double delta)
    {
        var first = _removalMotor.Step(1, 4, 1, checked((float)delta));
        var request = AlsFrameResult.CreateDefault(first.Identity);
        request.NextLeftFootProbeOrigin = new NumericsVector3(-1f, -0.77f, 0f);
        request.NextRightFootProbeOrigin = new NumericsVector3(1f, -0.77f, 0f);
        Require(AlsP3CommitStage.TryCopyFootProbeRequests(
                _removalExchange, first.Identity, request),
            "removal fixture failed to stage separate foot probes");

        var captured = _removalMotor.Step(2, 4, 1, checked((float)delta));
        Require(captured.LeftFootHit.PlatformId >= 0 &&
                captured.RightFootHit.PlatformId >= 0 &&
                captured.LeftFootHit.ColliderId != captured.RightFootHit.ColliderId,
            "removal fixture did not capture two independent platform identities");
        _removedLeftPlatformId = captured.LeftFootHit.PlatformId;
        _removedLeftColliderId = captured.LeftFootHit.ColliderId;
        _retainedRightPlatformId = captured.RightFootHit.PlatformId;
        _retainedRightColliderId = captured.RightFootHit.ColliderId;

        var nextRequest = AlsFrameResult.CreateDefault(captured.Identity);
        nextRequest.NextLeftFootProbeOrigin = new NumericsVector3(-1f, -0.77f, 0f);
        nextRequest.NextRightFootProbeOrigin = new NumericsVector3(0f, -0.77f, 0f);
        Require(AlsP3CommitStage.TryCopyFootProbeRequests(
                _removalExchange, captured.Identity, nextRequest),
            "removal fixture failed to stage N+1 probes");
        _removalLeftPlatform.QueueFree();
    }

    private void ValidateRealPlatformRemoval(double delta)
    {
        Require(!GodotObject.IsInstanceValid(_removalLeftPlatform),
            "queued platform was still valid at removal Gather N+1");
        Require(GodotObject.IsInstanceValid(_removalRightPlatform),
            "normal step-off control platform was unexpectedly removed");

        var input = _removalMotor.Step(3, 4, 1, checked((float)delta));
        var signals = input.FootPlacementReleaseSignals;
        Require(signals.LeftPlatformRemoved == 1 &&
                signals.LeftPlatformId == _removedLeftPlatformId &&
                signals.LeftColliderId == _removedLeftColliderId,
            "Main Gather did not publish the removed left platform identity");
        Require(signals.RightPlatformRemoved == 0 &&
                signals.RightPlatformId == -1 &&
                signals.RightColliderId == -1,
            "normal right-foot step-off was misclassified as platform removal");
        Require(input.RightFootHit.Valid == 1 &&
                input.RightFootHit.PlatformId == -1 &&
                input.RightFootHit.ColliderId >= 0 &&
                _retainedRightPlatformId >= 0 &&
                _retainedRightColliderId >= 0,
            "normal step-off control did not retain a live old platform and hit static ground");

        GD.Print("P4_FOOT_GATHER_OK latency=1 removal_identity=1 step_off=1");
        _finished = true;
        GetTree().Quit();
    }

    private void ValidateStationaryPlatformTransition(double delta)
    {
        var input = default(AlsFrameInput);
        for (var frame = 1; frame <= 60; frame++)
        {
            input = _stationaryTransitionMotor.Step(
                frame,
                2,
                1,
                checked((float)delta));
        }
        Require(_stationaryTransitionMotor.GlobalPosition.X > 20.3f &&
                input.Floor.IsGrounded == 1,
            "stationary platform transition fixture did not cross the static/platform boundary");
        Require(input.Floor.PlatformId ==
                AlsCharacterMotor.CreatePlatformId(_stationaryPlatform.GetInstanceId()) &&
                input.Floor.ColliderId == checked((long)_stationaryPlatform.GetInstanceId()),
            "stationary Animatable platform was hidden by stale static-floor evidence");
        _stationaryTransitionSource.Enabled = false;
        input = _stationaryTransitionMotor.Step(61, 2, 1, checked((float)delta));
        Require(input.Floor.PlatformId ==
                AlsCharacterMotor.CreatePlatformId(_stationaryPlatform.GetInstanceId()) &&
                input.Floor.ColliderId == checked((long)_stationaryPlatform.GetInstanceId()),
            "stationary Animatable platform identity was lost after entering steady state");
        Require(_stationaryTransitionMotor.LastFootGatherManagedAllocations > 0,
            "Godot GetSlideCollision wrapper allocation was incorrectly reported as zero bytes");
        GD.Print(
            "P4_FOOT_GATHER_SLIDE_ALLOC bytes=" +
            _stationaryTransitionMotor.LastFootGatherManagedAllocations);
    }

    private void ValidateSeamMovementBase(double delta)
    {
        var input = _seamMotor.Step(1, 1, 1, checked((float)delta));
        var sawStatic = false;
        var sawMoving = false;
        var slideContactCount = 0;
        var movingContactIndex = -1;
        for (var slideIndex = 0; slideIndex < _seamMotor.GetSlideCollisionCount(); slideIndex++)
        {
            var collision = _seamMotor.GetSlideCollision(slideIndex);
            for (var collisionIndex = 0;
                 collisionIndex < collision.GetCollisionCount();
                 collisionIndex++)
            {
                var collider = collision.GetCollider(collisionIndex);
                sawStatic |= collider == _seamStatic;
                if (collider == _seamPlatform)
                {
                    sawMoving = true;
                    movingContactIndex = slideContactCount;
                }
                slideContactCount++;
            }
        }
        using var supportProbe = new KinematicCollision3D();
        Require(_manyContactMotor.TestMove(
                _manyContactMotor.GlobalTransform,
                Vector3.Down * _manyContactMotor.FloorSnapLength,
                supportProbe,
                _manyContactMotor.SafeMargin,
                recoveryAsCollision: true,
                maxCollisions: 4),
            "four-contact control probe did not find floor support candidates");
        var limitedProbeCount = supportProbe.GetCollisionCount();
        var limitedPlatformIndex = FindColliderIndex(supportProbe, _manyContactPlatform);
        Require(limitedProbeCount == 4 && limitedPlatformIndex < 0,
            "old four-contact production limit did not reproduce candidate truncation: " +
            $"count={limitedProbeCount} platform_index={limitedPlatformIndex}");

        Require(AlsCharacterMotor.MaximumFloorSupportCollisions >= 7,
            "production floor support capacity cannot observe the seventh candidate");
        Require(_manyContactMotor.TestMove(
                _manyContactMotor.GlobalTransform,
                Vector3.Down * _manyContactMotor.FloorSnapLength,
                supportProbe,
                _manyContactMotor.SafeMargin,
                recoveryAsCollision: true,
                maxCollisions: AlsCharacterMotor.MaximumFloorSupportCollisions),
            "many-contact seam probe did not find floor support candidates");
        var platformCandidateIndex = FindColliderIndex(supportProbe, _manyContactPlatform);
        Require(supportProbe.GetCollisionCount() > 4 && platformCandidateIndex >= 4,
            "many-contact seam did not place the actual platform beyond the first four " +
            $"candidates: count={supportProbe.GetCollisionCount()} " +
            $"platform_index={platformCandidateIndex}");
        Require(_manyContactMotor.TryFindFloorColliderInSupportProbe(
                supportProbe,
                Vector3.Up,
                Vector3.Zero,
                Vector3.Zero,
                out var selectedSupport) && selectedSupport == _manyContactPlatform,
            "production TestMove candidate selection did not consume the platform beyond " +
            $"the first four candidates: count={supportProbe.GetCollisionCount()} " +
            $"platform_index={platformCandidateIndex}");
        Require(sawStatic && sawMoving && movingContactIndex >= 0,
            "seam fixture did not expose both actual floor collisions: " +
            $"static={sawStatic} moving={sawMoving} count={supportProbe.GetCollisionCount()} " +
            $"platform_index={platformCandidateIndex}");
        Require(_seamMotor.GetPlatformVelocity().DistanceTo(SeamPlatformVelocity) < 0.0001f,
            "seam fixture did not make the moving body CharacterBody's actual platform: " +
            $"actual={_seamMotor.GetPlatformVelocity()} count={supportProbe.GetCollisionCount()} " +
            $"platform_index={platformCandidateIndex}");
        Require(input.Floor.PlatformId ==
                AlsCharacterMotor.CreatePlatformId(_seamPlatform.GetInstanceId()) &&
                input.Floor.ColliderId == checked((long)_seamPlatform.GetInstanceId()),
            "floor seam selection did not follow CharacterBody's actual moving platform");
        Require(_seamMotor.LastFloorSelectionUsedSlideEvidence,
            "floor seam selection ignored this frame's MoveAndSlide collision evidence");
        GD.Print(
            $"P4_FOOT_GATHER_MANY_CONTACT_OK slide_count={slideContactCount} " +
            $"actual_moving_index={movingContactIndex} " +
            $"limited_probe_count={limitedProbeCount} " +
            $"limited_platform_index={limitedPlatformIndex} " +
            $"probe_count={supportProbe.GetCollisionCount()} " +
            $"probe_platform_index={platformCandidateIndex}");
    }

    private static int FindColliderIndex(
        KinematicCollision3D collision,
        CollisionObject3D expectedCollider)
    {
        for (var collisionIndex = 0;
             collisionIndex < collision.GetCollisionCount();
             collisionIndex++)
        {
            if (collision.GetCollider(collisionIndex) == expectedCollider)
            {
                return collisionIndex;
            }
        }
        return -1;
    }

    private static T CreateSeamBody<T>(string name, in Vector3 position)
        where T : StaticBody3D, new()
    {
        var body = new T
        {
            Name = name,
            Position = position,
            CollisionLayer = 1,
            CollisionMask = 1,
        };
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(2.4f, 0.2f, 4f) },
        });
        return body;
    }

    private void ValidateRigidPointVelocity(in AlsFootHit hit)
    {
        Require(hit.Valid == 1 &&
                hit.ColliderId == checked((long)_rigidPlatform.GetInstanceId()),
            "offset-COM rigid probe did not hit the RigidBody platform");
        var directState = PhysicsServer3D.BodyGetDirectState(_rigidPlatform.GetRid());
        Require(directState is not null,
            "RigidBody direct state was unavailable in the Main physics callback");
        var worldPoint = new Vector3(hit.Position.X, hit.Position.Y, hit.Position.Z);
        var worldCenterOfMass = directState!.Transform * directState.CenterOfMassLocal;
        var expected = directState.LinearVelocity +
                       directState.AngularVelocity.Cross(worldPoint - worldCenterOfMass);
        var oldGlobalOrigin = _rigidPlatform.LinearVelocity +
                              _rigidPlatform.AngularVelocity.Cross(
                                  worldPoint - _rigidPlatform.GlobalPosition);
        Require(expected.DistanceTo(oldGlobalOrigin) > 0.1f,
            "offset-COM fixture did not distinguish world COM from GlobalPosition");
        Require(IsApprox(hit.PointVelocity, expected),
            "RigidBody point velocity did not rotate around the physics world COM");
    }

    private void ValidateHit(
        in AlsFootHit hit,
        in NumericsVector3 localOrigin,
        in Transform3D expectedPlatformTransform)
    {
        Require(hit.Valid == 1 && hit.Walkable == 1,
            "Gather N+1 did not publish a walkable hit");
        var expectedRayOrigin = _motor.GlobalTransform * new Vector3(
            localOrigin.X,
            localOrigin.Y,
            localOrigin.Z);
        Require(Mathf.IsEqualApprox(hit.Position.X, expectedRayOrigin.X) &&
                Mathf.IsEqualApprox(hit.Position.Z, expectedRayOrigin.Z),
            "Gather did not transform the old pose-derived local request with the current character transform");
        Require(hit.ColliderId == checked((long)_platform.GetInstanceId()) &&
                hit.PlatformId == AlsCharacterMotor.CreatePlatformId(
                    _platform.GetInstanceId()),
            "Gather did not publish the hit collider identity");
        Require(IsApprox(hit.PlatformPosition, expectedPlatformTransform.Origin),
            "Gather did not publish the current platform position");
        var expectedRotation = expectedPlatformTransform.Basis.Orthonormalized()
            .GetRotationQuaternion().Normalized();
        Require(IsApprox(hit.PlatformRotation, expectedRotation),
            "Gather did not publish the current normalized platform rotation");

        var expectedVelocity = _platform.ConstantLinearVelocity +
                               _platform.ConstantAngularVelocity.Cross(
                                   new Vector3(hit.Position.X, hit.Position.Y, hit.Position.Z) -
                                   expectedPlatformTransform.Origin);
        Require(IsApprox(hit.PointVelocity, expectedVelocity),
            "Gather did not publish platform point velocity");
    }

    private static bool IsApprox(in NumericsVector3 actual, in Vector3 expected) =>
        Mathf.IsEqualApprox(actual.X, expected.X) &&
        Mathf.IsEqualApprox(actual.Y, expected.Y) &&
        Mathf.IsEqualApprox(actual.Z, expected.Z);

    private static bool IsApprox(in System.Numerics.Quaternion actual, in Quaternion expected) =>
        Mathf.IsEqualApprox(actual.X, expected.X) &&
        Mathf.IsEqualApprox(actual.Y, expected.Y) &&
        Mathf.IsEqualApprox(actual.Z, expected.Z) &&
        Mathf.IsEqualApprox(actual.W, expected.W);

    private static AlsMotorSettings CreateMotorSettings()
    {
        var walking = new AlsDirectionalSpeeds(1.75f, 1.5f, 1.25f);
        var running = new AlsDirectionalSpeeds(3.75f, 3.25f, 3f);
        var sprinting = new AlsDirectionalSpeeds(6.5f, 5f, 4f);
        var standing = new AlsStanceSpeeds(walking, running, sprinting);
        return new AlsMotorSettings(
            0.35f,
            1.8f,
            1.2f,
            standing,
            standing,
            20f,
            12.5f,
            9.81f,
            5f,
            1);
    }

    private static void ValidateMainOwnershipSource()
    {
        string[] mainOnlyFiles =
        [
            "res://src/Als.Godot/Locomotion/AlsCharacterMotor.cs",
            "res://src/Als.Godot/Locomotion/AlsP3CommitStage.cs",
        ];
        string[] forbiddenWorkerAccess =
        [
            "Skeleton3D",
            "AnimationTree",
            "FindChild",
            "VisualWorker",
        ];
        foreach (var resourcePath in mainOnlyFiles)
        {
            var source = File.ReadAllText(ProjectSettings.GlobalizePath(resourcePath));
            foreach (var token in forbiddenWorkerAccess)
            {
                Require(!source.Contains(token, StringComparison.Ordinal),
                    $"Main Gather/Commit source accessed Worker-owned token {token}");
            }
        }

        var motorSource = File.ReadAllText(ProjectSettings.GlobalizePath(mainOnlyFiles[0]));
        Require(!motorSource.Contains("maxCollisions: 4", StringComparison.Ordinal) &&
                motorSource.Split(
                    "maxCollisions: MaximumFloorSupportCollisions",
                    StringSplitOptions.None).Length - 1 == 2,
            "floor support and initial-floor probes do not share the 32-contact capacity");
    }

    private void Fail(string code, Exception exception)
    {
        if (_finished)
        {
            return;
        }
        _finished = true;
        GD.PushError($"GODOT_ALS_P4_FOOT_GATHER_FAIL code={code} {exception}");
        GetTree().Quit(1);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class IdleCommandSource : IAlsLocomotionCommandSource
    {
        public AlsLocomotionCommand GetCommand(long frameId) =>
            AlsLocomotionCommand.CreateDefault();
    }

    private sealed class RightCommandSource : IAlsLocomotionCommandSource
    {
        public bool Enabled { get; set; } = true;

        public AlsLocomotionCommand GetCommand(long frameId) =>
            !Enabled
                ? AlsLocomotionCommand.CreateDefault()
                : AlsLocomotionCommand.CreateDefault() with
            {
                MovementAxes = NumericsVector2.UnitX,
                RequestedGait = AlsGait.Walking,
            };
    }
}
