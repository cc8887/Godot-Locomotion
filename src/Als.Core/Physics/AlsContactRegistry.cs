using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

public readonly record struct AlsContactShapeHandle(int Slot, uint Revision);
public readonly record struct AlsRegisteredContactShape(int Body, AlsPrecisePose ActorLocal,
    uint Layer, uint Mask, bool Quadratic = false, bool Enabled = true, AlsSimulationFilter? SimulationFilter = null);

// Stable slots for one fixed body topology. Shape mutation invalidates handles;
// body reuse explicitly advances its generation. Mutations are forbidden during
// a solve, including from geometry callbacks.
public sealed class AlsContactRegistry
{
    private readonly AlsRegisteredContactShape[] _shapes;
    private readonly bool[] _present, _disabledPairs;
    private readonly uint[] _revisions, _bodyGenerations;
    private readonly ulong[] _particleOrder;
    private ulong _nextParticleOrder;
    private bool _locked;
    public int Capacity => _shapes.Length;
    public int BodyCount => _bodyGenerations.Length;
    public bool IsLocked => _locked;
    public long ChangeVersion { get; private set; }
    public AlsContactRegistry(int bodies, int shapeCapacity)
    {
        if (bodies <= 0 || shapeCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(bodies));
        _shapes = new AlsRegisteredContactShape[shapeCapacity]; _present = new bool[shapeCapacity];
        _revisions = new uint[shapeCapacity]; _bodyGenerations = new uint[bodies]; Array.Fill(_bodyGenerations, 1u);
        _particleOrder = new ulong[bodies];
        for (var i = 0; i < bodies; i++) _particleOrder[i] = (ulong)i;
        _nextParticleOrder = (ulong)bodies;
        _disabledPairs = new bool[checked(bodies * bodies)];
    }
    public bool Present(int slot) => (uint)slot < Capacity && _present[slot];
    // Fixed topology bodies are created in definition order. Reusing a body
    // slot gives the new particle a later order without changing its stable slot.
    public ulong ParticleOrderAt(int body) { Body(body); return _particleOrder[body]; }
    public AlsRegisteredContactShape At(int slot) => Present(slot) ? _shapes[slot] : throw new ArgumentOutOfRangeException(nameof(slot));
    public AlsContactShapeKey Key(int slot)
    { var shape = At(slot); return new((ulong)shape.Body + 1, _bodyGenerations[shape.Body], (uint)slot, _revisions[slot]); }
    public AlsContactShapeHandle Register(AlsRegisteredContactShape shape)
    {
        Mutable(); Validate(shape);
        for (var i = 0; i < Capacity; i++) if (!_present[i])
        { var revision = checked(_revisions[i] + 1); Changed(); _shapes[i] = shape; _revisions[i] = revision; _present[i] = true; return new(i, revision); }
        throw new InvalidOperationException("Contact shape registry is full.");
    }
    public AlsContactShapeHandle Replace(AlsContactShapeHandle handle, AlsRegisteredContactShape shape)
    {
        Mutable(); Check(handle); Validate(shape); var revision = checked(_revisions[handle.Slot] + 1);
        Changed(); _shapes[handle.Slot] = shape; _revisions[handle.Slot] = revision; return new(handle.Slot, revision);
    }
    public void Remove(AlsContactShapeHandle handle) { Mutable(); Check(handle); Changed(); _present[handle.Slot] = false; }
    public void RebindBody(int body)
    {
        Mutable(); Body(body); var generation = checked(_bodyGenerations[body] + 1);
        var nextOrder = checked(_nextParticleOrder + 1);
        Changed(); _bodyGenerations[body] = generation; _particleOrder[body] = _nextParticleOrder; _nextParticleOrder = nextOrder;
    }
    public void DisableBodyPair(int a, int b, bool disabled)
    { Mutable(); Body(a); Body(b); if (_disabledPairs[a * BodyCount + b] == disabled) return; Changed(); _disabledPairs[a * BodyCount + b] = _disabledPairs[b * BodyCount + a] = disabled; }
    public bool Allows(int a, int b)
    {
        if (!Present(a) || !Present(b)) return false;
        var x = _shapes[a]; var y = _shapes[b];
        return x.Body != y.Body && x.Enabled && y.Enabled && !_disabledPairs[x.Body * BodyCount + y.Body] &&
            (x.Layer & y.Mask) != 0 && (y.Layer & x.Mask) != 0 &&
            (x.SimulationFilter ?? AlsSimulationFilter.Unrestricted).Allows(y.SimulationFilter ?? AlsSimulationFilter.Unrestricted);
    }
    internal void Enter() { Mutable(); _locked = true; }
    internal void Leave() => _locked = false;
    private void Mutable() { if (_locked) throw new InvalidOperationException("Contact registry is locked during a physics step."); }
    private void Changed() => ChangeVersion = checked(ChangeVersion + 1);
    private void Body(int body) { if ((uint)body >= BodyCount) throw new ArgumentOutOfRangeException(nameof(body)); }
    private void Check(AlsContactShapeHandle handle)
    { if (!Present(handle.Slot) || _revisions[handle.Slot] != handle.Revision) throw new ArgumentException("Stale contact shape handle."); }
    private void Validate(AlsRegisteredContactShape shape)
    { Body(shape.Body); shape.ActorLocal.Validate(1e-5); if (shape.ActorLocal.Scale != AlsDoubleVector.One) throw new ArgumentException("Contact shape poses must be rigid."); }
}
