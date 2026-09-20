using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// Translates Overlay graph callbacks into the enclosing character's source frame.
// No private committed clock or notify scheduler; the Main Movement owner commits
// the combined source frame after the enclosing owner accepts both output poses.
internal sealed class AlsOverlaySharedSourceCollector : IAlsSharedSourceContributor
{
    private enum Phase { Idle, Collecting, Collected, Complete }
    private Phase _phase;
    private readonly AlsOverlaySharedSourceProfile _profile;
    private readonly AlsOverlayClockDefinition _clocks;
    private readonly uint _character, _generation;
    private readonly int[] _initializations = new int[148], _initializeCounts = new int[148];
    private readonly AlsOverlaySourceUpdate[] _updates = new AlsOverlaySourceUpdate[148];
    private readonly bool[] _updated = new bool[148];
    private int _count;
    private float _sweep;
    private AlsFrameIdentity _identity;
    private AlsCycleSyncFrame _sources;
    public AlsOverlaySharedSourceCollector(AlsOverlaySharedSourceProfile profile, AlsOverlayClockDefinition clocks, uint character, uint generation)
    {
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(clocks); ArgumentOutOfRangeException.ThrowIfZero(generation);
        if (profile.ClockBindingDigest != clocks.BindingDigest) throw new ArgumentException("Overlay collector timing binding is stale.");
        _profile = profile; _clocks = clocks; _character = character; _generation = generation;
        foreach (var map in profile.Overlay)
            if (map.Source != clocks.Sources[map.Source].Source ||
                profile.Sources.RuntimeSamples[map.SampleId].AnimationId != clocks.Sequences[clocks.Sources[map.Source].SequenceIndex].AnimationId)
                throw new ArgumentException("Overlay collector and shared source identities differ.");
    }
    public void Begin(in AlsFrameIdentity identity, double aimSweepTime)
    {
        if (_phase != Phase.Idle || identity.CharacterId != _character || identity.SlotGeneration != _generation ||
            !double.IsFinite(aimSweepTime) || !float.IsFinite((float)aimSweepTime)) throw new ArgumentException("Foreign Overlay collection frame.");
        Array.Clear(_initializeCounts); Array.Clear(_initializations); Array.Clear(_updated);
        _count = 0; _identity = identity; _sweep = (float)aimSweepTime; _phase = Phase.Collecting;
    }
    public void Initialize(int source, int initialization)
    {
        RequireSource(source);
        if (initialization <= 0 || _updated[source] || _initializeCounts[source] > 0 && initialization != _initializations[source] + 1)
            throw new ArgumentException("Out-of-order Overlay initialization.");
        _initializations[source] = initialization; _initializeCounts[source]++;
    }
    public void Update(in AlsOverlaySourceUpdate update)
    {
        RequireSource(update.Source);
        if (_updated[update.Source] || update.Initialization <= 0 || !float.IsFinite(update.Weight) || update.Weight < 0)
            throw new ArgumentException("Invalid or repeated Overlay source update.");
        _updated[update.Source] = true; _updates[_count++] = update;
    }
    public void Collect(in AlsFrameIdentity identity, ref AlsCycleSyncFrame candidate,
        Span<AlsLocomotionSourceUpdate> players, Span<AlsLocomotionSampleUpdate> samples, Span<bool> active,
        ref int playerCount, ref int sampleCount)
    {
        if (_phase != Phase.Collecting || identity != _identity || active.Length != players.Length ||
            (uint)playerCount > players.Length || (uint)sampleCount > samples.Length ||
            candidate.Initialized && candidate.BindingStamp != _profile.Sources.RuntimeStamp)
            throw new ArgumentException("Overlay collection does not belong to this shared frame.");
        var next = candidate; var nextPlayer = playerCount; var nextSample = sampleCount;
        for (var source = 0; source < 148; source++)
        {
            if (_initializeCounts[source] == 0) continue;
            var map = _profile.Overlay[source]; var policy = _clocks.Sources[source];
            if (checked(next.Epochs[map.PlayerId] + _initializeCounts[source]) != _initializations[source])
                throw new ArgumentException("Overlay graph and shared frame initialization epochs differ.");
            next.Epochs[map.PlayerId] = _initializations[source];
            if (!policy.Evaluator)
            {
                var sequence = _clocks.Sequences[policy.SequenceIndex];
                next.Times[map.PlayerId] = AlsAssetSourceInitialization.Time(AlsAssetSyncKind.Sequence,
                    policy.StartPosition, sequence.DurationSeconds, policy.PlayRate, assetRateScale: sequence.RateScale);
            }
        }
        // Validate before writing caller-owned update buffers or counts.
        for (var i = 0; i < _count; i++)
        {
            var update = _updates[i]; var map = _profile.Overlay[update.Source];
            if (next.Epochs[map.PlayerId] != update.Initialization) throw new ArgumentException("Overlay update uses a stale source epoch.");
            if (!_clocks.Sources[update.Source].Evaluator) { nextPlayer++; nextSample++; }
        }
        if (nextPlayer > players.Length || nextSample > samples.Length) throw new ArgumentException("Shared source update capacity exceeded.");
        nextPlayer = playerCount; nextSample = sampleCount;
        for (var i = 0; i < _count; i++)
        {
            var update = _updates[i]; var map = _profile.Overlay[update.Source]; var policy = _clocks.Sources[update.Source];
            var sequence = _clocks.Sequences[policy.SequenceIndex];
            if (policy.Evaluator)
            { next.Times[map.PlayerId] = System.Math.Clamp(policy.AimSweep ? _sweep : policy.ExplicitTime, 0, sequence.DurationSeconds); continue; }
            next.Times[map.PlayerId] = System.Math.Clamp(next.Times[map.PlayerId], 0, sequence.DurationSeconds);
            next.CachedWeights[map.PlayerId] = update.Weight;
            players[nextPlayer] = new(map.PlayerId, update.Initialization, next.Times[map.PlayerId], update.Weight, nextSample, 1, update.InertializationSync);
            samples[nextSample++] = new(map.SampleId, 1, 1); active[nextPlayer++] = !update.Inactive;
        }
        candidate = next; playerCount = nextPlayer; sampleCount = nextSample; _phase = Phase.Collected;
    }
    public void Complete(in AlsFrameIdentity identity, in AlsCycleSyncFrame synchronized)
    {
        if (_phase != Phase.Collected || identity != _identity || !synchronized.Initialized || synchronized.BindingStamp != _profile.Sources.RuntimeStamp)
            throw new ArgumentException("Overlay received a foreign or incomplete shared tick.");
        _sources = synchronized; _phase = Phase.Complete;
    }
    public float EvaluationTime(int source)
    {
        if (_phase != Phase.Complete || (uint)source >= 148 || !_updated[source]) throw new InvalidOperationException("Overlay source has not completed the shared tick.");
        return _sources.Times[_profile.Overlay[source].PlayerId];
    }
    public void Discard() { _phase = Phase.Idle; _count = 0; }
    private void RequireSource(int source)
    { if (_phase != Phase.Collecting || (uint)source >= 148) throw new InvalidOperationException("Overlay callback outside source collection."); }
}
