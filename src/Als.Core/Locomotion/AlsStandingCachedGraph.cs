using System.Runtime.CompilerServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsCachedMachineBinding(int MachineNodeIndex, int CacheNodeIndex);
public readonly record struct AlsPoseCacheCall(int ReadNodeIndex, AlsPoseUpdateContext Context);

public sealed class AlsStandingCachedGraphDefinition
{
    private readonly int[] _stopReads;
    private readonly int[] _detailReads;
    private readonly AlsDetailTransitionDefinition[] _detailTransitions;
    public AlsPoseCacheDefinition Caches { get; }
    public AlsGroundedMachineDefinition Standing { get; }
    public AlsGroundedMachineDefinition Stop { get; }
    public AlsCachedMachineBinding StandingBinding { get; }
    public AlsCachedMachineBinding DetailBinding { get; }
    public int StopNodeIndex { get; }
    public int CycleCacheIndex { get; }
    public int EntryReadIndex { get; }
    public int MovingReadIndex { get; }
    public ReadOnlySpan<int> StopReads => _stopReads;
    public ReadOnlySpan<int> DetailReads => _detailReads;
    public ReadOnlySpan<AlsDetailTransitionDefinition> DetailTransitions => _detailTransitions;

    public AlsStandingCachedGraphDefinition(AlsPoseCacheDefinition caches, AlsGroundedMachineDefinition standing,
        AlsGroundedMachineDefinition stop, AlsDetailTransitionDefinition[] detailTransitions,
        AlsCachedMachineBinding standingBinding, AlsCachedMachineBinding detailBinding,
        int stopNodeIndex, int cycleCacheIndex, int entryReadIndex, int movingReadIndex, int[] stopReads, int[] detailReads)
    {
        if (standing.Kind != AlsGroundedMachineKind.Standing || stop.Kind != AlsGroundedMachineKind.Stop ||
            standing.States.Length != 5 || stop.States.Length != 7 || stopReads.Length != 7 || detailReads.Length != 6 ||
            stopReads[1] != -1 || stopReads[2] != -1 || standingBinding.MachineNodeIndex < 0 ||
            detailBinding.MachineNodeIndex < 0 || stopNodeIndex < 0)
            throw new ArgumentException("Invalid Standing cache graph.");
        var order = caches.UpdateOrder.ToArray();
        var standingRank = Array.IndexOf(order, standingBinding.CacheNodeIndex);
        var detailRank = Array.IndexOf(order, detailBinding.CacheNodeIndex);
        var cycleRank = Array.IndexOf(order, cycleCacheIndex);
        if (standingRank < 0 || detailRank <= standingRank || cycleRank <= detailRank)
            throw new ArgumentException("Standing cache update order violates source dependencies.");
        var reads = caches.Reads.ToArray().ToDictionary(r => r.ReadNodeIndex, r => r.CacheNodeIndex);
        CheckRead(entryReadIndex, standingBinding.CacheNodeIndex);
        CheckRead(movingReadIndex, detailBinding.CacheNodeIndex);
        for (var i = 0; i < stopReads.Length; i++) if (i is not 1 and not 2) CheckRead(stopReads[i], detailBinding.CacheNodeIndex);
        foreach (var read in detailReads) CheckRead(read, cycleCacheIndex);
        Caches = caches; Standing = standing; Stop = stop;
        StandingBinding = standingBinding; DetailBinding = detailBinding; StopNodeIndex = stopNodeIndex;
        CycleCacheIndex = cycleCacheIndex; EntryReadIndex = entryReadIndex; MovingReadIndex = movingReadIndex;
        _stopReads = (int[])stopReads.Clone(); _detailReads = (int[])detailReads.Clone();
        _detailTransitions = (AlsDetailTransitionDefinition[])detailTransitions.Clone();
        void CheckRead(int read, int cache)
        {
            if (!reads.TryGetValue(read, out var target) || target != cache)
                throw new ArgumentException("State cache reference does not target the source cache.");
        }
    }
}

public readonly record struct AlsStandingDetailInputs(AlsGait Gait, float WeightGait, bool Pivot,
    float RelevantTimeRemaining, float MainGroundedRecordedWeight);
public record struct AlsStandingCachedGraphState(AlsFrameIdentity Identity, bool HasUpdated,
    AlsGroundedMachineState Standing, AlsGroundedMachineState Stop, AlsDetailMachineState Detail, float DetailRecordedWeight);
public struct AlsStandingCachedGraphUpdate
{
    public AlsStandingCachedGraphState State { get; internal set; }
    public bool StandingInitialized { get; internal set; }
    public int StopInitializationCount { get; internal set; }
    public bool DetailInitialized { get; internal set; }
    public bool CycleInitialized { get; internal set; }
    public AlsGroundedMachineUpdate Standing { get; internal set; }
    public AlsGroundedMachineUpdate Stop { get; internal set; }
    public AlsDetailMachineUpdate Detail { get; internal set; }
    public bool StandingUpdated { get; internal set; }
    public bool StopUpdated { get; internal set; }
    public bool DetailUpdated { get; internal set; }
    public bool CycleUpdated { get; internal set; }
    public AlsPoseUpdateContext StandingContext { get; internal set; }
    public AlsPoseUpdateContext StopContext { get; internal set; }
    public AlsPoseUpdateContext DetailContext { get; internal set; }
    public AlsPoseUpdateContext CycleContext { get; internal set; }
}

/// <summary>Source registration, initialization, notifications and inertialization requests are candidate-only.
/// The owner must finalize source synchronization and pose evaluation before committing the returned state.</summary>
public interface IAlsStandingCachedGraphSink
{
    // Return true only when this read actually initialized its Save source in the candidate lifecycle.
    bool InitializeStandingCache(int readNodeIndex) => false;
    void InitializeStandingSources(in AlsGroundedMachineUpdate initialization) { }
    void InitializeStopSources(in AlsGroundedMachineUpdate initialization) { }
    void InitializeDetailSources(in AlsDetailMachineUpdate initialization) { }
    void UpdateStandingSources(in AlsGroundedMachineUpdate update, in AlsPoseUpdateContext context);
    void UpdateStopSources(in AlsGroundedMachineUpdate update, in AlsPoseUpdateContext context);
    void UpdateDetailSources(in AlsDetailMachineUpdate update, in AlsPoseUpdateContext context);
    void UpdateCycleSource(in AlsPoseUpdateContext context);
    void RequestInertialization(in AlsPoseUpdateContext context, float seconds);
    void OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped);
}

[InlineArray(5)] internal struct AlsStandingAutomaticTimes { private AlsGroundedAutomaticTime _element; }

/// <summary>Standing/Stop/Detail update composition. Main Movement, Main Grounded and Slot owners supply entry contexts;
/// this graph does not pretend those upstream weights are always one.</summary>
public sealed class AlsStandingCachedGraph : IAlsPoseCacheUpdateSink
{
    private readonly AlsStandingCachedGraphDefinition _definition;
    private readonly AlsPoseCacheTraversal _caches;
    private AlsPoseCacheTraversal? _activeCaches;
    private AlsStandingCachedGraphState _previous;
    private AlsStandingCachedGraphUpdate _candidate;
    private AlsGroundedRuleInput _rules;
    private AlsStandingDetailInputs _detailInputs;
    private AlsStandingAutomaticTimes _times;
    private IAlsStandingCachedGraphSink? _sink;
    public int SourceUpdateCount => _caches.SourceUpdateCount;

    public AlsStandingCachedGraph(AlsStandingCachedGraphDefinition definition)
    {
        _definition = definition;
        _caches = new(definition.Caches, definition.Caches.Reads.Length + 16);
    }

    public AlsStandingCachedGraphUpdate Prepare(in AlsStandingCachedGraphState previous, AlsFrameIdentity identity,
        in AlsGroundedRuleInput rules, in AlsStandingDetailInputs detailInputs,
        ReadOnlySpan<AlsGroundedAutomaticTime> automaticTimes, ReadOnlySpan<AlsPoseCacheCall> entryCalls,
        IAlsStandingCachedGraphSink sink)
    {
        foreach (var call in entryCalls)
            if (call.ReadNodeIndex != _definition.EntryReadIndex || call.Context.Identity != identity)
                throw new ArgumentException("Standing entry must retain its source binding and frame identity.");
        if (_sink is not null) throw new ArgumentException("Standing graph is already preparing a candidate.");
        _caches.Begin(identity);
        BeginShared(previous, identity, rules, detailInputs, automaticTimes, _caches, sink);
        try
        {
            foreach (var call in entryCalls) _caches.Use(call.ReadNodeIndex, call.Context);
            _caches.Drain(this);
            return EndShared();
        }
        finally { CancelShared(); }
    }

    internal void BeginShared(in AlsStandingCachedGraphState previous, AlsFrameIdentity identity,
        in AlsGroundedRuleInput rules, in AlsStandingDetailInputs detailInputs,
        ReadOnlySpan<AlsGroundedAutomaticTime> automaticTimes, AlsPoseCacheTraversal caches, IAlsStandingCachedGraphSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (_sink is not null || identity.SlotGeneration == 0 || automaticTimes.Length != 5 ||
            !ReferenceEquals(caches.Definition, _definition.Caches) || previous.HasUpdated &&
            (previous.Identity.CharacterId != identity.CharacterId || previous.Identity.SlotGeneration != identity.SlotGeneration ||
             previous.Identity.FrameId >= identity.FrameId)) throw new ArgumentException("Invalid Standing graph candidate.");
        _previous = previous; _rules = rules; _detailInputs = detailInputs; automaticTimes.CopyTo(_times);
        _candidate = new() { State = previous with { Identity = identity, HasUpdated = true, DetailRecordedWeight = 0 } };
        _activeCaches = caches; _sink = sink;
    }

    internal void ResetStandingRoot()
    {
        if (_sink is null || _candidate.StandingUpdated) throw new InvalidOperationException("Standing cache was initialized after update.");
        // Detail/Cycles are separate saved poses and retain their own initialization counters.
        var initialization = AlsGroundedStateMachine.Initialize(_definition.Standing);
        _candidate.StandingInitialized = true;
        _candidate.Standing = initialization;
        _previous.Standing = initialization.State;
        _candidate.State = _candidate.State with { Standing = initialization.State };
        _sink.InitializeStandingSources(initialization);
    }

    internal void UpdateSharedCache(int cacheNodeIndex, in AlsPoseUpdateContext context) =>
        ((IAlsPoseCacheUpdateSink)this).UpdateCachedSource(cacheNodeIndex, context);

    internal AlsStandingCachedGraphUpdate EndShared()
    {
        if (_sink is null || !_activeCaches!.IsFinished) throw new InvalidOperationException("Standing candidate queue has not finished.");
        var result = _candidate; CancelShared(); return result;
    }

    internal void CancelShared() { _sink = null; _activeCaches = null; }

    void IAlsPoseCacheUpdateSink.UpdateCachedSource(int cacheNodeIndex, in AlsPoseUpdateContext context)
    {
        if (_sink is null || context.Identity != _candidate.State.Identity) throw new InvalidOperationException("Invalid shared Standing update.");
        var serial = context.Identity.FrameId;
        if (cacheNodeIndex == _definition.StandingBinding.CacheNodeIndex)
        {
            var update = AlsGroundedStateMachine.Update(_definition.Standing, _previous.Standing, _rules,
                _times, context.Weight, context.Delta, serial, updateCounter:context.UpdateCounter);
            if (_candidate.StandingUpdated) throw new InvalidOperationException("Standing cache was updated twice.");
            _candidate.Standing = update; _candidate.StandingUpdated = true; _candidate.StandingContext = context;
            _candidate.State = _candidate.State with { Standing = update.State };
            for (var i = 0; i < update.InitializationCount; i++)
            {
                var state = update.GetInitialization(i);
                if (state == 1) InitializeDetail(_definition.MovingReadIndex);
                else if (state == 2)
                {
                    var initialization = AlsGroundedStateMachine.Initialize(_definition.Stop);
                    _candidate.StopInitializationCount++;
                    _candidate.Stop = initialization;
                    _candidate.State = _candidate.State with { Stop = initialization.State };
                    InitializeDetail(_definition.StopReads[initialization.State.CurrentState]);
                    _sink!.InitializeStopSources(initialization);
                }
            }
            if (update.InertializationSeconds >= 0) _sink!.RequestInertialization(context, update.InertializationSeconds);
            _sink!.UpdateStandingSources(update, context);
            for (var i = 0; i < update.UpdateCount; i++)
            {
                var child = update.GetUpdate(i);
                var path = context.WithWeight(child.Weight).WithState(_definition.StandingBinding.MachineNodeIndex,
                    child.State, child.InertializationSync);
                if (child.State == 1) _activeCaches!.Use(_definition.MovingReadIndex, path);
                else if (child.State == 2) UpdateStop(path);
            }
        }
        else if (cacheNodeIndex == _definition.DetailBinding.CacheNodeIndex)
        {
            var inputs = new AlsDetailMachineInput(_detailInputs.Gait, _detailInputs.WeightGait, _detailInputs.Pivot,
                _detailInputs.RelevantTimeRemaining, _detailInputs.MainGroundedRecordedWeight,
                _previous.DetailRecordedWeight, context.Weight);
            var update = AlsLocomotionDetailMachine.Update(_definition.DetailTransitions, _previous.Detail, inputs, context.Delta, serial, context.UpdateCounter);
            _candidate.Detail = update; _candidate.DetailUpdated = true; _candidate.DetailContext = context;
            _candidate.State = _candidate.State with { Detail = update.State, DetailRecordedWeight = context.Weight };
            for (var i = 0; i < update.InitializationCount; i++)
                InitializeCycle(_definition.DetailReads[(int)update.GetInitialization(i)]);
            if (update.InertializationSeconds >= 0) _sink!.RequestInertialization(context, update.InertializationSeconds);
            _sink!.UpdateDetailSources(update, context);
            for (var i = 0; i < update.UpdateCount; i++)
            {
                var child = update.GetUpdate(i);
                _activeCaches!.Use(_definition.DetailReads[(int)child.State], context.WithWeight(child.Weight)
                    .WithState(_definition.DetailBinding.MachineNodeIndex, (int)child.State, child.InertializationSync));
            }
        }
        else if (cacheNodeIndex == _definition.CycleCacheIndex)
        {
            _candidate.CycleUpdated = true; _candidate.CycleContext = context;
            _sink!.UpdateCycleSource(context);
        }
        else throw new InvalidOperationException("Unhandled source cache in Standing graph.");
    }

    private void InitializeDetail(int readNodeIndex)
    {
        if (!_sink!.InitializeStandingCache(readNodeIndex)) return;
        if (_candidate.DetailUpdated) throw new InvalidOperationException("Detail cache initialized after its Update.");
        var initialization = AlsLocomotionDetailMachine.Initialize();
        _candidate.DetailInitialized = true; _previous.Detail = initialization.State;
        _candidate.Detail = initialization;
        _candidate.State = _candidate.State with { Detail = initialization.State };
        InitializeCycle(_definition.DetailReads[(int)initialization.State.CurrentState]);
        _sink.InitializeDetailSources(initialization);
    }

    private void InitializeCycle(int readNodeIndex)
    {
        if (_sink!.InitializeStandingCache(readNodeIndex)) _candidate.CycleInitialized = true;
    }

    private void UpdateStop(in AlsPoseUpdateContext context)
    {
        Span<AlsGroundedAutomaticTime> times = stackalloc AlsGroundedAutomaticTime[7];
        times.Clear();
        var update = AlsGroundedStateMachine.Update(_definition.Stop, _candidate.State.Stop, _rules,
            times, context.Weight, context.Delta, context.Identity.FrameId, updateCounter:context.UpdateCounter);
        _candidate.Stop = update; _candidate.StopUpdated = true; _candidate.StopContext = context;
        _candidate.State = _candidate.State with { Stop = update.State };
        for (var i = 0; i < update.InitializationCount; i++)
            InitializeDetail(_definition.StopReads[update.GetInitialization(i)]);
        _sink!.UpdateStopSources(update, context);
        for (var i = 0; i < update.UpdateCount; i++)
        {
            var child = update.GetUpdate(i);
            _activeCaches!.Use(_definition.StopReads[child.State], context.WithWeight(child.Weight)
                .WithState(_definition.StopNodeIndex, child.State, child.InertializationSync));
        }
    }

    void IAlsPoseCacheUpdateSink.OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped) =>
        _sink!.OnCachedUpdatesSkipped(handlerNodeIndex, skipped);
}
