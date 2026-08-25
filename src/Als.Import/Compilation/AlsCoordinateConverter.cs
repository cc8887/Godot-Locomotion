using System.Numerics;

namespace GodotAls.Import.Compilation;

public static class AlsCoordinateConverter
{
    private static readonly Matrix4x4 SourceToTarget = new(
        0f, 0f, -1f, 0f,
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 0f, 1f);

    private static readonly Matrix4x4 TargetToSource = CreateInverse();

    public static Vector3 PositionCentimetersToMeters(Vector3 value) => Direction(value) * 0.01f;

    public static Vector3 Direction(Vector3 value) => new(value.Y, value.Z, -value.X);

    public static Vector3 Scale(Vector3 value) => new(value.Y, value.Z, value.X);

    public static Quaternion Rotation(Quaternion value)
    {
        if (value.LengthSquared() <= float.Epsilon)
        {
            return Quaternion.Identity;
        }

        var normalized = Quaternion.Normalize(value);
        var sourceRotation = Matrix4x4.CreateFromQuaternion(normalized);
        var targetRotation = TargetToSource * sourceRotation * SourceToTarget;
        var result = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(targetRotation));
        return result.W < 0f
            ? new Quaternion(-result.X, -result.Y, -result.Z, -result.W)
            : result;
    }

    private static Matrix4x4 CreateInverse()
    {
        if (!Matrix4x4.Invert(SourceToTarget, out var inverse))
        {
            throw new InvalidOperationException("ALS coordinate conversion basis is not invertible.");
        }

        return inverse;
    }
}
