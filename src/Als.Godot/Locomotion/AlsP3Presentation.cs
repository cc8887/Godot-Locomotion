using Godot;
using GodotAls.Import.Compilation;
using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using NumericsVector3 = System.Numerics.Vector3;

namespace GodotAls.Locomotion;

internal static class AlsP3Presentation
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    public static Transform3D Create(in AlsPresentationDefinition definition)
    {
        var translation = definition.TranslationMeters;
        var transform = new Transform3D(
            new Basis(Vector3.Up, definition.YawRadians),
            new Vector3(translation.X, translation.Y, translation.Z));
        ThrowIfNonFinite(transform);
        return transform;
    }

    public static Transform3D ToGodot(in NumericsMatrix4x4 logical) => new(
        new Basis(
            new Vector3(logical.M11, logical.M12, logical.M13),
            new Vector3(logical.M21, logical.M22, logical.M23),
            new Vector3(logical.M31, logical.M32, logical.M33)),
        new Vector3(logical.M41, logical.M42, logical.M43));

    public static Transform3D Compose(
        in NumericsMatrix4x4 logical,
        in Transform3D presentation) =>
        ToGodot(logical) * presentation;

    public static Transform3D Compose(
        in Transform3D logical,
        in Transform3D presentation) =>
        logical * presentation;

    public static void ThrowIfNonFinite(in Transform3D transform)
    {
        if (!IsFinite(transform.Basis.X) ||
            !IsFinite(transform.Basis.Y) ||
            !IsFinite(transform.Basis.Z) ||
            !IsFinite(transform.Origin))
        {
            throw new ArgumentOutOfRangeException(
                nameof(transform),
                "P3 visual transform basis and origin must be finite.");
        }
    }

    public static AlsP3VisualTransformSnapshot Capture(in Transform3D transform) => new(
        ToNumerics(transform.Basis.X),
        ToNumerics(transform.Basis.Y),
        ToNumerics(transform.Basis.Z),
        ToNumerics(transform.Origin));

    public static ulong ComputeDigest(in Transform3D transform)
    {
        var digest = OffsetBasis;
        Append(ref digest, transform.Basis.X);
        Append(ref digest, transform.Basis.Y);
        Append(ref digest, transform.Basis.Z);
        Append(ref digest, transform.Origin);
        return digest;
    }

    private static bool IsFinite(in Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static NumericsVector3 ToNumerics(in Vector3 value) =>
        new(value.X, value.Y, value.Z);

    private static void Append(ref ulong digest, in Vector3 value)
    {
        Append(ref digest, value.X);
        Append(ref digest, value.Y);
        Append(ref digest, value.Z);
    }

    private static void Append(ref ulong digest, float value)
    {
        var quantized = checked((int)MathF.Round(
            value * 100_000f,
            MidpointRounding.AwayFromZero));
        for (var shift = 0; shift < 32; shift += 8)
        {
            digest ^= (byte)(quantized >> shift);
            digest *= Prime;
        }
    }
}
