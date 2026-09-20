using M = System.Math;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsFootSpringState(float Velocity, float PreviousTarget, bool Valid);
public readonly record struct AlsFootOffsetLocationState(bool Initialized, float OffsetZ,
    AlsFootSpringState Spring, AlsDoubleVector FootLocation)
{
    public bool ContactApplied { get; init; }
    public bool ContactConstrained { get; init; }
}

// Native UE component space, centimeters. Settings are supplied by the graph
// owner, not inferred from the V4 virtual-bone controls.
public readonly record struct AlsFootOffsetLocationInput(float DeltaTime, double PelvisZ,
    AlsDoubleVector ThighLocation, AlsDoubleVector TargetLocation, float OffsetZ, float PelvisOffset,
    float LegLength, float MinPelvisToFootDistance, float MaxLegStretchRatio,
    float Frequency, float DampingRatio, float TargetVelocityAmount);

public static class AlsFootOffsetLocationModel
{
    public static AlsFootOffsetLocationState Evaluate(in AlsFootOffsetLocationState previous,
        in AlsFootOffsetLocationInput input)
    {
        Validate(input);
        var target = M.Max(M.Min(input.OffsetZ,
            (float)input.PelvisZ - input.MinPelvisToFootDistance - (float)input.TargetLocation.Z), input.PelvisOffset);
        var spring = previous.Initialized ? previous.Spring : default;
        var offset = previous.Initialized ? previous.OffsetZ : target;
        if (previous.Initialized)
        {
            if (!float.IsFinite(offset) || !float.IsFinite(spring.Velocity) || !float.IsFinite(spring.PreviousTarget))
                throw new ArgumentException("Nonfinite foot spring history.");
            if (input.DeltaTime > 1e-8f)
            {
                if (!spring.Valid) { offset = target; spring = new(0, target, true); }
                else
                {
                    var velocity = spring.Velocity;
                    var targetVelocity = (target - spring.PreviousTarget) * M.Clamp(input.TargetVelocityAmount, 0, 1) / input.DeltaTime;
                    AlsRefactoredSpring.Evaluate(ref offset, ref velocity, target, targetVelocity, input.DeltaTime, input.Frequency, input.DampingRatio);
                    spring = new(velocity, target, true);
                }
            }
        }
        var location = input.TargetLocation + new AlsDoubleVector(0, 0, offset);
        var leg = location - input.ThighLocation;
        var maximum = input.LegLength * input.MaxLegStretchRatio;
        // Native clamps AFTER interpolation, without writing the clamp back to
        // spring history. The length product itself is float, vectors are double.
        if (maximum < 1e-4f) leg = default;
        else if (leg.LengthSquared > (double)maximum * maximum) leg *= maximum / M.Sqrt(leg.LengthSquared);
        return new(true, offset, spring, input.ThighLocation + leg);
    }


    private static void Validate(in AlsFootOffsetLocationInput input)
    {
        if (!float.IsFinite(input.DeltaTime) || input.DeltaTime < 0 || !float.IsFinite((float)input.PelvisZ) ||
            !input.ThighLocation.IsFinite || !input.TargetLocation.IsFinite || !float.IsFinite((float)input.TargetLocation.Z) ||
            !float.IsFinite(input.OffsetZ) || !float.IsFinite(input.PelvisOffset) || !float.IsFinite(input.LegLength) || input.LegLength < 0 ||
            !float.IsFinite(input.MinPelvisToFootDistance) || !float.IsFinite(input.MaxLegStretchRatio) || input.MaxLegStretchRatio < 0 ||
            !float.IsFinite(input.Frequency) || input.Frequency < 0 || !float.IsFinite(input.DampingRatio) || input.DampingRatio < 0 ||
            !float.IsFinite(input.TargetVelocityAmount)) throw new ArgumentException("Invalid foot offset location input.");
    }
}
