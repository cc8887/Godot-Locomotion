using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Events;

public enum AlsAssetNotifyQueueMode : byte { Filtered, Append }

/// <summary>Extracted reference plus native Tick context. PolicyIndex is source-table based,
/// not a playback handle; duplicate State references retain the first accepted context.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsAssetNotifyReference(int PolicyIndex, int OccurrenceHandleId,
    float CurrentTime, bool ActiveContext, bool ReachedEnd, bool HasSource = true, bool ScopeFiltered = false);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsAssetNotifyQueueContext(bool Leader, bool DedicatedServer, int PredictedLod, float Weight);

public static partial class AlsTimelineRuntime
{
    public const uint InitialAssetNotifyRandomSeed = 0x05629063;

    /// <summary>Candidate queue append. Scratch may change on failure; destination and random
    /// state do not. Frame reset is an empty current queue with the committed seed, not a new seed.
    /// Append merges already-filtered queues and must not filter or draw random numbers again.</summary>
    public static bool TryQueueAssetNotifies(ReadOnlySpan<AlsAssetNotifyPolicy> policies,
        ReadOnlySpan<AlsAssetNotifyReference> current, ReadOnlySpan<AlsAssetNotifyReference> incoming,
        in AlsAssetNotifyQueueContext context, AlsAssetNotifyQueueMode mode, uint randomSeed,
        Span<AlsAssetNotifyReference> scratch, Span<AlsAssetNotifyReference> destination,
        out int count, out uint candidateSeed, out AlsP5FailureCode failure)
    {
        count = 0; candidateSeed = randomSeed; failure = AlsP5FailureCode.InvalidBinding;
        if (mode is not (AlsAssetNotifyQueueMode.Filtered or AlsAssetNotifyQueueMode.Append) ||
            current.Overlaps(scratch) || incoming.Overlaps(scratch) || destination.Overlaps(scratch)) return false;
        if (mode == AlsAssetNotifyQueueMode.Filtered &&
            (!float.IsFinite(context.Weight) || context.Weight < 0 || context.PredictedLod < -1))
        { failure = AlsP5FailureCode.NonFiniteInput; return false; }
        foreach (var reference in current)
            if (!ValidQueueReference(policies, reference)) return false;
        foreach (var reference in incoming)
            if (reference.HasSource && reference.PolicyIndex != -1 && !ValidQueueReference(policies, reference)) return false;
        if (current.Length > scratch.Length || current.Length > destination.Length)
        { failure = AlsP5FailureCode.EventBufferOverflow; return false; }
        current.CopyTo(scratch); var written = current.Length; var seed = randomSeed;
        foreach (var reference in incoming)
        {
            if (!reference.HasSource || reference.PolicyIndex == -1) continue;
            var policy = policies[reference.PolicyIndex];
            if (mode == AlsAssetNotifyQueueMode.Filtered)
            {
                if (!context.Leader && !policy.OnFollower || context.DedicatedServer && !policy.OnDedicatedServer ||
                    policy.WeightThreshold > context.Weight ||
                    policy.FilterType == AlsAssetNotifyFilterType.Lod && policy.FilterLod <= context.PredictedLod) continue;
                if (policy.StateObjectId < 0 && GodotAls.Core.Math.AlsRandomStream.NextFraction(ref seed) >= policy.Chance) continue;
                if (policy.FilterViaRequest && reference.ScopeFiltered) continue;
            }
            var duplicate = false;
            if (policy.StateObjectId >= 0)
                for (var i = 0; i < written; i++)
                    if (EquivalentNotify(policies[scratch[i].PolicyIndex], policy)) { duplicate = true; break; }
            if (duplicate) continue;
            if (written == scratch.Length || written == destination.Length)
            { failure = AlsP5FailureCode.EventBufferOverflow; return false; }
            scratch[written++] = reference;
        }
        scratch[..written].CopyTo(destination); count = written; candidateSeed = seed;
        failure = AlsP5FailureCode.None; return true;
    }

    private static bool ValidQueueReference(ReadOnlySpan<AlsAssetNotifyPolicy> policies, in AlsAssetNotifyReference reference)
    {
        if (!reference.HasSource || (uint)reference.PolicyIndex >= policies.Length || reference.OccurrenceHandleId < 0 ||
            !float.IsFinite(reference.CurrentTime)) return false;
        var p = policies[reference.PolicyIndex];
        return p.EventId >= 0 && p.SourceIndex >= 0 && p.TrackIndex >= 0 && p.NameId >= 0 &&
            p.NotifyObjectId >= -1 && p.StateObjectId >= -1 && (p.NotifyObjectId < 0 || p.StateObjectId < 0) &&
            p.StateBehaviorFlags <= 1 && (p.StateObjectId >= 0 || p.StateBehaviorFlags == 0) &&
            float.IsFinite(p.WeightThreshold) && p.WeightThreshold >= 0 && p.WeightThreshold <= 1 &&
            float.IsFinite(p.Chance) && p.Chance >= 0 && p.Chance <= 1 && p.FilterLod >= 0 &&
            p.FilterType is AlsAssetNotifyFilterType.None or AlsAssetNotifyFilterType.Lod &&
            p.TickMode is AlsTimelineTickMode.Queued or AlsTimelineTickMode.BranchingPoint;
    }

    // Native reference equality first compares the event pointer, then the event's object/name rule.
    private static bool EquivalentNotify(in AlsAssetNotifyPolicy left, in AlsAssetNotifyPolicy right) =>
        left.EventId == right.EventId || left.NotifyObjectId >= 0 && left.NotifyObjectId == right.NotifyObjectId ||
        left.StateObjectId >= 0 && left.StateObjectId == right.StateObjectId ||
        left.NotifyObjectId < 0 && left.StateObjectId < 0 && left.NameId == right.NameId;

}
