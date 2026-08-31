using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Events;

[InlineArray(AlsCommittedNotifyStateBuffer.Capacity)]
internal struct AlsCommittedNotifyStateStorage
{
    private AlsAnimationEvent _element0;
}

[StructLayout(LayoutKind.Sequential)]
public struct AlsCommittedNotifyStateBuffer
{
    public const int Capacity = 16;

    private AlsCommittedNotifyStateStorage _storage;

    public int Count { get; private set; }

    public readonly AlsAnimationEvent this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            if (index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _storage[index];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryApply(in AlsAnimationEvent animationEvent)
    {
        if (Count < 0 || Count > Capacity)
        {
            return false;
        }

        if (animationEvent.Phase == AlsAnimationEventPhase.Begin)
        {
            if (!HasValidOwnerIdentity(animationEvent) || Count >= Capacity)
            {
                return false;
            }

            for (var index = 0; index < Count; index++)
            {
                if (HasSameOwnerIdentity(_storage[index], animationEvent))
                {
                    return false;
                }
            }

            _storage[Count++] = animationEvent;
            return true;
        }

        if (animationEvent.Phase != AlsAnimationEventPhase.End ||
            !HasValidOwnerIdentity(animationEvent))
        {
            return false;
        }

        var match = -1;
        for (var index = 0; index < Count; index++)
        {
            if (HasSameOwnerIdentity(_storage[index], animationEvent))
            {
                match = index;
                break;
            }
        }

        if (match < 0)
        {
            return false;
        }

        for (var index = match; index + 1 < Count; index++)
        {
            _storage[index] = _storage[index + 1];
        }

        Count--;
        _storage[Count] = default;
        return true;
    }

    public bool TryAppendSyntheticEnds(
        AlsActionResultCode reason,
        in AlsFrameIdentity identity,
        ref AlsEventBuffer destination)
    {
        if (reason is not AlsActionResultCode.InterruptedByLifecycle and
            not AlsActionResultCode.InterruptedByGeneration ||
            identity.FrameId < 0 || identity.SlotGeneration == 0 ||
            Count < 0 || Count > Capacity ||
            destination.Count < 0 || destination.Count > AlsEventBuffer.Capacity ||
            destination.Count > AlsEventBuffer.Capacity - Count)
        {
            return false;
        }

        for (var index = 0; index < Count; index++)
        {
            var entry = _storage[index];
            if (entry.Phase != AlsAnimationEventPhase.Begin || !HasValidOwnerIdentity(entry))
            {
                return false;
            }

            for (var other = 0; other < index; other++)
            {
                if (HasSameOwnerIdentity(_storage[other], entry))
                {
                    return false;
                }
            }
        }

        var candidate = destination;
        for (var index = 0; index < Count; index++)
        {
            var entry = _storage[index];
            var payload = entry.Payload with { TerminationReason = reason };
            var syntheticEnd = entry with
            {
                EventSequence = candidate.Count,
                AnimationTime = 0f,
                Phase = AlsAnimationEventPhase.End,
                Payload = payload,
            };
            if (!candidate.TryAdd(syntheticEnd))
            {
                return false;
            }
        }

        destination = candidate;
        Clear();
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        if (Count < 0 || Count > Capacity)
        {
            Count = 0;
            return;
        }

        for (var index = 0; index < Count; index++)
        {
            _storage[index] = default;
        }

        Count = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasValidOwnerIdentity(in AlsAnimationEvent animationEvent) =>
        animationEvent.EventId >= 0 &&
        animationEvent.SourceAnimationId >= 0 &&
        animationEvent.SourceActionId >= -1 &&
        animationEvent.OccurrenceHandleId >= 0 &&
        animationEvent.PlaybackEpoch >= 0 &&
        animationEvent.PlaybackCycle >= 0 &&
        animationEvent.OwnerToken != 0 &&
        animationEvent.BoundaryOrdinal >= 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasSameOwnerIdentity(
        in AlsAnimationEvent first,
        in AlsAnimationEvent second) =>
        first.EventId == second.EventId &&
        first.SourceAnimationId == second.SourceAnimationId &&
        first.SourceActionId == second.SourceActionId &&
        first.OccurrenceHandleId == second.OccurrenceHandleId &&
        first.PlaybackEpoch == second.PlaybackEpoch &&
        first.PlaybackCycle == second.PlaybackCycle &&
        first.OwnerToken == second.OwnerToken &&
        first.BoundaryOrdinal == second.BoundaryOrdinal;
}
