using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsSourcePoseKeyTableTests
{
    [Fact]
    public void CopiesEveryKeyAndPreservesRawQuaternionAndScaleBits()
    {
        // Native ALS_N_Pose ik_foot_r is close to an Euler singularity. Its tiny
        // raw quaternion length error must survive source storage unchanged.
        var native = new AlsLocalPose(new(-17.076315f, -5.072189f, 13.465193f),
            new(.703233003616333f, .07391838729381561f, -.7032334804534912f, .0739060640335083f),
            new(-1, -0.0f, 1e-9f));
        var slightlyNonUnit = AlsLocalPose.Identity with { Rotation = new(0, 0, 0, 1.000001f) };
        var changed = native with { Position = new(7, 8, 9), Rotation = -native.Rotation };
        AlsLocalPose[] source = [native, slightlyNonUnit, changed, AlsLocalPose.Identity];
        var expected = source.ToArray();
        var evaluator = Evaluator(1f / 30);
        var table = new AlsSourcePoseKeyTable(evaluator, 30, 1, 1.0 / 30, 2, source);
        Array.Fill(source, default);

        Assert.Same(evaluator, table.Evaluator);
        Assert.Equal(30, table.FrameRateNumerator);
        Assert.Equal(1, table.FrameRateDenominator);
        Assert.Equal(1.0 / 30, table.PlayLength);
        Assert.Equal(2, table.PhysicalBoneCount);
        Assert.Equal(2, table.SampledKeyCount);
        for (var key = 0; key < 2; key++)
        {
            Assert.Equal(2, table.GetKey(key).Length);
            for (var bone = 0; bone < 2; bone++)
                AssertBits(expected[key * 2 + bone], table.GetKey(key)[bone]);
        }
        Assert.NotEqual(Quaternion.Normalize(slightlyNonUnit.Rotation), table.GetKey(0)[1].Rotation);
    }

    [Fact]
    public void SupportsRationalRatesAndMoreThanTheTwoBasePoseKeysWithoutInventingInterpolation()
    {
        const int count = 4;
        const double duration = 3 * 1001.0 / 30000;
        var keys = Enumerable.Range(0, count).Select(i => AlsLocalPose.Identity with { Position = new(i, -i, i * i) }).ToArray();
        var table = new AlsSourcePoseKeyTable(Evaluator((float)duration), 30000, 1001, duration, 1, keys);
        Assert.Equal(count, table.SampledKeyCount);
        Assert.Equal(30000, table.FrameRateNumerator);
        Assert.Equal(1001, table.FrameRateDenominator);
        for (var key = 0; key < count; key++) Assert.Equal(keys[key], table.GetKey(key)[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = table.GetKey(-1); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = table.GetKey(count); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = table.GetKey(int.MaxValue); });
    }

    [Fact]
    public void AcceptsOneZeroDurationKeyAndFloatManifestDurationRounding()
    {
        var single = new AlsSourcePoseKeyTable(Evaluator(0), 30, 1, 0, 1, [AlsLocalPose.Identity]);
        Assert.Equal(1, single.SampledKeyCount);
        Assert.Equal(AlsLocalPose.Identity, single.GetKey(0)[0]);
        var roundedLength = (double)(1f / 30);
        var pair = new AlsSourcePoseKeyTable(Evaluator((float)roundedLength), 30, 1,
            roundedLength, 1, [AlsLocalPose.Identity, AlsLocalPose.Identity]);
        Assert.Equal(roundedLength, pair.PlayLength);
    }

    [Fact]
    public void RejectsInvalidLayoutAndFrameRates()
    {
        AlsLocalPose[] pair = [AlsLocalPose.Identity, AlsLocalPose.Identity];
        Assert.Throws<ArgumentNullException>(() => new AlsSourcePoseKeyTable(null!, 30, 1, 1.0 / 30, 1, pair));
        foreach (var invalid in new[] { 0, -1 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new AlsSourcePoseKeyTable(Evaluator(1f / 30), invalid, 1, 1.0 / 30, 1, pair));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AlsSourcePoseKeyTable(Evaluator(1f / 30), 30, invalid, 1.0 / 30, 1, pair));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AlsSourcePoseKeyTable(Evaluator(1f / 30), 30, 1, 1.0 / 30, invalid, pair));
        }
        Assert.Throws<ArgumentException>(() => new AlsSourcePoseKeyTable(Evaluator(0), 30, 1, 0, 1, []));
        Assert.Throws<ArgumentException>(() => new AlsSourcePoseKeyTable(Evaluator(0), 30, 1, 0, 3, pair));
        Assert.Throws<ArgumentException>(() => new AlsSourcePoseKeyTable(Evaluator(0), 30, 1, 0, 2,
            [AlsLocalPose.Identity, AlsLocalPose.Identity, AlsLocalPose.Identity]));
    }

    [Fact]
    public void RejectsNonfiniteDurationsWrongEndpointsAndEvaluatorLengthMismatch()
    {
        AlsLocalPose[] pair = [AlsLocalPose.Identity, AlsLocalPose.Identity];
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1.0 })
            Assert.Throws<ArgumentOutOfRangeException>(() => new AlsSourcePoseKeyTable(Evaluator(1f / 30), 30, 1, invalid, 1, pair));
        foreach (var wrongEndpoint in new[] { 0, .01, 2.0 / 30 })
            Assert.Throws<ArgumentException>(() => new AlsSourcePoseKeyTable(Evaluator((float)wrongEndpoint), 30, 1, wrongEndpoint, 1, pair));
        foreach (var wrongLength in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f, 2f / 30 })
            Assert.Throws<ArgumentException>(() => new AlsSourcePoseKeyTable(Evaluator(wrongLength), 30, 1, 1.0 / 30, 1, pair));
    }

    [Fact]
    public void ValidatesEveryTrsComponentInLaterKeysAndBones()
    {
        foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        for (var component = 0; component < 10; component++)
        {
            var values = Components(AlsLocalPose.Identity);
            values[component] = invalid;
            var pose = new AlsLocalPose(new(values[0], values[1], values[2]),
                new(values[3], values[4], values[5], values[6]), new(values[7], values[8], values[9]));
            var error = Assert.Throws<ArgumentException>(() => new AlsSourcePoseKeyTable(Evaluator(1f / 30),
                30, 1, 1.0 / 30, 2, [AlsLocalPose.Identity, AlsLocalPose.Identity, AlsLocalPose.Identity, pose]));
            Assert.Contains("key 1, bone 1", error.Message);
        }
        foreach (var rotation in new[] { default(Quaternion), new Quaternion(0, 0, 0, .9f), new Quaternion(0, 0, 0, 1.1f) })
            Assert.Throws<ArgumentException>(() => new AlsSourcePoseKeyTable(Evaluator(0), 30, 1, 0, 1,
                [AlsLocalPose.Identity with { Rotation = rotation }]));
    }

    private static AlsBasePoseEvaluatorDefinition Evaluator(float length) => new(40, 101,
        "source-asset", "/Game/Source.Source", length, 0, true, true,
        "ExplicitTime", "None", "CanBeLeader", "DoNotSync");

    private static float[] Components(AlsLocalPose pose) => [pose.Position.X, pose.Position.Y, pose.Position.Z,
        pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W, pose.Scale.X, pose.Scale.Y, pose.Scale.Z];

    private static void AssertBits(AlsLocalPose expected, AlsLocalPose actual) =>
        Assert.Equal(Components(expected).Select(BitConverter.SingleToInt32Bits), Components(actual).Select(BitConverter.SingleToInt32Bits));
}
