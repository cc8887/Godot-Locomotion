using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Import;
using System.Diagnostics;
using NumericsQuaternion = System.Numerics.Quaternion;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;

namespace GodotAls.Locomotion;

public partial class P4DemoSmoke : Node
{
    private const string DemoScenePath = "res://scenes/demo/p4_locomotion_demo.tscn";
    private const int ExpectedFrames = 300;
    private const int ExpectedMannequinBones = 68;
    private const uint ExpectedWorkerStageSequence = 0x1234567u;
    private const int MaximumPhysicsTicksWithoutCommit = 240;

    private P4LocomotionDemo _demo = null!;
    private long _lastCommittedFrame;
    private int _physicsTicksWithoutCommit;
    private int _maximumVisibleRigCount;
    private bool _routePositionInitialized;
    private Vector3 _previousRoutePosition;
    private int _maximumRouteSurfaceStage = -1;
    private bool _flatObserved;
    private bool _slopeObserved;
    private readonly HashSet<long> _stairColliders = [];
    private float _minimumStairHeight = float.PositiveInfinity;
    private float _maximumStairHeight = float.NegativeInfinity;
    private bool _positiveAimObserved;
    private bool _negativeAimObserved;
    private float _minimumAimYaw = float.PositiveInfinity;
    private float _maximumAimYaw = float.NegativeInfinity;
    private float _minimumAimPitch = float.PositiveInfinity;
    private float _maximumAimPitch = float.NegativeInfinity;
    private bool _turnObserved;
    private float _minimumTurnYaw = float.PositiveInfinity;
    private float _maximumTurnYaw = float.NegativeInfinity;
    private float _maximumTurnSpeed;
    private AlsRotationMode _turnWindowMode;
    private bool _rotateObserved;
    private bool _turnOrRotateCancelled;
    private bool _translatingPlatformObserved;
    private bool _translatingPlatformMoved;
    private int _translatingLeftLockSamples;
    private int _translatingRightLockSamples;
    private bool _translatingLeftWasLocked;
    private bool _translatingRightWasLocked;
    private float _translatingLeftActiveAmount;
    private float _translatingRightActiveAmount;
    private long _firstTranslatingPlatformFrame = -1;
    private long _lastTranslatingPlatformFrame = -1;
    private float _maximumTranslatingLockAmount;
    private float _maximumTranslatingLeftLockCurve;
    private float _maximumTranslatingRightLockCurve;
    private float _minimumTranslatingRotateYaw = float.PositiveInfinity;
    private float _maximumTranslatingRotateYaw = float.NegativeInfinity;
    private float _maximumTranslatingRotateSpeed;
    private bool _translatingRotateObserved;
    private Vector3 _routeSprintEndPosition;
    private Vector3 _routeBrakeEndPosition;
    private NumericsVector3 _previousTranslatingPlatformPosition;
    private NumericsVector3 _previousTranslatingLeftLocalTarget;
    private NumericsVector3 _previousTranslatingRightLocalTarget;
    private int _translatingPlatformId = -1;
    private bool _baseChangeObserved;
    private bool _baseChangeReleaseObserved;
    private bool _specifiedLandingObserved;
    private AlsFootReleaseReason _leftCrossReleaseReason;
    private AlsFootReleaseReason _rightCrossReleaseReason;
    private Vector3 _crossStartPosition;
    private Vector3 _crossEndPosition;
    private AlsFootLockState _lastBaseLeftLock;
    private AlsFootLockState _lastBaseRightLock;
    private bool _rotatingPlatformObserved;
    private bool _rotatingPlatformMoved;
    private int _rotatingLeftLockSamples;
    private int _rotatingRightLockSamples;
    private NumericsQuaternion _previousRotatingPlatformRotation = NumericsQuaternion.Identity;
    private NumericsVector3 _previousRotatingLeftLocalTarget;
    private NumericsVector3 _previousRotatingRightLocalTarget;
    private bool _teleportRequested;
    private long _teleportRequestFrame = -1;
    private int _teleportPlatformId = -1;
    private bool _teleportLeftWasLocked;
    private bool _teleportRightWasLocked;
    private bool _teleportReleaseObserved;
    private float _teleportLeftRequestAmount;
    private float _teleportRightRequestAmount;
    private AlsFootLockState _lastTeleportLeftLock;
    private AlsFootLockState _lastTeleportRightLock;
    private bool _sprintObserved;
    private bool _crouchObserved;
    private bool _velocityDirectionObserved;
    private bool _jumpObserved;
    private bool _finished;

    public P4DemoSmoke()
    {
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = 4;
    }

    public override void _Ready()
    {
        try
        {
            RequireExactArguments();
            using var scene = ResourceLoader.Load<PackedScene>(DemoScenePath)
                ?? throw new InvalidOperationException("P4 production demo scene is missing.");
            _demo = scene.Instantiate<P4LocomotionDemo>();
            _demo.ConfigureForSmoke(new P4DemoCommandSource());
            AddChild(_demo);
        }
        catch (Exception exception)
        {
            Fail("load", exception);
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
            _physicsTicksWithoutCommit++;
            Require(_physicsTicksWithoutCommit <= MaximumPhysicsTicksWithoutCommit,
                $"P4 demo committed-frame progress stalled after frame {_lastCommittedFrame}");
            if (_demo is null || !_demo.IsRuntimeReady)
            {
                return;
            }

            var active = _demo.ActiveCharacter;
            var frame = active.Diagnostics;
            if (frame.CommittedFrameId == 0 || frame.CommittedFrameId == _lastCommittedFrame)
            {
                return;
            }

            Require(frame.CommittedFrameId == _lastCommittedFrame + 1,
                $"committed frame sequence skipped from {_lastCommittedFrame} to {frame.CommittedFrameId}");
            ValidateCommittedFrame(active, in frame);
            ObserveEvidence(active, in frame);
            EnterRotatingPlatformPhase(active, frame.CommittedFrameId);
            _lastCommittedFrame = frame.CommittedFrameId;
            _physicsTicksWithoutCommit = 0;
            if (_lastCommittedFrame == ExpectedFrames)
            {
                Finish(active, in frame);
            }
        }
        catch (Exception exception)
        {
            Fail("physics", exception);
        }
    }

    private void ValidateCommittedFrame(AlsP3Character active, in AlsP3FrameDiagnostics frame)
    {
        var frameId = frame.CommittedFrameId;
        Require(frame.CommandFrameId == frameId &&
                frame.MotorSnapshotFrameId == frameId &&
                frame.ModelResultFrameId == frameId &&
                frame.PoseAdvanceFrameId == frameId,
            $"frame {frameId} did not preserve command/motor/model/pose/commit identity");
        Require(frame.Identity == active.HandleIdentity(frameId) &&
                frame.Result.Identity == frame.Identity &&
                frame.FootPose.Identity == frame.Identity &&
                frame.FootProbeSource.Identity == frame.Identity,
            $"frame {frameId} published mismatched production identities");
        Require(frame.FootPose.AnimationAdvanceCount == 1,
            $"frame {frameId} advanced animation {frame.FootPose.AnimationAdvanceCount} times");
        Require(frame.FootPose.ModifierWriteTransactionCount == 1,
            $"frame {frameId} committed {frame.FootPose.ModifierWriteTransactionCount} modifier transactions");
        Require(frame.FootPose.WorkerStageSequence == ExpectedWorkerStageSequence,
            $"frame {frameId} worker stage sequence was 0x{frame.FootPose.WorkerStageSequence:X}");
        Require(frame.Result.ErrorCode == 0 && frame.Result.P4ReasonCode == AlsP4ReasonCode.None,
            $"frame {frameId} reported result error={frame.Result.ErrorCode} p4={frame.Result.P4ReasonCode}");
        Require(_demo.ErrorCount == 0,
            $"frame {frameId} accumulated {_demo.ErrorCount} production diagnostics");
        Require(!active.IsPoseFrozen, $"frame {frameId} production pose was frozen");
        ValidateFinite(in frame);

        var replacement = _demo.ReplacementDiagnostics;
        Require(replacement.VisibleCharacterCount <= 1,
            $"frame {frameId} exposed {replacement.VisibleCharacterCount} visible production rigs");
        _maximumVisibleRigCount = Math.Max(_maximumVisibleRigCount, replacement.VisibleCharacterCount);
        var lifecycle = active.LifecycleDiagnostics;
        Require(lifecycle.IsVisible == active.Visible &&
                (!lifecycle.IsVisible || (lifecycle.IsActive && lifecycle.IsVisualReady)),
            $"frame {frameId} violated production visibility readiness");
    }

    private void ObserveEvidence(AlsP3Character active, in AlsP3FrameDiagnostics frame)
    {
        var frameId = frame.CommittedFrameId;
        var result = frame.Result;
        var leftHit = frame.FootPose.LeftGatherHit;
        var rightHit = frame.FootPose.RightGatherHit;

        if (frameId < 270)
        {
            ObserveContinuousRoute(active, in leftHit, in rightHit);
        }
        if (frameId == 83)
        {
            _routeSprintEndPosition = active.MovementAnchor.GlobalPosition;
        }
        if (frameId == 110)
        {
            _routeBrakeEndPosition = active.MovementAnchor.GlobalPosition;
        }
        if (frameId == 145)
        {
            _crossStartPosition = active.MovementAnchor.GlobalPosition;
        }
        if (frameId == 163)
        {
            _crossEndPosition = active.MovementAnchor.GlobalPosition;
        }
        if (frameId is >= 164 and <= 181 &&
            result.ActualRotationMode == AlsRotationMode.Aiming)
        {
            _minimumAimYaw = MathF.Min(_minimumAimYaw, result.AimRelativeYaw);
            _maximumAimYaw = MathF.Max(_maximumAimYaw, result.AimRelativeYaw);
            _minimumAimPitch = MathF.Min(_minimumAimPitch, result.AimRelativePitch);
            _maximumAimPitch = MathF.Max(_maximumAimPitch, result.AimRelativePitch);
            _positiveAimObserved |= result.AimRelativeYaw > 0.15f &&
                                    result.AimRelativePitch > 0.05f;
            _negativeAimObserved |= result.AimRelativeYaw < -0.15f &&
                                    result.AimRelativePitch < -0.05f;
        }
        if (frameId is >= 182 and <= 216)
        {
            _minimumTurnYaw = MathF.Min(_minimumTurnYaw, result.AimRelativeYaw);
            _maximumTurnYaw = MathF.Max(_maximumTurnYaw, result.AimRelativeYaw);
            _maximumTurnSpeed = MathF.Max(_maximumTurnSpeed, frame.ActualVelocity.Length());
            _turnWindowMode = result.ActualRotationMode;
            _turnObserved |= result.TurnActive == 1 && result.TurnDirection != 0 &&
                             result.TurnNominalDegrees is 90 or 180;
        }
        if ((frameId is >= 111 and <= 145) ||
            (frameId is >= 217 and <= 241) || frameId >= 276)
        {
            _rotateObserved |= result.RotateActive == 1 && result.RotateDirection != 0;
        }
        if (frameId is >= 111 and <= 145)
        {
            _translatingRotateObserved |= result.RotateActive == 1;
            _minimumTranslatingRotateYaw = MathF.Min(
                _minimumTranslatingRotateYaw, result.AimRelativeYaw);
            _maximumTranslatingRotateYaw = MathF.Max(
                _maximumTranslatingRotateYaw, result.AimRelativeYaw);
            _maximumTranslatingRotateSpeed = MathF.Max(
                _maximumTranslatingRotateSpeed, frame.ActualVelocity.Length());
            _maximumTranslatingLeftLockCurve = MathF.Max(
                _maximumTranslatingLeftLockCurve, result.LeftFootLockCurve);
            _maximumTranslatingRightLockCurve = MathF.Max(
                _maximumTranslatingRightLockCurve, result.RightFootLockCurve);
        }
        if (frameId >= 261 && _turnObserved && _rotateObserved &&
            result.TurnActive == 0 && result.RotateActive == 0 &&
            frame.ActualVelocity.LengthSquared() > 0.01f)
        {
            _turnOrRotateCancelled = true;
        }

        if (frameId < 270)
        {
            ObserveTranslatingPlatform(in frame);
        }
        if (frameId is >= 274 and <= 300)
        {
            ObserveRotatingPlatform(in frame);
        }

        if (frameId >= 1)
        {
            _sprintObserved |= result.ActualGait == AlsGait.Sprinting;
            _crouchObserved |= result.ActualStance == AlsStance.Crouching;
            _velocityDirectionObserved |= result.ActualRotationMode == AlsRotationMode.VelocityDirection;
            _jumpObserved |= result.ResolvedLocomotionState == AlsLocomotionState.InAir;
        }

    }

    private void ObserveContinuousRoute(
        AlsP3Character active,
        in AlsFootHit leftHit,
        in AlsFootHit rightHit)
    {
        var position = active.MovementAnchor.GlobalPosition;
        if (_routePositionInitialized)
        {
            var horizontalStep = new Vector2(position.X, position.Z).DistanceTo(
                new Vector2(_previousRoutePosition.X, _previousRoutePosition.Z));
            Require(horizontalStep <= 0.35f,
                $"main route used a discontinuous character move from " +
                $"{_previousRoutePosition} to {position}");
            Require(MathF.Abs(position.X + 4f) <= 0.8f,
                $"main route left its continuous collision lane at x={position.X}");
        }
        _routePositionInitialized = true;
        _previousRoutePosition = position;

        ObserveRouteHit(in leftHit);
        ObserveRouteHit(in rightHit);
    }

    private void ObserveRouteHit(in AlsFootHit hit)
    {
        if (hit.Valid == 0 || hit.Walkable == 0 || hit.ColliderId < 0)
        {
            return;
        }

        var stage = _demo.ClassifyMainRouteCollider(hit.ColliderId);
        if (stage < 0)
        {
            return;
        }
        _maximumRouteSurfaceStage = Math.Max(_maximumRouteSurfaceStage, stage);

        if (stage == 0)
        {
            _flatObserved |= hit.Normal.Y > 0.999f;
            return;
        }
        if (stage == 1)
        {
            Require(_flatObserved, "continuous route reached slope before start-floor evidence");
            _slopeObserved |= hit.Normal.Y < 0.999f;
            return;
        }
        if (stage is >= 2 and <= 6)
        {
            Require(_slopeObserved, "continuous route reached stairs before slope evidence");
            ObserveStair(in hit);
            return;
        }
        if (stage == 7)
        {
            Require(_stairColliders.Count >= 3,
                "continuous route reached translating platform before varied stairs");
            return;
        }
        if (stage == 8)
        {
            Require(_translatingPlatformObserved,
                "continuous route reached static landing before translating platform");
        }
    }

    private void ObserveStair(in AlsFootHit hit)
    {
        if (hit.Valid == 0 || hit.Walkable == 0 || hit.ColliderId < 0)
        {
            return;
        }
        _stairColliders.Add(hit.ColliderId);
        _minimumStairHeight = MathF.Min(_minimumStairHeight, hit.Position.Y);
        _maximumStairHeight = MathF.Max(_maximumStairHeight, hit.Position.Y);
    }

    private void ObserveTranslatingPlatform(in AlsP3FrameDiagnostics frame)
    {
        var footPose = frame.FootPose;
        var leftHit = footPose.LeftGatherHit;
        var rightHit = footPose.RightGatherHit;
        var leftLock = footPose.LeftFootLock;
        var rightLock = footPose.RightFootLock;
        if (leftHit.Valid != 0 && rightHit.Valid != 0 &&
            leftHit.PlatformId >= 0 && leftHit.PlatformId == rightHit.PlatformId)
        {
            if (_translatingPlatformObserved)
            {
                _translatingPlatformMoved |= NumericsVector3.Distance(
                    _previousTranslatingPlatformPosition, leftHit.PlatformPosition) > 0.0005f;
            }
            _translatingPlatformObserved = true;
            if (_firstTranslatingPlatformFrame < 0)
            {
                _firstTranslatingPlatformFrame = frame.CommittedFrameId;
            }
            _lastTranslatingPlatformFrame = frame.CommittedFrameId;
            _maximumTranslatingLockAmount = MathF.Max(
                _maximumTranslatingLockAmount,
                MathF.Max(leftLock.Amount, rightLock.Amount));
            _previousTranslatingPlatformPosition = leftHit.PlatformPosition;
            _translatingPlatformId = leftHit.PlatformId;
            _translatingLeftWasLocked |= leftLock.Locked == 1 && leftLock.Amount > 0.05f &&
                                         leftLock.PlatformId == leftHit.PlatformId;
            _translatingRightWasLocked |= rightLock.Locked == 1 && rightLock.Amount > 0.05f &&
                                          rightLock.PlatformId == rightHit.PlatformId;
            if (leftLock.Locked == 1 && leftLock.PlatformId == leftHit.PlatformId)
            {
                _translatingLeftActiveAmount = leftLock.Amount;
            }
            if (rightLock.Locked == 1 && rightLock.PlatformId == rightHit.PlatformId)
            {
                _translatingRightActiveAmount = rightLock.Amount;
            }
            ObserveLock(in leftLock, leftHit.PlatformId,
                ref _previousTranslatingLeftLocalTarget, ref _translatingLeftLockSamples);
            ObserveLock(in rightLock, rightHit.PlatformId,
                ref _previousTranslatingRightLocalTarget, ref _translatingRightLockSamples);
        }

        if (_translatingPlatformObserved && frame.CommittedFrameId >= 31)
        {
            _lastBaseLeftLock = leftLock;
            _lastBaseRightLock = rightLock;
            if (_leftCrossReleaseReason == AlsFootReleaseReason.None &&
                (frame.Result.LeftFootReleaseReason != AlsFootReleaseReason.None ||
                 leftLock.ReleaseReason != AlsFootReleaseReason.None))
            {
                _leftCrossReleaseReason = frame.Result.LeftFootReleaseReason !=
                                          AlsFootReleaseReason.None
                    ? frame.Result.LeftFootReleaseReason
                    : leftLock.ReleaseReason;
            }
            if (_rightCrossReleaseReason == AlsFootReleaseReason.None &&
                (frame.Result.RightFootReleaseReason != AlsFootReleaseReason.None ||
                 rightLock.ReleaseReason != AlsFootReleaseReason.None))
            {
                _rightCrossReleaseReason = frame.Result.RightFootReleaseReason !=
                                           AlsFootReleaseReason.None
                    ? frame.Result.RightFootReleaseReason
                    : rightLock.ReleaseReason;
            }
            var leftBaseChanged = _translatingLeftWasLocked &&
                (frame.Result.LeftFootReleaseReason == AlsFootReleaseReason.BaseChanged ||
                 leftLock.ReleaseReason == AlsFootReleaseReason.BaseChanged) &&
                leftLock.Locked == 2 && leftLock.PlatformId == _translatingPlatformId &&
                leftLock.Amount < _translatingLeftActiveAmount;
            var rightBaseChanged = _translatingRightWasLocked &&
                (frame.Result.RightFootReleaseReason == AlsFootReleaseReason.BaseChanged ||
                 rightLock.ReleaseReason == AlsFootReleaseReason.BaseChanged) &&
                rightLock.Locked == 2 && rightLock.PlatformId == _translatingPlatformId &&
                rightLock.Amount < _translatingRightActiveAmount;
            _baseChangeReleaseObserved |= leftBaseChanged || rightBaseChanged;
        }

        if (frame.CommittedFrameId >= 31 &&
            leftHit.Valid != 0 && rightHit.Valid != 0 &&
            leftHit.PlatformId == -1 && rightHit.PlatformId == -1 &&
            leftHit.ColliderId == _demo.PlatformLandingColliderId &&
            rightHit.ColliderId == _demo.PlatformLandingColliderId)
        {
            _specifiedLandingObserved = true;
        }

        if (_baseChangeReleaseObserved && _specifiedLandingObserved &&
            !(leftLock.Locked == 1 && leftLock.PlatformId == _translatingPlatformId) &&
            !(rightLock.Locked == 1 && rightLock.PlatformId == _translatingPlatformId))
        {
            _baseChangeObserved = true;
        }
    }

    private void ObserveRotatingPlatform(in AlsP3FrameDiagnostics frame)
    {
        var footPose = frame.FootPose;
        var leftHit = footPose.LeftGatherHit;
        var rightHit = footPose.RightGatherHit;
        var leftLock = footPose.LeftFootLock;
        var rightLock = footPose.RightFootLock;
        if (leftHit.Valid == 0 || rightHit.Valid == 0 ||
            leftHit.PlatformId < 0 || leftHit.PlatformId != rightHit.PlatformId)
        {
            return;
        }

        if (_rotatingPlatformObserved)
        {
            _rotatingPlatformMoved |= QuaternionAngle(
                _previousRotatingPlatformRotation, leftHit.PlatformRotation) > 0.0005f;
        }
        _rotatingPlatformObserved = true;
        _previousRotatingPlatformRotation = leftHit.PlatformRotation;
        ObserveLock(in leftLock, leftHit.PlatformId,
            ref _previousRotatingLeftLocalTarget, ref _rotatingLeftLockSamples);
        ObserveLock(in rightLock, rightHit.PlatformId,
            ref _previousRotatingRightLocalTarget, ref _rotatingRightLockSamples);

        var leftActive = leftLock.Locked == 1 && leftLock.Amount > 0.05f &&
                         leftLock.PlatformId == leftHit.PlatformId;
        var rightActive = rightLock.Locked == 1 && rightLock.Amount > 0.05f &&
                          rightLock.PlatformId == rightHit.PlatformId;
        if (!_teleportRequested && frame.CommittedFrameId >= 285 &&
            Math.Max(_rotatingLeftLockSamples, _rotatingRightLockSamples) >= 2)
        {
            Require(leftActive || rightActive,
                "rotating platform teleport was armed without an active platform-local lock");
            _teleportRequestFrame = frame.CommittedFrameId;
            _teleportPlatformId = leftHit.PlatformId;
            _teleportLeftWasLocked = leftActive;
            _teleportRightWasLocked = rightActive;
            _teleportLeftRequestAmount = leftLock.Amount;
            _teleportRightRequestAmount = rightLock.Amount;
            _demo.RequestSmokePlatformTeleport();
            _teleportRequested = true;
        }

        if (_teleportRequested && frame.CommittedFrameId > _teleportRequestFrame &&
            frame.CommittedFrameId <= _teleportRequestFrame + 10)
        {
            var leftReleased = _teleportLeftWasLocked &&
                (frame.Result.LeftFootReleaseReason == AlsFootReleaseReason.Teleported ||
                 leftLock.ReleaseReason == AlsFootReleaseReason.Teleported) &&
                leftLock.Locked == 2 && leftLock.PlatformId == _teleportPlatformId &&
                leftLock.Amount < _teleportLeftRequestAmount;
            var rightReleased = _teleportRightWasLocked &&
                (frame.Result.RightFootReleaseReason == AlsFootReleaseReason.Teleported ||
                 rightLock.ReleaseReason == AlsFootReleaseReason.Teleported) &&
                rightLock.Locked == 2 && rightLock.PlatformId == _teleportPlatformId &&
                rightLock.Amount < _teleportRightRequestAmount;
            _teleportReleaseObserved |= leftReleased || rightReleased;
        }
        if (_teleportRequested && frame.CommittedFrameId > _teleportRequestFrame)
        {
            _lastTeleportLeftLock = leftLock;
            _lastTeleportRightLock = rightLock;
        }
    }

    private static void ObserveLock(
        in AlsFootLockState footLock,
        int expectedPlatformId,
        ref NumericsVector3 previousLocalTarget,
        ref int samples)
    {
        if (footLock.Locked != 1 || footLock.Amount <= 0.05f ||
            footLock.PlatformId != expectedPlatformId)
        {
            return;
        }
        if (samples > 0)
        {
            Require(NumericsVector3.Distance(previousLocalTarget, footLock.LocalPosition) <= 0.08f,
                "platform-local foot lock drifted between committed samples");
        }
        previousLocalTarget = footLock.LocalPosition;
        samples++;
    }

    private void EnterRotatingPlatformPhase(AlsP3Character active, long frameId)
    {
        if (frameId != 270)
        {
            return;
        }

        var marker = _demo.GetNode<Marker3D>("SmokeStations/RotatingPlatform");
        var anchor = active.MovementAnchor;
        anchor.GlobalTransform = new Transform3D(Basis.Identity, marker.GlobalPosition);
        if (anchor is CharacterBody3D body)
        {
            body.Velocity = Vector3.Zero;
        }
    }

    private void Finish(AlsP3Character active, in AlsP3FrameDiagnostics frame)
    {
        Require(frame.CommittedFrameId == ExpectedFrames,
            $"P4 demo ended at frame {frame.CommittedFrameId}");
        Require(_flatObserved && _slopeObserved,
            $"flat/slope evidence was incomplete: flat={_flatObserved} slope={_slopeObserved}");
        Require(_stairColliders.Count >= 3 && _maximumStairHeight - _minimumStairHeight >= 0.15f,
            $"varied-stair evidence was incomplete: colliders={_stairColliders.Count} " +
            $"height_range={_maximumStairHeight - _minimumStairHeight}");
        Require(_maximumRouteSurfaceStage == 8,
            $"continuous main route did not reach the specified static landing: " +
            $"stage={_maximumRouteSurfaceStage}");
        Require(_positiveAimObserved && _negativeAimObserved,
            $"AimOffset did not publish positive/negative yaw/pitch evidence: " +
            $"yaw=[{_minimumAimYaw},{_maximumAimYaw}] " +
            $"pitch=[{_minimumAimPitch},{_maximumAimPitch}]");
        Require(_turnObserved && _rotateObserved && _turnOrRotateCancelled,
            $"Turn/Rotate/cancellation evidence was incomplete: turn={_turnObserved} " +
            $"rotate={_rotateObserved} cancel={_turnOrRotateCancelled} " +
            $"turnYaw=[{_minimumTurnYaw},{_maximumTurnYaw}] " +
            $"turnSpeedMax={_maximumTurnSpeed} turnMode={_turnWindowMode}");
        Require(_translatingPlatformObserved && _translatingPlatformMoved &&
                Math.Max(_translatingLeftLockSamples, _translatingRightLockSamples) >= 2,
            $"translating-platform evidence was incomplete: observed={_translatingPlatformObserved} " +
            $"moved={_translatingPlatformMoved} locks=" +
            $"{Math.Max(_translatingLeftLockSamples, _translatingRightLockSamples)} " +
            $"frames=[{_firstTranslatingPlatformFrame},{_lastTranslatingPlatformFrame}] " +
            $"lockMax={_maximumTranslatingLockAmount} sprintEnd={_routeSprintEndPosition} " +
            $"brakeEnd={_routeBrakeEndPosition} rotate={_translatingRotateObserved} " +
            $"rotateYaw=[{_minimumTranslatingRotateYaw},{_maximumTranslatingRotateYaw}] " +
            $"speedMax={_maximumTranslatingRotateSpeed} curves=" +
            $"({_maximumTranslatingLeftLockCurve},{_maximumTranslatingRightLockCurve})");
        Require(_baseChangeObserved,
            $"character did not publish BaseChanged and release the translating lock on the " +
            $"specified static landing: release={_baseChangeReleaseObserved} " +
            $"landing={_specifiedLandingObserved} reasons=" +
            $"({_leftCrossReleaseReason},{_rightCrossReleaseReason}) " +
            $"cross=[{_crossStartPosition},{_crossEndPosition}] wasLocked=" +
            $"({_translatingLeftWasLocked},{_translatingRightWasLocked}) final=" +
            $"(L:{_lastBaseLeftLock.Locked}/{_lastBaseLeftLock.PlatformId}/" +
            $"{_lastBaseLeftLock.Amount},R:{_lastBaseRightLock.Locked}/" +
            $"{_lastBaseRightLock.PlatformId}/{_lastBaseRightLock.Amount})");
        Require(_rotatingPlatformObserved && _rotatingPlatformMoved &&
                Math.Max(_rotatingLeftLockSamples, _rotatingRightLockSamples) >= 2,
            $"rotating-platform evidence was incomplete: observed={_rotatingPlatformObserved} " +
            $"moved={_rotatingPlatformMoved} locks=" +
            $"{Math.Max(_rotatingLeftLockSamples, _rotatingRightLockSamples)}");
        Require(_teleportRequested && _teleportReleaseObserved,
            $"platform teleport release was incomplete: requested={_teleportRequested} " +
            $"released={_teleportReleaseObserved} " +
            $"requestFrame={_teleportRequestFrame} requestAmounts=" +
            $"({_teleportLeftRequestAmount},{_teleportRightRequestAmount}) final=" +
            $"(L:{_lastTeleportLeftLock.Locked}/{_lastTeleportLeftLock.PlatformId}/" +
            $"{_lastTeleportLeftLock.Amount},R:{_lastTeleportRightLock.Locked}/" +
            $"{_lastTeleportRightLock.PlatformId}/{_lastTeleportRightLock.Amount})");
        Require(_sprintObserved && _crouchObserved && _velocityDirectionObserved && _jumpObserved,
            $"production controls were incomplete: sprint={_sprintObserved} crouch={_crouchObserved} " +
            $"velocity={_velocityDirectionObserved} jump={_jumpObserved}");
        Require(active.WorkerObservedOffMainThread,
            "P4 demo worker never executed off the main thread");
        Require(_maximumVisibleRigCount == 1 &&
                _demo.ReplacementDiagnostics.VisibleCharacterCount == 1,
            $"P4 demo visible-rig count was not exactly one: max={_maximumVisibleRigCount} " +
            $"current={_demo.ReplacementDiagnostics.VisibleCharacterCount}");
        Require(_demo.OrbitCamera.Target == active.MovementAnchor &&
                _demo.OrbitCamera.GlobalPosition.IsEqualApprox(
                    active.MovementAnchor.GlobalPosition + _demo.OrbitCamera.FollowOffset),
            "P4 demo camera did not follow the active movement anchor");

        var animationTree = active.FindChild(
            "AlsLocomotionAnimationTree", recursive: true, owned: false) as AnimationTree;
        Require(animationTree is not null, "P4 demo production AnimationTree was missing");
        var skeleton = AlsImportedResourceAuditor.FindFirst<Skeleton3D>(active);
        Require(skeleton is not null && skeleton.GetBoneCount() == ExpectedMannequinBones,
            "P4 demo did not run the real 68-bone Mannequin");
        ValidateHud(in frame);

        _demo.DisposeRuntime();
        _finished = true;
        GD.Print("P4_DEMO_OK frames=300 rigs=1");
        GetTree().Quit();
    }

    private void ValidateHud(in AlsP3FrameDiagnostics frame)
    {
        Require(_demo.Hud.LastStateFrame is >= ExpectedFrames - 2 and <= ExpectedFrames &&
                _demo.Hud.StateFormatCount > 1 && _demo.Hud.RefreshCallCount > 1,
            $"P4 HUD did not advance with render-time committed snapshots: " +
            $"frame={_demo.Hud.LastStateFrame} " +
            $"formats={_demo.Hud.StateFormatCount} refresh={_demo.Hud.RefreshCallCount}");

        var performanceFormats = _demo.Hud.PerformanceFormatCount;
        for (var refresh = 0; refresh < 15 &&
             _demo.Hud.PerformanceFormatCount == performanceFormats; refresh++)
        {
            _demo.Hud.Refresh(in frame, Engine.GetFramesPerSecond(), _demo.ErrorCount);
        }
        Require(_demo.Hud.LastStateFrame == ExpectedFrames &&
                _demo.Hud.PerformanceFormatCount > performanceFormats,
            "P4 HUD did not format the final committed snapshot through Refresh");

        var result = frame.Result;
        var turn = result.TurnActive == 0
            ? "off"
            : FormattableString.Invariant(
                $"{(result.TurnDirection < 0 ? 'L' : 'R')}{Math.Abs(result.TurnNominalDegrees)}@{result.TurnPhase:0.00}");
        var rotate = result.RotateActive == 0
            ? "off"
            : FormattableString.Invariant(
                $"{(result.RotateDirection < 0 ? 'L' : 'R')}@{result.RotatePhase:0.00}");
        var workerMilliseconds = result.WorkerElapsedTicks <= 0
            ? 0d
            : result.WorkerElapsedTicks * 1_000d / Stopwatch.Frequency;
        var expectedFields = new[]
        {
            $"Frame {frame.CommittedFrameId}",
            $"Mode {result.ActualRotationMode}",
            FormattableString.Invariant(
                $"Aim ({result.AimRelativeYaw:+0.00;-0.00;+0.00},{result.AimRelativePitch:+0.00;-0.00;+0.00})"),
            $"Turn {turn}",
            $"Rotate {rotate}",
            FormattableString.Invariant($"leftLock {result.LeftFootPose.LockAmount:0.00}"),
            FormattableString.Invariant($"rightLock {result.RightFootPose.LockAmount:0.00}"),
            FormattableString.Invariant($"Pelvis {result.PelvisOffset.Y:+0.000;-0.000;+0.000}"),
            $"Errors {_demo.ErrorCount}",
            FormattableString.Invariant($"workerMs {workerMilliseconds:0.000}"),
        };
        var combined = $"{_demo.Hud.StateText}\n{_demo.Hud.PerformanceText}";
        foreach (var field in expectedFields)
        {
            Require(combined.Contains(field, StringComparison.Ordinal),
                $"P4 HUD did not match committed operational field {field}");
        }
        foreach (var forbidden in new[]
        {
            "WASD", "RMB", "Shift", "Ctrl", "Space", "mouse", "tutorial", "help",
        })
        {
            Require(!combined.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"P4 HUD contained forbidden tutorial text {forbidden}");
        }
    }

    private static void ValidateFinite(in AlsP3FrameDiagnostics frame)
    {
        var result = frame.Result;
        Require(result.WorkerElapsedTicks >= 0 && IsFinite(result.TargetYaw) &&
                IsFinite(result.AimRelativeYaw) && IsFinite(result.AimRelativePitch) &&
                IsFinite(result.TurnPhase) && IsFinite(result.TurnPlayRate) &&
                IsFinite(result.TurnYawDelta) && IsFinite(result.RotatePhase) &&
                IsFinite(result.RotatePlayRate) && IsFinite(result.RotateYawDelta) &&
                IsFinite(result.PelvisOffset) && IsFinite(result.LeftFootPose.Position) &&
                IsFinite(result.LeftFootPose.Rotation) && IsFinite(result.LeftFootPose.LockAmount) &&
                IsFinite(result.RightFootPose.Position) && IsFinite(result.RightFootPose.Rotation) &&
                IsFinite(result.RightFootPose.LockAmount),
            $"frame {frame.CommittedFrameId} published non-finite P4 result data");
        var footPose = frame.FootPose;
        ValidateFootSnapshot(frame.CommittedFrameId, in footPose);
    }

    private static void ValidateFootSnapshot(long frameId, in AlsP4FootPlacementPoseSnapshot pose)
    {
        Require(IsFinite(pose.PelvisLocalPosition) && IsFinite(pose.LeftFootWorldPosition) &&
                IsFinite(pose.RightFootWorldPosition) && IsFinite(pose.LeftFootWorldRotation) &&
                IsFinite(pose.RightFootWorldRotation) && IsFinite(pose.LeftProbeWorldOrigin) &&
                IsFinite(pose.RightProbeWorldOrigin) && IsFinite(pose.LeftGatherHit) &&
                IsFinite(pose.RightGatherHit) && IsFinite(pose.LeftFootLock) &&
                IsFinite(pose.RightFootLock),
            $"frame {frameId} published non-finite foot-placement data");
    }

    private void Fail(string code, Exception exception)
    {
        if (_finished)
        {
            return;
        }
        _finished = true;
        try
        {
            if (_demo is not null && GodotObject.IsInstanceValid(_demo))
            {
                _demo.DisposeRuntime();
            }
        }
        catch
        {
        }
        var message = exception.Message.Replace('\r', ' ').Replace('\n', ' ');
        GD.Print($"P4_DEMO_FAIL code={code} {exception.GetType().Name}: {message}");
        GetTree().Quit(1);
    }

    private static void RequireExactArguments()
    {
        var arguments = OS.GetCmdlineUserArgs();
        Require(arguments.Length == 1 && arguments[0] == "--als-smoke-frames=300",
            "P4 demo smoke accepts only --als-smoke-frames=300.");
    }

    private static bool IsFinite(float value) => float.IsFinite(value);

    private static bool IsFinite(in NumericsVector3 value) =>
        IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);

    private static bool IsFinite(in NumericsQuaternion value) =>
        IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z) && IsFinite(value.W);

    private static bool IsFinite(in AlsFootHit hit) =>
        IsFinite(hit.Position) && IsFinite(hit.Normal) && IsFinite(hit.PlatformPosition) &&
        IsFinite(hit.PlatformRotation) && IsFinite(hit.PointVelocity);

    private static bool IsFinite(in AlsFootLockState footLock) =>
        IsFinite(footLock.LocalPosition) && IsFinite(footLock.LocalRotation) &&
        IsFinite(footLock.Offset) && IsFinite(footLock.Rotation) &&
        IsFinite(footLock.ProvenancePosition) && IsFinite(footLock.ProvenanceRotation) &&
        IsFinite(footLock.Amount);

    private static float QuaternionAngle(in NumericsQuaternion left, in NumericsQuaternion right)
    {
        var dot = MathF.Abs(NumericsQuaternion.Dot(left, right));
        return 2f * MathF.Acos(Math.Clamp(dot, 0f, 1f));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class P4DemoCommandSource : IAlsLocomotionCommandSource
    {
        public AlsLocomotionCommand GetCommand(long frameId)
        {
            if (frameId is <= 0 or > ExpectedFrames)
            {
                throw new ArgumentOutOfRangeException(nameof(frameId));
            }

            var command = AlsLocomotionCommand.CreateDefault();
            return frameId switch
            {
                <= 83 => command with
                {
                    MovementAxes = NumericsVector2.UnitY,
                    RequestedGait = AlsGait.Sprinting,
                    RequestedRotationMode = AlsRotationMode.VelocityDirection,
                },
                <= 110 => command,
                <= 145 => command with
                {
                    ViewYaw = 2.6f, AimYaw = 2.6f,
                    RequestedRotationMode = AlsRotationMode.Aiming,
                },
                <= 163 => command with
                {
                    MovementAxes = NumericsVector2.UnitY,
                    RequestedGait = AlsGait.Sprinting,
                    RequestedRotationMode = AlsRotationMode.VelocityDirection,
                },
                <= 172 => command with
                {
                    ViewYaw = -3f, ViewPitch = 0.3f, AimYaw = -3f, AimPitch = 0.3f,
                    RequestedRotationMode = AlsRotationMode.Aiming,
                },
                <= 181 => command with
                {
                    ViewYaw = 1.1f, ViewPitch = -0.3f, AimYaw = 1.1f, AimPitch = -0.3f,
                    RequestedRotationMode = AlsRotationMode.Aiming,
                },
                <= 216 => command with
                {
                    ViewYaw = -2.5f, AimYaw = -2.5f,
                    RequestedRotationMode = AlsRotationMode.LookingDirection,
                },
                <= 241 => command with
                {
                    ViewYaw = -2.6f, AimYaw = -2.6f,
                    RequestedRotationMode = AlsRotationMode.Aiming,
                },
                <= 252 => command with
                {
                    RequestedStance = AlsStance.Crouching,
                },
                <= 260 => command with
                {
                    JumpPressed = frameId == 255 ? (byte)1 : (byte)0,
                },
                <= 269 => command with
                {
                    MovementAxes = NumericsVector2.UnitY * 0.2f,
                    RequestedRotationMode = AlsRotationMode.VelocityDirection,
                },
                <= 275 => command,
                _ => command with
                {
                    ViewYaw = 2.6f, AimYaw = 2.6f,
                    RequestedRotationMode = AlsRotationMode.Aiming,
                },
            };
        }
    }
}
