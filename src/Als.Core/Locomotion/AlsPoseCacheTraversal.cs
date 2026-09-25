using System.Runtime.CompilerServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsActiveAnimationState(int MachineNodeIndex, int StateIndex);
[InlineArray(16)] internal struct AlsActiveAnimationStates { private AlsActiveAnimationState _element; }

/// <summary>Snapshot of an update path, including the top messages retained by UE's cached update.
/// The top active-state message contains its entire ancestor chain, not just the innermost state.</summary>
public readonly record struct AlsPoseUpdateContext
{
    private readonly AlsActiveAnimationStates _states;
    private bool Inactive { get; init; }
    public bool IsActive => !Inactive;
    public AlsFrameIdentity Identity { get; }
    public AlsGraphTraversalCounter? UpdateCounter { get; private init; }
    public float Weight { get; private init; }
    public float Delta { get; }
    public float RootMotionWeight { get; private init; }
    public bool HasSharedContext { get; }
    public bool InertializationSync { get; private init; }
    public int InertializationRequester { get; private init; }
    public int SkippedUpdateHandler { get; private init; }
    public int StateCount { get; private init; }

    public AlsPoseUpdateContext(AlsFrameIdentity identity, float weight, float delta,
        float rootMotionWeight = 1, bool sharedContext = true)
    {
        if (identity.SlotGeneration == 0 || !float.IsFinite(weight) || weight < 0 ||
            !float.IsFinite(delta) || delta < 0 || !float.IsFinite(rootMotionWeight) || rootMotionWeight < 0)
            throw new ArgumentException("Invalid pose update context.");
        Identity = identity; Weight = weight; Delta = delta; RootMotionWeight = rootMotionWeight;
        HasSharedContext = sharedContext; InertializationRequester = SkippedUpdateHandler = -1;
    }

    public AlsActiveAnimationState GetState(int index) => (uint)index < StateCount
        ? _states[index] : throw new ArgumentOutOfRangeException(nameof(index));

    public AlsPoseUpdateContext WithWeight(float weight, float rootMotionMultiplier = 1)
    {
        if (!float.IsFinite(weight) || weight < 0 || !float.IsFinite(rootMotionMultiplier) || rootMotionMultiplier < 0 ||
            !float.IsFinite(RootMotionWeight * rootMotionMultiplier)) throw new ArgumentOutOfRangeException(nameof(weight));
        return this with { Weight = weight, RootMotionWeight = RootMotionWeight * rootMotionMultiplier };
    }

    public AlsPoseUpdateContext AsInactive() => this with { Inactive = true };

    public AlsPoseUpdateContext WithUpdateCounter(in AlsGraphTraversalCounter counter)
    {
        if(!counter.HasUpdated)throw new ArgumentException("Uninitialized animation update traversal.");
        if(UpdateCounter is { } prior && !prior.MatchesAll(counter))
            throw new ArgumentException("Animation update traversal changed inside a pose path.");
        return this with {UpdateCounter=counter};
    }

    public AlsPoseUpdateContext WithState(int machineNodeIndex, int stateIndex, bool inertializationSync = false)
    {
        if (machineNodeIndex < 0 || stateIndex < 0) throw new ArgumentOutOfRangeException(nameof(stateIndex));
        if (!HasSharedContext) return this;
        if (StateCount == 16) throw new InvalidOperationException("Active animation state depth exceeded.");
        var states = _states;
        states[StateCount] = new(machineNodeIndex, stateIndex);
        return new(this, states) { StateCount = StateCount + 1, InertializationSync = InertializationSync || inertializationSync };
    }

    public AlsPoseUpdateContext WithInertialization(int nodeIndex, bool forwardSkippedUpdates)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nodeIndex);
        if (!HasSharedContext) return this;
        // These are different message types. Disabling an inner handler does not remove an outer handler.
        return this with { InertializationRequester = nodeIndex,
            SkippedUpdateHandler = forwardSkippedUpdates ? nodeIndex : SkippedUpdateHandler };
    }

    private AlsPoseUpdateContext(in AlsPoseUpdateContext other, AlsActiveAnimationStates states)
    {
        this = other;
        _states = states;
    }
}

public readonly record struct AlsPoseCacheReadBinding(int ReadNodeIndex, int CacheNodeIndex);

/// <summary>Indices are source compiled node identities, not animation IDs or P5 physical slots.</summary>
public sealed class AlsPoseCacheDefinition
{
    private readonly int[] _order;
    private readonly AlsPoseCacheReadBinding[] _reads;
    public ReadOnlySpan<int> UpdateOrder => _order;
    public ReadOnlySpan<AlsPoseCacheReadBinding> Reads => _reads;
    public int NodeCount { get; }
    public AlsPoseCacheDefinition(int nodeCount, int[] order, AlsPoseCacheReadBinding[] reads)
    {
        if (nodeCount <= 0 || order.Any(n => (uint)n >= nodeCount) ||
            order.Distinct().Count() != order.Length || reads.Select(r => r.ReadNodeIndex).Distinct().Count() != reads.Length ||
            reads.Any(r => (uint)r.ReadNodeIndex >= nodeCount || (uint)r.CacheNodeIndex >= nodeCount ||
                order.Contains(r.ReadNodeIndex) || !order.Contains(r.CacheNodeIndex)))
            throw new ArgumentException("Invalid source pose-cache layout.");
        NodeCount = nodeCount; _order = (int[])order.Clone(); _reads = (AlsPoseCacheReadBinding[])reads.Clone();
    }
}

/// <summary>Callbacks must modify only the caller's candidate transaction. They must not dispatch gameplay events.</summary>
public interface IAlsPoseCacheUpdateSink
{
    void UpdateCachedSource(int cacheNodeIndex, in AlsPoseUpdateContext context);
    void OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped);
}

/// <summary>Per-candidate deferred update traversal. No clocks, pose history, source instances or event queues are owned here.</summary>
public sealed class AlsPoseCacheTraversal
{
    private readonly AlsPoseCacheDefinition _definition;
    private readonly int[] _targets;
    private readonly int[] _ranks;
    private readonly int[] _first;
    private readonly int[] _last;
    private readonly int[] _best;
    private readonly int[] _next;
    private readonly AlsPoseUpdateContext[] _contexts;
    private readonly AlsPoseUpdateContext[] _skipped;
    private AlsFrameIdentity _identity;
    private AlsGraphTraversalCounter? _updateCounter;
    private int _count;
    private int _rank;
    private bool _begun;
    private bool _draining;
    private bool _finished;
    public int SourceUpdateCount { get; private set; }
    internal AlsPoseCacheDefinition Definition => _definition;
    public bool IsFinished => _finished;
    public int CachedCallCount => _count;

    public AlsPoseCacheTraversal(AlsPoseCacheDefinition definition, int callCapacity)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(callCapacity);
        _definition = definition;
        _targets = Enumerable.Repeat(-1, definition.NodeCount).ToArray();
        _ranks = Enumerable.Repeat(-1, definition.NodeCount).ToArray();
        for (var i = 0; i < definition.UpdateOrder.Length; i++) _ranks[definition.UpdateOrder[i]] = i;
        foreach (var read in definition.Reads) _targets[read.ReadNodeIndex] = read.CacheNodeIndex;
        _first = new int[definition.NodeCount]; _last = new int[definition.NodeCount]; _best = new int[definition.NodeCount];
        _next = new int[callCapacity]; _contexts = new AlsPoseUpdateContext[callCapacity]; _skipped = new AlsPoseUpdateContext[callCapacity];
    }

    public void Begin(AlsFrameIdentity identity)
    {
        if (_draining || identity.SlotGeneration == 0) throw new InvalidOperationException("Invalid pose-cache traversal begin.");
        _identity = identity; _count = 0; _rank = -1; SourceUpdateCount = 0;
        _updateCounter=null;
        Array.Fill(_first, -1); Array.Fill(_last, -1); Array.Fill(_best, -1);
        _begun = true; _finished = false;
    }

    public void Use(int readNodeIndex, in AlsPoseUpdateContext context)
    {
        if (!_begun || _finished || context.Identity != _identity || (uint)readNodeIndex >= _targets.Length ||
            _targets[readNodeIndex] < 0) throw new InvalidOperationException("Invalid, stale or unbound pose-cache call.");
        var cache = _targets[readNodeIndex];
        if (_ranks[cache] <= _rank) throw new InvalidOperationException("Pose-cache call arrived after its compiled update position.");
        if (_count == _contexts.Length) throw new InvalidOperationException("Pose-cache call capacity exceeded.");
        if(_count>0 && _updateCounter!=context.UpdateCounter)
            throw new InvalidOperationException("Pose-cache readers disagree on animation update traversal.");
        _updateCounter=context.UpdateCounter;
        var call = _count++;
        _contexts[call] = context; _next[call] = -1;
        if (_first[cache] < 0) _first[cache] = call;
        else _next[_last[cache]] = call;
        _last[cache] = call;
        if (_best[cache] < 0 || context.Weight > _contexts[_best[cache]].Weight) _best[cache] = call;
    }

    public void Drain(IAlsPoseCacheUpdateSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (!_begun || _draining || _finished) throw new InvalidOperationException("Pose-cache traversal is not awaiting update.");
        _draining = true;
        try
        {
            for (_rank = 0; _rank < _definition.UpdateOrder.Length; _rank++)
            {
                var cache = _definition.UpdateOrder[_rank];
                var winner = _best[cache];
                if (winner < 0) continue;
                var context = _contexts[winner];
                sink.UpdateCachedSource(cache, context);
                SourceUpdateCount++;
                if (!context.HasSharedContext || context.SkippedUpdateHandler < 0) continue;
                var count = 0;
                for (var call = _first[cache]; call >= 0; call = _next[call])
                    if (call != winner && _contexts[call].HasSharedContext) _skipped[count++] = _contexts[call];
                sink.OnCachedUpdatesSkipped(context.SkippedUpdateHandler, _skipped.AsSpan(0, count));
            }
        }
        finally
        {
            _draining = false; _finished = true;
        }
    }
}
