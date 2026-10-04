namespace GodotAls.Core.Sync;

public enum AlsSequenceEvaluatorReinitialization : byte { NoReset, StartPosition, ExplicitTime }
public readonly record struct AlsSequenceEvaluatorTick(float Time, float PlayRate, float ExplicitTime,
    bool Advances, bool Reinitialized);

/// <summary>UE SequenceEvaluator's update before registration with the common synchronizer.
/// The caller owns the occurrence clock/relevance history and commits the eventual Sync result.
/// Teleport produces a zero-rate independent tick; a named group or Graph method forces advancement.</summary>
public static class AlsSequenceEvaluatorPreparation
{
    public static AlsSequenceEvaluatorTick Prepare(float explicitTime, float previousTime, float duration,
        float frameDelta, float rateScale, bool looping, bool teleport, bool hasGroupName, bool graphMethod,
        bool reinitialized, AlsSequenceEvaluatorReinitialization reinitialization, float startPosition = 0)
    {
        if (!float.IsFinite(explicitTime) || !float.IsFinite(previousTime) || !float.IsFinite(duration) || duration <= 0 ||
            !float.IsFinite(frameDelta) || frameDelta < 0 || !float.IsFinite(rateScale) || !float.IsFinite(startPosition) ||
            !Enum.IsDefined(reinitialization)) throw new ArgumentException("Invalid SequenceEvaluator update input.");
        var current = System.Math.Clamp(explicitTime, 0, duration);
        var advances = !teleport || hasGroupName || graphMethod;
        if (!advances) return new(current, 0, current, false, reinitialized);
        var time = previousTime;
        if (reinitialized)
        {
            time = reinitialization switch { AlsSequenceEvaluatorReinitialization.StartPosition => startPosition,
                AlsSequenceEvaluatorReinitialization.ExplicitTime => current, _ => time };
            time = System.Math.Clamp(time, 0, duration);
        }
        var jump = current - time;
        if (looping && MathF.Abs(jump) > duration * .5f)
            jump = jump > 0 ? jump - duration : jump + duration;
        if (jump == 0) time = current;
        // FMath::IsNearlyZero uses UE_SMALL_NUMBER here, independently on
        // delta and asset rate. Keep divide then multiply in the Sync tick.
        var rate = MathF.Abs(frameDelta) <= 1e-8f || MathF.Abs(rateScale) <= 1e-8f
            ? 0 : jump / (frameDelta * rateScale);
        if (!float.IsFinite(rate)) throw new ArgumentException("Nonfinite SequenceEvaluator rate.");
        return new(time, rate, current, true, reinitialized);
    }
}
