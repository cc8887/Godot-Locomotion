using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public enum AlsMainBoneMachine : byte { Main, Standing, Stop, Detail, Crouching }

public interface IAlsMainGroundedBoneCacheSink
{
    void RefreshSourceBones(int cacheNodeIndex);
}

// Candidate-owned state-machine CacheBones counters, sharing the actual SaveCachedPose candidate.
// The owner supplies initialized machine snapshots and commits by swapping only after pose/events succeed.
public sealed class AlsMainGroundedBoneCache : IAlsPoseCachePoseSink
{
    private readonly AlsMainGroundedCachedGraphDefinition _definition;
    private readonly AlsGraphTraversalCounter[] _states = new AlsGraphTraversalCounter[31];
    private AlsPoseCacheEvaluation? _cache;
    private IAlsMainGroundedBoneCacheSink? _sink;
    private AlsGroundedMachineState _main, _standing, _stop, _crouching;
    private AlsDetailMachineState _detail;
    private bool _begun, _faulted, _stopInitializedThisFrame;
    public AlsFrameIdentity Identity { get; private set; }
    public AlsGraphTraversalCounter Counter { get; private set; }
    public int StateRefreshes { get; private set; }
    public int SourceRefreshes { get; private set; }
    public bool IsFaulted => _faulted || _cache?.IsFaulted == true;

    public AlsMainGroundedBoneCache(AlsMainGroundedCachedGraphDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition;
    }
    public bool Owns(AlsPoseCacheEvaluation cache) => _begun && !IsFaulted && ReferenceEquals(_cache, cache);

    public void Begin(AlsPoseCacheEvaluation cache, AlsMainGroundedBoneCache committed, AlsGraphTraversalCounter counter,
        in AlsMainGroundedCachedState previous, IAlsMainGroundedBoneCacheSink sink)
    {
        ArgumentNullException.ThrowIfNull(cache); ArgumentNullException.ThrowIfNull(committed); ArgumentNullException.ThrowIfNull(sink);
        if (ReferenceEquals(this, committed) || !ReferenceEquals(_definition, committed._definition) ||
            !ReferenceEquals(cache.Definition, _definition.Caches) || cache.IsFaulted || committed.IsFaulted ||
            cache.Identity.SlotGeneration == 0 || !counter.HasUpdated || previous.HasUpdated != committed._begun ||
            previous.HasUpdated && (committed.Identity != previous.Identity ||
                cache.Identity.CharacterId != previous.Identity.CharacterId || cache.Identity.SlotGeneration != previous.Identity.SlotGeneration ||
                cache.Identity.FrameId <= previous.Identity.FrameId))
            throw new ArgumentException("Invalid Main bone-cache candidate or owner.");
        committed._states.CopyTo(_states, 0);
        Identity = cache.Identity; Counter = counter; _cache = cache; _sink = sink; _begun = true; _faulted = false;
        StateRefreshes = SourceRefreshes = 0; _stopInitializedThisFrame = false;
        _main = previous.Main; _standing = previous.Standing.Standing; _stop = previous.Standing.Stop;
        _detail = previous.Standing.Detail; _crouching = previous.Crouching.Machine;
    }

    public void CacheEntry(int read)
    {
        if (!_definition.EntryReadIndices.Contains(read)) throw new ArgumentException("Not a Main cache entry.");
        Read(read);
    }

    // Called only when the corresponding Save Initialize actually forwards into its source.
    public void InitializeSavedSource(int cache)
    {
        Require();
        if (cache == _definition.MainCacheIndex) { Reset(AlsMainBoneMachine.Main); _main = default; }
        else if (cache == _definition.Standing.StandingBinding.CacheNodeIndex) { Reset(AlsMainBoneMachine.Standing); _standing = default; }
        else if (cache == _definition.Standing.DetailBinding.CacheNodeIndex) { Reset(AlsMainBoneMachine.Detail); _detail = default; }
        else if (cache == _definition.CrouchingCacheIndex) { Reset(AlsMainBoneMachine.Crouching); _crouching = default; }
        else if (cache != _definition.Standing.CycleCacheIndex && cache != _definition.Crouching.CycleCacheNodeIndex)
            throw new ArgumentException("Unknown Main saved source.");
    }

    public void ObserveInitialization(AlsMainBoneMachine machine, in AlsGroundedMachineUpdate initialization)
    {
        Require();
        var kind = machine switch
        {
            AlsMainBoneMachine.Main => AlsGroundedMachineKind.Main,
            AlsMainBoneMachine.Standing => AlsGroundedMachineKind.Standing,
            AlsMainBoneMachine.Stop => AlsGroundedMachineKind.Stop,
            AlsMainBoneMachine.Crouching => AlsGroundedMachineKind.Crouching,
            _ => throw new ArgumentException("Use the Detail initialization overload.")
        };
        if (!initialization.State.HasInitialized || initialization.State.HasUpdated || initialization.State.Kind != kind ||
            initialization.InitializationCount != 1 || initialization.UpdateCount != 0)
            throw new ArgumentException("Expected an actual state-machine initialization.");
        Reset(machine);
        switch (machine)
        {
            case AlsMainBoneMachine.Main: _main = initialization.State; break;
            case AlsMainBoneMachine.Standing: _standing = initialization.State; break;
            case AlsMainBoneMachine.Stop: _stop = initialization.State; _stopInitializedThisFrame = true; break;
            case AlsMainBoneMachine.Crouching: _crouching = initialization.State; break;
        }
        CacheState(machine, initialization.GetInitialization(0));
    }

    public void ObserveInitialization(in AlsDetailMachineUpdate initialization)
    {
        Require();
        if (!initialization.State.HasInitialized || initialization.State.HasUpdated ||
            initialization.InitializationCount != 1 || initialization.UpdateCount != 0)
            throw new ArgumentException("Expected an actual Detail initialization.");
        Reset(AlsMainBoneMachine.Detail); _detail = initialization.State;
        CacheState(AlsMainBoneMachine.Detail, (int)initialization.GetInitialization(0));
    }

    public void Observe(AlsMainBoneMachine machine, in AlsGroundedMachineUpdate update)
    {
        Require();
        var kind = machine switch
        {
            AlsMainBoneMachine.Main => AlsGroundedMachineKind.Main,
            AlsMainBoneMachine.Standing => AlsGroundedMachineKind.Standing,
            AlsMainBoneMachine.Stop => AlsGroundedMachineKind.Stop,
            AlsMainBoneMachine.Crouching => AlsGroundedMachineKind.Crouching,
            _ => throw new ArgumentException("Use the Detail update overload.")
        };
        if (!update.State.HasUpdated || update.State.LastUpdateSerial != Identity.FrameId || update.State.Kind != kind)
            throw new ArgumentException("Bone-cache update belongs to another machine or frame.");
        var wasUpdated = machine switch
        {
            AlsMainBoneMachine.Main => _main.HasUpdated, AlsMainBoneMachine.Standing => _standing.HasUpdated,
            AlsMainBoneMachine.Stop => _stop.HasUpdated, _ => _crouching.HasUpdated
        };
        // Before a machine's first Update, CacheBones already visits its initialized entry state.
        // Only a later relevance reset clears that first pass; an actual Save Initialize resets explicitly above.
        if (update.Reinitialized && wasUpdated) Reset(machine);
        switch (machine)
        {
            case AlsMainBoneMachine.Main: _main = update.State; break;
            case AlsMainBoneMachine.Standing: _standing = update.State; break;
            case AlsMainBoneMachine.Stop: _stop = update.State; break;
            case AlsMainBoneMachine.Crouching: _crouching = update.State; break;
        }
        for (var i = 0; i < update.InitializationCount; i++)
        {
            var state = update.GetInitialization(i);
            if (machine == AlsMainBoneMachine.Standing && state == 2 && !_stopInitializedThisFrame)
            {
                // The Stop machine lives inside this state, unlike the separately saved Detail source.
                Reset(AlsMainBoneMachine.Stop); _stop = default;
            }
            CacheState(machine, state);
        }
    }

    public void Observe(in AlsDetailMachineUpdate update)
    {
        Require();
        if (!update.State.HasUpdated || update.State.LastUpdateSerial != Identity.FrameId)
            throw new ArgumentException("Detail bone-cache update belongs to another frame.");
        if (update.Reinitialized && _detail.HasUpdated) Reset(AlsMainBoneMachine.Detail);
        _detail = update.State;
        for (var i = 0; i < update.InitializationCount; i++) CacheState(AlsMainBoneMachine.Detail, (int)update.GetInitialization(i));
    }

    public void CacheState(AlsMainBoneMachine machine, int state)
    {
        Require();
        var (start, count) = Range(machine);
        if ((uint)state >= count || machine == AlsMainBoneMachine.Main && _definition.Main.States[state].Conduit ||
            machine == AlsMainBoneMachine.Stop && _definition.Standing.Stop.States[state].Conduit)
            throw new ArgumentException("Invalid bone-cache state or conduit.");
        var index = start + state;
        if (_states[index].MatchesAll(Counter)) return;
        _states[index] = Counter; StateRefreshes++;
        try
        {
            switch (machine)
            {
                case AlsMainBoneMachine.Main:
                    if (state == 1) Read(_definition.Standing.EntryReadIndex);
                    if (state == 2) Read(_definition.CrouchingReadIndex);
                    break;
                case AlsMainBoneMachine.Standing:
                    if (state == 1) Read(_definition.Standing.MovingReadIndex);
                    if (state == 2) CacheMachine(AlsMainBoneMachine.Stop, _stop.Transitions, _stop.HasUpdated, _definition.Standing.Stop.InitialState);
                    break;
                case AlsMainBoneMachine.Stop: Read(_definition.Standing.StopReads[state]); break;
                case AlsMainBoneMachine.Detail: Read(_definition.Standing.DetailReads[state]); break;
                case AlsMainBoneMachine.Crouching:
                    if (state == 1) Read(_definition.Crouching.MovingReadNodeIndex);
                    if (state == 4) Read(_definition.Crouching.StopReadNodeIndex);
                    break;
            }
        }
        catch { _faulted = true; throw; }
    }

    private void Read(int read) { Require(); _cache!.CacheBones(read, Counter, this); }

    public void CacheSourceBones(int cache)
    {
        Require();
        _sink!.RefreshSourceBones(cache); SourceRefreshes++;
        if (cache == _definition.MainCacheIndex) CacheMachine(AlsMainBoneMachine.Main, _main.Transitions, _main.HasUpdated, _definition.Main.InitialState);
        else if (cache == _definition.Standing.StandingBinding.CacheNodeIndex)
            CacheMachine(AlsMainBoneMachine.Standing, _standing.Transitions, _standing.HasUpdated, _definition.Standing.Standing.InitialState);
        else if (cache == _definition.Standing.DetailBinding.CacheNodeIndex)
            CacheMachine(AlsMainBoneMachine.Detail, _detail.Transitions, _detail.HasUpdated, (int)AlsDetailState.Walking);
        else if (cache == _definition.CrouchingCacheIndex)
            CacheMachine(AlsMainBoneMachine.Crouching, _crouching.Transitions, _crouching.HasUpdated, _definition.Crouching.Machine.InitialState);
        else if (cache != _definition.Standing.CycleCacheIndex && cache != _definition.Crouching.CycleCacheNodeIndex)
            throw new ArgumentException("Unknown bone-cache source.");
    }

    private void CacheMachine(AlsMainBoneMachine machine, in AlsTransitionStackState transitions, bool updated, int initial)
    {
        if (!updated) { CacheState(machine, initial); return; }
        var count = Range(machine).Count;
        // Native CacheBones traverses states with strictly positive weight; EvaluateState separately handles zero-alpha paths.
        for (var state = 0; state < count; state++)
            if (AlsTransitionStack.Weight(transitions, state) > 0) CacheState(machine, state);
    }

    private void Reset(AlsMainBoneMachine machine) { var range = Range(machine); Array.Clear(_states, range.Start, range.Count); }
    private static (int Start, int Count) Range(AlsMainBoneMachine machine) => machine switch
    {
        AlsMainBoneMachine.Main => (0, 8), AlsMainBoneMachine.Standing => (8, 5), AlsMainBoneMachine.Stop => (13, 7),
        AlsMainBoneMachine.Detail => (20, 6), AlsMainBoneMachine.Crouching => (26, 5),
        _ => throw new ArgumentException("Unknown bone-cache machine.")
    };
    private void Require()
    {
        if (!_begun || IsFaulted || _cache!.Identity != Identity) throw new InvalidOperationException("Main bone-cache candidate is absent, stale or faulted.");
    }
    public void InitializeSource(int node) => throw new InvalidOperationException("Use the source initialization owner.");
    public void EvaluateSource(int node, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves) =>
        throw new InvalidOperationException("Use the Main pose consumer.");
}
