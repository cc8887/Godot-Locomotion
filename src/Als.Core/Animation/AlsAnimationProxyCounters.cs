using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Animation;

public enum AlsAnimationProxyPhase { Initialization, CachedBones, Update, Evaluation }

/// <summary>
/// Immutable UE Proxy traversal counters. The caller owns root identity,
/// bone-cache invalidation, worker gating and the external frame clock.
/// Function roots inherit the caller's phase; they do not advance a counter.
/// This value can be staged with the enclosing character transaction.
/// </summary>
public readonly record struct AlsAnimationProxyCounters(
    AlsGraphTraversalCounter Initialization,
    AlsGraphTraversalCounter CachedBones,
    AlsGraphTraversalCounter Update,
    AlsGraphTraversalCounter Evaluation)
{
    public AlsGraphTraversalCounter Counter(AlsAnimationProxyPhase phase) => phase switch
    {
        AlsAnimationProxyPhase.Initialization => Initialization,
        AlsAnimationProxyPhase.CachedBones => CachedBones,
        AlsAnimationProxyPhase.Update => Update,
        AlsAnimationProxyPhase.Evaluation => Evaluation,
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };

    // Invoke only when the actual nonnull Proxy Main root is entered. Cached
    // bones additionally require the original invalidation guard to permit it.
    public AlsAnimationProxyCounters AdvanceMainRoot(AlsAnimationProxyPhase phase, ulong globalFrame) =>
        WithCounter(phase, Counter(phase).Next(globalFrame));

    public AlsAnimationProxyCounters SynchronizeLinked(AlsAnimationProxyPhase phase,
        in AlsAnimationProxyCounters caller) => WithCounter(phase, caller.Counter(phase));

    // FAnimInstanceProxy::Initialize does not reset the other three counters.
    // This differs from graph-root initialization, which advances Initialization.
    public AlsAnimationProxyCounters InitializeProxy() => this with { Update = default };

    public AlsAnimationProxyCounters WithCounter(AlsAnimationProxyPhase phase, AlsGraphTraversalCounter counter) => phase switch
    {
        AlsAnimationProxyPhase.Initialization => this with { Initialization = counter },
        AlsAnimationProxyPhase.CachedBones => this with { CachedBones = counter },
        AlsAnimationProxyPhase.Update => this with { Update = counter },
        AlsAnimationProxyPhase.Evaluation => this with { Evaluation = counter },
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };
}
