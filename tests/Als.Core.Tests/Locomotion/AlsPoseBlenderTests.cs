using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsPoseBlenderTests
{
    [Fact]
    public void OrderedAccumulationAndRawTransitionStepsMatchNativeTransforms()
    {
        using var fixture = ReadFixture();
        var poses = fixture.RootElement.GetProperty("poses").EnumerateArray().Select(ReadPose).ToArray();
        var cases = fixture.RootElement.GetProperty("cases");
        Assert.Equal(64, cases.GetArrayLength());
        foreach (var row in cases.EnumerateArray())
        {
            var weights = row.GetProperty("weights").EnumerateArray().Select(value => value.GetSingle()).ToArray();
            var mixed = AlsPoseBlender.Weighted(poses, weights);
            AssertPose(ReadPose(row.GetProperty("weighted")), mixed);
            var body = mixed;
            var leg = mixed;
            foreach (var step in row.GetProperty("steps").EnumerateArray())
            {
                var target = poses[step.GetProperty("target").GetInt32()];
                var alpha = step.GetProperty("alpha").GetSingle();
                body = AlsPoseBlender.BlendRaw(body, target, alpha);
                // This historical probe manually computes LegAlpha; it tests FTransform arithmetic,
                // not UBlendProfile. Preserve its input formula without using it in production.
                var probeAlpha = alpha * 2 / (alpha * 2 + (1 - alpha) / 2);
                leg = AlsPoseBlender.BlendRaw(leg, target, probeAlpha);
                AssertPose(ReadPose(step.GetProperty("bodyRaw")), body);
                AssertPose(ReadPose(step.GetProperty("legRaw")), leg);
            }
            AssertPose(ReadPose(row.GetProperty("body")), AlsPoseBlender.Normalize(body));
            AssertPose(ReadPose(row.GetProperty("leg")), AlsPoseBlender.Normalize(leg));
        }
    }

    [Fact]
    public void SortedGridSamplesThresholdsAndTiesMatchAllSixNativeAssets()
    {
        using var fixture = ReadFixture();
        var rows = fixture.RootElement.GetProperty("grids");
        Assert.Equal(384, rows.GetArrayLength());
        Span<int> order = stackalloc int[4];
        foreach (var row in rows.EnumerateArray())
        {
            var count = AlsWalkRunBlendSpace.SampleEvaluation(new Vector2(row.GetProperty("x").GetSingle(),
                row.GetProperty("y").GetSingle()), order, out var weights);
            var expectedOrder = row.GetProperty("order").EnumerateArray().Select(value => value.GetInt32()).ToArray();
            Assert.True(expectedOrder.AsSpan().SequenceEqual(order[..count]), row.ToString());
            for (var i = 0; i < count; i++)
                Assert.InRange(MathF.Abs(weights[order[i]] - row.GetProperty("weights")[i].GetSingle()), 0, 0.000002f);
        }
    }

    [Fact]
    public void NormalizingEachTransitionWouldChangeTheNativeResult()
    {
        using var fixture = ReadFixture();
        var poses = fixture.RootElement.GetProperty("poses").EnumerateArray().Select(ReadPose).ToArray();
        var foundDifference = false;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var pose = ReadPose(row.GetProperty("weighted"));
            foreach (var step in row.GetProperty("steps").EnumerateArray())
                pose = AlsPoseBlender.Blend(pose, poses[step.GetProperty("target").GetInt32()], step.GetProperty("alpha").GetSingle());
            foundDifference |= MathF.Abs(Quaternion.Dot(pose.Rotation, ReadPose(row.GetProperty("body")).Rotation)) < .999f;
        }
        Assert.True(foundDifference);
    }

    [Fact]
    public void PoseCompositionSeparatesStateNormalizationAndPerBoneTransitions()
    {
        var clips = new AlsLocalPose[26 * 2];
        for (var clip = 0; clip < 26; clip++)
        for (var bone = 0; bone < 2; bone++)
            clips[clip * 2 + bone] = new AlsLocalPose(new Vector3(clip, 0, 0),
                Quaternion.CreateFromYawPitchRoll(clip * .23f, clip * .11f, clip * .07f), Vector3.One);
        var stack = AlsTransitionStack.Initialize((int)AlsCycleDirection.LeftForward);
        stack = AlsTransitionStack.Start(stack, (int)AlsCycleDirection.RightForward, 1, AlsTransitionBlend.Linear);
        stack = AlsTransitionStack.Advance(stack, .5f);
        var scratch = new AlsLocalPose[24];
        var output = new AlsLocalPose[2];
        AlsStandingCyclePose.Compose(clips, [false, true], new Vector4(0, 1, 0, 0), [1], new Vector4(0, 0, 1, 0),
            stack, 1, 0, scratch, output);
        AssertPose(AlsPoseBlender.Blend(clips[10 * 2], clips[14 * 2], .5f), output[0]);
        AssertPose(AlsPoseBlender.Blend(clips[10 * 2], clips[14 * 2], .8f), output[1]);
    }

    [Fact]
    public void InterruptedComposerPreservesTheUnnormalizedIntermediatePose()
    {
        var clips = new AlsLocalPose[26];
        for (var i = 0; i < clips.Length; i++)
            clips[i] = new AlsLocalPose(new Vector3(i, 0, 0),
                Quaternion.CreateFromYawPitchRoll(i * .31f, i * .19f, i * -.23f), Vector3.One);
        var stack = AlsTransitionStack.Initialize((int)AlsCycleDirection.LeftForward);
        stack = AlsTransitionStack.Start(stack, (int)AlsCycleDirection.RightForward, 1, AlsTransitionBlend.Linear);
        stack = AlsTransitionStack.Advance(stack, .2f);
        stack = AlsTransitionStack.Start(stack, (int)AlsCycleDirection.Forward, 1, AlsTransitionBlend.Linear);
        stack = AlsTransitionStack.Advance(stack, .15f);
        Assert.Equal(2, stack.Count);
        var output = new AlsLocalPose[1];
        AlsStandingCyclePose.Compose(clips, [false], new Vector4(0, 1, 0, 0), [1], new Vector4(0, 0, 1, 0),
            stack, 1, 0, new AlsLocalPose[12], output);
        var first = AlsPoseBlender.BlendRaw(clips[10], clips[14], stack.GetTransition(0).Alpha);
        var expected = AlsPoseBlender.Blend(first, clips[10], stack.GetTransition(1).Alpha);
        AssertPose(expected, output[0]);
        var normalizedEveryLayer = AlsPoseBlender.Blend(AlsPoseBlender.Normalize(first), clips[10], stack.GetTransition(1).Alpha);
        Assert.True(MathF.Abs(Quaternion.Dot(normalizedEveryLayer.Rotation, output[0].Rotation)) < .99999f);
    }

    [Fact]
    public void SprintIsMixedInsideForwardBeforeTheDirectionalPose()
    {
        var clips = Enumerable.Range(0, 26).Select(i => new AlsLocalPose(new Vector3(i, 0, 0),
            Quaternion.CreateFromYawPitchRoll(i * .2f, i * .13f, i * .07f), Vector3.One)).ToArray();
        var output = new AlsLocalPose[1]; var scratch = new AlsLocalPose[12];
        var stack = AlsTransitionStack.Initialize((int)AlsCycleDirection.LeftForward);
        AlsStandingCyclePose.Compose(clips, [false], new(0,1,0,0), [1], new(0,0,1,0), stack, 1, 1, scratch, output);
        AssertPose(clips[10], output[0]);
        AlsStandingCyclePose.Compose(clips, [false], new(0,1,0,0), [1], new(.5f,0,.5f,0), stack, 1, .6f, scratch, output, .4f);
        var forward = AlsPoseBlender.Blend(AlsPoseBlender.Blend(clips[2], clips[25], .6f), clips[2], .4f);
        AssertPose(AlsPoseBlender.Blend(forward, clips[10], .5f), output[0]);
        Assert.True(MathF.Abs(Quaternion.Dot(forward.Rotation,
            AlsPoseBlender.Blend(clips[2], clips[25], .36f).Rotation)) < .999999f);
    }

    [Fact]
    public void ProfiledBoneRetainsNativeMinimumContributionAtZeroAlpha()
    {
        var clips = Enumerable.Repeat(AlsLocalPose.Identity, 52).ToArray();
        clips[14 * 2] = clips[14 * 2 + 1] = AlsLocalPose.Identity with { Position = new(1000, 0, 0) };
        var stack = AlsTransitionStack.Start(AlsTransitionStack.Initialize((int)AlsCycleDirection.LeftForward),
            (int)AlsCycleDirection.RightForward, 1, AlsTransitionBlend.Linear);
        var output = new AlsLocalPose[2];
        AlsStandingCyclePose.Compose(clips, [false, true], new(0, 1, 0, 0), [1], new(0, 0, 1, 0),
            stack, 1, 0, new AlsLocalPose[24], output);
        Assert.Equal(0, output[0].Position.X);
        Assert.InRange(output[1].Position.X, .0199995f, .0199997f);
        Assert.InRange(AlsTransitionStack.Weight(stack, (int)AlsCycleDirection.RightForward, 2), .000019999f, .000020001f);
    }

    [Fact]
    public void SprintMaskKeepsPoseContributionSeparateFromCachedSourceUpdate()
    {
        var blend = new AlsBinaryBlendState(true, 1, default, default, .4f, .6f);
        var weights = AlsStandingSprint.Weights(blend, .5f);
        Assert.Equal(.3f, weights.PoseSprint, 6);
        Assert.Equal(.5f, weights.ForwardUpdate, 6);
        Assert.Equal(.3f, weights.SprintUpdate, 6);
        Assert.True(weights.ForwardActive); Assert.True(weights.SprintActive);
        Assert.Equal(new(0,1,0,true,false), AlsStandingSprint.Weights(blend, 1));
        Assert.Equal(new(.6f,.4f,.6f,false,true), AlsStandingSprint.Weights(blend, 0));
    }

    private static JsonDocument ReadFixture() => JsonDocument.Parse(File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "P3", "v4_pose_blend_native.json")));

    private static AlsLocalPose ReadPose(JsonElement row)
    {
        var p = row.GetProperty("position");
        var r = row.GetProperty("rotation");
        var s = row.GetProperty("scale");
        return new(new Vector3(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle()),
            new Quaternion(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle()),
            new Vector3(s[0].GetSingle(), s[1].GetSingle(), s[2].GetSingle()));
    }

    private static void AssertPose(AlsLocalPose expected, AlsLocalPose actual)
    {
        Assert.InRange(Vector3.Distance(expected.Position, actual.Position), 0, .000003f);
        Assert.InRange((expected.Rotation - actual.Rotation).Length(), 0, .000003f);
        Assert.InRange(Vector3.Distance(expected.Scale, actual.Scale), 0, .000003f);
    }
}
