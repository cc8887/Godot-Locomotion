using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

public readonly record struct AlsContactManifoldSettings(float CollisionTolerance, float CullDistance = 0);
public readonly record struct AlsRetainedManifoldState(AlsContactPairKey Key, long Epoch, int Count, float Tolerance,
    Vector3 PositionDelta, AlsQuaternion RotationDelta);
public readonly record struct AlsRetainedManifoldPoint(AlsDetectedContact Contact, Vector3 Initial0, Vector3 Initial1);

// Native persistent polygonal manifold restoration, separate from saved friction
// anchors. The world owner handles eligibility and culling. Geometry/history
// changes publish with the whole island; a failed step never advances this cache.
public sealed class AlsContactManifoldCache
{
    private AlsDetectedContact[] _points, _next;
    private readonly Vector3[] _initial0, _initial1;
    private AlsContactPairKey _key, _nextKey;
    private long _step = -1, _nextStep;
    private Vector3 _positionDelta, _nextPositionDelta;
    private AlsQuaternion _rotationDelta, _nextRotationDelta;
    private float _tolerance, _nextTolerance;
    private int _count, _nextCount;
    private bool _newManifold;
    public bool Pending { get; private set; }
    public float MinimumPhi { get; private set; }
    public int Count => _count;
    // Value copies of committed state remain available while a new step is
    // pending. Initial points/reference deltas belong to the last narrow phase.
    public AlsRetainedManifoldState RetainedState => new(_key, _step, _count, _tolerance, _positionDelta, _rotationDelta);
    public AlsRetainedManifoldPoint RetainedPointAt(int point) => (uint)point < (uint)_count
        ? new(_points[point], _initial0[point], _initial1[point]) : throw new ArgumentOutOfRangeException(nameof(point));
    public bool PreparedRestored => Pending ? !_newManifold : throw new InvalidOperationException("No prepared manifold.");
    public float PreparedTolerance => Pending ? _nextTolerance : throw new InvalidOperationException("No prepared manifold.");
    public AlsContactManifoldCache(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _points = new AlsDetectedContact[capacity]; _next = new AlsDetectedContact[capacity];
        _initial0 = new Vector3[capacity]; _initial1 = new Vector3[capacity];
    }

    public bool TryRestore(AlsContactPairKey key, long step, in AlsPrecisePose shape0, in AlsPrecisePose shape1,
        float tolerance, Span<AlsDetectedContact> destination, out int count)
    {
        Validate(step, shape0, shape1, tolerance); count = 0;
        // Native midphase resets an unused or empty manifold before restoration.
        if (_count == 0 || key != _key || step - _step != 1 || tolerance != _tolerance || tolerance == 0) return false;
        if (destination.Length < _count) throw new ArgumentException("Manifold destination capacity exceeded.");
        var translation = shape0.Position - shape1.Position;
        var rotation = shape0.Rotation.Conjugate() * shape1.Rotation;
        if (!(translation - new AlsDoubleVector(_positionDelta)).NearlyZero(.2 * tolerance) ||
            AlsQuaternion.Dot(rotation, _rotationDelta) <= .9999) return false;
        var relative = AlsPrecisePose.Relative(shape0, shape1);
        var positionTolerance = .8 * tolerance; var threshold = positionTolerance * positionTolerance;
        var active = _count; var minimum = float.MaxValue;
        for (var i = 0; i < _count; i++)
        {
            var point = _points[i]; _next[i] = point;
            if (point.Disabled) { active--; continue; }
            var p0 = new AlsDoubleVector(_initial0[i]).Rotate(relative.Rotation) + relative.Position;
            var p1 = new AlsDoubleVector(_initial1[i]); var normal = new AlsDoubleVector(point.Normal1);
            var delta = p0 - p1; var phi = AlsDoubleVector.Dot(delta, normal);
            var lateral = delta - normal * phi;
            if (lateral.LengthSquared < threshold)
            {
                _next[i] = point with { Point1 = (p0 - normal * phi).ToSingle(), NativePhi=(float)phi };
                minimum = MathF.Min(minimum, (float)phi);
            }
            else { _next[i] = point with { Disabled = true }; active--; }
        }
        if (active < System.Math.Min(_count, 4)) return false;
        _nextKey = key; _nextStep = step; _nextCount = _count; _nextTolerance = tolerance;
        _newManifold = false; Pending = true; MinimumPhi = minimum; count = _count;
        _next.AsSpan(0, count).CopyTo(destination); return true;
    }

    public void PrepareNew(AlsContactPairKey key, long step, in AlsPrecisePose shape0, in AlsPrecisePose shape1,
        float tolerance, ReadOnlySpan<AlsDetectedContact> points)
    {
        Validate(step, shape0, shape1, tolerance);
        if (points.Length > _next.Length) throw new ArgumentException("Manifold capacity exceeded.");
        foreach (var point in points)
            if (!new AlsDoubleVector(point.Point0).IsFinite || !new AlsDoubleVector(point.Point1).IsFinite ||
                !new AlsDoubleVector(point.Normal1).IsFinite || MathF.Abs(point.Normal1.LengthSquared() - 1) > 1e-5f ||
                (point.NativePhi.HasValue&&!float.IsFinite(point.NativePhi.Value)))
                throw new ArgumentException("Invalid manifold point.");
        _nextPositionDelta = (shape0.Position - shape1.Position).ToSingle();
        if (!new AlsDoubleVector(_nextPositionDelta).IsFinite) throw new ArgumentException("Relative translation exceeds native float storage.");
        _nextRotationDelta = new((shape0.Rotation.Conjugate() * shape1.Rotation).ToSingle());
        points.CopyTo(_next); _nextKey = key; _nextStep = step; _nextCount = points.Length; _nextTolerance = tolerance;
        _newManifold = true; Pending = true; MinimumPhi = float.MaxValue;
        var relative=AlsPrecisePose.Relative(shape0,shape1);
        foreach(var point in points)if(!point.Disabled)
        {
            var phi=point.NativePhi??(float)AlsDoubleVector.Dot(
                new AlsDoubleVector(point.Point0).Rotate(relative.Rotation)+relative.Position-new AlsDoubleVector(point.Point1),
                new(point.Normal1));
            MinimumPhi=MathF.Min(MinimumPhi,phi);
        }
    }
    // All validation occurs during Prepare. The owner stages every participant
    // before reaching this no-callback publication path.
    public void Publish()
    {
        if (!Pending) throw new InvalidOperationException("Prepare manifold before publishing.");
        if (_newManifold)
        {
            for (var i = 0; i < _nextCount; i++) { _initial0[i] = _next[i].Point0; _initial1[i] = _next[i].Point1; }
            _positionDelta = _nextPositionDelta; _rotationDelta = _nextRotationDelta;
        }
        (_points, _next) = (_next, _points); _count = _nextCount; _key = _nextKey; _step = _nextStep; _tolerance = _nextTolerance;
        Abort();
    }
    public void Abort() { Pending = false; MinimumPhi = float.MaxValue; }
    public void Reset()
    {
        if (Pending) throw new InvalidOperationException("Abort before resetting a manifold.");
        _count = 0; _step = -1; _key = default; _tolerance = 0;
    }
    private void Validate(long step, in AlsPrecisePose a, in AlsPrecisePose b, float tolerance)
    {
        if (Pending) throw new InvalidOperationException("Publish or abort the previous manifold first.");
        if (step < 0 || step <= _step) throw new ArgumentOutOfRangeException(nameof(step));
        a.Validate(1e-5); b.Validate(1e-5);
        if (a.Scale != AlsDoubleVector.One || b.Scale != AlsDoubleVector.One || !float.IsFinite(tolerance) || tolerance < 0)
            throw new ArgumentException("Restoration needs rigid poses and a finite nonnegative collision tolerance.");
    }
}
