using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

public interface IAlsContactGeometrySource
{
    // Complete shape-local manifold, normal in shape 1 space. Throw on capacity
    // overflow; never silently truncate. World poses use native cm coordinates.
    int Query(int shape0, in AlsPrecisePose world0, int shape1, in AlsPrecisePose world1, Span<AlsDetectedContact> destination);
}

// Fixed-topology awake contact owner. Every eligible pair shares the island's
// body buffers; geometry queries never own integration. Homogeneous resolved
// material/settings are explicit inputs; material-combine rules are not guessed.
public sealed class AlsWorldContacts : IAlsIslandContacts
{
    private readonly AlsContactRegistry _registry;
    private readonly IAlsContactGeometrySource _source;
    private readonly AlsPersistentContactPair[] _pairs;
    private readonly int[] _prepared, _body0, _body1;
    private readonly AlsPrecisePose[] _shapeWorld;
    private readonly AlsDetectedContact[] _points;
    private readonly AlsContactMaterial _material;
    private readonly AlsContactGatherSettings _settings;
    private int _count, _contactCount;
    private long _epoch;
    private bool _pending, _staged;
    public int LastContactCount { get; private set; }
    public int LastActivePairs { get; private set; }
    public long CompletedSteps => _epoch;
    public AlsWorldContacts(AlsContactRegistry registry, IAlsContactGeometrySource source,
        AlsContactMaterial material, AlsContactGatherSettings settings, int pointsPerPair = 8)
    {
        ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(source);
        if (pointsPerPair <= 0) throw new ArgumentOutOfRangeException(nameof(pointsPerPair));
        _registry = registry; _source = source; _material = material; _settings = settings;
        var capacity = checked(registry.Capacity * (registry.Capacity - 1) / 2);
        _pairs = new AlsPersistentContactPair[capacity]; for (var i = 0; i < capacity; i++) _pairs[i] = new(pointsPerPair);
        _prepared = new int[capacity]; _body0 = new int[capacity]; _body1 = new int[capacity];
        _shapeWorld = new AlsPrecisePose[registry.Capacity]; _points = new AlsDetectedContact[pointsPerPair];
    }
    public void Gather(ReadOnlySpan<AlsPrecisePose> predicted, ReadOnlySpan<AlsProjectionVelocity> velocities,
        ReadOnlySpan<AlsIslandBody> bodies, double dt)
    {
        if (_pending) throw new InvalidOperationException("World contacts already have a pending step.");
        if (!double.IsFinite(dt) || dt <= 0 || !float.IsFinite((float)dt) || !float.IsFinite(1 / (float)dt))
            throw new ArgumentOutOfRangeException(nameof(dt));
        if (predicted.Length != _registry.BodyCount || velocities.Length != predicted.Length || bodies.Length != predicted.Length)
            throw new ArgumentException("Contact registry and island body counts differ.");
        if (_epoch == long.MaxValue) throw new InvalidOperationException("Contact epoch exhausted.");
        _registry.Enter(); _pending = true; _staged = false; _count = _contactCount = 0;
        try
        {
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
                var count = _source.Query(a, _shapeWorld[a], b, _shapeWorld[b], _points);
                if ((uint)count > _points.Length) throw new ArgumentException("Geometry source returned an invalid contact count.");
                _pairs[slot].Gather(new(_registry.Key(a), _registry.Key(b)), _epoch, _points.AsSpan(0, count),
                    new(sa.Quadratic, sb.Quadratic), _material,
                    new(_shapeWorld[a], predicted[ia].Position, (float)bodies[ia].InverseMass.Mass, velocities[ia]), predicted[ia].Rotation, bodies[ia].InverseMass.Inertia,
                    new(_shapeWorld[b], predicted[ib].Position, (float)bodies[ib].InverseMass.Mass, velocities[ib]), predicted[ib].Rotation, bodies[ib].InverseMass.Inertia,
                    _settings with { Dt = (float)dt, PerContactInitialPhi = sa.Quadratic || sb.Quadratic ||
                        (bodies[ia].InverseMass.Mass > 0 && bodies[ib].InverseMass.Mass > 0) });
                _body0[slot] = ia; _body1[slot] = ib; _prepared[_count++] = slot;
                _contactCount += _pairs[slot].SolverCount;
            }
        }
        catch { Abort(); throw; }
    }
    public void SolvePosition(Span<AlsProjectionDelta> bodies, int iteration, int iterationCount)
    {
        Pending();
        for (var i = 0; i < _count; i++) { var slot = _prepared[i]; _pairs[slot].SolvePosition(ref bodies[_body0[slot]], ref bodies[_body1[slot]], iteration >= iterationCount - 4); }
    }
    public void SolveVelocity(Span<AlsProjectionVelocity> bodies, int iteration, int iterationCount, double dt)
    {
        Pending();
        for (var i = 0; i < _count; i++) { var slot = _prepared[i]; _pairs[slot].SolveVelocity(ref bodies[_body0[slot]], ref bodies[_body1[slot]], (float)dt, iteration == iterationCount - 1); }
    }
    public void StageCommit()
    {
        Pending(); _staged = false;
        for (var i = 0; i < _count; i++) _pairs[_prepared[i]].StageCommit();
        _staged = true;
    }
    public void Commit()
    {
        Pending(); if (!_staged) throw new InvalidOperationException("Stage all contacts before publishing.");
        var active = 0;
        for (var i = 0; i < _count; i++) { var pair = _pairs[_prepared[i]]; if (pair.SolverCount > 0) active++; pair.PublishCommit(); }
        LastContactCount = _contactCount; LastActivePairs = active; _epoch++; _pending = _staged = false; _registry.Leave();
    }
    public void Abort()
    {
        if (!_pending) return;
        for (var i = 0; i < _count; i++) _pairs[_prepared[i]].Abort();
        _pending = _staged = false; _registry.Leave();
    }
    public void Reset()
    {
        if (_pending) throw new InvalidOperationException("Abort contact step before resetting.");
        foreach (var pair in _pairs) pair.Reset(); _epoch = 0; LastActivePairs = LastContactCount = 0;
    }
    private void Pending() { if (!_pending) throw new InvalidOperationException("Gather world contacts first."); }
}
