using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public sealed class AlsMainGroundedCachedGraphDefinition
{
    private readonly int[] _entries;
    public AlsPoseCacheDefinition Caches { get; }
    public AlsGroundedMachineDefinition Main { get; }
    public int MainNodeIndex { get; }
    public int MainCacheIndex { get; }
    public int SlotNodeIndex { get; }
    public bool AlwaysUpdateSource { get; }
    public ReadOnlySpan<int> EntryReadIndices => _entries;
    public int CrouchingReadIndex { get; }
    public int CrouchingCacheIndex { get; }
    public AlsStandingCachedGraphDefinition Standing { get; }
    public AlsCrouchingStateDefinition Crouching { get; }

    public AlsMainGroundedCachedGraphDefinition(AlsPoseCacheDefinition caches, AlsGroundedMachineDefinition main,
        int mainNodeIndex, int mainCacheIndex, int slotNodeIndex, bool alwaysUpdateSource, int[] entries,
        int crouchingReadIndex, int crouchingCacheIndex, AlsStandingCachedGraphDefinition standing, AlsCrouchingStateDefinition crouching)
    {
        ArgumentNullException.ThrowIfNull(caches); ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(standing); ArgumentNullException.ThrowIfNull(crouching);
        if (main.Kind != AlsGroundedMachineKind.Main || main.States.Length != 8 || mainNodeIndex < 0 || slotNodeIndex < 0 ||
            entries.Length != 2 || entries.Distinct().Count() != 2 || !ReferenceEquals(caches, standing.Caches) ||
            !caches.UpdateOrder.SequenceEqual(new[] { mainCacheIndex, standing.StandingBinding.CacheNodeIndex,
                standing.DetailBinding.CacheNodeIndex, standing.CycleCacheIndex, crouchingCacheIndex, crouching.CycleCacheNodeIndex }))
            throw new ArgumentException("Invalid Main/BaseLayer cached graph definition.");
        var reads = caches.Reads.ToArray().ToDictionary(r => r.ReadNodeIndex, r => r.CacheNodeIndex);
        foreach (var entry in entries) Check(entry, mainCacheIndex);
        Check(standing.EntryReadIndex, standing.StandingBinding.CacheNodeIndex); Check(crouchingReadIndex, crouchingCacheIndex);
        foreach (var read in crouching.Caches.Reads) Check(read.ReadNodeIndex, read.CacheNodeIndex);
        Caches = caches; Main = main; MainNodeIndex = mainNodeIndex; MainCacheIndex = mainCacheIndex;
        SlotNodeIndex = slotNodeIndex; AlwaysUpdateSource = alwaysUpdateSource; _entries = (int[])entries.Clone();
        CrouchingReadIndex = crouchingReadIndex; CrouchingCacheIndex = crouchingCacheIndex; Standing = standing; Crouching = crouching;
        void Check(int read, int writer)
        { if (!reads.TryGetValue(read, out var target) || target != writer) throw new ArgumentException("Main cache source binding differs."); }
    }
}

public readonly record struct AlsMainGroundedCachedState(AlsFrameIdentity Identity, bool HasUpdated,
    AlsGroundedMachineState Main, AlsStandingCachedGraphState Standing, AlsCrouchingStateFrame Crouching, AlsSlotWeights Slot);

public struct AlsMainGroundedCachedUpdate
{
    public AlsMainGroundedCachedState State { get; internal set; }
    public AlsGroundedMachineUpdate Main { get; internal set; }
    public AlsStandingCachedGraphUpdate Standing { get; internal set; }
    public AlsCrouchingStateUpdate Crouching { get; internal set; }
    public bool MainUpdated { get; internal set; }
    public bool CrouchingUpdated { get; internal set; }
    public bool CrouchingInitialized { get; internal set; }
    public bool CrouchingCyclesUpdated { get; internal set; }
    public AlsPoseUpdateContext MainContext { get; internal set; }
    public AlsPoseUpdateContext CrouchingCyclesContext { get; internal set; }
}

/// <summary>All callbacks change candidate state only. InitializeMainCache returns true only when
/// the owning SaveCachedPose initialization counter actually caused its source to initialize.
/// Montage weights, registration, pose-cache lifecycle and final pose/commit remain with the owner.</summary>
public interface IAlsMainGroundedCachedGraphSink
{
    IAlsStandingCachedGraphSink Standing { get; }
    IAlsCrouchingStateUpdateSink Crouching { get; }
    bool InitializeMainCache(int readNodeIndex);
    void UpdateMainSources(in AlsGroundedMachineUpdate update, in AlsPoseUpdateContext context);
    void UpdateGroundedSlot(int slotNodeIndex, in AlsSlotWeights weights, in AlsSlotSourceUpdate source, in AlsPoseUpdateContext context);
    void UpdateCrouchingCycles(in AlsPoseUpdateContext context);
    void RequestInertialization(in AlsPoseUpdateContext context, float seconds);
    void OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped);
}

public sealed class AlsMainGroundedCachedGraph : IAlsPoseCacheUpdateSink, IAlsCrouchingStateUpdateSink
{
    private readonly AlsMainGroundedCachedGraphDefinition _definition;
    private readonly AlsPoseCacheTraversal _caches;
    private readonly AlsStandingCachedGraph _standing;
    private readonly AlsCrouchingStateRuntime _crouching;
    private readonly AlsGroundedAutomaticTime[] _mainTimes = new AlsGroundedAutomaticTime[8], _crouchingTimes = new AlsGroundedAutomaticTime[5];
    private AlsMainGroundedCachedUpdate _candidate;
    private AlsGroundedMachineState _mainState;
    private AlsCrouchingStateFrame _crouchingState;
    private AlsSlotWeights _slotState;
    private AlsGroundedRuleInput _rules;
    private AlsSlotWeights _slot;
    private bool _markSlotBlendingOut;
    private Func<float, float>? _changeStance;
    private IAlsMainGroundedCachedGraphSink? _sink;
    public int SourceUpdateCount => _caches.SourceUpdateCount;

    public AlsMainGroundedCachedGraph(AlsMainGroundedCachedGraphDefinition definition)
    {
        _definition = definition; _caches = new(definition.Caches, definition.Caches.Reads.Length + 32);
        _standing = new(definition.Standing); _crouching = new(definition.Crouching);
    }

    /// <summary>Called by the actual Main Save initialization callback, before CacheBones/Update.
    /// The owner consumes the returned initialization journal without updating or ticking its sources.
    /// Child Save lifecycles are independent and retain their committed snapshots.</summary>
    public AlsMainGroundedCachedState InitializeMainSource(in AlsMainGroundedCachedState previous,
        out AlsGroundedMachineUpdate initialization)
    {
        if (_sink is not null) throw new InvalidOperationException("Cannot initialize Main during cached Update.");
        initialization = AlsGroundedStateMachine.Initialize(_definition.Main);
        return previous with { Main = initialization.State, Slot = default };
    }

    public AlsMainGroundedCachedUpdate Prepare(in AlsMainGroundedCachedState previous, AlsFrameIdentity identity,
        in AlsGroundedRuleInput rules, in AlsStandingDetailInputs detail, in AlsSlotWeights slot,
        ReadOnlySpan<AlsGroundedAutomaticTime> mainTimes, ReadOnlySpan<AlsGroundedAutomaticTime> standingTimes,
        ReadOnlySpan<AlsGroundedAutomaticTime> crouchingTimes, ReadOnlySpan<AlsPoseCacheCall> entries,
        IAlsMainGroundedCachedGraphSink sink, Func<float, float>? changeStance = null,
        bool initializeMainSource = false, bool markSlotBlendingOut = true)
    {
        ArgumentNullException.ThrowIfNull(sink); ArgumentNullException.ThrowIfNull(sink.Standing); ArgumentNullException.ThrowIfNull(sink.Crouching);
        if (_sink is not null || identity.SlotGeneration == 0 || mainTimes.Length != 8 || standingTimes.Length != 5 || crouchingTimes.Length != 5 ||
            previous.HasUpdated && (previous.Identity.CharacterId != identity.CharacterId || previous.Identity.SlotGeneration != identity.SlotGeneration ||
                previous.Identity.FrameId >= identity.FrameId)) throw new ArgumentException("Invalid Main/BaseLayer candidate.");
        slot.Validate();
        foreach (var entry in entries)
            if (!_definition.EntryReadIndices.Contains(entry.ReadNodeIndex) || entry.Context.Identity != identity)
                throw new ArgumentException("Main entry does not retain its source read and frame identity.");
        // Keep machine candidates separately: whole-graph record copies duplicate large transition buffers on the stack.
        _candidate = default;
        _mainState = initializeMainSource ? default : previous.Main;
        _crouchingState = previous.Crouching;
        _slotState = initializeMainSource ? default : previous.Slot;
        _rules = rules; _slot = slot; _markSlotBlendingOut = markSlotBlendingOut; _changeStance = changeStance;
        mainTimes.CopyTo(_mainTimes); crouchingTimes.CopyTo(_crouchingTimes);
        _caches.Begin(identity); _sink = sink;
        try
        {
            _standing.BeginShared(previous.Standing, identity, rules, detail, standingTimes, _caches, sink.Standing);
            foreach (var entry in entries) _caches.Use(entry.ReadNodeIndex, entry.Context);
            _caches.Drain(this);
            var standing = _standing.EndShared(); _candidate.Standing = standing;
            _candidate.State = new(identity, true, _mainState, standing.State, _crouchingState, _slotState);
            return _candidate;
        }
        finally { _standing.CancelShared(); _sink = null; _changeStance = null; }
    }

    void IAlsPoseCacheUpdateSink.UpdateCachedSource(int cacheNodeIndex, in AlsPoseUpdateContext context)
    {
        if (cacheNodeIndex == _definition.MainCacheIndex)
        {
            var source = AlsSlotSourceUpdate.Resolve(_slotState.SourceWeight, _slot, context,
                _definition.AlwaysUpdateSource, _markSlotBlendingOut);
            _slotState = _slot;
            _sink!.UpdateGroundedSlot(_definition.SlotNodeIndex, _slot, source, context);
            if (!source.Updated) return;
            var update = AlsGroundedStateMachine.Update(_definition.Main, _mainState, _rules,
                _mainTimes, source.Context.Weight, source.Context.Delta, context.Identity.FrameId, _changeStance, context.UpdateCounter);
            _candidate.Main = update; _candidate.MainUpdated = true; _candidate.MainContext = source.Context;
            _mainState = update.State;
            for (var i = 0; i < update.InitializationCount; i++)
            {
                var state = update.GetInitialization(i);
                if (state == 1 && _sink.InitializeMainCache(_definition.Standing.EntryReadIndex)) _standing.ResetStandingRoot();
                if (state == 2 && _sink.InitializeMainCache(_definition.CrouchingReadIndex))
                {
                    _candidate.Crouching = _crouching.Initialize(context.Identity, this);
                    _crouchingState = _candidate.Crouching.State;
                    _candidate.CrouchingInitialized = true;
                }
            }
            if (update.InertializationSeconds >= 0) _sink.RequestInertialization(source.Context, update.InertializationSeconds);
            _sink.UpdateMainSources(update, source.Context);
            for (var i = 0; i < update.UpdateCount; i++)
            {
                var child = update.GetUpdate(i);
                var path = source.Context.WithWeight(child.Weight).WithState(_definition.MainNodeIndex, child.State, child.InertializationSync);
                if (child.State == 1) _caches.Use(_definition.Standing.EntryReadIndex, path);
                else if (child.State == 2) _caches.Use(_definition.CrouchingReadIndex, path);
            }
        }
        else if (cacheNodeIndex == _definition.CrouchingCacheIndex)
        {
            var update = _crouching.Prepare(_crouchingState, _rules, _crouchingTimes, context, this);
            _candidate.Crouching = update; _candidate.CrouchingUpdated = true;
            _crouchingState = update.State;
        }
        else if (cacheNodeIndex == _definition.Crouching.CycleCacheNodeIndex)
        {
            _candidate.CrouchingCyclesUpdated = true; _candidate.CrouchingCyclesContext = context;
            _sink!.UpdateCrouchingCycles(context);
        }
        else _standing.UpdateSharedCache(cacheNodeIndex, context);
    }

    void IAlsPoseCacheUpdateSink.OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped) =>
        _sink!.OnCachedUpdatesSkipped(handlerNodeIndex, skipped);
    void IAlsCrouchingStateUpdateSink.ClearSourceWeights(byte states) => _sink!.Crouching.ClearSourceWeights(states);
    void IAlsCrouchingStateUpdateSink.ObserveStateInitialization(in AlsCrouchingStateUpdate initialization) =>
        _sink!.Crouching.ObserveStateInitialization(initialization);
    void IAlsCrouchingStateUpdateSink.ObserveStateUpdate(in AlsCrouchingStateUpdate update) => _sink!.Crouching.ObserveStateUpdate(update);
    void IAlsCrouchingStateUpdateSink.InitializeSource(int playerId) => _sink!.Crouching.InitializeSource(playerId);
    void IAlsCrouchingStateUpdateSink.InitializeSlot(int slotNodeIndex, int sourcePlayerId) => _sink!.Crouching.InitializeSlot(slotNodeIndex, sourcePlayerId);
    void IAlsCrouchingStateUpdateSink.InitializeCycleCache(int readNodeIndex) => _sink!.Crouching.InitializeCycleCache(readNodeIndex);
    void IAlsCrouchingStateUpdateSink.UpdateSource(int playerId, in AlsPoseUpdateContext context) => _sink!.Crouching.UpdateSource(playerId, context);
    void IAlsCrouchingStateUpdateSink.UpdateSlot(int slotNodeIndex, int sourcePlayerId, in AlsPoseUpdateContext context) =>
        _sink!.Crouching.UpdateSlot(slotNodeIndex, sourcePlayerId, context);
    void IAlsCrouchingStateUpdateSink.UseCycleCache(int readNodeIndex, in AlsPoseUpdateContext context) => _caches.Use(readNodeIndex, context);
    void IAlsCrouchingStateUpdateSink.RequestInertialization(in AlsPoseUpdateContext context, float seconds) =>
        _sink!.Crouching.RequestInertialization(context, seconds);
}
