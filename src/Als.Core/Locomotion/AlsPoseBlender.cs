using System.Numerics;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsLocalPose(Vector3 Position, Quaternion Rotation, Vector3 Scale)
{
    public static AlsLocalPose Identity => new(Vector3.Zero, Quaternion.Identity, Vector3.One);
}

public static class AlsPoseBlender
{
    public const float WeightThreshold = 0.00001f;

    public static AlsLocalPose Scale(in AlsLocalPose pose, float weight) =>
        new(pose.Position * weight, pose.Rotation * weight, pose.Scale * weight);

    // UE FTransform::AccumulateWithShortestRotation does not normalize its accumulator.
    public static AlsLocalPose Accumulate(in AlsLocalPose accumulator, in AlsLocalPose source, float weight)
    {
        var rotation = source.Rotation * weight;
        if (Quaternion.Dot(accumulator.Rotation, rotation) < 0) rotation = -rotation;
        return new(accumulator.Position + source.Position * weight, accumulator.Rotation + rotation,
            accumulator.Scale + source.Scale * weight);
    }

    public static AlsLocalPose Normalize(in AlsLocalPose pose) => pose with
    {
        Rotation = pose.Rotation.LengthSquared() >= 1e-8f ? Quaternion.Normalize(pose.Rotation) : Quaternion.Identity,
    };

    public static AlsLocalPose BlendRaw(in AlsLocalPose from, in AlsLocalPose to, float alpha) =>
        Accumulate(Scale(from, 1 - alpha), to, alpha);

    public static AlsLocalPose Blend(in AlsLocalPose from, in AlsLocalPose to, float alpha) =>
        Normalize(BlendRaw(from, to, alpha));

    public static AlsLocalPose Weighted(ReadOnlySpan<AlsLocalPose> poses, ReadOnlySpan<float> weights)
    {
        if (poses.Length == 0 || poses.Length != weights.Length) throw new ArgumentException("Pose/weight lengths differ.");
        var accumulated = Scale(poses[0], weights[0]);
        for (var i = 1; i < poses.Length; i++) accumulated = Accumulate(accumulated, poses[i], weights[i]);
        return Normalize(accumulated);
    }
}
