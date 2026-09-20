using System.Numerics;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsLandPredictionSettings(float FallThreshold, float MinimumVerticalSpeed,
    float MaximumVerticalSpeed, float RangeStartSpeed, float RangeEndSpeed, float MinimumDistance, float MaximumDistance)
{
    public static AlsLandPredictionSettings Reference => new(-2, -40, -2, 0, -40, .5f, 20);
    public bool IsValid => float.IsFinite(FallThreshold) && FallThreshold < 0 &&
        float.IsFinite(MinimumVerticalSpeed) && float.IsFinite(MaximumVerticalSpeed) &&
        MinimumVerticalSpeed < MaximumVerticalSpeed && MaximumVerticalSpeed < 0 &&
        float.IsFinite(RangeStartSpeed) && float.IsFinite(RangeEndSpeed) && RangeStartSpeed > RangeEndSpeed &&
        float.IsFinite(MinimumDistance) && float.IsFinite(MaximumDistance) && MinimumDistance > 0 && MaximumDistance >= MinimumDistance;
}

public sealed class AlsLandPredictionModel
{
    public AlsLandPredictionSettings Settings { get; }
    private readonly AlsMovementInputCurve _curve;
    public AlsLandPredictionModel(AlsLandPredictionSettings settings, AlsMovementInputCurve curve)
    {
        if (!settings.IsValid) throw new ArgumentException("Invalid landing prediction settings.");
        Settings = settings; _curve = curve ?? throw new ArgumentNullException(nameof(curve));
    }

    // Uses Godot world velocity in m/s; normalization and map match the source Blueprint.
    public static bool TryCreateMotion(Vector3 velocity, in AlsLandPredictionSettings settings, out Vector3 motion)
    {
        if (!settings.IsValid || !float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y) || !float.IsFinite(velocity.Z))
            throw new ArgumentException("Invalid landing prediction input.");
        motion = default;
        if (velocity.Y >= settings.FallThreshold) return false;
        var vertical = System.Math.Clamp((double)velocity.Y, settings.MinimumVerticalSpeed, settings.MaximumVerticalSpeed);
        var length = System.Math.Sqrt((double)velocity.X * velocity.X + vertical * vertical + (double)velocity.Z * velocity.Z);
        var alpha = System.Math.Clamp(((double)velocity.Y - settings.RangeStartSpeed) / (settings.RangeEndSpeed - settings.RangeStartSpeed), 0, 1);
        var distance = settings.MinimumDistance + (settings.MaximumDistance - settings.MinimumDistance) * alpha;
        motion = new((float)(velocity.X / length * distance), (float)(vertical / length * distance), (float)(velocity.Z / length * distance));
        return true;
    }

    public float Evaluate(float fallSpeed, in AlsLandPredictionSample sample, float maskLandPrediction)
    {
        if (!float.IsFinite(fallSpeed) || !float.IsFinite(maskLandPrediction)) throw new ArgumentException("Invalid landing prediction input.");
        if (fallSpeed >= Settings.FallThreshold) return 0;
        if (sample.Queried != 1) throw new InvalidOperationException("Falling frame is missing its capsule prediction query.");
        if (sample.BlockingHit > 1 || sample.StartedPenetrating > 1 || sample.Walkable > 1)
            throw new ArgumentException("Invalid landing hit flags.");
        if (sample.BlockingHit == 0 || sample.StartedPenetrating == 1 || sample.Walkable == 0) return 0;
        if (!float.IsFinite(sample.Time) || sample.Time < 0 || sample.Time > 1)
            throw new ArgumentException("Invalid landing hit time.");
        var value = _curve.Sample(sample.Time);
        // UE Lerp does not clamp Alpha. Preserve the authored mask's actual value.
        return (float)(value + (0d - value) * maskLandPrediction);
    }
}
