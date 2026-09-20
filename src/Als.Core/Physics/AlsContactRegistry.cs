using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

public readonly record struct AlsContactShapeHandle(int Slot, uint Revision);
public readonly record struct AlsRegisteredContactShape(int Body, AlsPrecisePose ActorLocal,
    uint Layer, uint Mask, bool Quadratic = false, bool Enabled = true);

// Stable slots for one fixed body topology. Shape mutation invalidates handles;
// body reuse explicitly advances its generation. Mutations are forbidden during
// a solve, including from geometry callbacks.
public sealed class AlsContactRegistry
{
    private readonly AlsRegisteredContactShape[] _shapes;
    private readonly bool[] _present, _disabledPairs;
    private readonly uint[] _revisions, _bodyGenerations;
    private bool _locked;
    public int Capacity => _shapes.Length;
    public int BodyCount => _bodyGenerations.Length;
    public bool IsLocked => _locked;
    public AlsContactRegistry(int bodies, int shapeCapacity)
    {
        if (bodies <= 0 || shapeCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(bodies));
        _shapes = new AlsRegisteredContactShape[shapeCapacity]; _present = new bool[shapeCapacity];
        _revisions = new uint[shapeCapacity]; _bodyGenerations = new uint[bodies]; Array.Fill(_bodyGenerations, 1u);
        _disabledPairs = new bool[checked(bodies * bodies)];
    }
    public bool Present(int slot) => (uint)slot < Capacity && _present[slot];
    public AlsRegisteredContactShape At(int slot) => Present(slot) ? _shapes[slot] : throw new ArgumentOutOfRangeException(nameof(slot));
    public AlsContactShapeKey Key(int slot)
    { var shape = At(slot); return new((ulong)shape.Body + 1, _bodyGenerations[shape.Body], (uint)slot, _revisions[slot]); }
    public AlsContactShapeHandle Register(AlsRegisteredContactShape shape)
    {
        Mutable(); Validate(shape);
        for (var i = 0; i < Capacity; i++) if (!_present[i])
        { var revision = checked(_revisions[i] + 1); _shapes[i] = shape; _revisions[i] = revision; _present[i] = true; return new(i, revision); }
        throw new InvalidOperationException("Contact shape registry is full.");
    }
    public AlsContactShapeHandle Replace(AlsContactShapeHandle handle, AlsRegisteredContactShape shape)
    {
        Mutable(); Check(handle); Validate(shape); var revision = checked(_revisions[handle.Slot] + 1);
        _shapes[handle.Slot] = shape; _revisions[handle.Slot] = revision; return new(handle.Slot, revision);
    }
    public void Remove(AlsContactShapeHandle handle) { Mutable(); Check(handle); _present[handle.Slot] = false; }
    public void RebindBody(int body)
    { Mutable(); Body(body); _bodyGenerations[body] = checked(_bodyGenerations[body] + 1); }
    public void DisableBodyPair(int a, int b, bool disabled)
    { Mutable(); Body(a); Body(b); _disabledPairs[a * BodyCount + b] = _disabledPairs[b * BodyCount + a] = disabled; }
    public bool Allows(int a, int b)
    {
        if (!Present(a) || !Present(b)) return false;
        var x = _shapes[a]; var y = _shapes[b];
        return x.Body != y.Body && x.Enabled && y.Enabled && !_disabledPairs[x.Body * BodyCount + y.Body] &&
            (x.Layer & y.Mask) != 0 && (y.Layer & x.Mask) != 0;
    }
    internal void Enter() { Mutable(); _locked = true; }
    internal void Leave() => _locked = false;
    private void Mutable() { if (_locked) throw new InvalidOperationException("Contact registry is locked during a physics step."); }
    private void Body(int body) { if ((uint)body >= BodyCount) throw new ArgumentOutOfRangeException(nameof(body)); }
    private void Check(AlsContactShapeHandle handle)
    { if (!Present(handle.Slot) || _revisions[handle.Slot] != handle.Revision) throw new ArgumentException("Stale contact shape handle."); }
    private void Validate(AlsRegisteredContactShape shape)
    { Body(shape.Body); shape.ActorLocal.Validate(1e-5); if (shape.ActorLocal.Scale != AlsDoubleVector.One) throw new ArgumentException("Contact shape poses must be rigid."); }
}
