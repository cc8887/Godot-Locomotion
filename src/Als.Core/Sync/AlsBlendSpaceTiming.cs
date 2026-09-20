using GodotAls.Core.Contracts;

namespace GodotAls.Core.Sync;

public enum AlsBlendSpaceNotifyMode : byte { AllAnimations, HighestWeightedAnimation, None }

/// <summary>A resolved, valid sample in native cache order. CachedPlayRate is used for effective
/// length; SampleRateScale is used for sample timing. They must not be collapsed into one rate.</summary>
public readonly record struct AlsBlendSpaceTimingSample(int SampleId, int AnimationId,
    float Weight, float DurationSeconds, float AssetRateScale, float SampleRateScale,
    float CachedPlayRate, bool HasMarkers, float MarkerPreviousTime = 0, float MarkerTime = 0);

public readonly record struct AlsBlendSpaceTimingDescription(float EffectiveLengthSeconds,
    int HighestWeightIndex, int HighestMarkerWeightIndex);

/// <summary>NotifyEligible selects a sample for extraction, not queue admission or callback dispatch.</summary>
public readonly record struct AlsBlendSpaceSampleTime(int SampleId, int AnimationId,
    float PreviousTimeSeconds, float TimeSeconds, float AdvanceSeconds, bool NotifyEligible);

public static partial class AlsSyncRuntime
{
    public const int MaxBlendSpaceTimingSamples = 128;
    private const float BlendSampleWeightThreshold = .00001f;

    /// <summary>UE BlendSpace effective length and highest-sample selection. Valid resolved samples
    /// only; mirroring and single-frame evaluators are not represented by this contract.</summary>
    public static bool TryDescribeBlendSpaceTiming(ReadOnlySpan<AlsBlendSpaceTimingSample> samples,
        bool legacyLength, out AlsBlendSpaceTimingDescription description, out AlsP5FailureCode failure)
    {
        description = default; failure = AlsP5FailureCode.InvalidSyncGroup;
        if (!ValidBlendSamples(samples)) return false;
        var aggregate = 0f;
        var highest = -1; var marker = -1;
        var highestWeight = -1f; var markerWeight = 1.17549435E-38f;
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = samples[i];
            var weight = System.Math.Clamp(sample.Weight, 0, 1);
            var rate = sample.AssetRateScale * sample.CachedPlayRate;
            var contribution = legacyLength
                ? sample.DurationSeconds / (rate != 0 ? MathF.Abs(rate) : 1)
                : sample.DurationSeconds != 0 ? rate / sample.DurationSeconds : 0;
            aggregate += contribution * weight;
            if (weight > highestWeight) { highest = i; highestWeight = weight; }
            if (sample.HasMarkers && weight > markerWeight) { marker = i; markerWeight = weight; }
        }
        var length = legacyLength || aggregate == 0 ? aggregate : 1 / aggregate;
        if (!float.IsFinite(length)) { failure = AlsP5FailureCode.NonFiniteOutput; return false; }
        description = new(length, highest, marker); failure = AlsP5FailureCode.None;
        return true;
    }

    /// <summary>Length-mode leader advancement, after sample/filter evaluation and optional group
    /// resync. This does not select a group leader or implement marker synchronization.</summary>
    public static bool TryAdvanceBlendSpaceLength(float normalizedTime, float effectiveLengthSeconds,
        float tickDeltaSeconds, bool looping, out float normalizedResult, out AlsP5FailureCode failure)
    {
        normalizedResult = normalizedTime; failure = AlsP5FailureCode.InvalidSyncGroup;
        if (!float.IsFinite(normalizedTime) || normalizedTime is < 0 or > 1 ||
            !float.IsFinite(effectiveLengthSeconds) || effectiveLengthSeconds < 0 ||
            !float.IsFinite(tickDeltaSeconds)) return false;
        var time = normalizedTime * effectiveLengthSeconds + tickDeltaSeconds;
        if (!float.IsFinite(time)) { failure = AlsP5FailureCode.NonFiniteOutput; return false; }
        // UE retains the exact endpoint; only a value strictly outside the interval wraps.
        if (time < 0 || time > effectiveLengthSeconds)
        {
            if (looping)
            {
                time = effectiveLengthSeconds != 0 ? time % effectiveLengthSeconds : 0;
                if (time < 0) time += effectiveLengthSeconds;
            }
            else time = System.Math.Clamp(time, 0, effectiveLengthSeconds);
        }
        normalizedResult = effectiveLengthSeconds != 0 ? time / effectiveLengthSeconds : 0;
        failure = AlsP5FailureCode.None; return true;
    }

    /// <summary>Final sample-time stage of a non-evaluator BlendSpace tick. Marker times, when used,
    /// must already have been resolved by the caller's marker runtime. No owned clocks or queues.</summary>
    public static bool TryFinalizeBlendSpaceTimes(ReadOnlySpan<AlsBlendSpaceTimingSample> samples,
        float previousRatio, float ratio, float tickDeltaSeconds, bool markerSync,
        AlsBlendSpaceNotifyMode notifyMode, Span<AlsBlendSpaceSampleTime> output,
        out int count, out AlsP5FailureCode failure)
    {
        count = 0; failure = AlsP5FailureCode.InvalidSyncGroup;
        if (!float.IsFinite(previousRatio) || !float.IsFinite(ratio) || !float.IsFinite(tickDeltaSeconds) ||
            notifyMode > AlsBlendSpaceNotifyMode.None || !ValidBlendSamples(samples)) return false;
        var previous = System.Math.Clamp(previousRatio, 0, 1);
        var current = System.Math.Clamp(ratio, 0, 1);
        var highest = -1; var highestWeight = -1f; var active = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var weight = System.Math.Clamp(samples[i].Weight, 0, 1);
            if (weight > highestWeight) { highest = i; highestWeight = weight; }
            if (samples[i].Weight > BlendSampleWeightThreshold) active++;
        }
        if (output.Length < active) return false;
        // Stage every result before writing caller-owned memory, including late arithmetic failures.
        Span<AlsBlendSpaceSampleTime> staged = stackalloc AlsBlendSpaceSampleTime[samples.Length];
        var written = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = samples[i];
            if (sample.Weight <= BlendSampleWeightThreshold) continue;
            var rate = sample.AssetRateScale * sample.SampleRateScale;
            var start = markerSync && sample.HasMarkers ? sample.MarkerPreviousTime
                : (rate >= 0 ? previous : 1 - previous) * sample.DurationSeconds;
            var end = markerSync && sample.HasMarkers ? sample.MarkerTime
                : (rate >= 0 ? current : 1 - current) * sample.DurationSeconds;
            var advance = end - start;
            var requested = tickDeltaSeconds * rate;
            if (!float.IsFinite(requested)) { failure = AlsP5FailureCode.NonFiniteOutput; return false; }
            if (requested * advance < 0) advance += MathF.Sign(requested) * sample.DurationSeconds;
            if (!float.IsFinite(advance)) { failure = AlsP5FailureCode.NonFiniteOutput; return false; }
            var eligible = notifyMode == AlsBlendSpaceNotifyMode.AllAnimations ||
                notifyMode == AlsBlendSpaceNotifyMode.HighestWeightedAnimation && i == highest;
            staged[written++] = new(sample.SampleId, sample.AnimationId, start, end, advance, eligible);
        }
        staged[..written].CopyTo(output); count = written; failure = AlsP5FailureCode.None;
        return true;
    }

    private static bool ValidBlendSamples(ReadOnlySpan<AlsBlendSpaceTimingSample> samples)
    {
        if (samples.Length > MaxBlendSpaceTimingSamples) return false;
        for (var i = 0; i < samples.Length; i++)
        {
            var s = samples[i];
            if (s.SampleId < 0 || s.AnimationId < 0 || !float.IsFinite(s.Weight) ||
                !float.IsFinite(s.DurationSeconds) || s.DurationSeconds < 0 ||
                !float.IsFinite(s.AssetRateScale) || !float.IsFinite(s.SampleRateScale) ||
                !float.IsFinite(s.CachedPlayRate) || !float.IsFinite(s.AssetRateScale * s.CachedPlayRate) ||
                !float.IsFinite(s.AssetRateScale * s.SampleRateScale) ||
                !float.IsFinite(s.MarkerPreviousTime) || !float.IsFinite(s.MarkerTime) ||
                s.MarkerPreviousTime < 0 || s.MarkerPreviousTime > s.DurationSeconds ||
                s.MarkerTime < 0 || s.MarkerTime > s.DurationSeconds) return false;
            for (var j = 0; j < i; j++) if (s.SampleId == samples[j].SampleId) return false;
        }
        return true;
    }
}
