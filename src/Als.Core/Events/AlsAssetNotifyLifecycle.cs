using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Events;

public enum AlsAssetNotifySourceKind : byte { None, AssetPlayer, Montage }
public enum AlsAssetNotifyDispatchMode : byte { Default, ForceAnimGraphOnly, ForceMontageOnly, ForceAllSources, EndAll }
public enum AlsAssetNotifyCallbackKind : byte { Notify, End, Begin, Tick }

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsAssetNotifyDispatchInput(AlsAssetNotifyReference Reference,
    AlsAssetNotifySourceKind SourceKind, uint SourceInstanceId, bool NoMergeOnConcurrentPlay, float Duration,
    long PlaybackEpoch = 0, float EffectiveWeight = 1);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsAssetNotifyActiveState(AlsAssetNotifyDispatchInput Input, int InstanceId);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsAssetNotifyCallback(AlsAssetNotifyCallbackKind Kind,
    AlsAssetNotifyActiveState State, float Seconds);

/// <summary>Skip flags are resolved from UE's updated-source dispatch policy, not from
/// ActiveContext or ReachedEnd. Forced modes ignore them.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsAssetNotifyDispatchContext(AlsAssetNotifyDispatchMode Mode,
    float DeltaSeconds, bool SkipAnimGraph = false, bool SkipMontages = false);

public readonly ref struct AlsAssetNotifyLifecycleScratch(Span<AlsAssetNotifyActiveState> remaining,
    Span<AlsAssetNotifyActiveState> next, Span<int> begins, Span<AlsAssetNotifyCallback> callbacks)
{
    public readonly Span<AlsAssetNotifyActiveState> Remaining = remaining;
    public readonly Span<AlsAssetNotifyActiveState> Next = next;
    public readonly Span<int> Begins = begins;
    public readonly Span<AlsAssetNotifyCallback> Callbacks = callbacks;
}

public static partial class AlsTimelineRuntime
{
    /// <summary>UE TriggerAnimNotifies/EndNotifyStates candidate for class-based callbacks.
    /// No callbacks execute here. The owner publishes state, allocator and callbacks together
    /// after Finalize; discarded candidates may be retried with the same committed inputs.
    /// Named Blueprint dispatch and callback-driven reentrant teardown are not represented.</summary>
    public static bool TryAdvanceAssetNotifyStates(ReadOnlySpan<AlsAssetNotifyPolicy> policies,
        ReadOnlySpan<AlsAssetNotifyActiveState> current, ReadOnlySpan<AlsAssetNotifyDispatchInput> queued,
        in AlsAssetNotifyDispatchContext context, int nextInstanceId, in AlsAssetNotifyLifecycleScratch scratch,
        Span<AlsAssetNotifyActiveState> destination, Span<AlsAssetNotifyCallback> callbacks,
        out int stateCount, out int callbackCount, out int candidateNextInstanceId, out AlsP5FailureCode failure)
    {
        stateCount = callbackCount = 0; candidateNextInstanceId = nextInstanceId;
        failure = AlsP5FailureCode.InvalidBinding;
        if (context.Mode > AlsAssetNotifyDispatchMode.EndAll || nextInstanceId < 0 ||
            !ValidLifecycleMemory(policies, current, queued, scratch, destination, callbacks)) return false;
        if (!float.IsFinite(context.DeltaSeconds) || context.DeltaSeconds < 0)
        { failure = AlsP5FailureCode.NonFiniteInput; return false; }
        foreach (var state in current)
            if (state.InstanceId < 0 || !ValidLifecycleInput(policies, state.Input) ||
                policies[state.Input.Reference.PolicyIndex].StateObjectId < 0) return false;
        foreach (var input in queued)
            if (input.Reference.HasSource && input.Reference.PolicyIndex != -1 && !ValidLifecycleInput(policies, input)) return false;
        failure = AlsP5FailureCode.EventBufferOverflow;
        if (current.Length > scratch.Remaining.Length) return false;
        current.CopyTo(scratch.Remaining);
        var remaining = current.Length; var nextCount = 0; var beginCount = 0; var eventCount = 0; var allocator = nextInstanceId;
        if (context.Mode != AlsAssetNotifyDispatchMode.EndAll)
        {
            foreach (var input in queued)
            {
                if (!input.Reference.HasSource || input.Reference.PolicyIndex == -1 || !IncludedNotifySource(input, context.Mode)) continue;
                var policy = policies[input.Reference.PolicyIndex];
                if (policy.StateObjectId < 0)
                {
                    var instant = new AlsAssetNotifyActiveState(input, AllocateNotifyInstance(ref allocator));
                    if (!AddNotifyCallback(scratch.Callbacks, ref eventCount, AlsAssetNotifyCallbackKind.Notify, instant, 0)) return false;
                    continue;
                }
                var found = -1;
                for (var i = 0; i < remaining; i++)
                {
                    var old = scratch.Remaining[i];
                    // The native active event is a copy: compare its State object, not a queue pointer.
                    if (policies[old.Input.Reference.PolicyIndex].StateObjectId == policy.StateObjectId &&
                        (!input.NoMergeOnConcurrentPlay || input.SourceKind != AlsAssetNotifySourceKind.None &&
                            input.SourceKind == old.Input.SourceKind && input.SourceInstanceId == old.Input.SourceInstanceId &&
                            input.PlaybackEpoch == old.Input.PlaybackEpoch))
                    { found = i; break; }
                }
                int instance;
                if (found >= 0)
                {
                    instance = scratch.Remaining[found].InstanceId;
                    scratch.Remaining[found] = scratch.Remaining[--remaining];
                }
                else
                {
                    instance = AllocateNotifyInstance(ref allocator);
                    if (beginCount == scratch.Begins.Length) return false;
                    scratch.Begins[beginCount++] = nextCount;
                }
                if (nextCount == scratch.Next.Length) return false;
                scratch.Next[nextCount++] = new(input, instance);
            }
        }
        for (var i = 0; i < remaining; i++)
        {
            var state = scratch.Remaining[i];
            if (context.Mode == AlsAssetNotifyDispatchMode.Default &&
                (state.Input.SourceKind == AlsAssetNotifySourceKind.Montage ? context.SkipMontages : context.SkipAnimGraph))
            {
                if (nextCount == scratch.Next.Length) return false;
                scratch.Next[nextCount++] = state;
            }
            else if (!AddNotifyCallback(scratch.Callbacks, ref eventCount, AlsAssetNotifyCallbackKind.End, state, 0)) return false;
        }
        for (var i = 0; i < beginCount; i++)
        {
            var state = scratch.Next[scratch.Begins[i]];
            if (!AddNotifyCallback(scratch.Callbacks, ref eventCount, AlsAssetNotifyCallbackKind.Begin, state, state.Input.Duration)) return false;
        }
        for (var i = 0; i < nextCount; i++)
        {
            // UE 5.9 uses !skipMontage || !skipGraph here; the source predicates are mutually
            // exclusive, so retained states still Tick. Preserve observed code, not its comment.
            if (!AddNotifyCallback(scratch.Callbacks, ref eventCount, AlsAssetNotifyCallbackKind.Tick, scratch.Next[i], context.DeltaSeconds)) return false;
        }
        if (nextCount > destination.Length || eventCount > callbacks.Length) return false;
        scratch.Next[..nextCount].CopyTo(destination); scratch.Callbacks[..eventCount].CopyTo(callbacks);
        stateCount = nextCount; callbackCount = eventCount; candidateNextInstanceId = allocator;
        failure = AlsP5FailureCode.None; return true;
    }

    internal static bool ValidLifecycleInput(ReadOnlySpan<AlsAssetNotifyPolicy> policies, in AlsAssetNotifyDispatchInput input) =>
        ValidQueueReference(policies, input.Reference) && float.IsFinite(input.Duration) && input.Duration >= 0 &&
        input.PlaybackEpoch >= 0 && float.IsFinite(input.EffectiveWeight) && input.EffectiveWeight >= 0 &&
        input.SourceKind <= AlsAssetNotifySourceKind.Montage &&
        (input.SourceKind != AlsAssetNotifySourceKind.None || input.SourceInstanceId == 0) &&
        (policies[input.Reference.PolicyIndex].NotifyObjectId >= 0 || policies[input.Reference.PolicyIndex].StateObjectId >= 0);

    internal static bool IncludedNotifySource(in AlsAssetNotifyDispatchInput input, AlsAssetNotifyDispatchMode mode) =>
        mode != AlsAssetNotifyDispatchMode.ForceAnimGraphOnly && mode != AlsAssetNotifyDispatchMode.ForceMontageOnly ||
        (input.SourceKind == AlsAssetNotifySourceKind.Montage) == (mode == AlsAssetNotifyDispatchMode.ForceMontageOnly);

    internal static int AllocateNotifyInstance(ref int next)
    {
        // Matches the native int32 boundary, including its repeated zero at wrap.
        if (next == int.MaxValue) { next = 0; return 0; }
        return next++;
    }

    private static bool AddNotifyCallback(Span<AlsAssetNotifyCallback> scratch, ref int count,
        AlsAssetNotifyCallbackKind kind, in AlsAssetNotifyActiveState state, float seconds)
    {
        if (count == scratch.Length) return false;
        scratch[count++] = new(kind, state, seconds); return true;
    }

    private static bool ValidLifecycleMemory(ReadOnlySpan<AlsAssetNotifyPolicy> policies,
        ReadOnlySpan<AlsAssetNotifyActiveState> current, ReadOnlySpan<AlsAssetNotifyDispatchInput> queued,
        in AlsAssetNotifyLifecycleScratch scratch, Span<AlsAssetNotifyActiveState> destination, Span<AlsAssetNotifyCallback> callbacks)
    {
        var a = MemoryMarshal.AsBytes(scratch.Remaining); var b = MemoryMarshal.AsBytes(scratch.Next);
        var c = MemoryMarshal.AsBytes(scratch.Begins); var d = MemoryMarshal.AsBytes(scratch.Callbacks);
        var p = MemoryMarshal.AsBytes(policies); var q = MemoryMarshal.AsBytes(queued); var old = MemoryMarshal.AsBytes(current);
        var result = MemoryMarshal.AsBytes(destination); var events = MemoryMarshal.AsBytes(callbacks);
        return !a.Overlaps(b) && !a.Overlaps(c) && !a.Overlaps(d) && !b.Overlaps(c) && !b.Overlaps(d) && !c.Overlaps(d) &&
            !OverlapsLifecycleInputs(a, p, q, old, result, events) && !OverlapsLifecycleInputs(b, p, q, old, result, events) &&
            !OverlapsLifecycleInputs(c, p, q, old, result, events) && !OverlapsLifecycleInputs(d, p, q, old, result, events) &&
            !result.Overlaps(events) && !result.Overlaps(p) && !result.Overlaps(q) &&
            !events.Overlaps(p) && !events.Overlaps(q) && !events.Overlaps(old);
    }

    private static bool OverlapsLifecycleInputs(ReadOnlySpan<byte> scratch, ReadOnlySpan<byte> policies, ReadOnlySpan<byte> queued,
        ReadOnlySpan<byte> current, ReadOnlySpan<byte> destination, ReadOnlySpan<byte> callbacks) =>
        scratch.Overlaps(policies) || scratch.Overlaps(queued) || scratch.Overlaps(current) ||
        scratch.Overlaps(destination) || scratch.Overlaps(callbacks);
}
