using Godot;
using GodotAls.Core.Events;

namespace GodotAls.Animation.Lyra;

internal enum LyraMotionPhase
{
    Idle, Start, Cycle, Stop, Pivot, JumpStart, JumpStartLoop, JumpApex, FallLoop, FallLand,
}

internal readonly record struct LyraMotionObservation(Vector2 LocalAcceleration,
    Vector2 LocalVelocity, LyraGait Gait, double Delta, double DisplacementCm,
    double PredictedStopCm, double PredictedPivotCm, bool RunningIntoWall = false,
    bool IsCrouching = false, bool IsOnGround = true, float VerticalVelocity = 0,
    double TimeToJumpApex = 0, double GroundDistanceCm = double.MaxValue,
    bool IsAiming = false, double TimeSinceFiredWeapon = double.MaxValue,
    float ApplyHipfireOverridePose = 0, float AimPitchDegrees = 0,
    float ActorYawDegrees = 0, bool IsDashing = false);

internal sealed class LyraUnarmedMotion
{
    private readonly LyraBoundRig _rig;
    private readonly LyraLinkedLayerRouter _layers;
    private readonly LyraUnarmedTiming _timing;
    private readonly LyraCycleSync _cycleSync;
    private readonly LyraUnarmedNotifies _notifies;
    private readonly LyraUnarmedRootMotion _rootMotion;
    private LyraUnarmedLayerDefaults _defaults;
    private readonly LyraRootYawOffset _rootYaw;
    private readonly LyraLayerPosePipeline _posePipeline;
    private readonly LyraLogicalSourceBank? _logicalBank;
    private readonly LyraLogicalSourcePlayback? _logicalPlayback;
    private readonly LyraUnarmedTurnInPlace? _turn;
    private LyraItemLayerInstance? _itemLayer;
    private LyraHipFirePoseLayer? _hipFire => _itemLayer?.HipFire;
    private ILyraAimingLayer? _aimOffset => _itemLayer?.Aiming;
    private LyraLeftHandPoseLayer? _leftHand => _itemLayer?.LeftHand;
    private LyraFullBodyAdditivesLayer? _additives => _itemLayer?.Additives;
    private float _previousLeftHandDisable;
    private int _completedLeftHandFrames;
    private int _completedAdditiveFrames;
    private LyraHandRetargetPoseLayer? _handRetarget => _itemLayer?.HandRetarget;
    private int _completedHandRetargetFrames;
    private float _previousHandRetargetDisable;
    private float _previousRightHandDisable;
    private float _previousLeftHandIkDisable;
    private int _completedLeftHandIkFrames, _completedLeftHandIkApplied, _completedCopyFrames;
    private int _completedRightHandIkFrames, _completedRightHandIkApplied;
    private LyraCardinalDirection _direction = LyraCardinalDirection.Forward;
    private LyraCardinalDirection _startDirection = LyraCardinalDirection.Forward;
    private LyraCardinalDirection _pivotInitialDirection = LyraCardinalDirection.Forward;
    private string _slot = "";
    private double _phaseTime;
    private double _lastPivotTime;
    private double _timeAtPivotStop;
    private double _blendTime;
    private float _cycleRate = 1;
    private uint _notifySeed = AlsTimelineRuntime.InitialAssetNotifyRandomSeed;
    private readonly LyraQueuedNotify[] _frameNotifies = new LyraQueuedNotify[64];
    private int _frameNotifyCount;
    private LyraQueuedNotify? _activeTransitionState;
    private bool _isCrouching;

    public LyraUnarmedMotion(LyraBoundRig rig, LyraLinkedLayerRouter layers,
        LyraUnarmedTiming timing, LyraUnarmedNotifies notifies,
        LyraUnarmedRootMotion rootMotion, LyraUnarmedLayerDefaults defaults)
    {
        _rig = rig;
        _layers = layers;
        _timing = timing;
        _cycleSync = timing.CreateCycleSync();
        _notifies = notifies;
        _rootMotion = rootMotion;
        _defaults = defaults;
        _rootYaw = new LyraRootYawOffset(rig.Skeleton, LyraRootYawDefaults.Load());
        _logicalBank = rig.Remaining is null ? null : LyraLogicalSourceBank.Load();
        _posePipeline = new LyraLayerPosePipeline(rig.Skeleton, _logicalBank);
        if (_logicalBank is not null)
        {
            _rootYaw.ConfigureLogical(_logicalBank);
            _logicalPlayback = new(_logicalBank, IsLooping, ClipLength);
        }
        if (rig.Remaining is not null)
        {
            _turn = LyraUnarmedTurnInPlace.Load(rig.Remaining, rig.Pistol, rig.Rifle);
            _itemLayer = layers.CreateInstance(rig, logical: _logicalBank);
        }
    }

    public LyraMotionPhase Phase { get; private set; }
    public string CurrentSlot => _slot;
    public string ActiveLayer => _layers.ActiveName;
    public int LayerRevision => _layers.Revision;
    internal LyraItemLayerInstance? ItemLayerInstance => _itemLayer;
    public int NotifyTransitionCount { get; private set; }
    public int CycleRateUpdates { get; private set; }
    public int CycleResourceSwitchCount => _cycleSync.ResourceSwitchCount;
    public int ScopeClosedNotifyCount { get; private set; }
    public int HipFireBlendFrames => _hipFire?.AppliedFrames ?? 0;
    public float HipFireBlendWeight => _hipFire?.Weight ?? 0;
    public float AimOffsetBlendWeight => _hipFire?.AimOffsetBlendWeight ?? 0;
    public int AimOffsetFrames => _aimOffset?.AppliedFrames ?? 0;
    public int PoseLayerFrames => _completedAdditiveFrames + (_additives?.EvaluatedFrames ?? 0);
    public int LeftHandPoseFrames => _completedLeftHandFrames + (_leftHand?.EvaluatedFrames ?? 0);
    public int HandRetargetFrames => _completedHandRetargetFrames + (_handRetarget?.EvaluatedFrames ?? 0);
    public int PoseCommitFrames => _posePipeline.AppliedFrames;
    public int LogicalSourceFrames => _logicalPlayback?.SampledFrames ?? 0;
    public int LogicalSourceBlendFrames => _logicalPlayback?.BlendedFrames ?? 0;
    public int LeftHandIkFrames => _completedLeftHandIkFrames + (_itemLayer?.LeftHandIk?.EvaluatedFrames ?? 0);
    public int LeftHandIkAppliedFrames => _completedLeftHandIkApplied + (_itemLayer?.LeftHandIk?.AppliedFrames ?? 0);
    public int CopyTargetFrames => _completedCopyFrames + (_itemLayer?.LeftHandIk?.CopyFrames ?? 0);
    public float LeftHandIkAlpha => _itemLayer?.LeftHandIk?.Alpha ?? 0;
    internal ReadOnlySpan<GodotAls.Core.Locomotion.AlsPrecisePose> LogicalOutputPose => _posePipeline.LogicalOutputPose;
    internal ReadOnlySpan<GodotAls.Core.Locomotion.AlsPrecisePose> LogicalSourcePose => _posePipeline.LogicalSourcePose;
    public int RightHandIkFrames => _completedRightHandIkFrames + (_itemLayer?.RightHandIk.EvaluatedFrames ?? 0);
    public int RightHandIkAppliedFrames => _completedRightHandIkApplied + (_itemLayer?.RightHandIk.AppliedFrames ?? 0);
    public float RightHandIkAlpha => _itemLayer?.RightHandIk.Alpha ?? 0;
    public double RightHandIkRotationRadians => _itemLayer?.RightHandIk.LastUpperRotationRadians ?? 0;
    public double HandRetargetOffsetCm => _handRetarget?.LastOffsetCm ?? 0;
    public LyraAdditiveState AdditiveState => _additives?.State ?? LyraAdditiveState.Identity;
    public float LeftHandPoseWeight => _leftHand?.Weight ?? 0;
    public float RootYawOffsetDegrees => _rootYaw.OffsetDegrees;
    public float AimYawDegrees => _rootYaw.AimYawDegrees;
    public LyraIdleTurnState IdleTurnState => _turn?.State ?? LyraIdleTurnState.Idle;
    public int TurnRotationCount => _turn?.RotationCount ?? 0;
    public int TurnRecoveryCount => _turn?.RecoveryCount ?? 0;
    public int TurnCurveFeedbackCount => _rootYaw.TurnCurveFeedbackCount;
    public float MinimumCycleRate { get; private set; } = float.PositiveInfinity;
    public float MaximumCycleRate { get; private set; }
    public ReadOnlySpan<LyraQueuedNotify> FrameNotifies => _frameNotifies.AsSpan(0, _frameNotifyCount);

    public void LinkLayer(ILyraItemAnimationLayers layer)
    {
        if (_rig.Remaining is null)
            throw new InvalidOperationException("Lyra layer switching requires the complete base clip bank.");
        // LinkAnimClassLayers preserves the existing group when its class matches.
        // Check before constructing operators or restoring their visible pose.
        if (_layers.IsCurrentClass(layer)) return;
        _layers.ValidateProvider(layer);
        var instance = _layers.CreateInstance(_rig, layer, _logicalBank);
        _posePipeline.RestoreBase();
        _layers.Link(layer);
        _completedLeftHandFrames += _leftHand?.EvaluatedFrames ?? 0;
        _completedAdditiveFrames += _additives?.EvaluatedFrames ?? 0;
        _completedHandRetargetFrames += _handRetarget?.EvaluatedFrames ?? 0;
        _completedRightHandIkFrames += _itemLayer?.RightHandIk.EvaluatedFrames ?? 0;
        _completedRightHandIkApplied += _itemLayer?.RightHandIk.AppliedFrames ?? 0;
        _completedLeftHandIkFrames += _itemLayer?.LeftHandIk?.EvaluatedFrames ?? 0;
        _completedLeftHandIkApplied += _itemLayer?.LeftHandIk?.AppliedFrames ?? 0;
        _completedCopyFrames += _itemLayer?.LeftHandIk?.CopyFrames ?? 0;
        _defaults = instance.PlaybackDefaults;
        _itemLayer = instance;
    }

    public void Advance(in LyraMotionObservation frame)
    {
        _frameNotifyCount = 0;
        if (!double.IsFinite(frame.Delta) || frame.Delta < 0 ||
            !double.IsFinite(frame.DisplacementCm) || frame.DisplacementCm < 0 ||
            !double.IsFinite(frame.PredictedStopCm) || frame.PredictedStopCm < 0 ||
            !double.IsFinite(frame.PredictedPivotCm) || frame.PredictedPivotCm < 0 ||
            !double.IsFinite(frame.TimeToJumpApex) || frame.TimeToJumpApex < 0 ||
            !double.IsFinite(frame.GroundDistanceCm) || frame.GroundDistanceCm < 0 ||
            !double.IsFinite(frame.TimeSinceFiredWeapon) || frame.TimeSinceFiredWeapon < 0 ||
            !float.IsFinite(frame.ApplyHipfireOverridePose) ||
            !float.IsFinite(frame.ActorYawDegrees) || !float.IsFinite(frame.AimPitchDegrees) ||
            !float.IsFinite(frame.VerticalVelocity) ||
            !float.IsFinite(frame.LocalAcceleration.X) || !float.IsFinite(frame.LocalAcceleration.Y) ||
            !float.IsFinite(frame.LocalVelocity.X) || !float.IsFinite(frame.LocalVelocity.Y))
            throw new ArgumentOutOfRangeException(nameof(frame));
        _posePipeline.RestoreBase();
        _rootYaw.Update(frame.ActorYawDegrees, frame.Delta, frame.IsCrouching, frame.IsDashing);
        var hasAcceleration = frame.LocalAcceleration.LengthSquared() > 1e-6f;
        _hipFire?.UpdateWeight(frame.IsCrouching, frame.IsOnGround, frame.IsAiming,
            frame.TimeSinceFiredWeapon, frame.ApplyHipfireOverridePose,
            _rootYaw.OffsetDegrees, hasAcceleration, frame.Delta);

        var hasVelocity = frame.LocalVelocity.LengthSquared() > 1e-10f;
        var accelerationDirection = hasAcceleration ? Classify(frame.LocalAcceleration) : _direction;
        var velocityDirection = hasVelocity ? Classify(frame.LocalVelocity) : _direction;
        var reversing = hasAcceleration && hasVelocity &&
            frame.LocalVelocity.Dot(frame.LocalAcceleration) < 0 && !frame.RunningIntoWall;
        var crouchChanged = frame.IsCrouching != _isCrouching;
        _isCrouching = frame.IsCrouching;

        if (!frame.IsOnGround && !IsAirPhase(Phase))
            Enter(frame.VerticalVelocity > 0 ? LyraMotionPhase.JumpStart : LyraMotionPhase.JumpApex,
                velocityDirection, frame.VerticalVelocity > 0 ? 0.1 : 0.4);
        else if (frame.IsOnGround && IsAirPhase(Phase))
            Enter(hasAcceleration ? LyraMotionPhase.Cycle : LyraMotionPhase.Idle,
                velocityDirection, hasAcceleration ? 0 : 0.15);
        else if (IsAirPhase(Phase))
        {
            switch (Phase)
            {
                case LyraMotionPhase.JumpStart when RemainingTime() <= 0:
                    Enter(LyraMotionPhase.JumpStartLoop, velocityDirection, 0);
                    break;
                case LyraMotionPhase.JumpStartLoop when frame.TimeToJumpApex < 0.4:
                    Enter(LyraMotionPhase.JumpApex, velocityDirection, 0.1);
                    break;
                case LyraMotionPhase.JumpApex when RemainingTime() <= 0.133:
                    Enter(LyraMotionPhase.FallLoop, velocityDirection, 0.133);
                    break;
                case LyraMotionPhase.FallLoop when frame.GroundDistanceCm < 200:
                    Enter(LyraMotionPhase.FallLand, velocityDirection, 0.3);
                    break;
            }
        }
        else switch (Phase)
        {
            case LyraMotionPhase.Idle when hasAcceleration:
                _startDirection = velocityDirection;
                Enter(LyraMotionPhase.Start, velocityDirection, 0.15);
                break;
            case LyraMotionPhase.Start when !hasAcceleration:
                Enter(LyraMotionPhase.Stop, velocityDirection, 0.15);
                break;
            case LyraMotionPhase.Cycle when !hasAcceleration:
                Enter(LyraMotionPhase.Stop, velocityDirection, 0.25);
                break;
            case LyraMotionPhase.Pivot when !hasAcceleration:
                Enter(LyraMotionPhase.Stop, velocityDirection, 0.25);
                break;
            case LyraMotionPhase.Start or LyraMotionPhase.Cycle when reversing:
                _pivotInitialDirection = velocityDirection;
                _lastPivotTime = 0.2;
                _timeAtPivotStop = 0;
                Enter(LyraMotionPhase.Pivot, Opposite(accelerationDirection), 0.25);
                break;
            case LyraMotionPhase.Start when velocityDirection != _startDirection ||
                crouchChanged ||
                _phaseTime >= 0.15 && frame.DisplacementCm / Math.Max(frame.Delta, 1e-6) < 10 ||
                RemainingTime() <= 1.0:
                Enter(LyraMotionPhase.Cycle, velocityDirection, 1.0);
                break;
            case LyraMotionPhase.Stop when hasAcceleration:
                _startDirection = velocityDirection;
                Enter(LyraMotionPhase.Start, velocityDirection, 0.1);
                break;
            case LyraMotionPhase.Stop when crouchChanged:
                Enter(LyraMotionPhase.Idle, velocityDirection, 0.15);
                break;
            case LyraMotionPhase.Stop when RemainingTime() <= 0.75:
                Enter(LyraMotionPhase.Idle, velocityDirection, 0.75);
                break;
            case LyraMotionPhase.Pivot when _slot.Length > 0 &&
                _notifies.TransitionToLocomotionActive(_slot, _phaseTime):
                NotifyTransitionCount++;
                Enter(LyraMotionPhase.Cycle, velocityDirection, 0.5);
                break;
            case LyraMotionPhase.Pivot when crouchChanged:
                Enter(LyraMotionPhase.Cycle, velocityDirection, 0.5);
                break;
            case LyraMotionPhase.Pivot when _lastPivotTime <= 0 &&
                IsPerpendicular(_pivotInitialDirection, velocityDirection):
                Enter(LyraMotionPhase.Cycle, velocityDirection, 0.5);
                break;
            case LyraMotionPhase.Cycle when hasVelocity:
                _direction = velocityDirection;
                break;
        }

        if (Phase == LyraMotionPhase.Idle)
        {
            var turnCurve = _turn is not null && _turn.IsTurnSlot(_slot)
                ? _turn.Sample(_slot, _phaseTime) : default;
            _rootYaw.ProcessTurnYawCurve(turnCurve.RemainingYaw, turnCurve.Weight,
                frame.IsCrouching);
        }
        else _rootYaw.ResetTurnYawCurve();
        var turnSelection = _turn?.Select(Phase == LyraMotionPhase.Idle,
            _rootYaw.OffsetDegrees, frame.IsCrouching, _slot, _phaseTime, _layers);

        var hook = Phase switch
        {
            LyraMotionPhase.Idle => LyraLayerHook.FullBody_IdleState,
            LyraMotionPhase.Start => LyraLayerHook.FullBody_StartState,
            LyraMotionPhase.Cycle => LyraLayerHook.FullBody_CycleState,
            LyraMotionPhase.Stop => LyraLayerHook.FullBody_StopState,
            LyraMotionPhase.Pivot => LyraLayerHook.FullBody_PivotState,
            LyraMotionPhase.JumpStart => LyraLayerHook.FullBody_JumpStartState,
            LyraMotionPhase.JumpStartLoop => LyraLayerHook.FullBody_JumpStartLoopState,
            LyraMotionPhase.JumpApex => LyraLayerHook.FullBody_JumpApexState,
            LyraMotionPhase.FallLoop => LyraLayerHook.FullBody_FallLoopState,
            LyraMotionPhase.FallLand => LyraLayerHook.FullBody_FallLandState,
            _ => throw new InvalidOperationException("Unknown Lyra locomotion phase."),
        };
        _rootYaw.QueueMode(Phase);
        var desired = turnSelection?.Slot ?? _layers.Resolve(hook,
            new(_direction, frame.Gait, frame.IsCrouching, frame.IsAiming));
        if (desired != _slot || turnSelection?.Restart == true)
        {
            CloseActiveTransitionState();
            var continuingCycle = Phase == LyraMotionPhase.Cycle &&
                LyraUnarmedTiming.IsCycleSlot(_slot);
            var continuingTurn = turnSelection?.Restart != true && _turn is not null &&
                _turn.IsTurnSlot(_slot) && _turn.IsTurnSlot(desired);
            var wasSameSlot = desired == _slot;
            var blendDuration = Phase == LyraMotionPhase.Idle && _turn is not null &&
                _turn.IsTurnSlot(_slot) && turnSelection is null ? 0.35 :
                turnSelection.HasValue ? 0.2 : _blendTime;
            _rig.Player.Play(_rig.QualifiedName(desired), _slot.Length == 0 ? 0 : blendDuration);
            _logicalPlayback?.Select(desired, _slot.Length == 0 ? 0 : blendDuration, turnSelection?.Restart == true);
            _slot = desired;
            if (!continuingCycle && !continuingTurn) _phaseTime = 0;
            if (continuingTurn || wasSameSlot) _rig.Player.Seek(_phaseTime, true);
            _blendTime = 0.2;
            if (Phase == LyraMotionPhase.Stop && !hasVelocity &&
                _timing.TryGetDistance(_slot, out var stopCurve))
                _phaseTime = stopCurve.TimeAtDistance(0);
        }

        if (_timing.TryGetDistance(_slot, out var curve))
        {
            var previousTime = _phaseTime;
            var blendAlpha = Math.Clamp((_phaseTime - _defaults.StrideBlendStartOffset) /
                _defaults.StrideBlendDuration, 0, 1);
            var startMinimumRate = _defaults.StrideBlendDuration +
                (_defaults.StartPivotMinimumRate - _defaults.StrideBlendDuration) * blendAlpha;
            var pivotAlpha = Math.Clamp((_phaseTime - _timeAtPivotStop -
                _defaults.StrideBlendStartOffset) / _defaults.StrideBlendDuration, 0, 1);
            var pivotMinimumRate = 0.2 +
                (_defaults.StartPivotMinimumRate - 0.2) * pivotAlpha;
            var nextTime = Phase switch
            {
                LyraMotionPhase.Start => frame.DisplacementCm > 0
                    ? curve.AdvanceByDistance(_phaseTime, frame.DisplacementCm, frame.Delta,
                        startMinimumRate, _defaults.StartPivotMaximumRate)
                    : _phaseTime,
                LyraMotionPhase.Pivot when reversing && frame.PredictedPivotCm > 0 =>
                    curve.TimeAtDistance(-frame.PredictedPivotCm),
                LyraMotionPhase.Pivot => frame.DisplacementCm > 0
                    ? curve.AdvanceByDistance(_phaseTime, frame.DisplacementCm, frame.Delta,
                        pivotMinimumRate, _defaults.StartPivotMaximumRate)
                    : _phaseTime,
                LyraMotionPhase.Stop when hasVelocity && !hasAcceleration &&
                    frame.PredictedStopCm > 0 => curve.TimeAtDistance(-frame.PredictedStopCm),
                _ => _phaseTime + frame.Delta,
            };
            if (Phase == LyraMotionPhase.Pivot && reversing)
                _timeAtPivotStop = _phaseTime;
            _phaseTime = Math.Clamp(nextTime, 0, ClipLength(_slot));
            CollectNotifies(_slot, previousTime, _phaseTime - previousTime);
            _rig.Player.Advance(frame.Delta);
            _rig.Player.Seek(_phaseTime, true);
        }
        else if (Phase == LyraMotionPhase.Cycle)
        {
            var speed = frame.Delta > 0 ? frame.DisplacementCm / frame.Delta : 0;
            if (speed > float.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(frame), "Lyra displacement speed exceeds float range.");
            _cycleRate = LyraUnarmedRootMotion.MatchCycleRate((float)speed,
                _rootMotion[_slot].Target, _cycleRate, _defaults);
            var candidate = _cycleSync.Evaluate(_slot, _phaseTime, _cycleRate, frame.Delta);
            CollectNotifies(_slot, candidate.PreviousTime, candidate.Delta);
            _phaseTime = candidate.Time;
            _rig.Player.Advance(frame.Delta);
            _rig.Player.Seek(_phaseTime, true);
            _cycleSync.Commit(candidate);
            CycleRateUpdates++;
            MinimumCycleRate = Math.Min(MinimumCycleRate, _cycleRate);
            MaximumCycleRate = Math.Max(MaximumCycleRate, _cycleRate);
        }
        else
        {
            var previousTime = _phaseTime;
            CollectNotifies(_slot, previousTime, frame.Delta);
            _rig.Player.Advance(frame.Delta);
            var length = ClipLength(_slot);
            _phaseTime = _rig.SourceClip(_slot).Loop ||
                _slot is "idle" or "crouch_idle" or "crouch_entry" or "crouch_exit" or
                "jump_start_loop" or "jump_fall_loop"
                ? (_phaseTime + frame.Delta) % length
                : Math.Min(length, _phaseTime + frame.Delta);
        }
        var poseFrame = new LyraPoseEvaluation(Phase, frame.IsCrouching, frame.IsOnGround,
            _rootYaw.AimYawDegrees, frame.AimPitchDegrees, _previousLeftHandDisable, _previousHandRetargetDisable,
            _previousRightHandDisable, _previousLeftHandIkDisable);
        if (_logicalPlayback is not null && _itemLayer is not null)
            _posePipeline.ApplyLogical(_itemLayer, _rootYaw, poseFrame, _logicalPlayback, _phaseTime, frame.Delta);
        else _posePipeline.Apply(_itemLayer, _rootYaw, poseFrame);
        _previousLeftHandDisable = _timing.SampleLeftHandDisable(_slot, _phaseTime);
        _previousHandRetargetDisable = _timing.SampleControlCurve(_slot, "DisableHandIKRetargeting", _phaseTime);
        _previousRightHandDisable = _timing.SampleControlCurve(_slot, "DisableRHandIK", _phaseTime);
        _previousLeftHandIkDisable = _timing.SampleControlCurve(_slot, "DisableLHandIK", _phaseTime);
        if (Phase == LyraMotionPhase.Pivot && _lastPivotTime > 0)
            _lastPivotTime -= frame.Delta;
    }

    private double RemainingTime() => _slot.Length == 0 ? double.PositiveInfinity :
        Math.Max(0, ClipLength(_slot) - _phaseTime);

    private double ClipLength(string slot) => _rig.SourceClip(slot).PlayLength;
    private bool IsLooping(string slot) => _rig.SourceClip(slot).Loop || slot is
        "idle" or "crouch_idle" or "crouch_entry" or "crouch_exit" or "jump_start_loop" or "jump_fall_loop";

    private static bool IsAirPhase(LyraMotionPhase phase) => phase is
        LyraMotionPhase.JumpStart or LyraMotionPhase.JumpStartLoop or LyraMotionPhase.JumpApex or
        LyraMotionPhase.FallLoop or LyraMotionPhase.FallLand;

    private void Enter(LyraMotionPhase phase, LyraCardinalDirection direction, double blendTime)
    {
        if (Phase == LyraMotionPhase.Cycle && phase != LyraMotionPhase.Cycle)
            _cycleSync.Reset();
        if (Phase == LyraMotionPhase.Pivot && phase != LyraMotionPhase.Pivot)
            CloseActiveTransitionState();
        Phase = phase;
        _direction = direction;
        _phaseTime = 0;
        _blendTime = blendTime;
        if (phase == LyraMotionPhase.Cycle) _cycleRate = 1;
    }

    private void CollectNotifies(string slot, double previousTime, double delta)
    {
        var begin = _frameNotifyCount;
        _frameNotifyCount += _notifies.Collect(slot, previousTime, delta, ref _notifySeed,
            _frameNotifies.AsSpan(begin));
        foreach (var item in _frameNotifies.AsSpan(begin, _frameNotifyCount - begin))
        {
            if (item.Kind != LyraNotifyKind.TransitionToLocomotion) continue;
            _activeTransitionState = item.ReachedEnd ? null : item;
        }
    }

    private void CloseActiveTransitionState()
    {
        if (_activeTransitionState is not { } active) return;
        if (_frameNotifyCount == _frameNotifies.Length)
            throw new InvalidOperationException("Lyra notification queue is full.");
        _frameNotifies[_frameNotifyCount++] = active with { ReachedEnd = true, ScopeExit = true };
        _activeTransitionState = null;
        ScopeClosedNotifyCount++;
    }

    private static LyraCardinalDirection Classify(Vector2 vector) =>
        Math.Abs(vector.X) > Math.Abs(vector.Y)
            ? vector.X > 0 ? LyraCardinalDirection.Right : LyraCardinalDirection.Left
            : vector.Y > 0 ? LyraCardinalDirection.Backward : LyraCardinalDirection.Forward;

    private static LyraCardinalDirection Opposite(LyraCardinalDirection direction) => direction switch
    {
        LyraCardinalDirection.Forward => LyraCardinalDirection.Backward,
        LyraCardinalDirection.Backward => LyraCardinalDirection.Forward,
        LyraCardinalDirection.Left => LyraCardinalDirection.Right,
        _ => LyraCardinalDirection.Left,
    };

    private static bool IsPerpendicular(LyraCardinalDirection left, LyraCardinalDirection right) =>
        (left is LyraCardinalDirection.Forward or LyraCardinalDirection.Backward) !=
        (right is LyraCardinalDirection.Forward or LyraCardinalDirection.Backward);
}
