using System.Numerics;

namespace GodotAls.Core.Locomotion;

public static class AlsStopPlantComposer
{
    public const int SampleCount = 6;

    public static void Compose(ReadOnlySpan<AlsLocalPose> basis, ReadOnlySpan<AlsLocalPose> samples,
        Vector4 velocityWeights, float leftAlternateWeight, float rightAlternateWeight,
        ReadOnlySpan<int> parents, ReadOnlySpan<float> branchWeights,
        Span<AlsLocalPose> layerScratch, Span<Quaternion> rotationScratch, Span<AlsLocalPose> output)
    {
        var bones = basis.Length;
        if (bones == 0 || samples.Length != bones * SampleCount || layerScratch.Length != bones ||
            output.Length != bones || samples.Overlaps(layerScratch) || basis.Overlaps(layerScratch) ||
            layerScratch.Overlaps(output))
            throw new ArgumentException("Invalid Plant pose buffers.");
        ValidateWeights(velocityWeights, leftAlternateWeight, rightAlternateWeight);
        velocityWeights /= velocityWeights.X + velocityWeights.Y + velocityWeights.Z + velocityWeights.W;
        Span<AlsLocalPose> channels = stackalloc AlsLocalPose[4];
        for (var bone = 0; bone < bones; bone++)
        {
            channels[0] = samples[bone];
            channels[1] = samples[bones + bone];
            // BlendListByEnum normalizes each lateral pose before MultiWayBlend sees it.
            channels[2] = Select(samples[bones * 2 + bone], samples[bones * 3 + bone], leftAlternateWeight);
            channels[3] = Select(samples[bones * 4 + bone], samples[bones * 5 + bone], rightAlternateWeight);
            var pose = default(AlsLocalPose);
            var first = true;
            for (var channel = 0; channel < 4; channel++)
            {
                var weight = velocityWeights[channel];
                if (weight <= AlsPoseBlender.WeightThreshold) continue;
                pose = first ? AlsPoseBlender.Scale(channels[channel], weight)
                    : AlsPoseBlender.Accumulate(pose, channels[channel], weight);
                first = false;
            }
            layerScratch[bone] = AlsPoseBlender.Normalize(pose);
        }
        AlsMeshSpacePoseBlend.Blend(basis, layerScratch, parents, branchWeights, rotationScratch, output);
    }

    public static void SampleWeights(Vector4 velocityWeights, float leftAlternateWeight, float rightAlternateWeight,
        Span<float> output)
    {
        if (output.Length != SampleCount) throw new ArgumentException("Expected six Plant source weights.");
        ValidateWeights(velocityWeights, leftAlternateWeight, rightAlternateWeight);
        velocityWeights /= velocityWeights.X + velocityWeights.Y + velocityWeights.Z + velocityWeights.W;
        for (var i = 0; i < 4; i++)
            if (velocityWeights[i] <= AlsPoseBlender.WeightThreshold) velocityWeights[i] = 0;
        leftAlternateWeight = SelectWeight(leftAlternateWeight);
        rightAlternateWeight = SelectWeight(rightAlternateWeight);
        output[0] = velocityWeights.X;
        output[1] = velocityWeights.Y;
        output[2] = velocityWeights.Z * (1 - leftAlternateWeight);
        output[3] = velocityWeights.Z * leftAlternateWeight;
        output[4] = velocityWeights.W * (1 - rightAlternateWeight);
        output[5] = velocityWeights.W * rightAlternateWeight;
    }

    private static AlsLocalPose Select(in AlsLocalPose from, in AlsLocalPose to, float weight) =>
        weight <= AlsPoseBlender.WeightThreshold ? from :
        weight >= 1 - AlsPoseBlender.WeightThreshold ? to : AlsPoseBlender.Blend(from, to, weight);

    private static float SelectWeight(float weight) => weight <= AlsPoseBlender.WeightThreshold ? 0 :
        weight >= 1 - AlsPoseBlender.WeightThreshold ? 1 : weight;

    private static void ValidateWeights(Vector4 velocity, float left, float right)
    {
        var total = velocity.X + velocity.Y + velocity.Z + velocity.W;
        if (!float.IsFinite(total) || total <= AlsPoseBlender.WeightThreshold || velocity.X < 0 || velocity.Y < 0 ||
            velocity.Z < 0 || velocity.W < 0 || !float.IsFinite(left) || !float.IsFinite(right) ||
            left is < 0 or > 1 || right is < 0 or > 1)
            throw new ArgumentException("Plant requires a retained nonzero velocity blend and normalized selectors.");
    }

    public static void Compose(ReadOnlySpan<AlsPrecisePose> basis, ReadOnlySpan<AlsPrecisePose> samples,
        Vector4 velocityWeights, float leftAlternateWeight, float rightAlternateWeight,
        ReadOnlySpan<int> parents, ReadOnlySpan<float> branchWeights,
        Span<AlsPrecisePose> layerScratch, Span<AlsQuaternion> rotationScratch, Span<AlsPrecisePose> output)
    {
        var bones = basis.Length;
        if (bones == 0 || samples.Length != bones * SampleCount || layerScratch.Length != bones ||
            output.Length != bones || samples.Overlaps(layerScratch) || basis.Overlaps(layerScratch) ||
            layerScratch.Overlaps(output))
            throw new ArgumentException("Invalid Plant pose buffers.");
        ValidateWeights(velocityWeights, leftAlternateWeight, rightAlternateWeight);
        velocityWeights /= velocityWeights.X + velocityWeights.Y + velocityWeights.Z + velocityWeights.W;
        Span<AlsPrecisePose> channels = stackalloc AlsPrecisePose[4];
        for (var bone = 0; bone < bones; bone++)
        {
            channels[0] = samples[bone];
            channels[1] = samples[bones + bone];
            // BlendListByEnum normalizes each lateral pose before MultiWayBlend sees it.
            channels[2] = Select(samples[bones * 2 + bone], samples[bones * 3 + bone], leftAlternateWeight);
            channels[3] = Select(samples[bones * 4 + bone], samples[bones * 5 + bone], rightAlternateWeight);
            var pose = default(AlsPrecisePose);
            var first = true;
            for (var channel = 0; channel < 4; channel++)
            {
                var weight = velocityWeights[channel];
                if (weight <= AlsPoseBlender.WeightThreshold) continue;
                pose = first ? AlsPrecisePoseBlender.Scale(channels[channel], weight)
                    : AlsPrecisePoseBlender.Accumulate(pose, channels[channel], weight);
                first = false;
            }
            layerScratch[bone] = AlsPrecisePoseBlender.Normalize(pose);
        }
        AlsMeshSpacePoseBlend.Blend(basis, layerScratch, parents, branchWeights, rotationScratch, output);
    }
    private static AlsPrecisePose Select(in AlsPrecisePose from, in AlsPrecisePose to, float weight) =>
        weight <= AlsPoseBlender.WeightThreshold ? from :
        weight >= 1 - AlsPoseBlender.WeightThreshold ? to : AlsPrecisePoseBlender.Blend(from, to, weight);

}
