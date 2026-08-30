using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Exchange;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NumericsVector3 = System.Numerics.Vector3;

namespace GodotAls.Locomotion;

public partial class P4FootPlacementSmoke : Node
{
    private const string ProfilePath = "res://assets/config/p3_locomotion_profile.json";
    private const int NormalLaneCount = 8;
    private const int LaneCount = 10;
    private const long LastFrame = 140;
    private static readonly Vector3 TranslationVelocity = new(0.12f, 0f, 0.04f);
    private static readonly Vector3 RotationVelocity = new(0f, 0.25f, 0f);

    private readonly AlsP3Character[] _characters = new AlsP3Character[LaneCount];
    private readonly long[] _lastFrames = new long[LaneCount];
    private readonly NumericsVector3[] _previousLeftLocal = new NumericsVector3[LaneCount];
    private readonly NumericsVector3[] _previousRightLocal = new NumericsVector3[LaneCount];
    private readonly bool[] _hasPreviousProbe = new bool[LaneCount];
    private readonly float[] _previousPelvisY = new float[LaneCount];
    private readonly bool[] _hasPelvis = new bool[LaneCount];
    private readonly NumericsVector3[] _lockedLeftLocalTarget = new NumericsVector3[LaneCount];
    private readonly NumericsVector3[] _lockedRightLocalTarget = new NumericsVector3[LaneCount];
    private readonly float[] _maximumLeftLock = new float[LaneCount];
    private readonly float[] _maximumRightLock = new float[LaneCount];
    private readonly int[] _leftLockSamples = new int[LaneCount];
    private readonly int[] _rightLockSamples = new int[LaneCount];
    private readonly System.Numerics.Quaternion[] _lockedLeftLocalRotation =
        new System.Numerics.Quaternion[LaneCount];
    private readonly System.Numerics.Quaternion[] _lockedRightLocalRotation =
        new System.Numerics.Quaternion[LaneCount];
    private readonly bool[] _hasRotationSample = new bool[LaneCount];
    private readonly bool[] _physicalPelvisObserved = new bool[LaneCount];
    private readonly bool[] _platformTransformRefreshObserved = new bool[LaneCount];
    private readonly bool[] _laneObserved = new bool[LaneCount];
    private AlsHarnessMode _mode;
    private AlsP3RuntimeContext _context = null!;
    private AnimatableBody3D _translatingPlatform = null!;
    private AnimatableBody3D _rotatingPlatform = null!;
    private AnimatableBody3D _basePlatform = null!;
    private AnimatableBody3D _baseReplacementPlatform = null!;
    private AnimatableBody3D _teleportPlatform = null!;
    private JumpAfterLockCommandSource _jumpSource = null!;
    private bool _translationMotionStarted;
    private bool _rotationMotionStarted;
    private bool _jumpRequested;
    private bool _baseChanged;
    private bool _platformTeleported;
    private readonly AlsP3FrameDiagnostics[] _rollbackCheckpoints =
        new AlsP3FrameDiagnostics[2];
    private readonly long[] _rollbackPublishedFrames = new long[2];
    private int _rollbackPhase;
    private int _rollbackCount;
    private bool _finished;

    public override void _Ready()
    {
        try
        {
            VerifySnapshotCompatibilityShape();
            _mode = ReadMode();
            ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
            ProcessThreadGroupOrder = 4;
            var resource = ResourceLoader.Load<AlsAnimationSetResource>(
                AlsGodotImportCoordinator.CompiledResourcePath)
                ?? throw new InvalidOperationException("P4 foot placement animation set is missing.");
            var animationSet = resource.LoadDefinition();
            var profile = AlsLocomotionProfileCompiler.Compile(
                Godot.FileAccess.GetFileAsString(ProfilePath), animationSet);
            var settings = AlsLocomotionSettings.Load(
                Godot.FileAccess.GetFileAsString(
                    "res://assets/config/p3_locomotion_settings.json"));
            var motorSettings = new AlsMotorSettings(
                0.35f,
                settings.StandingHalfHeight * 2f,
                settings.CrouchedHalfHeight * 2f,
                settings.Standing,
                settings.Crouching,
                settings.InitialMaxAcceleration,
                settings.InitialMaxBrakingDeceleration,
                settings.Gravity,
                settings.JumpSpeed,
                1,
                settings.VelocityAngleInterpolationStart,
                settings.VelocityAngleInterpolationEnd);
            _context = new AlsP3RuntimeContext(
                _mode,
                settings,
                motorSettings,
                animationSet,
                profile,
                System.Environment.CurrentManagedThreadId,
                headlessOrDebug: false);

            CreateFixtures();
            _jumpSource = new JumpAfterLockCommandSource();
            for (var lane = 0; lane < LaneCount; lane++)
            {
                var character = new AlsP3Character { Name = $"FootLane_{lane}" };
                AddChild(character);
                character.Position = new Vector3(
                    lane * 8f,
                    motorSettings.StandingHeight * 0.5f,
                    0f);
                character.Configure(
                    _context,
                    new AlsSlotHandle(checked((uint)lane), 1),
                    lane == 5
                        ? _jumpSource
                        : lane is 3 or 4 or 6 or 7
                            ? new TurnCommandSource()
                        : new IdleCommandSource());
                character.SetActive(true);
                _characters[lane] = character;
            }
            VerifyDiagnosticsMainThreadContract();
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
            for (var lane = 0; lane < LaneCount; lane++)
            {
                var frame = _characters[lane].Diagnostics;
                if (frame.CommittedFrameId <= _lastFrames[lane])
                {
                    continue;
                }
                ValidateFrame(lane, in frame);
                _lastFrames[lane] = frame.CommittedFrameId;
            }
            ValidateWorkerRollbacks();

            if (!_translationMotionStarted &&
                MathF.Max(_maximumLeftLock[3], _maximumRightLock[3]) > 0.05f)
            {
                _translationMotionStarted = true;
                _translatingPlatform.ConstantLinearVelocity = TranslationVelocity;
            }
            if (!_rotationMotionStarted &&
                MathF.Max(_maximumLeftLock[4], _maximumRightLock[4]) > 0.05f)
            {
                _rotationMotionStarted = true;
                _rotatingPlatform.ConstantAngularVelocity = RotationVelocity;
            }
            if (_translationMotionStarted)
            {
                _translatingPlatform.Position += TranslationVelocity * (float)delta;
            }
            if (_rotationMotionStarted)
            {
                _rotatingPlatform.RotateY(RotationVelocity.Y * (float)delta);
            }

            if (_lastFrames.Take(NormalLaneCount).All(frame => frame >= LastFrame) &&
                _rollbackCount == 2)
            {
                Finish();
            }
        }
        catch (Exception exception)
        {
            Fail("runtime", exception);
        }
    }

    private void ValidateWorkerRollbacks()
    {
        if (_rollbackPhase is 0 or 2)
        {
            var rollbackIndex = _rollbackPhase / 2;
            var lane = NormalLaneCount + rollbackIndex;
            var character = _characters[lane];
            var checkpoint = character.Diagnostics;
            if (checkpoint.CommittedFrameId < 12 || character.WorkerInFlight != 0)
            {
                return;
            }
            _rollbackCheckpoints[rollbackIndex] = checkpoint;
            _rollbackPublishedFrames[rollbackIndex] = character.ResultPublishedFrameId;
            var targetIdentity = new AlsFrameIdentity(
                character.PublishedFrameId + 1,
                character.Handle.CharacterId,
                character.Handle.Generation);
            _context.ArmWorkerFailureInjection(
                rollbackIndex == 0
                    ? AlsP3WorkerFailureInjectionStage.ModifierAfterPelvis
                    : AlsP3WorkerFailureInjectionStage.ModifierAfterLeftFoot,
                in targetIdentity);
            _rollbackPhase++;
            return;
        }

        if (_rollbackPhase is not (1 or 3))
        {
            return;
        }
        var index = (_rollbackPhase - 1) / 2;
        var rollbackLane = NormalLaneCount + index;
        var rollbackCharacter = _characters[rollbackLane];
        if (rollbackCharacter.FailureDiagnosticCount == 0)
        {
            return;
        }
        var expected = _rollbackCheckpoints[index];
        var transaction = rollbackCharacter.WorkerTransactionRollbackDiagnostics;
        var pose = rollbackCharacter.RuntimeDiagnostics;
        Require(rollbackCharacter.IsPoseFrozen &&
                rollbackCharacter.Diagnostics.CommittedFrameId == expected.CommittedFrameId &&
                rollbackCharacter.ResultPublishedFrameId == _rollbackPublishedFrames[index],
            $"rollback lane {rollbackLane} published failed frame state");
        Require(transaction.Identity.CharacterId == rollbackCharacter.Handle.CharacterId &&
                transaction.Identity.SlotGeneration == rollbackCharacter.Handle.Generation &&
                transaction.Identity.FrameId > expected.CommittedFrameId &&
                transaction.RuntimeStateRestored &&
                transaction.FrameResultRestored &&
                transaction.ControllerRestored &&
                transaction.P4BanksRestored,
            $"rollback lane {rollbackLane} leaked runtime/controller state");
        Require(pose.RollbackVerified &&
                pose.RollbackFullPoseDigest == expected.FullPoseDigest &&
                pose.RollbackRootDigest == expected.RootDigest,
            $"rollback lane {rollbackLane} did not restore exact pose/root");
        _laneObserved[rollbackLane] = true;
        _rollbackCount++;
        _rollbackPhase++;
    }

    private void ValidateFrame(int lane, in AlsP3FrameDiagnostics frame)
    {
        Require(frame.FootPose.Identity == frame.Identity,
            $"lane {lane} foot-pose identity lagged commit");
        var input = _characters[lane].LatestMotorInput;
        if (_hasPreviousProbe[lane])
        {
            var expectedLeft = NumericsVector3.Transform(
                _previousLeftLocal[lane], input.CharacterTransform);
            var expectedRight = NumericsVector3.Transform(
                _previousRightLocal[lane], input.CharacterTransform);
            Require(NumericsVector3.Distance(
                        expectedLeft, frame.FootPose.LeftProbeWorldOrigin) <= 0.002f &&
                    NumericsVector3.Distance(
                        expectedRight, frame.FootPose.RightProbeWorldOrigin) <= 0.002f,
                $"lane {lane} did not consume committed N probes at Gather N+1");
        }
        _previousLeftLocal[lane] = frame.Result.NextLeftFootProbeOrigin;
        _previousRightLocal[lane] = frame.Result.NextRightFootProbeOrigin;
        _hasPreviousProbe[lane] = true;

        var pelvisY = frame.Result.PelvisOffset.Y;
        if (_hasPelvis[lane])
        {
            Require(MathF.Abs(pelvisY - _previousPelvisY[lane]) <= 0.08f,
                $"lane {lane} pelvis correction was discontinuous");
        }
        _previousPelvisY[lane] = pelvisY;
        _hasPelvis[lane] = true;

        Require(frame.FootPose.AnimationAdvanceCount == 1 &&
                frame.FootPose.ModifierWriteTransactionCount == 1,
            $"lane {lane} violated one-advance/one-write frame contract: " +
            $"advance={frame.FootPose.AnimationAdvanceCount} " +
            $"writes={frame.FootPose.ModifierWriteTransactionCount}");
        var hasFootInfluence = frame.Result.LeftFootIkWeight > 0f ||
            frame.Result.RightFootIkWeight > 0f ||
            frame.Result.PelvisOffset != NumericsVector3.Zero;
        if (hasFootInfluence)
        {
            var expectedPropagation = 7 +
                (frame.Result.LeftFootIkWeight > 0f ? 6 : 0) +
                (frame.Result.RightFootIkWeight > 0f ? 6 : 0);
            Require(frame.FootPose.ModifierFootChainRebuildCount == 1 &&
                    frame.FootPose.ModifierFootFullSkeletonRebuildCount == 0 &&
                    frame.FootPose.ModifierFootComponentPropagationCount ==
                    expectedPropagation,
                $"lane {lane} foot modifier traversal mismatch: " +
                $"chain={frame.FootPose.ModifierFootChainRebuildCount} " +
                $"full={frame.FootPose.ModifierFootFullSkeletonRebuildCount} " +
                $"propagated={frame.FootPose.ModifierFootComponentPropagationCount} " +
                $"expected={expectedPropagation}");
            var appliedPelvisDelta = frame.FootPose.PelvisWorldPosition -
                frame.FootPose.UncorrectedPelvisWorldPosition;
            Require(NumericsVector3.Distance(
                        appliedPelvisDelta, frame.Result.PelvisOffset) <= 0.008f,
                $"lane {lane} physical pelvis did not apply authoritative correction: " +
                $"actual={appliedPelvisDelta} expected={frame.Result.PelvisOffset}");
            _physicalPelvisObserved[lane] |= frame.Result.PelvisOffset.Length() > 0.002f;
        }

        if (frame.CommittedFrameId < 4)
        {
            return;
        }
        var leftHit = frame.FootPose.LeftGatherHit;
        var rightHit = frame.FootPose.RightGatherHit;
        if (frame.Result.ResolvedLocomotionState == AlsLocomotionState.Grounded)
        {
            Require(leftHit.Valid == 1 && leftHit.Walkable == 1 &&
                    rightHit.Valid == 1 && rightHit.Walkable == 1,
                $"lane {lane} did not gather two walkable production hits: " +
                $"frame={frame.CommittedFrameId} left=({leftHit.Valid},{leftHit.Walkable}) " +
                $"right=({rightHit.Valid},{rightHit.Walkable}) " +
                $"origins=({frame.FootPose.LeftProbeWorldOrigin},{frame.FootPose.RightProbeWorldOrigin})");
            if (frame.CommittedFrameId >= 2)
            {
                Require(TargetTracksSurface(frame.Result.LeftFootPose.Position, in leftHit) &&
                        TargetTracksSurface(frame.Result.RightFootPose.Position, in rightHit),
                    $"lane {lane} foot targets diverged from gathered surfaces: " +
                    $"left={NumericsVector3.Distance(frame.Result.LeftFootPose.Position, leftHit.Position)} " +
                    $"right={NumericsVector3.Distance(frame.Result.RightFootPose.Position, rightHit.Position)} " +
                    $"target_l={frame.Result.LeftFootPose.Position} hit_l={leftHit.Position}");
                var leftPhysicalBudget = NumericsVector3.Distance(
                        frame.FootPose.UncorrectedLeftFootWorldPosition,
                        frame.FootPose.LeftPhysicalTargetWorldPosition) *
                    (1f - frame.Result.LeftFootIkWeight) + 0.025f;
                var rightPhysicalBudget = NumericsVector3.Distance(
                        frame.FootPose.UncorrectedRightFootWorldPosition,
                        frame.FootPose.RightPhysicalTargetWorldPosition) *
                    (1f - frame.Result.RightFootIkWeight) + 0.025f;
                Require(NumericsVector3.Distance(
                            frame.FootPose.LeftFootWorldPosition,
                            frame.FootPose.LeftPhysicalTargetWorldPosition) <= leftPhysicalBudget &&
                        NumericsVector3.Distance(
                            frame.FootPose.RightFootWorldPosition,
                            frame.FootPose.RightPhysicalTargetWorldPosition) <= rightPhysicalBudget,
                    $"lane {lane} physical feet missed authoritative world targets: " +
                    $"left={NumericsVector3.Distance(frame.FootPose.LeftFootWorldPosition, frame.FootPose.LeftPhysicalTargetWorldPosition)} " +
                    $"right={NumericsVector3.Distance(frame.FootPose.RightFootWorldPosition, frame.FootPose.RightPhysicalTargetWorldPosition)} " +
                    $"weights=({frame.Result.LeftFootIkWeight},{frame.Result.RightFootIkWeight}) " +
                    $"locks=({frame.Result.LeftFootPose.LockAmount},{frame.Result.RightFootPose.LockAmount}) " +
                    $"physical_l={frame.FootPose.LeftFootWorldPosition} target_l={frame.FootPose.LeftPhysicalTargetWorldPosition} " +
                    $"probe_l={frame.FootPose.LeftProbeWorldOrigin} " +
                    $"physical_r={frame.FootPose.RightFootWorldPosition} target_r={frame.FootPose.RightPhysicalTargetWorldPosition} " +
                    $"probe_r={frame.FootPose.RightProbeWorldOrigin}");
                Require(PhysicalFootTracksSurface(
                            frame.FootPose.LeftPhysicalTargetWorldPosition,
                            in leftHit) &&
                        PhysicalFootTracksSurface(
                            frame.FootPose.RightPhysicalTargetWorldPosition,
                            in rightHit) &&
                        PhysicalFootTracksSurface(
                            frame.FootPose.LeftFootWorldPosition,
                            in leftHit) &&
                        PhysicalFootTracksSurface(
                            frame.FootPose.RightFootWorldPosition,
                            in rightHit),
                    $"lane {lane} physical targets or bones lost surface contact geometry: " +
                    $"target_l={DescribeFootSurface(frame.FootPose.LeftPhysicalTargetWorldPosition, in leftHit)} " +
                    $"actual_l={DescribeFootSurface(frame.FootPose.LeftFootWorldPosition, in leftHit)} " +
                    $"target_r={DescribeFootSurface(frame.FootPose.RightPhysicalTargetWorldPosition, in rightHit)} " +
                    $"actual_r={DescribeFootSurface(frame.FootPose.RightFootWorldPosition, in rightHit)}");
                var leftUpError = DirectionAngle(
                    NumericsVector3.Transform(
                        NumericsVector3.UnitY,
                        frame.FootPose.LeftFootWorldRotation),
                    NumericsVector3.Transform(
                        NumericsVector3.UnitY,
                        frame.FootPose.LeftPhysicalTargetWorldRotation));
                var rightUpError = DirectionAngle(
                    NumericsVector3.Transform(
                        NumericsVector3.UnitY,
                        frame.FootPose.RightFootWorldRotation),
                    NumericsVector3.Transform(
                        NumericsVector3.UnitY,
                        frame.FootPose.RightPhysicalTargetWorldRotation));
                Require(leftUpError <= 0.12f && rightUpError <= 0.12f,
                    $"lane {lane} physical foot-up axes missed authoritative rotations: " +
                    $"left={leftUpError} right={rightUpError} " +
                    $"uncorrected_l={frame.FootPose.UncorrectedLeftFootWorldRotation} " +
                    $"actual_l={frame.FootPose.LeftFootWorldRotation} " +
                    $"target_l={frame.FootPose.LeftPhysicalTargetWorldRotation}");
                if (lane == 4 && HasActiveLock(frame.FootPose))
                {
                    _hasRotationSample[lane] = true;
                }
            }
        }

        switch (lane)
        {
            case 0:
                _laneObserved[lane] |= leftHit.Valid == 1 && rightHit.Valid == 1 &&
                    (frame.Result.LeftFootReleaseReason == AlsFootReleaseReason.NotGrounded ||
                     frame.Result.RightFootReleaseReason == AlsFootReleaseReason.NotGrounded ||
                     frame.CommittedFrameId < 90);
                break;
            case 1:
                _laneObserved[lane] |= MathF.Abs(leftHit.Normal.X) > 0.05f ||
                    MathF.Abs(rightHit.Normal.X) > 0.05f;
                break;
            case 2:
                _laneObserved[lane] |= MathF.Abs(leftHit.Position.Y - rightHit.Position.Y) > 0.08f;
                break;
            case 3:
                ValidatePlatformLane(lane, in frame, _translatingPlatform);
                break;
            case 4:
                ValidatePlatformLane(lane, in frame, _rotatingPlatform);
                break;
            case 5:
                ValidateJumpRelease(lane, in frame);
                break;
            case 6:
                ValidateBaseChangeRelease(lane, in frame);
                break;
            case 7:
                ValidateTeleportRelease(lane, in frame);
                break;
        }
    }

    private void ValidateJumpRelease(int lane, in AlsP3FrameDiagnostics frame)
    {
        if (!_jumpRequested && HasActiveLock(frame.FootPose))
        {
            _jumpRequested = true;
            _jumpSource.RequestJump();
        }
        _laneObserved[lane] |=
            frame.Result.LeftFootReleaseReason == AlsFootReleaseReason.NotGrounded ||
            frame.Result.RightFootReleaseReason == AlsFootReleaseReason.NotGrounded;
    }

    private void ValidateBaseChangeRelease(int lane, in AlsP3FrameDiagnostics frame)
    {
        if (!_baseChanged && HasActiveLock(frame.FootPose))
        {
            _baseChanged = true;
            _basePlatform.CollisionLayer = 0;
            _baseReplacementPlatform.CollisionLayer = 1;
        }
        _laneObserved[lane] |=
            frame.Result.LeftFootReleaseReason == AlsFootReleaseReason.BaseChanged ||
            frame.Result.RightFootReleaseReason == AlsFootReleaseReason.BaseChanged;
    }

    private void ValidateTeleportRelease(int lane, in AlsP3FrameDiagnostics frame)
    {
        if (!_platformTeleported && HasActiveLock(frame.FootPose))
        {
            _platformTeleported = true;
            _teleportPlatform.RotateY(MathF.PI / 3f);
        }
        _laneObserved[lane] |=
            frame.Result.LeftFootReleaseReason == AlsFootReleaseReason.Teleported ||
            frame.Result.RightFootReleaseReason == AlsFootReleaseReason.Teleported;
    }

    private static bool HasActiveLock(AlsP4FootPlacementPoseSnapshot pose) =>
        (pose.LeftFootLock.Locked == 1 && pose.LeftFootLock.Amount > 0.05f) ||
        (pose.RightFootLock.Locked == 1 && pose.RightFootLock.Amount > 0.05f);

    private void ValidatePlatformLane(
        int lane,
        in AlsP3FrameDiagnostics frame,
        AnimatableBody3D platform)
    {
        var expectedPlatformId = AlsCharacterMotor.CreatePlatformId(platform.GetInstanceId());
        var leftHit = frame.FootPose.LeftGatherHit;
        var rightHit = frame.FootPose.RightGatherHit;
        Require(leftHit.PlatformId == expectedPlatformId &&
                leftHit.ColliderId == checked((long)platform.GetInstanceId()) &&
                rightHit.PlatformId == expectedPlatformId &&
                rightHit.ColliderId == checked((long)platform.GetInstanceId()),
            $"lane {lane} lost full platform/collider identity");
        if ((lane == 3 && _translationMotionStarted) ||
            (lane == 4 && _rotationMotionStarted))
        {
            var currentPlatformPosition = ToNumerics(platform.GlobalPosition);
            var currentPlatformRotation = ToNumerics(platform.GlobalBasis
                .Orthonormalized().GetRotationQuaternion().Normalized());
            Require(NumericsVector3.Distance(
                        leftHit.PlatformPosition,
                        currentPlatformPosition) <= 0.002f &&
                    NumericsVector3.Distance(
                        rightHit.PlatformPosition,
                        currentPlatformPosition) <= 0.002f &&
                    QuaternionAngle(
                        leftHit.PlatformRotation,
                        currentPlatformRotation) <= 0.002f &&
                    QuaternionAngle(
                        rightHit.PlatformRotation,
                        currentPlatformRotation) <= 0.002f,
                $"lane {lane} Gather N+1 did not capture current platform transform: " +
                $"frame={frame.CommittedFrameId} expected_pos={currentPlatformPosition} " +
                $"left_pos={leftHit.PlatformPosition} right_pos={rightHit.PlatformPosition} " +
                $"expected_rot={currentPlatformRotation} " +
                $"left_rot={leftHit.PlatformRotation}");
            _platformTransformRefreshObserved[lane] = true;
        }
        _laneObserved[lane] = true;
        _maximumLeftLock[lane] = MathF.Max(
            _maximumLeftLock[lane], frame.Result.LeftFootPose.LockAmount);
        _maximumRightLock[lane] = MathF.Max(
            _maximumRightLock[lane], frame.Result.RightFootPose.LockAmount);
        var leftFootLock = frame.FootPose.LeftFootLock;
        var rightFootLock = frame.FootPose.RightFootLock;
        ValidateLockedTarget(
            lane,
            in leftFootLock,
            ref _lockedLeftLocalTarget[lane],
            ref _lockedLeftLocalRotation[lane],
            ref _leftLockSamples[lane]);
        ValidateLockedTarget(
            lane,
            in rightFootLock,
            ref _lockedRightLocalTarget[lane],
            ref _lockedRightLocalRotation[lane],
            ref _rightLockSamples[lane]);
    }

    private static void ValidateLockedTarget(
        int lane,
        in AlsFootLockState footLock,
        ref NumericsVector3 previousLocalTarget,
        ref System.Numerics.Quaternion previousLocalRotation,
        ref int sampleCount)
    {
        if (footLock.Locked == 0 || footLock.Amount <= 0.001f)
        {
            return;
        }
        var localTarget = footLock.LocalPosition;
        if (sampleCount > 0)
        {
            Require(NumericsVector3.Distance(
                        localTarget, previousLocalTarget) <= 0.035f,
                $"lane {lane} platform-local foot lock drifted: " +
                $"distance={NumericsVector3.Distance(localTarget, previousLocalTarget)} " +
                $"amount={footLock.Amount} previous={previousLocalTarget} current={localTarget}");
            Require(QuaternionAngle(
                        footLock.LocalRotation, previousLocalRotation) <= 0.035f,
                $"lane {lane} platform-local foot-lock rotation drifted");
        }
        previousLocalTarget = localTarget;
        previousLocalRotation = footLock.LocalRotation;
        sampleCount++;
    }

    private void CreateFixtures()
    {
        AddChild(CreateBox<StaticBody3D>(
            "Flat", new Vector3(0f, -0.15f, 0f), new Vector3(6f, 0.3f, 6f)));
        var slope = CreateBox<StaticBody3D>(
            "Slope", new Vector3(8f, -0.15f, 0f), new Vector3(6f, 0.3f, 6f));
        slope.Rotation = new Vector3(0f, 0f, 0.14f);
        AddChild(slope);

        AddChild(CreateBox<StaticBody3D>(
            "StairLower", new Vector3(16f, -0.15f, 0f), new Vector3(6f, 0.3f, 6f)));
        AddChild(CreateBox<StaticBody3D>(
            "StairUpper", new Vector3(15.25f, 0f, 0f), new Vector3(1.4f, 0.2f, 6f)));

        _translatingPlatform = CreateBox<AnimatableBody3D>(
            "TranslatingPlatform", new Vector3(24f, -0.15f, 0f), new Vector3(6f, 0.3f, 6f));
        AddChild(_translatingPlatform);

        _rotatingPlatform = CreateBox<AnimatableBody3D>(
            "RotatingPlatform", new Vector3(32f, -0.15f, 0f), new Vector3(6f, 0.3f, 6f));
        AddChild(_rotatingPlatform);

        AddChild(CreateBox<StaticBody3D>(
            "JumpFlat", new Vector3(40f, -0.15f, 0f), new Vector3(6f, 0.3f, 6f)));

        _basePlatform = CreateBox<AnimatableBody3D>(
            "BasePlatform", new Vector3(48f, -0.15f, 0f), new Vector3(6f, 0.3f, 6f));
        AddChild(_basePlatform);
        _baseReplacementPlatform = CreateBox<AnimatableBody3D>(
            "BaseReplacementPlatform", new Vector3(48f, -0.15f, 0f), new Vector3(6f, 0.3f, 6f));
        _baseReplacementPlatform.CollisionLayer = 0;
        AddChild(_baseReplacementPlatform);

        _teleportPlatform = CreateBox<AnimatableBody3D>(
            "TeleportPlatform", new Vector3(56f, -0.15f, 0f), new Vector3(6f, 0.3f, 6f));
        AddChild(_teleportPlatform);

        AddChild(CreateBox<StaticBody3D>(
            "RollbackPelvisFlat", new Vector3(64f, -0.15f, 0f),
            new Vector3(6f, 0.3f, 6f)));
        AddChild(CreateBox<StaticBody3D>(
            "RollbackLeftFootFlat", new Vector3(72f, -0.15f, 0f),
            new Vector3(6f, 0.3f, 6f)));
    }

    private static T CreateBox<T>(
        string name,
        in Vector3 position,
        in Vector3 size)
        where T : StaticBody3D, new()
    {
        var body = new T
        {
            Name = name,
            Position = position,
            CollisionLayer = 1,
            CollisionMask = 1,
        };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        return body;
    }

    private void Finish()
    {
        Require(_laneObserved.All(value => value),
            $"one or more foot-placement fixture lanes produced no evidence: " +
            $"[{string.Join(',', _laneObserved.Select(value => value ? 1 : 0))}]");
        Require(_translationMotionStarted && _rotationMotionStarted &&
                Math.Max(_leftLockSamples[3], _rightLockSamples[3]) >= 2 &&
                Math.Max(_leftLockSamples[4], _rightLockSamples[4]) >= 2,
            $"moving-platform foot lock never became active: " +
            $"translate=({_maximumLeftLock[3]},{_maximumRightLock[3]};" +
            $"{_leftLockSamples[3]},{_rightLockSamples[3]}) " +
            $"rotate=({_maximumLeftLock[4]},{_maximumRightLock[4]};" +
            $"{_leftLockSamples[4]},{_rightLockSamples[4]})");
        Require(_jumpRequested && _baseChanged && _platformTeleported,
            "one or more release fixtures never triggered their event");
        Require(_rollbackCount == 2 && _physicalPelvisObserved.Any(value => value) &&
                _hasRotationSample[4] &&
                _platformTransformRefreshObserved[3] &&
                _platformTransformRefreshObserved[4],
            $"physical pelvis/rotation/rollback evidence incomplete: " +
            $"pelvis={(_physicalPelvisObserved.Any(value => value) ? 1 : 0)} " +
            $"rotation={(_hasRotationSample[4] ? 1 : 0)} " +
            $"platform=({(_platformTransformRefreshObserved[3] ? 1 : 0)}," +
            $"{(_platformTransformRefreshObserved[4] ? 1 : 0)}) rollback={_rollbackCount}");
        GD.Print(
            $"P4_FOOT_PLACEMENT_OK mode={_mode.ToString().ToLowerInvariant()} " +
            "flat=1 slope=1 stairs=1 translate=1 rotate=1 " +
            $"jump=1 base=1 teleport=1 rollback={_rollbackCount}");
        foreach (var character in _characters)
        {
            Require(character.WorkerInFlight == 0,
                "foot-placement teardown raced a Worker callback");
            character.SetActive(false);
            character.DisposeRuntime();
        }
        _finished = true;
        GetTree().Quit();
    }

    private static AlsHarnessMode ReadMode()
    {
        foreach (var argument in OS.GetCmdlineUserArgs())
        {
            if (argument == "--als-mode=parallel") return AlsHarnessMode.Parallel;
            if (argument == "--als-mode=single") return AlsHarnessMode.Single;
        }
        return AlsHarnessMode.Single;
    }

    private static void VerifySnapshotCompatibilityShape()
    {
        var snapshot = default(AlsP4FootPlacementPoseSnapshot);
        snapshot.Deconstruct(
            out _, out _, out _, out _, out _, out _,
            out _, out _, out _, out _, out _, out _);
    }

    private void VerifyDiagnosticsMainThreadContract()
    {
        var rejected = Task.Run(() =>
        {
            try
            {
                _ = _characters[0].Diagnostics;
                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }).GetAwaiter().GetResult();
        Require(rejected,
            "P4 diagnostics allowed a torn value-type snapshot off the main thread");
    }

    private void Fail(string code, Exception exception)
    {
        if (_finished) return;
        _finished = true;
        GD.PushError($"GODOT_ALS_P4_FOOT_PLACEMENT_FAIL code={code} {exception}");
        GetTree().Quit(1);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static bool TargetTracksSurface(
        in NumericsVector3 target,
        in AlsFootHit hit)
    {
        var delta = target - hit.Position;
        return delta.Length() <= 0.5f &&
               MathF.Abs(NumericsVector3.Dot(delta, hit.Normal)) <= 0.2f;
    }

    private static bool PhysicalFootTracksSurface(
        in NumericsVector3 foot,
        in AlsFootHit hit)
    {
        var delta = foot - hit.Position;
        var normalDistance = NumericsVector3.Dot(delta, hit.Normal);
        // A locked point on a moving platform intentionally separates tangentially
        // from the next probe ray. TargetTracksSurface already bounds the Core target
        // to the fixture; physical contact is the independent plane-normal contract.
        return normalDistance >= 0.07f && normalDistance <= 0.2f;
    }

    private static string DescribeFootSurface(
        in NumericsVector3 foot,
        in AlsFootHit hit)
    {
        var delta = foot - hit.Position;
        var normalDistance = NumericsVector3.Dot(delta, hit.Normal);
        var tangentDistance = (delta - hit.Normal * normalDistance).Length();
        return $"normal={normalDistance} tangent={tangentDistance}";
    }

    private static float QuaternionAngle(
        in System.Numerics.Quaternion left,
        in System.Numerics.Quaternion right)
    {
        var dot = MathF.Abs(System.Numerics.Quaternion.Dot(left, right));
        return 2f * MathF.Acos(Math.Clamp(dot, 0f, 1f));
    }

    private static float DirectionAngle(
        in NumericsVector3 left,
        in NumericsVector3 right)
    {
        var dot = NumericsVector3.Dot(
            NumericsVector3.Normalize(left),
            NumericsVector3.Normalize(right));
        return MathF.Acos(Math.Clamp(dot, -1f, 1f));
    }

    private static NumericsVector3 ToNumerics(in Vector3 value) =>
        new(value.X, value.Y, value.Z);

    private static System.Numerics.Quaternion ToNumerics(in Quaternion value) =>
        new(value.X, value.Y, value.Z, value.W);

    private sealed class IdleCommandSource : IAlsLocomotionCommandSource
    {
        public AlsLocomotionCommand GetCommand(long frameId) =>
            AlsLocomotionCommand.CreateDefault();
    }

    private sealed class JumpAfterLockCommandSource : IAlsLocomotionCommandSource
    {
        private int _jumpPending;

        public void RequestJump() => Interlocked.Exchange(ref _jumpPending, 1);

        public AlsLocomotionCommand GetCommand(long frameId) =>
            AlsLocomotionCommand.CreateDefault() with
            {
                ViewYaw = MathF.PI * 0.5f,
                AimYaw = MathF.PI * 0.5f,
                JumpPressed = (byte)Interlocked.Exchange(ref _jumpPending, 0),
            };
    }

    private sealed class TurnCommandSource : IAlsLocomotionCommandSource
    {
        public AlsLocomotionCommand GetCommand(long frameId) =>
            AlsLocomotionCommand.CreateDefault() with
            {
                ViewYaw = MathF.PI * 0.5f,
                AimYaw = MathF.PI * 0.5f,
            };
    }
}
