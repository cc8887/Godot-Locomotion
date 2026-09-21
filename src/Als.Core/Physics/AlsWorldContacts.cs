using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

public interface IAlsContactGeometrySource
{
    bool IsInvalidated => false;
    // Per-attempt borrowed context, before any geometry/restoration callbacks.
    // Legacy direct Gather supplies an empty previous span; a velocity-aware
    // provider must reject that absence instead of substituting predicted V.
    void PrepareStep(ReadOnlySpan<AlsIslandBodyState> previous, ReadOnlySpan<AlsProjectionVelocity> velocities,
        ReadOnlySpan<AlsIslandBody> bodies, double dt) { }
    // All fallible work belongs in PrepareStep/Query/StageCommit. PublishCommit
    // and Abort must not throw. Abort also follows a partially failed PrepareStep.
    // Callbacks occur under the registry lock; Reset occurs only while idle.
    void StageCommit() { }
    void PublishCommit() { }
    void Abort() { }
    void Reset() { }
    // Opt-in for polygonal pairs only. The provider supplies the native size-
    // based tolerance and its actual discovery/culling distance in cm.
    bool TryGetManifoldSettings(int shape0, int shape1, out AlsContactManifoldSettings settings)
    { settings = default; return false; }
    // Complete shape-local manifold, normal in shape 1 space. Throw on capacity
    // overflow; never silently truncate. World poses use native cm coordinates.
    int Query(int shape0, in AlsPrecisePose world0, int shape1, in AlsPrecisePose world1, Span<AlsDetectedContact> destination);
}

public readonly record struct AlsPreparedContactPair(int Body0, int Body1, int PointCount, AlsContactMaterial Material);
public readonly record struct AlsContactShockSettings(int PositionIterations, int VelocityIterations, float PositionScale, float VelocityScale)
{
    public static AlsContactShockSettings Native => new(3, 2, .77f, .77f);
    internal void Validate()
    {
        if (PositionIterations < 0 || VelocityIterations < 0 || !float.IsFinite(PositionScale) || !float.IsFinite(VelocityScale) ||
            PositionScale < 0 || PositionScale > 1 || VelocityScale < 0 || VelocityScale > 1) throw new ArgumentException("Invalid contact shock settings.");
    }
}

// Fixed-topology contact owner. A sleeping island holds this owner's epoch and
// histories until it resumes. Every eligible pair shares the island's
// body buffers; geometry queries never own integration. Homogeneous resolved
// material/settings are explicit inputs; material-combine rules are not guessed.
public sealed class AlsWorldContacts : IAlsIslandContacts
{
    private readonly AlsContactRegistry _registry;
    private readonly IAlsContactGeometrySource _source;
    private readonly AlsPersistentContactPair[] _pairs;
    private readonly AlsContactManifoldCache[] _manifolds;
    private readonly int[] _prepared, _body0, _body1;
    private readonly int[] _active;
    private readonly AlsContactPairKey[] _identities;
    private readonly AlsContactConstraintOrder? _order;
    private readonly AlsPrecisePose[] _shapeWorld;
    private readonly AlsDetectedContact[] _points;
    private readonly AlsContactMaterial _material;
    private readonly AlsContactGatherSettings _settings;
    private readonly float[]? _bodyOverlapVelocities;
    public AlsContactShockSettings ShockSettings { get; }
    public bool UsesGraphLevels => _order is not null;
    public int PreparedBodyLevelAt(int body) { Pending(); return _order?.BodyLevelAt(body) ?? 0; }
    private int _count, _contactCount, _activeCount, _restoredCount;
    private long _epoch;
    private long _committedRegistryVersion = -1;
    private bool _pending, _staged;
    public int LastContactCount { get; private set; }
    public int LastActivePairs { get; private set; }
    public int LastRestoredPairs { get; private set; }
    public long CompletedSteps => _epoch;
    public bool RequiresWake => _committedRegistryVersion != _registry.ChangeVersion || _source.IsInvalidated;
    public int PreparedPairCount { get { Pending(); return _order?.Count ?? _activeCount; } }
    public AlsPreparedContactPair PreparedPairAt(int index)
    {
        var slot = PreparedSlot(index); return new(_body0[slot], _body1[slot], _pairs[slot].SolverCount, _material);
    }
    public AlsContactPointInput PreparedPointAt(int pair, int point) => _pairs[PreparedSlot(pair)].SolverInputAt(point);
    private int PreparedSlot(int index)
    {
        if ((uint)index >= PreparedPairCount) throw new ArgumentOutOfRangeException(nameof(index));
        return _order?.ContactSlotAt(index) ?? _active[index];
    }
    public AlsWorldContacts(AlsContactRegistry registry, IAlsContactGeometrySource source,
        AlsContactMaterial material, AlsContactGatherSettings settings, int pointsPerPair = 8, AlsJointIsland? island = null,
        AlsContactShockSettings? shockSettings = null, ReadOnlySpan<float> bodyOverlapVelocities = default)
    {
        ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(source);
        if (pointsPerPair <= 0) throw new ArgumentOutOfRangeException(nameof(pointsPerPair));
        _registry = registry; _source = source; _material = material; _settings = settings;
        if (!bodyOverlapVelocities.IsEmpty)
        {
            if (bodyOverlapVelocities.Length != registry.BodyCount) throw new ArgumentException("Overlap body count differs.");
            foreach (var value in bodyOverlapVelocities) _ = AlsInitialOverlapSettings.Resolve(value, 0);
            _bodyOverlapVelocities = bodyOverlapVelocities.ToArray();
        }
        ShockSettings = shockSettings ?? AlsContactShockSettings.Native; ShockSettings.Validate();
        var capacity = checked(registry.Capacity * (registry.Capacity - 1) / 2);
        _pairs = new AlsPersistentContactPair[capacity]; for (var i = 0; i < capacity; i++) _pairs[i] = new(pointsPerPair);
        _manifolds = new AlsContactManifoldCache[capacity]; for (var i = 0; i < capacity; i++) _manifolds[i] = new(pointsPerPair);
        _prepared = new int[capacity]; _body0 = new int[capacity]; _body1 = new int[capacity];
        _active = new int[capacity]; _identities = new AlsContactPairKey[capacity];
        if (island is not null)
        {
            if (island.BodyCount != registry.BodyCount) throw new ArgumentException("Ordering island and registry body counts differ.");
            _order = new(island, capacity);
        }
        _shapeWorld = new AlsPrecisePose[registry.Capacity]; _points = new AlsDetectedContact[pointsPerPair];
    }
    public void Gather(ReadOnlySpan<AlsPrecisePose> predicted, ReadOnlySpan<AlsProjectionVelocity> velocities,
        ReadOnlySpan<AlsIslandBody> bodies, double dt)
        => Gather(predicted, velocities, bodies, dt, default);
    public void Gather(ReadOnlySpan<AlsPrecisePose> predicted, ReadOnlySpan<AlsProjectionVelocity> velocities,
        ReadOnlySpan<AlsIslandBody> bodies, double dt, ReadOnlySpan<AlsIslandBodyState> previous)
    {
        if (_pending) throw new InvalidOperationException("World contacts already have a pending step.");
        if (!double.IsFinite(dt) || dt <= 0 || !float.IsFinite((float)dt) || !float.IsFinite(1 / (float)dt))
            throw new ArgumentOutOfRangeException(nameof(dt));
        if (predicted.Length != _registry.BodyCount || velocities.Length != predicted.Length || bodies.Length != predicted.Length)
            throw new ArgumentException("Contact registry and island body counts differ.");
        if (!previous.IsEmpty && previous.Length != predicted.Length)
            throw new ArgumentException("Previous body state count differs from the island.");
        if (_epoch == long.MaxValue) throw new InvalidOperationException("Contact epoch exhausted.");
        _registry.Enter(); _pending = true; _staged = false; _count = _contactCount = _activeCount = _restoredCount = 0;
        try
        {
            _source.PrepareStep(previous, velocities, bodies, dt);
            for (var i = 0; i < _registry.Capacity; i++) if (_registry.Present(i))
            {
                var s = _registry.At(i); var local = bodies[s.Body].InverseMass.Mass > 0
                    ? AlsPrecisePose.Relative(s.ActorLocal, bodies[s.Body].MassLocal) : s.ActorLocal;
                _shapeWorld[i] = AlsPrecisePose.Compose(local, predicted[s.Body]);
            }
            var slot = 0;
            for (var a = 0; a < _registry.Capacity; a++) for (var b = a + 1; b < _registry.Capacity; b++, slot++)
            {
                if (!_registry.Allows(a, b)) continue;
                var sa = _registry.At(a); var sb = _registry.At(b); var ia = sa.Body; var ib = sb.Body;
                if (bodies[ia].InverseMass.Mass <= 0 && bodies[ib].InverseMass.Mass <= 0) continue;
                _identities[slot] = new(_registry.Key(a), _registry.Key(b));
                // Include the slot before any provider callback or proposal so
                // exceptions roll back geometry and friction history together.
                _body0[slot] = ia; _body1[slot] = ib; _prepared[_count++] = slot;
                var cache = _manifolds[slot];
                var enabled = _source.TryGetManifoldSettings(a, b, out var manifold) && !sa.Quadratic && !sb.Quadratic;
                if (enabled && (!float.IsFinite(manifold.CollisionTolerance) || manifold.CollisionTolerance < 0 ||
                    !float.IsFinite(manifold.CullDistance) || manifold.CullDistance < 0))
                    throw new ArgumentException("Invalid manifold settings.");
                var restored = enabled && cache.TryRestore(_identities[slot], _epoch, _shapeWorld[a], _shapeWorld[b],
                    manifold.CollisionTolerance, _points, out _);
                int count;
                if (restored)
                {
                    count = cache.Count;
                    // Native midphase activates only within cull distance. An
                    // inactive pair has no reusable manifold on the next tick.
                    if (cache.MinimumPhi > manifold.CullDistance)
                    {
                        cache.Abort(); count = 0;
                        cache.PrepareNew(_identities[slot], _epoch, _shapeWorld[a], _shapeWorld[b], manifold.CollisionTolerance, []);
                    }
                    else _restoredCount++;
                }
                else
                {
                    count = _source.Query(a, _shapeWorld[a], b, _shapeWorld[b], _points);
                    if ((uint)count > _points.Length) throw new ArgumentException("Geometry source returned an invalid contact count.");
                    cache.PrepareNew(_identities[slot], _epoch, _shapeWorld[a], _shapeWorld[b],
                        enabled ? manifold.CollisionTolerance : 0, enabled ? _points.AsSpan(0, count) : []);
                    if(enabled&&cache.MinimumPhi>manifold.CullDistance)
                    {
                        cache.Abort();count=0;
                        cache.PrepareNew(_identities[slot],_epoch,_shapeWorld[a],_shapeWorld[b],manifold.CollisionTolerance,[]);
                    }
                }
                _pairs[slot].Gather(_identities[slot], _epoch, _points.AsSpan(0, count),
                    new(sa.Quadratic, sb.Quadratic), _material,
                    new(_shapeWorld[a], predicted[ia].Position, (float)bodies[ia].InverseMass.Mass, velocities[ia]), predicted[ia].Rotation, bodies[ia].InverseMass.Inertia,
                    new(_shapeWorld[b], predicted[ib].Position, (float)bodies[ib].InverseMass.Mass, velocities[ib]), predicted[ib].Rotation, bodies[ib].InverseMass.Inertia,
                    _settings with { Dt = (float)dt,
                        MaxDepenetrationVelocity = _bodyOverlapVelocities is null ? _settings.MaxDepenetrationVelocity :
                            AlsInitialOverlapSettings.Resolve(_bodyOverlapVelocities[ia], _bodyOverlapVelocities[ib]),
                        PerContactInitialPhi = sa.Quadratic || sb.Quadratic ||
                        (bodies[ia].InverseMass.Mass > 0 && bodies[ib].InverseMass.Mass > 0) });
                _contactCount += _pairs[slot].SolverCount;
                if (_pairs[slot].SolverCount > 0) _active[_activeCount++] = slot;
            }
        }
        catch { Abort(); throw; }
    }
    public void SolvePosition(Span<AlsProjectionDelta> bodies, int iteration, int iterationCount)
    {
        Pending();
        for (var i = 0; i < (_order?.Count ?? _count); i++)
        {
            var slot = _order?.ContactSlotAt(i) ?? _prepared[i];
            SetShock(slot, iteration >= iterationCount - ShockSettings.PositionIterations ? ShockSettings.PositionScale : 1);
            _pairs[slot].SolvePosition(ref bodies[_body0[slot]], ref bodies[_body1[slot]], iteration >= iterationCount - 4);
        }
    }
    public void SolveVelocity(Span<AlsProjectionVelocity> bodies, int iteration, int iterationCount, double dt)
    {
        Pending();
        for (var i = 0; i < (_order?.Count ?? _count); i++)
        {
            var slot = _order?.ContactSlotAt(i) ?? _prepared[i];
            SetShock(slot, iteration >= iterationCount - ShockSettings.VelocityIterations ? ShockSettings.VelocityScale : 1);
            _pairs[slot].SolveVelocity(ref bodies[_body0[slot]], ref bodies[_body1[slot]], (float)dt, iteration == iterationCount - 1);
        }
    }
    private void SetShock(int slot, float scale)
    {
        // Legacy isolated providers have no graph and do not invent levels.
        if (_order is not null) _pairs[slot].SetShockPropagation(_order.BodyLevelAt(_body0[slot]), _order.BodyLevelAt(_body1[slot]), scale);
    }
    public void PrepareConstraintOrder(AlsJointIsland island, Span<int> jointOrder)
    { Pending(); _order?.Prepare(island, _active.AsSpan(0, _activeCount), _body0, _body1, _identities, jointOrder); }
    public void StageCommit()
    {
        Pending(); _staged = false;
        if (_order is not null) _ = _order.Count;
        for (var i = 0; i < _count; i++)
        {
            if (!_manifolds[_prepared[i]].Pending) throw new InvalidOperationException("Missing manifold proposal.");
            _pairs[_prepared[i]].StageCommit();
        }
        _source.StageCommit();
        _staged = true;
    }
    public void Commit()
    {
        Pending(); if (!_staged) throw new InvalidOperationException("Stage all contacts before publishing.");
        _source.PublishCommit();
        var active = 0;
        for (var i = 0; i < _count; i++) { var slot = _prepared[i]; var pair = _pairs[slot]; if (pair.SolverCount > 0) active++; pair.PublishCommit(); _manifolds[slot].Publish(); }
        LastContactCount = _contactCount; LastActivePairs = active; LastRestoredPairs = _restoredCount;
        _epoch++; _committedRegistryVersion = _registry.ChangeVersion;
        _order?.Commit();
        _pending = _staged = false; _registry.Leave();
    }
    public void Abort()
    {
        if (!_pending) return;
        for (var i = 0; i < _count; i++) { _pairs[_prepared[i]].Abort(); _manifolds[_prepared[i]].Abort(); }
        _order?.Abort();
        try { _source.Abort(); }
        finally { _pending = _staged = false; _registry.Leave(); }
    }
    public void Reset()
    {
        if (_pending) throw new InvalidOperationException("Abort contact step before resetting.");
        _source.Reset();
        foreach (var pair in _pairs) pair.Reset(); _epoch = 0; LastActivePairs = LastContactCount = 0; _committedRegistryVersion = -1;
        foreach (var cache in _manifolds) cache.Reset(); LastRestoredPairs = 0;
        _order?.Reset();
    }
    private void Pending() { if (!_pending) throw new InvalidOperationException("Gather world contacts first."); }
}
