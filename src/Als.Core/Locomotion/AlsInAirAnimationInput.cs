using System.Numerics;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsInAirAnimationInput(AlsFrameIdentity Identity, float FallSpeed,
    float LandPrediction, Vector2 Lean, float Speed);

// The enclosing final-pose owner supplies this snapshot from the previous accepted frame.
// Absence is retained even though AnimInstance.GetCurveValue returns zero for this reader.
public readonly record struct AlsAnimationInputFeedback(AlsFrameIdentity Identity, bool HasFrame,
    AlsInertialCurve LandPredictionMask)
{
    public AlsInertialCurve WeightGait { get; init; }
    public AlsInertialCurve BasePoseCrouch { get; init; }
    public AlsInertialCurve EnableTransition { get; init; }
    public AlsInertialCurve FeetPosition { get; init; }
    public AlsInertialCurve FeetCrossing { get; init; }
    public AlsInertialCurve HipOrientationBias { get; init; }
    public void Validate(AlsFrameIdentity previous)
    {
        if (previous.SlotGeneration == 0 ? HasFrame : !HasFrame || Identity != previous)
            throw new ArgumentException("Animation input feedback is not the previous committed frame.");
        ValidateCurve(LandPredictionMask, HasFrame); ValidateCurve(WeightGait, HasFrame); ValidateCurve(BasePoseCrouch, HasFrame);
        ValidateCurve(EnableTransition, HasFrame);
        ValidateCurve(FeetPosition, HasFrame); ValidateCurve(FeetCrossing, HasFrame); ValidateCurve(HipOrientationBias, HasFrame);
    }

    // These transition rules use AnimInstance.GetCurveValue, which returns zero
    // for an absent curve. Keep presence in the snapshot for other consumers.
    public AlsGroundedRuleInput ApplyTo(in AlsGroundedRuleInput rules) => rules with
    {
        BasePoseClf = BasePoseCrouch.Present ? BasePoseCrouch.Value : 0,
        FeetPosition = FeetPosition.Present ? FeetPosition.Value : 0,
        FeetCrossing = FeetCrossing.Present ? FeetCrossing.Value : 0,
        HipBias = HipOrientationBias.Present ? HipOrientationBias.Value : 0,
    };

    public static AlsAnimationInputFeedback FromCompletedFrame(AlsFrameIdentity identity,
        ReadOnlySpan<string> names, ReadOnlySpan<AlsInertialCurve> curves)
    {
        if (identity.SlotGeneration == 0 || names.Length != curves.Length) throw new ArgumentException("Invalid completed curve frame.");
        var mask = default(AlsInertialCurve); var gait = default(AlsInertialCurve); var crouch = default(AlsInertialCurve);
        var transition = default(AlsInertialCurve); var found = 0;
        var feet = default(AlsInertialCurve); var crossing = default(AlsInertialCurve); var hips = default(AlsInertialCurve);
        for (var i = 0; i < names.Length; i++)
        {
            var bit = names[i] switch { "Mask_LandPrediction" => 1, "Weight_Gait" => 2, "BasePose_CLF" => 4, "Enable_Transition" => 8,
                "Feet_Position" => 16, "Feet_Crossing" => 32, "HipOrientation_Bias" => 64, _ => 0 };
            if (bit == 0) continue;
            if ((found & bit) != 0) throw new ArgumentException("Duplicate input curve in completed frame.");
            found |= bit; ValidateCurve(curves[i], true);
            switch (bit)
            {
                case 1: mask = curves[i]; break;
                case 2: gait = curves[i]; break;
                case 4: crouch = curves[i]; break;
                case 8: transition = curves[i]; break;
                case 16: feet = curves[i]; break;
                case 32: crossing = curves[i]; break;
                case 64: hips = curves[i]; break;
            }
        }
        return new(identity, true, mask) { WeightGait = gait, BasePoseCrouch = crouch, EnableTransition = transition,
            FeetPosition = feet, FeetCrossing = crossing, HipOrientationBias = hips };
    }
    private static void ValidateCurve(AlsInertialCurve curve, bool hasFrame)
    {
        if (curve.Present && !float.IsFinite(curve.Value)) throw new ArgumentException("Non-finite animation input curve.");
        if (!hasFrame && (curve.Present || curve.Value != 0)) throw new ArgumentException("Cold input feedback contains an uncommitted curve.");
    }
}

public sealed class AlsInAirAnimationInputModel
{
    private readonly AlsMovementInputFunctions _functions;
    private readonly AlsLandPredictionModel _landing;
    private readonly float _interpSpeed;
    public AlsInAirAnimationInputModel(AlsMovementInputFunctions functions, AlsLandPredictionModel landing, float interpSpeed)
    {
        if (!float.IsFinite(interpSpeed) || interpSpeed < 0) throw new ArgumentOutOfRangeException(nameof(interpSpeed));
        _functions = functions ?? throw new ArgumentNullException(nameof(functions));
        _landing = landing ?? throw new ArgumentNullException(nameof(landing)); _interpSpeed = interpSpeed;
    }

    public AlsInAirAnimationInput Evaluate(in AlsFrameInput frame, Vector2 committedGlobalLean, in AlsAnimationInputFeedback feedback,
        AlsMovementStateInput? movementState = null)
    {
        if (frame.Floor.IsGrounded > 1 ||
            (movementState ?? (frame.Floor.IsGrounded == 0 ? AlsMovementStateInput.InAir : AlsMovementStateInput.Grounded)) != AlsMovementStateInput.InAir ||
            !float.IsFinite(frame.DeltaTime) || frame.DeltaTime <= 0)
            throw new ArgumentException("Expected a valid airborne input frame.");
        if (frame.Identity.SlotGeneration == 0 || feedback.HasFrame &&
            (feedback.Identity.SlotGeneration != frame.Identity.SlotGeneration || feedback.Identity.CharacterId != frame.Identity.CharacterId ||
             feedback.Identity.FrameId >= frame.Identity.FrameId))
            throw new ArgumentException("Foreign or future air input feedback.");
        if (!feedback.HasFrame) feedback.Validate(default);
        // The source reads actor rotation, not inverse component scale or view rotation.
        if (!Matrix4x4.Decompose(frame.CharacterTransform, out var scale, out var rotation, out _) ||
            !float.IsFinite(scale.LengthSquared()) || scale.X <= 0 || scale.Y <= 0 || scale.Z <= 0 ||
            !float.IsFinite(rotation.LengthSquared()) || rotation.LengthSquared() < .5f)
            throw new ArgumentException("Invalid actor transform for air input.");
        var velocity = frame.ActualVelocity;
        var local = Vector3.Transform(velocity, Quaternion.Conjugate(Quaternion.Normalize(rotation)));
        var fallSpeed = velocity.Y;
        var mask = feedback.HasFrame && feedback.LandPredictionMask.Present ? feedback.LandPredictionMask.Value : 0;
        var prediction = _landing.Evaluate(fallSpeed, frame.LandPrediction, mask);
        var target = _functions.InAirLean(local, fallSpeed);
        var lean = AlsMovementInputFunctions.InterpolateLean(committedGlobalLean, target, frame.DeltaTime, _interpSpeed);
        var speed = (float)System.Math.Sqrt((double)velocity.X * velocity.X + (double)velocity.Z * velocity.Z);
        if (!float.IsFinite(speed)) throw new ArgumentException("Non-finite air movement speed.");
        return new(frame.Identity, fallSpeed, prediction, lean, speed);
    }
}
