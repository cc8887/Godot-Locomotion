using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// Runtime owner for one detected shape pair. History -> geometry Gather -> rows
// -> history commit. The world stages all histories and body states before
// publishing, or aborts on failure; solving alone never advances history.
public sealed class AlsPersistentContactPair
{
    private readonly AlsContactHistory _history;
    private readonly AlsCachedContactManifold _solver;
    private readonly AlsContactGeometry[] _geometry;
    private readonly int[] _sourceIndices;
    private readonly AlsContactHistoryResult[] _results;
    private AlsContactGatherSnapshot _gatherSnapshot;
    public bool Pending => _history.Pending;
    public int SolverCount => Pending ? _solver.Count : 0;
    public int SavedCount => _history.SavedCount;
    public float MinInitialPhi => _history.MinInitialPhi;
    public AlsSavedContact SavedAt(int index) => _history.SavedAt(index);
    public AlsPreparedContact PreparedAt(int index) => _history.PreparedAt(index);
    internal AlsContactPointInput SolverInputAt(int index) { RequirePending(); return _solver.PointAt(index).Input; }
    internal AlsContactGatherSnapshot GatherSnapshot { get { RequirePending(); return _gatherSnapshot; } }
    internal AlsContactGeometry GeometryAt(int index)
    {
        RequirePending();
        if ((uint)index >= _solver.Count) throw new ArgumentOutOfRangeException(nameof(index));
        return _geometry[index];
    }
    internal float GatheredInitialPhiAt(int index) { RequirePending(); return _solver.InitialPhiAt(index); }

    public AlsPersistentContactPair(int capacity)
    {
        _history = new(capacity); _solver = new(capacity); _geometry = new AlsContactGeometry[capacity];
        _sourceIndices = new int[capacity]; _results = new AlsContactHistoryResult[capacity];
    }

    public void Gather(AlsContactPairKey key, long step, ReadOnlySpan<AlsDetectedContact> contacts,
        in AlsContactMatchSettings matching, in AlsContactMaterial material,
        in AlsContactGatherBody body0, AlsQuaternion rotation0, AlsDoubleVector inverseInertia0,
        in AlsContactGatherBody body1, AlsQuaternion rotation1, AlsDoubleVector inverseInertia1,
        in AlsContactGatherSettings settings)
    {
        if (Pending) throw new InvalidOperationException("Commit or abort the previous contact step first.");
        _history.Prepare(key, step, contacts, matching);
        try
        {
            var count = 0;
            for (var i = 0; i < contacts.Length; i++)
            {
                var point = _history.PreparedAt(i);
                _results[i] = new(0, point.Geometry.InitialPhi);
                if (point.Disabled) continue;
                _geometry[count] = point.Geometry; _sourceIndices[count++] = i;
            }
            var effectiveSettings = settings with
                { InitialManifold = _history.PreparedInitialManifold, MinInitialPhi = _history.PreparedMinInitialPhi };
            _solver.GatherGeometry(_geometry.AsSpan(0, count), material, body0, rotation0, inverseInertia0,
                body1, rotation1, inverseInertia1, effectiveSettings);
            _gatherSnapshot = new(body0, body1, effectiveSettings);
        }
        catch { _history.Abort(); throw; }
    }

    public void SolvePosition(ref AlsProjectionDelta body0, ref AlsProjectionDelta body1, bool friction)
    { RequirePending(); _solver.SolvePosition(ref body0, ref body1, friction); }
    public void SetShockPropagation(int level0, int level1, float scale)
    { RequirePending(); _solver.SetShockPropagation(level0, level1, scale); }
    public void SolveVelocity(ref AlsProjectionVelocity body0, ref AlsProjectionVelocity body1, float dt, bool friction)
    { RequirePending(); _solver.SolveVelocity(ref body0, ref body1, dt, friction); }
    public void Commit()
    { StageCommit(); PublishCommit(); }
    public void StageCommit()
    {
        RequirePending();
        for (var i = 0; i < _solver.Count; i++)
            _results[_sourceIndices[i]] = new(_solver.PointAt(i).StaticFrictionRatio, _solver.InitialPhiAt(i));
        _history.StageCommit(_results.AsSpan(0, _history.PreparedCount));
    }
    public void PublishCommit() => _history.PublishCommit();
    public void Abort() => _history.Abort();
    public void Reset() => _history.Reset();
    private void RequirePending()
    { if (!Pending) throw new InvalidOperationException("Gather this contact pair before solving or committing."); }
}
