using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsMeshSpacePoseBlendTests
{
    [Fact]
    public void MatchesNativePerBoneFilterIncludingThresholdsAndBothRequestedBackends()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "P3", "v4_mesh_space_blend_native.json")));
        var root = fixture.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        var basis = root.GetProperty("base").EnumerateArray().Select(ReadPose).ToArray();
        var layer = root.GetProperty("layer").EnumerateArray().Select(ReadPose).ToArray();
        var parents = root.GetProperty("parents").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        Assert.True(basis.Length >= 68);
        var output = new AlsLocalPose[basis.Length];
        var layered = new AlsLocalPose[basis.Length];
        var sourceIndices = new int[basis.Length];
        var scratch = new Quaternion[basis.Length * 3];
        var cases = root.GetProperty("cases");
        Assert.Equal(80, cases.GetArrayLength());
        foreach (var row in cases.EnumerateArray())
        {
            var weights = row.GetProperty("weights").EnumerateArray().Select(value => value.GetSingle()).ToArray();
            var expected = row.GetProperty("output").EnumerateArray().Select(ReadPose).ToArray();
            AlsMeshSpacePoseBlend.Blend(basis, layer, parents, weights, scratch, output);
            AlsMeshSpacePoseBlend.BlendLayers(basis, layer, parents, sourceIndices, weights, scratch, layered);
            Assert.Equal(output, layered);
            for (var bone = 0; bone < basis.Length; bone++)
            {
                var label = $"ispc={row.GetProperty("ispcRequested")} pattern={row.GetProperty("pattern")} alpha={row.GetProperty("alpha")} bone={bone}";
                Assert.True(Vector3.Distance(expected[bone].Position, output[bone].Position) <= .000003f, label + " position");
                Assert.True(Vector3.Distance(expected[bone].Scale, output[bone].Scale) <= .000003f, label + " scale");
                var error = MathF.Min((expected[bone].Rotation - output[bone].Rotation).Length(),
                    (expected[bone].Rotation + output[bone].Rotation).Length());
                Assert.True(error <= .000006f, label + $" rotation error={error}");
            }
        }
    }

    [Fact]
    public void FullLegBranchKeepsLayerMeshRotationAcrossAnUnblendedPelvis()
    {
        var basis = new[] { Pose(.6f), Pose(-.2f), Pose(.3f), Pose(.1f) };
        var layer = new[] { Pose(-.4f), Pose(.7f), Pose(-.5f), Pose(.8f) };
        int[] parents = [-1, 0, 1, 1];
        float[] weights = [0, 0, 1, 0];
        var output = new AlsLocalPose[4];
        AlsMeshSpacePoseBlend.Blend(basis, layer, parents, weights, new Quaternion[12], output);
        Assert.Equal(basis[0], output[0]);
        AssertRotation(layer[0].Rotation * layer[1].Rotation * layer[2].Rotation,
            output[0].Rotation * output[1].Rotation * output[2].Rotation);
        AssertRotation(basis[3].Rotation, output[3].Rotation);
        Assert.True(MathF.Abs(Quaternion.Dot(layer[2].Rotation, output[2].Rotation)) < .999f);
        Assert.Equal(layer[2].Position, output[2].Position);
    }

    [Fact]
    public void ZeroWeightChildCompensatesForItsBlendedParent()
    {
        var basis = new[] { Pose(.2f), Pose(.3f), Pose(.4f) };
        var layer = new[] { Pose(-.7f), Pose(.8f), Pose(.9f) };
        var output = new AlsLocalPose[3];
        AlsMeshSpacePoseBlend.Blend(basis, layer, [-1, 0, 1], [0, 1, 0], new Quaternion[9], output);
        AssertRotation(basis[0].Rotation * basis[1].Rotation * basis[2].Rotation,
            output[0].Rotation * output[1].Rotation * output[2].Rotation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SupportsExactInPlaceOutput(bool overwriteLayer)
    {
        var basis = new[] { Pose(.2f), Pose(.3f), Pose(.4f) };
        var layer = new[] { Pose(-.7f), Pose(.8f), Pose(.9f) };
        var expected = new AlsLocalPose[3];
        AlsMeshSpacePoseBlend.Blend(basis, layer, [-1, 0, 1], [0, .4f, 1], new Quaternion[9], expected);
        var output = overwriteLayer ? layer : basis;
        AlsMeshSpacePoseBlend.Blend(basis, layer, [-1, 0, 1], [0, .4f, 1], new Quaternion[9], output);
        Assert.Equal(expected, output);
    }

    [Theory]
    [InlineData(-2, 0)]
    [InlineData(0, 0)]
    [InlineData(-1, 1)]
    [InlineData(-1, 2)]
    public void RejectsInvalidHierarchyBeforeWritingOutput(int parent0, int parent1)
    {
        var basis = new[] { Pose(.2f), Pose(.3f) };
        var output = new[] { AlsLocalPose.Identity, AlsLocalPose.Identity };
        Assert.Throws<ArgumentException>(() => AlsMeshSpacePoseBlend.Blend(basis, basis,
            [parent0, parent1], [0, 1], new Quaternion[6], output));
        Assert.All(output, value => Assert.Equal(AlsLocalPose.Identity, value));
    }

    [Fact]
    public void RejectsNonFiniteWeightsAndShiftedAliasing()
    {
        var basis = new[] { Pose(.2f), Pose(.3f), Pose(.4f) };
        Assert.Throws<ArgumentException>(() => AlsMeshSpacePoseBlend.Blend(basis, basis,
            [-1, 0, 1], [0, float.NaN, 1], new Quaternion[9], new AlsLocalPose[3]));
        Assert.Throws<ArgumentException>(() => AlsMeshSpacePoseBlend.Blend(basis.AsSpan(0, 2), basis.AsSpan(0, 2),
            [-1, 0], [0, 1], new Quaternion[6], basis.AsSpan(1, 2)));
    }

    [Fact]
    public void HotPathDoesNotAllocate()
    {
        var basis = new[] { Pose(.2f), Pose(.3f), Pose(.4f) };
        var layer = new[] { Pose(-.7f), Pose(.8f), Pose(.9f) };
        int[] parents = [-1, 0, 1];
        float[] weights = [0, .4f, 1];
        var scratch = new Quaternion[9];
        var output = new AlsLocalPose[3];
        for (var i = 0; i < 64; i++) AlsMeshSpacePoseBlend.Blend(basis, layer, parents, weights, scratch, output);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) AlsMeshSpacePoseBlend.Blend(basis, layer, parents, weights, scratch, output);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void TwoSourcesShareTheSelectedTargetAncestryInsteadOfAccumulatingEachLayerSeparately()
    {
        var basis = new[] { Pose(.1f), Pose(.2f), Pose(.3f), Pose(.4f), Pose(.5f) };
        var left = new[] { Pose(-.3f), Pose(.6f), Pose(-.8f), Pose(.7f), Pose(.9f) };
        var right = new[] { Pose(.9f), Pose(-.4f), Pose(.8f), Pose(-.5f), Pose(.2f) };
        int[] parents = [-1, 0, 1, 1, 3]; int[] indices = [0, 0, 0, 1, 1];
        float[] weights = [0, 0, 1, 1, 0];
        var output = new AlsLocalPose[5];
        AlsMeshSpacePoseBlend.BlendLayers(basis, left.Concat(right).ToArray(), parents, indices, weights, new Quaternion[15], output);
        var resultLeft = output[0].Rotation * output[1].Rotation * output[2].Rotation;
        var resultRight = output[0].Rotation * output[1].Rotation * output[3].Rotation;
        AssertRotation(left[0].Rotation * left[1].Rotation * left[2].Rotation, resultLeft);
        AssertRotation(left[0].Rotation * left[1].Rotation * right[3].Rotation, resultRight);
        AssertRotation(basis[0].Rotation * basis[1].Rotation * basis[3].Rotation * basis[4].Rotation,
            resultRight * output[4].Rotation);
        Assert.True(MathF.Abs(Quaternion.Dot(right[0].Rotation * right[1].Rotation * right[3].Rotation, resultRight)) < .99f);
        Assert.Equal(left[2].Position, output[2].Position); Assert.Equal(right[3].Position, output[3].Position);
        var serial = new AlsLocalPose[5]; var serialResult = new AlsLocalPose[5];
        AlsMeshSpacePoseBlend.Blend(basis, left, parents, [0, 0, 1, 0, 0], new Quaternion[15], serial);
        AlsMeshSpacePoseBlend.Blend(serial, right, parents, [0, 0, 0, 1, 0], new Quaternion[15], serialResult);
        Assert.True(MathF.Abs(Quaternion.Dot(serialResult[3].Rotation, output[3].Rotation)) < .99f);
    }

    [Theory]
    [InlineData(-1)] [InlineData(2)] [InlineData(int.MaxValue)]
    public void RejectsInvalidSourceIndexEvenOnZeroWeightBonesBeforeWriting(int invalid)
    {
        var basis = new[] { Pose(.2f), Pose(.3f) }; var layers = basis.Concat(basis).ToArray();
        var output = new[] { AlsLocalPose.Identity, AlsLocalPose.Identity };
        Assert.Throws<ArgumentException>(() => AlsMeshSpacePoseBlend.BlendLayers(basis, layers,
            [-1, 0], [0, invalid], [1, 0], new Quaternion[6], output));
        Assert.All(output, p => Assert.Equal(AlsLocalPose.Identity, p));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void LayeredOutputCanOverwriteBaseOrAnEntireLayer(int destination)
    {
        var basis = new[] { Pose(.2f), Pose(.3f), Pose(.4f) };
        var layers = new[] { Pose(-.7f), Pose(.8f), Pose(.9f), Pose(.1f), Pose(-.2f), Pose(-.3f) };
        var expected = new AlsLocalPose[3];
        AlsMeshSpacePoseBlend.BlendLayers(basis, layers, [-1, 0, 1], [0, 1, 0], [0, .4f, 1], new Quaternion[9], expected);
        var output = destination == 0 ? basis.AsSpan() : layers.AsSpan((destination - 1) * 3, 3);
        AlsMeshSpacePoseBlend.BlendLayers(basis, layers, [-1, 0, 1], [0, 1, 0], [0, .4f, 1], new Quaternion[9], output);
        Assert.True(expected.AsSpan().SequenceEqual(output));
    }

    [Fact]
    public void RejectsMalformedLayeredBuffersAndShiftedAliasing()
    {
        var basis = new[] { Pose(.2f), Pose(.3f) }; var layers = new[] { Pose(.1f), Pose(.2f), Pose(.3f), Pose(.4f) };
        Assert.Throws<ArgumentException>(() => AlsMeshSpacePoseBlend.BlendLayers(basis, layers,
            [-1, 0], [], [0, 1], new Quaternion[6], new AlsLocalPose[2]));
        Assert.Throws<ArgumentException>(() => AlsMeshSpacePoseBlend.BlendLayers(basis, layers.AsSpan(0, 3),
            [-1, 0], [0, 1], [0, 1], new Quaternion[6], new AlsLocalPose[2]));
        Assert.Throws<ArgumentException>(() => AlsMeshSpacePoseBlend.BlendLayers(basis, layers,
            [-1, 0], [0, 1], [0, 1], new Quaternion[6], layers.AsSpan(1, 2)));
    }

    [Fact]
    public void LayeredHotPathDoesNotAllocate()
    {
        var basis = new[] { Pose(.2f), Pose(.3f), Pose(.4f) };
        var layers = new[] { Pose(-.7f), Pose(.8f), Pose(.9f), Pose(.1f), Pose(-.2f), Pose(-.3f) };
        int[] parents = [-1, 0, 1]; int[] indices = [0, 1, 0]; float[] weights = [0, .4f, 1];
        var scratch = new Quaternion[9]; var output = new AlsLocalPose[3];
        for (var i = 0; i < 200; i++) AlsMeshSpacePoseBlend.BlendLayers(basis, layers, parents, indices, weights, scratch, output);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2000; i++) AlsMeshSpacePoseBlend.BlendLayers(basis, layers, parents, indices, weights, scratch, output);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static AlsLocalPose Pose(float angle) => new(new Vector3(angle, angle * 2, 0),
        Quaternion.CreateFromYawPitchRoll(angle, angle * .4f, angle * .7f), Vector3.One);

    private static void AssertRotation(Quaternion expected, Quaternion actual) =>
        Assert.True(MathF.Abs(Quaternion.Dot(Quaternion.Normalize(expected), Quaternion.Normalize(actual))) > .999999f);

    private static AlsLocalPose ReadPose(JsonElement row)
    {
        var p = row.GetProperty("position");
        var r = row.GetProperty("rotation");
        var s = row.GetProperty("scale");
        return new(new Vector3(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle()),
            new Quaternion(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle()),
            new Vector3(s[0].GetSingle(), s[1].GetSingle(), s[2].GetSingle()));
    }
}
