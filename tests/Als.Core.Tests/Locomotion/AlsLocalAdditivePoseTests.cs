using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsLocalAdditivePoseTests
{
    [Fact]
    public void DifferencesAndAccumulationMatchNativeIncludingScaleAndWeightBoundaries()
    {
        using var fixture = ReadFixture();
        var rows = fixture.RootElement.GetProperty("additiveCases");
        Assert.Equal(288, rows.GetArrayLength());
        foreach (var row in rows.EnumerateArray())
        {
            var delta = AlsLocalAdditivePose.Difference(ReadPose(row.GetProperty("target")), ReadPose(row.GetProperty("reference")));
            AssertPose(ReadPose(row.GetProperty("additive")), delta);
            AssertPose(ReadPose(row.GetProperty("output")), AlsLocalAdditivePose.Apply(ReadPose(row.GetProperty("base")), delta,
                row.GetProperty("alpha").GetSingle()));
        }
    }

    [Fact]
    public void DetailCompositionMatchesNativeOrderAndZeroWeightRefPose()
    {
        using var fixture = ReadFixture();
        var rows = fixture.RootElement.GetProperty("detailCases");
        Assert.Equal(64, rows.GetArrayLength());
        var output = new AlsLocalPose[1];
        foreach (var row in rows.EnumerateArray())
        {
            var reference = ReadPose(row.GetProperty("reference"));
            var sources = row.GetProperty("sources").EnumerateArray().Select(ReadPose)
                .Select(source => AlsLocalAdditivePose.Difference(source, reference)).ToArray();
            var weights = row.GetProperty("weights");
            AlsDetailPoseComposer.Compose([ReadPose(row.GetProperty("base"))], sources, [reference],
                new(weights[0].GetSingle(), weights[1].GetSingle(), weights[2].GetSingle(), weights[3].GetSingle()), output);
            AssertPose(ReadPose(row.GetProperty("output")), output[0]);
        }
    }

    [Fact]
    public void ApplyingToReferenceReconstructsTargetAndDoesNotRotateTranslation()
    {
        var reference = new AlsLocalPose(new(2, 3, 5), Quaternion.CreateFromYawPitchRoll(.7f, .4f, -.8f), new(2, 3, 4));
        var target = new AlsLocalPose(new(-2, 5, 8), Quaternion.CreateFromYawPitchRoll(-1, .8f, .3f), new(4, 6, 12));
        var delta = AlsLocalAdditivePose.Difference(target, reference);
        AssertPose(target, AlsLocalAdditivePose.Apply(reference, delta));
        var other = reference with { Rotation = Quaternion.Identity };
        Assert.Equal(target.Position, AlsLocalAdditivePose.Apply(other, delta).Position);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void RejectsInvalidAlpha(float alpha) => Assert.Throws<ArgumentOutOfRangeException>(() =>
        AlsLocalAdditivePose.Apply(AlsLocalPose.Identity, AlsLocalPose.Identity, alpha));

    [Theory]
    [InlineData(-1)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void RejectsInvalidVelocity(float value) => Assert.Throws<ArgumentException>(() =>
        AlsDetailPoseComposer.SampleWeights(new(value, 0, 0, 0)));

    [Fact]
    public void ThresholdPruningDoesNotRenormalizeRemainingContributors()
    {
        var desired = new Vector4(1, .000005f, 0, 0);
        var result = AlsDetailPoseComposer.SampleWeights(desired);
        Assert.True(result.X < 1);
        Assert.Equal(0, result.Y);
        Assert.Equal(Vector4.Zero, AlsDetailPoseComposer.SampleWeights(new(.000005f, 0, 0, 0)));
    }

    [Fact]
    public void InPlaceOutputAndRetriesAreAllocationFree()
    {
        var basis = new[] { AlsLocalPose.Identity, AlsLocalPose.Identity };
        var sources = Enumerable.Range(0, 8).Select(i => new AlsLocalPose(new(i, .1f, 0),
            Quaternion.CreateFromYawPitchRoll(i * .3f, .4f, .2f), new(.01f, .02f, .03f))).ToArray();
        var output = new AlsLocalPose[2];
        AlsDetailPoseComposer.Compose(basis, sources, basis, Vector4.One, output);
        var inPlace = basis.ToArray();
        AlsDetailPoseComposer.Compose(inPlace, sources, basis, Vector4.One, inPlace);
        Assert.Equal(output, inPlace);
        for (var i = 0; i < 64; i++) AlsDetailPoseComposer.Compose(basis, sources, basis, Vector4.One, inPlace);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) AlsDetailPoseComposer.Compose(basis, sources, basis, Vector4.One, inPlace);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(output, inPlace);
    }

    [Fact]
    public void RejectsOverlappingSourceOrShiftedOutputBeforeWriting()
    {
        var buffer = Enumerable.Repeat(AlsLocalPose.Identity, 8).ToArray();
        var original = buffer.ToArray();
        Assert.Throws<ArgumentException>(() => AlsDetailPoseComposer.Compose(buffer.AsSpan(0, 2), buffer,
            buffer.AsSpan(0, 2), Vector4.One, buffer.AsSpan(0, 2)));
        Assert.Equal(original, buffer);
        Assert.Throws<ArgumentException>(() => AlsDetailPoseComposer.Compose(buffer.AsSpan(0, 2), new AlsLocalPose[8],
            new AlsLocalPose[2], Vector4.One, buffer.AsSpan(1, 2)));
        Assert.Equal(original, buffer);
    }

    private static JsonDocument ReadFixture() => JsonDocument.Parse(File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "P3", "v4_local_additive_native.json")));

    private static AlsLocalPose ReadPose(JsonElement row)
    {
        var p = row.GetProperty("position"); var r = row.GetProperty("rotation"); var s = row.GetProperty("scale");
        return new(new(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle()),
            new(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle()),
            new(s[0].GetSingle(), s[1].GetSingle(), s[2].GetSingle()));
    }

    private static void AssertPose(AlsLocalPose expected, AlsLocalPose actual)
    {
        Assert.InRange(Vector3.Distance(expected.Position, actual.Position), 0, .000005f);
        Assert.InRange(MathF.Min((expected.Rotation - actual.Rotation).Length(), (expected.Rotation + actual.Rotation).Length()), 0, .000005f);
        Assert.InRange(Vector3.Distance(expected.Scale, actual.Scale), 0, .000005f);
    }
}
