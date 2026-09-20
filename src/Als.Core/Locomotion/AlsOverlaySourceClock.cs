using GodotAls.Core.Contracts;
using GodotAls.Core.Sync;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsOverlayClockSource(int Source, int SequenceIndex, bool Evaluator,
    float ExplicitTime, bool AimSweep, float StartPosition, float PlayRate, int GroupId,
    AlsAssetSyncRole Role, ulong MarkerMask);

public sealed class AlsOverlayClockDefinition
{
    private readonly AlsOverlayClockSource[] _sources;
    private readonly AlsAssetSyncSequence[] _sequences;
    private readonly AlsAssetSyncMarker[] _markers;
    private readonly int[] _groups;
    public string BindingDigest { get; }
    public ReadOnlySpan<AlsOverlayClockSource> Sources => _sources;
    public ReadOnlySpan<AlsAssetSyncSequence> Sequences => _sequences;
    public ReadOnlySpan<AlsAssetSyncMarker> Markers => _markers;
    public ReadOnlySpan<int> Groups => _groups;
    public AlsOverlayClockDefinition(string bindingDigest, ReadOnlySpan<AlsOverlayClockSource> sources,
        ReadOnlySpan<AlsAssetSyncSequence> sequences, ReadOnlySpan<AlsAssetSyncMarker> markers, ReadOnlySpan<int> groups)
    {
        if (string.IsNullOrWhiteSpace(bindingDigest) || sources.Length != 148 || sequences.Length == 0 || groups.Length != 3)
            throw new ArgumentException("Incomplete Overlay clock binding.");
        for (var i = 0; i < sources.Length; i++)
        {
            var s = sources[i];
            if (s.Source != i || (uint)s.SequenceIndex >= sequences.Length ||
                !float.IsFinite(s.ExplicitTime) || s.ExplicitTime < 0 || !float.IsFinite(s.StartPosition) ||
                !float.IsFinite(s.PlayRate) || s.Role > AlsAssetSyncRole.AlwaysFollower ||
                (s.Evaluator ? s.GroupId != -1 : !groups.Contains(s.GroupId)))
                throw new ArgumentException("Invalid Overlay source clock policy.");
        }
        for (var i = 0; i < sequences.Length; i++)
        {
            var s = sequences[i];
            if (s.AnimationId < 0 || !float.IsFinite(s.DurationSeconds) || s.DurationSeconds <= 0 ||
                !float.IsFinite(s.RateScale) || s.MarkerStart < 0 || s.MarkerCount < 0 || s.MarkerStart > markers.Length - s.MarkerCount)
                throw new ArgumentException("Invalid Overlay sequence timing.");
        }
        for (var i = 0; i < groups.Length; i++)
            if (groups[i] < 0 || groups[..i].Contains(groups[i])) throw new ArgumentException("Aliased Overlay sync groups.");
        BindingDigest = bindingDigest; _sources = sources.ToArray(); _sequences = sequences.ToArray();
        _markers = markers.ToArray(); _groups = groups.ToArray();
    }
}

// Exclusive per-character clock owner. Graph callbacks collect into a candidate;
// standalone ticking is an Overlay-only integration boundary, not cross-graph sync.
// The enclosing frame must cancel this owner if pose or notify processing fails.
public sealed class AlsOverlaySourceClock
{
    private sealed class Bank
    {
        public readonly float[] Times = new float[148];
        public readonly int[] Initializations = new int[148];
        public readonly AlsAssetPlayerHistory[] Players = new AlsAssetPlayerHistory[148];
        public readonly AlsAssetSampleHistory[] Samples = new AlsAssetSampleHistory[148];
        public readonly AlsAssetSyncBatchGroupHistory[] Groups = new AlsAssetSyncBatchGroupHistory[3];
        public int PlayerCount;
        public bool Initialized;
        public AlsFrameIdentity Identity;
        public long Serial;
        public void CopyFrom(Bank other)
        {
            other.Times.CopyTo(Times, 0); other.Initializations.CopyTo(Initializations, 0);
            other.Players.CopyTo(Players, 0); other.Samples.CopyTo(Samples, 0); other.Groups.CopyTo(Groups, 0);
            PlayerCount = other.PlayerCount; Initialized = other.Initialized; Identity = other.Identity; Serial = other.Serial;
        }
    }
    private readonly AlsOverlayClockDefinition _definition;
    private readonly uint _character, _generation;
    private readonly AlsAssetSyncPlayer[] _ticks = new AlsAssetSyncPlayer[148];
    private readonly AlsAssetSyncSample[] _samples = new AlsAssetSyncSample[148];
    private readonly AlsAssetPlayerTickContext[] _contexts = new AlsAssetPlayerTickContext[148];
    private readonly AlsOverlaySourceUpdate[] _updates = new AlsOverlaySourceUpdate[148];
    private readonly int[] _groups = new int[148];
    private readonly bool[] _updated = new bool[148];
    private Bank _committed = new(), _candidate = new();
    private bool _prepared, _ticked;
    private int _count;
    private float _delta, _sweep;
    public bool HasCandidate => _prepared;
    public ReadOnlySpan<float> Times => _prepared ? _candidate.Times : _committed.Times;
    public ReadOnlySpan<AlsAssetSyncPlayer> CollectedPlayers => _prepared ? _ticks.AsSpan(0, _count) : throw new InvalidOperationException();
    public ReadOnlySpan<AlsAssetSyncSample> CollectedSamples => _prepared ? _samples.AsSpan(0, _count) : throw new InvalidOperationException();
    public ReadOnlySpan<int> CollectedGroups => _prepared ? _groups.AsSpan(0, _count) : throw new InvalidOperationException();
    public ReadOnlySpan<AlsOverlaySourceUpdate> PlayerUpdates => _prepared ? _updates.AsSpan(0, _count) : throw new InvalidOperationException();
    public ReadOnlySpan<AlsAssetPlayerTickContext> TickContexts => _ticked ? _contexts.AsSpan(0, _count) : throw new InvalidOperationException();
    public AlsOverlaySourceClock(AlsOverlayClockDefinition definition, uint character, uint generation)
    { ArgumentNullException.ThrowIfNull(definition); ArgumentOutOfRangeException.ThrowIfZero(generation); _definition = definition; _character = character; _generation = generation; }

    public void Begin(in AlsFrameIdentity identity, long serial, float delta, double aimSweepTime)
    {
        if (_prepared || identity.CharacterId != _character || identity.SlotGeneration != _generation || serial < 0 ||
            !float.IsFinite(delta) || delta < 0 || !double.IsFinite(aimSweepTime) || !float.IsFinite((float)aimSweepTime) ||
            _committed.Initialized && (serial <= _committed.Serial || identity.FrameId <= _committed.Identity.FrameId))
            throw new ArgumentException("Invalid Overlay source frame or owner.");
        _candidate.CopyFrom(_committed); _candidate.Identity = identity; _candidate.Serial = serial;
        _delta = delta; _sweep = (float)aimSweepTime; _count = 0; Array.Clear(_updated);
        _prepared = true; _ticked = false;
    }
    public void Initialize(int source, int initialization)
    {
        RequireCollecting(source);
        if (initialization <= 0 || initialization != checked(_candidate.Initializations[source] + 1) || _updated[source])
            throw new ArgumentException("Overlay source initialization does not match graph history.");
        _candidate.Initializations[source] = initialization;
        var policy = _definition.Sources[source]; var sequence = _definition.Sequences[policy.SequenceIndex];
        // UE evaluator initialization resets marker state, but does not reset its accumulator.
        if (!policy.Evaluator) _candidate.Times[source] = AlsAssetSourceInitialization.Time(AlsAssetSyncKind.Sequence,
            policy.StartPosition, sequence.DurationSeconds, policy.PlayRate, assetRateScale: sequence.RateScale);
    }
    public void Update(in AlsOverlaySourceUpdate update)
    {
        RequireCollecting(update.Source);
        var source = update.Source; var policy = _definition.Sources[source]; var sequence = _definition.Sequences[policy.SequenceIndex];
        if (update.Initialization <= 0 || update.Initialization != _candidate.Initializations[source] || _updated[source] ||
            !float.IsFinite(update.Weight) || update.Weight < 0)
            throw new ArgumentException("Invalid or repeated Overlay source update.");
        _updated[source] = true;
        if (policy.Evaluator)
        {
            // Teleport evaluators have no synchronized advance or notify traversal.
            _candidate.Times[source] = System.Math.Clamp(policy.AimSweep ? _sweep : policy.ExplicitTime, 0, sequence.DurationSeconds);
            return;
        }
        _candidate.Times[source] = System.Math.Clamp(_candidate.Times[source], 0, sequence.DurationSeconds);
        _ticks[_count] = new(source, sequence.AnimationId, update.Initialization, AlsAssetSyncKind.Sequence,
            _candidate.Times[source], policy.PlayRate, update.Weight, _count, 1, policy.MarkerMask,
            RequestedInertialization: update.InertializationSync, Role: policy.Role);
        _samples[_count] = new(source, policy.SequenceIndex, 1); _groups[_count] = policy.GroupId; _updates[_count] = update;
        _count++;
    }
    public void TickStandalone()
    {
        if (!_prepared || _ticked) throw new InvalidOperationException("Overlay sources require one tick per candidate.");
        try
        {
            if (!AlsSyncRuntime.TryEvaluateAssetSyncBatch(_definition.Groups, _groups.AsSpan(0, _count),
                _ticks.AsSpan(0, _count), _samples.AsSpan(0, _count), _definition.Sequences, _definition.Markers,
                _committed.Initialized ? _committed.Groups : [], _committed.Players.AsSpan(0, _committed.PlayerCount),
                _committed.Samples.AsSpan(0, _committed.PlayerCount), _delta, _candidate.Groups, _candidate.Players,
                _candidate.Samples, out var failure, _contexts.AsSpan(0, _count)))
                throw new InvalidOperationException($"Overlay source sync failed: {failure}");
            for (var i = 0; i < _count; i++) _candidate.Times[_candidate.Players[i].PlayerId] = _candidate.Players[i].Time;
            _candidate.PlayerCount = _count; _candidate.Initialized = true; _ticked = true;
        }
        catch { Cancel(); throw; }
    }
    public void Commit()
    {
        if (!_prepared || !_ticked) throw new InvalidOperationException("Overlay clocks require a successful candidate tick.");
        (_committed, _candidate) = (_candidate, _committed); Cancel();
    }
    public void Cancel() { _prepared = _ticked = false; _count = 0; }
    private void RequireCollecting(int source)
    {
        if (!_prepared || _ticked || (uint)source >= 148) throw new InvalidOperationException("Overlay source callback outside collection.");
    }
}
