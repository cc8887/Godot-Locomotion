using System.Numerics;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsGroundedAnimationInput(AlsFrameIdentity Identity, Vector4 VelocityBlend,
    Vector3 RelativeAcceleration, float DiagonalScale, float WalkRunBlend, float Stride,
    float StandingPlayRate, float CrouchingPlayRate, bool ShouldMove);
public readonly record struct AlsGroundedInputUpdate(AlsGroundedAnimationInput State, Vector2 Lean, bool Updated);

// One global update before graph traversal. Hidden animation nodes do not stop this update.
public sealed class AlsGroundedAnimationInputModel(AlsGroundedInputFunctions functions, AlsGroundedRateFunctions rates,
    AlsStandingMovementSettings movementSettings, float velocityInterpSpeed, float leanInterpSpeed,
    AlsGroundedAnimationInput initialState)
{
    public AlsGroundedAnimationInput InitialState { get; } = initialState;

    public AlsGroundedInputUpdate Evaluate(in AlsFrameInput frame, in AlsStandingMovementInput movement, AlsGait gait,
        in AlsGroundedAnimationInput previous, Vector2 committedGlobalLean, in AlsAnimationInputFeedback feedback, float meshVerticalScale,
        AlsMovementStateInput? movementState = null)
    {
        if (frame.Identity.SlotGeneration == 0 || frame.Identity != movement.Identity || frame.Floor.IsGrounded > 1 ||
            !float.IsFinite(frame.DeltaTime) || frame.DeltaTime <= 0 ||
            movement with { ControlRelativeYawDegrees = 0 } != AlsStandingMovementInputModel.Evaluate(frame.Identity, frame.ActualVelocity,
                movement.MovementInputAmount, movementSettings)) throw new ArgumentException("Ground input differs from the actual movement frame.");
        if (previous.Identity.SlotGeneration != 0 && (previous.Identity.CharacterId != frame.Identity.CharacterId ||
            previous.Identity.SlotGeneration != frame.Identity.SlotGeneration || previous.Identity.FrameId >= frame.Identity.FrameId))
            throw new ArgumentException("Invalid ground input history.");
        feedback.Validate(previous.Identity);
        var next = previous with { Identity = frame.Identity };
        // UpdateGraph only assigns ShouldMove in its Grounded switch branch.
        // Floor inference remains for older ground/air-only callers; production
        // supplies the animation state, independently of the physics hit.
        if ((movementState ?? (frame.Floor.IsGrounded == 1 ? AlsMovementStateInput.Grounded : AlsMovementStateInput.InAir)) != AlsMovementStateInput.Grounded)
            return new(next, committedGlobalLean, false);
        next = next with { ShouldMove = movement.ShouldMove };
        if (!next.ShouldMove) return new(next, committedGlobalLean, false);
        if (!Matrix4x4.Decompose(frame.CharacterTransform, out var scale, out var rotation, out _) ||
            !float.IsFinite(scale.LengthSquared()) || scale.X <= 0 || scale.Y <= 0 || scale.Z <= 0)
            throw new ArgumentException("Invalid actor transform for ground input.");
        var velocity = AlsGroundedInputFunctions.InterpolateVelocity(previous.VelocityBlend,
            functions.VelocityBlend(frame.ActualVelocity, rotation), frame.DeltaTime, velocityInterpSpeed);
        var diagonal = functions.DiagonalScale(velocity);
        var acceleration = functions.RelativeAcceleration(frame.ActualAcceleration, frame.ActualVelocity, rotation,
            frame.MaxAcceleration, frame.MaxBrakingDeceleration);
        var lean = AlsMovementInputFunctions.InterpolateLean(committedGlobalLean, new(acceleration.X, -acceleration.Z), frame.DeltaTime, leanInterpSpeed);
        var walkRun = AlsGroundedInputFunctions.WalkRunBlend(gait);
        var evaluatedRates = rates.Evaluate(movement.Speed, feedback.WeightGait.Present ? feedback.WeightGait.Value : 0,
            feedback.BasePoseCrouch.Present ? feedback.BasePoseCrouch.Value : 0, meshVerticalScale);
        return new(new(frame.Identity, velocity, acceleration, diagonal, walkRun, evaluatedRates.Stride,
            evaluatedRates.StandingPlayRate, evaluatedRates.CrouchingPlayRate, true), lean, true);
    }
}
