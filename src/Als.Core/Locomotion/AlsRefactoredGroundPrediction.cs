using GodotAls.Core.Contracts;
using M = System.Math;

namespace GodotAls.Core.Locomotion;

// Native world axes, centimeters and cm/s. Block comes from the instance's
// cached GroundPredictionBlock curve before this graph's current evaluation.
public readonly record struct AlsGroundPredictionInput(AlsFrameIdentity Identity, AlsDoubleVector Location,
    AlsDoubleVector Velocity, float Scale, float CapsuleRadius, float CapsuleHalfHeight,
    float WalkableFloorCosine, float Block);
public readonly record struct AlsGroundPredictionQuery(AlsFrameIdentity Identity, ulong Serial, bool Enabled,
    float Allowance, AlsDoubleVector Start, AlsDoubleVector End, float Radius, float HalfHeight, float WalkableFloorCosine);
public readonly record struct AlsGroundPredictionObservation(AlsGroundPredictionQuery Query,
    bool Blocking, bool StartedPenetrating, float Time, AlsDoubleVector Normal);
public readonly record struct AlsRefactoredAnimationFeedback(AlsRefactoredPoseCurveHistory Pose, float GroundPredictionBlock);
public readonly record struct AlsRefactoredGroundPredictionSample(byte Captured,
    AlsRefactoredAnimationFeedback Feedback, AlsGroundPredictionObservation Observation);

// UAlsAnimationInstance::RefreshGroundPrediction, split at its physics call.
// This deliberately cannot consume the V4 AlsLandPredictionSample contract.
public sealed class AlsRefactoredGroundPrediction
{
    private readonly AlsMovementInputCurve _amount;
    public AlsRefactoredGroundPrediction(AlsMovementInputCurve amount) =>
        _amount = amount ?? throw new ArgumentNullException(nameof(amount));

    public AlsGroundPredictionQuery Prepare(in AlsGroundPredictionInput input, ulong serial)
    {
        if (input.Identity.SlotGeneration == 0 || serial == 0 || !input.Location.IsFinite || !input.Velocity.IsFinite ||
            !float.IsFinite(input.Scale) || input.Scale <= 0 || !float.IsFinite(input.CapsuleRadius) || input.CapsuleRadius <= 0 ||
            !float.IsFinite(input.CapsuleHalfHeight) || input.CapsuleHalfHeight < input.CapsuleRadius ||
            !float.IsFinite(input.WalkableFloorCosine) || input.WalkableFloorCosine is < 0 or > 1 || !float.IsFinite(input.Block))
            throw new ArgumentException("Invalid Refactored ground prediction input.");
        // RefreshInAir first converts the FVector Z to float; it uses that float
        // for the threshold and mapping, but normalizes the original double vector.
        var vertical = (float)input.Velocity.Z;
        if (!float.IsFinite(vertical)) throw new ArgumentException("Vertical velocity exceeds native float range.");
        var allowance = 1f - M.Clamp(input.Block, 0, 1);
        var enabled = vertical <= -200f && allowance > 1e-4f;
        var end = input.Location;
        if (enabled)
        {
            var direction = new AlsDoubleVector(input.Velocity.X, input.Velocity.Y, M.Clamp(input.Velocity.Z, -4000d, -200d));
            direction *= 1d / M.Sqrt(direction.LengthSquared);
            // The locked Win64 Development reference uses /fp:fast: its constant
            // range division becomes multiplication by the rounded float reciprocal.
            // Preserve that rounding before Lerp (verified by native sweep samples).
            var alpha = M.Clamp((vertical - -200f) * (1f / (-4000f - -200f)), 0, 1);
            var distance = 150f + (2000f - 150f) * alpha;
            end += direction * distance * input.Scale;
            if (!end.IsFinite) throw new ArgumentException("Ground prediction sweep overflow.");
        }
        return new(input.Identity, serial, enabled, allowance, input.Location, end,
            input.CapsuleRadius, input.CapsuleHalfHeight, input.WalkableFloorCosine);
    }

    public float Evaluate(in AlsGroundPredictionQuery query, in AlsGroundPredictionObservation observation)
    {
        if (observation.Query != query) throw new ArgumentException("Prediction response belongs to another candidate.");
        if (query.Identity.SlotGeneration == 0 || query.Serial == 0) throw new ArgumentException("Unissued prediction query.");
        if (!query.Enabled) return 0;
        if (!observation.Blocking) return 0;
        if (!float.IsFinite(observation.Time) || observation.Time is < 0 or > 1 || !observation.Normal.IsFinite)
            throw new ArgumentException("Invalid native prediction hit.");
        if (observation.Normal.Z < query.WalkableFloorCosine) return 0;
        // Refactored explicitly accepts initial penetration with a walkable normal.
        // The authored response curve's output is not clamped here.
        return _amount.Sample(observation.Time) * query.Allowance;
    }
}
