using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsStandingCycleLifetime(bool HasInitialized, bool HasUpdated,
    AlsFrameIdentity Identity, long LastUpdateFrame, long Epoch, AlsGraphTraversalCounter SprintInitialization)
{
    public AlsGraphTraversalCounter? LastUpdateCounter { get; init; }
}
public readonly record struct AlsStandingCycleLifecycleUpdate(AlsStandingCycleLifetime State,
    bool Initialized, bool FirstUpdate, bool InitializeSprint);

/// <summary>The outer Cycle machine and its cached Sprint content have different lifetimes.
/// A relevance gap reinitializes the machine and outer inputs, not a Save whose counter still matches.</summary>
public static class AlsStandingCycleLifecycle
{
    public static AlsStandingCycleLifecycleUpdate Prepare(in AlsStandingCycleLifetime previous,
        AlsFrameIdentity identity, bool update, bool initialize, AlsGraphTraversalCounter initialization,
        AlsGraphTraversalCounter? updateCounter = null)
    {
        if(updateCounter is {HasUpdated:false} || update && !initialize && previous.HasUpdated &&
            previous.LastUpdateCounter.HasValue!=updateCounter.HasValue)
            throw new ArgumentException("Standing Cycle update traversal ownership differs.");
        if (identity.SlotGeneration == 0 || !initialization.HasUpdated || previous.Epoch < 0 ||
            !previous.HasInitialized && (previous.HasUpdated || previous.Epoch != 0) || previous.HasInitialized &&
            (previous.Epoch <= 0 || previous.Identity.CharacterId != identity.CharacterId ||
                previous.Identity.SlotGeneration != identity.SlotGeneration || previous.Identity.FrameId > identity.FrameId ||
                previous.HasUpdated && (previous.LastUpdateFrame > previous.Identity.FrameId || update && previous.LastUpdateFrame >= identity.FrameId)))
            throw new ArgumentException("Invalid Standing Cycle lifetime.");
        var reset = initialize || update && (!previous.HasInitialized || previous.HasUpdated && (updateCounter is { } counter
            ? !previous.LastUpdateCounter!.Value.WasSynchronizedCounter(counter)
            : identity.FrameId - previous.LastUpdateFrame > 1));
        var state = previous;
        var sprint = false;
        if (reset)
        {
            if (previous.Epoch == long.MaxValue) throw new ArgumentOutOfRangeException(nameof(previous));
            sprint = !previous.SprintInitialization.MatchesCounter(initialization);
            state = new(true, false, identity, 0, previous.Epoch + 1, initialization);
        }
        var first = update && !state.HasUpdated;
        if (update) state = state with { HasUpdated = true, Identity = identity, LastUpdateFrame = identity.FrameId, LastUpdateCounter=updateCounter };
        return new(state, reset, first, sprint);
    }
}
