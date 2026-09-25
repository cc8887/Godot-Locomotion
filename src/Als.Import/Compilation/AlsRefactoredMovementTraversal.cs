using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredSkippedCacheUpdates(int Handler, int First, int Count);

/// <summary>One deferred update pass from Movement Details' six readers through
/// Movement and all seven direction caches. Parent and inertialization consumers
/// receive candidate commands/messages; this owner never dispatches gameplay.</summary>
public sealed class AlsRefactoredMovementTraversal : IAlsPoseCacheUpdateSink
{
    public AlsPoseCacheDefinition Caches { get; }
    public AlsRefactoredMovementDetailsPoseGraph Details { get; }
    public AlsRefactoredDirectionRuntime Direction { get; }
    public AlsRefactoredDirectionSourceRuntime Sources { get; }
    private readonly AlsPoseCacheTraversal _traversal;
    private AlsPoseCacheTraversal? _activeTraversal;
    private bool _shared, _sharedComplete;
    private readonly HashSet<int> _movementReads;
    private readonly AlsRefactoredDirectionCacheUpdate[] _updates = new AlsRefactoredDirectionCacheUpdate[8];
    private readonly AlsRefactoredSkippedCacheUpdates[] _batches = new AlsRefactoredSkippedCacheUpdates[8];
    private readonly AlsPoseUpdateContext[] _skipped = new AlsPoseUpdateContext[32];
    private readonly AlsRefactoredSourcePlayerInput[] _inputs = new AlsRefactoredSourcePlayerInput[24];
    private readonly AlsPoseUpdateContext[] _contexts = new AlsPoseUpdateContext[24];
    private int _updateCount, _batchCount, _skippedCount, _inputCount;
    private AlsGraphTraversalCounter _initialization, _nextInitialization;
    private AlsGraphTraversalCounter _requestedInitialization;
    private AlsFrameIdentity _identity;
    private AlsRefactoredDirectionInput _direction;
    private AlsRefactoredMovementPlayerInput _movement;
    private AlsRefactoredForwardInput _forward;
    private Vector4 _velocity, _yaw;
    private bool _prepared, _initializeInstance, _resetMovement;
    private long _committed = -1;
    private AlsRefactoredMovementDetailsSourceRuntime? _owner;
    private AlsRefactoredMovementParentRuntime? _parent;
    private AlsPoseUpdateContext _movementContext;
    public ReadOnlySpan<AlsRefactoredDirectionCacheUpdate> CacheUpdates => _prepared ? _updates.AsSpan(0, _updateCount) : throw new InvalidOperationException("No Movement traversal.");
    public ReadOnlySpan<AlsRefactoredSkippedCacheUpdates> SkippedBatches => _prepared ? _batches.AsSpan(0, _batchCount) : throw new InvalidOperationException("No Movement traversal.");
    public ReadOnlySpan<AlsPoseUpdateContext> SkippedContexts => _prepared ? _skipped.AsSpan(0, _skippedCount) : throw new InvalidOperationException("No Movement traversal.");
    public ReadOnlySpan<AlsRefactoredSourcePlayerInput> SourceInputs => _prepared ? _inputs.AsSpan(0, _inputCount) : throw new InvalidOperationException("No Movement traversal.");
    public ReadOnlySpan<AlsPoseUpdateContext> SourceContexts => _prepared ? _contexts.AsSpan(0, _inputCount) : throw new InvalidOperationException("No Movement traversal.");
    public AlsPoseUpdateContext MovementContext => _prepared ? _movementContext : throw new InvalidOperationException("No Movement traversal.");
    public bool InitializeMovement => _prepared ? _resetMovement : throw new InvalidOperationException("No Movement traversal.");

    public AlsRefactoredMovementTraversal(AlsRefactoredAnimationCatalog catalog, AlsRefactoredMovementDetailsPoseGraph details,
        AlsRefactoredDirectionSourceProfile direction, int firstPlayer)
    {
        if (direction.Graph.Resources.Crouching || catalog.IndexDigest != details.Resources.CatalogDigest ||
            catalog.IndexDigest != direction.Graph.Resources.CatalogDigest) throw new ArgumentException("Foreign Movement traversal profiles.");
        Details = details; Direction = new(direction.Graph.Resources); Sources = new(direction, firstPlayer);
        _movementReads = details.States.ToArray().Select(s => s.ReadPropertyIndex).ToHashSet();
        var payload = catalog.Read(AlsRefactoredRotatePlayers.Blueprint(false));
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(n => n.GetProperty("compiledNodeIndex").GetInt32());
        var selected = direction.Caches.UpdateOrder.ToArray().Append(details.MovementCache.PropertyIndex).ToHashSet();
        var order = payload.GetProperty("compiled").GetProperty("orderedSavedPoseNodes").EnumerateArray().Single(n => n.GetProperty("root").GetString() == "AnimGraph")
            .GetProperty("compiledNodeIndices").EnumerateArray().Select(n => nodes[n.GetInt32()].GetProperty("propertyIndex").GetInt32()).Where(selected.Contains).ToArray();
        if (order.Length != 8 || order[0] != details.MovementCache.PropertyIndex || !order.AsSpan(1).SequenceEqual(direction.Caches.UpdateOrder))
            throw new ArgumentException("Movement dependency order differs.");
        Caches = new(direction.Caches.NodeCount, order, direction.Caches.Reads.ToArray().Concat(details.States.ToArray()
            .Select(s => new AlsPoseCacheReadBinding(s.ReadPropertyIndex, details.MovementCache.PropertyIndex))).ToArray());
        _traversal = new(Caches, 32);
    }

    public void Prepare(long frame, AlsRefactoredMovementDetailsRuntime detailsMachine, AlsRefactoredMovementDetailsSourceRuntime details,
        AlsGraphTraversalCounter initialization, AlsRefactoredDirectionInput direction, AlsRefactoredMovementPlayerInput movement,
        AlsRefactoredForwardInput forward, bool initializeInstance = false, Vector4 yaw = default,
        AlsRefactoredMovementParentRuntime? parent = null)
        => PrepareCore(frame, detailsMachine, details, initialization, direction, movement, forward, initializeInstance, yaw, parent, null);

    internal void PrepareShared(long frame, AlsRefactoredMovementDetailsRuntime detailsMachine, AlsRefactoredMovementDetailsSourceRuntime details,
        AlsGraphTraversalCounter initialization, AlsRefactoredDirectionInput direction, AlsRefactoredMovementPlayerInput movement,
        AlsRefactoredForwardInput forward, bool initializeInstance, Vector4 yaw, AlsRefactoredMovementParentRuntime parent, AlsPoseCacheTraversal traversal)
        => PrepareCore(frame, detailsMachine, details, initialization, direction, movement, forward, initializeInstance, yaw, parent, traversal);

    private void PrepareCore(long frame, AlsRefactoredMovementDetailsRuntime detailsMachine, AlsRefactoredMovementDetailsSourceRuntime details,
        AlsGraphTraversalCounter initialization, AlsRefactoredDirectionInput direction, AlsRefactoredMovementPlayerInput movement,
        AlsRefactoredForwardInput forward, bool initializeInstance, Vector4 yaw,
        AlsRefactoredMovementParentRuntime? parent, AlsPoseCacheTraversal? shared)
    {
        if (_prepared || frame < 0 || frame <= _committed || !ReferenceEquals(details.Profile, Details) ||
            _owner is not null && !ReferenceEquals(_owner, details) || details.FirstPlayer != Sources.FirstPlayer || !initialization.HasUpdated)
            throw new ArgumentException("Invalid Movement traversal owner/frame.");
        details.ValidateTraversal(frame, detailsMachine);
        if (details.CacheReads.IsEmpty || initializeInstance && !detailsMachine.Candidate.Reinitialized) throw new ArgumentException("Movement readers are not initialized.");
        _identity = details.CacheReads[0].Context.Identity;
        parent?.ValidateContext(_identity, Details.Resources.CatalogDigest); _parent = parent;
        _nextInitialization = initializeInstance ? default : _initialization;
        _requestedInitialization = initialization; _direction = direction; _movement = movement; _forward = forward;
        _velocity = details.DesiredWeights; _yaw = yaw; _initializeInstance = initializeInstance; _resetMovement = false;
        _shared = shared is not null; _sharedComplete = false; _activeTraversal = shared ?? _traversal;
        _updateCount = _batchCount = _skippedCount = _inputCount = 0;
        try
        {
            foreach (var read in details.CacheInitializationReads)
            {
                if (!_movementReads.Contains(read)) throw new ArgumentException("Foreign Movement initialization reader.");
                // This UE revision declares a SaveCachedPose UpdateCounter but
                // never synchronizes it in Update/PostGraphUpdate. Preserve its
                // actual initialization-counter behavior, not an inferred fix.
                if (!_nextInitialization.MatchesCounter(initialization))
                { _nextInitialization = initialization; _resetMovement = true; }
            }
            if (!_shared) _traversal.Begin(_identity);
            foreach (var read in details.CacheReads) _activeTraversal.Use(read.ReadPropertyIndex, read.Context);
            _owner ??= details; _prepared = true;
            if (!_shared) { _traversal.Drain(this); CompleteSources(frame); }
        }
        catch { Cancel(); throw; }
    }
    internal void CompleteShared(long frame)
    {
        if (!_prepared || !_shared || _sharedComplete || frame != _identity.FrameId || _activeTraversal is not { IsFinished: true })
            throw new ArgumentException("Invalid shared Movement completion.");
        CompleteSources(frame); _sharedComplete = true;
    }
    private void CompleteSources(long frame)
    {
        Sources.CompleteShared(frame);
        for (var i = 0; i < _owner!.SourceInputs.Length; i++) Add(_owner.SourceInputs[i], _owner.SourceContexts[i]);
        for (var i = 0; i < Sources.SourceInputs.Length; i++) Add(Sources.SourceInputs[i], Sources.SourceContexts[i]);
    }
    private void Add(AlsRefactoredSourcePlayerInput input, AlsPoseUpdateContext context)
    {
        for (var i = 0; i < _inputCount; i++) if (_inputs[i].PlayerId == input.PlayerId) throw new InvalidOperationException("Repeated Movement player identity.");
        _inputs[_inputCount] = input; _contexts[_inputCount++] = context;
    }
    void IAlsPoseCacheUpdateSink.UpdateCachedSource(int cacheNodeIndex, in AlsPoseUpdateContext context)
    {
        _updates[_updateCount++] = new(cacheNodeIndex, context);
        if (cacheNodeIndex == Details.MovementCache.PropertyIndex)
        {
            _movementContext = context;
            Direction.Prepare(_identity.FrameId, _direction, context.Delta, context.Weight, _resetMovement, context.UpdateCounter);
            Sources.PrepareShared(Direction, context, _requestedInitialization, _velocity, _movement, _forward,
                _initializeInstance, _yaw, _activeTraversal!, _parent);
        }
        else ((IAlsPoseCacheUpdateSink)Sources).UpdateCachedSource(cacheNodeIndex, context);
    }
    void IAlsPoseCacheUpdateSink.OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped)
    {
        _batches[_batchCount++] = new(handlerNodeIndex, _skippedCount, skipped.Length);
        skipped.CopyTo(_skipped.AsSpan(_skippedCount)); _skippedCount += skipped.Length;
    }
    public void ValidateCommit(long frame)
    {
        if (!_prepared || frame != _identity.FrameId || _shared && !_sharedComplete) throw new ArgumentException("Incomplete Movement traversal.");
        Direction.ValidateCommit(frame); Sources.ValidateCommit(frame);
    }
    public void Commit(long frame)
    {
        ValidateCommit(frame); Direction.Commit(frame); Sources.Commit(frame);
        _initialization = _nextInitialization; _committed = frame; Cancel();
    }
    public void Cancel() { _prepared = _sharedComplete = false; _parent = null; _activeTraversal = null; Direction.Cancel(); Sources.Cancel(); }
}
