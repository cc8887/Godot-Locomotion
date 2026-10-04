using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Events;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsAssetNotifyDefinition(int EventId, float TriggerTimeSeconds, float EndTriggerTimeSeconds);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsAssetNotifyOccurrence(int EventId, int DefinitionIndex, bool ReachedEnd);

public static partial class AlsTimelineRuntime
{
    /// <summary>Contiguous Montage HandleEvents endpoints. The native path tests
    /// these positions directly; reconstructing current from a rounded delta can
    /// change which events overlap a reverse traversal by one float ULP.</summary>
    public static bool TryExtractAssetNotifiesFromPositions(ReadOnlySpan<AlsAssetNotifyDefinition> definitions,
        float previous, float current, Span<AlsAssetNotifyOccurrence> output,
        out int count, out AlsP5FailureCode failure)
    {
        count=0;failure=AlsP5FailureCode.NonFiniteInput;
        if(!float.IsFinite(previous)||!float.IsFinite(current))return false;
        failure=AlsP5FailureCode.InvalidBinding;
        for(var i=0;i<definitions.Length;i++)
        {
            var d=definitions[i];
            if(d.EventId<0||!float.IsFinite(d.TriggerTimeSeconds)||!float.IsFinite(d.EndTriggerTimeSeconds)||d.EndTriggerTimeSeconds<d.TriggerTimeSeconds)return false;
            for(var j=0;j<i;j++)if(definitions[j].EventId==d.EventId)return false;
        }
        var backwards=current<previous;var needed=0;
        foreach(var d in definitions)if(Overlaps(d,previous,current,backwards))needed++;
        if(needed>output.Length){failure=AlsP5FailureCode.EventBufferOverflow;return false;}
        for(var i=0;i<definitions.Length;i++)
        {
            var d=definitions[i];if(!Overlaps(d,previous,current,backwards))continue;
            output[count++]=new(d.EventId,i,backwards?current<=MathF.Max(d.TriggerTimeSeconds,0):current>=d.EndTriggerTimeSeconds);
        }
        failure=AlsP5FailureCode.None;return true;
    }

    /// <summary>UE non-looping asset extraction before queue filtering and state lifecycle dispatch.
    /// Start and delta must come from the current asset tick, not the last committed notify cursor.</summary>
    public static bool TryExtractNonLoopingAssetNotifies(ReadOnlySpan<AlsAssetNotifyDefinition> definitions,
        float durationSeconds, float previousTimeSeconds, float tickDeltaSeconds,
        Span<AlsAssetNotifyOccurrence> output, out int count, out AlsP5FailureCode failure) =>
        TryExtractAssetNotifies(definitions, durationSeconds, previousTimeSeconds, tickDeltaSeconds,
            false, output, out count, out failure);

    /// <summary>Asset Tick extraction, in segment/definition order, before NotifyQueue filtering.
    /// Repeated state references across loop segments are intentional; queue deduplication is later.</summary>
    public static bool TryExtractAssetNotifies(ReadOnlySpan<AlsAssetNotifyDefinition> definitions,
        float durationSeconds, float previousTimeSeconds, float tickDeltaSeconds, bool looping,
        Span<AlsAssetNotifyOccurrence> output, out int count, out AlsP5FailureCode failure)
    {
        count = 0; failure = AlsP5FailureCode.NonFiniteInput;
        if (!float.IsFinite(durationSeconds) || !float.IsFinite(previousTimeSeconds) || !float.IsFinite(tickDeltaSeconds)) return false;
        failure = AlsP5FailureCode.InvalidTimeline;
        if (durationSeconds <= 0 || previousTimeSeconds < 0 || previousTimeSeconds > durationSeconds) return false;
        var desired = previousTimeSeconds + tickDeltaSeconds;
        if (!float.IsFinite(desired)) { failure = AlsP5FailureCode.NonFiniteOutput; return false; }
        var passes = 1u;
        if (looping)
        {
            var ratio = tickDeltaSeconds / durationSeconds;
            if (!float.IsFinite(ratio)) { failure = AlsP5FailureCode.NonFiniteOutput; return false; }
            passes = AssetNotifyPassLimit(durationSeconds, tickDeltaSeconds);
        }
        for (var i = 0; i < definitions.Length; i++)
        {
            var definition = definitions[i];
            if (definition.EventId < 0 || !float.IsFinite(definition.TriggerTimeSeconds) ||
                !float.IsFinite(definition.EndTriggerTimeSeconds) || definition.EndTriggerTimeSeconds < definition.TriggerTimeSeconds)
            { failure = AlsP5FailureCode.InvalidBinding; return false; }
            // Effective trigger offsets may lie just outside the authored animation interval.
            for (var j = 0; j < i; j++)
                if (definitions[j].EventId == definition.EventId)
                { failure = AlsP5FailureCode.InvalidBinding; return false; }
        }
        // Count with the same float traversal before any write, including late loop overflow.
        if (!TraverseAssetNotifies(definitions, durationSeconds, previousTimeSeconds, tickDeltaSeconds,
                looping, passes, output, false, out _, out failure)) return false;
        return TraverseAssetNotifies(definitions, durationSeconds, previousTimeSeconds, tickDeltaSeconds,
            looping, passes, output, true, out count, out failure);
    }

    // Match the observed UE Win64 unsigned conversion before clamping, including negative deltas.
    internal static uint AssetNotifyPassLimit(float duration, float delta) => MathF.Abs(delta) > duration
        ? System.Math.Clamp(unchecked((uint)(long)(delta / duration)), 2u, 1000u) : 2u;

    private static bool TraverseAssetNotifies(ReadOnlySpan<AlsAssetNotifyDefinition> definitions,
        float duration, float start, float delta, bool looping, uint passes, Span<AlsAssetNotifyOccurrence> output,
        bool write, out int count, out AlsP5FailureCode failure)
    {
        count = 0; failure = AlsP5FailureCode.None;
        var previous = start; var remaining = delta;
        for (var pass = 0u; pass < passes; pass++)
        {
            var desired = previous + remaining;
            if (!float.IsFinite(desired)) { count = 0; failure = AlsP5FailureCode.NonFiniteOutput; return false; }
            var current = System.Math.Clamp(desired, 0, duration);
            var backwards = current < previous;
            for (var i = 0; i < definitions.Length; i++)
            {
                var definition = definitions[i];
                if (!Overlaps(definition, previous, current, backwards)) continue;
                if (count == output.Length) { count = 0; failure = AlsP5FailureCode.EventBufferOverflow; return false; }
                if (write) output[count] = new(definition.EventId, i, backwards
                    ? current <= MathF.Max(definition.TriggerTimeSeconds, 0)
                    : current >= definition.EndTriggerTimeSeconds);
                count++;
            }
            if (!looping || desired >= 0 && desired <= duration) break;
            remaining -= current - previous;
            previous = delta < 0 ? duration : 0;
        }
        return true;
    }

    private static bool Overlaps(in AlsAssetNotifyDefinition definition, float previous, float current, bool backwards) => backwards
        ? definition.TriggerTimeSeconds < previous && definition.EndTriggerTimeSeconds >= current
        : definition.TriggerTimeSeconds <= current && definition.EndTriggerTimeSeconds > previous;
}
