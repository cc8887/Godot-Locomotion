using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraObservedCycleCandidate(LyraMainObservationCandidate Observation, LyraCycleLayerPoseCandidate Cycle,
    LyraMainUpdateCandidate? Main = null);

// One owner of the Main observation history and its registered Cycle closure.
// The enclosing complete Main still owns graph weights and common Sync.
internal sealed class LyraObservedCycleHost
{
    private readonly LyraMainObservationHost? _observation;
    private readonly LyraMainUpdateHost? _main;
    private readonly LyraCycleLayerPoseHost _cycle;
    private LyraObservedCycleCandidate? _pending;
    public LyraMainObservationState Observation => _main?.State ?? _observation!.State;
    public LyraMainTailState? Tail => _main?.Tail;
    public LyraObservedCycleHost(LyraCycleLayerPoseHost cycle, bool completeMain = false)
    { _cycle = cycle; if (completeMain) _main = new(); else _observation = new(); }
    public LyraObservedCycleCandidate Prepare(in LyraMainObservationInput input, float delta, float weight,
        double hipFireWeight, bool active, bool reinitialize, AlsPrecisePose component, AlsQuaternion relativeRotation)
    {
        if (_pending is not null || _observation is null) throw new InvalidOperationException("Invalid observed Cycle preparation.");
        try
        {
            var observation = _observation.Prepare(input, delta);
            var cycle = _cycle.PrepareObserved(observation, delta, weight, hipFireWeight, active, reinitialize, component, relativeRotation);
            return _pending = new(observation, cycle);
        }
        catch { _observation.Cancel(); _cycle.Cancel(); throw; }
    }
    public LyraObservedCycleCandidate PrepareFull(in LyraMainUpdateInput input, float delta, float weight,
        double hipFireWeight, bool active, bool reinitialize, AlsPrecisePose component, AlsQuaternion relativeRotation)
    {
        if (_pending is not null || _main is null) throw new InvalidOperationException("Invalid complete Main/Cycle preparation.");
        try
        {
            var main = _main.Prepare(input, delta);
            var cycle = _cycle.PrepareBound(main.CycleInput, delta, weight, hipFireWeight, active, reinitialize,
                new(main.State.DirectionAngle, component, relativeRotation, main.Observation.Frame));
            return _pending = new(main.Observation, cycle, main);
        }
        catch { _main.Cancel(); _cycle.Cancel(); throw; }
    }
    public void Evaluate(LyraObservedCycleCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> commonOutputs)
    { Validate(candidate); _cycle.Evaluate(candidate.Cycle, commonOutputs); }
    public void ValidateCommit(LyraObservedCycleCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> commonOutputs)
    {
        Validate(candidate);
        if (_main is not null) _main.ValidateCommit(candidate.Main!); else _observation!.ValidateCommit(candidate.Observation);
        _cycle.ValidateCommit(candidate.Cycle, commonOutputs);
    }
    public void Commit(LyraObservedCycleCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> commonOutputs)
    {
        ValidateCommit(candidate, commonOutputs);
        _cycle.Commit(candidate.Cycle, commonOutputs);
        if (_main is not null) _main.Commit(candidate.Main!); else _observation!.Commit(candidate.Observation);
        _pending = null;
    }
    public void Cancel() { _cycle.Cancel(); _observation?.Cancel(); _main?.Cancel(); _pending = null; }
    private void Validate(LyraObservedCycleCandidate candidate)
    { if (!ReferenceEquals(candidate, _pending)) throw new InvalidOperationException("Stale/repeated observed Cycle candidate."); }
}
