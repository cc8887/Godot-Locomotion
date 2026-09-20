namespace GodotAls.Core.Locomotion;

public enum AlsLocomotionTimingPolicy : byte
{
    Model,
    // Grounded standing results carry pending NaNs until CompleteStandingSourceTiming().
    StandingSourceGraph,
    // Every animation state is completed from the single movement graph owner.
    CompleteMovementGraph,
}

// A result summary, not a playback clock or a source identity.
public readonly record struct AlsLocomotionSourceTiming(float Stride, float PlayRate, float Phase)
{
    public static float NormalizeLoopingPhase(float sourceRatio)
    {
        if (!float.IsFinite(sourceRatio) || sourceRatio < 0 || sourceRatio > 1)
            throw new ArgumentOutOfRangeException(nameof(sourceRatio));
        // Native Sync can retain the inclusive endpoint; cyclic result summaries use [0, 1).
        return sourceRatio == 1 ? 0 : sourceRatio;
    }
}
