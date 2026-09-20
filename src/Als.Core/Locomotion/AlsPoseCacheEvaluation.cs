using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

/// <summary>UE traversal count and global frame are independent of the character frame identity.</summary>
public readonly record struct AlsGraphTraversalCounter
{
    private readonly bool _valid;
    private readonly short _counter;
    private readonly ulong _frame;
    public short Counter => _valid ? _counter : (short)-1;
    public ulong GlobalFrame => _valid ? _frame : ulong.MaxValue;
    public bool HasUpdated => _valid;

    public AlsGraphTraversalCounter(short counter, ulong globalFrame)
    {
        if (counter == -1 || globalFrame == ulong.MaxValue) throw new ArgumentOutOfRangeException(nameof(counter));
        _counter = counter; _frame = globalFrame; _valid = true;
    }

    public AlsGraphTraversalCounter Next(ulong globalFrame)
    {
        var count = unchecked((short)(Counter + 1));
        if (count == -1) count = 0;
        return new(count, globalFrame);
    }

    public bool MatchesCounter(in AlsGraphTraversalCounter other) => HasUpdated && Counter == other.Counter;
    public bool MatchesAll(in AlsGraphTraversalCounter other) => MatchesCounter(other) && GlobalFrame == other.GlobalFrame;
    // UE WasSynchronizedCounter ignores global frames and accepts the current or
    // immediately preceding traversal, including signed-counter wrap and -1 skip.
    public bool WasSynchronizedCounter(in AlsGraphTraversalCounter other) =>
        other.HasUpdated && (MatchesCounter(other) || Next(other.GlobalFrame).MatchesCounter(other));
}

public readonly record struct AlsPoseCacheScope(AlsFrameIdentity Identity, ulong Serial, int Depth)
{
    internal AlsPoseCacheEvaluation? Owner { get; init; }
}

/// <summary>Only candidate-owned state may change here. Evaluation must not tick sources or dispatch events.
/// This fixed-layout payload contains bones and curves, not UE custom attributes or root motion.</summary>
public interface IAlsPoseCachePoseSink
{
    void InitializeSource(int cacheNodeIndex);
    void CacheSourceBones(int cacheNodeIndex);
    void EvaluateSource(int cacheNodeIndex, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves);
}

public interface IAlsPrecisePoseCachePoseSink : IAlsPoseCachePoseSink
{
    void EvaluatePreciseSource(int cacheNodeIndex, Span<AlsPrecisePose> bones, Span<AlsInertialCurve> curves);
}

/// <summary>Candidate-owned SaveCachedPose lifecycle and scoped payloads. Allocate once per character worker.
/// BeginCandidate copies counters, never pose memory; commit by swapping candidates after all scopes are closed.</summary>
public sealed class AlsPoseCacheEvaluation
{
    private struct Lifecycle
    {
        public AlsGraphTraversalCounter Initialization, Bones, Evaluation;
    }

    private readonly AlsPoseCacheDefinition _definition;
    private readonly int[] _targets;
    private readonly Lifecycle[] _lifecycle;
    private readonly AlsLocalPose[] _bones;
    private readonly AlsPrecisePose[] _preciseBones;
    private readonly bool _precise;
    private readonly AlsInertialCurve[] _curves;
    private readonly bool[] _present;
    private readonly bool[] _evaluating;
    private readonly ulong[] _scopeSerials;
    private readonly int _boneCount, _curveCount;
    private int _depth;
    private ulong _serial;
    private bool _begun, _faulted;
    public AlsFrameIdentity Identity { get; private set; }
    public int SourceEvaluations { get; private set; }
    public bool IsFaulted => _faulted;
    public AlsPoseCacheDefinition Definition => _definition;

    public AlsPoseCacheEvaluation(AlsPoseCacheDefinition definition, int boneCount, int curveCount, int scopeCapacity = 2, bool precise = false)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(boneCount);
        ArgumentOutOfRangeException.ThrowIfNegative(curveCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scopeCapacity);
        _definition = definition; _boneCount = boneCount; _curveCount = curveCount;
        _targets = Enumerable.Repeat(-1, definition.NodeCount).ToArray();
        foreach (var read in definition.Reads) _targets[read.ReadNodeIndex] = definition.UpdateOrder.IndexOf(read.CacheNodeIndex);
        _lifecycle = new Lifecycle[definition.UpdateOrder.Length];
        var slots = checked(_lifecycle.Length * scopeCapacity);
        _precise = precise;
        _bones = precise ? [] : new AlsLocalPose[checked(slots * boneCount)];
        _preciseBones = precise ? new AlsPrecisePose[checked(slots * boneCount)] : [];
        _curves = new AlsInertialCurve[checked(slots * curveCount)];
        _present = new bool[slots]; _evaluating = new bool[slots]; _scopeSerials = new ulong[scopeCapacity];
    }

    public void BeginCandidate(AlsFrameIdentity identity, AlsPoseCacheEvaluation committed)
    {
        ArgumentNullException.ThrowIfNull(committed);
        if (_depth != 0 || committed._depth != 0 || ReferenceEquals(this, committed) || committed._faulted ||
            !ReferenceEquals(_definition, committed._definition) || _boneCount != committed._boneCount ||
            _curveCount != committed._curveCount || _precise != committed._precise || identity.SlotGeneration == 0 ||
            (committed._begun && (identity.CharacterId != committed.Identity.CharacterId ||
                identity.SlotGeneration != committed.Identity.SlotGeneration || identity.FrameId <= committed.Identity.FrameId)))
            throw new InvalidOperationException("Invalid pose-cache candidate, owner or layout.");
        committed._lifecycle.CopyTo(_lifecycle, 0);
        Identity = identity; SourceEvaluations = 0; _begun = true; _faulted = false;
        Array.Clear(_present); Array.Clear(_evaluating);
    }

    public bool Initialize(int readNodeIndex, AlsGraphTraversalCounter counter, IAlsPoseCachePoseSink sink)
    {
        var cache = Resolve(readNodeIndex, counter, sink);
        if (_lifecycle[cache].Initialization.MatchesCounter(counter)) return false;
        // Native lifecycle probe confirms SaveCachedPose's own UpdateCounter is never synchronized here.
        // Do not invent a relevance-gap reset by using FPoseLinkBase's separate debug counter.
        _lifecycle[cache].Initialization = counter;
        try { sink.InitializeSource(_definition.UpdateOrder[cache]); }
        catch { _faulted = true; throw; }
        return true;
    }

    public bool CacheBones(int readNodeIndex, AlsGraphTraversalCounter counter, IAlsPoseCachePoseSink sink)
    {
        var cache = Resolve(readNodeIndex, counter, sink);
        if (_lifecycle[cache].Bones.MatchesAll(counter)) return false;
        _lifecycle[cache].Bones = counter; _lifecycle[cache].Evaluation = default;
        try { sink.CacheSourceBones(_definition.UpdateOrder[cache]); }
        catch { _faulted = true; throw; }
        return true;
    }

    public AlsPoseCacheScope PushScope()
    {
        RequireCandidate();
        if (_depth == _scopeSerials.Length || _serial == ulong.MaxValue)
            throw new InvalidOperationException("Pose-cache scope capacity exceeded.");
        var offset = _depth * _lifecycle.Length;
        Array.Clear(_present, offset, _lifecycle.Length);
        Array.Clear(_evaluating, offset, _lifecycle.Length);
        _scopeSerials[_depth++] = ++_serial;
        return new(Identity, _serial, _depth) { Owner = this };
    }

    public void PopScope(AlsPoseCacheScope scope)
    {
        RequireScope(scope);
        var offset = (_depth - 1) * _lifecycle.Length;
        if (_evaluating.AsSpan(offset, _lifecycle.Length).Contains(true))
            throw new InvalidOperationException("Cannot close a scope during source evaluation.");
        _depth--;
    }

    public void Evaluate(int readNodeIndex, AlsGraphTraversalCounter counter, AlsPoseCacheScope scope,
        IAlsPoseCachePoseSink sink, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves)
    {
        if (_precise) throw new InvalidOperationException("Precise cache requires precise evaluation.");
        var cache = Resolve(readNodeIndex, counter, sink);
        RequireScope(scope);
        if (bones.Length != _boneCount || curves.Length != _curveCount)
            throw new ArgumentException("Pose-cache output layout differs.");
        var slot = (_depth - 1) * _lifecycle.Length + cache;
        if (_evaluating[slot]) { _faulted = true; throw new InvalidOperationException("Recursive pose-cache source."); }
        var cachedBones = _bones.AsSpan(slot * _boneCount, _boneCount);
        var cachedCurves = _curves.AsSpan(slot * _curveCount, _curveCount);
        if (!_lifecycle[cache].Evaluation.MatchesAll(counter) || !_present[slot])
        {
            _lifecycle[cache].Evaluation = counter;
            _present[slot] = false; _evaluating[slot] = true;
            cachedBones.Fill(AlsLocalPose.Identity); cachedCurves.Clear();
            try
            {
                sink.EvaluateSource(_definition.UpdateOrder[cache], cachedBones, cachedCurves);
                RequireScope(scope);
                RequireCandidate();
                _present[slot] = true; SourceEvaluations++;
            }
            catch { _faulted = true; throw; }
            finally { _evaluating[slot] = false; }
        }
        cachedBones.CopyTo(bones); cachedCurves.CopyTo(curves);
    }

    public void EvaluatePrecise(int readNodeIndex, AlsGraphTraversalCounter counter, AlsPoseCacheScope scope,
        IAlsPrecisePoseCachePoseSink sink, Span<AlsPrecisePose> bones, Span<AlsInertialCurve> curves)
    {
        if (!_precise) throw new InvalidOperationException("Single cache cannot supply precise poses.");
        var cache = Resolve(readNodeIndex, counter, sink);
        RequireScope(scope);
        if (bones.Length != _boneCount || curves.Length != _curveCount)
            throw new ArgumentException("Precise pose-cache output layout differs.");
        var slot = (_depth - 1) * _lifecycle.Length + cache;
        if (_evaluating[slot]) { _faulted = true; throw new InvalidOperationException("Recursive pose-cache source."); }
        var cachedBones = _preciseBones.AsSpan(slot * _boneCount, _boneCount);
        var cachedCurves = _curves.AsSpan(slot * _curveCount, _curveCount);
        if (!_lifecycle[cache].Evaluation.MatchesAll(counter) || !_present[slot])
        {
            _lifecycle[cache].Evaluation = counter;
            _present[slot] = false; _evaluating[slot] = true;
            cachedBones.Fill(AlsPrecisePose.Identity); cachedCurves.Clear();
            try
            {
                sink.EvaluatePreciseSource(_definition.UpdateOrder[cache], cachedBones, cachedCurves);
                RequireScope(scope); RequireCandidate();
                _present[slot] = true; SourceEvaluations++;
            }
            catch { _faulted = true; throw; }
            finally { _evaluating[slot] = false; }
        }
        cachedBones.CopyTo(bones); cachedCurves.CopyTo(curves);
    }

    private int Resolve(int read, AlsGraphTraversalCounter counter, IAlsPoseCachePoseSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        RequireCandidate();
        if (!counter.HasUpdated || (uint)read >= _targets.Length || _targets[read] < 0)
            throw new ArgumentException("Invalid pose-cache traversal counter or read binding.");
        return _targets[read];
    }

    private void RequireCandidate()
    {
        if (!_begun || _faulted) throw new InvalidOperationException("Pose-cache candidate is absent or faulted.");
    }

    private void RequireScope(AlsPoseCacheScope scope)
    {
        if (!ReferenceEquals(scope.Owner, this) || _depth == 0 || scope.Identity != Identity ||
            scope.Depth != _depth || scope.Serial != _scopeSerials[_depth - 1])
            throw new InvalidOperationException("Pose-cache scope is stale or not the innermost scope.");
    }
}
