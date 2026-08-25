using System.Security.Cryptography;
using System.Text;
using Godot;

namespace GodotAls.Animation;

public readonly record struct AlsBonePose(Vector3 Position, Quaternion Rotation, Vector3 Scale);

public static class AlsPoseDigest
{
    private const float QuantizationScale = 100_000f;

    public static AlsBonePose[] CapturePoses(Skeleton3D skeleton, IReadOnlyList<string> boneNames)
    {
        var poses = new AlsBonePose[boneNames.Count];
        for (var index = 0; index < boneNames.Count; index++)
        {
            var boneIndex = skeleton.FindBone(boneNames[index]);
            if (boneIndex < 0)
            {
                throw new InvalidOperationException($"Pose digest bone is missing: {boneNames[index]}");
            }

            var rotation = skeleton.GetBonePoseRotation(boneIndex).Normalized();
            if (rotation.W < 0f)
            {
                rotation = new Quaternion(-rotation.X, -rotation.Y, -rotation.Z, -rotation.W);
            }
            poses[index] = new AlsBonePose(
                skeleton.GetBonePosePosition(boneIndex),
                rotation,
                skeleton.GetBonePoseScale(boneIndex));
        }
        return poses;
    }

    public static string Compute(
        Skeleton3D skeleton,
        int frame,
        IReadOnlyList<string> boneNames)
    {
        var poses = CapturePoses(skeleton, boneNames);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), true))
        {
            writer.Write(frame);
            for (var index = 0; index < poses.Length; index++)
            {
                writer.Write(boneNames[index]);
                Write(writer, poses[index].Position);
                Write(writer, poses[index].Rotation);
                Write(writer, poses[index].Scale);
            }
        }
        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }

    public static bool HasChanged(IReadOnlyList<AlsBonePose> initial, IReadOnlyList<AlsBonePose> current)
    {
        if (initial.Count != current.Count)
        {
            throw new ArgumentException("Pose arrays must have the same length.");
        }

        for (var index = 0; index < initial.Count; index++)
        {
            if (!ApproximatelyEqual(initial[index].Position, current[index].Position) ||
                !ApproximatelyEqual(initial[index].Rotation, current[index].Rotation) ||
                !ApproximatelyEqual(initial[index].Scale, current[index].Scale))
            {
                return true;
            }
        }
        return false;
    }

    private static void Write(BinaryWriter writer, Vector3 value)
    {
        writer.Write(Quantize(value.X));
        writer.Write(Quantize(value.Y));
        writer.Write(Quantize(value.Z));
    }

    private static void Write(BinaryWriter writer, Quaternion value)
    {
        writer.Write(Quantize(value.X));
        writer.Write(Quantize(value.Y));
        writer.Write(Quantize(value.Z));
        writer.Write(Quantize(value.W));
    }

    private static int Quantize(float value) =>
        checked((int)MathF.Round(value * QuantizationScale, MidpointRounding.AwayFromZero));

    private static bool ApproximatelyEqual(Vector3 left, Vector3 right) =>
        left.DistanceSquaredTo(right) <= 1e-10f;

    private static bool ApproximatelyEqual(Quaternion left, Quaternion right) =>
        1f - MathF.Abs(left.Dot(right)) <= 1e-7f;
}
