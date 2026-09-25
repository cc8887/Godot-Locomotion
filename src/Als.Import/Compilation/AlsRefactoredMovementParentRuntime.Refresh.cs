using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed partial class AlsRefactoredMovementParentRuntime
{
    private readonly AlsRefactoredMovementSettings? _settings;
    private AlsRefactoredMovementInput _input;
    private bool _hasInput;
    private AlsRefactoredMovementState _movementCandidate;
    public AlsRefactoredMovementState CommittedMovement { get; private set; } = AlsRefactoredMovementState.Initial;
    public AlsRefactoredMovementState MovementCandidate { get { Check(_identity.FrameId); return _movementCandidate; } }

    public AlsRefactoredDirectionInput DirectionInput(float feetCrossing)
    {
        Check(_identity.FrameId);
        if (!float.IsFinite(feetCrossing)) throw new ArgumentException("Invalid committed FeetCrossing curve.");
        var s = _movementCandidate; var d = s.Direction;
        return new(d == AlsRefactoredMovementDirection.Forward, d == AlsRefactoredMovementDirection.Backward,
            d == AlsRefactoredMovementDirection.Left, d == AlsRefactoredMovementDirection.Right, s.HipsLock, feetCrossing);
    }
    public AlsRefactoredMovementPlayerInput PlayerInput(bool crouching = false)
    {
        Check(_identity.FrameId); var s = _movementCandidate;
        return new(s.StandingRate, s.CrouchingRate, crouching ? s.CrouchingStride : s.StandingStride, s.WalkRun);
    }
    public AlsRefactoredForwardInput ForwardInput()
    {
        Check(_identity.FrameId); RequireInput();
        return new(_input.Gait, _movementCandidate.SprintBlock, _movementCandidate.SprintAcceleration);
    }

    public void Prepare(AlsFrameIdentity identity, in AlsRefactoredMovementInput input, bool initializeInstance = false)
    {
        if (_settings is null) throw new InvalidOperationException("Full movement settings are required.");
        AlsRefactoredMovementModel.Validate(input);
        Prepare(identity, initializeInstance); _input = input; _hasInput = true;
    }

    public void ActivatePivot(long frame)
    {
        Check(frame); RequireInput(); ActivatePivot(frame, _input.Speed, _settings!.PivotThreshold);
    }

    public void InitializeGrounded(long frame)
    { Check(frame); _movementCandidate = _movementCandidate with { VelocityInitialized = false }; }
    public void InitializeLean(long frame)
    { Check(frame); _movementCandidate = _movementCandidate with { Lean = default }; }
    public void RefreshGrounded(long frame)
    {
        Check(frame);
        try
        {
            RequireInput();
            _movementCandidate = AlsRefactoredMovementModel.RefreshGrounded(_movementCandidate, _input,
                _settings!.VelocityBlendHalfLife, _settings.LeanHalfLife);
            ValidateMovement();
        }
        catch { _faulted = true; throw; }
    }

    private void Refresh(AlsRefactoredStanceFunction function)
    {
        RequireInput(); var settings = _settings!; var speed = _input.Speed / _input.Scale;
        switch (function)
        {
            case AlsRefactoredStanceFunction.RefreshGroundedMovement:
                var angle = AlsRefactoredMovementModel.VelocityYaw(_input);
                _movementCandidate = AlsRefactoredMovementModel.RefreshGroundedMovement(_movementCandidate, _input,
                    new(settings.YawForward.Sample(angle), settings.YawBackward.Sample(angle), settings.YawLeft.Sample(angle), settings.YawRight.Sample(angle)));
                break;
            case AlsRefactoredStanceFunction.RefreshStandingMovement:
                _movementCandidate = AlsRefactoredMovementModel.RefreshStanding(_movementCandidate, _input,
                    settings.WalkStride.Sample(speed), settings.RunStride.Sample(speed), settings.WalkSpeed, settings.RunSpeed, settings.SprintSpeed);
                break;
            case AlsRefactoredStanceFunction.RefreshCrouchingMovement:
                _movementCandidate = AlsRefactoredMovementModel.RefreshCrouching(_movementCandidate, _input, settings.CrouchStride.Sample(speed), settings.CrouchSpeed);
                break;
            default: throw new ArgumentException("Unsupported movement refresh callback.");
        }
        ValidateMovement();
    }
    private void RequireInput()
    {
        if (!_hasInput || _settings is null) { _faulted = true; throw new InvalidOperationException("No movement inputs for this frame."); }
    }
    private void ValidateMovement()
    {
        var s = _movementCandidate;
        if (!float.IsFinite(s.VelocityBlend.X) || !float.IsFinite(s.VelocityBlend.Y) || !float.IsFinite(s.VelocityBlend.Z) || !float.IsFinite(s.VelocityBlend.W) ||
            !float.IsFinite(s.Lean.X) || !float.IsFinite(s.Lean.Y) || !float.IsFinite(s.StandingStride) || !float.IsFinite(s.StandingRate) ||
            !float.IsFinite(s.SprintTime) || !float.IsFinite(s.SprintAcceleration) || !float.IsFinite(s.CrouchingStride) || !float.IsFinite(s.CrouchingRate))
            throw new ArgumentException("Nonfinite movement result.");
    }
}
