using M = System.Math;

namespace GodotAls.Core.Locomotion;

public sealed record AlsFootTraceRigDefinition(float Upward, float Downward, float FootHeight, float WalkableAngle);
public readonly record struct AlsFootTraceRigHit(bool Blocking, AlsDoubleVector Impact, AlsDoubleVector Normal)
{
    public ulong ColliderIdentity { get; init; }
}
public readonly record struct AlsFootTraceRigResult(bool Walkable, float OffsetZ, AlsDoubleVector OffsetNormal);

// FAlsRigUnit_FootOffsetTrace split into a main-thread ray and a pure consumer
// of the matching physics observation. Both use native axes and centimeters.
public static class AlsFootTraceRigModel
{
    public static AlsFootTraceSegment Trace(AlsFootTraceRigDefinition definition, AlsDoubleVector target, AlsPrecisePose toWorld)
    {
        Validate(definition, toWorld);
        if (!target.IsFinite) throw new ArgumentException("Nonfinite foot trace target.");
        return new(ToWorld(new(target.X, target.Y, definition.Upward), toWorld),
            ToWorld(new(target.X, target.Y, -definition.Downward), toWorld));
    }

    public static AlsFootTraceRigResult Evaluate(AlsFootTraceRigDefinition definition, bool enabled,
        in AlsFootTraceRigHit hit, AlsPrecisePose toWorld)
    {
        Validate(definition, toWorld);
        if (!hit.Impact.IsFinite || !hit.Normal.IsFinite) throw new ArgumentException("Nonfinite foot trace hit.");
        var empty = new AlsFootTraceRigResult(false, 0, new(0, 0, 1));
        if (!enabled) return empty;
        // InverseTransformVector includes inverse scale; native does NOT
        // normalize the resulting VM-space normal before walkability/height.
        var normal = InverseVector(hit.Normal, toWorld);
        var minimumZ = MathF.Cos(definition.WalkableAngle * (3.14159265359f / 180f));
        if (!hit.Blocking || normal.Z < minimumZ) return empty;
        var location = InverseVector(hit.Impact - toWorld.Position, toWorld);
        var slopeCosine = (float)normal.Z;
        var slopeOffset = slopeCosine > 1e-8f ? definition.FootHeight / slopeCosine - definition.FootHeight : 0;
        return new(true, (float)(location.Z + slopeOffset), normal);
    }

    private static AlsDoubleVector ToWorld(AlsDoubleVector point, AlsPrecisePose transform) =>
        (point * transform.Scale).Rotate(transform.Rotation) + transform.Position;
    private static AlsDoubleVector InverseVector(AlsDoubleVector vector, AlsPrecisePose transform) =>
        vector.Rotate(transform.Rotation.Conjugate()) *
        new AlsDoubleVector(Reciprocal(transform.Scale.X), Reciprocal(transform.Scale.Y), Reciprocal(transform.Scale.Z));
    private static double Reciprocal(double value) => M.Abs(value) <= 1e-8f ? 0 : 1 / value;
    private static void Validate(AlsFootTraceRigDefinition definition, AlsPrecisePose transform)
    {
        ArgumentNullException.ThrowIfNull(definition); transform.Validate();
        if (!float.IsFinite(definition.Upward) || definition.Upward < 0 || !float.IsFinite(definition.Downward) || definition.Downward < 0 ||
            !float.IsFinite(definition.FootHeight) || definition.FootHeight < 0 || !float.IsFinite(definition.WalkableAngle) || definition.WalkableAngle is < 0 or > 90)
            throw new ArgumentException("Invalid rig trace definition.");
    }
}
