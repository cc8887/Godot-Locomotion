namespace GodotAls.Core.Locomotion;

public readonly record struct AlsFootSupportInfluence(int Bone, AlsDoubleVector LocalPosition, float Weight);
public readonly record struct AlsFootSupportPoint(int FirstInfluence, int InfluenceCount, bool Left);

// Immutable imported mesh data. Positions are native bone-local centimeters;
// runtime skinning uses the current complete component pose, including toes.
public sealed class AlsFootSupportGeometry
{
    private readonly AlsFootSupportInfluence[] _influences;
    private readonly AlsFootSupportPoint[] _points;
    private readonly int _boneCount;
    public int LeftCount { get; }
    public int RightCount { get; }
    public AlsFootSupportGeometry(int boneCount, ReadOnlySpan<AlsFootSupportPoint> points,
        ReadOnlySpan<AlsFootSupportInfluence> influences)
    {
        _boneCount = boneCount; _points = points.ToArray(); _influences = influences.ToArray();
        if (boneCount <= 0 || points.Length == 0) throw new ArgumentException("Empty foot support geometry.");
        foreach (var point in points)
        {
            if (point.FirstInfluence < 0 || point.InfluenceCount is < 1 or > 8 ||
                point.FirstInfluence > influences.Length - point.InfluenceCount)
                throw new ArgumentException("Invalid foot support influence range.");
            double sum = 0;
            foreach (var influence in influences.Slice(point.FirstInfluence, point.InfluenceCount))
            {
                if ((uint)influence.Bone >= boneCount || !influence.LocalPosition.IsFinite ||
                    !float.IsFinite(influence.Weight) || influence.Weight <= 0)
                    throw new ArgumentException("Invalid foot support influence.");
                sum += influence.Weight;
            }
            if (System.Math.Abs(sum - 1) > .001) throw new ArgumentException("Unnormalized foot support point.");
            if (point.Left) LeftCount++; else RightCount++;
        }
        if (LeftCount == 0 || RightCount == 0) throw new ArgumentException("Support geometry needs both feet.");
    }

    public double MinimumDistance(bool left, ReadOnlySpan<AlsPrecisePose> components,
        AlsPrecisePose toWorld, AlsFootTraceRigHit hit)
    {
        if (components.Length != _boneCount || !hit.Blocking || !hit.Impact.IsFinite ||
            !hit.Normal.IsFinite || hit.Normal.LengthSquared < .999 || hit.Normal.LengthSquared > 1.001)
            throw new ArgumentException("Invalid support geometry observation.");
        toWorld.Validate();
        if (toWorld.Scale.X <= 0 || toWorld.Scale.Y <= 0 || toWorld.Scale.Z <= 0)
            throw new ArgumentException("Support geometry requires a positive component scale.");
        var minimum = double.PositiveInfinity;
        foreach (var point in _points)
        {
            if (point.Left != left) continue;
            var position = AlsDoubleVector.Zero;
            foreach (var influence in _influences.AsSpan(point.FirstInfluence, point.InfluenceCount))
            {
                var bone = components[influence.Bone];
                position += ((influence.LocalPosition * bone.Scale).Rotate(bone.Rotation) + bone.Position) * influence.Weight;
            }
            var world = (position * toWorld.Scale).Rotate(toWorld.Rotation) + toWorld.Position;
            var delta = world - hit.Impact;
            var distance = (delta.X * hit.Normal.X + delta.Y * hit.Normal.Y + delta.Z * hit.Normal.Z) /
                System.Math.Sqrt(hit.Normal.LengthSquared);
            if (!double.IsFinite(distance)) throw new ArgumentException("Nonfinite skinned support distance.");
            minimum = System.Math.Min(minimum, distance);
        }
        return minimum;
    }
}
