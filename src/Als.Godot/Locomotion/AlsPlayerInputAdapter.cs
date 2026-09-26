using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Actions;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;
using NumericsVector2 = System.Numerics.Vector2;

namespace GodotAls.Locomotion;

public readonly record struct AlsPlayerInputSnapshot(
    NumericsVector2 MovementAxes,
    bool WalkHeld,
    bool SprintHeld,
    bool CrouchTogglePressed,
    bool JumpPressed,
    bool RotationModeTogglePressed,
    bool AimHeld)
{
    public AlsOverlayKind Overlay { get; init; }
    public bool RollPreviewPressed { get; init; }
    public bool CancelActionPressed { get; init; }
}

public sealed class AlsPlayerInputAdapter : IAlsLocomotionCommandSource, IAlsActionRequestSource
{
    private AlsLocomotionCommand _command = AlsLocomotionCommand.CreateDefault();
    private AlsStance _stance = AlsStance.Standing;
    private AlsRotationMode _rotationMode = AlsRotationMode.LookingDirection;
    private long _capturedFrameId;
    private readonly AlsCapturedActionRequests _actions = new();
    private int _previewDefinition = -1, _previewSection = -1;
    private long _previewOwnerRequest, _previewOwnerEpoch;
    private long _lastOutcomeFrame;
    private AlsFrameIdentity _pendingActionIdentity;
    private bool _pendingRoll, _pendingCancel;
    public bool ActionPreviewEnabled => _previewDefinition >= 0;
    public AlsFrameIdentity CapturedActionIdentity => _actions.Identity;

    public void ConfigureActionPreview(int definition, int section)
    {
        if (definition < 0 || section < 0 || ActionPreviewEnabled || _capturedFrameId != 0)
            throw new InvalidOperationException("Configure the authored action preview once, before input capture.");
        _previewDefinition = definition; _previewSection = section;
    }

    public void QueueActionPreview(AlsFrameIdentity nextIdentity, bool roll, bool cancel)
    {
        if (!ActionPreviewEnabled || nextIdentity.FrameId <= 0 || nextIdentity.SlotGeneration == 0 ||
            _actions.Identity.SlotGeneration != 0 && (nextIdentity.CharacterId != _actions.Identity.CharacterId ||
                nextIdentity.SlotGeneration < _actions.Identity.SlotGeneration) ||
            SameOwner(nextIdentity, _actions.Identity) && nextIdentity.FrameId <= _actions.Identity.FrameId)
            throw new InvalidOperationException("Action input may only target a future capture.");
        if (!SameOwner(nextIdentity, _pendingActionIdentity)) _pendingRoll = _pendingCancel = false;
        _pendingActionIdentity = nextIdentity; _pendingRoll |= roll; _pendingCancel |= cancel;
    }

    // Observe outcomes only after Main Commit, never from a speculative worker.
    public void ObserveActionOutcome(AlsFrameIdentity identity, AlsActionOutcome outcome)
    {
        if (!SameOwner(identity, _actions.Identity) || outcome.ActionDefinitionId != _previewDefinition ||
            identity.FrameId < _lastOutcomeFrame) return;
        if (identity.FrameId > _actions.Identity.FrameId || outcome.RequestId <= 0)
            throw new ArgumentException("Action outcome is not from a captured frame.");
        _lastOutcomeFrame = identity.FrameId;
        if (outcome.ResultCode == AlsActionResultCode.Accepted)
        {
            _previewOwnerRequest = outcome.RequestId; _previewOwnerEpoch = outcome.PlaybackEpoch;
        }
        else if ((outcome.ResultCode == AlsActionResultCode.Completed || outcome.ResultCode >= AlsActionResultCode.InterruptedByReplacement) &&
            outcome.RequestId == _previewOwnerRequest && outcome.PlaybackEpoch == _previewOwnerEpoch)
            _previewOwnerRequest = _previewOwnerEpoch = 0;
    }

    public void CaptureGodotFrame(AlsFrameIdentity identity, float viewYaw, float viewPitch,
        AlsOverlayKind overlay = AlsOverlayKind.Default) =>
        CaptureFrame(identity, ReadGodotSnapshot() with { Overlay = overlay }, viewYaw, viewPitch);

    public void CaptureFrame(AlsFrameIdentity identity, in AlsPlayerInputSnapshot snapshot, float viewYaw, float viewPitch)
    {
        if (!ActionPreviewEnabled) { CaptureFrame(identity.FrameId, snapshot, viewYaw, viewPitch); return; }
        var sameOwner = SameOwner(identity, _actions.Identity);
        var pending = SameOwner(identity, _pendingActionIdentity) && identity.FrameId >= _pendingActionIdentity.FrameId;
        var cancel = snapshot.CancelActionPressed || pending && _pendingCancel;
        var start = snapshot.RollPreviewPressed || pending && _pendingRoll;
        var request = AlsActionRequest.None with { SlotGeneration = identity.SlotGeneration };
        // Cancel wins simultaneous edges; it only addresses an accepted owner.
        if (cancel && sameOwner && _previewOwnerRequest > 0)
            request = new(_previewOwnerRequest, AlsActionCommand.Cancel, _previewDefinition, -1, 0, identity.SlotGeneration);
        else if (!cancel && start)
            request = new(identity.FrameId, AlsActionCommand.Start, _previewDefinition, _previewSection, 100, identity.SlotGeneration);
        _actions.ValidateCapture(identity, request);
        // A replacement can inherit a movement frame already captured for the
        // retired actor. Rebind action ownership without applying toggles twice.
        if (sameOwner || identity.FrameId != _capturedFrameId)
            CaptureMovementFrame(identity.FrameId, snapshot with { CancelActionPressed = cancel }, viewYaw, viewPitch);
        _actions.Capture(identity, request);
        if (!sameOwner) _previewOwnerRequest = _previewOwnerEpoch = _lastOutcomeFrame = 0;
        if (pending || !SameOwner(identity, _pendingActionIdentity)) _pendingRoll = _pendingCancel = false;
    }

    public AlsActionRequest GetActionRequest(AlsFrameIdentity identity) => ActionPreviewEnabled
        ? _actions.Read(identity) : AlsActionRequest.None;
    public void DiscardUnpublishedActions(long publishedFrame)
    {
        _actions.DiscardUnpublished(publishedFrame);
        _pendingRoll = _pendingCancel = false;
        _previewOwnerRequest = _previewOwnerEpoch = 0;
    }

    private static bool SameOwner(AlsFrameIdentity a, AlsFrameIdentity b) => a.SlotGeneration != 0 &&
        a.CharacterId == b.CharacterId && a.SlotGeneration == b.SlotGeneration;

    public long CapturedFrameId => _capturedFrameId;

    public void CaptureGodotFrame(long frameId, float viewYaw, float viewPitch,
        AlsOverlayKind overlay = AlsOverlayKind.Default) =>
        CaptureFrame(frameId, ReadGodotSnapshot() with { Overlay = overlay }, viewYaw, viewPitch);

    public void CaptureFrame(
        long frameId,
        in AlsPlayerInputSnapshot snapshot,
        float viewYaw,
        float viewPitch)
    {
        if (ActionPreviewEnabled)
            throw new InvalidOperationException("Configured action input requires a full frame/character/generation identity.");
        CaptureMovementFrame(frameId, snapshot, viewYaw, viewPitch);
    }

    private void CaptureMovementFrame(long frameId, in AlsPlayerInputSnapshot snapshot, float viewYaw, float viewPitch)
    {
        if (frameId != _capturedFrameId + 1)
        {
            throw new InvalidOperationException(
                $"Player input frames must be captured consecutively. Last frame was " +
                $"{_capturedFrameId}, received {frameId}.");
        }
        if ((uint)snapshot.Overlay > (uint)AlsOverlayKind.Barrel)
            throw new ArgumentOutOfRangeException(nameof(snapshot), "Unknown Overlay selection.");
        if (!float.IsFinite(viewYaw) ||
            !float.IsFinite(viewPitch) ||
            !float.IsFinite(snapshot.MovementAxes.X) ||
            !float.IsFinite(snapshot.MovementAxes.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(snapshot),
                "Player input axes and view yaw/pitch must be finite.");
        }

        if (snapshot.CrouchTogglePressed)
        {
            _stance = _stance == AlsStance.Standing
                ? AlsStance.Crouching
                : AlsStance.Standing;
        }
        if (snapshot.RotationModeTogglePressed && !snapshot.AimHeld)
        {
            _rotationMode = _rotationMode == AlsRotationMode.LookingDirection
                ? AlsRotationMode.VelocityDirection
                : AlsRotationMode.LookingDirection;
        }

        var axes = snapshot.MovementAxes;
        var lengthSquared = axes.LengthSquared();
        if (lengthSquared > 1f)
        {
            axes /= MathF.Sqrt(lengthSquared);
        }

        var gait = snapshot.WalkHeld
            ? AlsGait.Walking
            : snapshot.SprintHeld
                ? AlsGait.Sprinting
                : AlsGait.Running;
        _command = new AlsLocomotionCommand(
            axes,
            viewYaw,
            viewPitch,
            viewYaw,
            viewPitch,
            gait,
            _stance,
            snapshot.AimHeld ? AlsRotationMode.Aiming : _rotationMode,
            snapshot.JumpPressed ? (byte)1 : (byte)0) { RequestedOverlay = snapshot.Overlay, CancelAction = snapshot.CancelActionPressed };
        _capturedFrameId = frameId;
    }

    public AlsLocomotionCommand GetCommand(long frameId)
    {
        if (frameId != _capturedFrameId || _capturedFrameId == 0)
        {
            throw new InvalidOperationException(
                $"Player input command frame {frameId} was requested before its exact frame capture. " +
                $"Last captured frame is {_capturedFrameId}.");
        }
        return _command;
    }

    private static AlsPlayerInputSnapshot ReadGodotSnapshot()
    {
        var movement = Input.GetVector(
            "move_left",
            "move_right",
            "move_forward",
            "move_back");
        return new AlsPlayerInputSnapshot(
            new NumericsVector2(movement.X, -movement.Y),
            Input.IsActionPressed("walk"),
            Input.IsActionPressed("sprint"),
            Input.IsActionJustPressed("crouch_toggle"),
            Input.IsActionJustPressed("jump"),
            Input.IsActionJustPressed("rotation_mode_toggle"),
            Input.IsActionPressed("aim"));
    }
}
