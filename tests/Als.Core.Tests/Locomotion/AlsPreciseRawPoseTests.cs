using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsPreciseRawPoseTests
{
    [Fact]
    public void NativeTransformCasesRetainDoubleRotationsIncludingNegativeAndDegenerateScale()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "P3", "v4_logical_pose_native.json")));
        var cases = fixture.RootElement.GetProperty("transformCases"); Assert.Equal(15, cases.GetArrayLength());
        foreach (var row in cases.EnumerateArray())
        {
            var a = Pose(row.GetProperty("a")); var b = Pose(row.GetProperty("b"));
            AssertPose(Pose(row.GetProperty("compose")), AlsPrecisePose.Compose(a, b));
            AssertPose(Pose(row.GetProperty("relative")), AlsPrecisePose.Relative(a, b));
        }
    }

    [Fact]
    public void MissingTracksUseOriginalReferenceAndVirtualBonesAreBuiltBeforeInterpolation()
    {
        var fixture = Fixture(); var output = new AlsPrecisePose[4];
        fixture.Sampler.Sample(.5 / 30, output);
        Assert.Equal(2.0000000005, output[2].Position.X);
        Assert.True(System.Math.Abs(output[3].Position.X - 1.00000000025) < 1e-9);
        Assert.True(System.Math.Abs(output[3].Position.Y + 1.00000000025) < 1e-9);
        var after = AlsPrecisePose.Relative(output[2], output[1]);
        Assert.True((after.Position - output[3].Position).LengthSquared > .1);
        var saved = output.ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() => fixture.Sampler.Sample(double.NaN, output));
        Assert.Equal(saved, output);
        Assert.Throws<ArgumentException>(() => fixture.Sampler.Sample(0, new AlsPrecisePose[3]));
    }

    [Fact]
    public void IndependentOwnersShareImmutableKeysAndSampleWithoutAllocation()
    {
        var fixture = Fixture(); var samplers = new AlsPreciseRawSequenceSampler[4];
        var outputs = new AlsPrecisePose[4][];
        for (var i = 0; i < 4; i++) { samplers[i] = new(fixture.Data, [-1, 0, 0, 1], fixture.Reference, [new(3, 1, 2)]); outputs[i] = new AlsPrecisePose[4]; }
        Parallel.For(0, 4, owner =>
        {
            for (var i = 0; i < 300; i++) samplers[owner].Sample(i % 29 / 900.0, outputs[owner]);
        });
        for (var i = 1; i < 4; i++) Assert.Equal(outputs[0], outputs[i]);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) samplers[0].Sample(i % 29 / 900.0, outputs[0]);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static (AlsRawAnimationPoseData Data, AlsPreciseRawSequenceSampler Sampler, AlsPrecisePose[] Reference) Fixture()
    {
        AlsLocalPose[] keys = [AlsLocalPose.Identity, AlsLocalPose.Identity, default,
            AlsLocalPose.Identity, AlsLocalPose.Identity with { Rotation = new(0, 0, MathF.Sqrt(.5f), MathF.Sqrt(.5f)) }, default];
        var data = new AlsRawAnimationPoseData(new(0, "precision", "/Game/Precision.Precision", 0), 30, 1, 2, 1.0 / 30,
            AlsRawAnimationInterpolation.Linear, [0, 1, 2, -1], [3], [true, true, false, false], keys, [default, default]);
        AlsPrecisePose[] reference = [AlsPrecisePose.Identity, AlsPrecisePose.Identity,
            AlsPrecisePose.Identity with { Position = new(2.0000000005, 0, 0) }, AlsPrecisePose.Identity];
        var sampler = new AlsPreciseRawSequenceSampler(data, [-1, 0, 0, 1], reference, [new(3, 1, 2)]);
        return (data, sampler, reference);
    }
    private static AlsPrecisePose Pose(JsonElement atom)
    {
        var p = atom.GetProperty("position"); var q = atom.GetProperty("rotation"); var s = atom.GetProperty("scale");
        return new(new(p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble()), new(q[0].GetDouble(), q[1].GetDouble(), q[2].GetDouble(), q[3].GetDouble()),
            new(s[0].GetDouble(), s[1].GetDouble(), s[2].GetDouble()));
    }
    private static void AssertPose(AlsPrecisePose expected, AlsPrecisePose actual)
    {
        var minus = expected.Rotation + actual.Rotation * -1; var plus = expected.Rotation + actual.Rotation;
        Assert.True(System.Math.Min(minus.LengthSquared, plus.LengthSquared) <= 4e-20, $"Rotation: {expected.Rotation} / {actual.Rotation}");
        for (var axis = 0; axis < 3; axis++)
        {
            Assert.True(System.Math.Abs(expected.Position[axis] - actual.Position[axis]) <= 1e-9 + System.Math.Abs(expected.Position[axis]) * 1e-12);
            Assert.True(System.Math.Abs(expected.Scale[axis] - actual.Scale[axis]) <= 1e-9 + System.Math.Abs(expected.Scale[axis]) * 1e-12);
        }
    }
}
