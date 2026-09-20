using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// One manifold's ordering is significant: normal rows for every point precede
// the friction pass. The caller owns body/shape identity and the persistent
// geometric anchors; these solver accumulators are reset at every Gather.
public sealed class AlsCachedContactManifold
{
    private AlsCachedContactPoint[] _points, _scratch;
    public int Count { get; private set; }
    public AlsCachedContactPoint PointAt(int index) => (uint)index < Count ? _points[index] : throw new ArgumentOutOfRangeException(nameof(index));
    public AlsCachedContactManifold(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _points = new AlsCachedContactPoint[capacity]; _scratch = new AlsCachedContactPoint[capacity];
    }
    public void Gather(ReadOnlySpan<AlsContactPointInput> points, in AlsContactMaterial material,
        AlsQuaternion rotation0, AlsJointInverseMass mass0, AlsQuaternion rotation1, AlsJointInverseMass mass1)
    {
        if (points.Length > _points.Length) throw new ArgumentException("Contact manifold capacity exceeded.");
        for (var i = 0; i < points.Length; i++) _scratch[i] = new(points[i], material, rotation0, mass0, rotation1, mass1);
        (_points, _scratch) = (_scratch, _points); Count = points.Length;
    }
    public void SolvePosition(ref AlsProjectionDelta body0, ref AlsProjectionDelta body1, bool friction)
    {
        for (var i = 0; i < Count; i++) _points[i].SolvePositionNormal(ref body0, ref body1);
        if (friction) for (var i = 0; i < Count; i++) _points[i].SolvePositionFriction(ref body0, ref body1);
    }
    public void SolveVelocity(ref AlsProjectionVelocity body0, ref AlsProjectionVelocity body1, float dt, bool friction)
    {
        if (!float.IsFinite(dt) || dt <= 0 || !float.IsFinite(1 / dt)) throw new ArgumentOutOfRangeException(nameof(dt));
        for (var i = 0; i < Count; i++) _points[i].SolveVelocity(ref body0, ref body1, dt, friction);
    }
}
