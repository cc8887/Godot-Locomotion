using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// One manifold's ordering is significant: normal rows for every point precede
// the friction pass. The caller owns body/shape identity and the persistent
// geometric anchors; these solver accumulators are reset at every Gather.
public sealed class AlsCachedContactManifold
{
    private AlsCachedContactPoint[] _points, _scratch;
    private float[] _initialPhi, _scratchPhi;
    public int Count { get; private set; }
    public AlsCachedContactPoint PointAt(int index) => (uint)index < Count ? _points[index] : throw new ArgumentOutOfRangeException(nameof(index));
    public float InitialPhiAt(int index) => (uint)index < Count ? _initialPhi[index] : throw new ArgumentOutOfRangeException(nameof(index));
    public AlsCachedContactManifold(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _points = new AlsCachedContactPoint[capacity]; _scratch = new AlsCachedContactPoint[capacity];
        _initialPhi = new float[capacity]; _scratchPhi = new float[capacity];
    }
    public void Gather(ReadOnlySpan<AlsContactPointInput> points, in AlsContactMaterial material,
        AlsQuaternion rotation0, AlsJointInverseMass mass0, AlsQuaternion rotation1, AlsJointInverseMass mass1)
    {
        if (points.Length > _points.Length) throw new ArgumentException("Contact manifold capacity exceeded.");
        for (var i = 0; i < points.Length; i++) { _scratch[i] = new(points[i], material, rotation0, mass0, rotation1, mass1); _scratchPhi[i] = 0; }
        (_points, _scratch) = (_scratch, _points); Count = points.Length;
        (_initialPhi, _scratchPhi) = (_scratchPhi, _initialPhi);
    }
    // This cache is per step; persistent anchors and initial-overlap state belong
    // to the shape-pair owner, who commits them only after the island succeeds.
    public void GatherGeometry(ReadOnlySpan<AlsContactGeometry> geometry, in AlsContactMaterial material,
        in AlsContactGatherBody body0, AlsQuaternion rotation0, AlsDoubleVector inverseInertia0,
        in AlsContactGatherBody body1, AlsQuaternion rotation1, AlsDoubleVector inverseInertia1,
        in AlsContactGatherSettings settings)
    {
        if (geometry.Length > _points.Length) throw new ArgumentException("Contact manifold capacity exceeded.");
        for (var i = 0; i < geometry.Length; i++)
        {
            var gathered = AlsContactGather.Gather(geometry[i], body0, body1, settings);
            _scratch[i] = new(gathered.Point, material, rotation0, new(body0.InverseMass, inverseInertia0),
                rotation1, new(body1.InverseMass, inverseInertia1), fromNativeGather: true);
            _scratchPhi[i] = gathered.InitialPhi;
        }
        (_points, _scratch) = (_scratch, _points); (_initialPhi, _scratchPhi) = (_scratchPhi, _initialPhi);
        Count = geometry.Length;
    }
    public void SolvePosition(ref AlsProjectionDelta body0, ref AlsProjectionDelta body1, bool friction)
    {
        for (var i = 0; i < Count; i++) _points[i].SolvePositionNormal(ref body0, ref body1);
        if (friction) for (var i = 0; i < Count; i++) _points[i].SolvePositionFriction(ref body0, ref body1);
    }
    public void SetShockPropagation(int level0, int level1, float scale)
    {
        if (!float.IsFinite(scale) || scale < 0 || scale > 1) throw new ArgumentOutOfRangeException(nameof(scale));
        for (var i = 0; i < Count; i++) _points[i].SetShockPropagation(level0, level1, scale);
    }
    public void SolveVelocity(ref AlsProjectionVelocity body0, ref AlsProjectionVelocity body1, float dt, bool friction)
    {
        if (!float.IsFinite(dt) || dt <= 0 || !float.IsFinite(1 / dt)) throw new ArgumentOutOfRangeException(nameof(dt));
        for (var i = 0; i < Count; i++) _points[i].SolveVelocity(ref body0, ref body1, dt, friction);
    }
}
