using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Sync;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Animation;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsP5SourceNotifyTick(int SampleId, long Epoch, float PreviousTime,
    float Delta, float ContextTime, float Weight, bool Leader, bool Loop, bool ActiveContext = true);

[InlineArray(AlsEventBuffer.Capacity)]
public struct AlsP5SourceNotifyStates { private AlsAssetNotifyActiveState _element; }

// Stored in the existing controller's committed/candidate banks, never in a second scheduler.
[StructLayout(LayoutKind.Sequential)]
public struct AlsP5SourceEventState
{
    public bool Initialized;
    public AlsFrameIdentity Identity;
    public AlsLocomotionSourceStamp SourceStamp;
    public ulong BindingDigest;
    public ulong LayoutDigest;
    public uint RandomSeed;
    public int NextInstanceId;
    public int ActiveCount;
    public AlsP5SourceNotifyStates ActiveStates;
}

public static partial class AlsP5Runtime
{
    public const int SourceNotifyReferenceCapacity = AlsEventBuffer.Capacity * 4;

    /// <summary>Bridge the exact completed Sync batch into native extraction order. Does not
    /// advance time, re-elect leaders, multiply sample weight into queue weight, or dispatch events.</summary>
    public static bool TryBuildSourceNotifyTicks(in AlsP5RuntimeBindings bindings,
        ReadOnlySpan<AlsAssetSyncPlayer> players, ReadOnlySpan<AlsAssetSyncSample> samples,
        ReadOnlySpan<AlsAssetPlayerHistory> mappedPlayers, ReadOnlySpan<AlsAssetSampleHistory> mappedSamples,
        ReadOnlySpan<AlsAssetPlayerTickContext> contexts, Span<AlsP5SourceNotifyTick> destination,
        out int count, out AlsP5FailureCode failure)
    {
        count = 0; failure = AlsP5FailureCode.InvalidBinding;
        if (!ValidSourceEventBindings(bindings) || players.Length > AlsSyncRuntime.MaxAssetSyncBatchPlayers ||
            samples.Length > AlsSyncRuntime.MaxAssetSyncBatchSamples || mappedPlayers.Length != players.Length ||
            contexts.Length != players.Length || mappedSamples.Length != samples.Length) return false;
        Span<AlsP5SourceNotifyTick> staged = stackalloc AlsP5SourceNotifyTick[samples.Length];
        var written = 0;
        for (var order = 0; order < players.Length; order++)
        {
            var mappedIndex = -1;
            for (var i = 0; i < contexts.Length; i++)
                if (contexts[i].Order == order) { if (mappedIndex >= 0) return false; mappedIndex = i; }
            if (mappedIndex < 0) return false;
            var history = mappedPlayers[mappedIndex]; var context = contexts[mappedIndex]; var inputIndex = -1;
            for (var i = 0; i < players.Length; i++)
                if (players[i].PlayerId == context.PlayerId) { if (inputIndex >= 0) return false; inputIndex = i; }
            if (inputIndex < 0 || history.PlayerId != context.PlayerId || (uint)history.PlayerId >= bindings.Sources.Players.Length) return false;
            var player = players[inputIndex]; var source = bindings.Sources.Players[history.PlayerId];
            var policy = bindings.Sources.SyncPlayers[history.PlayerId];
            if (source.Kind == AlsLocomotionSourceKind.TeleportEvaluator || source.PlayerId != history.PlayerId ||
                player.AssetId != history.AssetId || player.Epoch != history.Epoch || player.Epoch < 0 ||
                !AlsLocomotionSourceRuntime.HasValidInputPolicy(source) ||
                source.LoopInput == AlsSourceLoopInput.Constant && player.Looping != source.Loop ||
                policy.AssetId != player.AssetId || policy.PlayerId != player.PlayerId || policy.Role != player.Role || policy.Role > AlsAssetSyncRole.AlwaysFollower ||
                player.Kind != (source.Kind == AlsLocomotionSourceKind.Sequence ? AlsAssetSyncKind.Sequence : AlsAssetSyncKind.BlendSpace) ||
                !float.IsFinite(player.Weight) || player.Weight < 0 || !float.IsFinite(history.Time) ||
                player.SampleCount != history.SampleCount || player.SampleCount <= 0 ||
                player.SampleStart < 0 || player.SampleCount > samples.Length - player.SampleStart ||
                history.SampleStart < 0 || history.SampleCount > mappedSamples.Length - history.SampleStart ||
                policy.NotifyMode > AlsBlendSpaceNotifyMode.None) return false;
            var highest = 0; var highestWeight = -1f;
            for (var n = 0; n < player.SampleCount; n++)
            {
                var sample = samples[player.SampleStart + n];
                if (!float.IsFinite(sample.Weight) || sample.Weight < 0) return false;
                var weight = System.Math.Clamp(sample.Weight, 0, 1);
                if (weight > highestWeight) { highestWeight = weight; highest = n; }
            }
            for (var n = 0; n < player.SampleCount; n++)
            {
                var sample = samples[player.SampleStart + n]; var mapped = mappedSamples[history.SampleStart + n];
                if ((uint)sample.SampleId >= bindings.Sources.Samples.Length || sample.SampleId != mapped.SampleId) return false;
                var definition = bindings.Sources.Samples[sample.SampleId];
                if (definition.SampleId != sample.SampleId || definition.PlayerId != player.PlayerId || definition.AnimationId != mapped.AnimationId ||
                    sample.SequenceIndex != definition.SequenceIndex || !float.IsFinite(mapped.DeltaPrevious) ||
                    !float.IsFinite(mapped.Delta) || !float.IsFinite(mapped.Time)) return false;
                if (sample.Weight <= .00001f || policy.NotifyMode == AlsBlendSpaceNotifyMode.None ||
                    policy.NotifyMode == AlsBlendSpaceNotifyMode.HighestWeightedAnimation && n != highest) continue;
                staged[written++] = new(sample.SampleId, player.Epoch, mapped.DeltaPrevious, mapped.Delta,
                    history.Time, player.Weight, context.Leader, player.Looping);
            }
        }
        if (written > destination.Length) { failure = AlsP5FailureCode.EventBufferOverflow; return false; }
        staged[..written].CopyTo(destination); count = written; failure = AlsP5FailureCode.None; return true;
    }

    /// <summary>Source-event phase of P5. The existing frame transaction owns commit/discard of
    /// the returned state and events. This does not enable the legacy v2 cursor facade for v3.</summary>
    public static bool TryPrepareSourceEvents(in AlsP5RuntimeBindings bindings, in AlsFrameIdentity identity,
        float delta, ReadOnlySpan<AlsP5SourceNotifyTick> ticks, float graphWeight,
        in AlsP5SourceEventState current, out AlsP5SourceEventState candidate, out AlsEventBuffer events,
        out AlsP5FailureCode failure, bool dedicatedServer = false, int predictedLod = 0,
        IAlsMontageNotifyBinding? montageBinding = null, ReadOnlySpan<AlsAssetNotifyDispatchInput> montageNotifies = default,
        ReadOnlySpan<AlsAssetNotifyDispatchInput> montageDirectNotifies = default)
    {
        candidate = default; events = default; failure = AlsP5FailureCode.InvalidBinding;
        if (!ValidSourceEventBindings(bindings) || identity.FrameId < 0 || identity.SlotGeneration == 0 ||
            ticks.Length > AlsSyncRuntime.MaxAssetSyncBatchSamples || current.ActiveCount < 0 || current.ActiveCount > AlsEventBuffer.Capacity ||
            current.Initialized && (current.SourceStamp != bindings.Sources.Stamp || current.BindingDigest != bindings.Digest ||
                current.LayoutDigest != bindings.LayoutDigest || current.Identity.CharacterId != identity.CharacterId ||
                current.Identity.SlotGeneration != identity.SlotGeneration || current.Identity.FrameId >= identity.FrameId) ||
            !current.Initialized && (current.ActiveCount != 0 || current.NextInstanceId != 0 || current.RandomSeed != 0)) return false;
        if (!float.IsFinite(delta) || delta < 0 || !float.IsFinite(graphWeight) || graphWeight is < 0 or > 1 || predictedLod < -1)
        { failure = AlsP5FailureCode.NonFiniteInput; return false; }
        if (montageBinding is null && (!montageNotifies.IsEmpty || !montageDirectNotifies.IsEmpty) ||
            montageNotifies.Length > SourceNotifyReferenceCapacity ||
            montageDirectNotifies.Length > SourceNotifyReferenceCapacity - montageNotifies.Length ||
            montageBinding is not null && ((uint)montageBinding.SourcePolicyCount > montageBinding.Policies.Length ||
                !montageBinding.Policies[..montageBinding.SourcePolicyCount].SequenceEqual(bindings.Sources.NotifyPolicies))) return false;
        ReadOnlySpan<AlsAssetNotifyPolicy> policies = montageBinding is null ? bindings.Sources.NotifyPolicies : montageBinding.Policies;
        for (var group = 0; group < 2; group++)
            foreach (var notify in group == 0 ? montageDirectNotifies : montageNotifies)
                if (montageBinding is null || notify.SourceKind != AlsAssetNotifySourceKind.Montage ||
                    (uint)notify.Reference.PolicyIndex >= policies.Length ||
                    notify.Reference.PolicyIndex < montageBinding.SourcePolicyCount ||
                    !montageBinding.TryTimeline(notify.Reference, out var timeline) || timeline.TickMode != AlsTimelineTickMode.Queued ||
                    !ValidMontageHandle(bindings, notify.Reference.OccurrenceHandleId)) return false;
        var next = current; var seed = current.Initialized ? current.RandomSeed : AlsTimelineRuntime.InitialAssetNotifyRandomSeed;
        Span<AlsAssetNotifyOccurrence> extracted = stackalloc AlsAssetNotifyOccurrence[SourceNotifyReferenceCapacity];
        Span<AlsAssetNotifyReference> incoming = stackalloc AlsAssetNotifyReference[SourceNotifyReferenceCapacity];
        Span<AlsAssetNotifyReference> queue = stackalloc AlsAssetNotifyReference[SourceNotifyReferenceCapacity];
        Span<AlsAssetNotifyReference> queueScratch = stackalloc AlsAssetNotifyReference[SourceNotifyReferenceCapacity];
        var queueCount = 0;
        for (var tickIndex = 0; tickIndex < ticks.Length; tickIndex++)
        {
            failure = AlsP5FailureCode.InvalidBinding;
            var tick = ticks[tickIndex];
            if ((uint)tick.SampleId >= bindings.Sources.Samples.Length || tick.Epoch < 0 || !float.IsFinite(tick.ContextTime) ||
                !float.IsFinite(tick.Weight) || tick.Weight < 0) return false;
            for (var earlier = 0; earlier < tickIndex; earlier++) if (ticks[earlier].SampleId == tick.SampleId) return false;
            var sample = bindings.Sources.Samples[tick.SampleId];
            if (sample.SampleId != tick.SampleId || (uint)sample.PlayerId >= bindings.Sources.Players.Length ||
                (uint)sample.SequenceIndex >= bindings.Sources.NotifyRanges.Length) return false;
            var player = bindings.Sources.Players[sample.PlayerId];
            var map = bindings.SourceOccurrences.Mappings[tick.SampleId];
            if (player.PlayerId != sample.PlayerId || player.Kind == AlsLocomotionSourceKind.TeleportEvaluator ||
                !AlsLocomotionSourceRuntime.HasValidInputPolicy(player) ||
                player.LoopInput == AlsSourceLoopInput.Constant && player.Loop != tick.Loop || map.SampleId != tick.SampleId ||
                map.PlayerId != sample.PlayerId || map.AnimationId != sample.AnimationId || map.OccurrenceHandleId < 0) return false;
            var range = bindings.Sources.NotifyRanges[sample.SequenceIndex];
            if (range.AnimationId != sample.AnimationId || range.Offset < 0 || range.Count < 0 ||
                range.Count > bindings.Sources.NotifyDefinitions.Length - range.Offset) return false;
            if (!AlsTimelineRuntime.TryExtractAssetNotifies(bindings.Sources.NotifyDefinitions.Slice(range.Offset, range.Count),
                sample.DurationSeconds, tick.PreviousTime, tick.Delta, tick.Loop, extracted, out var found, out failure)) return false;
            for (var i = 0; i < found; i++) incoming[i] = new(range.Offset + extracted[i].DefinitionIndex,
                map.OccurrenceHandleId, tick.ContextTime, tick.ActiveContext, extracted[i].ReachedEnd);
            if (!AlsTimelineRuntime.TryQueueAssetNotifies(bindings.Sources.NotifyPolicies, queue[..queueCount], incoming[..found],
                new(tick.Leader, dedicatedServer, predictedLod, tick.Weight * graphWeight), AlsAssetNotifyQueueMode.Filtered, seed,
                queueScratch, queue, out queueCount, out seed, out failure)) return false;
        }
        Span<AlsAssetNotifyDispatchInput> sourceDispatch = stackalloc AlsAssetNotifyDispatchInput[queueCount];
        failure = AlsP5FailureCode.InvalidBinding;
        for (var i = 0; i < queueCount; i++)
        {
            var reference = queue[i]; var policy = bindings.Sources.NotifyPolicies[reference.PolicyIndex];
            var mapIndex = FindSourceMap(bindings, reference.OccurrenceHandleId);
            if (mapIndex < 0 || !TrySourceTimeline(bindings, reference, out var definition)) return false;
            var tickIndex = -1;
            for (var n = 0; n < ticks.Length; n++) if (ticks[n].SampleId == mapIndex) { tickIndex = n; break; }
            if (tickIndex < 0) return false;
            var tick = ticks[tickIndex];
            sourceDispatch[i] = new(reference, AlsAssetNotifySourceKind.AssetPlayer,
                (uint)bindings.Sources.Samples[mapIndex].PlayerId, (policy.StateBehaviorFlags & 1) != 0,
                definition.DurationSeconds, tick.Epoch, tick.Weight * graphWeight);
        }
        // UE HandleEvents puts Montage-owned notifies directly in AnimInstance's queue.
        // PostUpdate appends the proxy queue, then applies relevant Slot sequence queues.
        // State AddUnique can remove entries at either boundary. Retain the entire first
        // accepted dispatch context instead of indexing the inputs by the merged position.
        Span<AlsAssetNotifyDispatchInput> dispatch = stackalloc AlsAssetNotifyDispatchInput[SourceNotifyReferenceCapacity];
        queueCount = 0;
        for (var group = 0; group < 3; group++)
        {
            ReadOnlySpan<AlsAssetNotifyDispatchInput> inputs = group == 0 ? montageDirectNotifies : group == 1 ? sourceDispatch : montageNotifies;
            foreach (var input in inputs)
            {
                incoming[0] = input.Reference;
                var previousCount = queueCount;
                if (!AlsTimelineRuntime.TryQueueAssetNotifies(policies, queue[..queueCount], incoming[..1],
                    default, AlsAssetNotifyQueueMode.Append, seed, queueScratch, queue, out queueCount, out seed, out failure)) return false;
                if (queueCount > previousCount) dispatch[previousCount] = input;
            }
        }
        Span<AlsAssetNotifyActiveState> remaining = stackalloc AlsAssetNotifyActiveState[AlsEventBuffer.Capacity];
        Span<AlsAssetNotifyActiveState> nextStates = stackalloc AlsAssetNotifyActiveState[SourceNotifyReferenceCapacity + AlsEventBuffer.Capacity];
        Span<int> begins = stackalloc int[SourceNotifyReferenceCapacity];
        Span<AlsAssetNotifyCallback> callbackScratch = stackalloc AlsAssetNotifyCallback[SourceNotifyReferenceCapacity * 3 + AlsEventBuffer.Capacity];
        Span<AlsAssetNotifyCallback> callbacks = stackalloc AlsAssetNotifyCallback[AlsEventBuffer.Capacity];
        ReadOnlySpan<AlsAssetNotifyActiveState> active = current.ActiveStates;
        Span<AlsAssetNotifyActiveState> destination = next.ActiveStates;
        if (!AlsTimelineRuntime.TryAdvanceAssetNotifyStates(policies, active[..current.ActiveCount], dispatch[..queueCount],
            new(AlsAssetNotifyDispatchMode.ForceAllSources, delta), current.NextInstanceId,
            new(remaining, nextStates, begins, callbackScratch), destination, callbacks,
            out var activeCount, out var callbackCount, out var allocator, out failure)) return false;
        var output = new AlsEventBuffer();
        failure = AlsP5FailureCode.InvalidBinding;
        for (var i = 0; i < callbackCount; i++)
        {
            var callback = callbacks[i]; var input = callback.State.Input; var reference = input.Reference;
            AlsTimelineEventDefinition definition;
            if (input.SourceKind == AlsAssetNotifySourceKind.Montage)
            { if (montageBinding is null || !montageBinding.TryTimeline(reference, out definition)) return false; }
            else if (!TrySourceTimeline(bindings, reference, out definition)) return false;
            var phase = callback.Kind switch { AlsAssetNotifyCallbackKind.Notify => AlsAnimationEventPhase.Trigger,
                AlsAssetNotifyCallbackKind.Begin => AlsAnimationEventPhase.Begin, AlsAssetNotifyCallbackKind.Tick => AlsAnimationEventPhase.Tick,
                _ => AlsAnimationEventPhase.End };
            var item = new AlsAnimationEvent(definition.EventId, definition.SourceAnimationId, definition.SourceActionId, reference.OccurrenceHandleId,
                input.PlaybackEpoch, 0, (ulong)callback.State.InstanceId + 1, i, definition.BoundaryOrdinal, delta,
                input.EffectiveWeight, definition.Kind, phase, definition.Payload)
            { NativeContext = new(true, callback.State.InstanceId, reference.CurrentTime, callback.Seconds, reference.ActiveContext, reference.ReachedEnd) };
            if (!output.TryAdd(item)) { failure = AlsP5FailureCode.EventBufferOverflow; return false; }
        }
        destination[activeCount..].Clear(); next.ActiveCount = activeCount; next.NextInstanceId = allocator; next.RandomSeed = seed;
        next.Initialized = true; next.Identity = identity; next.SourceStamp = bindings.Sources.Stamp;
        next.BindingDigest = bindings.Digest; next.LayoutDigest = bindings.LayoutDigest;
        candidate = next; events = output; failure = AlsP5FailureCode.None; return true;
    }

    private static bool ValidMontageHandle(in AlsP5RuntimeBindings bindings, int handle)
    {
        foreach (var mapping in bindings.SourceOccurrences.Mappings)
            if (mapping.OccurrenceHandleId == handle) return false;
        return true;
    }

    private static bool ValidSourceEventBindings(in AlsP5RuntimeBindings bindings) =>
        bindings.Version == AlsP5RuntimeBindings.SourceGraphVersion && bindings.Digest != 0 && bindings.LayoutDigest != 0 &&
        bindings.Sources.Stamp.IsValid && bindings.SourceOccurrences.SourceStamp == bindings.Sources.Stamp &&
        bindings.SourceOccurrences.Digest == bindings.LayoutDigest && bindings.Sources.Players.Length == bindings.Sources.SyncPlayers.Length &&
        bindings.SourceOccurrences.Mappings.Length == bindings.Sources.Samples.Length &&
        bindings.Sources.NotifyDefinitions.Length == bindings.Sources.NotifyPolicies.Length &&
        bindings.Sources.NotifyRanges.Length == bindings.Sources.Sequences.Length;

    private static int FindSourceMap(in AlsP5RuntimeBindings bindings, int handle)
    {
        for (var i = 0; i < bindings.SourceOccurrences.Mappings.Length; i++)
            if (bindings.SourceOccurrences.Mappings[i].OccurrenceHandleId == handle) return i;
        return -1;
    }

    private static bool TrySourceTimeline(in AlsP5RuntimeBindings bindings, in AlsAssetNotifyReference reference,
        out AlsTimelineEventDefinition definition)
    {
        definition = default;
        if ((uint)reference.PolicyIndex >= bindings.Sources.NotifyPolicies.Length) return false;
        var policy = bindings.Sources.NotifyPolicies[reference.PolicyIndex];
        foreach (var entry in bindings.TimelineDefinitions)
            if (entry.RequiredOccurrenceHandleId == reference.OccurrenceHandleId && entry.EventId == policy.EventId && entry.SourceIndex == policy.SourceIndex)
            { definition = entry; return true; }
        return false;
    }
}
