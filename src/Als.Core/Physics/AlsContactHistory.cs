using System.Numerics;

namespace GodotAls.Core.Physics;

// Supplied by the world owner. Generation and revision must change on body-slot
// reuse and shape replacement respectively. Endpoint order is significant.
public readonly record struct AlsContactShapeKey(ulong Body, uint Generation, uint Shape, uint Revision);
public readonly record struct AlsContactPairKey(AlsContactShapeKey Shape0, AlsContactShapeKey Shape1);
public readonly record struct AlsDetectedContact(Vector3 Point0, Vector3 Point1, Vector3 Normal1,
    bool Disabled = false, float TargetPhi = 0,
    bool DisablePosition = false, bool DisableVelocity = false, bool DisableFriction = false)
{
    // Native narrow-phase phi is rounded independently from stored points.
    // Preserve it for activation at exact cull boundaries.
    public float? NativePhi { get; init; }
}
public readonly record struct AlsContactMatchSettings(bool Quadratic0, bool Quadratic1,
    bool SimpleAssignment = true, bool RestoreFriction = true, float ExactTolerance = .2f, float NearTolerance = 1);
public readonly record struct AlsPreparedContact(AlsContactGeometry Geometry, bool Disabled, int SavedIndex);
public readonly record struct AlsSavedContact(Vector3 Anchor0, Vector3 Anchor1, float InitialPhi);
public readonly record struct AlsContactHistoryResult(float FrictionRatio, float InitialPhi);

// One ordered shape pair, with explicit Prepare / Commit / Abort ownership.
// Native defaults permit multiple new points to reuse the same saved anchor.
// This class does not allocate body IDs, detect contacts or implement sleeping.
public sealed class AlsContactHistory
{
    private AlsSavedContact[] _saved, _scratch;
    private readonly AlsPreparedContact[] _prepared;
    private readonly int[] _allowed;
    private AlsContactPairKey _key, _pendingKey;
    private long _step = -1, _pendingStep;
    private int _lastPointCount;
    private int _stagedCount;
    private float _stagedMinimum;
    private bool _staged;
    public int SavedCount { get; private set; }
    public float MinInitialPhi { get; private set; }
    public bool Pending { get; private set; }
    public int PreparedCount { get; private set; }
    public bool PreparedInitialManifold { get; private set; }
    public float PreparedMinInitialPhi { get; private set; }
    public AlsSavedContact SavedAt(int index) => (uint)index < SavedCount ? _saved[index] : throw new ArgumentOutOfRangeException(nameof(index));
    public AlsPreparedContact PreparedAt(int index) => Pending && (uint)index < PreparedCount ? _prepared[index] : throw new ArgumentOutOfRangeException(nameof(index));

    public AlsContactHistory(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _saved = new AlsSavedContact[capacity]; _scratch = new AlsSavedContact[capacity];
        _prepared = new AlsPreparedContact[capacity]; _allowed = new int[capacity];
    }

    public void Prepare(AlsContactPairKey key, long step, ReadOnlySpan<AlsDetectedContact> contacts, in AlsContactMatchSettings settings)
    {
        if (Pending) throw new InvalidOperationException("Finish or abort the pending contact step first.");
        if (step < 0 || step <= _step) throw new ArgumentOutOfRangeException(nameof(step));
        if (contacts.Length > _prepared.Length) throw new ArgumentException("Contact history capacity exceeded.");
        if (key.Shape0.Body == key.Shape1.Body && key.Shape0.Generation == key.Shape1.Generation)
            throw new ArgumentException("A shape pair needs distinct bodies.");
        if (!float.IsFinite(settings.ExactTolerance) || settings.ExactTolerance < 0 || !float.IsFinite(settings.NearTolerance) ||
            settings.NearTolerance < settings.ExactTolerance || !float.IsFinite(settings.NearTolerance * settings.NearTolerance))
            throw new ArgumentException("Invalid contact matching tolerances.");
        foreach (var contact in contacts)
            if (!Finite(contact.Point0) || !Finite(contact.Point1) || !Finite(contact.Normal1) ||
                MathF.Abs(contact.Normal1.LengthSquared() - 1) > 1e-5f || !float.IsFinite(contact.TargetPhi))
                throw new ArgumentException("Invalid detected contact.");
        var continuing = _step >= 0 && step - _step == 1 && key == _key && _lastPointCount > 0;
        var savedCount = continuing ? SavedCount : 0; var remaining = savedCount;
        for (var i = 0; i < remaining; i++) _allowed[i] = i;
        for (var i = 0; i < contacts.Length; i++)
        {
            var c = contacts[i]; var selected = -1; var allowedIndex = -1;
            if (!c.Disabled && settings.RestoreFriction)
            {
                var best = settings.NearTolerance * settings.NearTolerance;
                var exact = settings.ExactTolerance * settings.ExactTolerance;
                var count = settings.SimpleAssignment ? savedCount : remaining;
                for (var j = 0; j < count; j++)
                {
                    var index = settings.SimpleAssignment ? (j + i) % savedCount : _allowed[j];
                    var old = _saved[index];
                    var d0 = (c.Point0 - old.Anchor0).LengthSquared(); var d1 = (c.Point1 - old.Anchor1).LengthSquared();
                    var distance = settings.Quadratic0 && !settings.Quadratic1 ? d0 : settings.Quadratic1 && !settings.Quadratic0 ? d1 : MathF.Min(d0, d1);
                    if (distance < exact) { selected = index; allowedIndex = j; break; }
                    if (distance < best) { best = distance; selected = index; allowedIndex = j; }
                }
                if (!settings.SimpleAssignment && allowedIndex >= 0) _allowed[allowedIndex] = _allowed[--remaining];
            }
            var previous = selected >= 0 ? _saved[selected] : new AlsSavedContact(c.Point0, c.Point1, 0);
            _prepared[i] = new(new(c.Point0, c.Point1, c.Normal1, previous.Anchor0, previous.Anchor1,
                selected >= 0, selected < 0, previous.InitialPhi, c.TargetPhi, c.DisablePosition, c.DisableVelocity, c.DisableFriction), c.Disabled, selected);
        }
        _pendingKey = key; _pendingStep = step; PreparedCount = contacts.Length;
        PreparedInitialManifold = !continuing; PreparedMinInitialPhi = continuing ? MinInitialPhi : 0; Pending = true;
    }

    public void Commit(ReadOnlySpan<AlsContactHistoryResult> results)
    { StageCommit(results); PublishCommit(); }

    public void StageCommit(ReadOnlySpan<AlsContactHistoryResult> results)
    {
        if (!Pending) throw new InvalidOperationException("Prepare contact history before committing.");
        _staged = false;
        if (results.Length != PreparedCount) throw new ArgumentException("One result per detected point is required, including disabled points.");
        var count = 0; var minimum = 0f;
        for (var i = 0; i < results.Length; i++)
        {
            var prepared = _prepared[i]; var geometry = prepared.Geometry;
            // Native Scatter also records disabled points, using zero friction.
            var ratio = prepared.Disabled ? 0 : results[i].FrictionRatio;
            var phi = prepared.Disabled ? geometry.InitialPhi : results[i].InitialPhi;
            if (!float.IsFinite(ratio) || ratio < 0 || !float.IsFinite(phi)) throw new ArgumentException("Invalid contact history result.");
            minimum = MathF.Min(minimum, phi);
            if (ratio >= 1 - 1e-4f) _scratch[count++] = new(geometry.Anchor0, geometry.Anchor1, phi);
            else if (ratio < 1e-4f)
            {
                if (PreparedCount < 8) _scratch[count++] = new(geometry.Point0, geometry.Point1, phi);
            }
            else
            {
                // Native TVector::Lerp uses A + alpha * (B - A).
                var anchor0 = geometry.Point0 + ratio * (geometry.Anchor0 - geometry.Point0);
                var anchor1 = geometry.Point1 + ratio * (geometry.Anchor1 - geometry.Point1);
                if (!Finite(anchor0) || !Finite(anchor1)) throw new ArgumentException("Friction anchor overflow.");
                _scratch[count++] = new(anchor0, anchor1, phi);
            }
        }
        _stagedCount = count; _stagedMinimum = minimum; _staged = true;
    }

    // After every participant stages successfully, publication performs only
    // prevalidated swaps and scalar assignments; no callbacks or allocations.
    public void PublishCommit()
    {
        if (!_staged || !Pending) throw new InvalidOperationException("Stage contact results before publishing.");
        (_saved, _scratch) = (_scratch, _saved); SavedCount = _stagedCount; MinInitialPhi = _stagedMinimum;
        _key = _pendingKey; _step = _pendingStep; _lastPointCount = PreparedCount; ClearPending();
    }

    public void Abort() => ClearPending();
    public void Reset()
    {
        if (Pending) throw new InvalidOperationException("Abort before resetting contact history.");
        SavedCount = 0; MinInitialPhi = 0; _step = -1; _key = default; _lastPointCount = 0;
    }
    private void ClearPending() { Pending = false; _staged = false; PreparedCount = 0; PreparedMinInitialPhi = 0; PreparedInitialManifold = false; }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
