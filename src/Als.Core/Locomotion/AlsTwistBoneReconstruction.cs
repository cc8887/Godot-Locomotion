namespace GodotAls.Core.Locomotion;

// SOMA has no twist joints. Authoring bakes half axial roll into ALS's eight
// leaf branches. IK adds only the difference from its input pose, retaining
// authored twist (including native animation) and requiring no frame history.
public sealed class AlsTwistBoneReconstruction
{
    public const double Fraction = .5;
    private readonly Segment[] _segments;
    private readonly AlsPrecisePose[] _before, _after;
    private readonly AlsQuaternion[] _candidate;
    private readonly record struct Segment(int Twist, int Driver, AlsDoubleVector Axis,
        AlsDoubleVector LocalAxis, AlsQuaternion ReferenceInverse, double Weight);
    public int BoneCount => _before.Length;
    public int TwistBoneCount => _segments.Length;

    public AlsTwistBoneReconstruction(ReadOnlySpan<string> names, ReadOnlySpan<int> parents,
        ReadOnlySpan<AlsPrecisePose> reference)
    {
        if (names.IsEmpty || parents.Length != names.Length || reference.Length != names.Length)
            throw new ArgumentException("Incomplete twist skeleton reference.");
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var bone = 0; bone < names.Length; bone++)
        {
            if (parents[bone] < -1 || parents[bone] >= bone || !map.TryAdd(names[bone], bone))
                throw new ArgumentException("Twist skeleton requires unique parent-first bones.");
            reference[bone].Validate();
        }
        var segments = new List<Segment>(8);
        foreach (var side in new[] { "l", "r" })
        foreach (var (main, end, proximal) in new[] { ("upperarm", "lowerarm", true), ("lowerarm", "hand", false),
            ("thigh", "calf", true), ("calf", "foot", false) })
        {
            if (!map.TryGetValue(main + "_twist_01_" + side, out var twist)) continue;
            if (!map.TryGetValue(main + "_" + side, out var parent) || !map.TryGetValue(end + "_" + side, out var endpoint) ||
                parents[twist] != parent || parents[endpoint] != parent || parents.Contains(twist))
                throw new ArgumentException("Twist must be a leaf sibling of its segment endpoint.");
            var axis = reference[endpoint].Position.SafeNormal(1e-16);
            if (axis.LengthSquared < .99) throw new ArgumentException("Twist segment has no reference axis.");
            var driver = proximal ? parent : endpoint;
            var driverRest = reference[driver].Rotation.Normalized();
            segments.Add(new(twist, driver, proximal ? axis.Rotate(driverRest) : axis,
                axis, driverRest.Conjugate(), proximal ? -Fraction : Fraction));
        }
        _segments = segments.ToArray(); _candidate = new AlsQuaternion[_segments.Length];
        _before = new AlsPrecisePose[names.Length]; _after = new AlsPrecisePose[names.Length];
    }

    public void Adjust(ReadOnlySpan<AlsPrecisePose> beforeIk, Span<AlsPrecisePose> afterIk)
    {
        if (beforeIk.Length != BoneCount || afterIk.Length != BoneCount || beforeIk.Overlaps(afterIk))
            throw new ArgumentException("Invalid twist correction buffers.");
        // Validate and compute every branch before touching the caller's pose.
        // A rejected/singular candidate cannot partly modify its input.
        for (var i = 0; i < _segments.Length; i++)
        {
            var segment = _segments[i]; beforeIk[segment.Driver].Validate(); afterIk[segment.Driver].Validate();
            afterIk[segment.Twist].Validate();
            var before = Roll(beforeIk[segment.Driver].Rotation * segment.ReferenceInverse, segment.Axis);
            var after = Roll(afterIk[segment.Driver].Rotation * segment.ReferenceInverse, segment.Axis);
            var difference = System.Math.IEEERemainder(after - before, 2 * System.Math.PI);
            _candidate[i] = System.Math.Abs(difference) <= 1e-14 ? afterIk[segment.Twist].Rotation :
                (AlsQuaternion.FromAxisAngle(segment.LocalAxis, difference * segment.Weight) *
                 afterIk[segment.Twist].Rotation).Normalized();
        }
        for (var i = 0; i < _segments.Length; i++)
        {
            var bone = _segments[i].Twist;
            afterIk[bone] = afterIk[bone] with { Rotation = _candidate[i] };
        }
    }

    public void Adjust(ReadOnlySpan<AlsLocalPose> beforeIk, Span<AlsLocalPose> afterIk)
    {
        if (beforeIk.Length != BoneCount || afterIk.Length != BoneCount || beforeIk.Overlaps(afterIk))
            throw new ArgumentException("Invalid twist correction buffers.");
        for (var bone = 0; bone < BoneCount; bone++)
        { _before[bone] = new(beforeIk[bone]); _after[bone] = new(afterIk[bone]); }
        Adjust(_before, _after);
        foreach (var segment in _segments) afterIk[segment.Twist] = _after[segment.Twist].ToSingle();
    }

    private static double Roll(AlsQuaternion delta, AlsDoubleVector axis)
    {
        var projection = delta.X * axis.X + delta.Y * axis.Y + delta.Z * axis.Z;
        if (projection * projection + delta.W * delta.W < 1e-16)
            throw new ArgumentException("Axial twist is undefined for a 180-degree swing.");
        return 2 * System.Math.Atan2(projection, delta.W);
    }
}
